using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using SpatialCheckPro.Processors;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using SchemaCheckConfig = SpatialCheckPro.Models.Config.SchemaCheckConfig;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 4단계 검수 프로세스를 관리하는 서비스 구현체
    /// </summary>
    public class ValidationService : IValidationService
    {
        private readonly ILogger<ValidationService> _logger;
        private readonly ICsvConfigService _configService;
        private readonly ITableCheckProcessor _tableCheckProcessor;
        private readonly ISchemaCheckProcessor _schemaCheckProcessor;
        private readonly IGeometryCheckProcessor _geometryCheckProcessor;
        private readonly IRelationCheckProcessor _relationCheckProcessor;
        private readonly IValidationResultService _resultService;

        /// <summary>
        /// 진행 중인 검수 작업들을 추적하는 딕셔너리
        /// </summary>
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningValidations = new();

        /// <summary>
        /// 검수 상태를 추적하는 딕셔너리
        /// </summary>
        private readonly ConcurrentDictionary<string, ValidationStatus> _validationStatuses = new();

        public ValidationService(
            ILogger<ValidationService> logger,
            ICsvConfigService configService,
            ITableCheckProcessor tableCheckProcessor,
            ISchemaCheckProcessor schemaCheckProcessor,
            IGeometryCheckProcessor geometryCheckProcessor,
            IRelationCheckProcessor relationCheckProcessor,
            IValidationResultService resultService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _tableCheckProcessor = tableCheckProcessor ?? throw new ArgumentNullException(nameof(tableCheckProcessor));
            _schemaCheckProcessor = schemaCheckProcessor ?? throw new ArgumentNullException(nameof(schemaCheckProcessor));
            _geometryCheckProcessor = geometryCheckProcessor ?? throw new ArgumentNullException(nameof(geometryCheckProcessor));
            _relationCheckProcessor = relationCheckProcessor ?? throw new ArgumentNullException(nameof(relationCheckProcessor));
            _resultService = resultService ?? throw new ArgumentNullException(nameof(resultService));
        }

        /// <summary>
        /// 전체 검수 프로세스 실행
        /// </summary>
        public async Task<ValidationResult> ExecuteValidationAsync(
            SpatialFileInfo spatialFile, 
            string configDirectory,
            IProgress<ValidationProgress>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            var validationId = Guid.NewGuid().ToString();
            var startTime = DateTime.Now;

            LoggingService.LogValidationStarted(_logger, validationId, spatialFile.FilePath, spatialFile.FileSize);

            // 검수 결과 초기화
            var validationResult = new ValidationResult
            {
                ValidationId = validationId,
                TargetFile = spatialFile.FilePath,
                StartedAt = startTime,
                Status = ValidationStatus.Running
            };

            // 취소 토큰 등록
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runningValidations[validationId] = combinedCts;
            _validationStatuses[validationId] = ValidationStatus.Running;

            try
            {
                // 검수 설정 로드
                _logger.LogInformation("검수 설정을 로드합니다. 디렉토리: {ConfigDirectory}", configDirectory);
                var config = await _configService.LoadValidationConfigAsync(configDirectory);

                var validationProgress = new ValidationProgress
                {
                    StartTime = startTime,
                    CurrentStage = 0,
                    CurrentStageName = "검수 준비 중",
                    OverallPercentage = 0
                };

                progress?.Report(validationProgress);

                // 1단계: 테이블 검수
                validationProgress.CurrentStage = 1;
                validationProgress.CurrentStageName = "테이블 검수";
                validationProgress.OverallPercentage = 10;
                validationProgress.CurrentTask = "테이블 리스트 및 구조 검증 중";
                progress?.Report(validationProgress);

                var tableStageResult = await ExecuteTableCheckAsync(
                    spatialFile, config.TableChecks, combinedCts.Token);
                validationResult.TableCheckResult = ConvertStageResultToTableCheckResult(tableStageResult);

                validationProgress.OverallPercentage = 25;
                validationProgress.ErrorCount = validationResult.TableCheckResult.ErrorCount;
                validationProgress.WarningCount = validationResult.TableCheckResult.WarningCount;
                progress?.Report(validationProgress);

                // 1단계 실패 시 검수 중단
                if (validationResult.TableCheckResult.Status == CheckStatus.Failed)
                {
                    _logger.LogWarning("1단계 테이블 검수 실패로 인해 검수를 중단합니다. ValidationId: {ValidationId}", validationId);
                    
                    validationResult.Status = ValidationStatus.Failed;
                    validationResult.CompletedAt = DateTime.Now;
                    
                    // 부분 레포트 생성 및 저장
                    await _resultService.SaveValidationResultAsync(validationResult);
                    
                    validationProgress.CurrentTask = "1단계 실패로 검수 중단됨";
                    validationProgress.OverallPercentage = 100;
                    progress?.Report(validationProgress);

                    return validationResult;
                }

                // 2단계: 스키마 검수
                validationProgress.CurrentStage = 2;
                validationProgress.CurrentStageName = "스키마 검수";
                validationProgress.OverallPercentage = 30;
                validationProgress.CurrentTask = "컬럼 구조 및 데이터 타입 검증 중";
                progress?.Report(validationProgress);

                var schemaStageResult = await ExecuteSchemaCheckAsync(
                    spatialFile, config.SchemaChecks, combinedCts.Token);
                validationResult.SchemaCheckResult = ConvertStageResultToSchemaCheckResult(schemaStageResult);

                validationProgress.OverallPercentage = 50;
                validationProgress.ErrorCount += validationResult.SchemaCheckResult.ErrorCount;
                validationProgress.WarningCount += validationResult.SchemaCheckResult.WarningCount;
                progress?.Report(validationProgress);

                // 3단계: 지오메트리 검수
                validationProgress.CurrentStage = 3;
                validationProgress.CurrentStageName = "지오메트리 검수";
                validationProgress.OverallPercentage = 55;
                validationProgress.CurrentTask = "지오메트리 오류 검사 중";
                progress?.Report(validationProgress);

                var geometryStageResult = await ExecuteGeometryCheckAsync(
                    spatialFile, config.GeometryChecks, combinedCts.Token);
                validationResult.GeometryCheckResult = ConvertStageResultToGeometryCheckResult(geometryStageResult);

                validationProgress.OverallPercentage = 75;
                validationProgress.ErrorCount += validationResult.GeometryCheckResult.ErrorCount;
                validationProgress.WarningCount += validationResult.GeometryCheckResult.WarningCount;
                progress?.Report(validationProgress);

                // 4단계: 관계 검수
                validationProgress.CurrentStage = 4;
                validationProgress.CurrentStageName = "관계 검수";
                validationProgress.OverallPercentage = 80;
                validationProgress.CurrentTask = "테이블 간 관계 검증 중";
                progress?.Report(validationProgress);

                var relationStageResult = await ExecuteRelationCheckAsync(
                    spatialFile, config.RelationChecks, combinedCts.Token);
                validationResult.RelationCheckResult = ConvertStageResultToRelationCheckResult(relationStageResult);

                validationProgress.OverallPercentage = 95;
                validationProgress.ErrorCount += validationResult.RelationCheckResult.ErrorCount;
                validationProgress.WarningCount += validationResult.RelationCheckResult.WarningCount;
                progress?.Report(validationProgress);

                // 검수 완료 처리
                validationResult.CompletedAt = DateTime.Now;
                validationResult.Status = ValidationStatus.Completed;
                validationResult.TotalErrors = validationProgress.ErrorCount;
                validationResult.TotalWarnings = validationProgress.WarningCount;

                // 검수 결과 저장
                await _resultService.SaveValidationResultAsync(validationResult);

                validationProgress.CurrentTask = "검수 완료";
                validationProgress.OverallPercentage = 100;
                validationProgress.EstimatedCompletionTime = DateTime.Now;
                progress?.Report(validationProgress);

                LoggingService.LogValidationCompleted(_logger, validationId, DateTime.Now - startTime, 
                    validationResult.TotalErrors, validationResult.TotalWarnings, validationResult.Status.ToString());

                return validationResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("검수 프로세스가 취소되었습니다. ID: {ValidationId}", validationId);
                
                validationResult.Status = ValidationStatus.Cancelled;
                validationResult.CompletedAt = DateTime.Now;
                
                await _resultService.SaveValidationResultAsync(validationResult);
                
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 프로세스 중 오류가 발생했습니다. ID: {ValidationId}", validationId);
                
                validationResult.Status = ValidationStatus.Failed;
                validationResult.CompletedAt = DateTime.Now;
                validationResult.ErrorMessage = ex.Message;
                
                await _resultService.SaveValidationResultAsync(validationResult);
                
                throw;
            }
            finally
            {
                // 정리 작업
                _runningValidations.TryRemove(validationId, out _);
                _validationStatuses[validationId] = validationResult.Status;
            }
        }

        /// <summary>
        /// 1단계: 테이블 검수 실행
        /// </summary>
        public async Task<StageResult> ExecuteTableCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<TableCheckConfig> config,
            CancellationToken cancellationToken = default)
        {
            var stageResult = new StageResult
            {
                StageNumber = 1,
                StageName = "테이블 검수",
                Status = StageStatus.Running,
                StartedAt = DateTime.Now
            };

            try
            {
                LoggingService.LogValidationStageStarted(_logger, spatialFile.FilePath, 1, "테이블 검수");

                var configList = config.ToList();
                if (!configList.Any())
                {
                    _logger.LogWarning("테이블 검수 설정이 없습니다.");
                    stageResult.Status = StageStatus.Skipped;
                    stageResult.CompletedAt = DateTime.Now;
                    return stageResult;
                }

                // 테이블 리스트 검증
                cancellationToken.ThrowIfCancellationRequested();
                var tableListValidationResult = await _tableCheckProcessor.ValidateTableListAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                var tableListResult = ConvertValidationResultToCheckResult(tableListValidationResult, "TABLE_LIST_CHECK", "테이블 리스트 검증");
                stageResult.CheckResults.Add(tableListResult);

                // 좌표계 검증
                cancellationToken.ThrowIfCancellationRequested();
                var coordSystemValidationResult = await _tableCheckProcessor.ValidateCoordinateSystemAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                var coordSystemResult = ConvertValidationResultToCheckResult(coordSystemValidationResult, "COORDINATE_SYSTEM_CHECK", "좌표계 검증");
                stageResult.CheckResults.Add(coordSystemResult);

                // 지오메트리 타입 검증
                cancellationToken.ThrowIfCancellationRequested();
                var geomTypeValidationResult = await _tableCheckProcessor.ValidateGeometryTypeAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                var geomTypeResult = ConvertValidationResultToCheckResult(geomTypeValidationResult, "GEOMETRY_TYPE_CHECK", "지오메트리 타입 검증");
                stageResult.CheckResults.Add(geomTypeResult);

                // 단계 결과 집계
                var hasErrors = stageResult.CheckResults.Any(r => r.Status == CheckStatus.Failed);
                stageResult.Status = hasErrors ? StageStatus.Failed : StageStatus.Completed;
                stageResult.CompletedAt = DateTime.Now;

                var errorCount = stageResult.CheckResults.Sum(r => r.ErrorCount);
                var warningCount = stageResult.CheckResults.Sum(r => r.WarningCount);
                LoggingService.LogValidationStageCompleted(_logger, spatialFile.FilePath, 1, "테이블 검수", 
                    stageResult.CompletedAt.Value - stageResult.StartedAt, errorCount, warningCount);

                return stageResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("1단계 테이블 검수가 취소되었습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = "검수가 취소되었습니다.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1단계 테이블 검수 중 오류가 발생했습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = ex.Message;
                throw;
            }
        }

        /// <summary>
        /// 2단계: 스키마 검수 실행
        /// </summary>
        public async Task<StageResult> ExecuteSchemaCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<SchemaCheckConfig> config,
            CancellationToken cancellationToken = default)
        {
            var stageResult = new StageResult
            {
                StageNumber = 2,
                StageName = "스키마 검수",
                Status = StageStatus.Running,
                StartedAt = DateTime.Now
            };

            try
            {
                _logger.LogInformation("2단계 스키마 검수를 시작합니다. 파일: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                if (!configList.Any())
                {
                    _logger.LogWarning("스키마 검수 설정이 없습니다.");
                    stageResult.Status = StageStatus.Skipped;
                    stageResult.CompletedAt = DateTime.Now;
                    return stageResult;
                }

                // 컬럼 구조 검증
                cancellationToken.ThrowIfCancellationRequested();
                var columnStructureValidationResult = await _schemaCheckProcessor.ValidateColumnStructureAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                var columnStructureResult = ConvertValidationResultToCheckResult(columnStructureValidationResult, "COLUMN_STRUCTURE_CHECK", "컬럼 구조 검증");
                stageResult.CheckResults.Add(columnStructureResult);

                // 데이터 타입 검증
                cancellationToken.ThrowIfCancellationRequested();
                var dataTypeValidationResult = await _schemaCheckProcessor.ValidateDataTypesAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                var dataTypeResult = ConvertValidationResultToCheckResult(dataTypeValidationResult, "DATA_TYPE_CHECK", "데이터 타입 검증");
                stageResult.CheckResults.Add(dataTypeResult);

                // PK/FK 검증 (SHP 파일 제외)
                if (spatialFile.Format != SpatialFileFormat.SHP)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pkFkValidationResult = await _schemaCheckProcessor.ValidatePrimaryForeignKeysAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                    var pkFkResult = ConvertValidationResultToCheckResult(pkFkValidationResult, "PK_FK_CHECK", "기본키/외래키 검증");
                    stageResult.CheckResults.Add(pkFkResult);

                    // FK 연계 테이블 검증
                    cancellationToken.ThrowIfCancellationRequested();
                    var fkRelationValidationResult = await _schemaCheckProcessor.ValidateForeignKeyRelationsAsync(spatialFile.FilePath, configList.First(), cancellationToken);
                    var fkRelationResult = ConvertValidationResultToCheckResult(fkRelationValidationResult, "FK_RELATION_CHECK", "외래키 관계 검증");
                    stageResult.CheckResults.Add(fkRelationResult);
                }

                // 단계 결과 집계
                var hasErrors = stageResult.CheckResults.Any(r => r.Status == CheckStatus.Failed);
                stageResult.Status = hasErrors ? StageStatus.Failed : StageStatus.Completed;
                stageResult.CompletedAt = DateTime.Now;

                _logger.LogInformation("2단계 스키마 검수가 완료되었습니다. 상태: {Status}, 소요시간: {Duration}",
                    stageResult.Status, stageResult.CompletedAt - stageResult.StartedAt);

                return stageResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("2단계 스키마 검수가 취소되었습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = "검수가 취소되었습니다.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "2단계 스키마 검수 중 오류가 발생했습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = ex.Message;
                throw;
            }
        }

        /// <summary>
        /// 3단계: 지오메트리 검수 실행
        /// </summary>
        public async Task<StageResult> ExecuteGeometryCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<GeometryCheckConfig> config,
            CancellationToken cancellationToken = default)
        {
            var stageResult = new StageResult
            {
                StageNumber = 3,
                StageName = "지오메트리 검수",
                Status = StageStatus.Running,
                StartedAt = DateTime.Now
            };

            try
            {
                _logger.LogInformation("3단계 지오메트리 검수를 시작합니다. 파일: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                if (!configList.Any())
                {
                    _logger.LogWarning("지오메트리 검수 설정이 없습니다.");
                    stageResult.Status = StageStatus.Skipped;
                    stageResult.CompletedAt = DateTime.Now;
                    return stageResult;
                }

                // 중복 지오메트리 검사
                var duplicateConfigs = configList.Where(c => c.CheckDuplicate == "Y").ToList();
                if (duplicateConfigs.Any())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var duplicateValidationResult = await _geometryCheckProcessor.CheckDuplicateGeometriesAsync(spatialFile.FilePath, duplicateConfigs.First(), cancellationToken);
                    var duplicateResult = ConvertValidationResultToCheckResult(duplicateValidationResult, "DUPLICATE_GEOMETRY_CHECK", "중복 지오메트리 검사");
                    stageResult.CheckResults.Add(duplicateResult);
                }

                // 겹침 지오메트리 검사
                var overlapConfigs = configList.Where(c => c.CheckOverlap == "Y").ToList();
                if (overlapConfigs.Any())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var overlapValidationResult = await _geometryCheckProcessor.CheckOverlappingGeometriesAsync(spatialFile.FilePath, overlapConfigs.First(), cancellationToken);
                    var overlapResult = ConvertValidationResultToCheckResult(overlapValidationResult, "OVERLAPPING_GEOMETRY_CHECK", "겹침 지오메트리 검사");
                    stageResult.CheckResults.Add(overlapResult);
                }

                // 꼬임 지오메트리 검사
                var twistConfigs = configList.Where(c => c.CheckSelfIntersection == "Y").ToList();
                if (twistConfigs.Any())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var twistValidationResult = await _geometryCheckProcessor.CheckTwistedGeometriesAsync(spatialFile.FilePath, twistConfigs.First(), cancellationToken);
                    var twistResult = ConvertValidationResultToCheckResult(twistValidationResult, "TWISTED_GEOMETRY_CHECK", "꼬임 지오메트리 검사");
                    stageResult.CheckResults.Add(twistResult);
                }

                // 슬리버 폴리곤 검사
                var sliverConfigs = configList.Where(c => c.CheckSliver == "Y").ToList();
                if (sliverConfigs.Any())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sliverValidationResult = await _geometryCheckProcessor.CheckSliverPolygonsAsync(spatialFile.FilePath, sliverConfigs.First(), cancellationToken);
                    var sliverResult = ConvertValidationResultToCheckResult(sliverValidationResult, "SLIVER_POLYGON_CHECK", "슬리버 폴리곤 검사");
                    stageResult.CheckResults.Add(sliverResult);
                }

                // 단계 결과 집계
                var hasErrors = stageResult.CheckResults.Any(r => r.Status == CheckStatus.Failed);
                stageResult.Status = hasErrors ? StageStatus.Failed : StageStatus.Completed;
                stageResult.CompletedAt = DateTime.Now;

                _logger.LogInformation("3단계 지오메트리 검수가 완료되었습니다. 상태: {Status}, 소요시간: {Duration}",
                    stageResult.Status, stageResult.CompletedAt - stageResult.StartedAt);

                return stageResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("3단계 지오메트리 검수가 취소되었습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = "검수가 취소되었습니다.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "3단계 지오메트리 검수 중 오류가 발생했습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = ex.Message;
                throw;
            }
        }

        /// <summary>
        /// 4단계: 관계 검수 실행
        /// </summary>
        public async Task<StageResult> ExecuteRelationCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<RelationCheckConfig> config,
            CancellationToken cancellationToken = default)
        {
            var stageResult = new StageResult
            {
                StageNumber = 4,
                StageName = "관계 검수",
                Status = StageStatus.Running,
                StartedAt = DateTime.Now
            };

            try
            {
                _logger.LogInformation("4단계 관계 검수를 시작합니다. 파일: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                if (!configList.Any())
                {
                    _logger.LogWarning("관계 검수 설정이 없습니다.");
                    stageResult.Status = StageStatus.Skipped;
                    stageResult.CompletedAt = DateTime.Now;
                    return stageResult;
                }

                // 현재 요구사항 기반 기본 관계 검수 실행 (Processor가 케이스별 내부 처리)
                cancellationToken.ThrowIfCancellationRequested();
                // CSV의 모든 규칙을 순회 처리 (Enabled=Y 만)
                foreach (var rc in configList.Where(r => string.Equals(r.Enabled, "Y", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relationValidation = await _relationCheckProcessor.ProcessAsync(spatialFile.FilePath, rc, cancellationToken);
                    var relationResult = ConvertValidationResultToCheckResult(relationValidation,
                        string.IsNullOrWhiteSpace(rc.RuleId) ? "RELATION_CHECK" : rc.RuleId,
                        string.IsNullOrWhiteSpace(rc.CaseType) ? "관계 검수" : rc.CaseType);
                    stageResult.CheckResults.Add(relationResult);
                }

                // 단계 결과 집계
                var hasErrors = stageResult.CheckResults.Any(r => r.Status == CheckStatus.Failed);
                stageResult.Status = hasErrors ? StageStatus.Failed : StageStatus.Completed;
                stageResult.CompletedAt = DateTime.Now;

                _logger.LogInformation("4단계 관계 검수가 완료되었습니다. 상태: {Status}, 소요시간: {Duration}",
                    stageResult.Status, stageResult.CompletedAt - stageResult.StartedAt);

                return stageResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("4단계 관계 검수가 취소되었습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = "검수가 취소되었습니다.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "4단계 관계 검수 중 오류가 발생했습니다.");
                stageResult.Status = StageStatus.Failed;
                stageResult.CompletedAt = DateTime.Now;
                stageResult.ErrorMessage = ex.Message;
                throw;
            }
        }

        /// <summary>
        /// 검수 취소
        /// </summary>
        public async Task<bool> CancelValidationAsync(string validationId)
        {
            try
            {
                _logger.LogInformation("검수 취소를 요청합니다. ID: {ValidationId}", validationId);

                if (_runningValidations.TryGetValue(validationId, out var cts))
                {
                    cts.Cancel();
                    _validationStatuses[validationId] = ValidationStatus.Cancelled;
                    
                    _logger.LogInformation("검수가 취소되었습니다. ID: {ValidationId}", validationId);
                    return true;
                }

                _logger.LogWarning("취소할 검수를 찾을 수 없습니다. ID: {ValidationId}", validationId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 취소 중 오류가 발생했습니다. ID: {ValidationId}", validationId);
                return false;
            }
        }

        /// <summary>
        /// 검수 상태 조회
        /// </summary>
        public async Task<ValidationStatus> GetValidationStatusAsync(string validationId)
        {
            await Task.CompletedTask; // 비동기 메서드 시그니처 유지

            if (_validationStatuses.TryGetValue(validationId, out var status))
            {
                return status;
            }

            // 데이터베이스에서 조회 시도
            var result = await _resultService.GetValidationResultAsync(validationId);
            return result?.Status ?? ValidationStatus.NotStarted;
        }

        /// <summary>
        /// ValidationResult를 CheckResult로 변환합니다
        /// </summary>
        private static CheckResult ConvertValidationResultToCheckResult(ValidationResult validationResult, string checkId, string checkName)
        {
            return new CheckResult
            {
                CheckId = checkId,
                CheckName = checkName,
                Status = validationResult.IsValid ? CheckStatus.Passed : CheckStatus.Failed,
                ErrorCount = validationResult.ErrorCount,
                WarningCount = validationResult.WarningCount,
                Errors = validationResult.Errors,
                Warnings = validationResult.Warnings
            };
        }

        /// <summary>
        /// StageResult를 TableCheckResult로 변환합니다
        /// </summary>
        private static TableCheckResult ConvertStageResultToTableCheckResult(StageResult stageResult)
        {
            return new TableCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 SchemaCheckResult로 변환합니다
        /// </summary>
        private static SchemaCheckResult ConvertStageResultToSchemaCheckResult(StageResult stageResult)
        {
            return new SchemaCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 GeometryCheckResult로 변환합니다
        /// </summary>
        private static GeometryCheckResult ConvertStageResultToGeometryCheckResult(StageResult stageResult)
        {
            return new GeometryCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 RelationCheckResult로 변환합니다
        /// </summary>
        private static RelationCheckResult ConvertStageResultToRelationCheckResult(StageResult stageResult)
        {
            return new RelationCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageStatus를 CheckStatus로 변환합니다
        /// </summary>
        private static CheckStatus ConvertStageStatusToCheckStatus(StageStatus stageStatus)
        {
            return stageStatus switch
            {
                StageStatus.Completed => CheckStatus.Passed,
                StageStatus.Failed => CheckStatus.Failed,
                StageStatus.CompletedWithWarnings => CheckStatus.Warning,
                StageStatus.Skipped => CheckStatus.Skipped,
                _ => CheckStatus.Failed
            };
        }
    }
}
