using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;
using System.Collections.Concurrent;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 중앙집중식 리소스 모니터링 서비스
    /// 모든 리소스 분석 요청을 중앙에서 관리하여 중복 제거
    /// </summary>
    public class CentralizedResourceMonitor : IDisposable
    {
        private readonly ILogger<CentralizedResourceMonitor> _logger;
        private readonly SystemResourceAnalyzer _resourceAnalyzer;
        private readonly PerformanceSettings _settings;
        
        private readonly Timer _monitoringTimer;
        private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();
        private readonly ConcurrentDictionary<string, SystemResourceInfo> _cachedResourceInfo = new();
        
        private volatile SystemResourceInfo? _currentResourceInfo;
        private volatile bool _isMonitoring = false;
        private DateTime _lastFullAnalysis = DateTime.MinValue;
        
        // 동시 실행 방지용 락 (single-flight)
        private readonly SemaphoreSlim _analysisLock = new(1, 1);
        
        // 이벤트 기반 알림
        public event EventHandler<ResourceInfoUpdatedEventArgs>? ResourceInfoUpdated;
        
        public CentralizedResourceMonitor(
            ILogger<CentralizedResourceMonitor> logger,
            SystemResourceAnalyzer resourceAnalyzer,
            PerformanceSettings settings)
        {
            _logger = logger;
            _resourceAnalyzer = resourceAnalyzer;
            _settings = settings;
            
            // 모니터링 타이머 시작 (기본 5분 간격으로 변경)
            _monitoringTimer = new Timer(MonitorResources, null, 
                TimeSpan.Zero, TimeSpan.FromMinutes(5));
            
            _logger.LogInformation("중앙집중식 리소스 모니터 초기화 완료");
        }

        /// <summary>
        /// 리소스 정보 가져오기 (캐시 우선)
        /// </summary>
        public SystemResourceInfo GetResourceInfo(string requester = "Unknown", bool forceRefresh = false)
        {
            try
            {
                // 강제 새로고침이거나 캐시가 없거나 오래된 경우에만 새로 분석 (10분 유지)
                var needsRefresh = forceRefresh || _currentResourceInfo == null || 
                                   DateTime.Now - _lastFullAnalysis > TimeSpan.FromMinutes(10);

                if (needsRefresh)
                {
                    // 단일 실행 보장: 동시 요청을 병합
                    _analysisLock.Wait();
                    try
                    {
                        // 잠금 후 다시 검사(이미 다른 쓰레드가 갱신했을 수 있음)
                        if (forceRefresh || _currentResourceInfo == null || 
                            DateTime.Now - _lastFullAnalysis > TimeSpan.FromMinutes(10))
                        {
                            _logger.LogDebug("리소스 정보 새로고침 요청: {Requester}", requester);
                            _currentResourceInfo = _resourceAnalyzer.AnalyzeSystemResources();
                            _lastFullAnalysis = DateTime.Now;
                        }
                    }
                    finally
                    {
                        _analysisLock.Release();
                    }
                }
                
                // 요청자별 마지막 업데이트 시간 기록
                _lastUpdateTimes[requester] = DateTime.Now;
                
                return _currentResourceInfo ?? GetDefaultResourceInfo();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "리소스 정보 가져오기 실패: {Requester}", requester);
                return GetDefaultResourceInfo();
            }
        }

        /// <summary>
        /// 특정 요청자에 대한 리소스 정보 가져오기
        /// </summary>
        public SystemResourceInfo GetResourceInfoForRequester(string requester, bool forceRefresh = false)
        {
            var cacheKey = $"requester_{requester}";
            
            // 캐시된 정보가 있고 최신인 경우 반환
            if (!forceRefresh && _cachedResourceInfo.TryGetValue(cacheKey, out var cachedInfo))
            {
                if (_lastUpdateTimes.TryGetValue(cacheKey, out var lastUpdate) && 
                    DateTime.Now - lastUpdate < TimeSpan.FromMinutes(10)) // 10분으로 변경
                {
                    return cachedInfo;
                }
            }
            
            // 새로 분석하고 캐시에 저장
            var resourceInfo = GetResourceInfo(requester, forceRefresh);
            _cachedResourceInfo[cacheKey] = resourceInfo;
            _lastUpdateTimes[cacheKey] = DateTime.Now;
            
            return resourceInfo;
        }

        /// <summary>
        /// 리소스 모니터링 (타이머 콜백)
        /// </summary>
        private void MonitorResources(object? state)
        {
            try
            {
                if (_isMonitoring) return; // 중복 실행 방지
                
                _isMonitoring = true;
                
                // 주기적 리소스 분석 (10분마다로 변경)
                if (DateTime.Now - _lastFullAnalysis > TimeSpan.FromMinutes(10))
                {
                    var resourceInfo = _resourceAnalyzer.AnalyzeSystemResources();
                    _currentResourceInfo = resourceInfo;
                    _lastFullAnalysis = DateTime.Now;
                    
                    // 이벤트 발생
                    ResourceInfoUpdated?.Invoke(this, new ResourceInfoUpdatedEventArgs
                    {
                        ResourceInfo = resourceInfo,
                        UpdateTime = DateTime.Now
                    });
                    
                    _logger.LogDebug("주기적 리소스 분석 완료: CPU {CpuCount}개, RAM {TotalMemoryGB}GB", 
                        resourceInfo.ProcessorCount, resourceInfo.TotalMemoryGB);
                }
                
                // 오래된 캐시 정리
                CleanupOldCache();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "리소스 모니터링 중 오류 발생");
            }
            finally
            {
                _isMonitoring = false;
            }
        }

        /// <summary>
        /// 오래된 캐시 정리
        /// </summary>
        private void CleanupOldCache()
        {
            try
            {
                var cutoffTime = DateTime.Now - TimeSpan.FromMinutes(10);
                var keysToRemove = new List<string>();
                
                foreach (var kvp in _lastUpdateTimes)
                {
                    if (kvp.Value < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    _lastUpdateTimes.TryRemove(key, out _);
                    _cachedResourceInfo.TryRemove(key, out _);
                }
                
                if (keysToRemove.Count > 0)
                {
                    _logger.LogDebug("오래된 캐시 정리 완료: {Count}개 항목 제거", keysToRemove.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "캐시 정리 중 오류 발생");
            }
        }

        /// <summary>
        /// 모니터링 간격 설정
        /// </summary>
        public void SetMonitoringInterval(int seconds)
        {
            try
            {
                var interval = Math.Max(10, Math.Min(seconds, 300)); // 10초~5분 범위
                _monitoringTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(interval));
                
                _logger.LogInformation("모니터링 간격 변경: {Interval}초", interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "모니터링 간격 설정 실패");
            }
        }

        /// <summary>
        /// 모니터링 활성화/비활성화
        /// </summary>
        public void SetMonitoringEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    _monitoringTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(5)); // 5분으로 변경
                    _logger.LogInformation("리소스 모니터링 활성화");
                }
                else
                {
                    _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _logger.LogInformation("리소스 모니터링 비활성화");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "모니터링 상태 변경 실패");
            }
        }

        /// <summary>
        /// 기본 리소스 정보 반환
        /// </summary>
        private SystemResourceInfo GetDefaultResourceInfo()
        {
            return new SystemResourceInfo
            {
                ProcessorCount = Environment.ProcessorCount,
                AvailableMemoryGB = 4.0,
                TotalMemoryGB = 8.0,
                CurrentProcessMemoryMB = 100,
                RecommendedMaxParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                RecommendedBatchSize = 1000,
                RecommendedMaxMemoryUsageMB = 2048,
                SystemLoadLevel = SystemLoadLevel.Medium
            };
        }

        /// <summary>
        /// 리소스 사용 통계
        /// </summary>
        public ResourceMonitorStats GetStats()
        {
            return new ResourceMonitorStats
            {
                CacheSize = _cachedResourceInfo.Count,
                LastFullAnalysis = _lastFullAnalysis,
                IsMonitoring = _isMonitoring,
                ActiveRequesters = _lastUpdateTimes.Count
            };
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _cachedResourceInfo.Clear();
            _lastUpdateTimes.Clear();
            _logger.LogInformation("중앙집중식 리소스 모니터 종료");
        }
    }

    /// <summary>
    /// 리소스 정보 업데이트 이벤트 인수
    /// </summary>
    public class ResourceInfoUpdatedEventArgs : EventArgs
    {
        public SystemResourceInfo ResourceInfo { get; set; } = new();
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// 리소스 모니터 통계
    /// </summary>
    public class ResourceMonitorStats
    {
        public int CacheSize { get; set; }
        public DateTime LastFullAnalysis { get; set; }
        public bool IsMonitoring { get; set; }
        public int ActiveRequesters { get; set; }
    }
}
