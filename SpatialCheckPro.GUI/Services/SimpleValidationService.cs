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
using static SpatialCheckPro.Services.FileAnalysisService;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using SpatialCheckPro.GUI.Constants;

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
        private readonly GdbToSqliteConverter _gdbToSqliteConverter;
        private readonly IServiceProvider _serviceProvider;
        private readonly ValidationResultConverter _validationResultConverter;
        private readonly ILargeFileProcessor _largeFileProcessor;
        private readonly IGeometryCheckProcessor _geometryCheckProcessor;
        private readonly SpatialCheckPro.Models.Config.PerformanceSettings _performanceSettings;
        private readonly ValidationMetricsCollector? _metricsCollector;
        private bool _isProcessingRelationCheck = false; // 관계 검수 처리 중 플래그 (개별 규칙 진행률 무시용)

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
            SpatialCheckPro.Models.Config.PerformanceSettings? performanceSettings,
            ValidationResultConverter validationResultConverter,
            IGeometryCheckProcessor geometryCheckProcessor, ILargeFileProcessor? largeFileProcessor = null,
            ValidationMetricsCollector? metricsCollector = null)
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
            _gdbToSqliteConverter = gdbToSqliteConverter;
            _serviceProvider = serviceProvider;
            _validationResultConverter = validationResultConverter;
            _geometryCheckProcessor = geometryCheckProcessor ?? throw new ArgumentNullException(nameof(geometryCheckProcessor));
            _largeFileProcessor = largeFileProcessor;
            _performanceSettings = performanceSettings ?? new SpatialCheckPro.Models.Config.PerformanceSettings();
            _metricsCollector = metricsCollector;

            _relationProcessor.ProgressUpdated += OnRelationProgressUpdated;
            
            System.Console.WriteLine($"[SimpleValidationService] 생성자 - 인스턴스: {this.GetHashCode()}");
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
        /// <param name="errorCount">발견된 오류 수</param>
        /// <param name="warningCount">발견된 경고 수</param>
        private void ReportProgress(int stage, string stageName, double stageProgress, string statusMessage, bool isCompleted = false, bool isSuccessful = true, int errorCount = 0, int warningCount = 0, ValidationResult? partialResult = null)
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
                IsStageSkipped = false,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                PartialResult = partialResult
            };

            System.Console.WriteLine($"[SimpleValidationService] 이벤트 발생 전: ProgressUpdated={ProgressUpdated != null}, 구독자 수={ProgressUpdated?.GetInvocationList().Length ?? 0}");
            ProgressUpdated?.Invoke(this, args);
            System.Console.WriteLine($"[SimpleValidationService] 이벤트 발생 후: Stage={stage}, Progress={stageProgress:F1}%");
            _logger.LogInformation("진행률 업데이트: {Stage}단계 {StageName} - 전체 {OverallProgress:F1}%, 단계 {StageProgress:F1}%, 완료={Completed}, PartialResult={HasPartial}, 오류 {ErrorCount}개 - {Status}", 
                stage, stageName, args.OverallProgress, stageProgress, isCompleted, partialResult != null ? "있음" : "null", errorCount, statusMessage);
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
        /// <param name="partialResult">부분 검수 결과</param>
        private void ReportProgressWithUnits(int stage, string stageName, double stageProgress, string statusMessage, long processedUnits, long totalUnits, bool isCompleted = false, bool isSuccessful = true, ValidationResult? partialResult = null)
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
                TotalUnits = totalUnits,
                PartialResult = partialResult
            };

            System.Console.WriteLine($"[SimpleValidationService] 단위 이벤트 발생 전: ProgressUpdated={ProgressUpdated != null}, 구독자 수={ProgressUpdated?.GetInvocationList().Length ?? 0}");
            ProgressUpdated?.Invoke(this, args);
            System.Console.WriteLine($"[SimpleValidationService] 단위 이벤트 발생 후: Stage={stage}, Units={processedUnits}/{totalUnits}");
            _logger.LogInformation("진행률 업데이트(단위): {Stage}단계 {StageName} - 전체 {OverallProgress:F1}%, 단계 {StageProgress:F1}% ({ProcessedUnits}/{TotalUnits}), 완료={Completed}, PartialResult={HasPartial} - {Status}", 
                stage, stageName, args.OverallProgress, stageProgress, processedUnits, totalUnits, isCompleted, partialResult != null ? "있음" : "null", statusMessage);
        }

        private void OnRelationProgressUpdated(object? sender, RelationValidationProgressEventArgs e)
        {
            try
            {
                // ExecuteRelationCheckAsync에서 직접 진행률을 관리하는 동안에는 개별 규칙 이벤트 무시
                if (_isProcessingRelationCheck)
                {
                    return; // 중복 진행률 업데이트 방지
                }
                
                const int stageNumber = 5;
                var stageName = StageDefinitions.GetByNumber(stageNumber).StageName;
                var totalRules = e.TotalRules > 0 ? e.TotalRules : Math.Max(1, e.ProcessedRules);
                var processedRules = Math.Max(0, Math.Min(e.ProcessedRules, totalRules));
                var message = string.IsNullOrWhiteSpace(e.StatusMessage)
                    ? (string.IsNullOrWhiteSpace(e.CurrentRule)
                        ? "공간 관계 규칙 검수 중"
                        : $"규칙 {e.CurrentRule} 검수 중")
                    : e.StatusMessage;

                ReportProgressWithUnits(
                    stageNumber,
                    stageName,
                    e.StageProgress,
                    message,
                    processedRules,
                    totalRules,
                    e.IsStageCompleted,
                    e.IsStageSuccessful);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "관계 검수 진행률 전파 중 예외가 발생했지만 계속 진행합니다.");
            }
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
            try
            {
                // 경로 정규화: 끝의 백슬래시/슬래시 제거
                filePath = filePath.TrimEnd('\\', '/');

                var totalStopwatch = Stopwatch.StartNew(); // 전체 소요시간 측정을 위한 Stopwatch 시작
                _logger.LogInformation("검수 시작: {FilePath}, 고성능 모드: {UseHPMode}", filePath, useHighPerformanceMode);

            // 경로 존재 여부 선검사
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                var warningMessage = $"검수 대상 파일/폴더를 찾을 수 없습니다: {filePath}";
                _logger.LogWarning("검수 대상 파일/폴더를 찾을 수 없음: {FilePath}. 검증을 건너뜁니다.", filePath);

                return new ValidationResult
                {
                    IsValid = false,
                    Status = ValidationStatus.Failed,
                    ErrorCount = 1,
                    WarningCount = 0,
                    Message = warningMessage,
                    ProcessingTime = TimeSpan.Zero,
                    StartedAt = DateTime.Now,
                    CompletedAt = DateTime.Now
                };
            }

            // 대용량 파일 분석 및 처리 모드 결정
            bool isLargeFile = false;
            string processingMode = "Standard";
            long fileSize = 0;

            if (File.Exists(filePath))
            {
                fileSize = new FileInfo(filePath).Length;
            }
            else if (Directory.Exists(filePath))
            {
                fileSize = CalculateDirectorySizeSafe(filePath);
            }

            var sizeThresholdExceeded = fileSize >= _performanceSettings.HighPerformanceModeSizeThresholdBytes;

            if (_largeFileProcessor != null)
            {
                try
                {
                    var fileAnalysis = _largeFileProcessor.AnalyzeFileForProcessing(filePath);
                    isLargeFile = (bool)(fileAnalysis.GetType().GetProperty("IsLargeFile")?.GetValue(fileAnalysis) ?? false);
                    processingMode = fileAnalysis.GetType().GetProperty("RecommendedProcessingMode")?.GetValue(fileAnalysis)?.ToString() ?? "Standard";

                    _logger.LogInformation("파일 분석 결과: 대용량={IsLargeFile}, 처리모드={ProcessingMode}", isLargeFile, processingMode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "파일 분석 중 오류 발생, 표준 모드로 진행합니다");
                }
            }

            // 성능 모니터링 시작 (기본 로깅으로 구현)
            var performanceStartTime = DateTime.Now;
            _logger.LogInformation("성능 모니터링 시작: 파일크기 {FileSize:N0} bytes, 임계값초과={ThresholdExceeded}, 추천모드={ProcessingMode}",
                fileSize,
                sizeThresholdExceeded,
                processingMode);

            var result = new ValidationResult
            {
                ValidationId = Guid.NewGuid().ToString(), // 이 ID는 이제 내부 식별용
                TargetFile = filePath,
                StartedAt = DateTime.Now,
                Status = ValidationStatus.Running
            };
            
            // 메트릭 수집 시작 (피처 카운트는 나중에 업데이트)
            _metricsCollector?.StartNewRun(filePath, fileSize, 0, 0);

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

                // 파일/폴더 존재 여부 검증 - 예외 대신 사용자 친화적 처리
                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    var warningMessage = $"검수 대상 파일/폴더를 찾을 수 없습니다: {filePath}";
                    _logger.LogWarning("검수 대상 파일/폴더를 찾을 수 없음: {FilePath}. 검증을 건너뜁니다.", filePath);

                    // 빈 결과 반환 (프로그램 중단 방지)
                    return new ValidationResult
                    {
                        IsValid = false,
                        Status = ValidationStatus.Failed,
                        ErrorCount = 1,
                        WarningCount = 0,
                        Message = warningMessage,
                        ProcessingTime = TimeSpan.Zero,
                        StartedAt = DateTime.Now,
                        CompletedAt = DateTime.Now
                    };
                }

                // FileGDB 폴더인 경우 추가 검증
                if (Directory.Exists(filePath) && filePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsValidFileGdbDirectory(filePath))
                    {
                        var warningMessage = $"유효하지 않은 FileGDB 폴더: {filePath}\n";
                        warningMessage += "FileGDB의 필수 파일들이 누락되었거나 손상되었을 수 있습니다.";
                        _logger.LogWarning("유효하지 않은 FileGDB 폴더 검증 실패: {FilePath}", filePath);

                        // 경고만 기록하고 계속 진행 (완전성 검수 단계에서 더 자세히 확인)
                        ReportProgress(0, "FileGDB 검증", 0, warningMessage, false, false);
                    }
                }

                if (Directory.Exists(filePath) && filePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    // 0단계: FileGDB 완전성 검수
                    cancellationToken.ThrowIfCancellationRequested();
                    _metricsCollector?.RecordStageStart(0, "FileGDB 완전성 검수", 1);
                    ReportProgress(0, "FileGDB 완전성 검수", 0, "FileGDB 사전 점검을 시작합니다...");

                    // 여기서는 qcGdbPath가 아닌 원본 filePath를 사용해야 합니다.
                    var fgdbCheck = await ExecuteFileGdbIntegrityCheckAsync(filePath, cancellationToken);
                    result.FileGdbCheckResult = fgdbCheck;
                    result.ErrorCount += fgdbCheck.ErrorCount;
                    result.WarningCount += fgdbCheck.WarningCount;

                    if (fgdbCheck.Status == CheckStatus.Failed)
                    {
                        var errorMessage = $"FileGDB 검증 실패: {filePath}. 정상적인 FileGDB가 아니므로 검증을 중단합니다.";
                        _logger.LogError("FileGDB 검증 실패로 검증 중단: {FilePath}", filePath);

                        // 검증 실패 시 빈 결과 반환 (프로그램 중단 방지)
                        return new ValidationResult
                        {
                            IsValid = false,
                            Status = ValidationStatus.Failed,
                            ErrorCount = fgdbCheck.ErrorCount,
                            WarningCount = fgdbCheck.WarningCount,
                            Message = errorMessage,
                            FileGdbCheckResult = fgdbCheck,
                            ProcessingTime = TimeSpan.Zero,
                            StartedAt = DateTime.Now,
                            CompletedAt = DateTime.Now
                        };
                    }
                    _metricsCollector?.RecordStageEnd(0, fgdbCheck.Status != CheckStatus.Failed, fgdbCheck.ErrorCount, fgdbCheck.WarningCount, 0);
                    ReportProgress(0, "FileGDB 완전성 검수", 100, "FileGDB 완전성 검수 완료", true, true, fgdbCheck.ErrorCount, fgdbCheck.WarningCount);
                }

                // 단계별 처리 실행 (순차)
                _logger.LogInformation("=== 순차 처리 모드로 실행 ===");
                await ExecuteStagesSequentiallyAsync(filePath, validationDataSourcePath, dataProvider, result, actualTableConfigPath, actualSchemaConfigPath, 
                    actualGeometryConfigPath, actualAttributeConfigPath, actualRelationConfigPath, codelistPath, cancellationToken);

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
                // 성능 모니터링 완료 및 분석 (기본 로깅으로 구현)
                var performanceDuration = DateTime.Now - performanceStartTime;
                _logger.LogInformation("성능 모니터링 완료: 총 소요시간 {Duration}, 성공: {Success}",
                    performanceDuration, result.IsValid);

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

                result.SkippedCount =
                    (result.GeometryCheckResult?.SkippedCount ?? 0) +
                    (result.RelationCheckResult?.SkippedCount ?? 0) +
                    (result.AttributeRelationCheckResult?.SkippedCount ?? 0);

                totalStopwatch.Stop();
                result.CompletedAt = DateTime.Now;
                result.ProcessingTime = totalStopwatch.Elapsed;
                
                // 메트릭 수집 완료
                if (_metricsCollector != null)
                {
                    var finalTableCount = result.TableCheckResult?.TotalTableCount ?? 0;
                    var finalFeatureCount = result.TableCheckResult?.TableResults?.Sum(t => t.FeatureCount ?? 0) ?? 0;
                    await _metricsCollector.CompleteRunAndSaveAsync(
                        filePath, fileSize, finalTableCount, finalFeatureCount, 
                        result.IsValid);
                }
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 실행 중 치명적 오류 발생: {FilePath}", filePath);

                // 예외 발생 시 빈 결과 반환 (프로그램 중단 방지)
                return new ValidationResult
                {
                    IsValid = false,
                    Status = ValidationStatus.Failed,
                    ErrorCount = 1,
                    WarningCount = 0,
                    Message = $"검수 실행 중 오류 발생: {ex.Message}",
                    ProcessingTime = TimeSpan.Zero,
                    StartedAt = DateTime.Now,
                    CompletedAt = DateTime.Now
                };
            }
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
                
                // 테이블 수 계산
                var tableCount = 0;
                try
                {
                    using var ds = OSGeo.OGR.Ogr.Open(originalGdbPath, 0);
                    if (ds != null) tableCount = ds.GetLayerCount();
                }
                catch { }
                
                _metricsCollector?.RecordStageStart(1, "테이블 검수", tableCount);
                ReportProgress(1, "테이블 검수", 0, "테이블 검수를 시작합니다...");
                _logger.LogInformation("1단계: 테이블 검수 시작");
                _logger.LogInformation("테이블 설정 파일: {ConfigPath}", tableConfigPath);

                var tableResult = await ExecuteTableCheckAsync(dataSourcePath, dataProvider, tableConfigPath, _selectedStage1Items);
                
                result.TableCheckResult = tableResult;
                result.ErrorCount += tableResult.ErrorCount;
                result.WarningCount += tableResult.WarningCount;

                // 오류가 있으면 실패로 표시
                bool isTableSuccessful = tableResult.ErrorCount == 0;
                string tableMessage = isTableSuccessful 
                    ? "테이블 검수 완료" 
                    : $"테이블 검수 완료 - 오류 {tableResult.ErrorCount}개";
                _metricsCollector?.RecordStageEnd(1, isTableSuccessful, tableResult.ErrorCount, tableResult.WarningCount, 0);
                
                // 1단계 완료 시 부분 결과 생성
                var partialResult1 = new ValidationResult
                {
                    ValidationId = result.ValidationId,
                    TargetFile = result.TargetFile,
                    StartedAt = result.StartedAt,
                    Status = ValidationStatus.Running,
                    TableCheckResult = tableResult,
                    ErrorCount = tableResult.ErrorCount,
                    WarningCount = tableResult.WarningCount
                };
                
                // 단위 정보와 함께 완료 보고
                ReportProgressWithUnits(1, "테이블 검수", 100, tableMessage, tableResult.TotalTableCount, tableResult.TotalTableCount, true, isTableSuccessful, partialResult1);
                _logger.LogInformation("1단계: 테이블 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    tableResult.ErrorCount, tableResult.WarningCount);
            }

            // 2단계: 스키마 검수
            if (ShouldRunStage(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(2, "스키마 검수", 0, "스키마 검수를 시작합니다...");
                _logger.LogInformation("2단계: 스키마 검수 시작");

                var schemaResult = await ExecuteSchemaCheckAsync(originalGdbPath, dataProvider, schemaConfigPath, 
                    result.TableCheckResult?.TableResults ?? new List<TableValidationItem>(), _selectedStage2Items);
                
                result.SchemaCheckResult = schemaResult;
                result.ErrorCount += schemaResult.ErrorCount;
                result.WarningCount += schemaResult.WarningCount;

                // 오류가 있으면 실패로 표시
                bool isSchemaSuccessful = schemaResult.ErrorCount == 0;
                string schemaMessage = isSchemaSuccessful 
                    ? "스키마 검수 완료" 
                    : $"스키마 검수 완료 - 오류 {schemaResult.ErrorCount}개";
                
                // 2단계 완료 시 부분 결과 생성
                var partialResult2 = new ValidationResult
                {
                    ValidationId = result.ValidationId,
                    TargetFile = result.TargetFile,
                    StartedAt = result.StartedAt,
                    Status = ValidationStatus.Running,
                    TableCheckResult = result.TableCheckResult,
                    SchemaCheckResult = schemaResult,
                    ErrorCount = (result.TableCheckResult?.ErrorCount ?? 0) + schemaResult.ErrorCount,
                    WarningCount = (result.TableCheckResult?.WarningCount ?? 0) + schemaResult.WarningCount
                };
                
                // 단위 정보와 함께 완료 보고
                ReportProgressWithUnits(2, "스키마 검수", 100, schemaMessage, schemaResult.TotalColumnCount, schemaResult.TotalColumnCount, true, isSchemaSuccessful, partialResult2);
                _logger.LogInformation("2단계: 스키마 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개", 
                    schemaResult.ErrorCount, schemaResult.WarningCount);
            }

            // 3단계: 지오메트리 검수
            if (ShouldRunStage(3))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(3, "지오메트리 검수", 0, "지오메트리 검수를 시작합니다...");
                _logger.LogInformation("3단계: 지오메트리 검수 시작");

                var geometryTargetTables = result.SchemaCheckResult != null && result.SchemaCheckResult.SchemaResults.Any()
                    ? result.SchemaCheckResult.SchemaResults
                        .Select(s => new TableValidationItem { TableId = s.TableId, TableName = s.TableId })
                        .DistinctBy(t => t.TableId)
                        .ToList()
                    : result.TableCheckResult?.TableResults?.ToList() ?? new List<TableValidationItem>();

                var geometryResult = await ExecuteGeometryCheckAsync(originalGdbPath, dataSourcePath, dataProvider, geometryConfigPath, 
                    geometryTargetTables, _selectedStage3Items);
                
                result.GeometryCheckResult = geometryResult;
                result.ErrorCount += geometryResult.ErrorCount;
                result.WarningCount += geometryResult.WarningCount;

                // 오류가 있으면 실패로 표시
                bool isGeometrySuccessful = geometryResult.ErrorCount == 0;
                string geometryMessage = isGeometrySuccessful 
                    ? "지오메트리 검수 완료" 
                    : $"지오메트리 검수 완료 - 오류 {geometryResult.ErrorCount}개";
                
                // 3단계 완료 시 부분 결과 생성
                var partialResult3 = new ValidationResult
                {
                    ValidationId = result.ValidationId,
                    TargetFile = result.TargetFile,
                    StartedAt = result.StartedAt,
                    Status = ValidationStatus.Running,
                    TableCheckResult = result.TableCheckResult,
                    SchemaCheckResult = result.SchemaCheckResult,
                    GeometryCheckResult = geometryResult,
                    ErrorCount = (result.TableCheckResult?.ErrorCount ?? 0) + (result.SchemaCheckResult?.ErrorCount ?? 0) + geometryResult.ErrorCount,
                    WarningCount = (result.TableCheckResult?.WarningCount ?? 0) + (result.SchemaCheckResult?.WarningCount ?? 0) + geometryResult.WarningCount
                };
                
                // 단위 정보와 함께 완료 보고
                var totalGeometryItems = geometryResult.GeometryResults?.Sum(g => g.TotalFeatureCount) ?? 0;
                ReportProgressWithUnits(3, "지오메트리 검수", 100, geometryMessage, totalGeometryItems, totalGeometryItems, true, isGeometrySuccessful, partialResult3);
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

                var attributeResult = await ExecuteAttributeRelationCheckAsync(dataSourcePath, dataProvider, attributeConfigPath, codelistPath);
                
                result.AttributeRelationCheckResult = attributeResult;
                result.ErrorCount += attributeResult.ErrorCount;
                result.WarningCount += attributeResult.WarningCount;

                // 오류가 있으면 실패로 표시
                bool isAttributeSuccessful = attributeResult.ErrorCount == 0;
                string attributeMessage = isAttributeSuccessful 
                    ? "속성 관계 검수 완료" 
                    : $"속성 관계 검수 완료 - 오류 {attributeResult.ErrorCount}개";
                
                // 4단계 완료 시 부분 결과 생성
                var partialResult4 = new ValidationResult
                {
                    ValidationId = result.ValidationId,
                    TargetFile = result.TargetFile,
                    StartedAt = result.StartedAt,
                    Status = ValidationStatus.Running,
                    TableCheckResult = result.TableCheckResult,
                    SchemaCheckResult = result.SchemaCheckResult,
                    GeometryCheckResult = result.GeometryCheckResult,
                    AttributeRelationCheckResult = attributeResult,
                    ErrorCount = (result.TableCheckResult?.ErrorCount ?? 0) + (result.SchemaCheckResult?.ErrorCount ?? 0) + 
                                 (result.GeometryCheckResult?.ErrorCount ?? 0) + attributeResult.ErrorCount,
                    WarningCount = (result.TableCheckResult?.WarningCount ?? 0) + (result.SchemaCheckResult?.WarningCount ?? 0) + 
                                   (result.GeometryCheckResult?.WarningCount ?? 0) + attributeResult.WarningCount
                };
                
                // 단위 정보와 함께 완료 보고
                ReportProgressWithUnits(4, "속성 관계 검수", 100, attributeMessage, attributeResult.ProcessedRulesCount, attributeResult.ProcessedRulesCount, true, isAttributeSuccessful, partialResult4);
                _logger.LogInformation("4단계: 속성 관계 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개",
                    attributeResult.ErrorCount, attributeResult.WarningCount);
            }

            // 5단계: 공간 관계 검수
            if (ShouldRunStage(5))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 5단계 시작 시 단위 정보와 함께 진행률 보고
                _metricsCollector?.RecordStageStart(5, "공간 관계 검수", 0);
                ReportProgressWithUnits(5, "공간 관계 검수", 0, "공간 관계 검수를 시작합니다...", 0, 1, false, true);
                _logger.LogInformation("5단계: 공간 관계 검수 시작");

                var relationResult = await ExecuteRelationCheckAsync(dataSourcePath, dataProvider, relationConfigPath, _selectedStage5Items);
                
                // ProcessedRulesCount 보존 (재분류 전에 저장)
                var originalProcessedRulesCount = relationResult.ProcessedRulesCount;
                
                // REL_CENTERLINE_ATTR_MISMATCH 오류는 속성 관계 검수로 재분류
                var centerlineAttrMismatchErrors = relationResult.Errors
                    .Where(e => !string.IsNullOrWhiteSpace(e.ErrorCode) 
                        && e.ErrorCode.Equals("REL_CENTERLINE_ATTR_MISMATCH", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (centerlineAttrMismatchErrors.Count > 0)
                {
                    // 속성 관계 검수 결과에 추가 (이미 존재하는 경우)
                    if (result.AttributeRelationCheckResult != null)
                    {
                        result.AttributeRelationCheckResult.Errors.AddRange(centerlineAttrMismatchErrors);
                        // ErrorCount는 Errors.Count를 기준으로 재계산 (동기화 보장)
                        result.AttributeRelationCheckResult.ErrorCount = result.AttributeRelationCheckResult.Errors.Count;
                        result.AttributeRelationCheckResult.IsValid = result.AttributeRelationCheckResult.ErrorCount == 0;
                        _logger.LogInformation("REL_CENTERLINE_ATTR_MISMATCH 오류 {Count}개를 속성 관계 검수로 재분류", centerlineAttrMismatchErrors.Count);
                    }
                    else
                    {
                        // AttributeRelationCheckResult가 없으면 생성
                        result.AttributeRelationCheckResult = new AttributeRelationCheckResult
                        {
                            StartedAt = DateTime.Now,
                            IsValid = false,
                            ErrorCount = centerlineAttrMismatchErrors.Count,
                            WarningCount = 0,
                            Errors = new List<ValidationError>(centerlineAttrMismatchErrors),
                            Warnings = new List<ValidationError>(),
                            Message = $"속성 관계 검수 (재분류된 오류 {centerlineAttrMismatchErrors.Count}개 포함)"
                        };
                        _logger.LogInformation("AttributeRelationCheckResult 생성 및 REL_CENTERLINE_ATTR_MISMATCH 오류 {Count}개 추가", centerlineAttrMismatchErrors.Count);
                    }
                    
                    // 공간 관계 검수 결과에서 제거
                    foreach (var error in centerlineAttrMismatchErrors)
                    {
                        relationResult.Errors.Remove(error);
                    }
                    // ErrorCount는 Errors.Count를 기준으로 재계산 (동기화 보장, 음수 방지)
                    relationResult.ErrorCount = Math.Max(0, relationResult.Errors.Count);
                    relationResult.IsValid = relationResult.ErrorCount == 0;
                    _logger.LogInformation("공간 관계 검수에서 REL_CENTERLINE_ATTR_MISMATCH 제거 후 ErrorCount: {Count} (Errors.Count: {ErrorsCount})", 
                        relationResult.ErrorCount, relationResult.Errors.Count);
                }
                
                // ProcessedRulesCount 복원 (재분류 후에도 유지)
                relationResult.ProcessedRulesCount = originalProcessedRulesCount;
                _logger.LogInformation("공간 관계 검수 ProcessedRulesCount: {Count} (재분류 후에도 유지)", relationResult.ProcessedRulesCount);
                
                result.RelationCheckResult = relationResult;
                result.ErrorCount += relationResult.ErrorCount;
                result.WarningCount += relationResult.WarningCount;
                
                // 재분류된 오류도 전체 집계에 포함
                if (centerlineAttrMismatchErrors.Count > 0 && result.AttributeRelationCheckResult != null)
                {
                    // 이미 AttributeRelationCheckResult.ErrorCount에 포함되어 있으므로 중복 집계 방지
                    // 하지만 전체 ErrorCount는 이미 정확하게 계산됨 (4단계에서 이미 집계됨)
                }

                // 오류가 있으면 실패로 표시
                bool isRelationSuccessful = relationResult.ErrorCount == 0;
                string relationMessage = isRelationSuccessful 
                    ? "공간 관계 검수 완료" 
                    : $"공간 관계 검수 완료 - 오류 {relationResult.ErrorCount}개";
                
                // 5단계 완료 시 부분 결과 생성
                var partialResult5 = new ValidationResult
                {
                    ValidationId = result.ValidationId,
                    TargetFile = result.TargetFile,
                    StartedAt = result.StartedAt,
                    Status = ValidationStatus.Running,
                    TableCheckResult = result.TableCheckResult,
                    SchemaCheckResult = result.SchemaCheckResult,
                    GeometryCheckResult = result.GeometryCheckResult,
                    AttributeRelationCheckResult = result.AttributeRelationCheckResult,
                    RelationCheckResult = relationResult,
                    ErrorCount = (result.TableCheckResult?.ErrorCount ?? 0) + (result.SchemaCheckResult?.ErrorCount ?? 0) + 
                                 (result.GeometryCheckResult?.ErrorCount ?? 0) + (result.AttributeRelationCheckResult?.ErrorCount ?? 0) + relationResult.ErrorCount,
                    WarningCount = (result.TableCheckResult?.WarningCount ?? 0) + (result.SchemaCheckResult?.WarningCount ?? 0) + 
                                   (result.GeometryCheckResult?.WarningCount ?? 0) + (result.AttributeRelationCheckResult?.WarningCount ?? 0) + relationResult.WarningCount
                };
                
                ReportProgress(5, "공간 관계 검수", 100, relationMessage, true, isRelationSuccessful, relationResult.ErrorCount, relationResult.WarningCount, partialResult5);
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
                var totalTables = tableConfigs.Count;
                var stageName = StageDefinitions.GetByNumber(1).StageName;

                // 고급 테이블 검수 서비스 사용
                if (_advancedTableCheckService != null)
                {
                    IProgress<(double percentage, string message)>? tableProgress = null;
                    if (totalTables > 0)
                    {
                        tableProgress = new Progress<(double percentage, string message)>(update =>
                        {
                            var clampedPercentage = Math.Clamp(update.percentage, 0, 100);
                            var processed = (long)Math.Round(totalTables * clampedPercentage / 100.0);
                            if (clampedPercentage >= 100.0 || processed > totalTables)
                            {
                                processed = totalTables;
                            }

                            var isCompleted = clampedPercentage >= 100.0 - 0.001;
                            ReportProgressWithUnits(
                                1,
                                stageName,
                                clampedPercentage,
                                update.message,
                                processed,
                                totalTables,
                                isCompleted,
                                true);
                        });
                    }

                    var advancedResult = await _advancedTableCheckService.PerformAdvancedTableCheckAsync(
                        dataSourcePath,
                        dataProvider,
                        tableConfigs,
                        tableProgress);
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
                    var processedTables = 0;
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

                        processedTables++;
                        if (totalTables > 0)
                        {
                            var percentage = (processedTables * 100.0) / totalTables;
                            var message = $"테이블 검수 중... ({processedTables}/{totalTables}) {config.TableName}";
                            var isCompleted = processedTables >= totalTables;
                            ReportProgressWithUnits(
                                1,
                                stageName,
                                percentage,
                                message,
                                processedTables,
                                totalTables,
                                isCompleted,
                                true);
                        }
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

                const int stageNumber = 3;
                var stageName = StageDefinitions.GetByNumber(stageNumber).StageName;
                var totalConfigs = geometryConfigs.Count;
                var processedConfigs = 0;
                
                // 피처 개수 기반 진행률 추적
                long totalFeatures = 0;
                long processedFeatures = 0;

                // 각 설정 파일의 총 피처 개수 미리 계산
                using (var ds = Ogr.Open(originalGdbPath, 0))
                {
                    if (ds != null)
                    {
                        foreach (var config in geometryConfigs)
                        {
                            var layer = ds.GetLayerByName(config.TableId);
                            if (layer != null)
                            {
                                // 필터 적용 전 총 피처 개수
                                var featureCount = layer.GetFeatureCount(1);
                                totalFeatures += Math.Max(0, featureCount);
                                _logger.LogDebug("지오메트리 검수 대상 피처 수: {TableId} = {Count}개", config.TableId, featureCount);
                            }
                        }
                    }
                }
                
                _logger.LogInformation("지오메트리 검수 전체 대상 피처 수: {TotalFeatures:N0}개", totalFeatures);

                void PublishGeometryProgress(string message, bool isSuccessful, bool markCompleted = false, long additionalProcessed = 0)
                {
                    if (totalFeatures <= 0 && totalConfigs <= 0)
                    {
                        return;
                    }

                    // 피처 개수가 있으면 피처 개수 기준, 없으면 설정 파일 개수 기준
                    if (totalFeatures > 0)
                    {
                        processedFeatures += additionalProcessed;
                        var progressValue = Math.Clamp(processedFeatures * 100.0 / totalFeatures, 0.0, 100.0);
                        ReportProgressWithUnits(
                            stageNumber,
                            stageName,
                            progressValue,
                            message,
                            processedFeatures,
                            totalFeatures,
                            markCompleted,
                            isSuccessful);
                    }
                    else
                    {
                        var progressValue = Math.Clamp(processedConfigs * 100.0 / totalConfigs, 0.0, 100.0);
                        ReportProgressWithUnits(
                            stageNumber,
                            stageName,
                            progressValue,
                            message,
                            processedConfigs,
                            totalConfigs,
                            markCompleted,
                            isSuccessful);
                    }
                }

                // 초기 진행률 보고 (totalFeatures가 계산된 후)
                if (totalFeatures > 0)
                {
                    PublishGeometryProgress($"지오메트리 검수 준비 중... (0/{totalFeatures:N0}개 피처)", true);
                }
                else
                {
                    PublishGeometryProgress($"지오메트리 검수 준비 중... (0/{totalConfigs}개 테이블)", true);
                }

                // 파일 분석을 통해 처리 모드 결정 (FileAnalysisService 직접 생성)
                var fileAnalysisService = new SpatialCheckPro.Services.FileAnalysisService(
                    (ILogger)_logger,
                    _largeFileProcessor,
                    _performanceSettings);

                var fileAnalysisResult = await fileAnalysisService.AnalyzeFileAsync(originalGdbPath);
                if (!fileAnalysisResult.IsSuccess)
                {
                    _logger.LogWarning("파일 분석 실패, 기본 모드로 진행: {Error}", fileAnalysisResult.ErrorMessage);
                }
                else
                {
                    _logger.LogInformation("파일 분석 결과 - 크기: {Size:N0} bytes, 피처: {Features:N0}, 모드: {Mode}",
                        fileAnalysisResult.FileSize, fileAnalysisResult.TotalFeatureCount, fileAnalysisResult.ProcessingMode);
                    _logger.LogInformation("스트리밍 임계값 - 크기: {SizeThreshold:N0} bytes, 피처: {FeatureThreshold:N0}",
                        _performanceSettings.HighPerformanceModeSizeThresholdBytes,
                        _performanceSettings.HighPerformanceModeFeatureThreshold);
                }

                if (_performanceSettings.ForceStreamingMode)
                {
                    _logger.LogInformation("사용자 설정에 의해 스트리밍 모드를 강제 활성화합니다.");
                }

                // 처리 모드에 따른 스트리밍 경로 결정
                string? streamingOutputPath = null;
                var recommendedSettings = fileAnalysisResult.IsSuccess ? fileAnalysisResult.RecommendedSettings : null;

                long analyzedFileSize = fileAnalysisResult.IsSuccess
                    ? fileAnalysisResult.FileSize
                    : _largeFileProcessor?.GetFileSize(originalGdbPath)
                      ?? (Directory.Exists(originalGdbPath)
                          ? CalculateDirectorySizeSafe(originalGdbPath)
                          : (File.Exists(originalGdbPath) ? new FileInfo(originalGdbPath).Length : 0));

                bool sizeThresholdExceededForStreaming =
                    analyzedFileSize >= _performanceSettings.HighPerformanceModeSizeThresholdBytes ||
                    (fileAnalysisResult.IsSuccess && fileAnalysisResult.IsLargeFile);
                bool featureThresholdExceeded = fileAnalysisResult.IsSuccess
                    && fileAnalysisResult.TotalFeatureCount >= _performanceSettings.HighPerformanceModeFeatureThreshold;
                bool recommendedStreaming = fileAnalysisResult.IsSuccess
                    && fileAnalysisResult.ProcessingMode == SpatialCheckPro.Services.ProcessingMode.Streaming;

                bool shouldEnableStreaming = ShouldEnableStreamingMode(
                    _performanceSettings.ForceStreamingMode,
                    sizeThresholdExceededForStreaming,
                    recommendedStreaming,
                    featureThresholdExceeded);

                _logger.LogInformation(
                    "스트리밍 모드 판정: Force={Force}, SizeExceeded={SizeExceeded}, FeatureExceeded={FeatureExceeded}, Recommended={Recommended}, Result={Result}",
                    _performanceSettings.ForceStreamingMode,
                    sizeThresholdExceededForStreaming,
                    featureThresholdExceeded,
                    recommendedStreaming,
                    shouldEnableStreaming);

                if (shouldEnableStreaming)
                {
                    streamingOutputPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        $"geometry_streaming_{Guid.NewGuid():N}.tmp");

                    var effectiveBatchSize = recommendedSettings?.StreamingBatchSize > 0
                        ? recommendedSettings.StreamingBatchSize
                        : _performanceSettings.StreamingBatchSize;

                    _logger.LogInformation("스트리밍 모드 활성화 - 배치 크기 {BatchSize}, 임시 경로 {Path}",
                        effectiveBatchSize,
                        streamingOutputPath);
                }
                else
                {
                    _logger.LogInformation("스트리밍 모드 미적용 - 기본 메모리 모드로 진행합니다.");
                }

                // 각 지오메트리 설정에 대해 GeometryCheckProcessor를 사용하여 검수 수행
                var geometryResults = new List<GeometryValidationItem>();
                var totalSkippedFeatures = 0;
                foreach (var config in geometryConfigs)
                {
                    var tableSucceeded = true;
                    long tableFeatureCount = 0;
                    ValidationResult? validationResult = null;
                    try
                    {
                        _logger.LogDebug("지오메트리 설정 검수 시작: {TableId}", config.TableId);

                        // 처리 전 피처 개수 확인
                        using (var ds = Ogr.Open(originalGdbPath, 0))
                        {
                            if (ds != null)
                            {
                                var layer = ds.GetLayerByName(config.TableId);
                                if (layer != null)
                                {
                                    tableFeatureCount = Math.Max(0, layer.GetFeatureCount(1));
                                }
                            }
                        }

                        // 레이어 존재 여부 확인
                        bool layerExists = false;
                        using (var ds = Ogr.Open(originalGdbPath, 0))
                        {
                            if (ds != null)
                            {
                                var layer = ds.GetLayerByName(config.TableId);
                                layerExists = layer != null;
                            }
                        }

                        // 레이어가 존재하지 않으면 스킵
                        if (!layerExists)
                        {
                            _logger.LogWarning("레이어가 존재하지 않아 지오메트리 검수를 스킵합니다: {TableId}", config.TableId);
                            processedConfigs++;
                            continue;
                        }

                        // GeometryCheckProcessor를 사용하여 검수 수행
                        validationResult = await _geometryCheckProcessor.ProcessAsync(
                            originalGdbPath,
                            config,
                            cancellationToken: default,
                            streamingOutputPath: streamingOutputPath);

                        // ValidationResult를 GeometryValidationItem으로 변환
                        List<GeometryErrorDetail> errorDetails;
                        
                        // 스트리밍 모드: 임시 파일에서 오류 읽기
                        if (!string.IsNullOrEmpty(streamingOutputPath) && File.Exists(streamingOutputPath))
                        {
                            var streamingErrors = await StreamingErrorWriter.ReadErrorsFromFileAsync(streamingOutputPath, _logger);
                            errorDetails = ConvertValidationErrorsToGeometryErrorDetails(streamingErrors);
                            _logger.LogInformation("스트리밍 파일에서 {Count}개 오류 로드: {TableId}", streamingErrors.Count, config.TableId);
                        }
                        else
                        {
                            // 일반 모드: 메모리의 Errors 사용
                            errorDetails = ConvertValidationErrorsToGeometryErrorDetails(validationResult.Errors);
                        }
                        
                        // 스트리밍 모드에서는 ErrorCount를 사용 (Errors 리스트는 비어있음)
                        var totalErrorCount = validationResult.ErrorCount > 0 ? validationResult.ErrorCount : validationResult.TotalErrors;
                        tableSucceeded = totalErrorCount == 0;
                        
                        // 오류를 타입별로 분류
                        var duplicateCount = 0;
                        var overlapCount = 0;
                        var selfIntersectionCount = 0;
                        var selfOverlapCount = 0;
                        var sliverCount = 0;
                        var spikeCount = 0;
                        var shortObjectCount = 0;
                        var smallAreaCount = 0;
                        var polygonInPolygonCount = 0;
                        var minPointCount = 0;
                        var undershootCount = 0;
                        var overshootCount = 0;
                        var basicErrorCount = 0;

                        // ErrorDetails를 순회하면서 타입별로 카운트
                        foreach (var error in errorDetails)
                        {
                            // ErrorType이 GEOM_INVALID 등으로 설정되어 있으므로 DetailMessage에서 실제 타입 추출
                            var message = error.ErrorValue?.ToLowerInvariant() ?? error.DetailMessage?.ToLowerInvariant() ?? "";
                            
                            if (message.Contains("중복") || message.Contains("duplicate"))
                            {
                                duplicateCount++;
                            }
                            else if (message.Contains("겹침") || message.Contains("overlap"))
                            {
                                overlapCount++;
                            }
                            else if (message.Contains("자체교차") || message.Contains("자기교차") || message.Contains("self") && message.Contains("intersection"))
                            {
                                selfIntersectionCount++;
                            }
                            else if (message.Contains("자기중첩") || message.Contains("자체중첩") || message.Contains("self") && message.Contains("overlap"))
                            {
                                selfOverlapCount++;
                            }
                            else if (message.Contains("슬리버") || message.Contains("sliver"))
                            {
                                sliverCount++;
                            }
                            else if (message.Contains("스파이크") || message.Contains("spike"))
                            {
                                spikeCount++;
                            }
                            else if (message.Contains("짧은") && message.Contains("객체") || message.Contains("short") && message.Contains("object"))
                            {
                                shortObjectCount++;
                            }
                            else if (message.Contains("작은") && message.Contains("면적") || message.Contains("small") && message.Contains("area"))
                            {
                                smallAreaCount++;
                            }
                            else if (message.Contains("폴리곤") && message.Contains("내") || message.Contains("홀") || message.Contains("hole") || message.Contains("polygon") && message.Contains("in"))
                            {
                                polygonInPolygonCount++;
                            }
                            else if (message.Contains("최소") && message.Contains("정점") || message.Contains("min") && message.Contains("vertices"))
                            {
                                minPointCount++;
                            }
                            else if (message.Contains("언더슛") || message.Contains("undershoot"))
                            {
                                undershootCount++;
                            }
                            else if (message.Contains("오버슛") || message.Contains("overshoot"))
                            {
                                overshootCount++;
                            }
                            else
                            {
                                basicErrorCount++;
                            }
                        }

                        // 처리된 피처 개수 설정 (스킵된 피처 제외)
                        var processedCount = tableFeatureCount - validationResult.SkippedCount;
                        
                        var item = new GeometryValidationItem
                        {
                            TableId = config.TableId,
                            TableName = config.TableName ?? config.TableId,
                            GeometryType = "Unknown", // 추후 GDAL에서 확인
                            TotalFeatureCount = (int)Math.Min(tableFeatureCount, int.MaxValue),
                            ProcessedFeatureCount = (int)Math.Min(processedCount, int.MaxValue),
                            SkippedFeatureCount = validationResult.SkippedCount,
                            BasicValidationErrorCount = basicErrorCount,
                            DuplicateCount = duplicateCount,
                            OverlapCount = overlapCount,
                            SelfIntersectionCount = selfIntersectionCount,
                            SelfOverlapCount = selfOverlapCount,
                            SliverCount = sliverCount,
                            SpikeCount = spikeCount,
                            ShortObjectCount = shortObjectCount,
                            SmallAreaCount = smallAreaCount,
                            PolygonInPolygonCount = polygonInPolygonCount,
                            MinPointCount = minPointCount,
                            UndershootCount = undershootCount,
                            OvershootCount = overshootCount,
                            ErrorDetails = errorDetails // 변환된 오류 상세 정보
                        };
                        
                        _logger.LogInformation("지오메트리 오류 타입별 분류 완료 - {TableId}: 중복={Dup}, 겹침={Ovl}, 스파이크={Spk}, 슬리버={Slv}", 
                            config.TableId, duplicateCount, overlapCount, spikeCount, sliverCount);

                        geometryResults.Add(item);
                        if (validationResult.SkippedCount > 0)
                        {
                            totalSkippedFeatures += validationResult.SkippedCount;
                            _logger.LogInformation("지오메트리 검수에서 OBJFLTN_SE 제외 적용: Table={Table}, 제외 건수={Count}", config.TableId, validationResult.SkippedCount);
                        }
                        _logger.LogDebug("지오메트리 설정 검수 완료: {TableId}, 오류: {Errors}, 경고: {Warnings}",
                            config.TableId, totalErrorCount, validationResult.WarningCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "지오메트리 설정 검수 중 오류: {TableId}", config.TableId);
                        tableSucceeded = false;
                        // 오류 발생 시 빈 결과 추가
                        geometryResults.Add(new GeometryValidationItem
                        {
                            TableId = config.TableId,
                            TableName = config.TableName ?? config.TableId,
                            BasicValidationErrorCount = 1, // 오류를 기본 검수 오류로 기록
                            ErrorDetails = new List<GeometryErrorDetail>
                            {
                                new GeometryErrorDetail
                                {
                                    ErrorType = "PROCESSING_ERROR",
                                    DetailMessage = $"처리 중 오류 발생: {ex.Message}"
                                }
                            }
                        });
                    }
                    finally
                    {
                        processedConfigs++;
                        // 처리된 피처 개수 업데이트 (스킵된 피처 제외)
                        var processedCount = tableFeatureCount - (validationResult?.SkippedCount ?? 0);
                        var message = $"지오메트리 검수 중... ({processedConfigs}/{totalConfigs}) {config.TableName ?? config.TableId}";
                        PublishGeometryProgress(message, tableSucceeded, markCompleted: false, additionalProcessed: processedCount);
                    }
                }
                result.GeometryResults = geometryResults;
                result.ErrorCount = geometryResults.Sum(r => r.TotalErrorCount); // TotalErrorCount 사용
                result.WarningCount = 0; // 현재는 경고를 별도로 처리하지 않음
                result.IsValid = result.ErrorCount == 0;
                result.SkippedCount = totalSkippedFeatures;
                
                // 통계 설정
                result.TotalTableCount = geometryConfigs.Select(c => c.TableId).Distinct().Count();
                result.ProcessedTableCount = geometryResults.Count(r => r.ProcessedFeatureCount > 0);
                result.SkippedTableCount = result.TotalTableCount - result.ProcessedTableCount;
                
                _logger.LogInformation("3단계 통계: 전체 {Total}개, 처리 {Processed}개, 스킵 {Skipped}개", 
                    result.TotalTableCount, result.ProcessedTableCount, result.SkippedTableCount);
                
                // 최종 진행률 보고 (100% 완료)
                PublishGeometryProgress("지오메트리 검수 완료", true, markCompleted: true);
                
                // 스트리밍 임시 파일 정리
                if (!string.IsNullOrEmpty(streamingOutputPath) && File.Exists(streamingOutputPath))
                {
                    try
                    {
                        File.Delete(streamingOutputPath);
                        _logger.LogInformation("스트리밍 임시 파일 삭제 완료: {Path}", streamingOutputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "스트리밍 임시 파일 삭제 실패: {Path}", streamingOutputPath);
                    }
                }
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
        /// ValidationError 목록을 GeometryErrorDetail 목록으로 변환합니다
        /// </summary>
        private List<GeometryErrorDetail> ConvertValidationErrorsToGeometryErrorDetails(List<ValidationError> validationErrors)
        {
            var geometryErrorDetails = new List<GeometryErrorDetail>();

            foreach (var validationError in validationErrors)
            {
                var geometryErrorDetail = new GeometryErrorDetail
                {
                    ObjectId = validationError.FeatureId ?? string.Empty,
                    ErrorType = validationError.ErrorCode ?? "UNKNOWN_ERROR",
                    ErrorValue = validationError.Message,
                    ThresholdValue = string.Empty, // ValidationError에는 임계값이 없음
                    Location = $"{validationError.X ?? 0},{validationError.Y ?? 0}",
                    DetailMessage = validationError.Message,
                    X = validationError.X ?? 0,
                    Y = validationError.Y ?? 0,
                    GeometryWkt = validationError.GeometryWKT
                };

                geometryErrorDetails.Add(geometryErrorDetail);
            }

            return geometryErrorDetails;
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

                result.IsValid = result.ErrorCount == 0;

                if (_attributeCheckProcessor is AttributeCheckProcessor attributeProcessor)
                {
                    result.SkippedCount = attributeProcessor.LastSkippedFeatureCount;
                }
                else
                {
                    result.SkippedCount = 0;
                }
                if (result.SkippedCount > 0)
                {
                    _logger.LogInformation("속성 검수에서 OBJFLTN_SE 제외 적용: 제외 건수 {Count}", result.SkippedCount);
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
                _isProcessingRelationCheck = true; // 관계 검수 시작 - 개별 규칙 진행률 무시
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
                var allRelationConfigs = await LoadFlexibleRelationConfigsAsync(relationConfigPath);
                if (!allRelationConfigs.Any())
                        {
                            result.WarningCount++;
                    result.Message = "관계 설정이 없어 관계 검수를 스킵했습니다.";
                    _logger.LogWarning("관계 설정이 비어있습니다: {Path}", relationConfigPath);
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // Enabled=Y인 규칙만 필터링 (비활성화된 규칙 제외)
                var relationConfigs = allRelationConfigs
                    .Where(c => string.Equals(c.Enabled, "Y", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _logger.LogInformation("활성화된 관계 검수 규칙: {EnabledCount}개 (전체: {TotalCount}개)", 
                    relationConfigs.Count, allRelationConfigs.Count);

                // 선택된 행이 있으면 필터링
                if (selectedRows != null && selectedRows.Any())
                {
                    var selectedKeys = selectedRows.Select(r => $"{r.MainTableId}_{r.RelatedTableId}_{r.CaseType}").ToHashSet();
                    relationConfigs = relationConfigs.Where(c => selectedKeys.Contains($"{c.MainTableId}_{c.RelatedTableId}_{c.CaseType}")).ToList();
                    _logger.LogInformation("선택된 관계만 검수: {Count}개", relationConfigs.Count);
                }

                _logger.LogInformation("관계 검수 대상: {Count}개", relationConfigs.Count);
                
                // 관계 검수 실행
                var totalSkippedRelation = 0;
                var totalRules = relationConfigs.Count;
                var processedRules = 0;
                var actuallyProcessedRules = 0; // 실제로 처리된 규칙 수 (ProcessAsync 성공적으로 완료된 규칙만)
                
                // 실제 규칙 수로 초기 진행률 보고 (0/totalRules로 시작)
                _logger.LogInformation("[ExecuteRelationCheckAsync] 초기 진행률 보고: 0/{TotalRules} 규칙", totalRules);
                
                var initialProgressArgs = new ValidationProgressEventArgs
                {
                    CurrentStage = 5,
                    StageName = "공간 관계 검수",
                    OverallProgress = 80.0,
                    StageProgress = 0,
                    StatusMessage = $"공간 관계 검수 시작 - 총 {totalRules}개 규칙",
                    IsStageCompleted = false,
                    IsStageSuccessful = true,
                    ProcessedUnits = 0,
                    TotalUnits = totalRules
                };
                _logger.LogInformation("[ExecuteRelationCheckAsync] ProgressUpdated 이벤트 발생: ProcessedUnits={P}, TotalUnits={T}", 
                    initialProgressArgs.ProcessedUnits, initialProgressArgs.TotalUnits);
                ProgressUpdated?.Invoke(this, initialProgressArgs);
                
                foreach (var rule in relationConfigs)
                {
                    processedRules++;
                    
                    // 규칙별 진행률 계산
                    var ruleProgress = (processedRules - 1) * 100.0 / totalRules;
                    var statusMsg = $"규칙 {processedRules}/{totalRules} 처리 중... ({rule.RuleId})";
                    
                    // 진행 중 이벤트 발생 (규칙 시작 시)
                    var progressArgs = new ValidationProgressEventArgs
                    {
                        CurrentStage = 5,
                        StageName = "공간 관계 검수",
                        OverallProgress = 80 + (ruleProgress * 0.20), // 5단계는 80~100% 구간
                        StageProgress = ruleProgress,
                        StatusMessage = statusMsg,
                        IsStageCompleted = false,
                        IsStageSuccessful = true,
                        ProcessedUnits = processedRules - 1,
                        TotalUnits = totalRules
                    };
                    ProgressUpdated?.Invoke(this, progressArgs);
                    
                    try
                    {
                        var vr = await _relationProcessor.ProcessAsync(dataSourcePath, rule);
                        
                        // ProcessAsync가 성공적으로 완료된 경우에만 카운트 (예외 없이 완료)
                        actuallyProcessedRules++;
                        
                        if (!vr.IsValid)
                        {
                            result.IsValid = false;
                        }
                        // ErrorCount는 Errors.Count와 동기화되어야 하므로, Errors를 먼저 추가한 후 Count로 재계산
                        if (vr.Errors != null && vr.Errors.Count > 0)
                        {
                            result.Errors.AddRange(vr.Errors);
                            // Errors.Count를 기준으로 ErrorCount 재계산 (동기화 보장)
                            result.ErrorCount = result.Errors.Count;
                        }
                        else
                        {
                            // Errors가 없어도 ErrorCount는 누적 (vr.ErrorCount가 0이 아닐 수 있음)
                            result.ErrorCount += vr.ErrorCount;
                        }
                        if (vr.SkippedCount > 0)
                        {
                            totalSkippedRelation += vr.SkippedCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        // ProcessAsync 실패 시에도 규칙은 처리 시도한 것으로 간주 (로그만 기록)
                        _logger.LogWarning(ex, "규칙 처리 중 오류 발생 (계속 진행): RuleId={RuleId}, CaseType={CaseType}", 
                            rule.RuleId, rule.CaseType);
                        // 예외가 발생해도 규칙은 처리 시도한 것으로 카운트 (실제 처리 여부와 무관)
                        actuallyProcessedRules++;
                    }
                    
                    // 규칙 완료 시 진행률 업데이트 + 부분 결과 생성
                    var completedProgress = processedRules * 100.0 / totalRules;
                    var completedMsg = $"규칙 {processedRules}/{totalRules} 완료 ({rule.RuleId})";
                    
                    // 현재까지의 부분 결과 생성 (규칙 완료마다)
                    ValidationResult? currentPartialResult = null;
                    if (processedRules == totalRules || processedRules % 5 == 0) // 마지막 규칙 또는 5개마다
                    {
                        currentPartialResult = new ValidationResult
                        {
                            ValidationId = Guid.NewGuid().ToString(),
                            TargetFile = dataSourcePath,
                            StartedAt = result.StartedAt,
                            Status = ValidationStatus.Running,
                            RelationCheckResult = new RelationCheckResult
                            {
                                StartedAt = result.StartedAt,
                                ErrorCount = result.ErrorCount,
                                WarningCount = result.WarningCount,
                                Errors = new List<ValidationError>(result.Errors),
                                ProcessedRulesCount = processedRules
                            },
                            ErrorCount = result.ErrorCount,
                            WarningCount = result.WarningCount
                        };
                    }
                    
                    var completedArgs = new ValidationProgressEventArgs
                    {
                        CurrentStage = 5,
                        StageName = "공간 관계 검수",
                        OverallProgress = 80 + (completedProgress * 0.20), // 5단계는 80~100% 구간
                        StageProgress = completedProgress,
                        StatusMessage = completedMsg,
                        IsStageCompleted = processedRules == totalRules,
                        IsStageSuccessful = true,
                        ProcessedUnits = processedRules,
                        TotalUnits = totalRules,
                        ErrorCount = result.ErrorCount,
                        WarningCount = result.WarningCount,
                        PartialResult = currentPartialResult
                    };
                    ProgressUpdated?.Invoke(this, completedArgs);
                }

                // ErrorCount와 Errors.Count 동기화 보장 (재분류 후에도 정확성 유지)
                result.ErrorCount = Math.Max(0, result.Errors.Count);
                result.IsValid = result.ErrorCount == 0;
                result.SkippedCount = totalSkippedRelation;
                if (result.SkippedCount > 0)
                {
                    _logger.LogInformation("관계 검수에서 OBJFLTN_SE 제외 적용: 제외 건수 {Count}", result.SkippedCount);
                }

                // 통계 설정: 실제 처리된 규칙 수 사용 (ProcessAsync가 호출된 규칙만)
                // actuallyProcessedRules는 foreach 루프에서 실제로 ProcessAsync가 호출된 규칙 수
                // relationConfigs.Count는 활성화된 규칙 수 (Enabled=Y)
                result.ProcessedRulesCount = actuallyProcessedRules > 0 ? actuallyProcessedRules : relationConfigs.Count;
                _logger.LogInformation("5단계 통계: 검사한 규칙 {Count}개 (활성화된 규칙: {EnabledCount}개, 실제 처리 시도: {ProcessedCount}개), 오류 {Error}개 (REL_CENTERLINE_ATTR_MISMATCH 제외)", 
                    result.ProcessedRulesCount, relationConfigs.Count, actuallyProcessedRules, result.ErrorCount);

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
            finally
            {
                _isProcessingRelationCheck = false; // 관계 검수 종료 - 개별 규칙 진행률 다시 활성화
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

        /// <summary>
        /// FileGDB 폴더가 유효한지 기본 검증
        /// </summary>
        /// <param name="gdbPath">FileGDB 폴더 경로</param>
        /// <returns>유효한 FileGDB 폴더인지 여부</returns>
        private bool IsValidFileGdbDirectory(string gdbPath)
        {
            try
            {
                if (!Directory.Exists(gdbPath))
                    return false;

                // 핵심 .gdbtable 파일 존재 여부 확인
                var gdbTableExists = Directory.EnumerateFiles(gdbPath, "*.gdbtable", SearchOption.TopDirectoryOnly).Any();
                if (!gdbTableExists)
                    return false;

                // 인덱스 파일(.gdbtablx) 또는 시스템 파일(gdb) 존재 여부 확인 (선택적)
                var hasIndexOrSystemFile = Directory.EnumerateFiles(gdbPath, "*.gdbtablx", SearchOption.TopDirectoryOnly).Any()
                    || File.Exists(Path.Combine(gdbPath, "gdb"));

                return hasIndexOrSystemFile || gdbTableExists;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileGDB 폴더 검증 중 오류 발생: {GdbPath}", gdbPath);
                return false;
            }
        }

        /// <summary>
        /// 디렉터리 크기를 안전하게 계산합니다
        /// </summary>
        /// <param name="directoryPath">디렉터리 경로</param>
        /// <returns>디렉터리 내 파일 크기 합계</returns>
        private long CalculateDirectorySizeSafe(string directoryPath)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Select(file =>
                    {
                        try
                        {
                            return new FileInfo(file).Length;
                        }
                        catch
                        {
                            return 0L;
                        }
                    })
                    .Sum();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "디렉터리 크기 계산 실패: {DirectoryPath}", directoryPath);
                return 0;
            }
        }

        private bool ShouldEnableStreamingMode(bool forceStreaming, bool sizeThresholdExceeded, bool recommendedStreaming, bool featureThresholdExceeded)
        {
            if (forceStreaming)
            {
                return true;
            }

            if (recommendedStreaming)
            {
                return true;
            }

            if (featureThresholdExceeded)
            {
                return true;
            }

            return sizeThresholdExceeded;
        }
    }
}