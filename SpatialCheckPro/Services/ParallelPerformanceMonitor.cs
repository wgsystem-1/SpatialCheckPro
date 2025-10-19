using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 병렬 처리 성능 모니터링 서비스
    /// </summary>
    public class ParallelPerformanceMonitor
    {
        private readonly ILogger<ParallelPerformanceMonitor> _logger;
        private readonly ConcurrentDictionary<string, PerformanceMetrics> _metrics = new();
        private readonly ConcurrentQueue<PerformanceSnapshot> _snapshots = new();
        private readonly Timer _monitoringTimer;
        private readonly Timer _reportingTimer;
        
        private readonly int _maxSnapshots = 1000;
        private readonly TimeSpan _monitoringInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _reportingInterval = TimeSpan.FromMinutes(1);
        
        public event EventHandler<PerformanceReportEventArgs>? PerformanceReportGenerated;

        public ParallelPerformanceMonitor(ILogger<ParallelPerformanceMonitor> logger)
        {
            _logger = logger;
            
            // 성능 모니터링 타이머
            _monitoringTimer = new Timer(CollectPerformanceSnapshot, null, 
                TimeSpan.Zero, _monitoringInterval);
            
            // 성능 보고서 생성 타이머
            _reportingTimer = new Timer(GeneratePerformanceReport, null, 
                _reportingInterval, _reportingInterval);
        }

        /// <summary>
        /// 작업 시작
        /// </summary>
        public void StartOperation(string operationId, string operationName, int expectedItems)
        {
            var metrics = new PerformanceMetrics
            {
                OperationId = operationId,
                OperationName = operationName,
                ExpectedItems = expectedItems,
                StartTime = DateTime.Now,
                Status = OperationStatus.Running
            };
            
            _metrics[operationId] = metrics;
            
            _logger.LogDebug("성능 모니터링 시작: {OperationId} - {OperationName}", 
                operationId, operationName);
        }

        /// <summary>
        /// 작업 진행률 업데이트
        /// </summary>
        public void UpdateProgress(string operationId, int processedItems, int? totalItems = null)
        {
            if (_metrics.TryGetValue(operationId, out var metrics))
            {
                metrics.ProcessedItems = processedItems;
                if (totalItems.HasValue)
                {
                    metrics.TotalItems = totalItems.Value;
                }
                metrics.LastUpdateTime = DateTime.Now;
                
                // 처리 속도 계산
                var elapsed = DateTime.Now - metrics.StartTime;
                if (elapsed.TotalSeconds > 0)
                {
                    metrics.ItemsPerSecond = processedItems / elapsed.TotalSeconds;
                }
                
                // 예상 완료 시간 계산
                if (metrics.ItemsPerSecond > 0 && metrics.TotalItems > processedItems)
                {
                    var remainingItems = metrics.TotalItems - processedItems;
                    metrics.EstimatedCompletionTime = DateTime.Now.AddSeconds(remainingItems / metrics.ItemsPerSecond);
                }
            }
        }

        /// <summary>
        /// 작업 완료
        /// </summary>
        public void CompleteOperation(string operationId, bool success = true)
        {
            if (_metrics.TryGetValue(operationId, out var metrics))
            {
                metrics.EndTime = DateTime.Now;
                metrics.Status = success ? OperationStatus.Completed : OperationStatus.Failed;
                metrics.Duration = metrics.EndTime.Value - metrics.StartTime;
                
                // 최종 통계 계산
                if (metrics.Duration.TotalSeconds > 0)
                {
                    metrics.FinalItemsPerSecond = metrics.ProcessedItems / metrics.Duration.TotalSeconds;
                }
                
                _logger.LogInformation("성능 모니터링 완료: {OperationId} - {Duration:F1}초, {ItemsPerSecond:F1} items/sec", 
                    operationId, metrics.Duration.TotalSeconds, metrics.FinalItemsPerSecond);
            }
        }

        /// <summary>
        /// 성능 메트릭 가져오기
        /// </summary>
        public PerformanceMetrics? GetMetrics(string operationId)
        {
            return _metrics.TryGetValue(operationId, out var metrics) ? metrics : null;
        }

        /// <summary>
        /// 모든 성능 메트릭 가져오기
        /// </summary>
        public List<PerformanceMetrics> GetAllMetrics()
        {
            return _metrics.Values.ToList();
        }

        /// <summary>
        /// 성능 요약 가져오기
        /// </summary>
        public PerformanceSummary GetPerformanceSummary()
        {
            var allMetrics = _metrics.Values.ToList();
            var completedMetrics = allMetrics.Where(m => m.Status == OperationStatus.Completed).ToList();
            var recentSnapshots = _snapshots.Where(s => s.Timestamp > DateTime.Now.AddHours(-1)).ToList();
            
            return new PerformanceSummary
            {
                TotalOperations = allMetrics.Count,
                CompletedOperations = completedMetrics.Count,
                FailedOperations = allMetrics.Count(m => m.Status == OperationStatus.Failed),
                RunningOperations = allMetrics.Count(m => m.Status == OperationStatus.Running),
                AverageDuration = completedMetrics.Any() ? completedMetrics.Average(m => m.Duration.TotalSeconds) : 0,
                AverageItemsPerSecond = completedMetrics.Any() ? completedMetrics.Average(m => m.FinalItemsPerSecond) : 0,
                TotalItemsProcessed = completedMetrics.Sum(m => m.ProcessedItems),
                CurrentCpuUsage = GetCurrentCpuUsage(),
                CurrentMemoryUsage = GetCurrentMemoryUsage(),
                RecentSnapshots = recentSnapshots.Count
            };
        }

        /// <summary>
        /// 성능 스냅샷 수집
        /// </summary>
        private void CollectPerformanceSnapshot(object? state)
        {
            try
            {
                var snapshot = new PerformanceSnapshot
                {
                    Timestamp = DateTime.Now,
                    CpuUsage = GetCurrentCpuUsage(),
                    MemoryUsage = GetCurrentMemoryUsage(),
                    ActiveOperations = _metrics.Values.Count(m => m.Status == OperationStatus.Running),
                    TotalOperations = _metrics.Count,
                    ItemsPerSecond = CalculateCurrentItemsPerSecond()
                };
                
                _snapshots.Enqueue(snapshot);
                
                // 스냅샷 크기 제한
                while (_snapshots.Count > _maxSnapshots)
                {
                    _snapshots.TryDequeue(out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "성능 스냅샷 수집 중 오류 발생");
            }
        }

        /// <summary>
        /// 성능 보고서 생성 (개선판 - 의미 있는 데이터가 있을 때만 로그 출력)
        /// </summary>
        private void GeneratePerformanceReport(object? state)
        {
            try
            {
                var summary = GetPerformanceSummary();
                var recentSnapshots = _snapshots.Where(s => s.Timestamp > DateTime.Now.AddMinutes(-5)).ToList();
                
                var report = new PerformanceReport
                {
                    GeneratedAt = DateTime.Now,
                    Summary = summary,
                    RecentSnapshots = recentSnapshots,
                    TopPerformers = GetTopPerformers(),
                    PerformanceTrends = AnalyzePerformanceTrends()
                };
                
                // 개선: 의미 있는 데이터가 있을 때만 로그 출력
                // 조건: 실행 중인 작업이 있거나, 최근 5분 내에 완료된 작업이 있거나, items/sec > 0
                var hasRecentActivity = summary.RunningOperations > 0 || 
                                       summary.AverageItemsPerSecond > 0.01 ||
                                       recentSnapshots.Any(s => s.ItemsPerSecond > 0.01);
                
                if (hasRecentActivity)
                {
                    _logger.LogInformation("성능 보고서 생성: {TotalOperations}개 작업, 평균 {AvgItemsPerSec:F1} items/sec, " +
                        "실행중={Running}, 완료={Completed}", 
                        summary.TotalOperations, summary.AverageItemsPerSecond,
                        summary.RunningOperations, summary.CompletedOperations);
                }
                else
                {
                    // 활동이 없을 때는 Debug 레벨로 출력 (조용히)
                    _logger.LogDebug("성능 모니터링 대기 중 (활동 없음)");
                }
                
                // 이벤트 발생 (유지 - 다른 컴포넌트가 사용할 수 있음)
                PerformanceReportGenerated?.Invoke(this, new PerformanceReportEventArgs
                {
                    Report = report
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "성능 보고서 생성 중 오류 발생");
            }
        }

        /// <summary>
        /// 최고 성능 작업 가져오기
        /// </summary>
        private List<PerformanceMetrics> GetTopPerformers()
        {
            return _metrics.Values
                .Where(m => m.Status == OperationStatus.Completed)
                .OrderByDescending(m => m.FinalItemsPerSecond)
                .Take(5)
                .ToList();
        }

        /// <summary>
        /// 성능 트렌드 분석
        /// </summary>
        private PerformanceTrends AnalyzePerformanceTrends()
        {
            var recentSnapshots = _snapshots.Where(s => s.Timestamp > DateTime.Now.AddHours(-1)).ToList();
            
            if (recentSnapshots.Count < 2)
            {
                return new PerformanceTrends();
            }
            
            var firstHalf = recentSnapshots.Take(recentSnapshots.Count / 2).ToList();
            var secondHalf = recentSnapshots.Skip(recentSnapshots.Count / 2).ToList();
            
            var firstHalfAvg = firstHalf.Average(s => s.ItemsPerSecond);
            var secondHalfAvg = secondHalf.Average(s => s.ItemsPerSecond);
            
            return new PerformanceTrends
            {
                ItemsPerSecondTrend = secondHalfAvg - firstHalfAvg,
                CpuUsageTrend = secondHalf.Average(s => s.CpuUsage) - firstHalf.Average(s => s.CpuUsage),
                MemoryUsageTrend = secondHalf.Average(s => s.MemoryUsage) - firstHalf.Average(s => s.MemoryUsage),
                IsImproving = secondHalfAvg > firstHalfAvg
            };
        }

        /// <summary>
        /// 현재 초당 처리량 계산
        /// </summary>
        private double CalculateCurrentItemsPerSecond()
        {
            var runningOperations = _metrics.Values.Where(m => m.Status == OperationStatus.Running).ToList();
            return runningOperations.Sum(m => m.ItemsPerSecond);
        }

        /// <summary>
        /// 현재 CPU 사용률 가져오기
        /// </summary>
        private double GetCurrentCpuUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.TotalProcessorTime.TotalMilliseconds / Environment.TickCount;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 현재 메모리 사용량 가져오기 (MB)
        /// </summary>
        private long GetCurrentMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _reportingTimer?.Dispose();
            _metrics.Clear();
            
            while (_snapshots.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// 성능 메트릭
    /// </summary>
    public class PerformanceMetrics
    {
        public string OperationId { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public int ExpectedItems { get; set; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public TimeSpan Duration { get; set; }
        public double ItemsPerSecond { get; set; }
        public double FinalItemsPerSecond { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public OperationStatus Status { get; set; }
    }

    /// <summary>
    /// 성능 스냅샷
    /// </summary>
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public int ActiveOperations { get; set; }
        public int TotalOperations { get; set; }
        public double ItemsPerSecond { get; set; }
    }

    /// <summary>
    /// 성능 요약
    /// </summary>
    public class PerformanceSummary
    {
        public int TotalOperations { get; set; }
        public int CompletedOperations { get; set; }
        public int FailedOperations { get; set; }
        public int RunningOperations { get; set; }
        public double AverageDuration { get; set; }
        public double AverageItemsPerSecond { get; set; }
        public int TotalItemsProcessed { get; set; }
        public double CurrentCpuUsage { get; set; }
        public long CurrentMemoryUsage { get; set; }
        public int RecentSnapshots { get; set; }
    }

    /// <summary>
    /// 성능 트렌드
    /// </summary>
    public class PerformanceTrends
    {
        public double ItemsPerSecondTrend { get; set; }
        public double CpuUsageTrend { get; set; }
        public double MemoryUsageTrend { get; set; }
        public bool IsImproving { get; set; }
    }

    /// <summary>
    /// 성능 보고서
    /// </summary>
    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public PerformanceSummary Summary { get; set; } = new();
        public List<PerformanceSnapshot> RecentSnapshots { get; set; } = new();
        public List<PerformanceMetrics> TopPerformers { get; set; } = new();
        public PerformanceTrends PerformanceTrends { get; set; } = new();
    }

    /// <summary>
    /// 성능 보고서 이벤트 인수
    /// </summary>
    public class PerformanceReportEventArgs : EventArgs
    {
        public PerformanceReport Report { get; set; } = new();
    }

    /// <summary>
    /// 작업 상태
    /// </summary>
    public enum OperationStatus
    {
        Running,
        Completed,
        Failed,
        Cancelled
    }
}
