using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 메모리 관리 및 최적화를 담당하는 서비스
    /// </summary>
    public class MemoryManager : IMemoryManager
    {
        private readonly ILogger<MemoryManager> _logger;
        private readonly long _maxMemoryUsageBytes;
        private readonly Timer _memoryMonitorTimer;
        private readonly object _lockObject = new object();
        
        private long _currentMemoryUsage;
        private int _gcCollectionCount;
        private DateTime _lastGcTime = DateTime.MinValue;
        
        /// <summary>
        /// 메모리 압박 상황 발생 이벤트
        /// </summary>
        public event EventHandler<MemoryPressureEventArgs>? MemoryPressureDetected;

        public MemoryManager(ILogger<MemoryManager> logger, long maxMemoryUsageMB = 1024)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxMemoryUsageBytes = maxMemoryUsageMB * 1024 * 1024; // MB를 bytes로 변환
            
            // 메모리 모니터링 타이머 설정 (5초마다 체크)
            _memoryMonitorTimer = new Timer(MonitorMemoryUsage, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            
            _logger.LogInformation("메모리 관리자 초기화 완료 - 최대 메모리 사용량: {MaxMemoryMB}MB", maxMemoryUsageMB);
        }

        /// <summary>
        /// 현재 메모리 사용량을 조회합니다
        /// </summary>
        public long GetCurrentMemoryUsage()
        {
            var memoryUsage = GC.GetTotalMemory(false);
            Interlocked.Exchange(ref _currentMemoryUsage, memoryUsage);
            return memoryUsage;
        }

        /// <summary>
        /// 메모리 사용량이 임계값을 초과했는지 확인합니다
        /// </summary>
        public bool IsMemoryPressureHigh()
        {
            var currentUsage = GetCurrentMemoryUsage();
            var pressureRatio = (double)currentUsage / _maxMemoryUsageBytes;
            
            return pressureRatio > 0.8; // 80% 이상 사용 시 압박 상황으로 판단
        }

        /// <summary>
        /// 강제 가비지 컬렉션을 수행합니다
        /// </summary>
        public void ForceGarbageCollection()
        {
            lock (_lockObject)
            {
                var beforeMemory = GetCurrentMemoryUsage();
                var stopwatch = Stopwatch.StartNew();
                
                // 모든 세대의 가비지 컬렉션 수행
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                stopwatch.Stop();
                var afterMemory = GetCurrentMemoryUsage();
                var freedMemory = beforeMemory - afterMemory;
                
                _gcCollectionCount++;
                _lastGcTime = DateTime.UtcNow;
                
                _logger.LogDebug("강제 GC 수행 완료 - 해제된 메모리: {FreedMemoryMB:F2}MB, 소요시간: {ElapsedMs}ms, " +
                               "이전: {BeforeMemoryMB:F2}MB, 이후: {AfterMemoryMB:F2}MB",
                    freedMemory / (1024.0 * 1024.0), stopwatch.ElapsedMilliseconds,
                    beforeMemory / (1024.0 * 1024.0), afterMemory / (1024.0 * 1024.0));
            }
        }

        /// <summary>
        /// 메모리 압박 시 자동 정리를 수행합니다
        /// </summary>
        public async Task<bool> TryReduceMemoryPressureAsync()
        {
            if (!IsMemoryPressureHigh())
            {
                return false;
            }

            _logger.LogWarning("메모리 압박 상황 감지 - 자동 정리 시작");
            
            var beforeMemory = GetCurrentMemoryUsage();
            
            // 1. 강제 가비지 컬렉션
            ForceGarbageCollection();
            
            // 2. 대기 시간을 두어 GC가 완전히 완료되도록 함
            await Task.Delay(100);
            
            // 3. 메모리 압축 시도 (LOH 압축)
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            
            var afterMemory = GetCurrentMemoryUsage();
            var freedMemory = beforeMemory - afterMemory;
            var isStillUnderPressure = IsMemoryPressureHigh();
            
            _logger.LogInformation("메모리 압박 해소 시도 완료 - 해제된 메모리: {FreedMemoryMB:F2}MB, " +
                                 "현재 압박 상태: {IsUnderPressure}",
                freedMemory / (1024.0 * 1024.0), isStillUnderPressure);
            
            // 메모리 압박 이벤트 발생
            if (isStillUnderPressure)
            {
                OnMemoryPressureDetected(new MemoryPressureEventArgs
                {
                    CurrentMemoryUsage = afterMemory,
                    MaxMemoryLimit = _maxMemoryUsageBytes,
                    PressureRatio = (double)afterMemory / _maxMemoryUsageBytes,
                    RecommendedAction = "배치 크기 감소 또는 처리 일시 중단 권장"
                });
            }
            
            return !isStillUnderPressure;
        }

        /// <summary>
        /// 메모리 사용량에 따라 동적으로 배치 크기를 조정합니다
        /// </summary>
        public int GetOptimalBatchSize(int defaultBatchSize, int minBatchSize = 1000)
        {
            var currentUsage = GetCurrentMemoryUsage();
            var pressureRatio = (double)currentUsage / _maxMemoryUsageBytes;
            
            int adjustedBatchSize;
            
            if (pressureRatio > 0.9) // 90% 이상 사용
            {
                adjustedBatchSize = Math.Max(minBatchSize, defaultBatchSize / 4);
            }
            else if (pressureRatio > 0.8) // 80% 이상 사용
            {
                adjustedBatchSize = Math.Max(minBatchSize, defaultBatchSize / 2);
            }
            else if (pressureRatio > 0.6) // 60% 이상 사용
            {
                adjustedBatchSize = Math.Max(minBatchSize, (int)(defaultBatchSize * 0.75));
            }
            else
            {
                adjustedBatchSize = defaultBatchSize;
            }
            
            if (adjustedBatchSize != defaultBatchSize)
            {
                _logger.LogDebug("배치 크기 동적 조정: {DefaultSize} -> {AdjustedSize} (메모리 사용률: {PressureRatio:P1})",
                    defaultBatchSize, adjustedBatchSize, pressureRatio);
            }
            
            return adjustedBatchSize;
        }

        /// <summary>
        /// 메모리 통계 정보를 반환합니다
        /// </summary>
        public MemoryStatistics GetMemoryStatistics()
        {
            var currentMemory = GetCurrentMemoryUsage();
            var gen0Collections = GC.CollectionCount(0);
            var gen1Collections = GC.CollectionCount(1);
            var gen2Collections = GC.CollectionCount(2);
            
            return new MemoryStatistics
            {
                CurrentMemoryUsage = currentMemory,
                MaxMemoryLimit = _maxMemoryUsageBytes,
                PressureRatio = (double)currentMemory / _maxMemoryUsageBytes,
                Gen0Collections = gen0Collections,
                Gen1Collections = gen1Collections,
                Gen2Collections = gen2Collections,
                ForcedGcCount = _gcCollectionCount,
                LastGcTime = _lastGcTime,
                IsUnderPressure = IsMemoryPressureHigh()
            };
        }

        /// <summary>
        /// 메모리 사용량을 주기적으로 모니터링합니다
        /// </summary>
        private void MonitorMemoryUsage(object? state)
        {
            try
            {
                var currentUsage = GetCurrentMemoryUsage();
                var pressureRatio = (double)currentUsage / _maxMemoryUsageBytes;
                
                // 메모리 사용량이 높을 때만 로깅
                if (pressureRatio > 0.7)
                {
                    _logger.LogDebug("메모리 사용량 모니터링: {CurrentMemoryMB:F2}MB / {MaxMemoryMB:F2}MB ({PressureRatio:P1})",
                        currentUsage / (1024.0 * 1024.0), _maxMemoryUsageBytes / (1024.0 * 1024.0), pressureRatio);
                }
                
                // 임계값 초과 시 자동 정리 수행
                if (pressureRatio > 0.85)
                {
                    _ = Task.Run(async () => await TryReduceMemoryPressureAsync());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 메모리 압박 이벤트를 발생시킵니다
        /// </summary>
        private void OnMemoryPressureDetected(MemoryPressureEventArgs args)
        {
            MemoryPressureDetected?.Invoke(this, args);
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _memoryMonitorTimer?.Dispose();
            _logger.LogInformation("메모리 관리자 종료됨");
        }
    }

    /// <summary>
    /// 메모리 관리자 인터페이스
    /// </summary>
    public interface IMemoryManager : IDisposable
    {
        /// <summary>
        /// 메모리 압박 상황 발생 이벤트
        /// </summary>
        event EventHandler<MemoryPressureEventArgs>? MemoryPressureDetected;

        /// <summary>
        /// 현재 메모리 사용량을 조회합니다
        /// </summary>
        long GetCurrentMemoryUsage();

        /// <summary>
        /// 메모리 사용량이 임계값을 초과했는지 확인합니다
        /// </summary>
        bool IsMemoryPressureHigh();

        /// <summary>
        /// 강제 가비지 컬렉션을 수행합니다
        /// </summary>
        void ForceGarbageCollection();

        /// <summary>
        /// 메모리 압박 시 자동 정리를 수행합니다
        /// </summary>
        Task<bool> TryReduceMemoryPressureAsync();

        /// <summary>
        /// 메모리 사용량에 따라 동적으로 배치 크기를 조정합니다
        /// </summary>
        int GetOptimalBatchSize(int defaultBatchSize, int minBatchSize = 1000);

        /// <summary>
        /// 메모리 통계 정보를 반환합니다
        /// </summary>
        MemoryStatistics GetMemoryStatistics();
    }

    /// <summary>
    /// 메모리 압박 이벤트 인자
    /// </summary>
    public class MemoryPressureEventArgs : EventArgs
    {
        /// <summary>
        /// 현재 메모리 사용량 (bytes)
        /// </summary>
        public long CurrentMemoryUsage { get; set; }

        /// <summary>
        /// 최대 메모리 제한 (bytes)
        /// </summary>
        public long MaxMemoryLimit { get; set; }

        /// <summary>
        /// 메모리 압박 비율 (0.0 ~ 1.0)
        /// </summary>
        public double PressureRatio { get; set; }

        /// <summary>
        /// 권장 조치 사항
        /// </summary>
        public string RecommendedAction { get; set; } = string.Empty;
    }

    /// <summary>
    /// 메모리 통계 정보
    /// </summary>
    public class MemoryStatistics
    {
        /// <summary>
        /// 현재 메모리 사용량 (bytes)
        /// </summary>
        public long CurrentMemoryUsage { get; set; }

        /// <summary>
        /// 최대 메모리 제한 (bytes)
        /// </summary>
        public long MaxMemoryLimit { get; set; }

        /// <summary>
        /// 메모리 압박 비율 (0.0 ~ 1.0)
        /// </summary>
        public double PressureRatio { get; set; }

        /// <summary>
        /// Generation 0 GC 횟수
        /// </summary>
        public int Gen0Collections { get; set; }

        /// <summary>
        /// Generation 1 GC 횟수
        /// </summary>
        public int Gen1Collections { get; set; }

        /// <summary>
        /// Generation 2 GC 횟수
        /// </summary>
        public int Gen2Collections { get; set; }

        /// <summary>
        /// 강제 GC 수행 횟수
        /// </summary>
        public int ForcedGcCount { get; set; }

        /// <summary>
        /// 마지막 GC 수행 시간
        /// </summary>
        public DateTime LastGcTime { get; set; }

        /// <summary>
        /// 현재 메모리 압박 상태 여부
        /// </summary>
        public bool IsUnderPressure { get; set; }
    }
}
