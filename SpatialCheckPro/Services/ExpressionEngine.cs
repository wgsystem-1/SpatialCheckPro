using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// SQL 유사 표현식 파싱 및 실행 엔진 구현
    /// </summary>
    public class ExpressionEngine : IExpressionEngine
    {
        private readonly ILogger<ExpressionEngine> _logger;
        private readonly Dictionary<string, ExpressionParseResult> _parseCache;
        private readonly Dictionary<string, object> _evaluationCache;

        public ExpressionEngine(ILogger<ExpressionEngine> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _parseCache = new Dictionary<string, ExpressionParseResult>();
            _evaluationCache = new Dictionary<string, object>();
        }

        /// <summary>
        /// 표현식을 파싱하고 유효성을 검증합니다
        /// </summary>
        public async Task<ExpressionParseResult> ParseExpressionAsync(
            string expression, Dictionary<string, Type> tableSchema)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return new ExpressionParseResult
                {
                    IsValid = false,
                    Errors = { "표현식이 비어있습니다." }
                };
            }

            // 캐시 확인
            var cacheKey = $"{expression}_{string.Join(",", tableSchema.Keys)}";
            if (_parseCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            var result = new ExpressionParseResult();

            try
            {
                _logger.LogDebug("표현식 파싱 시작: {Expression}", expression);

                // 1. 기본 구문 검증
                var syntaxErrors = ValidateBasicSyntax(expression);
                if (syntaxErrors.Any())
                {
                    result.Errors.AddRange(syntaxErrors);
                    result.IsValid = false;
                    return result;
                }

                // 2. 필드 참조 추출 및 검증
                result.FieldReferences = ExtractFieldReferences(expression);
                var fieldErrors = ValidateFieldReferences(result.FieldReferences, tableSchema);
                if (fieldErrors.Any())
                {
                    result.Errors.AddRange(fieldErrors);
                    result.IsValid = false;
                    return result;
                }

                // 3. 표현식 타입 추론
                result.ExpressionType = InferExpressionType(expression, result.FieldReferences);

                // 4. 추상 구문 트리 생성
                result.RootNode = await BuildExpressionTreeAsync(expression, tableSchema);
                if (result.RootNode == null)
                {
                    result.Errors.Add("표현식 트리 생성에 실패했습니다.");
                    result.IsValid = false;
                    return result;
                }

                // 5. 표현식 최적화
                result.OptimizedExpression = OptimizeExpression(expression);

                result.IsValid = true;
                _logger.LogDebug("표현식 파싱 완료: {Expression}", expression);

                // 캐시에 저장
                _parseCache[cacheKey] = result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "표현식 파싱 중 오류 발생: {Expression}", expression);
                result.IsValid = false;
                result.Errors.Add($"파싱 오류: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 파싱된 표현식을 피처에 대해 실행합니다
        /// </summary>
        public Task<ExpressionExecutionResult> ExecuteExpressionAsync(
            ExpressionParseResult parseResult, Feature feature, ExpressionExecutionContext context)
        {
            var startTime = DateTime.UtcNow;
            var result = new ExpressionExecutionResult();

            try
            {
                if (!parseResult.IsValid || parseResult.RootNode == null)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "유효하지 않은 파싱 결과입니다.";
                    return Task.FromResult(result);
                }

                // 실행 컨텍스트에 현재 피처 정보 설정
                SetCurrentFeatureInContext(context, feature);

                // 표현식 트리 평가
                var value = parseResult.RootNode.Evaluate(context);
                
                result.IsSuccess = true;
                result.Value = value;
                result.ExecutionTime = DateTime.UtcNow - startTime;

                _logger.LogDebug("표현식 실행 완료: 결과={Value}, 시간={ExecutionTime}ms", 
                    value, result.ExecutionTime.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "표현식 실행 중 오류 발생");
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                result.ExecutionTime = DateTime.UtcNow - startTime;
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// 표현식을 직접 실행합니다 (파싱과 실행을 한번에)
        /// </summary>
        public async Task<ExpressionExecutionResult> EvaluateExpressionAsync(
            string expression, Feature feature, ExpressionExecutionContext context)
        {
            try
            {
                // 간단한 캐시 키 생성
                var cacheKey = $"{expression}_{feature.GetFID()}";
                if (context.Options.EnableCaching && _evaluationCache.TryGetValue(cacheKey, out var cachedValue))
                {
                    return new ExpressionExecutionResult
                    {
                        IsSuccess = true,
                        Value = cachedValue
                    };
                }

                // 테이블 스키마 추출
                var tableSchema = ExtractTableSchemaFromFeature(feature);
                
                // 파싱
                var parseResult = await ParseExpressionAsync(expression, tableSchema);
                if (!parseResult.IsValid)
                {
                    return new ExpressionExecutionResult
                    {
                        IsSuccess = false,
                        ErrorMessage = string.Join("; ", parseResult.Errors)
                    };
                }

                // 실행
                var executionResult = await ExecuteExpressionAsync(parseResult, feature, context);

                // 캐시에 저장
                if (context.Options.EnableCaching && executionResult.IsSuccess)
                {
                    _evaluationCache[cacheKey] = executionResult.Value;
                }

                return executionResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "표현식 평가 중 오류 발생: {Expression}", expression);
                return new ExpressionExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 복합 표현식을 실행합니다 (여러 테이블 참조)
        /// </summary>
        public async Task<ExpressionExecutionResult> EvaluateComplexExpressionAsync(
            string expression, Dictionary<string, Feature> features, ExpressionExecutionContext context)
        {
            var startTime = DateTime.UtcNow;
            var result = new ExpressionExecutionResult();

            try
            {
                _logger.LogDebug("복합 표현식 실행 시작: {Expression}", expression);

                // 모든 테이블의 스키마 정보를 컨텍스트에 설정
                foreach (var kvp in features)
                {
                    var tableSchema = ExtractTableSchemaFromFeature(kvp.Value);
                    context.TableSchemas[kvp.Key] = tableSchema;
                }

                // 통합 스키마 생성
                var combinedSchema = new Dictionary<string, Type>();
                foreach (var tableSchema in context.TableSchemas.Values)
                {
                    foreach (var field in tableSchema)
                    {
                        combinedSchema[field.Key] = field.Value;
                    }
                }

                // 표현식 파싱
                var parseResult = await ParseExpressionAsync(expression, combinedSchema);
                if (!parseResult.IsValid)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = string.Join("; ", parseResult.Errors);
                    return result;
                }

                // 컨텍스트에 모든 피처 정보 설정
                SetMultipleFeaturesInContext(context, features);

                // 표현식 실행
                var value = parseResult.RootNode?.Evaluate(context);
                
                result.IsSuccess = true;
                result.Value = value;
                result.ExecutionTime = DateTime.UtcNow - startTime;

                _logger.LogDebug("복합 표현식 실행 완료: 결과={Value}, 시간={ExecutionTime}ms", 
                    value, result.ExecutionTime.TotalMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "복합 표현식 실행 중 오류 발생: {Expression}", expression);
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                result.ExecutionTime = DateTime.UtcNow - startTime;
                return result;
            }
        }

        /// <summary>
        /// 표현식에서 사용된 필드 목록을 추출합니다
        /// </summary>
        public List<FieldReference> ExtractFieldReferences(string expression)
        {
            var fieldReferences = new List<FieldReference>();

            try
            {
                // 필드 참조 패턴: [테이블.]필드명
                var fieldPattern = @"\b(?:(\w+)\.)?(\w+)\b";
                var matches = Regex.Matches(expression, fieldPattern, RegexOptions.IgnoreCase);

                var position = 0;
                foreach (Match match in matches)
                {
                    var tableAlias = match.Groups[1].Success ? match.Groups[1].Value : null;
                    var fieldName = match.Groups[2].Value;
                    var fullReference = match.Value;

                    // SQL 키워드는 제외
                    if (IsSqlKeyword(fieldName))
                        continue;

                    fieldReferences.Add(new FieldReference
                    {
                        TableAlias = tableAlias,
                        FieldName = fieldName,
                        FullReference = fullReference,
                        Position = position++
                    });
                }

                return fieldReferences.DistinctBy(fr => fr.FullReference).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필드 참조 추출 중 오류 발생: {Expression}", expression);
                return fieldReferences;
            }
        }

        /// <summary>
        /// 표현식을 OGR 필터 문자열로 변환합니다
        /// </summary>
        public string ConvertToOgrFilter(string expression, string? tableAlias = null)
        {
            try
            {
                // 기본적인 SQL 표현식을 OGR 필터로 변환
                var ogrFilter = expression;

                // 테이블 별칭 제거
                if (!string.IsNullOrEmpty(tableAlias))
                {
                    ogrFilter = ogrFilter.Replace($"{tableAlias}.", "");
                }

                // SQL 함수를 OGR 함수로 변환
                ogrFilter = ogrFilter.Replace("UPPER(", "UPPER(");
                ogrFilter = ogrFilter.Replace("LOWER(", "LOWER(");
                ogrFilter = ogrFilter.Replace("LENGTH(", "LENGTH(");

                // 날짜 형식 변환
                ogrFilter = Regex.Replace(ogrFilter, @"DATE\s*\(\s*'([^']+)'\s*\)", "'$1'");

                return ogrFilter;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OGR 필터 변환 중 오류 발생: {Expression}", expression);
                return expression;
            }
        }

        #region Private Methods

        /// <summary>
        /// 기본 구문을 검증합니다
        /// </summary>
        private List<string> ValidateBasicSyntax(string expression)
        {
            var errors = new List<string>();

            try
            {
                // 괄호 균형 검사
                var openParens = expression.Count(c => c == '(');
                var closeParens = expression.Count(c => c == ')');
                if (openParens != closeParens)
                {
                    errors.Add("괄호가 균형을 이루지 않습니다.");
                }

                // 따옴표 균형 검사
                var singleQuotes = expression.Count(c => c == '\'');
                if (singleQuotes % 2 != 0)
                {
                    errors.Add("작은따옴표가 균형을 이루지 않습니다.");
                }

                // 기본 SQL 구문 검사
                if (Regex.IsMatch(expression, @";\s*$"))
                {
                    errors.Add("세미콜론은 허용되지 않습니다.");
                }

                // 위험한 키워드 검사
                var dangerousKeywords = new[] { "DROP", "DELETE", "INSERT", "UPDATE", "CREATE", "ALTER" };
                foreach (var keyword in dangerousKeywords)
                {
                    if (expression.ToUpper().Contains(keyword))
                    {
                        errors.Add($"위험한 키워드가 포함되어 있습니다: {keyword}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본 구문 검증 중 오류 발생");
                errors.Add("구문 검증 중 오류가 발생했습니다.");
            }

            return errors;
        }

        /// <summary>
        /// 필드 참조를 검증합니다
        /// </summary>
        private List<string> ValidateFieldReferences(
            List<FieldReference> fieldReferences, 
            Dictionary<string, Type> tableSchema)
        {
            var errors = new List<string>();

            foreach (var fieldRef in fieldReferences)
            {
                if (!tableSchema.ContainsKey(fieldRef.FieldName))
                {
                    errors.Add($"존재하지 않는 필드입니다: {fieldRef.FieldName}");
                }
                else
                {
                    fieldRef.FieldType = tableSchema[fieldRef.FieldName];
                }
            }

            return errors;
        }

        /// <summary>
        /// 표현식 타입을 추론합니다
        /// </summary>
        private ExpressionType InferExpressionType(string expression, List<FieldReference> fieldReferences)
        {
            // 비교 연산자가 있으면 Boolean
            if (Regex.IsMatch(expression, @"[<>=!]+|AND|OR|NOT", RegexOptions.IgnoreCase))
            {
                return ExpressionType.Boolean;
            }

            // 수학 연산자가 있으면 Numeric
            if (Regex.IsMatch(expression, @"[+\-*/]"))
            {
                return ExpressionType.Numeric;
            }

            // 문자열 함수가 있으면 String
            if (Regex.IsMatch(expression, @"UPPER|LOWER|SUBSTRING|CONCAT", RegexOptions.IgnoreCase))
            {
                return ExpressionType.String;
            }

            // 날짜 함수가 있으면 Date
            if (Regex.IsMatch(expression, @"DATE|YEAR|MONTH|DAY", RegexOptions.IgnoreCase))
            {
                return ExpressionType.Date;
            }

            return ExpressionType.Mixed;
        }

        /// <summary>
        /// 표현식 트리를 구축합니다
        /// </summary>
        private async Task<ExpressionNode?> BuildExpressionTreeAsync(
            string expression, 
            Dictionary<string, Type> tableSchema)
        {
            try
            {
                // 간단한 파서 구현 (실제로는 더 복잡한 파서가 필요)
                return new LiteralExpressionNode(true); // 임시 구현
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "표현식 트리 구축 중 오류 발생");
                return null;
            }
        }

        /// <summary>
        /// 표현식을 최적화합니다
        /// </summary>
        private string OptimizeExpression(string expression)
        {
            try
            {
                // 기본적인 최적화
                var optimized = expression.Trim();
                
                // 중복 공백 제거
                optimized = Regex.Replace(optimized, @"\s+", " ");
                
                // 불필요한 괄호 제거 (간단한 경우만)
                optimized = Regex.Replace(optimized, @"\(\s*([^()]+)\s*\)", "$1");

                return optimized;
            }
            catch
            {
                return expression;
            }
        }

        /// <summary>
        /// 피처에서 테이블 스키마를 추출합니다
        /// </summary>
        private Dictionary<string, Type> ExtractTableSchemaFromFeature(Feature feature)
        {
            var schema = new Dictionary<string, Type>();

            try
            {
                var featureDefn = feature.GetDefnRef();
                for (int i = 0; i < featureDefn.GetFieldCount(); i++)
                {
                    var fieldDefn = featureDefn.GetFieldDefn(i);
                    var fieldName = fieldDefn.GetName();
                    var fieldType = ConvertOgrTypeToClrType(fieldDefn.GetFieldType());
                    
                    schema[fieldName] = fieldType;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 스키마 추출 중 오류 발생");
            }

            return schema;
        }

        /// <summary>
        /// OGR 타입을 CLR 타입으로 변환합니다
        /// </summary>
        private Type ConvertOgrTypeToClrType(FieldType ogrType)
        {
            return ogrType switch
            {
                FieldType.OFTInteger => typeof(int),
                FieldType.OFTInteger64 => typeof(long),
                FieldType.OFTReal => typeof(double),
                FieldType.OFTString => typeof(string),
                FieldType.OFTDate => typeof(DateTime),
                FieldType.OFTDateTime => typeof(DateTime),
                FieldType.OFTTime => typeof(TimeSpan),
                _ => typeof(string)
            };
        }

        /// <summary>
        /// 현재 피처 정보를 컨텍스트에 설정합니다
        /// </summary>
        private void SetCurrentFeatureInContext(ExpressionExecutionContext context, Feature feature)
        {
            context.Variables["CURRENT_FEATURE"] = feature;
            context.Variables["CURRENT_FID"] = feature.GetFID();
        }

        /// <summary>
        /// 여러 피처 정보를 컨텍스트에 설정합니다
        /// </summary>
        private void SetMultipleFeaturesInContext(
            ExpressionExecutionContext context, 
            Dictionary<string, Feature> features)
        {
            foreach (var kvp in features)
            {
                context.Variables[$"{kvp.Key}_FEATURE"] = kvp.Value;
                context.Variables[$"{kvp.Key}_FID"] = kvp.Value.GetFID();
            }
        }

        /// <summary>
        /// SQL 키워드인지 확인합니다
        /// </summary>
        private bool IsSqlKeyword(string word)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "IS", "NULL",
                "TRUE", "FALSE", "BETWEEN", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END"
            };

            return keywords.Contains(word);
        }

        #endregion
    }

    /// <summary>
    /// 리터럴 표현식 노드
    /// </summary>
    public class LiteralExpressionNode : ExpressionNode
    {
        private readonly object? _value;

        public LiteralExpressionNode(object? value)
        {
            _value = value;
        }

        public override ExpressionNodeType NodeType => ExpressionNodeType.Literal;

        public override object? Evaluate(ExpressionExecutionContext context)
        {
            return _value;
        }

        public override string ToString()
        {
            return _value?.ToString() ?? "NULL";
        }
    }
}
