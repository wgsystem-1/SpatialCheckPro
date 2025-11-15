using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 병렬 처리 관리 서비스
    /// </summary>
    public class ParallelProcessingManager
    {
        private readonly ILogger<ParallelProcessingManager> _logger;
        private readonly PerformanceSettings _settings;
        private readonly SemaphoreSlim _semaphore;
        private volatile int _currentParallelism;

        public ParallelProcessingManager(
            ILogger<ParallelProcessingManager> logger,
            PerformanceSettings settings)
        {
            _logger = logger;
            _settings = settings;

            _currentParallelism = Math.Max(1, _settings.MaxDegreeOfParallelism);
            _semaphore = new SemaphoreSlim(_currentParallelism, _currentParallelism);

            _logger.LogInformation("병렬 처리 관리자 초기화: 최대 병렬도 {MaxParallelism}", _currentParallelism);
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
            var semaphore = _semaphore;
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
            _semaphore.Dispose();
        }
    }
}
