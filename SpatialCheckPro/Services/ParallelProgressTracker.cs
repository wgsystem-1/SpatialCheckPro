using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 병렬 처리 진행률 추적 서비스
    /// </summary>
    public class ParallelProgressTracker
    {
        private readonly ILogger<ParallelProgressTracker> _logger;
        private readonly ConcurrentDictionary<string, ParallelTaskProgress> _taskProgress = new();
        private readonly ConcurrentDictionary<string, DateTime> _taskStartTimes = new();
        private readonly Timer _progressUpdateTimer;
        
        public event EventHandler<ParallelProgressEventArgs>? ProgressUpdated;

        public ParallelProgressTracker(ILogger<ParallelProgressTracker> logger)
        {
            _logger = logger;
            
            // 진행률 업데이트 타이머 (1초마다)
            _progressUpdateTimer = new Timer(UpdateProgress, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// 병렬 작업 등록
        /// </summary>
        public void RegisterTask(string taskId, string taskName, int totalItems)
        {
            _taskProgress[taskId] = new ParallelTaskProgress
            {
                TaskId = taskId,
                TaskName = taskName,
                TotalItems = totalItems,
                ProcessedItems = 0,
                StartTime = DateTime.Now,
                LastUpdateTime = DateTime.Now
            };
            
            _taskStartTimes[taskId] = DateTime.Now;
            
            _logger.LogDebug("병렬 작업 등록: {TaskId} - {TaskName} ({TotalItems}개 항목)", 
                taskId, taskName, totalItems);
        }

        /// <summary>
        /// 작업 진행률 업데이트
        /// </summary>
        public void UpdateTaskProgress(string taskId, int processedItems, string? currentItem = null)
        {
            if (_taskProgress.TryGetValue(taskId, out var progress))
            {
                progress.ProcessedItems = processedItems;
                progress.LastUpdateTime = DateTime.Now;
                
                if (!string.IsNullOrEmpty(currentItem))
                {
                    progress.CurrentItem = currentItem;
                }
                
                // 진행률 계산
                progress.ProgressPercentage = progress.TotalItems > 0 
                    ? (double)progress.ProcessedItems / progress.TotalItems * 100 
                    : 0;
                
                // 예상 완료 시간 계산
                if (progress.ProcessedItems > 0)
                {
                    var elapsed = DateTime.Now - progress.StartTime;
                    var estimatedTotal = TimeSpan.FromTicks(elapsed.Ticks * progress.TotalItems / progress.ProcessedItems);
                    progress.EstimatedCompletionTime = progress.StartTime.Add(estimatedTotal);
                }
            }
        }

        /// <summary>
        /// 작업 완료
        /// </summary>
        public void CompleteTask(string taskId)
        {
            if (_taskProgress.TryRemove(taskId, out var progress))
            {
                progress.CompletedTime = DateTime.Now;
                progress.IsCompleted = true;
                
                var duration = progress.CompletedTime.Value - progress.StartTime;
                
                _logger.LogInformation("병렬 작업 완료: {TaskId} - {TaskName} ({Duration:F1}초)", 
                    taskId, progress.TaskName, duration.TotalSeconds);
                
                // 완료 이벤트 발생
                ProgressUpdated?.Invoke(this, new ParallelProgressEventArgs
                {
                    TaskId = taskId,
                    TaskName = progress.TaskName,
                    ProgressPercentage = 100,
                    ProcessedItems = progress.TotalItems,
                    TotalItems = progress.TotalItems,
                    CurrentItem = "완료",
                    IsCompleted = true,
                    Duration = duration
                });
            }
            
            _taskStartTimes.TryRemove(taskId, out _);
        }

        /// <summary>
        /// 작업 취소
        /// </summary>
        public void CancelTask(string taskId)
        {
            if (_taskProgress.TryRemove(taskId, out var progress))
            {
                progress.IsCancelled = true;
                
                _logger.LogWarning("병렬 작업 취소: {TaskId} - {TaskName}", taskId, progress.TaskName);
                
                // 취소 이벤트 발생
                ProgressUpdated?.Invoke(this, new ParallelProgressEventArgs
                {
                    TaskId = taskId,
                    TaskName = progress.TaskName,
                    ProgressPercentage = progress.ProgressPercentage,
                    ProcessedItems = progress.ProcessedItems,
                    TotalItems = progress.TotalItems,
                    CurrentItem = "취소됨",
                    IsCancelled = true
                });
            }
            
            _taskStartTimes.TryRemove(taskId, out _);
        }

        /// <summary>
        /// 모든 작업 진행률 가져오기
        /// </summary>
        public List<ParallelTaskProgress> GetAllTaskProgress()
        {
            return _taskProgress.Values.ToList();
        }

        /// <summary>
        /// 특정 작업 진행률 가져오기
        /// </summary>
        public ParallelTaskProgress? GetTaskProgress(string taskId)
        {
            return _taskProgress.TryGetValue(taskId, out var progress) ? progress : null;
        }

        /// <summary>
        /// 전체 진행률 계산
        /// </summary>
        public OverallProgress GetOverallProgress()
        {
            var tasks = _taskProgress.Values.ToList();
            if (!tasks.Any())
            {
                return new OverallProgress();
            }

            var totalItems = tasks.Sum(t => t.TotalItems);
            var processedItems = tasks.Sum(t => t.ProcessedItems);
            var completedTasks = tasks.Count(t => t.IsCompleted);
            var cancelledTasks = tasks.Count(t => t.IsCancelled);
            var activeTasks = tasks.Count(t => !t.IsCompleted && !t.IsCancelled);

            return new OverallProgress
            {
                TotalTasks = tasks.Count,
                CompletedTasks = completedTasks,
                CancelledTasks = cancelledTasks,
                ActiveTasks = activeTasks,
                TotalItems = totalItems,
                ProcessedItems = processedItems,
                ProgressPercentage = totalItems > 0 ? (double)processedItems / totalItems * 100 : 0,
                AverageProgressPercentage = tasks.Average(t => t.ProgressPercentage)
            };
        }

        /// <summary>
        /// 진행률 업데이트 타이머 콜백
        /// </summary>
        private void UpdateProgress(object? state)
        {
            try
            {
                var activeTasks = _taskProgress.Values.Where(t => !t.IsCompleted && !t.IsCancelled).ToList();
                
                foreach (var task in activeTasks)
                {
                    // 진행률 이벤트 발생
                    ProgressUpdated?.Invoke(this, new ParallelProgressEventArgs
                    {
                        TaskId = task.TaskId,
                        TaskName = task.TaskName,
                        ProgressPercentage = task.ProgressPercentage,
                        ProcessedItems = task.ProcessedItems,
                        TotalItems = task.TotalItems,
                        CurrentItem = task.CurrentItem,
                        EstimatedCompletionTime = task.EstimatedCompletionTime,
                        Duration = DateTime.Now - task.StartTime
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "진행률 업데이트 중 오류 발생");
            }
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _progressUpdateTimer?.Dispose();
            _taskProgress.Clear();
            _taskStartTimes.Clear();
        }
    }

    /// <summary>
    /// 병렬 작업 진행률 정보
    /// </summary>
    public class ParallelTaskProgress
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public double ProgressPercentage { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCancelled { get; set; }
        public TimeSpan Duration => (CompletedTime ?? DateTime.Now) - StartTime;
    }

    /// <summary>
    /// 전체 진행률 정보
    /// </summary>
    public class OverallProgress
    {
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int CancelledTasks { get; set; }
        public int ActiveTasks { get; set; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public double ProgressPercentage { get; set; }
        public double AverageProgressPercentage { get; set; }
    }

    /// <summary>
    /// 병렬 진행률 이벤트 인수
    /// </summary>
    public class ParallelProgressEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; }
        public int ProcessedItems { get; set; }
        public int TotalItems { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public DateTime? EstimatedCompletionTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCancelled { get; set; }
    }
}
