#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 메모리 및 리소스 관리를 위한 서비스
    /// Requirements: 6.4 - 메모리 사용량 모니터링 및 최적화
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public class ResourceManagementService : IDisposable
    {
        private readonly ILogger<ResourceManagementService> _logger;
        
        // 메모리 모니터링
        private readonly Timer _memoryMonitorTimer;
        private readonly PerformanceCounter? _memoryCounter;
        private long _maxMemoryUsage = 500 * 1024 * 1024; // 500MB
        private readonly List<long> _memoryUsageHistory = new();
        
        // GDAL 객체 추적
        private readonly HashSet<WeakReference> _gdalObjects = new();
        private readonly object _gdalObjectsLock = new();
        private int _totalGdalObjectsCreated = 0;
        private int _totalGdalObjectsDisposed = 0;
        
        // 이미지 리소스 캐시
        private readonly Dictionary<string, WeakReference> _imageCache = new();
        private readonly object _imageCacheLock = new();
        private long _imageCacheSize = 0;
        private readonly long _maxImageCacheSize = 100 * 1024 * 1024; // 100MB
        
        // 리소스 정리 설정
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
        private bool _autoCleanupEnabled = true;
        
        // 통계
        private int _totalCleanupOperations = 0;
        private long _totalMemoryFreed = 0;
        private DateTime _lastCleanupTime = DateTime.UtcNow;

        public ResourceManagementService(ILogger<ResourceManagementService> logger)
        {
            _logger = logger;
            
            try
            {
                // 메모리 성능 카운터 초기화 (Windows에서만 사용 가능)
                _memoryCounter = new PerformanceCounter("Process", "Working Set", Process.GetCurrentProcess().ProcessName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "메모리 성능 카운터 초기화 실패 - 기본 GC 메모리 사용");
            }
            
            // 메모리 모니터링 타이머 (30초마다)
            _memoryMonitorTimer = new Timer(MonitorMemoryUsage, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            
            // 리소스 정리 타이머
            _cleanupTimer = new Timer(PerformScheduledCleanup, null, 
                _cleanupInterval, _cleanupInterval);
            
            _logger.LogInformation("리소스 관리 서비스 초기화됨");
        }

        #region GDAL 객체 관리

        /// <summary>
        /// GDAL 객체를 등록하여 추적합니다
        /// </summary>
        /// <param name="gdalObject">추적할 GDAL 객체</param>
        public void RegisterGdalObject(object gdalObject)
        {
            if (gdalObject == null) return;

            lock (_gdalObjectsLock)
            {
                _gdalObjects.Add(new WeakReference(gdalObject));
                _totalGdalObjectsCreated++;
            }
            
            _logger.LogTrace("GDAL 객체 등록됨: {Type}", gdalObject.GetType().Name);
        }

        /// <summary>
        /// GDAL 객체의 해제를 기록합니다
        /// </summary>
        /// <param name="gdalObject">해제된 GDAL 객체</param>
        public void UnregisterGdalObject(object gdalObject)
        {
            if (gdalObject == null) return;

            lock (_gdalObjectsLock)
            {
                _totalGdalObjectsDisposed++;
            }
            
            _logger.LogTrace("GDAL 객체 해제됨: {Type}", gdalObject.GetType().Name);
        }

        /// <summary>
        /// 모든 GDAL 객체를 적절히 해제합니다
        /// </summary>
        public void CleanupGdalObjects()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var cleanedCount = 0;

                lock (_gdalObjectsLock)
                {
                    var aliveObjects = new HashSet<WeakReference>();
                    
                    foreach (var weakRef in _gdalObjects)
                    {
                        if (weakRef.IsAlive)
                        {
                            var target = weakRef.Target;
                            if (target != null)
                            {
                                try
                                {
                                    // GDAL 객체 타입별 적절한 해제
                                    DisposeGdalObject(target);
                                    cleanedCount++;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "GDAL 객체 해제 실패: {Type}", target.GetType().Name);
                                }
                            }
                        }
                        else
                        {
                            aliveObjects.Add(weakRef);
                        }
                    }
                    
                    _gdalObjects.Clear();
                    foreach (var aliveRef in aliveObjects)
                    {
                        _gdalObjects.Add(aliveRef);
                    }
                }

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("GDAL 객체 정리 완료: {Count}개 해제, 소요시간: {ElapsedMs}ms", 
                    cleanedCount, elapsedMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 객체 정리 실패");
            }
        }

        /// <summary>
        /// GDAL 객체 타입별 적절한 해제를 수행합니다
        /// </summary>
        private void DisposeGdalObject(object gdalObject)
        {
            switch (gdalObject)
            {
                case DataSource dataSource:
                    dataSource.Dispose();
                    break;
                case Layer layer:
                    layer.Dispose();
                    break;
                case Feature feature:
                    feature.Dispose();
                    break;
                case Geometry geometry:
                    geometry.Dispose();
                    break;
                case FieldDefn fieldDefn:
                    fieldDefn.Dispose();
                    break;
                case OSGeo.OSR.SpatialReference spatialRef:
                    spatialRef.Dispose();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                default:
                    _logger.LogTrace("알 수 없는 GDAL 객체 타입: {Type}", gdalObject.GetType().Name);
                    break;
            }
        }

        /// <summary>
        /// 현재 추적 중인 GDAL 객체 통계를 가져옵니다
        /// </summary>
        public GdalObjectStatistics GetGdalObjectStatistics()
        {
            lock (_gdalObjectsLock)
            {
                var aliveCount = _gdalObjects.Count(wr => wr.IsAlive);
                
                return new GdalObjectStatistics
                {
                    TotalCreated = _totalGdalObjectsCreated,
                    TotalDisposed = _totalGdalObjectsDisposed,
                    CurrentlyAlive = aliveCount,
                    PendingDisposal = _gdalObjects.Count - aliveCount
                };
            }
        }

        #endregion

        #region 이미지 리소스 캐싱

        /// <summary>
        /// 이미지를 캐시에 저장합니다
        /// </summary>
        /// <param name="key">캐시 키</param>
        /// <param name="image">이미지 객체</param>
        /// <param name="estimatedSize">예상 크기 (바이트)</param>
        public void CacheImage(string key, Image image, long estimatedSize)
        {
            if (string.IsNullOrEmpty(key) || image == null) return;

            lock (_imageCacheLock)
            {
                // 캐시 크기 제한 확인
                if (_imageCacheSize + estimatedSize > _maxImageCacheSize)
                {
                    CleanupImageCache();
                }

                // 기존 항목 제거
                if (_imageCache.TryGetValue(key, out var existingRef))
                {
                    if (existingRef.Target is Image existingImage)
                    {
                        _imageCacheSize -= GetImageSize(existingImage);
                    }
                }

                // 새 항목 추가
                _imageCache[key] = new WeakReference(image);
                _imageCacheSize += estimatedSize;
            }

            _logger.LogTrace("이미지 캐시됨: {Key}, 크기: {Size}bytes", key, estimatedSize);
        }

        /// <summary>
        /// 캐시에서 이미지를 가져옵니다
        /// </summary>
        /// <param name="key">캐시 키</param>
        /// <returns>캐시된 이미지 (없으면 null)</returns>
        public Image? GetCachedImage(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (_imageCacheLock)
            {
                if (_imageCache.TryGetValue(key, out var weakRef) && weakRef.IsAlive)
                {
                    if (weakRef.Target is Image image)
                    {
                        _logger.LogTrace("이미지 캐시 히트: {Key}", key);
                        return image;
                    }
                }
            }

            _logger.LogTrace("이미지 캐시 미스: {Key}", key);
            return null;
        }

        /// <summary>
        /// 이미지 캐시를 정리합니다
        /// </summary>
        public void CleanupImageCache()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var removedCount = 0;
                var freedSize = 0L;

                lock (_imageCacheLock)
                {
                    var keysToRemove = new List<string>();
                    
                    foreach (var kvp in _imageCache)
                    {
                        if (!kvp.Value.IsAlive || kvp.Value.Target == null)
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                        else if (kvp.Value.Target is Image image)
                        {
                            // 오래된 이미지 제거 (LRU 방식 간소화)
                            var imageSize = GetImageSize(image);
                            if (_imageCacheSize > _maxImageCacheSize * 0.8) // 80% 초과 시 정리
                            {
                                keysToRemove.Add(kvp.Key);
                                freedSize += imageSize;
                            }
                        }
                    }

                    foreach (var key in keysToRemove)
                    {
                        _imageCache.Remove(key);
                        removedCount++;
                    }

                    _imageCacheSize -= freedSize;
                }

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("이미지 캐시 정리 완료: {Count}개 제거, {Size}bytes 해제, 소요시간: {ElapsedMs}ms", 
                    removedCount, freedSize, elapsedMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "이미지 캐시 정리 실패");
            }
        }

        /// <summary>
        /// 이미지의 예상 크기를 계산합니다
        /// </summary>
        private long GetImageSize(Image image)
        {
            // 간단한 크기 추정: 너비 × 높이 × 4바이트 (RGBA)
            return image.Width * image.Height * 4L;
        }

        /// <summary>
        /// 이미지 캐시 통계를 가져옵니다
        /// </summary>
        public ImageCacheStatistics GetImageCacheStatistics()
        {
            lock (_imageCacheLock)
            {
                var aliveCount = _imageCache.Count(kvp => kvp.Value.IsAlive);
                
                return new ImageCacheStatistics
                {
                    TotalCachedItems = _imageCache.Count,
                    AliveCachedItems = aliveCount,
                    CurrentCacheSizeBytes = _imageCacheSize,
                    MaxCacheSizeBytes = _maxImageCacheSize,
                    CacheUtilization = (double)_imageCacheSize / _maxImageCacheSize
                };
            }
        }

        #endregion

        #region 메모리 사용량 모니터링

        /// <summary>
        /// 메모리 사용량을 모니터링합니다
        /// </summary>
        private void MonitorMemoryUsage(object? state)
        {
            try
            {
                var currentMemory = GetCurrentMemoryUsage();
                
                // 메모리 사용량 히스토리 업데이트
                _memoryUsageHistory.Add(currentMemory);
                if (_memoryUsageHistory.Count > 100) // 최근 100개만 유지
                {
                    _memoryUsageHistory.RemoveAt(0);
                }

                // 메모리 사용량 초과 확인
                if (currentMemory > _maxMemoryUsage)
                {
                    _logger.LogWarning("메모리 사용량 초과: {CurrentMB}MB / {MaxMB}MB", 
                        currentMemory / 1024 / 1024, _maxMemoryUsage / 1024 / 1024);
                    
                    // 자동 정리 실행
                    if (_autoCleanupEnabled)
                    {
                        _ = Task.Run(PerformEmergencyCleanup);
                    }
                }
                else
                {
                    _logger.LogTrace("메모리 사용량: {CurrentMB}MB / {MaxMB}MB", 
                        currentMemory / 1024 / 1024, _maxMemoryUsage / 1024 / 1024);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 모니터링 실패");
            }
        }

        /// <summary>
        /// 현재 메모리 사용량을 가져옵니다
        /// </summary>
        private long GetCurrentMemoryUsage()
        {
            try
            {
                if (_memoryCounter != null)
                {
                    return (long)_memoryCounter.NextValue();
                }
                else
                {
                    return GC.GetTotalMemory(false);
                }
            }
            catch
            {
                return GC.GetTotalMemory(false);
            }
        }

        /// <summary>
        /// 최대 메모리 사용량을 설정합니다
        /// </summary>
        /// <param name="maxMemoryMB">최대 메모리 사용량 (MB)</param>
        public void SetMaxMemoryUsage(int maxMemoryMB)
        {
            _maxMemoryUsage = maxMemoryMB * 1024L * 1024L;
            _logger.LogInformation("최대 메모리 사용량 설정: {MaxMemoryMB}MB", maxMemoryMB);
        }

        /// <summary>
        /// 자동 정리 기능을 활성화/비활성화합니다
        /// </summary>
        /// <param name="enabled">활성화 여부</param>
        public void SetAutoCleanup(bool enabled)
        {
            _autoCleanupEnabled = enabled;
            _logger.LogInformation("자동 정리 기능: {Enabled}", enabled);
        }

        #endregion

        #region 리소스 정리

        /// <summary>
        /// 예약된 리소스 정리를 수행합니다
        /// </summary>
        private void PerformScheduledCleanup(object? state)
        {
            if (!_autoCleanupEnabled) return;

            try
            {
                _logger.LogDebug("예약된 리소스 정리 시작");
                PerformCleanup(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "예약된 리소스 정리 실패");
            }
        }

        /// <summary>
        /// 응급 리소스 정리를 수행합니다
        /// </summary>
        private void PerformEmergencyCleanup()
        {
            try
            {
                _logger.LogWarning("응급 리소스 정리 시작");
                PerformCleanup(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "응급 리소스 정리 실패");
            }
        }

        /// <summary>
        /// 리소스 정리를 수행합니다
        /// </summary>
        /// <param name="aggressive">적극적 정리 여부</param>
        public void PerformCleanup(bool aggressive = false)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var beforeMemory = GetCurrentMemoryUsage();

                // GDAL 객체 정리
                CleanupGdalObjects();

                // 이미지 캐시 정리
                CleanupImageCache();

                // 가비지 컬렉션
                if (aggressive)
                {
                    // 적극적 정리: 모든 세대 정리
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                else
                {
                    // 일반 정리: 0세대만
                    GC.Collect(0, GCCollectionMode.Optimized);
                }

                var afterMemory = GetCurrentMemoryUsage();
                var freedMemory = beforeMemory - afterMemory;
                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _totalCleanupOperations++;
                _totalMemoryFreed += Math.Max(0, freedMemory);
                _lastCleanupTime = DateTime.UtcNow;

                _logger.LogInformation("리소스 정리 완료: {FreedMB}MB 해제, 소요시간: {ElapsedMs}ms, 적극적: {Aggressive}", 
                    freedMemory / 1024 / 1024, elapsedMs, aggressive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "리소스 정리 실패");
            }
        }

        #endregion

        #region 통계 및 정보

        /// <summary>
        /// 리소스 관리 통계를 가져옵니다
        /// </summary>
        public ResourceManagementStatistics GetStatistics()
        {
            var currentMemory = GetCurrentMemoryUsage();
            var gdalStats = GetGdalObjectStatistics();
            var imageStats = GetImageCacheStatistics();

            return new ResourceManagementStatistics
            {
                CurrentMemoryUsageMB = currentMemory / 1024 / 1024,
                MaxMemoryUsageMB = _maxMemoryUsage / 1024 / 1024,
                AverageMemoryUsageMB = _memoryUsageHistory.Any() ? _memoryUsageHistory.Average() / 1024 / 1024 : 0,
                PeakMemoryUsageMB = _memoryUsageHistory.Any() ? _memoryUsageHistory.Max() / 1024 / 1024 : 0,
                TotalCleanupOperations = _totalCleanupOperations,
                TotalMemoryFreedMB = _totalMemoryFreed / 1024 / 1024,
                LastCleanupTime = _lastCleanupTime,
                AutoCleanupEnabled = _autoCleanupEnabled,
                GdalObjectStats = gdalStats,
                ImageCacheStats = imageStats
            };
        }

        /// <summary>
        /// 메모리 사용량 히스토리를 가져옵니다
        /// </summary>
        public List<long> GetMemoryUsageHistory()
        {
            return _memoryUsageHistory.ToList();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            try
            {
                _memoryMonitorTimer?.Dispose();
                _cleanupTimer?.Dispose();
                _memoryCounter?.Dispose();
                
                // 최종 정리
                PerformCleanup(true);
                
                _logger.LogInformation("리소스 관리 서비스 해제됨");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "리소스 관리 서비스 해제 실패");
            }
        }

        #endregion
    }

    /// <summary>
    /// GDAL 객체 통계
    /// </summary>
    public class GdalObjectStatistics
    {
        public int TotalCreated { get; set; }
        public int TotalDisposed { get; set; }
        public int CurrentlyAlive { get; set; }
        public int PendingDisposal { get; set; }
    }

    /// <summary>
    /// 이미지 캐시 통계
    /// </summary>
    public class ImageCacheStatistics
    {
        public int TotalCachedItems { get; set; }
        public int AliveCachedItems { get; set; }
        public long CurrentCacheSizeBytes { get; set; }
        public long MaxCacheSizeBytes { get; set; }
        public double CacheUtilization { get; set; }
    }

    /// <summary>
    /// 리소스 관리 통계
    /// </summary>
    public class ResourceManagementStatistics
    {
        public long CurrentMemoryUsageMB { get; set; }
        public long MaxMemoryUsageMB { get; set; }
        public double AverageMemoryUsageMB { get; set; }
        public long PeakMemoryUsageMB { get; set; }
        public int TotalCleanupOperations { get; set; }
        public long TotalMemoryFreedMB { get; set; }
        public DateTime LastCleanupTime { get; set; }
        public bool AutoCleanupEnabled { get; set; }
        public GdalObjectStatistics GdalObjectStats { get; set; } = new();
        public ImageCacheStatistics ImageCacheStats { get; set; } = new();
    }
}