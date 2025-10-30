using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 병렬 처리 오류 처리 서비스
    /// </summary>
    public class ParallelErrorHandler
    {
        private readonly ILogger<ParallelErrorHandler> _logger;
        private readonly ConcurrentDictionary<string, ErrorContext> _errorContexts = new();
        private readonly ConcurrentQueue<ParallelError> _errorQueue = new();
        private readonly Timer _errorProcessingTimer;
        
        // 오류 처리 설정
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _retryDelay;
        private readonly int _maxErrorQueueSize;
        
        public event EventHandler<ParallelErrorEventArgs>? ErrorOccurred;
        public event EventHandler<ParallelErrorEventArgs>? ErrorRecovered;

        public ParallelErrorHandler(ILogger<ParallelErrorHandler> logger, 
            int maxRetryAttempts = 3, 
            TimeSpan? retryDelay = null,
            int maxErrorQueueSize = 1000)
        {
            _logger = logger;
            _maxRetryAttempts = maxRetryAttempts;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
            _maxErrorQueueSize = maxErrorQueueSize;
            
            // 오류 처리 타이머 (10초마다)
            _errorProcessingTimer = new Timer(ProcessErrorQueue, null, 
                TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// 오류 처리 (재시도 로직 포함)
        /// </summary>
        public async Task<TResult?> HandleErrorAsync<TResult>(
            string operationId,
            Func<Task<TResult>> operation,
            Func<Exception, bool>? shouldRetry = null,
            string? context = null)
        {
            var errorContext = GetOrCreateErrorContext(operationId, context);
            
            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    var result = await operation();
                    
                    // 성공 시 오류 컨텍스트 정리
                    if (attempt > 1)
                    {
                        _logger.LogInformation("작업 재시도 성공: {OperationId} (시도 {Attempt}/{MaxAttempts})", 
                            operationId, attempt, _maxRetryAttempts);
                        
                        ErrorRecovered?.Invoke(this, new ParallelErrorEventArgs
                        {
                            OperationId = operationId,
                            Attempt = attempt,
                            IsRecovered = true,
                            Context = context
                        });
                    }
                    
                    CleanupErrorContext(operationId);
                    return result;
                }
                catch (Exception ex)
                {
                    var error = new ParallelError
                    {
                        OperationId = operationId,
                        Exception = ex,
                        Attempt = attempt,
                        Timestamp = DateTime.Now,
                        Context = context ?? string.Empty
                    };
                    
                    // 오류 큐에 추가
                    AddErrorToQueue(error);
                    
                    // 재시도 여부 판단
                    var shouldRetryOperation = shouldRetry?.Invoke(ex) ?? ShouldRetryByDefault(ex);
                    
                    if (attempt < _maxRetryAttempts && shouldRetryOperation)
                    {
                        _logger.LogWarning("작업 오류 발생, 재시도 예정: {OperationId} (시도 {Attempt}/{MaxAttempts}) - {Error}", 
                            operationId, attempt, _maxRetryAttempts, ex.Message);
                        
                        // 재시도 지연
                        await Task.Delay(_retryDelay);
                        continue;
                    }
                    else
                    {
                        _logger.LogError(ex, "작업 최종 실패: {OperationId} (시도 {Attempt}/{MaxAttempts})", 
                            operationId, attempt, _maxRetryAttempts);
                        
                        // 오류 이벤트 발생
                        ErrorOccurred?.Invoke(this, new ParallelErrorEventArgs
                        {
                            OperationId = operationId,
                            Exception = ex,
                            Attempt = attempt,
                            IsFinalFailure = true,
                            Context = context
                        });
                        
                        return default(TResult);
                    }
                }
            }
            
            return default(TResult);
        }

        /// <summary>
        /// 배치 오류 처리
        /// </summary>
        public async Task<List<TResult>> HandleBatchErrorAsync<TItem, TResult>(
            List<TItem> items,
            Func<TItem, Task<TResult>> processor,
            string operationId,
            Func<Exception, bool>? shouldRetry = null,
            IProgress<BatchErrorProgress>? progress = null)
        {
            var results = new List<TResult>();
            var failedItems = new List<TItem>();
            var errorCount = 0;
            
            _logger.LogInformation("배치 오류 처리 시작: {OperationId} ({ItemCount}개 항목)", 
                operationId, items.Count);
            
            // 첫 번째 시도
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var itemId = $"{operationId}_item_{i}";
                
                try
                {
                    var result = await processor(item);
                    results.Add(result);
                    
                    progress?.Report(new BatchErrorProgress
                    {
                        ProcessedItems = i + 1,
                        TotalItems = items.Count,
                        ErrorCount = errorCount,
                        CurrentItem = $"항목 {i + 1}/{items.Count}"
                    });
                }
                catch (Exception ex)
                {
                    errorCount++;
                    failedItems.Add(item);
                    
                    _logger.LogWarning("배치 항목 처리 실패: {ItemId} - {Error}", itemId, ex.Message);
                    
                    // 오류 큐에 추가
                    AddErrorToQueue(new ParallelError
                    {
                        OperationId = itemId,
                        Exception = ex,
                        Attempt = 1,
                        Timestamp = DateTime.Now,
                        Context = $"배치 처리: {operationId}"
                    });
                }
            }
            
            // 실패한 항목들 재시도
            if (failedItems.Any())
            {
                _logger.LogInformation("실패한 항목 재시도: {FailedCount}개", failedItems.Count);
                
                for (int retryAttempt = 2; retryAttempt <= _maxRetryAttempts; retryAttempt++)
                {
                    var stillFailedItems = new List<TItem>();
                    
                    for (int i = 0; i < failedItems.Count; i++)
                    {
                        var item = failedItems[i];
                        var itemId = $"{operationId}_item_retry_{i}";
                        
                        try
                        {
                            var result = await processor(item);
                            results.Add(result);
                            
                            _logger.LogInformation("재시도 성공: {ItemId} (시도 {Attempt})", itemId, retryAttempt);
                        }
                        catch (Exception ex)
                        {
                            var shouldRetryOperation = shouldRetry?.Invoke(ex) ?? ShouldRetryByDefault(ex);
                            
                            if (retryAttempt < _maxRetryAttempts && shouldRetryOperation)
                            {
                                stillFailedItems.Add(item);
                                _logger.LogWarning("재시도 실패: {ItemId} (시도 {Attempt}) - {Error}", 
                                    itemId, retryAttempt, ex.Message);
                            }
                            else
                            {
                                _logger.LogError(ex, "최종 실패: {ItemId} (시도 {Attempt})", 
                                    itemId, retryAttempt);
                            }
                        }
                    }
                    
                    failedItems = stillFailedItems;
                    
                    if (!failedItems.Any())
                        break;
                    
                    // 재시도 지연
                    await Task.Delay(_retryDelay);
                }
            }
            
            _logger.LogInformation("배치 오류 처리 완료: {OperationId} - 성공 {SuccessCount}개, 실패 {FailureCount}개", 
                operationId, results.Count, failedItems.Count);
            
            return results;
        }

        /// <summary>
        /// 오류 통계 가져오기
        /// </summary>
        public ParallelErrorStatistics GetErrorStatistics()
        {
            var errors = _errorQueue.ToList();
            var contexts = _errorContexts.Values.ToList();

            return new ParallelErrorStatistics
            {
                TotalErrors = errors.Count,
                UniqueOperations = errors.Select(e => e.OperationId).Distinct().Count(),
                ErrorsByType = errors.GroupBy(e => e.Exception.GetType().Name)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AverageRetryAttempts = errors.Any() ? errors.Average(e => e.Attempt) : 0,
                RecentErrors = errors.Where(e => e.Timestamp > DateTime.Now.AddHours(-1)).Count(),
                ActiveErrorContexts = contexts.Count
            };
        }

        /// <summary>
        /// 오류 컨텍스트 가져오기 또는 생성
        /// </summary>
        private ErrorContext GetOrCreateErrorContext(string operationId, string? context)
        {
            return _errorContexts.GetOrAdd(operationId, _ => new ErrorContext
            {
                OperationId = operationId,
                Context = context ?? string.Empty,
                FirstErrorTime = DateTime.Now,
                ErrorCount = 0
            });
        }

        /// <summary>
        /// 오류 컨텍스트 정리
        /// </summary>
        private void CleanupErrorContext(string operationId)
        {
            _errorContexts.TryRemove(operationId, out _);
        }

        /// <summary>
        /// 오류 큐에 추가
        /// </summary>
        private void AddErrorToQueue(ParallelError error)
        {
            _errorQueue.Enqueue(error);
            
            // 큐 크기 제한
            while (_errorQueue.Count > _maxErrorQueueSize)
            {
                _errorQueue.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 기본 재시도 여부 판단
        /// </summary>
        private bool ShouldRetryByDefault(Exception ex)
        {
            return ex switch
            {
                TimeoutException => true,
                HttpRequestException => true,
                IOException => true,
                UnauthorizedAccessException => false,
                ArgumentException => false,
                InvalidOperationException => false,
                _ => true
            };
        }

        /// <summary>
        /// 오류 큐 처리 타이머 콜백
        /// </summary>
        private void ProcessErrorQueue(object? state)
        {
            try
            {
                var processedCount = 0;
                var maxProcessPerCycle = 100;
                
                while (_errorQueue.TryDequeue(out var error) && processedCount < maxProcessPerCycle)
                {
                    // 오류 로깅 및 분석
                    _logger.LogDebug("오류 처리: {OperationId} - {ErrorType}: {Message}", 
                        error.OperationId, error.Exception.GetType().Name, error.Exception.Message);
                    
                    processedCount++;
                }
                
                if (processedCount > 0)
                {
                    _logger.LogDebug("오류 큐 처리 완료: {ProcessedCount}개 오류 처리됨", processedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 큐 처리 중 예외 발생");
            }
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _errorProcessingTimer?.Dispose();
            _errorContexts.Clear();
            
            while (_errorQueue.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// 병렬 오류 정보
    /// </summary>
    public class ParallelError
    {
        public string OperationId { get; set; } = string.Empty;
        public Exception Exception { get; set; } = null!;
        public int Attempt { get; set; }
        public DateTime Timestamp { get; set; }
        public string Context { get; set; } = string.Empty;
    }

    /// <summary>
    /// 오류 컨텍스트
    /// </summary>
    public class ErrorContext
    {
        public string OperationId { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public DateTime FirstErrorTime { get; set; }
        public int ErrorCount { get; set; }
        public DateTime LastErrorTime { get; set; }
    }

    /// <summary>
    /// 병렬 오류 이벤트 인수
    /// </summary>
    public class ParallelErrorEventArgs : EventArgs
    {
        public string OperationId { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public int Attempt { get; set; }
        public bool IsRecovered { get; set; }
        public bool IsFinalFailure { get; set; }
        public string? Context { get; set; }
    }

    /// <summary>
    /// 배치 오류 진행률
    /// </summary>
    public class BatchErrorProgress
    {
        public int ProcessedItems { get; set; }
        public int TotalItems { get; set; }
        public int ErrorCount { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public double ProgressPercentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100 : 0;
    }

    /// <summary>
    /// 병렬 처리 오류 통계
    /// </summary>
    public class ParallelErrorStatistics
    {
        public int TotalErrors { get; set; }
        public int UniqueOperations { get; set; }
        public Dictionary<string, int> ErrorsByType { get; set; } = new();
        public double AverageRetryAttempts { get; set; }
        public int RecentErrors { get; set; }
        public int ActiveErrorContexts { get; set; }
    }
}
