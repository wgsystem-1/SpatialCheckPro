using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.Text.Json;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 오류 스트리밍 기록 구현체
    /// Phase 2 Item #7: 메모리 누적 없이 오류를 디스크에 즉시 기록
    /// - JSONL (JSON Lines) 형식 사용: 각 줄이 독립적인 JSON 객체
    /// - 비동기 스트림 라이터로 I/O 병목 최소화
    /// - 통계만 메모리에 유지 (오류 객체 자체는 메모리에 유지 안 함)
    /// </summary>
    public class StreamingErrorWriter : IStreamingErrorWriter
    {
        private readonly ILogger<StreamingErrorWriter> _logger;
        private readonly StreamWriter _writer;
        private readonly ErrorStatistics _statistics;
        private readonly SemaphoreSlim _writeLock;
        private bool _disposed = false;

        public string OutputPath { get; }

        /// <summary>
        /// StreamingErrorWriter 생성자
        /// </summary>
        /// <param name="outputPath">출력 파일 경로 (JSONL 형식)</param>
        /// <param name="logger">로거</param>
        public StreamingErrorWriter(string outputPath, ILogger<StreamingErrorWriter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));

            // 출력 디렉토리 생성
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // StreamWriter 생성 (UTF-8, 비동기 지원)
            _writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8)
            {
                AutoFlush = false // 수동 플러시로 성능 향상
            };

            _statistics = new ErrorStatistics
            {
                StartTime = DateTime.Now,
                OutputFilePath = outputPath
            };

            _writeLock = new SemaphoreSlim(1, 1);

            _logger.LogInformation("StreamingErrorWriter 초기화: {OutputPath}", outputPath);
        }

        /// <summary>
        /// 단일 오류를 디스크에 즉시 기록
        /// </summary>
        public async Task WriteErrorAsync(ValidationError error)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingErrorWriter));

            await _writeLock.WaitAsync();
            try
            {
                // JSON으로 직렬화 (한 줄에 하나씩)
                var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                await _writer.WriteLineAsync(json);

                // 통계 업데이트
                UpdateStatistics(error);

                // 1000개마다 플러시 (I/O 최적화)
                if (_statistics.TotalErrorCount % 1000 == 0)
                {
                    await _writer.FlushAsync();
                    _logger.LogDebug("StreamingErrorWriter: {Count}개 오류 기록 완료 (플러시)",
                        _statistics.TotalErrorCount);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// 여러 오류를 배치로 기록
        /// </summary>
        public async Task WriteErrorsAsync(IEnumerable<ValidationError> errors)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingErrorWriter));

            await _writeLock.WaitAsync();
            try
            {
                foreach (var error in errors)
                {
                    var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });

                    await _writer.WriteLineAsync(json);
                    UpdateStatistics(error);
                }

                // 배치 후 플러시
                await _writer.FlushAsync();

                _logger.LogDebug("StreamingErrorWriter: 배치 기록 완료 (총 {Count}개)",
                    _statistics.TotalErrorCount);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// 현재까지 기록된 오류 통계 가져오기
        /// </summary>
        public ErrorStatistics GetStatistics()
        {
            return new ErrorStatistics
            {
                TotalErrorCount = _statistics.TotalErrorCount,
                TotalWarningCount = _statistics.TotalWarningCount,
                ErrorCountByCode = new Dictionary<string, int>(_statistics.ErrorCountByCode),
                ErrorCountBySeverity = new Dictionary<string, int>(_statistics.ErrorCountBySeverity),
                ErrorCountByTable = new Dictionary<string, int>(_statistics.ErrorCountByTable),
                OutputFilePath = _statistics.OutputFilePath,
                StartTime = _statistics.StartTime,
                EndTime = _statistics.EndTime
            };
        }

        /// <summary>
        /// 기록 완료 및 최종 통계 반환
        /// </summary>
        public async Task<ErrorStatistics> FinalizeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingErrorWriter));

            await _writeLock.WaitAsync();
            try
            {
                await _writer.FlushAsync();
                _statistics.EndTime = DateTime.Now;

                _logger.LogInformation(
                    "StreamingErrorWriter 완료: 총 {ErrorCount}개 오류, {WarningCount}개 경고, 소요시간: {Duration:F2}초, 출력: {OutputPath}",
                    _statistics.TotalErrorCount,
                    _statistics.TotalWarningCount,
                    _statistics.Duration.TotalSeconds,
                    OutputPath);

                return GetStatistics();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// 통계 업데이트 (메모리 효율적)
        /// </summary>
        private void UpdateStatistics(ValidationError error)
        {
            // 심각도 기반 카운팅
            if (error.Severity == ErrorSeverity.Warning)
            {
                _statistics.TotalWarningCount++;
            }
            else
            {
                _statistics.TotalErrorCount++;
            }

            // 오류 코드별 집계
            if (!string.IsNullOrEmpty(error.ErrorCode))
            {
                if (_statistics.ErrorCountByCode.ContainsKey(error.ErrorCode))
                    _statistics.ErrorCountByCode[error.ErrorCode]++;
                else
                    _statistics.ErrorCountByCode[error.ErrorCode] = 1;
            }

            // 심각도별 집계
            var severityName = error.Severity.ToString();
            if (_statistics.ErrorCountBySeverity.ContainsKey(severityName))
                _statistics.ErrorCountBySeverity[severityName]++;
            else
                _statistics.ErrorCountBySeverity[severityName] = 1;

            // 테이블별 집계
            if (!string.IsNullOrEmpty(error.TableName))
            {
                if (_statistics.ErrorCountByTable.ContainsKey(error.TableName))
                    _statistics.ErrorCountByTable[error.TableName]++;
                else
                    _statistics.ErrorCountByTable[error.TableName] = 1;
            }
        }

        /// <summary>
        /// 임시 파일에서 모든 오류를 읽어옵니다 (스트리밍 모드 완료 후 사용)
        /// </summary>
        public static async Task<List<ValidationError>> ReadErrorsFromFileAsync(string filePath, ILogger? logger = null)
        {
            var errors = new List<ValidationError>();
            
            if (!File.Exists(filePath))
            {
                logger?.LogWarning("스트리밍 오류 파일을 찾을 수 없습니다: {FilePath}", filePath);
                return errors;
            }

            try
            {
                using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
                string? line;
                int lineNumber = 0;
                
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var error = JsonSerializer.Deserialize<ValidationError>(line, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        
                        if (error != null)
                        {
                            errors.Add(error);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "스트리밍 파일 라인 {LineNumber} 파싱 실패: {Line}", lineNumber, line);
                    }
                }

                logger?.LogInformation("스트리밍 파일에서 {Count}개 오류 읽기 완료: {FilePath}", errors.Count, filePath);
                return errors;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "스트리밍 파일 읽기 실패: {FilePath}", filePath);
                return errors;
            }
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _writer?.Dispose();
            _writeLock?.Dispose();
            _disposed = true;

            _logger.LogDebug("StreamingErrorWriter 리소스 해제 완료");
        }
    }
}
