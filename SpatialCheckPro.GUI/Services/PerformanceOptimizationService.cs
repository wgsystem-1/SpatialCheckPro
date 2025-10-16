#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.Models;


namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 대용량 데이터 처리 최적화를 위한 서비스
    /// Requirements: 6.1, 6.4 - 성능 및 사용성
    /// </summary>
    public class PerformanceOptimizationService
    {
        private readonly ILogger<PerformanceOptimizationService> _logger;
        
        // 뷰포트 기반 필터링 설정
        private double _viewportMinX, _viewportMinY, _viewportMaxX, _viewportMaxY;
        private bool _viewportFilteringEnabled = true;
        
        // 백그라운드 로딩 관리
        private readonly CancellationTokenSource _backgroundCancellationTokenSource = new();
        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);
        
        // 프로그레시브 렌더링 설정
        private readonly int _progressiveRenderingBatchSize = 500;
        private readonly int _maxRenderingItems = 10000;
        
        // 성능 통계
        private readonly List<long> _loadingTimes = new();
        private readonly List<int> _processedCounts = new();
        
        // 메모리 관리
        private long _maxMemoryUsage = 500 * 1024 * 1024; // 500MB
        private readonly Timer _memoryMonitorTimer;

        public PerformanceOptimizationService(ILogger<PerformanceOptimizationService> logger)
        {
            _logger = logger;
            
            // 메모리 모니터링 타이머 설정 (30초마다)
            _memoryMonitorTimer = new Timer(MonitorMemoryUsage, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        #region 뷰포트 기반 필터링

        /// <summary>
        /// 뷰포트 영역을 설정합니다
        /// Requirements: 6.1 - 10,000개 이상의 오류가 있을 때 3초 이내 렌더링
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        public void SetViewport(double minX, double minY, double maxX, double maxY)
        {
            _viewportMinX = minX;
            _viewportMinY = minY;
            _viewportMaxX = maxX;
            _viewportMaxY = maxY;
            
            _logger.LogDebug("뷰포트 설정: ({MinX}, {MinY}) - ({MaxX}, {MaxY})", 
                minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 뷰포트 기반 필터링을 활성화/비활성화합니다
        /// </summary>
        /// <param name="enabled">활성화 여부</param>
        public void SetViewportFiltering(bool enabled)
        {
            _viewportFilteringEnabled = enabled;
            _logger.LogDebug("뷰포트 필터링: {Enabled}", enabled);
        }

        /// <summary>
        /// 뷰포트 내의 오류들만 필터링합니다
        /// </summary>
        /// <param name="errors">전체 오류 목록</param>
        /// <returns>뷰포트 내 오류 목록</returns>
        public List<ErrorFeature> FilterErrorsInViewport(List<ErrorFeature> errors)
        {
            if (!_viewportFilteringEnabled)
                return errors;

            try
            {
                var startTime = DateTime.UtcNow;
                
                var filteredErrors = errors.AsParallel()
                    .Where(error => IsInViewport(error.QcError.X, error.QcError.Y))
                    .ToList();

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("뷰포트 필터링 완료: {Original}개 -> {Filtered}개, 소요시간: {ElapsedMs}ms", 
                    errors.Count, filteredErrors.Count, elapsedMs);

                return filteredErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "뷰포트 필터링 실패");
                return errors;
            }
        }

        /// <summary>
        /// 좌표가 뷰포트 내에 있는지 확인합니다
        /// </summary>
        private bool IsInViewport(double x, double y)
        {
            return x >= _viewportMinX && x <= _viewportMaxX && 
                   y >= _viewportMinY && y <= _viewportMaxY;
        }

        #endregion

        #region 백그라운드 데이터 로딩

        /// <summary>
        /// 백그라운드에서 오류 데이터를 로드합니다
        /// Requirements: 6.5 - 네트워크 지연 시 로딩 인디케이터 표시
        /// </summary>
        /// <param name="dataLoader">데이터 로더 함수</param>
        /// <param name="progressCallback">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>로드된 오류 목록</returns>
        public async Task<List<ErrorFeature>> LoadDataInBackgroundAsync(
            Func<CancellationToken, Task<List<ErrorFeature>>> dataLoader,
            Action<int, string>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            await _loadingSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                var startTime = DateTime.UtcNow;
                progressCallback?.Invoke(0, "데이터 로딩 시작...");
                
                _logger.LogInformation("백그라운드 데이터 로딩 시작");

                // 백그라운드 스레드에서 데이터 로드
                var errors = await Task.Run(async () =>
                {
                    return await dataLoader(cancellationToken);
                }, cancellationToken);

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _loadingTimes.Add((long)elapsedMs);
                _processedCounts.Add(errors.Count);

                progressCallback?.Invoke(100, $"로딩 완료: {errors.Count}개 오류");
                
                _logger.LogInformation("백그라운드 데이터 로딩 완료: {Count}개, 소요시간: {ElapsedMs}ms", 
                    errors.Count, elapsedMs);

                return errors;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("백그라운드 데이터 로딩 취소됨");
                progressCallback?.Invoke(0, "로딩 취소됨");
                return new List<ErrorFeature>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "백그라운드 데이터 로딩 실패");
                progressCallback?.Invoke(0, "로딩 실패");
                return new List<ErrorFeature>();
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        /// <summary>
        /// 백그라운드 로딩을 취소합니다
        /// </summary>
        public void CancelBackgroundLoading()
        {
            _backgroundCancellationTokenSource.Cancel();
            _logger.LogInformation("백그라운드 로딩 취소 요청됨");
        }

        #endregion

        #region 프로그레시브 렌더링

        /// <summary>
        /// 프로그레시브 렌더링을 위해 오류 목록을 배치로 분할합니다
        /// Requirements: 6.2 - 지도 확대/축소 시 1초 이내 업데이트
        /// </summary>
        /// <param name="errors">전체 오류 목록</param>
        /// <param name="batchSize">배치 크기</param>
        /// <returns>배치별로 분할된 오류 목록</returns>
        public List<List<ErrorFeature>> CreateProgressiveRenderingBatches(
            List<ErrorFeature> errors, 
            int? batchSize = null)
        {
            try
            {
                var actualBatchSize = batchSize ?? _progressiveRenderingBatchSize;
                var batches = new List<List<ErrorFeature>>();
                
                // 최대 렌더링 개수 제한
                var limitedErrors = errors.Take(_maxRenderingItems).ToList();
                
                // 우선순위별 정렬 (심각도 > 상태 > 거리)
                var sortedErrors = limitedErrors
                    .OrderBy(e => GetSeverityPriority(e.QcError.Severity))
                    .ThenBy(e => GetStatusPriority(e.QcError.Status))
                    .ToList();

                for (int i = 0; i < sortedErrors.Count; i += actualBatchSize)
                {
                    var batch = sortedErrors.Skip(i).Take(actualBatchSize).ToList();
                    batches.Add(batch);
                }

                _logger.LogDebug("프로그레시브 렌더링 배치 생성: {TotalErrors}개 -> {BatchCount}개 배치", 
                    limitedErrors.Count, batches.Count);

                return batches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "프로그레시브 렌더링 배치 생성 실패");
                return new List<List<ErrorFeature>> { errors };
            }
        }

        /// <summary>
        /// 프로그레시브 렌더링을 실행합니다
        /// </summary>
        /// <param name="batches">렌더링 배치 목록</param>
        /// <param name="renderCallback">렌더링 콜백</param>
        /// <param name="progressCallback">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        public async Task ExecuteProgressiveRenderingAsync(
            List<List<ErrorFeature>> batches,
            Func<List<ErrorFeature>, Task> renderCallback,
            Action<int, string>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var totalBatches = batches.Count;
                var processedBatches = 0;

                _logger.LogInformation("프로그레시브 렌더링 시작: {BatchCount}개 배치", totalBatches);

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 배치 렌더링
                    await renderCallback(batch);
                    processedBatches++;

                    // 진행률 업데이트
                    var progress = (int)((double)processedBatches / totalBatches * 100);
                    progressCallback?.Invoke(progress, 
                        $"렌더링 중... {processedBatches}/{totalBatches} 배치");

                    // UI 응답성을 위한 짧은 지연
                    await Task.Delay(10, cancellationToken);
                }

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("프로그레시브 렌더링 완료: {BatchCount}개 배치, 소요시간: {ElapsedMs}ms", 
                    totalBatches, elapsedMs);

                progressCallback?.Invoke(100, "렌더링 완료");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("프로그레시브 렌더링 취소됨");
                progressCallback?.Invoke(0, "렌더링 취소됨");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "프로그레시브 렌더링 실패");
                progressCallback?.Invoke(0, "렌더링 실패");
            }
        }

        #endregion

        #region 메모리 관리

        /// <summary>
        /// 메모리 사용량을 모니터링합니다
        /// Requirements: 6.4 - 메모리 사용량 500MB 초과 방지
        /// </summary>
        private void MonitorMemoryUsage(object? state)
        {
            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                
                if (currentMemory > _maxMemoryUsage)
                {
                    _logger.LogWarning("메모리 사용량 초과: {CurrentMB}MB / {MaxMB}MB", 
                        currentMemory / 1024 / 1024, _maxMemoryUsage / 1024 / 1024);
                    
                    // 강제 가비지 컬렉션
                    OptimizeMemoryUsage();
                }
                else
                {
                    _logger.LogDebug("메모리 사용량: {CurrentMB}MB / {MaxMB}MB", 
                        currentMemory / 1024 / 1024, _maxMemoryUsage / 1024 / 1024);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 모니터링 실패");
            }
        }

        /// <summary>
        /// 메모리 사용량을 최적화합니다
        /// </summary>
        public void OptimizeMemoryUsage()
        {
            try
            {
                var beforeMemory = GC.GetTotalMemory(false);
                
                // 가비지 컬렉션 실행
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var afterMemory = GC.GetTotalMemory(false);
                var freedMemory = beforeMemory - afterMemory;
                
                _logger.LogInformation("메모리 최적화 완료: {FreedMB}MB 해제됨", 
                    freedMemory / 1024 / 1024);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 최적화 실패");
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

        #endregion

        #region 성능 통계

        /// <summary>
        /// 성능 통계를 가져옵니다
        /// </summary>
        /// <returns>성능 통계 정보</returns>
        public PerformanceStatistics GetPerformanceStatistics()
        {
            return new PerformanceStatistics
            {
                AverageLoadingTimeMs = _loadingTimes.Any() ? _loadingTimes.Average() : 0,
                LastLoadingTimeMs = _loadingTimes.LastOrDefault(),
                AverageProcessedCount = _processedCounts.Any() ? (int)_processedCounts.Average() : 0,
                TotalLoadingOperations = _loadingTimes.Count,
                CurrentMemoryUsageMB = GC.GetTotalMemory(false) / 1024 / 1024,
                MaxMemoryUsageMB = _maxMemoryUsage / 1024 / 1024,
                ViewportFilteringEnabled = _viewportFilteringEnabled,
                ProgressiveRenderingBatchSize = _progressiveRenderingBatchSize,
                MaxRenderingItems = _maxRenderingItems
            };
        }

        /// <summary>
        /// 성능 통계를 초기화합니다
        /// </summary>
        public void ResetPerformanceStatistics()
        {
            _loadingTimes.Clear();
            _processedCounts.Clear();
            _logger.LogDebug("성능 통계 초기화됨");
        }

        #endregion

        #region 내부 헬퍼 메서드

        /// <summary>
        /// 심각도 우선순위를 가져옵니다
        /// </summary>
        private int GetSeverityPriority(string severity)
        {
            return severity switch
            {
                "CRIT" => 1,
                "MAJOR" => 2,
                "MINOR" => 3,
                "INFO" => 4,
                _ => 5
            };
        }

        /// <summary>
        /// 상태 우선순위를 가져옵니다
        /// </summary>
        private int GetStatusPriority(string status)
        {
            return status switch
            {
                "OPEN" => 1,
                "FALSE_POS" => 2,
                "IGNORED" => 3,
                "FIXED" => 4,
                _ => 5
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _backgroundCancellationTokenSource?.Dispose();
            _loadingSemaphore?.Dispose();
            _memoryMonitorTimer?.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// 성능 통계 정보
    /// </summary>
    public class PerformanceStatistics
    {
        public double AverageLoadingTimeMs { get; set; }
        public long LastLoadingTimeMs { get; set; }
        public int AverageProcessedCount { get; set; }
        public int TotalLoadingOperations { get; set; }
        public long CurrentMemoryUsageMB { get; set; }
        public long MaxMemoryUsageMB { get; set; }
        public bool ViewportFilteringEnabled { get; set; }
        public int ProgressiveRenderingBatchSize { get; set; }
        public int MaxRenderingItems { get; set; }
    }
}