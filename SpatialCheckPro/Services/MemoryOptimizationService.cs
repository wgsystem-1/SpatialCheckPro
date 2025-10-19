using Microsoft.Extensions.Logging;
using System.Diagnostics;
using SpatialCheckPro.Models.Config;
using System.Collections.Concurrent;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 메모리 최적화 및 리소스 관리 서비스
    /// </summary>
    public class MemoryOptimizationService
    {
        private readonly ILogger<MemoryOptimizationService> _logger;
        private readonly PerformanceSettings _settings;
        
        private long _lastMemoryUsage = 0;
        private DateTime _lastGcTime = DateTime.Now;
        private int _gcCount = 0;
        private int _gcFrequency = 1; // GC 빈도 조절 (1=기본, 2=빈번, 0=최소)
        private DateTime _lastGcFrequencyAdjustment = DateTime.Now;
        
        // 메모리 사용량 추적
        private readonly ConcurrentQueue<long> _memoryHistory = new();
        private readonly object _memoryLock = new object();
        
        // 배치 처리 최적화
        private int _optimalBatchSize = 1000;
        private DateTime _lastBatchOptimization = DateTime.Now;

        public MemoryOptimizationService(ILogger<MemoryOptimizationService> logger, PerformanceSettings settings)
        {
            _logger = logger;
            _settings = settings;
            
            // 메모리 모니터링 타이머 시작 -> 교착 상태 해결 후 불필요한 강제 GC를 막기 위해 비활성화
            /*
            _memoryMonitoringTimer = new Timer(MonitorMemoryUsage, null, 
                TimeSpan.Zero, TimeSpan.FromSeconds(5)); // 5초마다 체크
            */
            
            _logger.LogInformation("메모리 최적화 서비스 초기화 완료 (자동 GC 비활성화됨)");
        }

        /// <summary>
        /// 메모리 사용량 모니터링
        /// </summary>
        private void MonitorMemoryUsage(object? state)
        {
            try
            {
                var currentMemory = GetCurrentMemoryUsageMB();
                var memoryPressure = CalculateMemoryPressure(currentMemory);
                
                // GC 빈도 조절
                AdjustGcFrequency(currentMemory, memoryPressure);
                
                // 메모리 압박이 높은 경우 자동 GC 실행
                if (_settings.EnableAutomaticGarbageCollection && memoryPressure > 0.8)
                {
                    _logger.LogWarning("메모리 압박 감지: {CurrentMemory}MB (압박도: {Pressure:P}) - 자동 GC 실행", 
                        currentMemory, memoryPressure);
                    
                    PerformGarbageCollection();
                }
                
                // 정기적 GC 실행 (GC 빈도에 따라 간격 조절)
                var gcInterval = GetGcInterval();
                if (_settings.EnableAutomaticGarbageCollection && 
                    DateTime.Now - _lastGcTime > gcInterval)
                {
                    _logger.LogInformation("정기적 GC 실행: {CurrentMemory}MB (간격: {Interval:F1}분)", 
                        currentMemory, gcInterval.TotalMinutes);
                    PerformGarbageCollection();
                }
                
                _lastMemoryUsage = currentMemory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 현재 메모리 사용량 (MB)
        /// </summary>
        public long GetCurrentMemoryUsageMB()
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
        /// 메모리 압박도 계산 (0.0 ~ 1.0)
        /// </summary>
        private double CalculateMemoryPressure(long currentMemoryMB)
        {
            if (_settings.MaxMemoryUsageMB <= 0)
                return 0.0;
                
            return Math.Min(1.0, (double)currentMemoryMB / _settings.MaxMemoryUsageMB);
        }

        /// <summary>
        /// 가비지 컬렉션 실행
        /// </summary>
        public void PerformGarbageCollection()
        {
            try
            {
                var beforeMemory = GetCurrentMemoryUsageMB();
                
                // 강제 GC 실행
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var afterMemory = GetCurrentMemoryUsageMB();
                var freedMemory = beforeMemory - afterMemory;
                
                _gcCount++;
                _lastGcTime = DateTime.Now;
                
                _logger.LogInformation("GC 실행 완료: {BeforeMemory}MB → {AfterMemory}MB (해제: {FreedMemory}MB, 총 실행횟수: {GcCount})", 
                    beforeMemory, afterMemory, freedMemory, _gcCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GC 실행 중 오류 발생");
            }
        }

        /// <summary>
        /// 강제 가비지 컬렉션 실행 (외부 호출용)
        /// </summary>
        public void ForceGarbageCollection()
        {
            PerformGarbageCollection();
        }

        /// <summary>
        /// 메모리 사용량이 임계값을 초과하는지 확인
        /// </summary>
        public bool IsMemoryPressureHigh()
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            return currentMemory > _settings.MemoryPressureThresholdMB;
        }

        /// <summary>
        /// 메모리 사용량 통계
        /// </summary>
        public MemoryUsageStats GetMemoryStats()
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            var pressure = CalculateMemoryPressure(currentMemory);
            
            return new MemoryUsageStats
            {
                CurrentUsageMB = currentMemory,
                MaxAllowedMB = _settings.MaxMemoryUsageMB,
                PressureLevel = pressure,
                GcCount = _gcCount,
                LastGcTime = _lastGcTime,
                IsPressureHigh = pressure > 0.8
            };
        }

        /// <summary>
        /// 메모리 최적화 권장사항 제공
        /// </summary>
        public MemoryOptimizationRecommendation GetOptimizationRecommendation()
        {
            var stats = GetMemoryStats();
            var recommendation = new MemoryOptimizationRecommendation();
            
            if (stats.IsPressureHigh)
            {
                recommendation.IsOptimizationNeeded = true;
                recommendation.RecommendedActions.Add("메모리 사용량이 높습니다. 배치 크기를 줄이거나 스트리밍 모드를 활성화하세요.");
                
                if (_settings.BatchSize > 1000)
                {
                    recommendation.RecommendedActions.Add($"배치 크기를 {_settings.BatchSize}에서 {_settings.BatchSize / 2}로 줄이세요.");
                }
                
                if (!_settings.EnableStreamingMode)
                {
                    recommendation.RecommendedActions.Add("스트리밍 모드를 활성화하세요.");
                }
            }
            else if (stats.PressureLevel < 0.5)
            {
                recommendation.IsOptimizationNeeded = false;
                recommendation.RecommendedActions.Add("메모리 사용량이 적절합니다. 현재 설정을 유지하세요.");
                
                if (_settings.BatchSize < 5000)
                {
                    recommendation.RecommendedActions.Add($"배치 크기를 {_settings.BatchSize}에서 {Math.Min(10000, _settings.BatchSize * 2)}로 늘릴 수 있습니다.");
                }
            }
            
            return recommendation;
        }

        /// <summary>
        /// 메모리 사용량 기록 및 분석
        /// </summary>
        public void RecordMemoryUsage(long memoryUsageMB)
        {
            lock (_memoryLock)
            {
                _memoryHistory.Enqueue(memoryUsageMB);
                
                // 최근 100개 기록만 유지
                while (_memoryHistory.Count > 100)
                {
                    _memoryHistory.TryDequeue(out _);
                }
                
                // 배치 크기 최적화 (5분마다)
                if (DateTime.Now - _lastBatchOptimization > TimeSpan.FromMinutes(5))
                {
                    OptimizeBatchSize();
                    _lastBatchOptimization = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 배치 크기 최적화
        /// </summary>
        private void OptimizeBatchSize()
        {
            if (_memoryHistory.Count < 10) return;

            var recentMemory = _memoryHistory.TakeLast(20).ToList();
            var avgMemory = recentMemory.Average();
            var maxMemory = recentMemory.Max();
            var memoryVariation = maxMemory - avgMemory;

            // 메모리 변동이 큰 경우 배치 크기 감소
            if (memoryVariation > 500) // 500MB 이상 변동
            {
                _optimalBatchSize = Math.Max(100, _optimalBatchSize / 2);
                _logger.LogInformation("메모리 변동 감지로 배치 크기 감소: {BatchSize}", _optimalBatchSize);
            }
            // 메모리가 안정적인 경우 배치 크기 증가
            else if (memoryVariation < 100 && avgMemory < _settings.MemoryUsageLimitPercent * 1024 * 0.5)
            {
                _optimalBatchSize = Math.Min(10000, _optimalBatchSize * 2);
                _logger.LogInformation("메모리 안정으로 배치 크기 증가: {BatchSize}", _optimalBatchSize);
            }
        }

        /// <summary>
        /// 최적화된 배치 크기 반환
        /// </summary>
        public int GetOptimalBatchSize()
        {
            return _settings.StreamingBatchSize > 0 ? _settings.StreamingBatchSize : _optimalBatchSize;
        }

        /// <summary>
        /// 스트리밍 모드 권장 여부 확인
        /// </summary>
        public bool ShouldUseStreamingMode()
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            var memoryPressure = CalculateMemoryPressure(currentMemory);
            
            return _settings.EnableStreamingMode || memoryPressure > 0.7;
        }

        /// <summary>
        /// 메모리 히스토리 통계 반환
        /// </summary>
        public MemoryHistoryStats GetMemoryHistoryStats()
        {
            lock (_memoryLock)
            {
                if (_memoryHistory.Count == 0)
                {
                    return new MemoryHistoryStats();
                }

                var memoryList = _memoryHistory.ToList();
                return new MemoryHistoryStats
                {
                    AverageMB = memoryList.Average(),
                    MinMB = memoryList.Min(),
                    MaxMB = memoryList.Max(),
                    CurrentMB = memoryList.LastOrDefault(),
                    SampleCount = memoryList.Count,
                    OptimalBatchSize = _optimalBatchSize
                };
            }
        }

        /// <summary>
        /// GC 빈도 조절
        /// </summary>
        private void AdjustGcFrequency(long currentMemoryMB, double memoryPressure)
        {
            // 10분마다 GC 빈도 조절
            if (DateTime.Now - _lastGcFrequencyAdjustment < TimeSpan.FromMinutes(10))
                return;

            var previousFrequency = _gcFrequency;

            if (memoryPressure > 0.9) // 매우 높은 메모리 압박
            {
                _gcFrequency = 2; // 빈번한 GC
                _logger.LogWarning("메모리 압박 매우 높음 - GC 빈도 증가: {Frequency}", _gcFrequency);
            }
            else if (memoryPressure > 0.7) // 높은 메모리 압박
            {
                _gcFrequency = 1; // 기본 GC
            }
            else if (memoryPressure < 0.3) // 낮은 메모리 압박
            {
                _gcFrequency = 0; // 최소 GC
                _logger.LogInformation("메모리 압박 낮음 - GC 빈도 감소: {Frequency}", _gcFrequency);
            }

            if (previousFrequency != _gcFrequency)
            {
                _logger.LogInformation("GC 빈도 조절: {PreviousFrequency} → {NewFrequency} (메모리 압박: {Pressure:P})", 
                    previousFrequency, _gcFrequency, memoryPressure);
            }

            _lastGcFrequencyAdjustment = DateTime.Now;
        }

        /// <summary>
        /// GC 간격 계산
        /// </summary>
        private TimeSpan GetGcInterval()
        {
            return _gcFrequency switch
            {
                0 => TimeSpan.FromMinutes(15), // 최소 GC: 15분
                1 => TimeSpan.FromMinutes(5),  // 기본 GC: 5분
                2 => TimeSpan.FromMinutes(2),  // 빈번한 GC: 2분
                _ => TimeSpan.FromMinutes(5)
            };
        }

        /// <summary>
        /// 현재 GC 빈도 설정 반환
        /// </summary>
        public int GetGcFrequency()
        {
            return _gcFrequency;
        }

        /// <summary>
        /// GC 빈도 수동 설정
        /// </summary>
        public void SetGcFrequency(int frequency)
        {
            if (frequency < 0 || frequency > 2)
            {
                _logger.LogWarning("잘못된 GC 빈도 설정: {Frequency} (0-2 범위)", frequency);
                return;
            }

            var previousFrequency = _gcFrequency;
            _gcFrequency = frequency;
            
            _logger.LogInformation("GC 빈도 수동 설정: {PreviousFrequency} → {NewFrequency}", 
                previousFrequency, _gcFrequency);
        }

        /// <summary>
        /// 메모리 압박 기반 동적 배치 크기 조절
        /// </summary>
        public int GetDynamicBatchSize(int baseBatchSize)
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            var memoryPressure = CalculateMemoryPressure(currentMemory);

            return memoryPressure switch
            {
                > 0.8 => Math.Max(100, baseBatchSize / 4),     // 매우 높은 압박: 1/4로 감소
                > 0.6 => Math.Max(200, baseBatchSize / 2),     // 높은 압박: 1/2로 감소
                > 0.4 => baseBatchSize,                        // 보통 압박: 기본 크기
                _ => Math.Min(10000, baseBatchSize * 2)        // 낮은 압박: 2배로 증가
            };
        }

        /// <summary>
        /// 메모리 사용량 예측
        /// </summary>
        public MemoryUsagePrediction PredictMemoryUsage(int estimatedItems, int batchSize)
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            var estimatedMemoryPerItem = 0.1; // MB per item (추정값)
            var estimatedTotalMemory = currentMemory + (estimatedItems * estimatedMemoryPerItem);
            var estimatedPressure = CalculateMemoryPressure((long)estimatedTotalMemory);

            return new MemoryUsagePrediction
            {
                CurrentMemoryMB = currentMemory,
                EstimatedTotalMemoryMB = estimatedTotalMemory,
                EstimatedPressure = estimatedPressure,
                RecommendedBatchSize = GetDynamicBatchSize(batchSize),
                IsMemorySafe = estimatedPressure < 0.8,
                WarningLevel = estimatedPressure switch
                {
                    > 0.9 => "위험",
                    > 0.7 => "주의",
                    > 0.5 => "보통",
                    _ => "안전"
                }
            };
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _logger.LogInformation("메모리 최적화 서비스 종료");
        }
    }

    /// <summary>
    /// 메모리 사용량 통계
    /// </summary>
    public class MemoryUsageStats
    {
        public long CurrentUsageMB { get; set; }
        public int MaxAllowedMB { get; set; }
        public double PressureLevel { get; set; }
        public int GcCount { get; set; }
        public DateTime LastGcTime { get; set; }
        public bool IsPressureHigh { get; set; }
    }

    /// <summary>
    /// 메모리 최적화 권장사항
    /// </summary>
    public class MemoryOptimizationRecommendation
    {
        public bool IsOptimizationNeeded { get; set; }
        public List<string> RecommendedActions { get; set; } = new List<string>();
    }

    /// <summary>
    /// 메모리 히스토리 통계
    /// </summary>
    public class MemoryHistoryStats
    {
        public double AverageMB { get; set; }
        public double MinMB { get; set; }
        public double MaxMB { get; set; }
        public double CurrentMB { get; set; }
        public int SampleCount { get; set; }
        public int OptimalBatchSize { get; set; }
    }

    /// <summary>
    /// 메모리 사용량 예측
    /// </summary>
    public class MemoryUsagePrediction
    {
        public long CurrentMemoryMB { get; set; }
        public double EstimatedTotalMemoryMB { get; set; }
        public double EstimatedPressure { get; set; }
        public int RecommendedBatchSize { get; set; }
        public bool IsMemorySafe { get; set; }
        public string WarningLevel { get; set; } = string.Empty;
    }
}
