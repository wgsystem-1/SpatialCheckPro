using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 고급 병렬 처리 관리자 - 하이브리드 병렬처리 지원
    /// </summary>
    public class AdvancedParallelProcessingManager
    {
        private readonly ILogger<AdvancedParallelProcessingManager> _logger;
        private readonly CentralizedResourceMonitor _resourceMonitor;
        private readonly PerformanceSettings _settings;
        private readonly MemoryOptimizationService _memoryOptimizationService;
        private readonly ParallelPerformanceMonitor? _performanceMonitor;
        
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        
        private volatile int _currentParallelism;
        private volatile bool _isHighLoad = false;
        private DateTime _lastResourceCheck = DateTime.Now;
        
        // 병렬처리 레벨별 설정
        private readonly Dictionary<ParallelProcessingLevel, ParallelProcessingConfig> _levelConfigs = new();
        private readonly Dictionary<ProcessingType, (double cpuFactor, int maxLimit)> _typeConfigs = new();

        public AdvancedParallelProcessingManager(
            ILogger<AdvancedParallelProcessingManager> logger,
            CentralizedResourceMonitor resourceMonitor,
            PerformanceSettings settings,
            MemoryOptimizationService memoryOptimizationService,
            ParallelPerformanceMonitor? performanceMonitor = null)
        {
            _logger = logger;
            _resourceMonitor = resourceMonitor;
            _settings = settings;
            _memoryOptimizationService = memoryOptimizationService;
            _performanceMonitor = performanceMonitor;
            
            _currentParallelism = _settings.MaxDegreeOfParallelism;
            
            // 병렬처리 레벨별 설정 초기화
            InitializeLevelConfigs();

            // 작업 유형별 설정 초기화
            InitializeTypeConfigs();
            
            // 리소스 모니터링 타이머 시작 -> 교착 상태 유발 가능성으로 인해 비활성화
            /*
            if (_settings.EnableDynamicParallelismAdjustment)
            {
                _resourceMonitoringTimer = new Timer(MonitorResources, null, 
                    TimeSpan.Zero, TimeSpan.FromSeconds(_settings.ResourceMonitoringIntervalSeconds));
            }
            */
            
            _logger.LogInformation("고급 병렬 처리 관리자 초기화: 최대 병렬도 {MaxParallelism}, 동적 조정 비활성화됨", 
                _settings.MaxDegreeOfParallelism);
        }

        /// <summary>
        /// 작업 유형별 병렬처리 설정 초기화
        /// </summary>
        private void InitializeTypeConfigs()
        {
            // I/O 작업은 CPU 코어의 25%만 사용하되 최대 4개를 넘지 않도록 제한
            _typeConfigs[ProcessingType.IOBound] = (cpuFactor: 0.25, maxLimit: 4);
            
            // CPU 작업은 CPU 코어의 75%를 사용
            _typeConfigs[ProcessingType.CPUBound] = (cpuFactor: 0.75, maxLimit: Environment.ProcessorCount);
        }

        /// <summary>
        /// 병렬처리 레벨별 설정 초기화
        /// </summary>
        private void InitializeLevelConfigs()
        {
            var resourceInfo = _resourceMonitor.GetResourceInfo("AdvancedParallelProcessingManager_Init");
            
            // 파일별 병렬처리 (최상위 레벨)
            _levelConfigs[ParallelProcessingLevel.File] = new ParallelProcessingConfig
            {
                MaxParallelism = Math.Max(1, Math.Min(2, resourceInfo.ProcessorCount / 4)), // 파일당 리소스 집약적
                BatchSize = 1, // 파일은 하나씩 처리
                EnableMemoryOptimization = true,
                EnableResourceMonitoring = true
            };
            
            // 단계별 병렬처리 (독립적인 단계들)
            _levelConfigs[ParallelProcessingLevel.Stage] = new ParallelProcessingConfig
            {
                MaxParallelism = Math.Max(1, Math.Min(3, resourceInfo.ProcessorCount / 3)), // 단계별 병렬처리
                BatchSize = 1, // 단계는 하나씩 처리
                EnableMemoryOptimization = true,
                EnableResourceMonitoring = true
            };
            
            // 테이블별 병렬처리 (중간 레벨)
            _levelConfigs[ParallelProcessingLevel.Table] = new ParallelProcessingConfig
            {
                MaxParallelism = Math.Max(1, Math.Min(resourceInfo.RecommendedMaxParallelism, resourceInfo.ProcessorCount)),
                BatchSize = resourceInfo.RecommendedBatchSize,
                EnableMemoryOptimization = true,
                EnableResourceMonitoring = true
            };
            
            // 규칙별 병렬처리 (최하위 레벨)
            _levelConfigs[ParallelProcessingLevel.Rule] = new ParallelProcessingConfig
            {
                MaxParallelism = Math.Max(1, Math.Min(resourceInfo.RecommendedMaxParallelism * 2, resourceInfo.ProcessorCount * 2)),
                BatchSize = Math.Max(100, resourceInfo.RecommendedBatchSize / 2),
                EnableMemoryOptimization = true,
                EnableResourceMonitoring = false // 규칙별은 모니터링 비활성화
            };
        }

        /// <summary>
        /// 파일별 병렬 처리 실행 (배치 검수용)
        /// </summary>
        public async Task<List<T>> ExecuteFileParallelProcessingAsync<T>(
            List<object> files,
            Func<object, Task<T>> fileProcessor,
            IProgress<string>? progress = null,
            string operationName = "파일 처리")
        {
            var config = _levelConfigs[ParallelProcessingLevel.File];
            
            if (!_settings.EnableStageParallelProcessing) // 파일별은 단계별 설정 사용
            {
                _logger.LogInformation("파일별 병렬 처리 비활성화 - 순차 처리로 실행");
                return await ExecuteSequentialProcessingAsync(files, fileProcessor, progress, operationName);
            }

            var results = new List<T>();
            var semaphore = GetSemaphore(ParallelProcessingLevel.File);
            var tasks = new List<Task<T>>();

            _logger.LogInformation("{OperationName} 파일별 병렬 처리 시작: {FileCount}개 파일, 병렬도 {Parallelism}", 
                operationName, files.Count, config.MaxParallelism);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    var index = i;

                    var task = ProcessItemWithSemaphoreAsync(file, fileProcessor, semaphore, index, files.Count, progress, operationName);
                    tasks.Add(task);
                }

                // 모든 작업 완료 대기
                var completedTasks = await Task.WhenAll(tasks);
                results.AddRange(completedTasks);

                _logger.LogInformation("{OperationName} 파일별 병렬 처리 완료: {ResultCount}개 결과", 
                    operationName, results.Count);

                return results;
            }
            catch (Exception)
            {
                _logger.LogError("{OperationName} 파일별 병렬 처리 중 오류 발생", operationName);
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
            var config = _levelConfigs[ParallelProcessingLevel.Stage];
            
            if (!_settings.EnableStageParallelProcessing)
            {
                _logger.LogInformation("단계별 병렬 처리 비활성화 - 순차 처리로 실행");
                return await ExecuteSequentialStageProcessingAsync(stageProcessors, progress);
            }

            var results = new Dictionary<string, object>();
            var tasks = new Dictionary<string, Task<object>>();

            _logger.LogInformation("단계별 병렬 처리 시작: {StageCount}개 단계, 병렬도 {Parallelism}", 
                stageProcessors.Count, config.MaxParallelism);

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
            catch (Exception)
            {
                _logger.LogError("단계별 병렬 처리 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 테이블별 병렬 처리 실행
        /// </summary>
        public async Task<List<T>> ExecuteTableParallelProcessingAsync<T>(
            List<object> items,
            Func<object, Task<T>> processor,
            ProcessingType processingType = ProcessingType.CPUBound, // 기본값은 CPUBound
            IProgress<string>? progress = null,
            string operationName = "테이블 처리")
        {
            var operationId = $"{operationName}_{Guid.NewGuid():N}";
            
            // 성능 모니터링 시작
            _performanceMonitor?.StartOperation(operationId, operationName, items.Count);
            
            try
            {
                var config = _levelConfigs[ParallelProcessingLevel.Table];
                
                if (!_settings.EnableTableParallelProcessing)
                {
                    _logger.LogInformation("테이블별 병렬 처리 비활성화 - 순차 처리로 실행");
                    var result = await ExecuteSequentialProcessingAsync(items, processor, progress, operationName);
                    _performanceMonitor?.CompleteOperation(operationId, true);
                    return result;
                }

                var results = new List<T>();
                var semaphore = GetSemaphore(ParallelProcessingLevel.Table, processingType);
                var tasks = new List<Task<T>>();

                _logger.LogInformation("{OperationName} 테이블별 병렬 처리 시작: {ItemCount}개 항목, 병렬도 {Parallelism}, 타입 {ProcessingType}", 
                    operationName, items.Count, semaphore.CurrentCount, processingType);

                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        var index = i;

                        var task = ProcessItemWithSemaphoreAsync(item, processor, semaphore, index, items.Count, progress, operationName, operationId);
                        tasks.Add(task);
                    }

                    // 모든 작업 완료 대기
                    var completedTasks = await Task.WhenAll(tasks);
                    results.AddRange(completedTasks);

                    _logger.LogInformation("{OperationName} 테이블별 병렬 처리 완료: {ResultCount}개 결과", 
                        operationName, results.Count);

                    _performanceMonitor?.CompleteOperation(operationId, true);
                    return results;
                }
                catch (Exception)
                {
                    _logger.LogError("{OperationName} 테이블별 병렬 처리 중 오류 발생", operationName);
                    _performanceMonitor?.CompleteOperation(operationId, false);
                    throw;
                }
            }
            catch (Exception)
            {
                _performanceMonitor?.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// 규칙별 병렬 처리 실행
        /// </summary>
        public async Task<List<T>> ExecuteRuleParallelProcessingAsync<T>(
            List<object> rules,
            Func<object, Task<T>> ruleProcessor,
            IProgress<string>? progress = null,
            string operationName = "규칙 처리")
        {
            var operationId = $"{operationName}_{Guid.NewGuid():N}";
            
            // 성능 모니터링 시작
            _performanceMonitor?.StartOperation(operationId, operationName, rules.Count);
            
            try
            {
                var config = _levelConfigs[ParallelProcessingLevel.Rule];
                
                if (!_settings.EnableTableParallelProcessing) // 규칙별은 테이블별 설정 사용
                {
                    _logger.LogInformation("규칙별 병렬 처리 비활성화 - 순차 처리로 실행");
                    var result = await ExecuteSequentialProcessingAsync(rules, ruleProcessor, progress, operationName);
                    _performanceMonitor?.CompleteOperation(operationId, true);
                    return result;
                }

                var results = new List<T>();
                var semaphore = GetSemaphore(ParallelProcessingLevel.Rule);
                var tasks = new List<Task<T>>();

                _logger.LogInformation("{OperationName} 규칙별 병렬 처리 시작: {RuleCount}개 규칙, 병렬도 {Parallelism}", 
                    operationName, rules.Count, config.MaxParallelism);

                try
                {
                    for (int i = 0; i < rules.Count; i++)
                    {
                        var rule = rules[i];
                        var index = i;

                        var task = ProcessItemWithSemaphoreAsync(rule, ruleProcessor, semaphore, index, rules.Count, progress, operationName, operationId);
                        tasks.Add(task);
                    }

                    // 모든 작업 완료 대기
                    var completedTasks = await Task.WhenAll(tasks);
                    results.AddRange(completedTasks);

                    _logger.LogInformation("{OperationName} 규칙별 병렬 처리 완료: {ResultCount}개 결과", 
                        operationName, results.Count);

                    _performanceMonitor?.CompleteOperation(operationId, true);
                    return results;
                }
                catch (Exception)
                {
                    _logger.LogError("{OperationName} 규칙별 병렬 처리 중 오류 발생", operationName);
                    _performanceMonitor?.CompleteOperation(operationId, false);
                    throw;
                }
            }
            catch (Exception)
            {
                _performanceMonitor?.CompleteOperation(operationId, false);
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
            string operationName,
            string? operationId = null)
        {
            await semaphore.WaitAsync();
            
            try
            {
                progress?.Report($"{operationName} 중... ({index + 1}/{totalCount})");
                
                var result = await processor(item);
                
                // 성능 모니터링 업데이트 (상위에서 전달된 동일 operationId 사용)
                if (operationId != null)
                {
                    _performanceMonitor?.UpdateProgress(operationId, index + 1, totalCount);
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
        /// 레벨별 세마포어 가져오기 또는 생성
        /// </summary>
        private SemaphoreSlim GetSemaphore(ParallelProcessingLevel level, ProcessingType processingType = ProcessingType.CPUBound)
        {
            var config = _levelConfigs[level];
            var typeConfig = _typeConfigs[processingType];

            // 작업 유형을 고려하여 실제 병렬도 계산
            var resourceInfo = _resourceMonitor.GetResourceInfo("GetSemaphore");
            var calculatedParallelism = (int)Math.Max(1, Math.Min(resourceInfo.ProcessorCount * typeConfig.cpuFactor, typeConfig.maxLimit));
            var finalParallelism = Math.Min(config.MaxParallelism, calculatedParallelism);
            
            var key = $"{level}_{processingType}";
            return _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(finalParallelism, finalParallelism));
        }

        /// <summary>
        /// 리소스 모니터링
        /// </summary>
        private void MonitorResources(object? state)
        {
            try
            {
                // 중앙집중식 모니터에서 리소스 정보 가져오기 (캐시 우선)
                var resourceInfo = _resourceMonitor.GetResourceInfoForRequester("AdvancedParallelProcessingManager");
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
            catch (Exception)
            {
                _logger.LogError("리소스 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 최적 병렬도 계산
        /// </summary>
        private int CalculateOptimalParallelism(SystemResourceInfo resourceInfo)
        {
            if (_isHighLoad)
            {
                // 고부하 시 병렬도 감소
                return Math.Max(_settings.MinDegreeOfParallelism, _currentParallelism / 2);
            }
            else
            {
                // 저부하 시 병렬도 증가
                var maxParallelism = Math.Min(_settings.MaxDegreeOfParallelismLimit, resourceInfo.RecommendedMaxParallelism);
                return Math.Min(maxParallelism, _currentParallelism + 1);
            }
        }

        /// <summary>
        /// 세마포어 용량 업데이트
        /// </summary>
        private void UpdateSemaphoreCapacity()
        {
            _semaphores.Clear();
            // 변경된 병렬도 설정을 적용하기 위해 유형별 설정도 다시 초기화
            InitializeTypeConfigs();
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
        /// 파일별 병렬 처리 실행 (배치 검수용)
        /// </summary>
        public async Task<List<TResult>> ExecuteFileParallelProcessingAsync<TItem, TResult>(
            List<TItem> files,
            Func<TItem, Task<TResult>> processor,
            IProgress<ProcessingProgress>? progress = null,
            string operationName = "파일 처리")
        {
            var operationId = $"{operationName}_{Guid.NewGuid():N}";
            
            // 성능 모니터링 시작
            _performanceMonitor?.StartOperation(operationId, operationName, files.Count);
            
            try
            {
                if (!_settings.EnableFileParallelProcessing)
                {
                    _logger.LogInformation("파일별 병렬 처리가 비활성화되어 순차 처리로 실행: {FileCount}개 파일", files.Count);
                    var result = await ExecuteSequentialProcessing(files, processor, progress, operationName);
                    _performanceMonitor?.CompleteOperation(operationId, true);
                    return result;
                }

                var config = _levelConfigs[ParallelProcessingLevel.File];
                var maxParallelism = Math.Min(config.MaxParallelism, _currentParallelism);
                
                _logger.LogInformation("파일별 병렬 처리 시작: {FileCount}개 파일, 최대 병렬도: {MaxParallelism}", 
                    files.Count, maxParallelism);

                var results = new List<TResult>();
                var semaphore = GetOrCreateSemaphore(operationName, maxParallelism);
                var cancellationToken = GetOrCreateCancellationToken(operationName);
                
                try
                {
                    var tasks = files.Select(async (file, index) =>
                    {
                        await semaphore.WaitAsync(cancellationToken.Token);
                        try
                        {
                            var result = await processor(file);
                            
                            // 진행률 업데이트
                            var processedCount = Interlocked.Increment(ref _processedItems);
                            progress?.Report(new ProcessingProgress
                            {
                                ProcessedItems = processedCount,
                                TotalItems = files.Count,
                                CurrentItem = $"파일 {index + 1}/{files.Count}",
                                OperationName = operationName
                            });
                            
                            // 성능 모니터링 업데이트
                            _performanceMonitor?.UpdateProgress(operationId, processedCount, files.Count);
                            
                            return result;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    results = (await Task.WhenAll(tasks)).ToList();
                    
                    _logger.LogInformation("파일별 병렬 처리 완료: {FileCount}개 파일 처리됨", files.Count);
                    _performanceMonitor?.CompleteOperation(operationId, true);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("파일별 병렬 처리 취소됨");
                    _performanceMonitor?.CompleteOperation(operationId, false);
                    throw;
                }
                catch (Exception)
                {
                    _logger.LogError("파일별 병렬 처리 중 오류 발생");
                    _performanceMonitor?.CompleteOperation(operationId, false);
                    throw;
                }
                finally
                {
                    CleanupSemaphore(operationName);
                    CleanupCancellationToken(operationName);
                }

                return results;
            }
            catch (Exception)
            {
                _performanceMonitor?.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// 순차 처리 실행 (병렬 처리 비활성화 시 사용)
        /// </summary>
        private async Task<List<TResult>> ExecuteSequentialProcessing<TItem, TResult>(
            List<TItem> items,
            Func<TItem, Task<TResult>> processor,
            IProgress<ProcessingProgress>? progress = null,
            string operationName = "순차 처리")
        {
            var results = new List<TResult>();
            
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var result = await processor(item);
                results.Add(result);
                
                // 진행률 업데이트
                progress?.Report(new ProcessingProgress
                {
                    ProcessedItems = i + 1,
                    TotalItems = items.Count,
                    CurrentItem = $"항목 {i + 1}/{items.Count}",
                    OperationName = operationName
                });
            }
            
            return results;
        }

        /// <summary>
        /// 세마포어 가져오기 또는 생성
        /// </summary>
        private SemaphoreSlim GetOrCreateSemaphore(string operationName, int maxParallelism)
        {
            return _semaphores.GetOrAdd(operationName, _ => new SemaphoreSlim(maxParallelism, maxParallelism));
        }

        /// <summary>
        /// 취소 토큰 가져오기 또는 생성
        /// </summary>
        private CancellationTokenSource GetOrCreateCancellationToken(string operationName)
        {
            return _cancellationTokens.GetOrAdd(operationName, _ => new CancellationTokenSource());
        }

        /// <summary>
        /// 세마포어 정리
        /// </summary>
        private void CleanupSemaphore(string operationName)
        {
            if (_semaphores.TryRemove(operationName, out var semaphore))
            {
                semaphore.Dispose();
            }
        }

        /// <summary>
        /// 취소 토큰 정리
        /// </summary>
        private void CleanupCancellationToken(string operationName)
        {
            if (_cancellationTokens.TryRemove(operationName, out var cts))
            {
                cts.Dispose();
            }
        }

        private volatile int _processedItems = 0;

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
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

    /// <summary>
    /// 병렬처리 레벨
    /// </summary>
    public enum ParallelProcessingLevel
    {
        File,   // 파일별 병렬처리
        Stage,  // 단계별 병렬처리
        Table,  // 테이블별 병렬처리
        Rule    // 규칙별 병렬처리
    }

    /// <summary>
    /// 병렬처리 설정
    /// </summary>
    public class ParallelProcessingConfig
    {
        public int MaxParallelism { get; set; }
        public int BatchSize { get; set; }
        public bool EnableMemoryOptimization { get; set; }
        public bool EnableResourceMonitoring { get; set; }
    }

    /// <summary>
    /// 처리 진행률 정보
    /// </summary>
    public class ProcessingProgress
    {
        public int ProcessedItems { get; set; }
        public int TotalItems { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public double ProgressPercentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100 : 0;
    }
}
