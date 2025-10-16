using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 고급 메모리 관리 및 리소스 최적화를 담당하는 서비스
    /// 실시간 모니터링, 임계치 관리, 안전한 리소스 정리, 가비지 컬렉션 최적화 기능 제공
    /// </summary>
    public class AdvancedMemoryManager : IAdvancedMemoryManager
    {
        private readonly ILogger<AdvancedMemoryManager> _logger;
        private readonly long _maxMemoryUsageBytes;
        private readonly Timer _memoryMonitorTimer;
        private readonly Timer _gcOptimizationTimer;
        
        // 메모리 임계치 설정
        private readonly double _warningThreshold = 0.7;   // 70%
        private readonly double _criticalThreshold = 0.85; // 85%
        private readonly double _emergencyThreshold = 0.95; // 95%
        
        // 메모리 사용량 추적
        private readonly ConcurrentQueue<MemorySnapshot> _memoryHistory;
        private readonly int _maxHistorySize = 100;
        private long _currentMemoryUsage;
        private long _peakMemoryUsage;
        private DateTime _lastGcTime = DateTime.MinValue;
        private int _forcedGcCount;
        
        // 리소스 정리 관리
        private readonly ConcurrentDictionary<string, IDisposable> _managedResources;
        private readonly ConcurrentDictionary<string, WeakReference> _weakReferences;
        private readonly object _cleanupLock = new object();
        
        // 취소 토큰 관리
        private readonly CancellationTokenSource _shutdownTokenSource;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _operationTokens;
        
        // 성능 카운터
        private long _totalAllocations;
        private long _totalDeallocations;
        private long _memoryLeakDetections;

        /// <summary>
        /// 메모리 압박 상황 발생 이벤트
        /// </summary>
        public event EventHandler<AdvancedMemoryPressureEventArgs>? MemoryPressureDetected;

        /// <summary>
        /// 메모리 누수 감지 이벤트
        /// </summary>
        public event EventHandler<MemoryLeakEventArgs>? MemoryLeakDetected;

        /// <summary>
        /// 리소스 정리 완료 이벤트
        /// </summary>
        public event EventHandler<ResourceCleanupEventArgs>? ResourceCleanupCompleted;

        public AdvancedMemoryManager(ILogger<AdvancedMemoryManager> logger, long maxMemoryUsageMB = 2048)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxMemoryUsageBytes = maxMemoryUsageMB * 1024 * 1024;
            
            // 컬렉션 초기화
            _memoryHistory = new ConcurrentQueue<MemorySnapshot>();
            _managedResources = new ConcurrentDictionary<string, IDisposable>();
            _weakReferences = new ConcurrentDictionary<string, WeakReference>();
            _operationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
            _shutdownTokenSource = new CancellationTokenSource();
            
            // 타이머 설정
            _memoryMonitorTimer = new Timer(MonitorMemoryUsage, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            _gcOptimizationTimer = new Timer(OptimizeGarbageCollection, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.LogInformation("고급 메모리 관리자 초기화 완료 - 최대 메모리: {MaxMemoryMB}MB, " +
                                 "경고임계치: {WarningThreshold:P0}, 위험임계치: {CriticalThreshold:P0}",
                maxMemoryUsageMB, _warningThreshold, _criticalThreshold);
        }

        /// <summary>
        /// 현재 메모리 사용량을 조회합니다
        /// </summary>
        public long GetCurrentMemoryUsage()
        {
            var memoryUsage = GC.GetTotalMemory(false);
            Interlocked.Exchange(ref _currentMemoryUsage, memoryUsage);
            
            // 피크 메모리 사용량 업데이트
            if (memoryUsage > _peakMemoryUsage)
            {
                Interlocked.Exchange(ref _peakMemoryUsage, memoryUsage);
            }
            
            return memoryUsage;
        }

        /// <summary>
        /// 메모리 압박 수준을 확인합니다
        /// </summary>
        public MemoryPressureLevel GetMemoryPressureLevel()
        {
            var currentUsage = GetCurrentMemoryUsage();
            var pressureRatio = (double)currentUsage / _maxMemoryUsageBytes;
            
            if (pressureRatio >= _emergencyThreshold)
                return MemoryPressureLevel.Emergency;
            else if (pressureRatio >= _criticalThreshold)
                return MemoryPressureLevel.Critical;
            else if (pressureRatio >= _warningThreshold)
                return MemoryPressureLevel.Warning;
            else
                return MemoryPressureLevel.Normal;
        }

        /// <summary>
        /// 실시간 메모리 사용량 모니터링 및 임계치 관리
        /// </summary>
        public async Task<MemoryMonitoringResult> MonitorMemoryWithThresholdsAsync(CancellationToken cancellationToken = default)
        {
            var currentUsage = GetCurrentMemoryUsage();
            var pressureLevel = GetMemoryPressureLevel();
            var pressureRatio = (double)currentUsage / _maxMemoryUsageBytes;
            
            var result = new MemoryMonitoringResult
            {
                CurrentMemoryUsage = currentUsage,
                MaxMemoryLimit = _maxMemoryUsageBytes,
                PressureRatio = pressureRatio,
                PressureLevel = pressureLevel,
                PeakMemoryUsage = _peakMemoryUsage,
                MemoryTrend = AnalyzeMemoryTrend(),
                RecommendedActions = GetRecommendedActions(pressureLevel),
                MonitoringTime = DateTime.UtcNow
            };

            // 임계치 초과 시 자동 조치
            switch (pressureLevel)
            {
                case MemoryPressureLevel.Warning:
                    _logger.LogWarning("메모리 사용량 경고 수준: {CurrentMB:F2}MB / {MaxMB:F2}MB ({Ratio:P1})",
                        currentUsage / (1024.0 * 1024.0), _maxMemoryUsageBytes / (1024.0 * 1024.0), pressureRatio);
                    
                    // 약한 참조 정리
                    await CleanupWeakReferencesAsync();
                    break;

                case MemoryPressureLevel.Critical:
                    _logger.LogError("메모리 사용량 위험 수준: {CurrentMB:F2}MB / {MaxMB:F2}MB ({Ratio:P1})",
                        currentUsage / (1024.0 * 1024.0), _maxMemoryUsageBytes / (1024.0 * 1024.0), pressureRatio);
                    
                    // 강제 가비지 컬렉션 및 리소스 정리
                    await PerformEmergencyCleanupAsync();
                    break;

                case MemoryPressureLevel.Emergency:
                    _logger.LogCritical("메모리 사용량 응급 수준: {CurrentMB:F2}MB / {MaxMB:F2}MB ({Ratio:P1})",
                        currentUsage / (1024.0 * 1024.0), _maxMemoryUsageBytes / (1024.0 * 1024.0), pressureRatio);
                    
                    // 모든 가능한 정리 작업 수행
                    await PerformCriticalMemoryRecoveryAsync();
                    break;
            }

            // 메모리 압박 이벤트 발생
            if (pressureLevel >= MemoryPressureLevel.Warning)
            {
                OnMemoryPressureDetected(new AdvancedMemoryPressureEventArgs
                {
                    CurrentMemoryUsage = currentUsage,
                    MaxMemoryLimit = _maxMemoryUsageBytes,
                    PressureRatio = pressureRatio,
                    PressureLevel = pressureLevel,
                    RecommendedActions = result.RecommendedActions,
                    MemoryTrend = result.MemoryTrend
                });
            }

            return result;
        }

        /// <summary>
        /// 검수 작업 취소 시 안전한 리소스 정리 메커니즘
        /// </summary>
        public async Task<bool> SafelyCleanupOperationAsync(string operationId, TimeSpan timeout = default)
        {
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("작업 안전 정리 시작: {OperationId}", operationId);
            
            var cleanupStopwatch = Stopwatch.StartNew();
            var cleanupSuccess = true;

            try
            {
                // 1. 작업 취소 토큰 활성화
                if (_operationTokens.TryGetValue(operationId, out var tokenSource))
                {
                    tokenSource.Cancel();
                    _logger.LogDebug("작업 취소 신호 전송: {OperationId}", operationId);
                }

                // 2. 관련 리소스 정리
                var resourcesToCleanup = _managedResources
                    .Where(kvp => kvp.Key.StartsWith(operationId))
                    .ToList();

                foreach (var resource in resourcesToCleanup)
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(timeout);
                        await Task.Run(() =>
                        {
                            resource.Value.Dispose();
                            _managedResources.TryRemove(resource.Key, out _);
                        }, timeoutCts.Token);
                        
                        _logger.LogDebug("리소스 정리 완료: {ResourceKey}", resource.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "리소스 정리 실패: {ResourceKey}", resource.Key);
                        cleanupSuccess = false;
                    }
                }

                // 3. 약한 참조 정리
                await CleanupWeakReferencesAsync();

                // 4. 메모리 정리
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });

                // 5. 작업 토큰 정리
                _operationTokens.TryRemove(operationId, out _);

                cleanupStopwatch.Stop();
                
                _logger.LogInformation("작업 안전 정리 완료: {OperationId} - 성공: {Success}, " +
                                     "소요시간: {ElapsedMs}ms, 정리된 리소스: {ResourceCount}개",
                    operationId, cleanupSuccess, cleanupStopwatch.ElapsedMilliseconds, resourcesToCleanup.Count);

                // 정리 완료 이벤트 발생
                OnResourceCleanupCompleted(new ResourceCleanupEventArgs
                {
                    OperationId = operationId,
                    CleanupSuccess = cleanupSuccess,
                    CleanedResourceCount = resourcesToCleanup.Count,
                    CleanupDuration = cleanupStopwatch.Elapsed
                });

                return cleanupSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "작업 안전 정리 중 오류 발생: {OperationId}", operationId);
                return false;
            }
        }

        /// <summary>
        /// 가비지 컬렉션 최적화 및 메모리 누수 방지
        /// </summary>
        public async Task<GcOptimizationResult> OptimizeGarbageCollectionAsync()
        {
            _logger.LogDebug("가비지 컬렉션 최적화 시작");
            
            var beforeMemory = GetCurrentMemoryUsage();
            var beforeGen0 = GC.CollectionCount(0);
            var beforeGen1 = GC.CollectionCount(1);
            var beforeGen2 = GC.CollectionCount(2);
            
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 1. 약한 참조 정리
                await CleanupWeakReferencesAsync();

                // 2. 단계적 가비지 컬렉션
                await Task.Run(() =>
                {
                    // Generation 0 정리
                    GC.Collect(0, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                    
                    // Generation 1 정리
                    GC.Collect(1, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                    
                    // 필요시 Generation 2 정리
                    var pressureLevel = GetMemoryPressureLevel();
                    if (pressureLevel >= MemoryPressureLevel.Warning)
                    {
                        GC.Collect(2, GCCollectionMode.Forced);
                        GC.WaitForPendingFinalizers();
                        
                        // LOH 압축
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect();
                    }
                });

                stopwatch.Stop();
                var afterMemory = GetCurrentMemoryUsage();
                
                var result = new GcOptimizationResult
                {
                    BeforeMemoryUsage = beforeMemory,
                    AfterMemoryUsage = afterMemory,
                    MemoryFreed = beforeMemory - afterMemory,
                    OptimizationDuration = stopwatch.Elapsed,
                    Gen0Collections = GC.CollectionCount(0) - beforeGen0,
                    Gen1Collections = GC.CollectionCount(1) - beforeGen1,
                    Gen2Collections = GC.CollectionCount(2) - beforeGen2,
                    OptimizationTime = DateTime.UtcNow
                };

                _forcedGcCount++;
                _lastGcTime = DateTime.UtcNow;

                _logger.LogInformation("가비지 컬렉션 최적화 완료 - 해제된 메모리: {FreedMB:F2}MB, " +
                                     "소요시간: {ElapsedMs}ms, Gen0/1/2: {Gen0}/{Gen1}/{Gen2}",
                    result.MemoryFreed / (1024.0 * 1024.0), stopwatch.ElapsedMilliseconds,
                    result.Gen0Collections, result.Gen1Collections, result.Gen2Collections);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "가비지 컬렉션 최적화 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 메모리 누수를 감지합니다
        /// </summary>
        public async Task<List<MemoryLeakSuspect>> DetectMemoryLeaksAsync()
        {
            var suspects = new List<MemoryLeakSuspect>();
            
            try
            {
                // 1. 메모리 사용량 추세 분석
                var memoryTrend = AnalyzeMemoryTrend();
                if (memoryTrend == MemoryTrend.IncreasingRapidly || memoryTrend == MemoryTrend.IncreasingSteadily)
                {
                    suspects.Add(new MemoryLeakSuspect
                    {
                        SuspectType = MemoryLeakType.MemoryTrendAnomaly,
                        Description = $"메모리 사용량이 지속적으로 증가하는 추세: {memoryTrend}",
                        Severity = MemoryLeakSeverity.Medium,
                        DetectedAt = DateTime.UtcNow
                    });
                }

                // 2. 약한 참조 정리 후에도 남아있는 객체 확인
                var beforeCleanup = GetCurrentMemoryUsage();
                await CleanupWeakReferencesAsync();
                var afterCleanup = GetCurrentMemoryUsage();
                
                if (afterCleanup > beforeCleanup * 0.95) // 5% 미만 정리됨
                {
                    suspects.Add(new MemoryLeakSuspect
                    {
                        SuspectType = MemoryLeakType.WeakReferenceCleanupIneffective,
                        Description = "약한 참조 정리 후에도 메모리 사용량이 크게 감소하지 않음",
                        Severity = MemoryLeakSeverity.High,
                        DetectedAt = DateTime.UtcNow
                    });
                }

                // 3. 관리되지 않는 리소스 확인
                var unmanagedResourceCount = _managedResources.Count(kvp => 
                {
                    try
                    {
                        // 리소스가 여전히 유효한지 확인
                        return kvp.Value != null;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (unmanagedResourceCount > 100) // 임계치 초과
                {
                    suspects.Add(new MemoryLeakSuspect
                    {
                        SuspectType = MemoryLeakType.UnmanagedResourceAccumulation,
                        Description = $"관리되지 않는 리소스가 과도하게 누적됨: {unmanagedResourceCount}개",
                        Severity = MemoryLeakSeverity.High,
                        DetectedAt = DateTime.UtcNow
                    });
                }

                // 4. GC 효율성 분석
                var gcEfficiency = AnalyzeGcEfficiency();
                if (gcEfficiency < 0.3) // 30% 미만 효율성
                {
                    suspects.Add(new MemoryLeakSuspect
                    {
                        SuspectType = MemoryLeakType.LowGcEfficiency,
                        Description = $"가비지 컬렉션 효율성이 낮음: {gcEfficiency:P1}",
                        Severity = MemoryLeakSeverity.Medium,
                        DetectedAt = DateTime.UtcNow
                    });
                }

                // 메모리 누수 감지 이벤트 발생
                if (suspects.Count > 0)
                {
                    Interlocked.Increment(ref _memoryLeakDetections);
                    
                    OnMemoryLeakDetected(new MemoryLeakEventArgs
                    {
                        Suspects = suspects,
                        CurrentMemoryUsage = GetCurrentMemoryUsage(),
                        DetectionTime = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("메모리 누수 감지 완료 - 의심 항목: {SuspectCount}개", suspects.Count);
                
                return suspects;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 누수 감지 중 오류 발생");
                return suspects;
            }
        }

        /// <summary>
        /// 리소스를 등록하여 관리합니다
        /// </summary>
        public void RegisterManagedResource(string resourceId, IDisposable resource)
        {
            _managedResources.AddOrUpdate(resourceId, resource, (key, existing) =>
            {
                existing?.Dispose();
                return resource;
            });
            
            Interlocked.Increment(ref _totalAllocations);
            _logger.LogDebug("리소스 등록: {ResourceId}", resourceId);
        }

        /// <summary>
        /// 약한 참조를 등록합니다
        /// </summary>
        public void RegisterWeakReference(string referenceId, object target)
        {
            _weakReferences.AddOrUpdate(referenceId, new WeakReference(target), (key, existing) => new WeakReference(target));
            _logger.LogDebug("약한 참조 등록: {ReferenceId}", referenceId);
        }

        /// <summary>
        /// 작업 취소 토큰을 등록합니다
        /// </summary>
        public CancellationToken RegisterOperationToken(string operationId)
        {
            var tokenSource = new CancellationTokenSource();
            _operationTokens.AddOrUpdate(operationId, tokenSource, (key, existing) =>
            {
                existing?.Dispose();
                return tokenSource;
            });
            
            _logger.LogDebug("작업 토큰 등록: {OperationId}", operationId);
            return tokenSource.Token;
        }

        /// <summary>
        /// 고급 메모리 통계를 가져옵니다
        /// </summary>
        public AdvancedMemoryStatistics GetAdvancedMemoryStatistics()
        {
            var currentMemory = GetCurrentMemoryUsage();
            var pressureLevel = GetMemoryPressureLevel();
            var memoryTrend = AnalyzeMemoryTrend();
            
            return new AdvancedMemoryStatistics
            {
                CurrentMemoryUsage = currentMemory,
                MaxMemoryLimit = _maxMemoryUsageBytes,
                PeakMemoryUsage = _peakMemoryUsage,
                PressureRatio = (double)currentMemory / _maxMemoryUsageBytes,
                PressureLevel = pressureLevel,
                MemoryTrend = memoryTrend,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                ForcedGcCount = _forcedGcCount,
                LastGcTime = _lastGcTime,
                ManagedResourceCount = _managedResources.Count,
                WeakReferenceCount = _weakReferences.Count,
                ActiveOperationCount = _operationTokens.Count,
                TotalAllocations = _totalAllocations,
                TotalDeallocations = _totalDeallocations,
                MemoryLeakDetections = _memoryLeakDetections,
                GcEfficiency = AnalyzeGcEfficiency()
            };
        }

        #region Private Helper Methods

        /// <summary>
        /// 메모리 사용량을 주기적으로 모니터링합니다
        /// </summary>
        private void MonitorMemoryUsage(object? state)
        {
            try
            {
                var currentUsage = GetCurrentMemoryUsage();
                var snapshot = new MemorySnapshot
                {
                    MemoryUsage = currentUsage,
                    Timestamp = DateTime.UtcNow,
                    PressureLevel = GetMemoryPressureLevel()
                };
                
                _memoryHistory.Enqueue(snapshot);
                
                // 히스토리 크기 제한
                while (_memoryHistory.Count > _maxHistorySize)
                {
                    _memoryHistory.TryDequeue(out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 가비지 컬렉션을 최적화합니다
        /// </summary>
        private void OptimizeGarbageCollection(object? state)
        {
            try
            {
                var pressureLevel = GetMemoryPressureLevel();
                if (pressureLevel >= MemoryPressureLevel.Warning)
                {
                    _ = Task.Run(async () => await OptimizeGarbageCollectionAsync());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "자동 GC 최적화 중 오류 발생");
            }
        }

        /// <summary>
        /// 메모리 사용량 추세를 분석합니다
        /// </summary>
        private MemoryTrend AnalyzeMemoryTrend()
        {
            var snapshots = _memoryHistory.ToArray();
            if (snapshots.Length < 10) return MemoryTrend.Stable;
            
            var recentSnapshots = snapshots.TakeLast(10).ToArray();
            var oldestUsage = recentSnapshots.First().MemoryUsage;
            var newestUsage = recentSnapshots.Last().MemoryUsage;
            
            var changeRatio = (double)(newestUsage - oldestUsage) / oldestUsage;
            
            if (changeRatio > 0.2) return MemoryTrend.IncreasingRapidly;
            else if (changeRatio > 0.05) return MemoryTrend.IncreasingSteadily;
            else if (changeRatio < -0.2) return MemoryTrend.DecreasingRapidly;
            else if (changeRatio < -0.05) return MemoryTrend.DecreasingSteadily;
            else return MemoryTrend.Stable;
        }

        /// <summary>
        /// GC 효율성을 분석합니다
        /// </summary>
        private double AnalyzeGcEfficiency()
        {
            var snapshots = _memoryHistory.ToArray();
            if (snapshots.Length < 5) return 1.0;
            
            var beforeGcSnapshots = snapshots.Where(s => s.PressureLevel >= MemoryPressureLevel.Warning).ToArray();
            if (beforeGcSnapshots.Length == 0) return 1.0;
            
            var avgMemoryBeforeGc = beforeGcSnapshots.Average(s => s.MemoryUsage);
            var currentMemory = GetCurrentMemoryUsage();
            
            return Math.Max(0, (avgMemoryBeforeGc - currentMemory) / avgMemoryBeforeGc);
        }

        /// <summary>
        /// 권장 조치 사항을 가져옵니다
        /// </summary>
        private List<string> GetRecommendedActions(MemoryPressureLevel pressureLevel)
        {
            return pressureLevel switch
            {
                MemoryPressureLevel.Warning => new List<string>
                {
                    "배치 크기 감소",
                    "약한 참조 정리",
                    "불필요한 캐시 정리"
                },
                MemoryPressureLevel.Critical => new List<string>
                {
                    "강제 가비지 컬렉션",
                    "리소스 정리",
                    "처리 일시 중단",
                    "배치 크기 대폭 감소"
                },
                MemoryPressureLevel.Emergency => new List<string>
                {
                    "모든 가능한 리소스 정리",
                    "작업 취소",
                    "시스템 재시작 고려",
                    "메모리 누수 점검"
                },
                _ => new List<string>()
            };
        }

        /// <summary>
        /// 약한 참조를 정리합니다
        /// </summary>
        private async Task CleanupWeakReferencesAsync()
        {
            await Task.Run(() =>
            {
                var keysToRemove = new List<string>();
                
                foreach (var kvp in _weakReferences)
                {
                    if (!kvp.Value.IsAlive)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    _weakReferences.TryRemove(key, out _);
                    Interlocked.Increment(ref _totalDeallocations);
                }
                
                if (keysToRemove.Count > 0)
                {
                    _logger.LogDebug("약한 참조 정리 완료: {CleanedCount}개", keysToRemove.Count);
                }
            });
        }

        /// <summary>
        /// 응급 정리를 수행합니다
        /// </summary>
        private async Task PerformEmergencyCleanupAsync()
        {
            await CleanupWeakReferencesAsync();
            await OptimizeGarbageCollectionAsync();
        }

        /// <summary>
        /// 위험 수준 메모리 복구를 수행합니다
        /// </summary>
        private async Task PerformCriticalMemoryRecoveryAsync()
        {
            // 1. 모든 관리 리소스 정리
            var resourcesToCleanup = _managedResources.ToList();
            foreach (var resource in resourcesToCleanup)
            {
                try
                {
                    resource.Value.Dispose();
                    _managedResources.TryRemove(resource.Key, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "위험 수준 리소스 정리 실패: {ResourceKey}", resource.Key);
                }
            }
            
            // 2. 약한 참조 정리
            await CleanupWeakReferencesAsync();
            
            // 3. 강제 GC
            await OptimizeGarbageCollectionAsync();
            
            _logger.LogWarning("위험 수준 메모리 복구 완료 - 정리된 리소스: {ResourceCount}개", resourcesToCleanup.Count);
        }

        /// <summary>
        /// 메모리 압박 이벤트를 발생시킵니다
        /// </summary>
        private void OnMemoryPressureDetected(AdvancedMemoryPressureEventArgs args)
        {
            MemoryPressureDetected?.Invoke(this, args);
        }

        /// <summary>
        /// 메모리 누수 감지 이벤트를 발생시킵니다
        /// </summary>
        private void OnMemoryLeakDetected(MemoryLeakEventArgs args)
        {
            MemoryLeakDetected?.Invoke(this, args);
        }

        /// <summary>
        /// 리소스 정리 완료 이벤트를 발생시킵니다
        /// </summary>
        private void OnResourceCleanupCompleted(ResourceCleanupEventArgs args)
        {
            ResourceCleanupCompleted?.Invoke(this, args);
        }

        #endregion

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _shutdownTokenSource.Cancel();
            
            _memoryMonitorTimer?.Dispose();
            _gcOptimizationTimer?.Dispose();
            
            // 모든 관리 리소스 정리
            foreach (var resource in _managedResources.Values)
            {
                try
                {
                    resource?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "리소스 정리 중 오류 발생");
                }
            }
            
            // 모든 작업 토큰 정리
            foreach (var tokenSource in _operationTokens.Values)
            {
                try
                {
                    tokenSource?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "토큰 정리 중 오류 발생");
                }
            }
            
            _shutdownTokenSource.Dispose();
            _logger.LogInformation("고급 메모리 관리자 종료됨");
        }
    }

    /// <summary>
    /// 고급 메모리 관리자 인터페이스
    /// </summary>
    public interface IAdvancedMemoryManager : IDisposable
    {
        /// <summary>
        /// 메모리 압박 상황 발생 이벤트
        /// </summary>
        event EventHandler<AdvancedMemoryPressureEventArgs>? MemoryPressureDetected;

        /// <summary>
        /// 메모리 누수 감지 이벤트
        /// </summary>
        event EventHandler<MemoryLeakEventArgs>? MemoryLeakDetected;

        /// <summary>
        /// 리소스 정리 완료 이벤트
        /// </summary>
        event EventHandler<ResourceCleanupEventArgs>? ResourceCleanupCompleted;

        /// <summary>
        /// 현재 메모리 사용량을 조회합니다
        /// </summary>
        long GetCurrentMemoryUsage();

        /// <summary>
        /// 메모리 압박 수준을 확인합니다
        /// </summary>
        MemoryPressureLevel GetMemoryPressureLevel();

        /// <summary>
        /// 실시간 메모리 사용량 모니터링 및 임계치 관리
        /// </summary>
        Task<MemoryMonitoringResult> MonitorMemoryWithThresholdsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 검수 작업 취소 시 안전한 리소스 정리 메커니즘
        /// </summary>
        Task<bool> SafelyCleanupOperationAsync(string operationId, TimeSpan timeout = default);

        /// <summary>
        /// 가비지 컬렉션 최적화 및 메모리 누수 방지
        /// </summary>
        Task<GcOptimizationResult> OptimizeGarbageCollectionAsync();

        /// <summary>
        /// 메모리 누수를 감지합니다
        /// </summary>
        Task<List<MemoryLeakSuspect>> DetectMemoryLeaksAsync();

        /// <summary>
        /// 리소스를 등록하여 관리합니다
        /// </summary>
        void RegisterManagedResource(string resourceId, IDisposable resource);

        /// <summary>
        /// 약한 참조를 등록합니다
        /// </summary>
        void RegisterWeakReference(string referenceId, object target);

        /// <summary>
        /// 작업 취소 토큰을 등록합니다
        /// </summary>
        CancellationToken RegisterOperationToken(string operationId);

        /// <summary>
        /// 고급 메모리 통계를 가져옵니다
        /// </summary>
        AdvancedMemoryStatistics GetAdvancedMemoryStatistics();
    }

    #region Data Models

    /// <summary>
    /// 메모리 압박 수준 열거형
    /// </summary>
    public enum MemoryPressureLevel
    {
        Normal,
        Warning,
        Critical,
        Emergency
    }

    /// <summary>
    /// 메모리 사용량 추세 열거형
    /// </summary>
    public enum MemoryTrend
    {
        DecreasingRapidly,
        DecreasingSteadily,
        Stable,
        IncreasingSteadily,
        IncreasingRapidly
    }

    /// <summary>
    /// 메모리 스냅샷
    /// </summary>
    public class MemorySnapshot
    {
        public long MemoryUsage { get; set; }
        public DateTime Timestamp { get; set; }
        public MemoryPressureLevel PressureLevel { get; set; }
    }

    /// <summary>
    /// 메모리 모니터링 결과
    /// </summary>
    public class MemoryMonitoringResult
    {
        public long CurrentMemoryUsage { get; set; }
        public long MaxMemoryLimit { get; set; }
        public double PressureRatio { get; set; }
        public MemoryPressureLevel PressureLevel { get; set; }
        public long PeakMemoryUsage { get; set; }
        public MemoryTrend MemoryTrend { get; set; }
        public List<string> RecommendedActions { get; set; } = new();
        public DateTime MonitoringTime { get; set; }
    }

    /// <summary>
    /// GC 최적화 결과
    /// </summary>
    public class GcOptimizationResult
    {
        public long BeforeMemoryUsage { get; set; }
        public long AfterMemoryUsage { get; set; }
        public long MemoryFreed { get; set; }
        public TimeSpan OptimizationDuration { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public DateTime OptimizationTime { get; set; }
    }

    /// <summary>
    /// 고급 메모리 통계
    /// </summary>
    public class AdvancedMemoryStatistics
    {
        public long CurrentMemoryUsage { get; set; }
        public long MaxMemoryLimit { get; set; }
        public long PeakMemoryUsage { get; set; }
        public double PressureRatio { get; set; }
        public MemoryPressureLevel PressureLevel { get; set; }
        public MemoryTrend MemoryTrend { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public int ForcedGcCount { get; set; }
        public DateTime LastGcTime { get; set; }
        public int ManagedResourceCount { get; set; }
        public int WeakReferenceCount { get; set; }
        public int ActiveOperationCount { get; set; }
        public long TotalAllocations { get; set; }
        public long TotalDeallocations { get; set; }
        public long MemoryLeakDetections { get; set; }
        public double GcEfficiency { get; set; }
    }

    /// <summary>
    /// 메모리 누수 의심 항목
    /// </summary>
    public class MemoryLeakSuspect
    {
        public MemoryLeakType SuspectType { get; set; }
        public string Description { get; set; } = string.Empty;
        public MemoryLeakSeverity Severity { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    /// <summary>
    /// 메모리 누수 타입 열거형
    /// </summary>
    public enum MemoryLeakType
    {
        MemoryTrendAnomaly,
        WeakReferenceCleanupIneffective,
        UnmanagedResourceAccumulation,
        LowGcEfficiency
    }

    /// <summary>
    /// 메모리 누수 심각도 열거형
    /// </summary>
    public enum MemoryLeakSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// 고급 메모리 압박 이벤트 인자
    /// </summary>
    public class AdvancedMemoryPressureEventArgs : EventArgs
    {
        public long CurrentMemoryUsage { get; set; }
        public long MaxMemoryLimit { get; set; }
        public double PressureRatio { get; set; }
        public MemoryPressureLevel PressureLevel { get; set; }
        public List<string> RecommendedActions { get; set; } = new();
        public MemoryTrend MemoryTrend { get; set; }
    }

    /// <summary>
    /// 메모리 누수 이벤트 인자
    /// </summary>
    public class MemoryLeakEventArgs : EventArgs
    {
        public List<MemoryLeakSuspect> Suspects { get; set; } = new();
        public long CurrentMemoryUsage { get; set; }
        public DateTime DetectionTime { get; set; }
    }

    /// <summary>
    /// 리소스 정리 이벤트 인자
    /// </summary>
    public class ResourceCleanupEventArgs : EventArgs
    {
        public string OperationId { get; set; } = string.Empty;
        public bool CleanupSuccess { get; set; }
        public int CleanedResourceCount { get; set; }
        public TimeSpan CleanupDuration { get; set; }
    }

    #endregion
}
