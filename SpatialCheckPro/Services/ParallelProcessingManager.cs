using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 병렬 처리 관리 서비스
    /// </summary>
    public class ParallelProcessingManager
    {
        private readonly ILogger<ParallelProcessingManager> _logger;
        private readonly CentralizedResourceMonitor _resourceMonitor;
        private readonly PerformanceSettings _settings;
        
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly Timer _resourceMonitoringTimer;
        
        private volatile int _currentParallelism;
        private volatile bool _isHighLoad = false;
        private DateTime _lastResourceCheck = DateTime.Now;

        public ParallelProcessingManager(
            ILogger<ParallelProcessingManager> logger,
            CentralizedResourceMonitor resourceMonitor,
            PerformanceSettings settings)
        {
            _logger = logger;
            _resourceMonitor = resourceMonitor;
            _settings = settings;
            
            _currentParallelism = _settings.MaxDegreeOfParallelism;
            
            // 리소스 모니터링 타이머 시작
            if (_settings.EnableDynamicParallelismAdjustment)
            {
                _resourceMonitoringTimer = new Timer(MonitorResources, null, 
                    TimeSpan.Zero, TimeSpan.FromSeconds(_settings.ResourceMonitoringIntervalSeconds));
            }
            
            _logger.LogInformation("병렬 처리 관리자 초기화: 최대 병렬도 {MaxParallelism}, 동적 조정 {DynamicAdjustment}", 
                _settings.MaxDegreeOfParallelism, _settings.EnableDynamicParallelismAdjustment);
        }

        /// <summary>
        /// 테이블별 병렬 처리 실행
        /// </summary>
        public async Task<List<T>> ExecuteTableParallelProcessingAsync<T>(
            List<object> items,
            Func<object, Task<T>> processor,
            IProgress<string>? progress = null,
            string operationName = "테이블 처리")
        {
            if (!_settings.EnableTableParallelProcessing)
            {
                _logger.LogInformation("테이블별 병렬 처리 비활성화 - 순차 처리로 실행");
                return await ExecuteSequentialProcessingAsync(items, processor, progress, operationName);
            }

            var results = new List<T>();
            var semaphore = GetSemaphore();
            var tasks = new List<Task<T>>();

            _logger.LogInformation("{OperationName} 병렬 처리 시작: {ItemCount}개 항목, 병렬도 {Parallelism}", 
                operationName, items.Count, _currentParallelism);

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var index = i;

                    var task = ProcessItemWithSemaphoreAsync(item, processor, semaphore, index, items.Count, progress, operationName);
                    tasks.Add(task);
                }

                // 모든 작업 완료 대기
                var completedTasks = await Task.WhenAll(tasks);
                results.AddRange(completedTasks);

                _logger.LogInformation("{OperationName} 병렬 처리 완료: {ResultCount}개 결과", 
                    operationName, results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{OperationName} 병렬 처리 중 오류 발생", operationName);
                throw;
            }
        }

        /// <summary>
        /// 단계별 병렬 처리 실행 (독립적인 단계들)
        /// </summary>
        public async Task<Dictionary<string, object>> ExecuteStageParallelProcessingAsync(
            Dictionary<string, Func<Task<object>>> stageProcessors,
            IProgress<string>? progress = null)
        {
            if (!_settings.EnableStageParallelProcessing)
            {
                _logger.LogInformation("단계별 병렬 처리 비활성화 - 순차 처리로 실행");
                return await ExecuteSequentialStageProcessingAsync(stageProcessors, progress);
            }

            var results = new Dictionary<string, object>();
            var tasks = new Dictionary<string, Task<object>>();

            _logger.LogInformation("단계별 병렬 처리 시작: {StageCount}개 단계", stageProcessors.Count);

            try
            {
                // 모든 단계를 병렬로 시작
                foreach (var stage in stageProcessors)
                {
                    var stageName = stage.Key;
                    var processor = stage.Value;
                    
                    progress?.Report($"단계 '{stageName}' 시작...");
                    
                    tasks[stageName] = processor();
                }

                // 모든 단계 완료 대기
                await Task.WhenAll(tasks.Values);

                // 결과 수집
                foreach (var task in tasks)
                {
                    results[task.Key] = await task.Value;
                    progress?.Report($"단계 '{task.Key}' 완료");
                }

                _logger.LogInformation("단계별 병렬 처리 완료: {ResultCount}개 단계", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "단계별 병렬 처리 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 세마포어를 사용한 개별 항목 처리
        /// </summary>
        private async Task<T> ProcessItemWithSemaphoreAsync<T>(
            object item,
            Func<object, Task<T>> processor,
            SemaphoreSlim semaphore,
            int index,
            int totalCount,
            IProgress<string>? progress,
            string operationName)
        {
            await semaphore.WaitAsync();
            
            try
            {
                progress?.Report($"{operationName} 중... ({index + 1}/{totalCount})");
                
                var result = await processor(item);
                
                // 메모리 압박 체크 및 GC 실행
                if (_settings.EnableAutomaticGarbageCollection && ShouldRunGarbageCollection())
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 순차 처리 실행
        /// </summary>
        private async Task<List<T>> ExecuteSequentialProcessingAsync<T>(
            List<object> items,
            Func<object, Task<T>> processor,
            IProgress<string>? progress,
            string operationName)
        {
            var results = new List<T>();
            
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                progress?.Report($"{operationName} 중... ({i + 1}/{items.Count})");
                
                var result = await processor(item);
                results.Add(result);
            }
            
            return results;
        }

        /// <summary>
        /// 순차 단계 처리 실행
        /// </summary>
        private async Task<Dictionary<string, object>> ExecuteSequentialStageProcessingAsync(
            Dictionary<string, Func<Task<object>>> stageProcessors,
            IProgress<string>? progress)
        {
            var results = new Dictionary<string, object>();
            
            foreach (var stage in stageProcessors)
            {
                progress?.Report($"단계 '{stage.Key}' 처리 중...");
                var result = await stage.Value();
                results[stage.Key] = result;
                progress?.Report($"단계 '{stage.Key}' 완료");
            }
            
            return results;
        }

        /// <summary>
        /// 세마포어 가져오기 또는 생성
        /// </summary>
        private SemaphoreSlim GetSemaphore()
        {
            var key = "default";
            return _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_currentParallelism, _currentParallelism));
        }

        /// <summary>
        /// 리소스 모니터링
        /// </summary>
        private void MonitorResources(object? state)
        {
            try
            {
                // 중앙집중식 모니터에서 리소스 정보 가져오기 (캐시 우선)
                var resourceInfo = _resourceMonitor.GetResourceInfoForRequester("ParallelProcessingManager");
                var wasHighLoad = _isHighLoad;
                
                // 시스템 부하 판단
                _isHighLoad = resourceInfo.SystemLoadLevel == SystemLoadLevel.High ||
                             GetCurrentMemoryUsageMB() > _settings.MemoryPressureThresholdMB;
                
                // 병렬도 동적 조정
                if (_settings.EnableAutomaticLoadBalancing)
                {
                    var newParallelism = CalculateOptimalParallelism(resourceInfo);
                    
                    if (newParallelism != _currentParallelism)
                    {
                        _currentParallelism = newParallelism;
                        _logger.LogInformation("병렬도 동적 조정: {OldParallelism} → {NewParallelism} (부하: {LoadLevel})", 
                            _currentParallelism, newParallelism, resourceInfo.SystemLoadLevel);
                        
                        // 세마포어 업데이트
                        UpdateSemaphoreCapacity();
                    }
                }
                
                _lastResourceCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "리소스 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 최적 병렬도 계산 (Phase 2 Item #14: 동적 병렬도 조정 개선)
        /// - CPU 사용률 기반 점진적 조정
        /// - 메모리 압박 기반 제약
        /// - 기존 보수적 알고리즘 개선 (절반 감소 → 2씩 감소, 1씩 증가 → 2씩 증가)
        /// </summary>
        private int CalculateOptimalParallelism(SystemResourceInfo resourceInfo)
        {
            var cpuUsage = resourceInfo.CpuUsagePercent;
            var memoryPressure = resourceInfo.MemoryPressureRatio;

            // CPU 기반 목표 병렬도 계산
            int cpuBasedTarget;
            if (cpuUsage > 90)
            {
                // CPU 매우 높음 - 병렬도 2씩 감소
                cpuBasedTarget = Math.Max(1, _currentParallelism - 2);
                _logger.LogDebug("CPU 사용률 매우 높음 ({CpuUsage}%) - 병렬도 감소: {Current} -> {Target}",
                    cpuUsage, _currentParallelism, cpuBasedTarget);
            }
            else if (cpuUsage > 70)
            {
                // CPU 높음 - 현재 유지
                cpuBasedTarget = _currentParallelism;
            }
            else if (cpuUsage < 50)
            {
                // CPU 낮음 - 병렬도 2씩 증가
                cpuBasedTarget = Math.Min(_settings.MaxDegreeOfParallelismLimit,
                                         _currentParallelism + 2);
                _logger.LogDebug("CPU 사용률 낮음 ({CpuUsage}%) - 병렬도 증가: {Current} -> {Target}",
                    cpuUsage, _currentParallelism, cpuBasedTarget);
            }
            else
            {
                // CPU 적정 - 현재 유지
                cpuBasedTarget = _currentParallelism;
            }

            // 메모리 기반 제약 계산
            int memoryBasedMax;
            if (memoryPressure > 0.9)
            {
                // 메모리 압박 매우 높음 - 병렬도 절반으로 감소
                memoryBasedMax = Math.Max(1, _currentParallelism / 2);
                _logger.LogWarning("메모리 압박 매우 높음 ({MemoryPressure:P1}) - 병렬도 긴급 감소: {Current} -> {Max}",
                    memoryPressure, _currentParallelism, memoryBasedMax);
            }
            else if (memoryPressure > 0.8)
            {
                // 메모리 압박 높음 - 현재 유지 (증가 금지)
                memoryBasedMax = _currentParallelism;
                _logger.LogWarning("메모리 압박 높음 ({MemoryPressure:P1}) - 병렬도 증가 금지",
                    memoryPressure);
            }
            else
            {
                // 메모리 여유 - 제약 없음
                memoryBasedMax = _settings.MaxDegreeOfParallelismLimit;
            }

            // 최종 병렬도: CPU 목표와 메모리 제약 중 작은 값
            var targetParallelism = Math.Min(cpuBasedTarget, memoryBasedMax);

            // 최소/최대 범위 제약
            targetParallelism = Math.Clamp(targetParallelism,
                _settings.MinDegreeOfParallelism,
                _settings.MaxDegreeOfParallelismLimit);

            if (targetParallelism != _currentParallelism)
            {
                _logger.LogInformation(
                    "병렬도 조정: {Current} -> {Target} (CPU: {CpuUsage:F1}%, 메모리 압박: {MemoryPressure:P1}, 추천: {Recommended})",
                    _currentParallelism,
                    targetParallelism,
                    cpuUsage,
                    memoryPressure,
                    resourceInfo.RecommendedMaxParallelism);
            }

            return targetParallelism;
        }

        /// <summary>
        /// 세마포어 용량 업데이트
        /// </summary>
        private void UpdateSemaphoreCapacity()
        {
            foreach (var semaphore in _semaphores.Values)
            {
                // 기존 세마포어 해제
                semaphore.Dispose();
            }
            _semaphores.Clear();
        }

        /// <summary>
        /// 현재 메모리 사용량 (MB)
        /// </summary>
        private long GetCurrentMemoryUsageMB()
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
        /// 가비지 컬렉션 실행 여부 판단
        /// </summary>
        private bool ShouldRunGarbageCollection()
        {
            var currentMemory = GetCurrentMemoryUsageMB();
            return currentMemory > _settings.MemoryPressureThresholdMB;
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _resourceMonitoringTimer?.Dispose();
            
            foreach (var semaphore in _semaphores.Values)
            {
                semaphore.Dispose();
            }
            _semaphores.Clear();
            
            foreach (var cts in _cancellationTokens.Values)
            {
                cts.Dispose();
            }
            _cancellationTokens.Clear();
        }
    }
}
