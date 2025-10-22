#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Microsoft.Extensions.Logging;
using CsvHelper;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using SpatialCheckPro.Services;
using SpatialCheckPro.Processors;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 간단한 검수 서비스 (GUI 전용) - 5.2 스키마 검수 프로세서 완전 구현
    /// </summary>
    public class SimpleValidationService
    {
        private readonly ILogger<SimpleValidationService> _logger;
        private readonly CsvConfigService _csvConfigService;
        private readonly GdalDataAnalysisService _gdalService;
        private readonly GeometryValidationService _geometryService;
        private readonly SchemaValidationService _schemaService;
        private readonly QcErrorService _qcErrorService;
        private readonly IRelationCheckProcessor _relationProcessor;
        private readonly RelationErrorsIntegrator? _relationErrorsIntegrator;
        private readonly AdvancedTableCheckService? _advancedTableCheckService;
        private readonly IAttributeCheckProcessor _attributeCheckProcessor;
        private readonly SpatialCheckPro.Services.IDataSourcePool _dataSourcePool;
        private readonly ParallelProcessingManager? _parallelProcessingManager;
        private readonly AdvancedParallelProcessingManager? _advancedParallelProcessingManager;
        private readonly CentralizedResourceMonitor? _resourceMonitor;
        private readonly SpatialCheckPro.Models.Config.PerformanceSettings _performanceSettings;
        private readonly StageParallelProcessingManager? _stageParallelProcessingManager;
        private readonly ParallelPerformanceMonitor? _performanceMonitor;
        private readonly GdbToSqliteConverter _gdbToSqliteConverter;
        private readonly IServiceProvider _serviceProvider;
        private readonly ValidationResultConverter _validationResultConverter;

        /// <summary>
        /// 검수 진행률 업데이트 이벤트
        /// </summary>
        public event EventHandler<ValidationProgressEventArgs>? ProgressUpdated;

        // 설정창에서 선택된 행 전달용(의존성 주입 대신 간단 연결). UI에서 설정 후 주입해 사용
        // 기본값은 null 유지: 선택이 전달되지 않은 경우 전체 규칙 적용 의미
        internal List<TableCheckConfig>? _selectedStage1Items = null;
        internal List<SchemaCheckConfig>? _selectedStage2Items = null;
        internal List<GeometryCheckConfig>? _selectedStage3Items = null;
        internal List<AttributeCheckConfig>? _selectedStage4Items = null;
        internal List<RelationCheckConfig>? _selectedStage5Items = null;

        public SimpleValidationService(
            ILogger<SimpleValidationService> logger, CsvConfigService csvService, GdalDataAnalysisService gdalService, 
            GeometryValidationService geometryService, SchemaValidationService schemaService, QcErrorService qcErrorService, 
            AdvancedTableCheckService advancedTableCheckService, IRelationCheckProcessor relationProcessor, 
            IAttributeCheckProcessor attributeCheckProcessor, RelationErrorsIntegrator relationErrorsIntegrator, 
            IDataSourcePool dataSourcePool, IServiceProvider serviceProvider, GdbToSqliteConverter gdbToSqliteConverter, 
            ParallelProcessingManager? parallelProcessingManager, AdvancedParallelProcessingManager? advancedParallelProcessingManager, 
            CentralizedResourceMonitor? resourceMonitor, SpatialCheckPro.Models.Config.PerformanceSettings? performanceSettings, 
            StageParallelProcessingManager? stageParallelProcessingManager, ParallelPerformanceMonitor? performanceMonitor,
            ValidationResultConverter validationResultConverter)
        {
            _logger = logger;
            _csvConfigService = csvService;
            _gdalService = gdalService;
            _geometryService = geometryService;
            _schemaService = schemaService;
            _qcErrorService = qcErrorService;
            _advancedTableCheckService = advancedTableCheckService;
            _relationProcessor = relationProcessor;
            _attributeCheckProcessor = attributeCheckProcessor;
            _relationErrorsIntegrator = relationErrorsIntegrator;
            _dataSourcePool = dataSourcePool;
            _parallelProcessingManager = parallelProcessingManager;
            _advancedParallelProcessingManager = advancedParallelProcessingManager;
            _resourceMonitor = resourceMonitor;
            _performanceSettings = performanceSettings ?? new SpatialCheckPro.Models.Config.PerformanceSettings();
            _stageParallelProcessingManager = stageParallelProcessingManager;
            _performanceMonitor = performanceMonitor;
            _gdbToSqliteConverter = gdbToSqliteConverter;
            _serviceProvider = serviceProvider;
            _validationResultConverter = validationResultConverter;
            
            // 시스템 리소스 분석 및 최적화 설정 적용
            if (_resourceMonitor != null)
            {
                var resourceInfo = _resourceMonitor.GetResourceInfo("SimpleValidationService");
                ApplyOptimalSettings(resourceInfo);
            }
        }

        /// <summary>
        /// 시스템 리소스에 따른 최적화 설정 적용
        /// </summary>
        private void ApplyOptimalSettings(SystemResourceInfo resourceInfo)
        {
            _logger.LogInformation("시스템 리소스 기반 최적화 설정 적용 시작");
            
            // 병렬 처리 설정 최적화
            if (resourceInfo.SystemLoadLevel == SystemLoadLevel.High)
            {
                _performanceSettings.EnableTableParallelProcessing = false;
                _performanceSettings.MaxDegreeOfParallelism = Math.Max(1, resourceInfo.ProcessorCount / 2);
                _logger.LogInformation("고부하 시스템 감지 - 병렬 처리 제한: 병렬도 {Parallelism}", _performanceSettings.MaxDegreeOfParallelism);
            }
            else
            {
                _performanceSettings.EnableTableParallelProcessing = true;
                _performanceSettings.MaxDegreeOfParallelism = resourceInfo.RecommendedMaxParallelism;
                _logger.LogInformation("정상 부하 시스템 - 병렬 처리 활성화: 병렬도 {Parallelism}", _performanceSettings.MaxDegreeOfParallelism);
            }
            
            // 메모리 기반 배치 크기 조정
            _performanceSettings.BatchSize = resourceInfo.RecommendedBatchSize;
            _performanceSettings.MaxMemoryUsageMB = resourceInfo.RecommendedMaxMemoryUsageMB;
            
            // 스트리밍 모드 활성화 (메모리가 부족한 경우)
            if (resourceInfo.AvailableMemoryGB < 2.0)
            {
                _performanceSettings.EnableStreamingMode = true;
                _performanceSettings.StreamingBatchSize = Math.Min(500, resourceInfo.RecommendedBatchSize);
                _logger.LogInformation("메모리 부족 감지 - 스트리밍 모드 활성화: 배치크기 {BatchSize}", _performanceSettings.StreamingBatchSize);
            }
            
            _logger.LogInformation("최적화 설정 적용 완료: 병렬처리={ParallelProcessing}, 병렬도={Parallelism}, 배치크기={BatchSize}, 메모리제한={MemoryMB}MB", 
                _performanceSettings.EnableTableParallelProcessing, 
                _performanceSettings.MaxDegreeOfParallelism, 
                _performanceSettings.BatchSize, 
                _performanceSettings.MaxMemoryUsageMB);
        }

        /// <summary>
        /// UI에서 전달된 성능 설정 업데이트
        /// </summary>
        public void UpdatePerformanceSettings(bool enableParallelProcessing, int maxParallelism, int batchSize)
        {
            try
            {
                _performanceSettings.EnableTableParallelProcessing = enableParallelProcessing;
                _performanceSettings.MaxDegreeOfParallelism = Math.Max(1, Math.Min(maxParallelism, Environment.ProcessorCount * 2));
                _performanceSettings.BatchSize = Math.Max(100, Math.Min(batchSize, 50000));
                
                _logger.LogInformation("UI 설정으로 성능 설정 업데이트: 병렬처리={ParallelProcessing}, 병렬도={Parallelism}, 배치크기={BatchSize}", 
                    _performanceSettings.EnableTableParallelProcessing, 
                    _performanceSettings.MaxDegreeOfParallelism, 
                    _performanceSettings.BatchSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "성능 설정 업데이트 실패");
            }
        }

        /// <summary>
        /// 0단계: FileGDB 완전성 검수 실행
        /// - 디렉터리/확장자(.gdb) 확인 (UI에서 1차 체크되지만 재확인)
        /// - 코어 시스템 테이블 존재 여부 확인
        /// - .gdbtable ↔ .gdbtablx 페어 확인
        /// - GDAL/OGR 오픈 및 드라이버가 OpenFileGDB 인지 확인
        /// - 레이어 강제 읽기는 제외 (기존 단계에서 수행)
        /// </summary>
        private async Task<CheckResult> ExecuteFileGdbIntegrityCheckAsync(string gdbPath, System.Threading.CancellationToken cancellationToken)
        {
            var check = new CheckResult
            {
                CheckId = "FILEGDB_INTEGRITY_CHECK",
                CheckName = "FileGDB 완전성 검수",
                Status = CheckStatus.Running
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) 디렉터리 및 확장자 확인
                if (!Directory.Exists(gdbPath))
                {
                    check.Errors.Add(new ValidationError { ErrorCode = "GDB000", Message = "경로가 존재하지 않거나 디렉터리가 아닙니다." });
                }
                if (!gdbPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    check.Errors.Add(new ValidationError { ErrorCode = "GDB001", Message = "폴더명이 .gdb로 끝나지 않습니다." });
                }

                // 2) 파일 나열
                var fileNames = new HashSet<string>(Directory.EnumerateFiles(gdbPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);

                // 3) 코어 시스템 테이블 확인
                string[] coreTables =
                {
                    "a00000001.gdbtable",
                    "a00000002.gdbtable",
                    "a00000003.gdbtable",
                    "a00000004.gdbtable"
                };
                var hasCore = coreTables.All(ct => fileNames.Contains(ct));
                if (!hasCore)
                {
                    check.Errors.Add(new ValidationError { ErrorCode = "GDB020", Message = "핵심 시스템 테이블이 하나 이상 누락되었습니다 (Items/ItemTypes/Relationships/SpatialRefs)." });
                }

                // 4) .gdbtable ↔ .gdbtablx 페어 확인
                int missingPairCount = 0;
                foreach (var f in fileNames)
                {
                    if (f.EndsWith(".gdbtable", StringComparison.OrdinalIgnoreCase))
                    {
                        var pair = Path.GetFileNameWithoutExtension(f) + ".gdbtablx";
                        if (!fileNames.Contains(pair))
                        {
                            missingPairCount++;
                        }
                    }
                }
                if (missingPairCount > 0)
                {
                    check.Errors.Add(new ValidationError { ErrorCode = "GDB010", Message = $".gdbtablx 인덱스 페어가 누락된 테이블이 {missingPairCount}개 있습니다." });
                }

                // 5) OGR로 열기 및 드라이버 확인 (OpenFileGDB 고정)
                // DataSourcePool을 사용하여 성능 최적화
                var ds = _dataSourcePool.GetDataSource(gdbPath);
                try
                {
                    if (ds == null)
                    {
                        check.Errors.Add(new ValidationError { ErrorCode = "GDB030", Message = "OGR가 폴더를 FileGDB로 열지 못했습니다." });
                    }
                    else
                    {
                        var drv = ds.GetDriver();
                        var name = drv?.GetName() ?? string.Empty;
                        if (!string.Equals(name, "OpenFileGDB", StringComparison.OrdinalIgnoreCase))
                        {
                            check.Errors.Add(new ValidationError { ErrorCode = "GDB031", Message = $"예상 드라이버(OpenFileGDB)가 아닙니다: {name}" });
                        }
                    }
                }
                finally
                {
                    if (ds != null)
                    {
                        _dataSourcePool.ReturnDataSource(gdbPath, ds);
                    }
                }

                // 집계
                check.ErrorCount = check.Errors.Count;
                check.WarningCount = check.Warnings.Count;
                check.TotalCount = 4; // 시그니처, 코어, 페어, 드라이버
                check.Status = check.ErrorCount > 0 ? CheckStatus.Failed : CheckStatus.Passed;
                return await Task.FromResult(check);
            }
            catch (OperationCanceledException)
            {
                check.Status = CheckStatus.Failed;
                check.Errors.Add(new ValidationError { ErrorCode = "GDB099", Message = "검사가 취소되었습니다." });
                check.ErrorCount = check.Errors.Count;
                return check;
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Failed;
                check.Errors.Add(new ValidationError { ErrorCode = "GDB098", Message = $"예외 발생: {ex.Message}" });
                check.ErrorCount = check.Errors.Count;
                return check;
            }
        }

        /// <summary>
        /// 진행률을 보고합니다
        /// </summary>
        /// <param name="stage">현재 단계 (1-4)</param>
        /// <param name="stageName">단계명</param>
        /// <param name="stageProgress">단계 진행률 (0-100)</param>
        /// <param name="statusMessage">상태 메시지</param>
        /// <param name="isCompleted">단계 완료 여부</param>
        /// <param name="isSuccessful">단계 성공 여부</param>
        private void ReportProgress(int stage, string stageName, double stageProgress, string statusMessage, bool isCompleted = false, bool isSuccessful = true)
        {
            // 전체 진행률 계산: 각 단계는 20%씩 차지(0~5단계)
            // 0단계는 사전 점검 단계로 0~20% 구간을 사용
            var clampedStage = Math.Max(0, Math.Min(5, stage));
            var clampedStageProgress = Math.Max(0, Math.Min(100, stageProgress));

            double overallProgress;
            if (clampedStage == 0)
            {
                overallProgress = clampedStageProgress * 0.20;
                if (isCompleted) overallProgress = 20.0;
            }
            else
            {
                overallProgress = ((clampedStage - 1) * 20.0) + (clampedStageProgress * 0.20);
                if (isCompleted) overallProgress = clampedStage * 20.0;
            }

            var args = new ValidationProgressEventArgs
            {
                CurrentStage = stage,
                StageName = stageName,
                OverallProgress = overallProgress,
                StageProgress = stageProgress,
                StatusMessage = statusMessage,
                IsStageCompleted = isCompleted,
                IsStageSuccessful = isSuccessful,
                IsStageSkipped = false
            };

            ProgressUpdated?.Invoke(this, args);
            _logger.LogInformation("진행률 업데이트: {Stage}단계 {StageName} - 전체 {OverallProgress:F1}%, 단계 {StageProgress:F1}% - {Status}", 
                stage, stageName, args.OverallProgress, stageProgress, statusMessage);
        }
        
        /// <summary>
        /// 단위 기반 진행률을 보고합니다
        /// </summary>
        /// <param name="stage">현재 단계 (0~5)</param>
        /// <param name="stageName">단계 이름</param>
        /// <param name="stageProgress">단계별 진행률 (0~100)</param>
        /// <param name="statusMessage">상태 메시지</param>
        /// <param name="processedUnits">처리된 단위 수</param>
        /// <param name="totalUnits">전체 단위 수</param>
        /// <param name="isCompleted">단계 완료 여부</param>
        /// <param name="isSuccessful">단계 성공 여부</param>
        private void ReportProgressWithUnits(int stage, string stageName, double stageProgress, string statusMessage, long processedUnits, long totalUnits, bool isCompleted = false, bool isSuccessful = true)
        {
            // 전체 진행률 계산: 각 단계는 20%씩 차지(0~5단계)
            var clampedStage = Math.Max(0, Math.Min(5, stage));
            var clampedStageProgress = Math.Max(0, Math.Min(100, stageProgress));

            double overallProgress;
            if (clampedStage == 0)
            {
                overallProgress = clampedStageProgress * 0.20;
                if (isCompleted) overallProgress = 20.0;
            }
            else
            {
                overallProgress = ((clampedStage - 1) * 20.0) + (clampedStageProgress * 0.20);
                if (isCompleted) overallProgress = clampedStage * 20.0;
            }

            var args = new ValidationProgressEventArgs
            {
                CurrentStage = stage,
                StageName = stageName,
                OverallProgress = overallProgress,
                StageProgress = stageProgress,
                StatusMessage = statusMessage,
                IsStageCompleted = isCompleted,
                IsStageSuccessful = isSuccessful,
                IsStageSkipped = false,
                ProcessedUnits = processedUnits,
                TotalUnits = totalUnits
            };

            ProgressUpdated?.Invoke(this, args);
            _logger.LogInformation("진행률 업데이트(단위): {Stage}단계 {StageName} - 전체 {OverallProgress:F1}%, 단계 {StageProgress:F1}% ({ProcessedUnits}/{TotalUnits}) - {Status}", 
                stage, stageName, args.OverallProgress, stageProgress, processedUnits, totalUnits, statusMessage);
        }

        /// <summary>
        /// 검수를 실행합니다
        /// </summary>
        public async Task<ValidationResult> ValidateAsync(string filePath)
        {
            return await ValidateAsync(filePath, null, null, null, null, null, null, false, System.Threading.CancellationToken.None);
        }

        /// <summary>
        /// 설정 파일 경로를 지정하여 검수를 실행합니다
        /// </summary>
        public async Task<ValidationResult> ValidateAsync(string filePath, 
            string? tableConfigPath, 
            string? schemaConfigPath, 
            string? geometryConfigPath, 
            string? relationConfigPath,
            string? attributeConfigPath)
        {  
            return await ValidateAsync(filePath, tableConfigPath, schemaConfigPath, geometryConfigPath, relationConfigPath, attributeConfigPath, null, false, System.Threading.CancellationToken.None);
        }

        /// <summary>
        /// 설정 파일 경로와 취소 토큰을 지정하여 검수를 실행합니다
        /// </summary>
        public async Task<ValidationResult> ValidateAsync(string filePath,
            string? tableConfigPath,
            string? schemaConfigPath,
            string? geometryConfigPath,
            string? relationConfigPath,
            string? attributeConfigPath,
            string? codelistPath,
            System.Threading.CancellationToken cancellationToken)
        {
            // 이 메서드는 이제 useHighPerformanceMode=false로 새 기본 메서드를 호출합니다.
            return await ValidateAsync(filePath, tableConfigPath, schemaConfigPath, geometryConfigPath, relationConfigPath, attributeConfigPath, codelistPath, false, cancellationToken);
        }


        // ValidateAsync 오버로드들을 하나로 통합 (새로운 기본 진입점)
        public async Task<ValidationResult> ValidateAsync(string filePath,
            string? tableConfigPath, string? schemaConfigPath, string? geometryConfigPath,
            string? relationConfigPath, string? attributeConfigPath, string? codelistPath,
            bool useHighPerformanceMode, System.Threading.CancellationToken cancellationToken)
        {
            // 경로 정규화: 끝의 백슬래시/슬래시 제거
            filePath = filePath.TrimEnd('\\', '/');
            
            var totalStopwatch = Stopwatch.StartNew(); // 전체 소요시간 측정을 위한 Stopwatch 시작
            _logger.LogInformation("검수 시작: {FilePath}, 고성능 모드: {UseHPMode}", filePath, useHighPerformanceMode);
            
            _performanceMonitor?.StartOperation("전체 검수", "검수 프로세스 전체 실행", 1000);

            var result = new ValidationResult
            {
                ValidationId = Guid.NewGuid().ToString(), // 이 ID는 이제 내부 식별용
                TargetFile = filePath,
                StartedAt = DateTime.Now,
                Status = ValidationStatus.Running
            };

            string? qcGdbPath = null;
            string? runId = null;
            
            string validationDataSourcePath = filePath;
            IValidationDataProvider? dataProvider = null;
            string? tempSqliteFile = null;

            try
            {
                // ===== QC 시스템 초기화 (새로운 흐름) =====
                var runInfo = new QcRun 
                { 
                    RunName = $"Spatial QC - {Path.GetFileName(filePath)}",
                    TargetFilePath = filePath, 
                    RulesetVersion = "1.0.0", // 필요시 동적으로 설정
                    ExecutedBy = Environment.UserName
                };
                (qcGdbPath, runId) = await _qcErrorService.BeginRunAsync(runInfo, filePath);
                result.ValidationId = runId; // UI 등에서 사용할 ID를 RunID로 통일
                // ===========================================

                cancellationToken.ThrowIfCancellationRequested();

                // 자동 고성능 모드 판단 (UI 토글 없이 파일 크기/피처 수 기준)
                if (_performanceSettings.EnableAutoHighPerformanceMode)
                {
                    try
                    {
                        var autoDecision = await ShouldEnableHighPerformanceModeAsync(filePath, cancellationToken);
                        if (autoDecision)
                        {
                            useHighPerformanceMode = true;
                            _logger.LogInformation("자동 고성능 모드 활성화: 기준 충족");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "자동 고성능 모드 판단 중 경고");
                    }
                }

                if (useHighPerformanceMode)
                {
                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        tempSqliteFile = await _gdbToSqliteConverter.ConvertAsync(filePath);
                        validationDataSourcePath = tempSqliteFile;
                        stopwatch.Stop();
                        _logger.LogInformation("GDB -> SQLite 변환 완료: {Duration} 소요", stopwatch.Elapsed);
                        dataProvider = _serviceProvider.GetRequiredService<SqliteDataProvider>();
                    }
                    catch (Exception ex)
                    {
                        // 변환 실패 시 폴백: GDB 직접 모드
                        _logger.LogWarning(ex, "GDB -> SQLite 변환 실패, GDB 직접 모드로 폴백합니다.");
                        useHighPerformanceMode = false;
                        validationDataSourcePath = filePath;
                        dataProvider = _serviceProvider.GetRequiredService<GdbDataProvider>();
                    }
                }
                else
                {
                    dataProvider = _serviceProvider.GetRequiredService<GdbDataProvider>();
                }

                await dataProvider.InitializeAsync(validationDataSourcePath);

                // QC_ERRORS 시스템 초기화 로직은 BeginRunAsync로 이동되었으므로 제거
                // _logger.LogInformation("QC_ERRORS 시스템 초기화 시작: {FilePath}", filePath);
                // ... (기존 초기화 코드 삭제)

                var configDirectory = GetDefaultConfigDirectory();
                var actualTableConfigPath = tableConfigPath ?? Path.Combine(configDirectory, "1_table_check.csv");
                var actualSchemaConfigPath = schemaConfigPath ?? Path.Combine(configDirectory, "2_schema_check.csv");
                var actualGeometryConfigPath = geometryConfigPath ?? Path.Combine(configDirectory, "3_geometry_check.csv");
                var actualAttributeConfigPath = attributeConfigPath ?? Path.Combine(configDirectory, "4_attribute_check.csv");
                var actualRelationConfigPath = relationConfigPath ?? Path.Combine(configDirectory, "5_relation_check.csv");

                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    throw new FileNotFoundException($"검수 대상 파일을 찾을 수 없습니다: {filePath}");
                }

                if (Directory.Exists(filePath) && filePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    // 0단계: FileGDB 완전성 검수
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress(0, "FileGDB 완전성 검수", 0, "FileGDB 사전 점검을 시작합니다...");

                    // 여기서는 qcGdbPath가 아닌 원본 filePath를 사용해야 합니다.
                    var fgdbCheck = await ExecuteFileGdbIntegrityCheckAsync(filePath, cancellationToken);
                    result.FileGdbCheckResult = fgdbCheck;
                    result.ErrorCount += fgdbCheck.ErrorCount;
                    result.WarningCount += fgdbCheck.WarningCount;

                    if (fgdbCheck.Status == CheckStatus.Failed)
                    {
                        throw new Exception("정상적인 File Geodatabase가 아니므로 검수를 중단합니다.");
                    }
                        ReportProgress(0, "FileGDB 완전성 검수", 100, "FileGDB 완전성 검수 완료", true, true);
                }

                // 단계별 처리 실행
                if (_stageParallelProcessingManager != null && _performanceSettings.EnableStageParallelProcessing)
                {
                    _logger.LogInformation("=== 단계별 병렬 처리 모드로 실행 ===");
                    await ExecuteStagesInParallelAsync(filePath, validationDataSourcePath, dataProvider, result, actualTableConfigPath, actualSchemaConfigPath, 
                        actualGeometryConfigPath, actualAttributeConfigPath, actualRelationConfigPath, codelistPath, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("=== 순차 처리 모드로 실행 ===");
                    await ExecuteStagesSequentiallyAsync(filePath, validationDataSourcePath, dataProvider, result, actualTableConfigPath, actualSchemaConfigPath, 
                        actualGeometryConfigPath, actualAttributeConfigPath, actualRelationConfigPath, codelistPath, cancellationToken);
                }

                result.IsValid = result.ErrorCount == 0;
                result.Status = ValidationStatus.Completed;
                result.Message = result.IsValid ? "모든 검수 단계가 성공적으로 완료되었습니다!" : $"검수 완료: {result.ErrorCount}개 오류, {result.WarningCount}개 경고";

                // EndRun 호출을 finally 블록으로 이동
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("검사가 사용자에 의해 취소되었습니다.");
                result.Status = ValidationStatus.Cancelled;
                result.Message = "검사가 취소되었습니다.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 중 심각한 오류 발생");
                result.ErrorCount++;
                result.IsValid = false;
                result.Status = ValidationStatus.Failed;
                result.Message = $"검수 중 오류 발생: {ex.Message}";
            }
            finally
            {
                dataProvider?.Close();
                if (tempSqliteFile != null && File.Exists(tempSqliteFile))
                {
                    try
                    {
                        File.Delete(tempSqliteFile);
                        _logger.LogInformation("임시 SQLite 파일 삭제: {Path}", tempSqliteFile);
                    }
                    catch(Exception ex)
                    {
                        _logger.LogWarning(ex, "임시 SQLite 파일 삭제 실패: {Path}", tempSqliteFile);
                    }
                }

                totalStopwatch.Stop();
                result.CompletedAt = DateTime.Now;
                result.ProcessingTime = totalStopwatch.Elapsed;
                _performanceMonitor?.CompleteOperation("전체 검수", result.ErrorCount == 0);
                
                // ===== QC Run 상태 업데이트 및 오류 저장 (새로운 흐름) =====
                if (runId != null && qcGdbPath != null)
                {
                    // 1. 검수 결과를 QcError 목록으로 변환
                    var qcErrors = _validationResultConverter.ConvertValidationResultToQcErrors(result, Guid.Parse(runId));
                    _logger.LogInformation("{ErrorCount}개의 오류를 QC 포맷으로 변환했습니다.", qcErrors.Count);

                    // 2. 변환된 오류를 QC GDB에 일괄 저장
                    if (qcErrors.Any())
                    {
                        var dataService = _serviceProvider.GetRequiredService<QcErrorDataService>();
                        await dataService.BatchAppendQcErrorsAsync(qcGdbPath, qcErrors);
                        _logger.LogInformation("{ErrorCount}개의 QC 오류를 GDB에 저장했습니다: {QcGdbPath}", qcErrors.Count, qcGdbPath);
                    }

                    // 3. QC_Runs 테이블에 최종 결과 업데이트
                    await _qcErrorService.EndRunAsync(result.ErrorCount, result.WarningCount, result.Message, result.Status == ValidationStatus.Completed && result.IsValid);
                }
                // ============================================
                
                _logger.LogInformation("=== 검수 완료 ===");
                _logger.LogInformation("총 소요시간: {ElapsedTime}", result.ProcessingTime);
                _logger.LogInformation("검수 결과: {IsValid}, 상태: {Status}, 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    result.IsValid, result.Status, result.ErrorCount, result.WarningCount);
            }

            return result;
        }

        /// <summary>
        /// 파일 크기와 총 피처 수를 기준으로 고성능 모드 활성화 필요 여부를 판단합니다
        /// </summary>
        private async Task<bool> ShouldEnableHighPerformanceModeAsync(string filePath, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // 파일 크기 기준 체크 (폴더형 GDB의 경우 폴더 내 파일들의 총합)
                long totalSize = 0;
                if (Directory.Exists(filePath))
                {
                    foreach (var f in Directory.EnumerateFiles(filePath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { totalSize += new FileInfo(f).Length; } catch { }
                    }
                }
                else if (File.Exists(filePath))
                {
                    totalSize = new FileInfo(filePath).Length;
                }

                if (totalSize >= _performanceSettings.HighPerformanceModeSizeThresholdBytes)
                {
                    _logger.LogInformation("자동 HP 판단: 파일 크기 임계 초과({Size} bytes)", totalSize);
                    return true;
                }

                // 총 피처 수 기준 체크 (GDAL로 빠른 카운트)
                long totalFeatures = 0;
                var ds = _dataSourcePool.GetDataSource(filePath);
                try
                {
                    if (ds != null)
                    {
                        for (int i = 0; i < ds.GetLayerCount(); i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using var layer = ds.GetLayerByIndex(i);
                            if (layer != null)
                            {
                                totalFeatures += layer.GetFeatureCount(1);
                                if (totalFeatures >= _performanceSettings.HighPerformanceModeFeatureThreshold)
                                {
                                    _logger.LogInformation("자동 HP 판단: 피처 수 임계 초과({Count}개)", totalFeatures);
                                    return true;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (ds != null) _dataSourcePool.ReturnDataSource(filePath, ds);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "자동 고성능 모드 판단 실패 - 기본값(비활성화) 적용");
                return false;
            }
        }

        /// <summary>
        /// 검수 완료 후 QC_ERRORS 자동 확인 및 알림
        /// </summary>
        private async Task VerifyQcErrorsAfterValidationAsync(string filePath, string validationId)
        {
            try
            {
                // 전체 QC_ERRORS 개수 확인
                var allErrors = await _qcErrorService.GetQcErrorsAsync(filePath, null);
                _logger.LogInformation("검수 완료 후 전체 QC_ERRORS 개수: {Count}개", allErrors.Count);
                
                // 현재 검수 RunID로 필터링된 QC_ERRORS 개수 확인
                var currentRunErrors = await _qcErrorService.GetQcErrorsAsync(filePath, validationId);
                _logger.LogInformation("현재 검수 RunID({RunId}) QC_ERRORS 개수: {Count}개", validationId, currentRunErrors.Count);
                
                if (currentRunErrors.Count > 0)
                {
                    _logger.LogInformation("QC_ERRORS 저장 완료: {Count}개 오류가 검수 대상 FileGDB에 저장되었습니다", currentRunErrors.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 확인 중 오류 발생");
            }
        }

        /// <summary>
        /// 기본 설정 디렉토리 경로를 반환합니다
        /// </summary>
        private string GetDefaultConfigDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        }

        /// <summary>
        /// 단계별 병렬 처리 실행
        /// </summary>
        private async Task ExecuteStagesInParallelAsync(string originalGdbPath, string dataSourcePath, IValidationDataProvider dataProvider, ValidationResult result, 
            string tableConfigPath, string schemaConfigPath, string geometryConfigPath, 
            string attributeConfigPath, string relationConfigPath, string? codelistPath, 
            System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // 독립적인 단계들 (병렬 실행 가능)
                var independentStages = new Dictionary<int, Func<Task<object>>>
                {
                    [0] = async () => {
                        ReportProgress(0, "FileGDB 완전성 검수", 0, "FileGDB 사전 점검을 시작합니다...");
                        var check = await ExecuteFileGdbIntegrityCheckAsync(originalGdbPath, cancellationToken);
                        ReportProgress(0, "FileGDB 완전성 검수", 100, "FileGDB 완전성 검수 완료", true, true);
                        return check;
                    },
                    [1] = async () => {
                        ReportProgress(1, "테이블 검수", 0, "테이블 검수를 시작합니다...");
                        var tableResult = await ExecuteTableCheckAsync(dataSourcePath, dataProvider, tableConfigPath, _selectedStage1Items);
                        ReportProgress(1, "테이블 검수", 100, "테이블 검수 완료", true, true);
                        return tableResult;
                    },
                    [4] = async () => {
                        ReportProgress(4, "속성 관계 검수", 0, "속성 관계 검수를 시작합니다...");
                        var attrResult = await ExecuteAttributeRelationCheckAsync(dataSourcePath, dataProvider, attributeConfigPath, codelistPath);
                        ReportProgress(4, "속성 관계 검수", 100, "속성 관계 검수 완료", true, true);
                        return attrResult;
                    },
                    [5] = async () => {
                        ReportProgress(5, "공간 관계 검수", 0, "공간 관계 검수를 시작합니다...");
                        var relationResult = await ExecuteRelationCheckAsync(dataSourcePath, dataProvider, relationConfigPath, _selectedStage5Items);
                        ReportProgress(5, "공간 관계 검수", 100, "공간 관계 검수 완료", true, true);
                        return relationResult;
                    }
                };

                // 의존적인 단계들 (이전 단계 결과 필요)
                var dependentStages = new Dictionary<int, Func<object, Task<object>>>
                {
                    [2] = async (tableResult) => {
                        ReportProgress(2, "스키마 검수", 0, "스키마 검수를 시작합니다...");
                        var tableCheckResult = (TableCheckResult)tableResult;
                        var schemaResult = await ExecuteSchemaCheckAsync(originalGdbPath, dataProvider, schemaConfigPath, tableCheckResult.TableResults, _selectedStage2Items);
                        ReportProgress(2, "스키마 검수", 100, "스키마 검수 완료", true, true);
                        return schemaResult;
                    },
                    [3] = async (previousStageResult) => {
                        ReportProgress(3, "지오메트리 검수", 0, "지오메트리 검수를 시작합니다...");

                        List<TableValidationItem> targetTables;

                        if (previousStageResult is SchemaCheckResult schemaCheckResult && schemaCheckResult.SchemaResults.Any())
                        {
                            targetTables = schemaCheckResult.SchemaResults
                                .Select(s => new TableValidationItem
                                {
                                    TableId = s.TableId,
                                    TableName = s.TableId
                                })
                                .DistinctBy(t => t.TableId)
                                .ToList();
                        }
                        else if (previousStageResult is TableCheckResult tableCheckResult)
                        {
                            targetTables = tableCheckResult.TableResults.ToList();
                        }
                        else
                        {
                            throw new InvalidOperationException("지오메트리 검수를 실행하려면 스키마 또는 테이블 검수 결과가 필요합니다.");
                        }

                        var geometryResult = await ExecuteGeometryCheckAsync(originalGdbPath, dataSourcePath, dataProvider, geometryConfigPath,
                            targetTables,
                            _selectedStage3Items);
                        ReportProgress(3, "지오메트리 검수", 100, "지오메트리 검수 완료", true, true);
                        return geometryResult;
                    }
                };

                // StageParallelProcessingManager를 사용하여 병렬 실행
                var enabledStages = new bool[] { true, true, true, true, true, true }; // 모든 단계 활성화
                var stageResults = await _stageParallelProcessingManager.ExecuteStagesInParallelAsync(
                    independentStages[0], independentStages[1], dependentStages[2], dependentStages[3], 
                    independentStages[4], independentStages[5], enabledStages);

                // 결과 처리
                if (stageResults.Stage0Result != null)
                {
                    result.FileGdbCheckResult = (CheckResult)stageResults.Stage0Result;
                    result.ErrorCount += ((CheckResult)stageResults.Stage0Result).ErrorCount;
                    result.WarningCount += ((CheckResult)stageResults.Stage0Result).WarningCount;
                }
                if (stageResults.Stage1Result != null)
                {
                    result.TableCheckResult = (TableCheckResult)stageResults.Stage1Result;
                    result.ErrorCount += ((TableCheckResult)stageResults.Stage1Result).ErrorCount;
                    result.WarningCount += ((TableCheckResult)stageResults.Stage1Result).WarningCount;
                }
                if (stageResults.Stage2Result != null)
                {
                    result.SchemaCheckResult = (SchemaCheckResult)stageResults.Stage2Result;
                    result.ErrorCount += ((SchemaCheckResult)stageResults.Stage2Result).ErrorCount;
                    result.WarningCount += ((SchemaCheckResult)stageResults.Stage2Result).WarningCount;
                }
                if (stageResults.Stage3Result != null)
                {
                    result.GeometryCheckResult = (GeometryCheckResult)stageResults.Stage3Result;
                    result.ErrorCount += ((GeometryCheckResult)stageResults.Stage3Result).ErrorCount;
                    result.WarningCount += ((GeometryCheckResult)stageResults.Stage3Result).WarningCount;
                }
                if (stageResults.Stage4Result != null)
                {
                    result.AttributeRelationCheckResult = (AttributeRelationCheckResult)stageResults.Stage4Result;
                    result.ErrorCount += ((AttributeRelationCheckResult)stageResults.Stage4Result).ErrorCount;
                    result.WarningCount += ((AttributeRelationCheckResult)stageResults.Stage4Result).WarningCount;
                }
                if (stageResults.Stage5Result != null)
                {
                    result.RelationCheckResult = (RelationCheckResult)stageResults.Stage5Result;
                    result.ErrorCount += ((RelationCheckResult)stageResults.Stage5Result).ErrorCount;
                    result.WarningCount += ((RelationCheckResult)stageResults.Stage5Result).WarningCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "단계별 병렬 처리 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 단계별 순차 처리 실행
        /// </summary>
        private async Task ExecuteStagesSequentiallyAsync(string originalGdbPath, string dataSourcePath, IValidationDataProvider dataProvider, ValidationResult result, 
            string tableConfigPath, string schemaConfigPath, string geometryConfigPath, 
            string attributeConfigPath, string relationConfigPath, string? codelistPath, 
            System.Threading.CancellationToken cancellationToken)
        {
            // 1단계: 테이블 검수
            if (ShouldRunStage(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(1, "테이블 검수", 0, "테이블 검수를 시작합니다...");
                _logger.LogInformation("1단계: 테이블 검수 시작");
                _logger.LogInformation("테이블 설정 파일: {ConfigPath}", tableConfigPath);

                // 성능 모니터링 시작
                _performanceMonitor?.StartOperation("테이블 검수", "1단계 테이블 검수 실행", 100);

                var tableResult = await ExecuteTableCheckAsync(dataSourcePath, dataProvider, tableConfigPath, _selectedStage1Items);
                
                // 성능 모니터링 완료
                _performanceMonitor?.CompleteOperation("테이블 검수", tableResult.ErrorCount == 0);
                result.TableCheckResult = tableResult;
                result.ErrorCount += tableResult.ErrorCount;
                result.WarningCount += tableResult.WarningCount;

                ReportProgress(1, "테이블 검수", 100, "테이블 검수 완료", true, true);
                _logger.LogInformation("1단계: 테이블 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    tableResult.ErrorCount, tableResult.WarningCount);
            }

            // 2단계: 스키마 검수
            if (ShouldRunStage(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(2, "스키마 검수", 0, "스키마 검수를 시작합니다...");
                _logger.LogInformation("2단계: 스키마 검수 시작");

                // 성능 모니터링 시작
                _performanceMonitor?.StartOperation("스키마 검수", "2단계 스키마 검수 실행", 200);

                var schemaResult = await ExecuteSchemaCheckAsync(originalGdbPath, dataProvider, schemaConfigPath, 
                    result.TableCheckResult?.TableResults ?? new List<TableValidationItem>(), _selectedStage2Items);
                
                // 성능 모니터링 완료
                _performanceMonitor?.CompleteOperation("스키마 검수", schemaResult.ErrorCount == 0);
                result.SchemaCheckResult = schemaResult;
                result.ErrorCount += schemaResult.ErrorCount;
                result.WarningCount += schemaResult.WarningCount;

                ReportProgress(2, "스키마 검수", 100, "스키마 검수 완료", true, true);
                _logger.LogInformation("2단계: 스키마 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    schemaResult.ErrorCount, schemaResult.WarningCount);
            }

            // 3단계: 지오메트리 검수
            if (ShouldRunStage(3))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(3, "지오메트리 검수", 0, "지오메트리 검수를 시작합니다...");
                _logger.LogInformation("3단계: 지오메트리 검수 시작");

                // 성능 모니터링 시작
                _performanceMonitor?.StartOperation("지오메트리 검수", "3단계 지오메트리 검수 실행", 500);

                var geometryTargetTables = result.SchemaCheckResult != null && result.SchemaCheckResult.SchemaResults.Any()
                    ? result.SchemaCheckResult.SchemaResults
                        .Select(s => new TableValidationItem { TableId = s.TableId, TableName = s.TableId })
                        .DistinctBy(t => t.TableId)
                        .ToList()
                    : result.TableCheckResult?.TableResults?.ToList() ?? new List<TableValidationItem>();

                var geometryResult = await ExecuteGeometryCheckAsync(originalGdbPath, dataSourcePath, dataProvider, geometryConfigPath, 
                    geometryTargetTables, _selectedStage3Items);
                
                // 성능 모니터링 완료
                _performanceMonitor?.CompleteOperation("지오메트리 검수", geometryResult.ErrorCount == 0);
                result.GeometryCheckResult = geometryResult;
                result.ErrorCount += geometryResult.ErrorCount;
                result.WarningCount += geometryResult.WarningCount;

                ReportProgress(3, "지오메트리 검수", 100, "지오메트리 검수 완료", true, true);
                _logger.LogInformation("3단계: 지오메트리 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    geometryResult.ErrorCount, geometryResult.WarningCount);

                // 지오메트리 검수 완료 후 QC_ERRORS 확인
                var qcErrors = await _qcErrorService.GetQcErrorsAsync(originalGdbPath, result.ValidationId);
                _logger.LogInformation("저장된 QC_ERRORS 개수: {Count}개", qcErrors.Count);
            }

            // 4단계: 속성 관계 검수
            if (ShouldRunStage(4))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(4, "속성 관계 검수", 0, "속성 관계 검수를 시작합니다...");
                _logger.LogInformation("4단계: 속성 관계 검수 시작");

                // 성능 모니터링 시작
                _performanceMonitor?.StartOperation("속성 관계 검수", "4단계 속성 관계 검수 실행", 300);

                var attributeResult = await ExecuteAttributeRelationCheckAsync(dataSourcePath, dataProvider, attributeConfigPath, codelistPath);
                
                // 성능 모니터링 완료
                _performanceMonitor?.CompleteOperation("속성 관계 검수", attributeResult.ErrorCount == 0);
                result.AttributeRelationCheckResult = attributeResult;
                result.ErrorCount += attributeResult.ErrorCount;
                result.WarningCount += attributeResult.WarningCount;

                ReportProgress(4, "속성 관계 검수", 100, "속성 관계 검수 완료", true, true);
                _logger.LogInformation("4단계: 속성 관계 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    attributeResult.ErrorCount, attributeResult.WarningCount);
            }

            // 5단계: 공간 관계 검수
            if (ShouldRunStage(5))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(5, "공간 관계 검수", 0, "공간 관계 검수를 시작합니다...");
                _logger.LogInformation("5단계: 공간 관계 검수 시작");

                // 성능 모니터링 시작
                _performanceMonitor?.StartOperation("공간 관계 검수", "5단계 공간 관계 검수 실행", 400);

                var relationResult = await ExecuteRelationCheckAsync(dataSourcePath, dataProvider, relationConfigPath, _selectedStage5Items);
                
                // 성능 모니터링 완료
                _performanceMonitor?.CompleteOperation("공간 관계 검수", relationResult.ErrorCount == 0);
                result.RelationCheckResult = relationResult;
                result.ErrorCount += relationResult.ErrorCount;
                result.WarningCount += relationResult.WarningCount;

                ReportProgress(5, "공간 관계 검수", 100, "공간 관계 검수 완료", true, true);
                _logger.LogInformation("5단계: 공간 관계 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    relationResult.ErrorCount, relationResult.WarningCount);
            }
        }

        /// <summary>
        /// 단계 실행 여부를 결정합니다. MainWindow에서 설정한 플래그를 조회합니다.
        /// </summary>
        private bool ShouldRunStage(int stage)
        {
            try
            {
                var app = System.Windows.Application.Current as SpatialCheckPro.GUI.App;
                var main = System.Windows.Application.Current?.MainWindow as SpatialCheckPro.GUI.MainWindow;
                if (main == null) return true; // 기본: 실행

                return stage switch
                {
                    1 => (bool)main.GetType().GetField("_enableStage1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!,
                    2 => (bool)main.GetType().GetField("_enableStage2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!,
                    3 => (bool)main.GetType().GetField("_enableStage3", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!,
                    4 => (bool)main.GetType().GetField("_enableStage4", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!,
                    5 => (bool)main.GetType().GetField("_enableStage5", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!,
                    _ => true
                };
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 1단계 테이블 검수를 실행합니다
        /// </summary>
        private async Task<TableCheckResult> ExecuteTableCheckAsync(string dataSourcePath, IValidationDataProvider dataProvider, string tableConfigPath, List<SpatialCheckPro.Models.Config.TableCheckConfig>? selectedRows = null)
        {
            var result = new TableCheckResult
            {
                StartedAt = DateTime.Now,
                IsValid = true,
                ErrorCount = 0,
                WarningCount = 0,
                Message = "테이블 검수 완료",
                TableResults = new List<TableValidationItem>()
            };

            try
            {
                _logger.LogInformation("1단계 테이블 검수 시작: {ConfigPath}", tableConfigPath);

                // 설정 파일 존재 확인 및 로드
                if (!File.Exists(tableConfigPath))
                {
                    result.WarningCount++;
                    result.Message = "테이블 설정 파일이 없어 테이블 검수를 스킵했습니다.";
                    _logger.LogWarning("테이블 설정 파일을 찾을 수 없습니다: {Path}", tableConfigPath);
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 테이블 설정 로드
                var tableConfigs = await LoadTableConfigsAsync(tableConfigPath);
                if (!tableConfigs.Any())
                {
                    result.WarningCount++;
                    result.Message = "테이블 설정이 없어 테이블 검수를 스킵했습니다.";
                    _logger.LogWarning("테이블 설정이 비어있습니다: {Path}", tableConfigPath);
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 선택된 행이 있으면 필터링
                if (selectedRows != null && selectedRows.Any())
                {
                    var selectedTableIds = selectedRows.Select(r => r.TableId).ToHashSet();
                    tableConfigs = tableConfigs.Where(c => selectedTableIds.Contains(c.TableId)).ToList();
                    _logger.LogInformation("선택된 테이블만 검수: {Count}개", tableConfigs.Count);
                }

                _logger.LogInformation("테이블 검수 대상: {Count}개", tableConfigs.Count);

                // 고급 테이블 검수 서비스 사용
                if (_advancedTableCheckService != null)
                {
                    var advancedResult = await _advancedTableCheckService.PerformAdvancedTableCheckAsync(dataSourcePath, dataProvider, tableConfigs);
                    // AdvancedTableCheckResult를 TableCheckResult로 변환
                    result.TableResults = advancedResult.TableItems.Select(ti => new TableValidationItem
                    {
                        TableId = ti.TableId,
                        TableName = ti.TableName,
                        FeatureCount = ti.FeatureCount,
                        FeatureType = ti.ActualFeatureType,
                        FeatureTypeCheck = ti.FeatureTypeCheck,
                        TableExistsCheck = ti.TableExistsCheck,
                        ExpectedFeatureType = ti.ExpectedFeatureType,
                        ActualFeatureType = ti.ActualFeatureType,
                        ActualFeatureClassName = ti.ActualFeatureClassName,
                        CoordinateSystem = ti.ExpectedCoordinateSystem,
                        Status = ti.TableExistsCheck == "Y" ? "통과" : "오류",
                        Errors = ti.TableExistsCheck == "N" ? new List<string> { "테이블이 존재하지 않습니다" } : new List<string>(),
                        Warnings = new List<string>()
                    }).ToList();
                result.ErrorCount = advancedResult.ErrorCount;
                result.WarningCount = advancedResult.WarningCount;
                result.IsValid = result.ErrorCount == 0;
                
                // 통계 설정
                result.TotalTableCount = result.TableResults.Count;
                result.ProcessedTableCount = result.TableResults.Count(t => t.TableExistsCheck == "Y" && t.FeatureCount > 0);
                result.SkippedTableCount = result.TableResults.Count(t => t.TableExistsCheck == "N" || t.FeatureCount == 0);
                
                _logger.LogInformation("1단계 통계: 전체 {Total}개, 처리 {Processed}개, 스킵 {Skipped}개", 
                    result.TotalTableCount, result.ProcessedTableCount, result.SkippedTableCount);
                }
                else
                {
                    _logger.LogWarning("고급 테이블 검수 서비스가 없어 기본 검수를 수행합니다");
                    // 기본 검수 로직 (간단한 버전)
                    foreach (var config in tableConfigs)
                    {
                        var tableItem = new TableValidationItem
                        {
                            TableId = config.TableId,
                            TableName = config.TableName,
                            TableExistsCheck = "Y",
                            FeatureCount = 0
                        };
                        result.TableResults.Add(tableItem);
                    }
                }

                result.Message = result.IsValid ? "테이블 검수 완료" : $"테이블 검수 완료: 오류 {result.ErrorCount}개";
                result.CompletedAt = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 검수 중 오류 발생");
                result.ErrorCount = 1;
                result.IsValid = false;
                result.Message = $"테이블 검수 중 오류 발생: {ex.Message}";
                result.CompletedAt = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// 테이블 설정을 로드합니다
        /// </summary>
        private async Task<List<SpatialCheckPro.Models.Config.TableCheckConfig>> LoadTableConfigsAsync(string configPath)
        {
            return await Task.Run(() =>
            {
                var configs = new List<SpatialCheckPro.Models.Config.TableCheckConfig>();
                
                try
                {
                    using var reader = new StreamReader(configPath);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    configs = csv.GetRecords<SpatialCheckPro.Models.Config.TableCheckConfig>().ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "테이블 설정 로드 중 오류 발생: {Path}", configPath);
                }
                
                return configs;
            });
        }

        /// <summary>
        /// 2단계 스키마 검수를 실행합니다
        /// </summary>
        private async Task<SchemaCheckResult> ExecuteSchemaCheckAsync(string gdbPath, IValidationDataProvider dataProvider, string schemaConfigPath, List<TableValidationItem> validTables, List<SpatialCheckPro.Models.Config.SchemaCheckConfig>? selectedRows = null)
        {
            // 2단계는 실제 스키마 추출/비교를 수행하도록 GUI의 SchemaValidationService를 호출한다
            try
            {
                _logger.LogInformation("2단계 스키마 검수 시작: {ConfigPath}", schemaConfigPath);

                if (!File.Exists(schemaConfigPath))
                {
                    _logger.LogWarning("스키마 설정 파일을 찾을 수 없습니다: {Path}", schemaConfigPath);
                    return new SchemaCheckResult
                    {
                        StartedAt = DateTime.Now,
                        CompletedAt = DateTime.Now,
                        IsValid = true,
                        WarningCount = 1,
                        Message = "스키마 설정 파일이 없어 스키마 검수를 스킵했습니다.",
                        SchemaResults = new List<SchemaValidationItem>()
                    };
                }

                // 선택 항목이 있는 경우, 해당 테이블만 대상으로 제한한다
                if (selectedRows != null && selectedRows.Any())
                {
                    var selectedTableIds = selectedRows.Select(r => r.TableId).Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
                    validTables = validTables.Where(t => selectedTableIds.Contains(t.TableId)).ToList();
                    _logger.LogInformation("선택된 테이블만 스키마 검수: {Count}개", validTables.Count);
                }

                _logger.LogInformation("실제 스키마 기반 검수를 수행합니다 - 대상 테이블: {Count}개", validTables.Count);
                var schemaResult = await _schemaService.ValidateSchemaAsync(gdbPath, schemaConfigPath, validTables);
                _logger.LogInformation("2단계: 스키마 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", schemaResult.ErrorCount, schemaResult.WarningCount);
                return schemaResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 검수 실행 오류");
                return new SchemaCheckResult
                {
                    StartedAt = DateTime.Now,
                    CompletedAt = DateTime.Now,
                    IsValid = false,
                    ErrorCount = 1,
                    Message = $"스키마 검수 중 오류 발생: {ex.Message}",
                    SchemaResults = new List<SchemaValidationItem>()
                };
            }
        }

        /// <summary>
        /// 스키마 설정을 로드합니다
        /// </summary>
        private async Task<List<SpatialCheckPro.Models.Config.SchemaCheckConfig>> LoadSchemaConfigsAsync(string configPath)
        {
            return await Task.Run(() =>
            {
                var configs = new List<SpatialCheckPro.Models.Config.SchemaCheckConfig>();
                
                try
                {
                    using var reader = new StreamReader(configPath);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    configs = csv.GetRecords<SpatialCheckPro.Models.Config.SchemaCheckConfig>().ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "스키마 설정 로드 중 오류 발생: {Path}", configPath);
                }
                
                return configs;
            });
        }

        /// <summary>
        /// 3단계 지오메트리 검수를 실행합니다
        /// </summary>
        private async Task<GeometryCheckResult> ExecuteGeometryCheckAsync(string originalGdbPath, string dataSourcePath, IValidationDataProvider dataProvider, string geometryConfigPath, List<TableValidationItem> validTables, List<SpatialCheckPro.Models.Config.GeometryCheckConfig>? selectedRows = null)
        {
            var result = new GeometryCheckResult
            {
                StartedAt = DateTime.Now,
                IsValid = true,
                Message = "지오메트리 검수 완료",
                GeometryResults = new List<GeometryValidationItem>()
            };

            try
            {
                _logger.LogInformation("3단계 지오메트리 검수 시작: {ConfigPath}", geometryConfigPath);
                
                var geometryConfigs = await LoadGeometryConfigsAsync(geometryConfigPath);
                if (selectedRows != null && selectedRows.Any())
                {
                    var selectedKeys = selectedRows.Select(r => $"{r.TableId}_{r.TableName}").ToHashSet();
                    geometryConfigs = geometryConfigs.Where(c => selectedKeys.Contains($"{c.TableId}_{c.TableName}")).ToList();
                }

                if (!geometryConfigs.Any())
                {
                    result.Message = "지오메트리 설정이 없어 스킵합니다.";
                    return result;
                }
                
                var geometryResults = await _geometryService.ValidateGeometryAsync(originalGdbPath, validTables, geometryConfigs, null);
                result.GeometryResults = geometryResults;
                result.ErrorCount = geometryResults.Sum(r => r.ErrorCount);
                result.WarningCount = geometryResults.Sum(r => r.WarningCount);
                result.IsValid = result.ErrorCount == 0;
                
                // 통계 설정
                result.TotalTableCount = geometryConfigs.Select(c => c.TableId).Distinct().Count();
                result.ProcessedTableCount = geometryResults.Count(r => r.ProcessedFeatureCount > 0);
                result.SkippedTableCount = result.TotalTableCount - result.ProcessedTableCount;
                
                _logger.LogInformation("3단계 통계: 전체 {Total}개, 처리 {Processed}개, 스킵 {Skipped}개", 
                    result.TotalTableCount, result.ProcessedTableCount, result.SkippedTableCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 중 오류 발생");
                result.IsValid = false;
                result.Message = $"오류: {ex.Message}";
            }

            result.CompletedAt = DateTime.Now;
            return result;
        }

        /// <summary>
        /// 지오메트리 설정을 로드합니다
        /// </summary>
        private async Task<List<SpatialCheckPro.Models.Config.GeometryCheckConfig>> LoadGeometryConfigsAsync(string configPath)
        {
            return await Task.Run(() =>
            {
                var configs = new List<SpatialCheckPro.Models.Config.GeometryCheckConfig>();
                
                try
                {
                    using var reader = new StreamReader(configPath);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    configs = csv.GetRecords<SpatialCheckPro.Models.Config.GeometryCheckConfig>().ToList();
            }
            catch (Exception ex)
            {
                    _logger.LogError(ex, "지오메트리 설정 로드 중 오류 발생: {Path}", configPath);
                }
                
                return configs;
            });
        }

        /// <summary>
        /// 4단계 속성 관계 검수를 실행합니다
        /// </summary>
        private async Task<AttributeRelationCheckResult> ExecuteAttributeRelationCheckAsync(string dataSourcePath, IValidationDataProvider dataProvider, string attributeConfigPath, string? codelistPath)
        {
            var result = new AttributeRelationCheckResult
            {
                StartedAt = DateTime.Now,
                IsValid = true,
                ErrorCount = 0,
                WarningCount = 0,
                Message = "속성 관계 검수 완료",
                Status = CheckStatus.Running
            };

            try
            {
                _logger.LogInformation("4단계 속성 관계 검수 시작: {ConfigPath}", attributeConfigPath);

                // 설정 파일 존재 확인 및 로드
                if (!File.Exists(attributeConfigPath))
                {
                    result.WarningCount++;
                    result.Message = "속성 관계 설정 파일이 없어 속성 관계 검수를 스킵했습니다.";
                    _logger.LogWarning("속성 관계 설정 파일을 찾을 수 없습니다: {Path}", attributeConfigPath);
                    result.Status = CheckStatus.Skipped;
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 속성 설정 로드
                var attributeConfigs = await LoadAttributeConfigFlexibleAsync(attributeConfigPath);
                if (!attributeConfigs.Any())
                {
                    result.WarningCount++;
                    result.Message = "속성 관계 설정이 없어 속성 관계 검수를 스킵했습니다.";
                    _logger.LogWarning("속성 관계 설정이 비어있습니다: {Path}", attributeConfigPath);
                    result.Status = CheckStatus.Skipped;
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                _logger.LogInformation("속성 관계 검수 대상: {Count}개", attributeConfigs.Count);

                // codelist 파일 로드 (있는 경우)
                var actualCodelistPath = codelistPath ?? Path.Combine(GetDefaultConfigDirectory(), "codelist.csv");
                if (File.Exists(actualCodelistPath))
                {
                    _attributeCheckProcessor.LoadCodelist(actualCodelistPath);
                }

                // 속성 관계 검수 실행
                var attrErrors = await _attributeCheckProcessor.ValidateAsync(dataSourcePath, dataProvider, attributeConfigs);
                
                foreach (var e in attrErrors)
                {
                    if (e.Severity == SpatialCheckPro.Models.Enums.ErrorSeverity.Critical ||
                        e.Severity == SpatialCheckPro.Models.Enums.ErrorSeverity.Error)
                    {
                        result.ErrorCount += 1;
                        result.Errors.Add(e);
                }
                else
                {
                        result.WarningCount += 1;
                        result.Warnings.Add(e);
                    }
                }

                // 통계 설정
                result.ProcessedRulesCount = attributeConfigs.Count;
                _logger.LogInformation("4단계 통계: 검사한 규칙 {Count}개, 오류 {Error}개", 
                    result.ProcessedRulesCount, result.ErrorCount);

                result.Status = result.ErrorCount > 0 ? CheckStatus.Failed : (result.WarningCount > 0 ? CheckStatus.Warning : CheckStatus.Passed);
                result.Message = result.IsValid ? "속성 관계 검수 완료" : $"속성 관계 검수 완료: 오류 {result.ErrorCount}개";
                result.CompletedAt = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "속성 관계 검수 중 오류 발생");
                result.ErrorCount = 1;
                result.IsValid = false;
                result.Message = $"속성 관계 검수 중 오류 발생: {ex.Message}";
                result.Status = CheckStatus.Failed;
                result.CompletedAt = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// 5단계 공간 관계 검수를 실행합니다
        /// </summary>
        private async Task<RelationCheckResult> ExecuteRelationCheckAsync(string dataSourcePath, IValidationDataProvider dataProvider, string relationConfigPath, List<SpatialCheckPro.Models.Config.RelationCheckConfig>? selectedRows)
        {
            var result = new RelationCheckResult
            {
                StartedAt = DateTime.Now,
                IsValid = true,
                ErrorCount = 0,
                WarningCount = 0,
                Message = "관계 검수 완료"
            };

            try
            {
                _logger.LogInformation("5단계 공간 관계 검수 시작: {ConfigPath}", relationConfigPath);

                // 설정 파일 존재 확인 및 로드
                if (!File.Exists(relationConfigPath))
                        {
                            result.WarningCount++;
                    result.Message = "관계 설정 파일이 없어 관계 검수를 스킵했습니다.";
                    _logger.LogWarning("관계 설정 파일을 찾을 수 없습니다: {Path}", relationConfigPath);
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 관계 설정 로드
                var relationConfigs = await LoadFlexibleRelationConfigsAsync(relationConfigPath);
                if (!relationConfigs.Any())
                        {
                            result.WarningCount++;
                    result.Message = "관계 설정이 없어 관계 검수를 스킵했습니다.";
                    _logger.LogWarning("관계 설정이 비어있습니다: {Path}", relationConfigPath);
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 선택된 행이 있으면 필터링
                if (selectedRows != null && selectedRows.Any())
                {
                    var selectedKeys = selectedRows.Select(r => $"{r.MainTableId}_{r.RelatedTableId}_{r.CaseType}").ToHashSet();
                    relationConfigs = relationConfigs.Where(c => selectedKeys.Contains($"{c.MainTableId}_{c.RelatedTableId}_{c.CaseType}")).ToList();
                    _logger.LogInformation("선택된 관계만 검수: {Count}개", relationConfigs.Count);
                }

                _logger.LogInformation("관계 검수 대상: {Count}개", relationConfigs.Count);

                // 관계 검수 실행
                foreach (var rule in relationConfigs)
                {
                    var vr = await _relationProcessor.ProcessAsync(dataSourcePath, rule);
                    if (!vr.IsValid)
                    {
                    result.IsValid = false;
                }
                    result.ErrorCount += vr.ErrorCount;
                    if (vr.Errors != null && vr.Errors.Count > 0)
                {
                        result.Errors.AddRange(vr.Errors);
                }
                }

                // 통계 설정
                result.ProcessedRulesCount = relationConfigs.Count;
                _logger.LogInformation("5단계 통계: 검사한 규칙 {Count}개, 오류 {Error}개", 
                    result.ProcessedRulesCount, result.ErrorCount);

                result.Message = result.IsValid ? "관계 검수 완료" : $"관계 검수 완료: 오류 {result.ErrorCount}개";
                result.CompletedAt = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "관계 검수 중 오류 발생");
                result.ErrorCount = 1;
                result.IsValid = false;
                result.Message = $"관계 검수 중 오류 발생: {ex.Message}";
                result.CompletedAt = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// 유연한 속성 설정 로드
        /// </summary>
        private async Task<List<SpatialCheckPro.Models.Config.AttributeCheckConfig>> LoadAttributeConfigFlexibleAsync(string configPath)
        {
            return await Task.Run(() =>
            {
                var configs = new List<SpatialCheckPro.Models.Config.AttributeCheckConfig>();
                
                try
                {
                    using var reader = new StreamReader(configPath);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    configs = csv.GetRecords<SpatialCheckPro.Models.Config.AttributeCheckConfig>().ToList();
            }
            catch (Exception ex)
            {
                    _logger.LogError(ex, "속성 설정 로드 중 오류 발생: {Path}", configPath);
                }
                
                return configs;
            });
        }

        /// <summary>
        /// 유연한 관계 설정 로드
        /// </summary>
        private async Task<List<SpatialCheckPro.Models.Config.RelationCheckConfig>> LoadFlexibleRelationConfigsAsync(string configPath)
        {
            return await Task.Run(() =>
            {
                var configs = new List<SpatialCheckPro.Models.Config.RelationCheckConfig>();
                
                try
                {
                    using var reader = new StreamReader(configPath);
                    using var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HeaderValidated = null,  // 헤더 검증 비활성화
                        MissingFieldFound = null, // 누락된 필드 무시
                        IgnoreBlankLines = true,
                        TrimOptions = CsvHelper.Configuration.TrimOptions.Trim
                    });
                    
                    configs = csv.GetRecords<SpatialCheckPro.Models.Config.RelationCheckConfig>().ToList();
                    _logger.LogInformation("관계 검수 설정 로드 완료: {Count}개 규칙", configs.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "관계 설정 로드 중 오류 발생: {Path}", configPath);
                }
                
                return configs;
            });
        }

        /// <summary>
        /// WKT와 중심점 추출
        /// </summary>
        private async Task<(string wkt, double cx, double cy)> ExtractWktAndCenterAsync(string filePath, string layerName, string featureId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var dataSource = OSGeo.OGR.Ogr.Open(filePath, 0);
                    if (dataSource == null) return (string.Empty, 0, 0);

                    using var layer = dataSource.GetLayerByName(layerName);
                    if (layer == null) return (string.Empty, 0, 0);

                    if (int.TryParse(featureId, out var fid))
                    {
                        using var feature = layer.GetFeature(fid);
                        if (feature == null) return (string.Empty, 0, 0);

                        using var geometry = feature.GetGeometryRef();
                        if (geometry == null) return (string.Empty, 0, 0);

                        geometry.ExportToWkt(out string wkt);
                        var env = new OSGeo.OGR.Envelope();
                        geometry.GetEnvelope(env);
                        var cx = (env.MinX + env.MaxX) / 2.0;
                        var cy = (env.MinY + env.MaxY) / 2.0;
                        return (wkt, cx, cy);
                    }
                }
                catch
                {
                    return (string.Empty, 0, 0);
                }
                return (string.Empty, 0, 0);
            });
        }
    }
}