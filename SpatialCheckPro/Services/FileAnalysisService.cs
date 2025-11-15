using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Diagnostics;
using PerformanceSettings = SpatialCheckPro.Models.Config.PerformanceSettings;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 파일 분석 및 대용량 처리 모드 결정 서비스
    /// </summary>
    public class FileAnalysisService
    {
        private readonly ILogger _logger;
        private readonly ILargeFileProcessor _largeFileProcessor;
        private readonly PerformanceSettings _performanceSettings;

        public FileAnalysisService(
            ILogger<FileAnalysisService> logger,
            ILargeFileProcessor largeFileProcessor,
            PerformanceSettings performanceSettings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _largeFileProcessor = largeFileProcessor ?? throw new ArgumentNullException(nameof(largeFileProcessor));
            _performanceSettings = performanceSettings ?? throw new ArgumentNullException(nameof(performanceSettings));
        }

        // ILogger를 직접 받는 생성자 추가 (호환성 유지)
        public FileAnalysisService(
            ILogger logger,
            ILargeFileProcessor largeFileProcessor,
            PerformanceSettings performanceSettings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _largeFileProcessor = largeFileProcessor ?? throw new ArgumentNullException(nameof(largeFileProcessor));
            _performanceSettings = performanceSettings ?? throw new ArgumentNullException(nameof(performanceSettings));
        }

        /// <summary>
        /// 파일을 분석하여 처리 모드를 결정합니다
        /// </summary>
        public async Task<FileAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var result = new FileAnalysisResult
            {
                FilePath = filePath,
                AnalysisStartTime = DateTime.Now
            };

            try
            {
                _logger.LogInformation("파일 분석 시작: {FilePath}", filePath);

                // 1. 기본 파일 정보 수집
                result.FileSize = _largeFileProcessor.GetFileSize(filePath);
                result.IsLargeFile = _largeFileProcessor.IsLargeFile(filePath, _performanceSettings.HighPerformanceModeSizeThresholdBytes);

                // 2. GDAL을 통한 파일 구조 분석
                var structureInfo = await AnalyzeFileStructureAsync(filePath, cancellationToken);
                result.LayerCount = structureInfo.LayerCount;
                result.TotalFeatureCount = structureInfo.TotalFeatureCount;
                result.HasGeometryData = structureInfo.HasGeometryData;

                // 3. 처리 모드 결정
                result.ProcessingMode = DetermineProcessingMode(result);

                // 4. 권장 설정 계산
                result.RecommendedSettings = CalculateRecommendedSettings(result);

                result.AnalysisCompletedTime = DateTime.Now;
                result.AnalysisDuration = result.AnalysisCompletedTime - result.AnalysisStartTime;

                _logger.LogInformation("파일 분석 완료: {FilePath}, 크기: {FileSize:N0} bytes, 피처: {FeatureCount:N0}, 모드: {Mode}",
                    filePath, result.FileSize, result.TotalFeatureCount, result.ProcessingMode);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 분석 중 오류 발생: {FilePath}", filePath);
                result.ErrorMessage = ex.Message;
                result.AnalysisCompletedTime = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// GDAL을 통해 파일 구조를 분석합니다
        /// </summary>
        private async Task<FileStructureInfo> AnalyzeFileStructureAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var info = new FileStructureInfo();

                try
                {
                    using var ds = Ogr.Open(filePath, 0);
                    if (ds == null)
                    {
                        throw new InvalidOperationException($"파일을 열 수 없습니다: {filePath}");
                    }

                    info.LayerCount = ds.GetLayerCount();
                    info.HasGeometryData = false;

                    for (int i = 0; i < info.LayerCount; i++)
                    {
                        using var layer = ds.GetLayerByIndex(i);
                        if (layer != null)
                        {
                            var featureCount = layer.GetFeatureCount(1); // 정확한 카운트를 위해 1을 전달
                            info.TotalFeatureCount += featureCount;

                            // 지오메트리 데이터 존재 여부 확인
                            if (!info.HasGeometryData)
                            {
                                var geomType = layer.GetGeomType();
                                info.HasGeometryData = geomType != wkbGeometryType.wkbNone;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "파일 구조 분석 중 오류: {FilePath}", filePath);
                }

                return info;
            }, cancellationToken);
        }

        /// <summary>
        /// 파일 특성에 따른 처리 모드를 결정합니다
        /// </summary>
        private ProcessingMode DetermineProcessingMode(FileAnalysisResult analysis)
        {
            // 1. 파일 크기 기반 기본 모드 결정
            if (analysis.IsLargeFile)
            {
                return ProcessingMode.Streaming;
            }

            // 2. 피처 수 기반 모드 결정
            if (analysis.TotalFeatureCount >= _performanceSettings.HighPerformanceModeFeatureThreshold)
            {
                return ProcessingMode.Streaming;
            }

            // 3. 메모리 사용량 기반 모드 결정
            var estimatedMemoryUsage = EstimateMemoryUsage(analysis);
            if (estimatedMemoryUsage >= _performanceSettings.MemoryPressureThresholdMB * 1024 * 1024)
            {
                return ProcessingMode.Streaming;
            }

            // 4. 기본 모드 (메모리 모드)
            return ProcessingMode.Memory;
        }

        /// <summary>
        /// 예상 메모리 사용량을 계산합니다
        /// </summary>
        private long EstimateMemoryUsage(FileAnalysisResult analysis)
        {
            // 대략적인 메모리 사용량 추정 (실제로는 더 복잡한 계산 필요)
            // 지오메트리 데이터의 경우 피처당 약 1-5KB, 속성 데이터 포함
            const long bytesPerFeature = 2048; // 2KB per feature (conservative estimate)
            return analysis.TotalFeatureCount * bytesPerFeature;
        }

        /// <summary>
        /// 분석 결과에 따른 권장 설정을 계산합니다
        /// </summary>
        private RecommendedSettings CalculateRecommendedSettings(FileAnalysisResult analysis)
        {
            var settings = new RecommendedSettings();

            switch (analysis.ProcessingMode)
            {
                case ProcessingMode.Streaming:
                    settings.EnableStreamingMode = true;
                    settings.StreamingBatchSize = Math.Min(1000, Math.Max(100, (int)(analysis.TotalFeatureCount / 100)));
                    settings.EnableParallelProcessing = false; // 스트리밍 모드에서는 순차 처리 권장
                    settings.MaxDegreeOfParallelism = 1;
                    settings.EnableMemoryOptimization = true;
                    break;

                case ProcessingMode.Parallel:
                    settings.EnableStreamingMode = false;
                    settings.EnableParallelProcessing = true;
                    settings.MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, analysis.LayerCount);
                    settings.EnableMemoryOptimization = false;
                    break;

                case ProcessingMode.Memory:
                default:
                    settings.EnableStreamingMode = false;
                    settings.EnableParallelProcessing = _performanceSettings.EnableParallelProcessing;
                    settings.MaxDegreeOfParallelism = _performanceSettings.MaxDegreeOfParallelism;
                    settings.EnableMemoryOptimization = false;
                    break;
            }

            return settings;
        }

        /// <summary>
        /// 파일 구조 정보
        /// </summary>
        private class FileStructureInfo
        {
            public int LayerCount { get; set; }
            public long TotalFeatureCount { get; set; }
            public bool HasGeometryData { get; set; }
        }
    }

    /// <summary>
    /// 파일 분석 결과
    /// </summary>
    public class FileAnalysisResult
    {
        /// <summary>분석 대상 파일 경로</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>파일 크기 (바이트)</summary>
        public long FileSize { get; set; }

        /// <summary>대용량 파일 여부</summary>
        public bool IsLargeFile { get; set; }

        /// <summary>레이어 수</summary>
        public int LayerCount { get; set; }

        /// <summary>총 피처 수</summary>
        public long TotalFeatureCount { get; set; }

        /// <summary>지오메트리 데이터 포함 여부</summary>
        public bool HasGeometryData { get; set; }

        /// <summary>권장 처리 모드</summary>
        public ProcessingMode ProcessingMode { get; set; }

        /// <summary>권장 설정</summary>
        public RecommendedSettings RecommendedSettings { get; set; } = new();

        /// <summary>분석 시작 시간</summary>
        public DateTime AnalysisStartTime { get; set; }

        /// <summary>분석 완료 시간</summary>
        public DateTime AnalysisCompletedTime { get; set; }

        /// <summary>분석 소요 시간</summary>
        public TimeSpan AnalysisDuration { get; set; }

        /// <summary>오류 메시지</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>분석 성공 여부</summary>
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// 처리 모드 열거형
    /// </summary>
    public enum ProcessingMode
    {
        /// <summary>메모리에 모든 데이터를 로드하여 처리</summary>
        Memory,

        /// <summary>스트리밍 방식으로 순차 처리</summary>
        Streaming,

        /// <summary>병렬 처리 방식</summary>
        Parallel
    }

    /// <summary>
    /// 권장 설정
    /// </summary>
    public class RecommendedSettings
    {
        /// <summary>스트리밍 모드 활성화</summary>
        public bool EnableStreamingMode { get; set; }

        /// <summary>스트리밍 배치 크기</summary>
        public int StreamingBatchSize { get; set; }

        /// <summary>병렬 처리 활성화</summary>
        public bool EnableParallelProcessing { get; set; }

        /// <summary>최대 병렬도</summary>
        public int MaxDegreeOfParallelism { get; set; }

        /// <summary>메모리 최적화 활성화</summary>
        public bool EnableMemoryOptimization { get; set; }
    }
}
