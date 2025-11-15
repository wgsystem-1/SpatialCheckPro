using SpatialCheckPro.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Linq;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 파일 처리 서비스
    /// </summary>
    public class LargeFileProcessor : ILargeFileProcessor
    {
        private readonly ILogger<LargeFileProcessor> _logger;
        private readonly IMemoryManager? _memoryManager;

        public LargeFileProcessor(
            ILogger<LargeFileProcessor> logger,
            IMemoryManager? memoryManager = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryManager = memoryManager;
        }



        public async Task<ProcessingResult> ProcessInChunksAsync(string filePath, int chunkSize, Func<byte[], int, Task<bool>> processor, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("청크 단위 파일 처리 시작: {FilePath}, 청크 크기: {ChunkSize} bytes", filePath, chunkSize);

            var startTime = DateTime.Now;
            var totalProcessedBytes = 0L;
            var chunkIndex = 0;
            var success = true;

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, true))
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead;

                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 청크 처리
                        var chunkSuccess = await processor(buffer, bytesRead);
                        if (!chunkSuccess)
                        {
                            success = false;
                            _logger.LogWarning("청크 {ChunkIndex} 처리 실패", chunkIndex);
                            break;
                        }

                        totalProcessedBytes += bytesRead;
                        chunkIndex++;

                        _logger.LogDebug("청크 {ChunkIndex} 처리 완료: {BytesRead} bytes", chunkIndex, bytesRead);

                        // 메모리 압박 체크
                        if (_memoryManager != null && _memoryManager.IsMemoryPressureHigh())
                        {
                            _logger.LogWarning("메모리 압박 감지 - 청크 처리 중단 권장");
                            // 메모리 압박 시 메모리 정리 시도
                            await _memoryManager.TryReduceMemoryPressureAsync();
                        }
                    }
                }

                var processingTime = DateTime.Now - startTime;
                _logger.LogInformation("청크 단위 파일 처리 완료: {FilePath}, 총 {TotalBytes:N0} bytes, {ChunkCount}개 청크, 소요시간: {Duration}",
                    filePath, totalProcessedBytes, chunkIndex, processingTime);

                return new ProcessingResult
                {
                    Success = success,
                    ProcessedBytes = totalProcessedBytes,
                    ProcessingTime = processingTime,
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        ["ChunkCount"] = chunkIndex,
                        ["ChunkSize"] = chunkSize,
                        ["AverageChunkTime"] = chunkIndex > 0 ? processingTime.TotalMilliseconds / chunkIndex : 0
                    }
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("청크 단위 파일 처리 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "청크 단위 파일 처리 중 오류 발생: {FilePath}", filePath);
                return new ProcessingResult
                {
                    Success = false,
                    ProcessedBytes = totalProcessedBytes,
                    ProcessingTime = DateTime.Now - startTime,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 파일의 메타데이터를 분석하여 처리 모드를 결정합니다
        /// </summary>
        public object AnalyzeFileForProcessing(string filePath)
        {
            // TODO: FileAnalysisResult 모델을 사용하여 구현
            // 현재는 임시로 간단한 객체 반환
            return new
            {
                FilePath = filePath,
                FileSize = GetFileSize(filePath),
                IsLargeFile = IsLargeFile(filePath),
                RecommendedProcessingMode = IsLargeFile(filePath) ? "Streaming" : "Standard"
            };
        }

        public long GetFileSize(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                return new System.IO.FileInfo(filePath).Length;
            }

            if (System.IO.Directory.Exists(filePath))
            {
                try
                {
                    return System.IO.Directory
                        .EnumerateFiles(filePath, "*", System.IO.SearchOption.AllDirectories)
                        .Select(path =>
                        {
                            try
                            {
                                return new System.IO.FileInfo(path).Length;
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
                    _logger.LogWarning(ex, "디렉터리 크기 계산 중 오류 발생: {FilePath}", filePath);
                    return 0;
                }
            }

            return 0;
        }

        public bool IsLargeFile(string filePath, long threshold = 1_932_735_283L)
        {
            return GetFileSize(filePath) >= threshold;
        }

        public long GetMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        public async Task<ProcessingResult> ProcessFileStreamAsync(System.IO.Stream stream, Func<System.IO.Stream, Task<bool>> processor, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("스트림 처리 시작");
            
            try
            {
                var startTime = DateTime.Now;
                var success = await processor(stream);
                var endTime = DateTime.Now;

                return new ProcessingResult
                {
                    Success = success,
                    ProcessedBytes = stream.Length,
                    ProcessingTime = endTime - startTime
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스트림 처리 중 오류 발생");
                return new ProcessingResult
                {
                    Success = false,
                    ProcessedBytes = 0,
                    ProcessingTime = TimeSpan.Zero,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
