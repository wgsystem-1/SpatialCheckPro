using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Collections.Concurrent;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 조건부 규칙 엔진 구현
    /// ConditionalRule 기반 복합 조건 처리 및 동적 규칙 적용을 담당
    /// </summary>
    public class ConditionalRuleEngine : IConditionalRuleEngine
    {
        private readonly IExpressionEngine _expressionEngine;
        private readonly IGdalDataReader _gdalDataReader;
        private readonly ILogger<ConditionalRuleEngine> _logger;
        
        // 규칙 캐시 (성능 최적화)
        private readonly ConcurrentDictionary<string, ConditionalRule> _ruleCache;
        
        // 파싱된 표현식 캐시
        private readonly ConcurrentDictionary<string, ExpressionParseResult> _conditionCache;
        private readonly ConcurrentDictionary<string, ExpressionParseResult> _validationCache;
        
        // 규칙 의존성 그래프
        private readonly Dictionary<string, List<string>> _dependencyGraph;
        
        // 실행 통계
        private readonly Dictionary<string, RuleExecutionStatistics> _executionStats;

        public ConditionalRuleEngine(
            IExpressionEngine expressionEngine,
            IGdalDataReader gdalDataReader,
            ILogger<ConditionalRuleEngine> logger)
        {
            _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
            _gdalDataReader = gdalDataReader ?? throw new ArgumentNullException(nameof(gdalDataReader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _ruleCache = new ConcurrentDictionary<string, ConditionalRule>();
            _conditionCache = new ConcurrentDictionary<string, ExpressionParseResult>();
            _validationCache = new ConcurrentDictionary<string, ExpressionParseResult>();
            _dependencyGraph = new Dictionary<string, List<string>>();
            _executionStats = new Dictionary<string, RuleExecutionStatistics>();
        }

        /// <summary>
        /// 조건부 규칙을 검증합니다
        /// </summary>
        public async Task<List<AttributeRelationError>> ValidateConditionalRuleAsync(
            string gdbPath,
            string tableName,
            ConditionalRule rule,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var errors = new List<AttributeRelationError>();

            try
            {
                _logger.LogInformation("조건부 규칙 검증 시작: {RuleId} - {TableName}", rule.RuleId, tableName);

                // 1. 규칙 유효성 검증
                var validationErrors = rule.Validate();
                if (validationErrors.Any())
                {
                    _logger.LogError("규칙 유효성 검증 실패: {RuleId}", rule.RuleId);
                    throw new ArgumentException($"규칙이 유효하지 않습니다: {string.Join(", ", validationErrors.Select(e => e.Message))}");
                }

                // 2. 규칙이 비활성화된 경우 건너뛰기
                if (!rule.IsEnabled)
                {
                    _logger.LogDebug("규칙이 비활성화됨: {RuleId}", rule.RuleId);
                    return errors;
                }

                // 3. 의존 규칙 확인
                if (!await CheckDependentRulesAsync(rule, gdbPath, tableName, cancellationToken))
                {
                    _logger.LogWarning("의존 규칙 조건이 만족되지 않음: {RuleId}", rule.RuleId);
                    return errors;
                }

                // 4. 테이블 스키마 정보 추출
                var tableSchema = await ExtractTableSchemaAsync(gdbPath, tableName);
                if (tableSchema == null || !tableSchema.Any())
                {
                    _logger.LogError("테이블 스키마를 가져올 수 없음: {TableName}", tableName);
                    throw new InvalidOperationException($"테이블 스키마를 가져올 수 없습니다: {tableName}");
                }

                // 5. 조건 표현식 파싱 및 캐싱
                var conditionParseResult = await GetOrParseConditionAsync(rule.Condition, tableSchema);
                if (!conditionParseResult.IsValid)
                {
                    _logger.LogError("조건 표현식 파싱 실패: {Condition}", rule.Condition);
                    throw new ArgumentException($"조건 표현식이 유효하지 않습니다: {string.Join(", ", conditionParseResult.Errors)}");
                }

                // 6. 검증 표현식 파싱 및 캐싱
                var validationParseResult = await GetOrParseValidationAsync(rule.ValidationExpression, tableSchema);
                if (!validationParseResult.IsValid)
                {
                    _logger.LogError("검증 표현식 파싱 실패: {ValidationExpression}", rule.ValidationExpression);
                    throw new ArgumentException($"검증 표현식이 유효하지 않습니다: {string.Join(", ", validationParseResult.Errors)}");
                }

                // 7. 피처 스트리밍 처리로 메모리 효율성 확보
                var processedCount = 0;
                var errorCount = 0;

                await foreach (var feature in _gdalDataReader.GetFeaturesStreamAsync(gdbPath, tableName, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // 조건 평가
                        var conditionResult = await EvaluateConditionAsync(conditionParseResult, feature);
                        if (!conditionResult.IsSuccess)
                        {
                            _logger.LogWarning("조건 평가 실패: ObjectId={ObjectId}, Error={Error}", 
                                GetObjectId(feature), conditionResult.ErrorMessage);
                            continue;
                        }

                        // 조건이 만족되는 경우에만 검증 수행
                        if (conditionResult.BooleanValue)
                        {
                            var validationResult = await EvaluateValidationAsync(validationParseResult, feature);
                            if (!validationResult.IsSuccess)
                            {
                                _logger.LogWarning("검증 평가 실패: ObjectId={ObjectId}, Error={Error}", 
                                    GetObjectId(feature), validationResult.ErrorMessage);
                                continue;
                            }

                            // 검증 실패 시 오류 생성
                            if (!validationResult.BooleanValue)
                            {
                                var error = new AttributeRelationError
                                {
                                    ObjectId = GetObjectId(feature),
                                    TableName = tableName,
                                    FieldName = "Unknown",
                                    RuleName = rule.RuleId,
                                    Severity = rule.Severity,
                                    DetectedAt = DateTime.UtcNow
                                };
                                errors.Add(error);
                                errorCount++;

                                _logger.LogDebug("조건부 규칙 위반 발견: ObjectId={ObjectId}, Rule={RuleId}", 
                                    GetObjectId(feature), rule.RuleId);
                            }
                        }

                        processedCount++;

                        // 진행률 로깅 (1000개마다)
                        if (processedCount % 1000 == 0)
                        {
                            _logger.LogDebug("조건부 규칙 검증 진행: {ProcessedCount}개 처리, {ErrorCount}개 오류 발견", 
                                processedCount, errorCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "피처 처리 중 오류 발생: ObjectId={ObjectId}", GetObjectId(feature));
                        // 개별 피처 오류는 전체 처리를 중단하지 않음
                        continue;
                    }
                }

                // 8. 실행 통계 업데이트
                UpdateExecutionStatistics(rule.RuleId, startTime, processedCount, errorCount);

                _logger.LogInformation("조건부 규칙 검증 완료: {RuleId} - 처리={ProcessedCount}, 오류={ErrorCount}, 시간={ElapsedMs}ms", 
                    rule.RuleId, processedCount, errorCount, (DateTime.UtcNow - startTime).TotalMilliseconds);

                return errors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "조건부 규칙 검증 중 오류 발생: {RuleId}", rule.RuleId);
                throw;
            }
        }

        /// <summary>
        /// 여러 조건부 규칙을 병렬로 검증합니다
        /// </summary>
        public async Task<List<AttributeRelationError>> ValidateMultipleRulesAsync(
            string gdbPath,
            string tableName,
            List<ConditionalRule> rules,
            CancellationToken cancellationToken = default)
        {
            if (!rules?.Any() == true)
            {
                return new List<AttributeRelationError>();
            }

            _logger.LogInformation("다중 조건부 규칙 검증 시작: {RuleCount}개 규칙, 테이블={TableName}", 
                rules.Count, tableName);

            try
            {
                // 1. 규칙을 우선순위별로 정렬
                var sortedRules = rules
                    .Where(r => r.IsEnabled)
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.RuleId)
                    .ToList();

                // 2. 의존성 그래프 구축
                BuildDependencyGraph(sortedRules);

                // 3. 의존성 순서에 따라 규칙 실행
                var allErrors = new List<AttributeRelationError>();
                var executedRules = new HashSet<string>();

                foreach (var rule in sortedRules)
                {
                    // 의존 규칙이 모두 실행되었는지 확인
                    if (rule.DependentRuleIds.Any(depId => !executedRules.Contains(depId)))
                    {
                        _logger.LogWarning("의존 규칙이 아직 실행되지 않음: {RuleId}", rule.RuleId);
                        continue;
                    }

                    var ruleErrors = await ValidateConditionalRuleAsync(gdbPath, tableName, rule, cancellationToken);
                    allErrors.AddRange(ruleErrors);
                    executedRules.Add(rule.RuleId);

                    _logger.LogDebug("규칙 실행 완료: {RuleId}, 오류 개수: {ErrorCount}", 
                        rule.RuleId, ruleErrors.Count);
                }

                _logger.LogInformation("다중 조건부 규칙 검증 완료: 총 {TotalErrors}개 오류 발견", allErrors.Count);
                return allErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "다중 조건부 규칙 검증 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 런타임에 규칙을 동적으로 추가합니다
        /// </summary>
#pragma warning disable CS1998 // 이 비동기 메서드에는 'await' 연산자가 없음
        public async Task<bool> AddRuleAsync(ConditionalRule rule)
#pragma warning restore CS1998
        {
            try
            {
                _logger.LogInformation("동적 규칙 추가: {RuleId}", rule.RuleId);

                // 규칙 유효성 검증
                var validationErrors = rule.Validate();
                if (validationErrors.Any())
                {
                    _logger.LogError("규칙 유효성 검증 실패: {RuleId}, 오류: {Errors}", 
                        rule.RuleId, string.Join(", ", validationErrors.Select(e => e.Message)));
                    return false;
                }

                // 캐시에 추가
                _ruleCache.AddOrUpdate(rule.RuleId, rule, (key, oldValue) => rule);

                // 의존성 그래프 업데이트
                if (rule.DependentRuleIds.Any())
                {
                    _dependencyGraph[rule.RuleId] = rule.DependentRuleIds.ToList();
                }

                _logger.LogInformation("동적 규칙 추가 완료: {RuleId}", rule.RuleId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "동적 규칙 추가 중 오류 발생: {RuleId}", rule.RuleId);
                return false;
            }
        }

        /// <summary>
        /// 런타임에 규칙을 동적으로 제거합니다
        /// </summary>
#pragma warning disable CS1998 // 이 비동기 메서드에는 'await' 연산자가 없음
        public async Task<bool> RemoveRuleAsync(string ruleId)
#pragma warning restore CS1998
        {
            try
            {
                _logger.LogInformation("동적 규칙 제거: {RuleId}", ruleId);

                // 캐시에서 제거
                _ruleCache.TryRemove(ruleId, out _);

                // 파싱 캐시에서 관련 항목 제거
                var keysToRemove = _conditionCache.Keys.Where(k => k.Contains(ruleId)).ToList();
                foreach (var key in keysToRemove)
                {
                    _conditionCache.TryRemove(key, out _);
                    _validationCache.TryRemove(key, out _);
                }

                // 의존성 그래프에서 제거
                _dependencyGraph.Remove(ruleId);

                // 실행 통계에서 제거
                _executionStats.Remove(ruleId);

                _logger.LogInformation("동적 규칙 제거 완료: {RuleId}", ruleId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "동적 규칙 제거 중 오류 발생: {RuleId}", ruleId);
                return false;
            }
        }

        /// <summary>
        /// 런타임에 규칙을 동적으로 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateRuleAsync(ConditionalRule rule)
        {
            try
            {
                _logger.LogInformation("동적 규칙 업데이트: {RuleId}", rule.RuleId);

                // 기존 규칙 제거
                await RemoveRuleAsync(rule.RuleId);

                // 새 규칙 추가
                return await AddRuleAsync(rule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "동적 규칙 업데이트 중 오류 발생: {RuleId}", rule.RuleId);
                return false;
            }
        }

        /// <summary>
        /// 규칙 실행 통계를 조회합니다
        /// </summary>
        public Dictionary<string, RuleExecutionStatistics> GetExecutionStatistics()
        {
            return new Dictionary<string, RuleExecutionStatistics>(_executionStats);
        }

        /// <summary>
        /// 캐시를 정리합니다
        /// </summary>
        public void ClearCache()
        {
            _logger.LogInformation("조건부 규칙 엔진 캐시 정리");
            
            _ruleCache.Clear();
            _conditionCache.Clear();
            _validationCache.Clear();
            _dependencyGraph.Clear();
            _executionStats.Clear();
        }

        #region Private Methods

        /// <summary>
        /// 의존 규칙들이 모두 통과했는지 확인합니다
        /// </summary>
        private async Task<bool> CheckDependentRulesAsync(
            ConditionalRule rule, 
            string gdbPath, 
            string tableName, 
            CancellationToken cancellationToken)
        {
            if (!rule.DependentRuleIds.Any())
            {
                return true; // 의존 규칙이 없으면 통과
            }

            foreach (var dependentRuleId in rule.DependentRuleIds)
            {
                if (!_ruleCache.TryGetValue(dependentRuleId, out var dependentRule))
                {
                    _logger.LogWarning("의존 규칙을 찾을 수 없음: {DependentRuleId}", dependentRuleId);
                    return false;
                }

                // 의존 규칙을 실행하여 통과 여부 확인
                var dependentErrors = await ValidateConditionalRuleAsync(gdbPath, tableName, dependentRule, cancellationToken);
                if (dependentErrors.Any())
                {
                    _logger.LogDebug("의존 규칙 실패: {DependentRuleId}, 오류 개수: {ErrorCount}", 
                        dependentRuleId, dependentErrors.Count);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 테이블 스키마 정보를 추출합니다
        /// </summary>
        private async Task<Dictionary<string, Type>> ExtractTableSchemaAsync(string gdbPath, string tableName)
        {
            try
            {
                return await _gdalDataReader.GetTableSchemaAsync(gdbPath, tableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 스키마 추출 실패: {TableName}", tableName);
                return new Dictionary<string, Type>();
            }
        }

        /// <summary>
        /// 조건 표현식을 파싱하고 캐시합니다
        /// </summary>
        private async Task<ExpressionParseResult> GetOrParseConditionAsync(
            string condition, 
            Dictionary<string, Type> tableSchema)
        {
            var cacheKey = $"condition_{condition}_{string.Join(",", tableSchema.Keys)}";
            
            if (_conditionCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            var parseResult = await _expressionEngine.ParseExpressionAsync(condition, tableSchema);
            _conditionCache.TryAdd(cacheKey, parseResult);
            
            return parseResult;
        }

        /// <summary>
        /// 검증 표현식을 파싱하고 캐시합니다
        /// </summary>
        private async Task<ExpressionParseResult> GetOrParseValidationAsync(
            string validationExpression, 
            Dictionary<string, Type> tableSchema)
        {
            var cacheKey = $"validation_{validationExpression}_{string.Join(",", tableSchema.Keys)}";
            
            if (_validationCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            var parseResult = await _expressionEngine.ParseExpressionAsync(validationExpression, tableSchema);
            _validationCache.TryAdd(cacheKey, parseResult);
            
            return parseResult;
        }

        /// <summary>
        /// 조건을 평가합니다
        /// </summary>
        private async Task<ExpressionExecutionResult> EvaluateConditionAsync(
            ExpressionParseResult parseResult, 
            Feature feature)
        {
            var context = new ExpressionExecutionContext
            {
                Options = new ExpressionExecutionOptions
                {
                    NullHandling = NullHandling.ReturnFalse,
                    AllowTypeConversion = true,
                    CaseSensitive = false,
                    EnableCaching = true
                }
            };

            return await _expressionEngine.ExecuteExpressionAsync(parseResult, feature, context);
        }

        /// <summary>
        /// 검증을 평가합니다
        /// </summary>
        private async Task<ExpressionExecutionResult> EvaluateValidationAsync(
            ExpressionParseResult parseResult, 
            Feature feature)
        {
            var context = new ExpressionExecutionContext
            {
                Options = new ExpressionExecutionOptions
                {
                    NullHandling = NullHandling.ReturnFalse,
                    AllowTypeConversion = true,
                    CaseSensitive = false,
                    EnableCaching = true
                }
            };

            return await _expressionEngine.ExecuteExpressionAsync(parseResult, feature, context);
        }

        /// <summary>
        /// 피처에서 ObjectId를 추출합니다
        /// </summary>
        private long GetObjectId(Feature feature)
        {
            try
            {
                // OBJECTID 필드 우선 확인
                var objectIdIndex = feature.GetFieldIndex("OBJECTID");
                if (objectIdIndex >= 0)
                {
                    return feature.GetFieldAsInteger64(objectIdIndex);
                }

                // FID 폴백
                return feature.GetFID();
            }
            catch
            {
                return feature.GetFID();
            }
        }



        /// <summary>
        /// 의존성 그래프를 구축합니다
        /// </summary>
        private void BuildDependencyGraph(List<ConditionalRule> rules)
        {
            _dependencyGraph.Clear();
            
            foreach (var rule in rules)
            {
                if (rule.DependentRuleIds.Any())
                {
                    _dependencyGraph[rule.RuleId] = rule.DependentRuleIds.ToList();
                }
            }
        }

        /// <summary>
        /// 실행 통계를 업데이트합니다
        /// </summary>
        private void UpdateExecutionStatistics(
            string ruleId, 
            DateTime startTime, 
            int processedCount, 
            int errorCount)
        {
            var executionTime = DateTime.UtcNow - startTime;
            
            if (!_executionStats.ContainsKey(ruleId))
            {
                _executionStats[ruleId] = new RuleExecutionStatistics
                {
                    RuleId = ruleId
                };
            }

            var stats = _executionStats[ruleId];
            stats.ExecutionCount++;
            stats.TotalProcessedFeatures += processedCount;
            stats.TotalErrorsFound += errorCount;
            stats.TotalExecutionTime += executionTime;
            stats.LastExecutionTime = DateTime.UtcNow;
            stats.AverageExecutionTime = TimeSpan.FromMilliseconds(
                stats.TotalExecutionTime.TotalMilliseconds / stats.ExecutionCount);
        }

        #endregion
    }

    /// <summary>
    /// 규칙 실행 통계 정보
    /// </summary>
    public class RuleExecutionStatistics
    {
        /// <summary>
        /// 규칙 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 실행 횟수
        /// </summary>
        public int ExecutionCount { get; set; }

        /// <summary>
        /// 총 처리된 피처 수
        /// </summary>
        public int TotalProcessedFeatures { get; set; }

        /// <summary>
        /// 총 발견된 오류 수
        /// </summary>
        public int TotalErrorsFound { get; set; }

        /// <summary>
        /// 총 실행 시간
        /// </summary>
        public TimeSpan TotalExecutionTime { get; set; }

        /// <summary>
        /// 평균 실행 시간
        /// </summary>
        public TimeSpan AverageExecutionTime { get; set; }

        /// <summary>
        /// 마지막 실행 시간
        /// </summary>
        public DateTime LastExecutionTime { get; set; }

        /// <summary>
        /// 오류 발견율 (%)
        /// </summary>
        public double ErrorRate => TotalProcessedFeatures > 0 
            ? (double)TotalErrorsFound / TotalProcessedFeatures * 100 
            : 0;
    }
}
