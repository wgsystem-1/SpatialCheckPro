using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 데이터 스트리밍 처리를 위한 프로세서
    /// </summary>
    public class StreamingDataProcessor : IStreamingDataProcessor
    {
        private readonly IMemoryManager _memoryManager;
        private readonly ILogger<StreamingDataProcessor> _logger;
        private readonly int _defaultChunkSize;
        private readonly int _minChunkSize;

        public StreamingDataProcessor(
            IMemoryManager memoryManager, 
            ILogger<StreamingDataProcessor> logger,
            int defaultChunkSize = 10000,
            int minChunkSize = 1000)
        {
            _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _defaultChunkSize = defaultChunkSize;
            _minChunkSize = minChunkSize;

            // 메모리 압박 이벤트 구독
            _memoryManager.MemoryPressureDetected += OnMemoryPressureDetected;
        }

        /// <summary>
        /// 데이터를 청크 단위로 스트리밍 처리합니다
        /// </summary>
        public async IAsyncEnumerable<TResult> ProcessInChunksAsync<TSource, TResult>(
            IAsyncEnumerable<TSource> source,
            Func<IEnumerable<TSource>, Task<IEnumerable<TResult>>> processor,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var chunk = new List<TSource>();
            var processedCount = 0L;
            var chunkCount = 0;

            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                chunk.Add(item);
                processedCount++;

                // 동적 청크 크기 결정
                var optimalChunkSize = _memoryManager.GetOptimalBatchSize(_defaultChunkSize, _minChunkSize);

                if (chunk.Count >= optimalChunkSize)
                {
                    // 청크 처리
                    var results = await ProcessChunkWithMemoryManagementAsync(chunk, processor, chunkCount);
                    
                    foreach (var result in results)
                    {
                        yield return result;
                    }

                    // 청크 초기화
                    chunk.Clear();
                    chunkCount++;

                    // 메모리 압박 체크 및 정리
                    await HandleMemoryPressureAsync(chunkCount);

                    _logger.LogDebug("청크 {ChunkNumber} 처리 완료 - 항목 수: {ChunkSize}, 총 처리: {ProcessedCount}",
                        chunkCount, optimalChunkSize, processedCount);
                }
            }

            // 마지막 청크 처리
            if (chunk.Count > 0)
            {
                var results = await ProcessChunkWithMemoryManagementAsync(chunk, processor, chunkCount);
                
                foreach (var result in results)
                {
                    yield return result;
                }

                _logger.LogDebug("마지막 청크 처리 완료 - 항목 수: {ChunkSize}, 총 처리: {ProcessedCount}",
                    chunk.Count, processedCount);
            }

            _logger.LogInformation("스트리밍 처리 완료 - 총 {ProcessedCount}개 항목, {ChunkCount}개 청크 처리됨",
                processedCount, chunkCount + 1);
        }

        /// <summary>
        /// 배치 단위로 데이터를 집계 처리합니다
        /// </summary>
        public async Task<TResult> ProcessBatchAggregationAsync<TSource, TResult>(
            IAsyncEnumerable<TSource> source,
            Func<TResult> seedFactory,
            Func<TResult, IEnumerable<TSource>, Task<TResult>> aggregator,
            CancellationToken cancellationToken = default)
        {
            var result = seedFactory();
            var batch = new List<TSource>();
            var processedCount = 0L;
            var batchCount = 0;

            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                batch.Add(item);
                processedCount++;

                var optimalBatchSize = _memoryManager.GetOptimalBatchSize(_defaultChunkSize, _minChunkSize);

                if (batch.Count >= optimalBatchSize)
                {
                    // 배치 집계 처리
                    result = await ProcessBatchWithMemoryManagementAsync(result, batch, aggregator, batchCount);
                    
                    // 배치 초기화
                    batch.Clear();
                    batchCount++;

                    // 메모리 압박 체크
                    await HandleMemoryPressureAsync(batchCount);

                    _logger.LogDebug("배치 {BatchNumber} 집계 완료 - 항목 수: {BatchSize}, 총 처리: {ProcessedCount}",
                        batchCount, optimalBatchSize, processedCount);
                }
            }

            // 마지막 배치 처리
            if (batch.Count > 0)
            {
                result = await ProcessBatchWithMemoryManagementAsync(result, batch, aggregator, batchCount);
                
                _logger.LogDebug("마지막 배치 집계 완료 - 항목 수: {BatchSize}, 총 처리: {ProcessedCount}",
                    batch.Count, processedCount);
            }

            _logger.LogInformation("배치 집계 처리 완료 - 총 {ProcessedCount}개 항목, {BatchCount}개 배치 처리됨",
                processedCount, batchCount + 1);

            return result;
        }

        /// <summary>
        /// 메모리 효율적인 필드값 카운팅을 수행합니다
        /// </summary>
        public async Task<Dictionary<string, int>> CountFieldValuesAsync(
            IAsyncEnumerable<string> fieldValues,
            CancellationToken cancellationToken = default)
        {
            return await ProcessBatchAggregationAsync(
                fieldValues,
                () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                async (counts, batch) =>
                {
                    await Task.Run(() =>
                    {
                        foreach (var value in batch)
                        {
                            if (counts.ContainsKey(value))
                            {
                                counts[value]++;
                            }
                            else
                            {
                                counts[value] = 1;
                            }
                        }
                    });
                    return counts;
                },
                cancellationToken);
        }

        /// <summary>
        /// 메모리 효율적인 중복값 검출을 수행합니다
        /// </summary>
        public async Task<List<string>> FindDuplicateValuesAsync(
            IAsyncEnumerable<string> fieldValues,
            CancellationToken cancellationToken = default)
        {
            var valueCounts = await CountFieldValuesAsync(fieldValues, cancellationToken);
            
            return await Task.Run(() =>
            {
                var duplicates = valueCounts
                    .Where(kvp => kvp.Value > 1)
                    .Select(kvp => kvp.Key)
                    .ToList();

                _logger.LogInformation("중복값 검출 완료 - 총 {TotalValues}개 값 중 {DuplicateCount}개 중복값 발견",
                    valueCounts.Count, duplicates.Count);

                return duplicates;
            });
        }

        /// <summary>
        /// 메모리 관리가 포함된 청크 처리
        /// </summary>
        private async Task<IEnumerable<TResult>> ProcessChunkWithMemoryManagementAsync<TSource, TResult>(
            List<TSource> chunk,
            Func<IEnumerable<TSource>, Task<IEnumerable<TResult>>> processor,
            int chunkNumber)
        {
            try
            {
                // 메모리 사용량 체크
                var memoryBefore = _memoryManager.GetCurrentMemoryUsage();
                
                // 청크 처리
                var results = await processor(chunk);
                
                // 메모리 사용량 모니터링
                var memoryAfter = _memoryManager.GetCurrentMemoryUsage();
                var memoryDelta = memoryAfter - memoryBefore;
                
                if (memoryDelta > 0)
                {
                    _logger.LogDebug("청크 {ChunkNumber} 처리 - 메모리 증가: {MemoryDeltaMB:F2}MB",
                        chunkNumber, memoryDelta / (1024.0 * 1024.0));
                }

                return results;
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "청크 {ChunkNumber} 처리 중 메모리 부족 발생", chunkNumber);
                
                // 긴급 메모리 정리
                await _memoryManager.TryReduceMemoryPressureAsync();
                
                throw;
            }
        }

        /// <summary>
        /// 메모리 관리가 포함된 배치 집계 처리
        /// </summary>
        private async Task<TResult> ProcessBatchWithMemoryManagementAsync<TSource, TResult>(
            TResult currentResult,
            List<TSource> batch,
            Func<TResult, IEnumerable<TSource>, Task<TResult>> aggregator,
            int batchNumber)
        {
            try
            {
                var memoryBefore = _memoryManager.GetCurrentMemoryUsage();
                
                var result = await aggregator(currentResult, batch);
                
                var memoryAfter = _memoryManager.GetCurrentMemoryUsage();
                var memoryDelta = memoryAfter - memoryBefore;
                
                if (memoryDelta > 0)
                {
                    _logger.LogDebug("배치 {BatchNumber} 집계 - 메모리 증가: {MemoryDeltaMB:F2}MB",
                        batchNumber, memoryDelta / (1024.0 * 1024.0));
                }

                return result;
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "배치 {BatchNumber} 집계 중 메모리 부족 발생", batchNumber);
                
                // 긴급 메모리 정리
                await _memoryManager.TryReduceMemoryPressureAsync();
                
                throw;
            }
        }

        /// <summary>
        /// 메모리 압박 상황을 처리합니다
        /// </summary>
        private async Task HandleMemoryPressureAsync(int processedBatches)
        {
            // 일정 배치마다 메모리 체크
            if (processedBatches % 10 == 0)
            {
                if (_memoryManager.IsMemoryPressureHigh())
                {
                    _logger.LogWarning("메모리 압박 감지 - 자동 정리 수행 중 (배치 {BatchNumber})", processedBatches);
                    
                    var success = await _memoryManager.TryReduceMemoryPressureAsync();
                    
                    if (!success)
                    {
                        _logger.LogWarning("메모리 압박 해소 실패 - 처리 속도가 느려질 수 있습니다");
                        
                        // 추가 대기 시간을 두어 시스템 안정화
                        await Task.Delay(1000);
                    }
                }
            }
        }

        /// <summary>
        /// 메모리 압박 이벤트 핸들러
        /// </summary>
        private void OnMemoryPressureDetected(object? sender, MemoryPressureEventArgs e)
        {
            _logger.LogWarning("메모리 압박 상황 감지 - 현재 사용량: {CurrentMemoryMB:F2}MB / {MaxMemoryMB:F2}MB ({PressureRatio:P1}), " +
                             "권장 조치: {RecommendedAction}",
                e.CurrentMemoryUsage / (1024.0 * 1024.0), 
                e.MaxMemoryLimit / (1024.0 * 1024.0), 
                e.PressureRatio, 
                e.RecommendedAction);
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            if (_memoryManager != null)
            {
                _memoryManager.MemoryPressureDetected -= OnMemoryPressureDetected;
            }
        }
    }

    /// <summary>
    /// 스트리밍 데이터 프로세서 인터페이스
    /// </summary>
    public interface IStreamingDataProcessor : IDisposable
    {
        /// <summary>
        /// 데이터를 청크 단위로 스트리밍 처리합니다
        /// </summary>
        IAsyncEnumerable<TResult> ProcessInChunksAsync<TSource, TResult>(
            IAsyncEnumerable<TSource> source,
            Func<IEnumerable<TSource>, Task<IEnumerable<TResult>>> processor,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 배치 단위로 데이터를 집계 처리합니다
        /// </summary>
        Task<TResult> ProcessBatchAggregationAsync<TSource, TResult>(
            IAsyncEnumerable<TSource> source,
            Func<TResult> seedFactory,
            Func<TResult, IEnumerable<TSource>, Task<TResult>> aggregator,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 메모리 효율적인 필드값 카운팅을 수행합니다
        /// </summary>
        Task<Dictionary<string, int>> CountFieldValuesAsync(
            IAsyncEnumerable<string> fieldValues,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 메모리 효율적인 중복값 검출을 수행합니다
        /// </summary>
        Task<List<string>> FindDuplicateValuesAsync(
            IAsyncEnumerable<string> fieldValues,
            CancellationToken cancellationToken = default);
    }
}
