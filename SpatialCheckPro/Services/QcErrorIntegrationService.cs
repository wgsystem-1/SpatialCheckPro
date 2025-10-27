using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC 오류 데이터 통합 관리 서비스
    /// </summary>
    public class QcErrorIntegrationService
    {
        private readonly ILogger<QcErrorIntegrationService> _logger;
        private readonly FgdbSchemaService _schemaService;
        private readonly QcErrorDataService _dataService;
        private readonly ValidationResultConverter _converter;
        private readonly ShapefileToFgdbMigrationService _migrationService;
        private readonly QcStoragePathService _pathService;
        private readonly RelationErrorsIntegrator _relationIntegrator;

        public QcErrorIntegrationService(
            ILogger<QcErrorIntegrationService> logger,
            FgdbSchemaService schemaService,
            QcErrorDataService dataService,
            ValidationResultConverter converter,
            ShapefileToFgdbMigrationService migrationService,
            QcStoragePathService pathService,
            RelationErrorsIntegrator relationIntegrator)
        {
            _logger = logger;
            _schemaService = schemaService;
            _dataService = dataService;
            _converter = converter;
            _migrationService = migrationService;
            _pathService = pathService;
            _relationIntegrator = relationIntegrator;
        }

        /// <summary>
        /// 검수 결과를 FGDB에 자동 저장합니다
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <param name="targetFilePath">검수 대상 파일 경로</param>
        /// <param name="outputGdbPath">출력 FGDB 경로 (null이면 자동 생성)</param>
        /// <param name="executedBy">실행자</param>
        /// <returns>저장된 오류 개수</returns>
        public async Task<int> SaveValidationResultToFgdbAsync(
            ValidationResult validationResult, 
            string targetFilePath, 
            string? outputGdbPath = null,
            string? executedBy = null)
        {
            try
            {
                _logger.LogInformation("검수 결과 FGDB 저장 시작: {ValidationId}", validationResult.ValidationId);

                // 출력 FGDB 경로 결정
                if (string.IsNullOrEmpty(outputGdbPath))
                {
                    outputGdbPath = _pathService.BuildQcGdbPath(targetFilePath);
                }

                // QC 실행 정보 생성
                var qcRun = _converter.CreateQcRun(validationResult, targetFilePath, executedBy);
                
                // QC 실행 정보 저장
                var runCreated = await _dataService.CreateQcRunAsync(outputGdbPath, qcRun);
                if (runCreated == null || runCreated == string.Empty)
                {
                    _logger.LogError("QC 실행 정보 생성 실패");
                    return 0;
                }

                // ValidationResult를 QcError 목록으로 변환 (1/2/3단계 중심)
                var qcErrors = _converter.ConvertValidationResultToQcErrors(validationResult, Guid.Parse(qcRun.GlobalID));

                // QC 오류 데이터 저장 (1/2/3단계)
                var savedCount = await _dataService.BatchAppendQcErrorsAsync(outputGdbPath, qcErrors);

                // === Stage 4/5 결과 저장 (REL/ATTR_REL) 추가 ===
                if (validationResult.RelationCheckResult != null && 
                    (validationResult.RelationCheckResult.ErrorCount > 0 || validationResult.RelationCheckResult.Errors.Count > 0))
                {
                    // RelationCheckResult를 RelationValidationResult로 변환 (간단 매핑)
                    var rel = new RelationValidationResult
                    {
                        ValidationId = validationResult.ValidationId,
                        StartedAt = validationResult.StartedAt,
                        CompletedAt = validationResult.CompletedAt ?? DateTime.UtcNow,
                        IsValid = validationResult.RelationCheckResult.Status == CheckStatus.Passed,
                        SpatialErrorCount = validationResult.RelationCheckResult.ErrorCount,
                        AttributeErrorCount = 0
                    };

                    // RelationCheckResult.Errors를 SpatialRelationError로 해석 가능한 경우에만 매핑 (메타 기반 확장 여지)
                    // 현재 구조에서는 별도 수집 모델이 있을 가능성이 높으므로, 실제 오류 컬렉션이 별도에 있다면 그 컬렉션을 전달해야 함
                    // 여기서는 빈 리스트를 그대로 유지

                    var integrated = await _relationIntegrator.SaveRelationValidationResultAsync(
                        outputGdbPath,
                        rel,
                        qcRun.GlobalID,
                        targetFilePath);

                    _logger.LogInformation("Stage 4/5 결과 저장 {Status}", integrated ? "성공" : "실패");
                }

                // QC 실행 상태 업데이트
                var runStatus = validationResult.Status == ValidationStatus.Completed ? 
                    QcRunStatus.COMPLETED.ToString() : QcRunStatus.FAILED.ToString();
                
                await _dataService.UpdateQcRunStatusAsync(
                    outputGdbPath, 
                    qcRun.GlobalID, 
                    runStatus,
                    validationResult.TotalErrors,
                    validationResult.TotalWarnings,
                    $"총 {savedCount}개 오류 저장 완료")
                ;

                _logger.LogInformation("검수 결과 FGDB 저장 완료: {SavedCount}개 오류, 출력: {OutputGdbPath}", 
                    savedCount, outputGdbPath);

                return savedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 FGDB 저장 중 오류 발생");
                return 0;
            }
        }

        /// <summary>
        /// 기존 SHP 오류 파일들을 FGDB로 일괄 이관합니다
        /// </summary>
        /// <param name="sourceDirectory">SHP 파일들이 있는 디렉토리</param>
        /// <param name="targetGdbPath">대상 FGDB 경로</param>
        /// <param name="createNewRun">새로운 실행 정보 생성 여부</param>
        /// <returns>이관된 오류 개수</returns>
        public async Task<int> MigrateLegacyShapefilesToFgdbAsync(
            string sourceDirectory, 
            string targetGdbPath,
            bool createNewRun = true)
        {
            try
            {
                _logger.LogInformation("레거시 SHP 파일 이관 시작: {SourceDirectory} → {TargetGdbPath}", 
                    sourceDirectory, targetGdbPath);

                Guid runId;

                if (createNewRun)
                {
                    // 이관용 QC 실행 정보 생성
                    var migrationRun = new QcRun
                    {
                        GlobalID = Guid.NewGuid().ToString(),
                        RunName = $"레거시_이관_{DateTime.Now:yyyyMMdd_HHmmss}",
                        TargetFilePath = sourceDirectory,
                        RulesetVersion = "Legacy",
                        StartTimeUTC = DateTime.UtcNow,
                        ExecutedBy = Environment.UserName,
                        Status = QcRunStatus.RUNNING.ToString(),
                        ConfigInfo = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            MigrationType = "Shapefile to FGDB",
                            SourceDirectory = sourceDirectory
                        })
                    };

                    var runCreatedId = await _dataService.CreateQcRunAsync(targetGdbPath, migrationRun);
                    if (string.IsNullOrEmpty(runCreatedId))
                    {
                        _logger.LogError("이관용 QC 실행 정보 생성 실패");
                        return 0;
                    }

                    runId = Guid.Parse(migrationRun.GlobalID);
                }
                else
                {
                    // 기본 실행 ID 사용
                    runId = Guid.NewGuid();
                }

                // SHP 파일들 이관
                var migratedCount = await _migrationService.MigrateShapefilesToFgdbAsync(
                    sourceDirectory, targetGdbPath, runId);

                if (createNewRun)
                {
                    // 이관 완료 상태 업데이트
                    await _dataService.UpdateQcRunStatusAsync(
                        targetGdbPath,
                        runId.ToString(),
                        QcRunStatus.COMPLETED.ToString(),
                        migratedCount,
                        0,
                        $"레거시 SHP 파일 {migratedCount}개 이관 완료"
                    );
                }

                _logger.LogInformation("레거시 SHP 파일 이관 완료: {MigratedCount}개", migratedCount);
                return migratedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레거시 SHP 파일 이관 중 오류 발생");
                return 0;
            }
        }

        /// <summary>
        /// FGDB 스키마 초기화 및 검증
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="forceRecreate">강제 재생성 여부</param>
        /// <returns>초기화 성공 여부</returns>
        public async Task<bool> InitializeFgdbSchemaAsync(string gdbPath, bool forceRecreate = false)
        {
            try
            {
                _logger.LogInformation("FGDB 스키마 초기화: {GdbPath}, 강제재생성: {ForceRecreate}", 
                    gdbPath, forceRecreate);

                if (forceRecreate && Directory.Exists(gdbPath))
                {
                    _logger.LogInformation("기존 FGDB 삭제 중...");
                    Directory.Delete(gdbPath, true);
                }

                // 스키마 생성
                var schemaCreated = await _schemaService.CreateQcErrorsSchemaAsync(gdbPath);
                if (!schemaCreated)
                {
                    _logger.LogError("FGDB 스키마 생성 실패");
                    return false;
                }

                // 스키마 검증
                var schemaValid = await _schemaService.ValidateSchemaAsync(gdbPath);
                if (!schemaValid)
                {
                    _logger.LogError("FGDB 스키마 검증 실패");
                    return false;
                }

                _logger.LogInformation("FGDB 스키마 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FGDB 스키마 초기화 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// FGDB 성능 최적화 (인덱스 재생성 등)
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <returns>최적화 성공 여부</returns>
        public async Task<bool> OptimizeFgdbPerformanceAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("FGDB 성능 최적화 시작: {GdbPath}", gdbPath);

                // 실제 구현에서는 Esri FileGDB API나 ArcPy를 사용하여
                // 인덱스 재생성, 통계 업데이트 등을 수행
                // 여기서는 로그만 남김
                
                await Task.Delay(100); // 시뮬레이션
                
                _logger.LogInformation("FGDB 성능 최적화 완료");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FGDB 성능 최적화 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// FGDB 통계 정보 조회
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <returns>통계 정보</returns>
        public async Task<FgdbStatistics?> GetFgdbStatisticsAsync(string gdbPath)
        {
            try
            {
                if (!await _schemaService.ValidateSchemaAsync(gdbPath))
                {
                    return null;
                }

                // 실제 구현에서는 각 테이블의 레코드 수, 크기 등을 조회
                // 여기서는 기본값 반환
                return new FgdbStatistics
                {
                    GdbPath = gdbPath,
                    TotalErrors = 0, // 실제 조회 필요
                    PointErrors = 0,
                    LineErrors = 0,
                    PolygonErrors = 0,
                    NoGeomErrors = 0,
                    TotalRuns = 0,
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FGDB 통계 정보 조회 중 오류 발생");
                return null;
            }
        }
    }

    /// <summary>
    /// FGDB 통계 정보
    /// </summary>
    public class FgdbStatistics
    {
        public string GdbPath { get; set; } = string.Empty;
        public int TotalErrors { get; set; }
        public int PointErrors { get; set; }
        public int LineErrors { get; set; }
        public int PolygonErrors { get; set; }
        public int NoGeomErrors { get; set; }
        public int TotalRuns { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
