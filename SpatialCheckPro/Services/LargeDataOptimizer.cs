using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 데이터 처리 최적화를 위한 서비스
    /// 10GB 이상 파일을 30분 이내에 처리하기 위한 최적화 기능 제공
    /// </summary>
    public class LargeDataOptimizer : ILargeDataOptimizer
    {
        private readonly ILogger<LargeDataOptimizer> _logger;
        private readonly IMemoryManager _memoryManager;
        private readonly IStreamingDataProcessor _streamingProcessor;
        private readonly IGdalDataReader _gdalDataReader;
        
        // 성능 최적화 설정
        private readonly int _defaultBatchSize = 10000;
        private readonly int _minBatchSize = 1000;
        private readonly int _maxBatchSize = 50000;
        private readonly long _targetProcessingTimeMs = 30 * 60 * 1000; // 30분
        
        // 동적 최적화 상태
        private int _currentBatchSize;
        private double _averageProcessingTimePerBatch = 0;
        private int _processedBatches = 0;
        private readonly object _optimizationLock = new object();

        public LargeDataOptimizer(
            ILogger<LargeDataOptimizer> logger,
            IMemoryManager memoryManager,
            IStreamingDataProcessor streamingProcessor,
            IGdalDataReader gdalDataReader)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
            _streamingProcessor = streamingProcessor ?? throw new ArgumentNullException(nameof(streamingProcessor));
            _gdalDataReader = gdalDataReader ?? throw new ArgumentNullException(nameof(gdalDataReader));
            
            _currentBatchSize = _defaultBatchSize;
        }

        /// <summary>
        /// 대용량 파일의 크기를 분석하고 최적 처리 전략을 결정합니다
        /// </summary>
        public async Task<LargeDataProcessingStrategy> AnalyzeFileAndCreateStrategyAsync(
            string gdbPath, 
            List<string> tableNames,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("대용량 파일 분석 시작: {GdbPath}", gdbPath);
            
            var stopwatch = Stopwatch.StartNew();
            var strategy = new LargeDataProcessingStrategy
            {
                GdbPath = gdbPath,
                TableNames = tableNames,
                AnalysisStartTime = DateTime.UtcNow
            };

            try
            {
                // 1. 파일 크기 분석
                var fileInfo = new FileInfo(gdbPath);
                strategy.FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
                strategy.FileSizeMB = strategy.FileSizeBytes / (1024.0 * 1024.0);
                
                _logger.LogInformation("파일 크기: {FileSizeMB:F2}MB", strategy.FileSizeMB);

                // 2. 테이블별 레코드 수 분석
                var tableSizes = new Dictionary<string, long>();
                long totalRecords = 0;

                foreach (var tableName in tableNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var recordCount = await _gdalDataReader.GetRecordCountAsync(gdbPath, tableName);
                    tableSizes[tableName] = recordCount;
                    totalRecords += recordCount;
                    
                    _logger.LogDebug("테이블 크기 분석: {TableName} = {RecordCount:N0}개", tableName, recordCount);
                }

                strategy.TableSizes = tableSizes;
                strategy.TotalRecords = totalRecords;

                // 3. 처리 복잡도 추정
                strategy.EstimatedComplexity = EstimateProcessingComplexity(strategy);
                
                // 4. 최적 배치 크기 결정
                strategy.OptimalBatchSize = CalculateOptimalBatchSize(strategy);
                
                // 5. 예상 처리 시간 계산
                strategy.EstimatedProcessingTimeMinutes = EstimateProcessingTime(strategy);
                
                // 6. 메모리 요구사항 분석
                strategy.EstimatedMemoryRequirementMB = EstimateMemoryRequirement(strategy);
                
                // 7. 처리 전략 결정
                strategy.ProcessingMode = DetermineProcessingMode(strategy);
                
                stopwatch.Stop();
                strategy.AnalysisTimeMs = stopwatch.ElapsedMilliseconds;
                
                _logger.LogInformation("파일 분석 완료 - 총 레코드: {TotalRecords:N0}개, " +
                                     "예상 처리시간: {EstimatedTime:F1}분, " +
                                     "최적 배치크기: {BatchSize:N0}, " +
                                     "처리모드: {ProcessingMode}",
                    strategy.TotalRecords, 
                    strategy.EstimatedProcessingTimeMinutes,
                    strategy.OptimalBatchSize,
                    strategy.ProcessingMode);

                return strategy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 분석 중 오류 발생");
                strategy.HasError = true;
                strategy.ErrorMessage = ex.Message;
                return strategy;
            }
        }

        /// <summary>
        /// 최적화된 스트리밍 방식으로 대용량 데이터를 처리합니다
        /// </summary>
        public async IAsyncEnumerable<TResult> ProcessLargeDataStreamAsync<TSource, TResult>(
            string gdbPath,
            string tableName,
            Func<IEnumerable<TSource>, Task<IEnumerable<TResult>>> processor,
            Func<OSGeo.OGR.Feature, TSource> featureConverter,
            LargeDataProcessingStrategy strategy,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("대용량 데이터 스트리밍 처리 시작: {TableName}", tableName);
            
            var totalProcessed = 0L;
            var batchCount = 0;
            var overallStopwatch = Stopwatch.StartNew();
            
            // 초기 배치 크기 설정
            _currentBatchSize = strategy.OptimalBatchSize;

            // 피처 스트림을 소스 객체로 변환
            var sourceStream = ConvertFeatureStreamToSourceStream(gdbPath, tableName, featureConverter, cancellationToken);
            
            // 스트리밍 프로세서를 사용하여 청크 단위 처리
            await foreach (var result in _streamingProcessor.ProcessInChunksAsync(
                sourceStream, 
                processor, 
                cancellationToken))
            {
                yield return result;
                totalProcessed++;
                
                // 배치 완료 시 성능 최적화
                if (totalProcessed % _currentBatchSize == 0)
                {
                    batchCount++;
                    await OptimizeBatchSizeAsync(batchCount, overallStopwatch.ElapsedMilliseconds, strategy);
                    
                    // 진행률 로깅
                    if (batchCount % 10 == 0)
                    {
                        var progressPercent = (double)totalProcessed / strategy.TableSizes[tableName] * 100;
                        var elapsedMinutes = overallStopwatch.ElapsedMilliseconds / 60000.0;
                        var estimatedTotalMinutes = elapsedMinutes / progressPercent * 100;
                        
                        _logger.LogInformation("처리 진행률: {Progress:F1}% ({Processed:N0}/{Total:N0}), " +
                                             "경과시간: {Elapsed:F1}분, 예상완료: {Estimated:F1}분",
                            progressPercent, totalProcessed, strategy.TableSizes[tableName],
                            elapsedMinutes, estimatedTotalMinutes);
                    }
                }
            }

            overallStopwatch.Stop();
            
            _logger.LogInformation("대용량 데이터 처리 완료: {TableName} - " +
                                 "처리된 레코드: {TotalProcessed:N0}개, " +
                                 "총 소요시간: {ElapsedMinutes:F2}분, " +
                                 "평균 처리속도: {RecordsPerSecond:F0}개/초",
                tableName, totalProcessed, overallStopwatch.ElapsedMilliseconds / 60000.0,
                totalProcessed / (overallStopwatch.ElapsedMilliseconds / 1000.0));
        }

        /// <summary>
        /// 메모리 사용량을 모니터링하고 자동으로 최적화합니다
        /// </summary>
        public async Task<bool> MonitorAndOptimizeMemoryAsync(CancellationToken cancellationToken = default)
        {
            var memoryStats = _memoryManager.GetMemoryStatistics();
            
            _logger.LogDebug("메모리 모니터링: 사용량 {CurrentMB:F2}MB / {MaxMB:F2}MB ({Ratio:P1}), " +
                           "압박상태: {IsUnderPressure}",
                memoryStats.CurrentMemoryUsage / (1024.0 * 1024.0),
                memoryStats.MaxMemoryLimit / (1024.0 * 1024.0),
                memoryStats.PressureRatio,
                memoryStats.IsUnderPressure);

            if (memoryStats.IsUnderPressure)
            {
                _logger.LogWarning("메모리 압박 상황 감지 - 자동 최적화 수행");
                
                // 1. 배치 크기 감소
                lock (_optimizationLock)
                {
                    var newBatchSize = Math.Max(_minBatchSize, _currentBatchSize / 2);
                    if (newBatchSize != _currentBatchSize)
                    {
                        _logger.LogInformation("배치 크기 자동 조정: {OldSize} -> {NewSize}", _currentBatchSize, newBatchSize);
                        _currentBatchSize = newBatchSize;
                    }
                }
                
                // 2. 메모리 정리 수행
                var success = await _memoryManager.TryReduceMemoryPressureAsync();
                
                if (!success)
                {
                    _logger.LogWarning("메모리 압박 해소 실패 - 처리 속도 저하 가능");
                    
                    // 3. 추가 대기 시간
                    await Task.Delay(2000, cancellationToken);
                }
                
                return success;
            }
            
            return true;
        }

        /// <summary>
        /// 처리 성능을 분석하고 동적으로 배치 크기를 최적화합니다
        /// </summary>
        private async Task OptimizeBatchSizeAsync(int batchCount, long elapsedMs, LargeDataProcessingStrategy strategy)
        {
            lock (_optimizationLock)
            {
                // 평균 처리 시간 업데이트
                var currentBatchTime = elapsedMs / (double)batchCount;
                _averageProcessingTimePerBatch = (_averageProcessingTimePerBatch * _processedBatches + currentBatchTime) / (_processedBatches + 1);
                _processedBatches++;

                // 목표 시간 대비 현재 진행률 계산
                var projectedTotalTime = _averageProcessingTimePerBatch * (strategy.TotalRecords / _currentBatchSize);
                var timeRatio = projectedTotalTime / _targetProcessingTimeMs;

                // 배치 크기 동적 조정
                int newBatchSize = _currentBatchSize;
                
                if (timeRatio > 1.2) // 목표 시간보다 20% 이상 느림
                {
                    // 메모리 여유가 있으면 배치 크기 증가
                    if (!_memoryManager.IsMemoryPressureHigh() && _currentBatchSize < _maxBatchSize)
                    {
                        newBatchSize = Math.Min(_maxBatchSize, (int)(_currentBatchSize * 1.5));
                    }
                }
                else if (timeRatio < 0.8) // 목표 시간보다 20% 이상 빠름
                {
                    // 메모리 압박이 있으면 배치 크기 감소
                    if (_memoryManager.IsMemoryPressureHigh() && _currentBatchSize > _minBatchSize)
                    {
                        newBatchSize = Math.Max(_minBatchSize, (int)(_currentBatchSize * 0.8));
                    }
                }

                if (newBatchSize != _currentBatchSize)
                {
                    _logger.LogDebug("배치 크기 동적 조정: {OldSize} -> {NewSize} " +
                                   "(평균 배치시간: {AvgTime:F2}ms, 예상 총 시간: {ProjectedTime:F1}분)",
                        _currentBatchSize, newBatchSize, _averageProcessingTimePerBatch, projectedTotalTime / 60000.0);
                    
                    _currentBatchSize = newBatchSize;
                }
            }

            // 주기적 메모리 최적화
            if (batchCount % 5 == 0)
            {
                await MonitorAndOptimizeMemoryAsync();
            }
        }

        /// <summary>
        /// 피처 스트림을 소스 객체 스트림으로 변환합니다
        /// </summary>
        private async IAsyncEnumerable<TSource> ConvertFeatureStreamToSourceStream<TSource>(
            string gdbPath,
            string tableName,
            Func<OSGeo.OGR.Feature, TSource> converter,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var feature in _gdalDataReader.GetFeaturesStreamAsync(gdbPath, tableName, cancellationToken))
            {
                TSource sourceObject;
                try
                {
                    sourceObject = converter(feature);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "피처 변환 실패 - 건너뜀");
                    continue;
                }
                
                yield return sourceObject;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 처리 복잡도를 추정합니다
        /// </summary>
        private ProcessingComplexity EstimateProcessingComplexity(LargeDataProcessingStrategy strategy)
        {
            var totalRecords = strategy.TotalRecords;
            var fileSizeMB = strategy.FileSizeMB;
            
            if (totalRecords > 10_000_000 || fileSizeMB > 10_000) // 1천만 레코드 또는 10GB 이상
                return ProcessingComplexity.VeryHigh;
            else if (totalRecords > 5_000_000 || fileSizeMB > 5_000) // 5백만 레코드 또는 5GB 이상
                return ProcessingComplexity.High;
            else if (totalRecords > 1_000_000 || fileSizeMB > 1_000) // 1백만 레코드 또는 1GB 이상
                return ProcessingComplexity.Medium;
            else
                return ProcessingComplexity.Low;
        }

        /// <summary>
        /// 최적 배치 크기를 계산합니다
        /// </summary>
        private int CalculateOptimalBatchSize(LargeDataProcessingStrategy strategy)
        {
            var baseSize = _defaultBatchSize;
            
            // 복잡도에 따른 조정
            var complexityMultiplier = strategy.EstimatedComplexity switch
            {
                ProcessingComplexity.VeryHigh => 0.5,
                ProcessingComplexity.High => 0.75,
                ProcessingComplexity.Medium => 1.0,
                ProcessingComplexity.Low => 1.5,
                _ => 1.0
            };
            
            // 메모리 상황에 따른 조정
            var memoryMultiplier = _memoryManager.IsMemoryPressureHigh() ? 0.5 : 1.0;
            
            var optimalSize = (int)(baseSize * complexityMultiplier * memoryMultiplier);
            
            return Math.Max(_minBatchSize, Math.Min(_maxBatchSize, optimalSize));
        }

        /// <summary>
        /// 예상 처리 시간을 계산합니다 (분 단위)
        /// </summary>
        private double EstimateProcessingTime(LargeDataProcessingStrategy strategy)
        {
            // 레코드당 평균 처리 시간 (밀리초) - 복잡도별 추정
            var avgTimePerRecord = strategy.EstimatedComplexity switch
            {
                ProcessingComplexity.VeryHigh => 2.0,
                ProcessingComplexity.High => 1.5,
                ProcessingComplexity.Medium => 1.0,
                ProcessingComplexity.Low => 0.5,
                _ => 1.0
            };
            
            var totalTimeMs = strategy.TotalRecords * avgTimePerRecord;
            return totalTimeMs / 60000.0; // 분 단위로 변환
        }

        /// <summary>
        /// 예상 메모리 요구사항을 계산합니다 (MB 단위)
        /// </summary>
        private double EstimateMemoryRequirement(LargeDataProcessingStrategy strategy)
        {
            // 배치당 평균 메모리 사용량 추정
            var avgMemoryPerBatch = strategy.OptimalBatchSize * 0.001; // 레코드당 1KB 추정
            
            // 버퍼링 및 처리 오버헤드 고려
            var totalMemoryMB = avgMemoryPerBatch * 2; // 2배 버퍼
            
            return Math.Max(100, totalMemoryMB); // 최소 100MB
        }

        /// <summary>
        /// 처리 모드를 결정합니다
        /// </summary>
        private DataProcessingMode DetermineProcessingMode(LargeDataProcessingStrategy strategy)
        {
            if (strategy.EstimatedProcessingTimeMinutes > 30)
                return DataProcessingMode.UltraOptimized;
            else if (strategy.EstimatedProcessingTimeMinutes > 15)
                return DataProcessingMode.HighlyOptimized;
            else if (strategy.EstimatedProcessingTimeMinutes > 5)
                return DataProcessingMode.Optimized;
            else
                return DataProcessingMode.Standard;
        }

        #endregion
    }

    /// <summary>
    /// 대용량 데이터 최적화 인터페이스
    /// </summary>
    public interface ILargeDataOptimizer
    {
        /// <summary>
        /// 대용량 파일의 크기를 분석하고 최적 처리 전략을 결정합니다
        /// </summary>
        Task<LargeDataProcessingStrategy> AnalyzeFileAndCreateStrategyAsync(
            string gdbPath, 
            List<string> tableNames,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 최적화된 스트리밍 방식으로 대용량 데이터를 처리합니다
        /// </summary>
        IAsyncEnumerable<TResult> ProcessLargeDataStreamAsync<TSource, TResult>(
            string gdbPath,
            string tableName,
            Func<IEnumerable<TSource>, Task<IEnumerable<TResult>>> processor,
            Func<OSGeo.OGR.Feature, TSource> featureConverter,
            LargeDataProcessingStrategy strategy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 메모리 사용량을 모니터링하고 자동으로 최적화합니다
        /// </summary>
        Task<bool> MonitorAndOptimizeMemoryAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 대용량 데이터 처리 전략
    /// </summary>
    public class LargeDataProcessingStrategy
    {
        /// <summary>
        /// 파일 경로
        /// </summary>
        public string GdbPath { get; set; } = string.Empty;

        /// <summary>
        /// 처리할 테이블 목록
        /// </summary>
        public List<string> TableNames { get; set; } = new();

        /// <summary>
        /// 파일 크기 (bytes)
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// 파일 크기 (MB)
        /// </summary>
        public double FileSizeMB { get; set; }

        /// <summary>
        /// 테이블별 레코드 수
        /// </summary>
        public Dictionary<string, long> TableSizes { get; set; } = new();

        /// <summary>
        /// 총 레코드 수
        /// </summary>
        public long TotalRecords { get; set; }

        /// <summary>
        /// 예상 처리 복잡도
        /// </summary>
        public ProcessingComplexity EstimatedComplexity { get; set; }

        /// <summary>
        /// 최적 배치 크기
        /// </summary>
        public int OptimalBatchSize { get; set; }

        /// <summary>
        /// 예상 처리 시간 (분)
        /// </summary>
        public double EstimatedProcessingTimeMinutes { get; set; }

        /// <summary>
        /// 예상 메모리 요구사항 (MB)
        /// </summary>
        public double EstimatedMemoryRequirementMB { get; set; }

        /// <summary>
        /// 처리 모드
        /// </summary>
        public DataProcessingMode ProcessingMode { get; set; }

        /// <summary>
        /// 분석 시작 시간
        /// </summary>
        public DateTime AnalysisStartTime { get; set; }

        /// <summary>
        /// 분석 소요 시간 (ms)
        /// </summary>
        public long AnalysisTimeMs { get; set; }

        /// <summary>
        /// 오류 발생 여부
        /// </summary>
        public bool HasError { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 처리 복잡도 열거형
    /// </summary>
    public enum ProcessingComplexity
    {
        Low,
        Medium,
        High,
        VeryHigh
    }

    /// <summary>
    /// 데이터 처리 모드 열거형
    /// </summary>
    public enum DataProcessingMode
    {
        Standard,
        Optimized,
        HighlyOptimized,
        UltraOptimized
    }
}
