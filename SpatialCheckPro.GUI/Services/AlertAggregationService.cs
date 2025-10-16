#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.ViewModels;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 검수 단계 알림을 집계하고 중복을 제거하는 서비스
    /// </summary>
    public class AlertAggregationService : IDisposable
    {
        private readonly ILogger<AlertAggregationService> _logger;
        private readonly ConcurrentDictionary<string, AlertViewModel> _alerts = new();
        private readonly TimeSpan _debounceInterval;
        private Timer? _flushTimer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isDisposed;

        /// <summary>
        /// 알림 집계 결과가 준비되었을 때 발생하는 이벤트
        /// </summary>
        public event EventHandler<IReadOnlyCollection<AlertViewModel>>? AlertsAggregated;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="debounceInterval">집계 지연 시간</param>
        public AlertAggregationService(ILogger<AlertAggregationService> logger, TimeSpan? debounceInterval = null)
        {
            _logger = logger;
            _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(350);
        }

        /// <summary>
        /// 알림을 추가하거나 갱신합니다
        /// </summary>
        /// <param name="alert">알림 뷰모델</param>
        public void EnqueueAlert(AlertViewModel alert)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AlertAggregationService));
            }

            _alerts.AddOrUpdate(alert.AlertKey, alert, (_, existing) =>
            {
                existing.Update(alert.Severity, alert.Message, alert.Detail, alert.RelatedStageStatus);
                return existing;
            });

            RestartTimer();
        }

        /// <summary>
        /// 지정된 단계의 알림을 제거합니다
        /// </summary>
        /// <param name="stageId">단계 식별자</param>
        public void RemoveAlert(string stageId)
        {
            _alerts.TryRemove(stageId, out _);
            RestartTimer();
        }

        /// <summary>
        /// 집계기를 초기화합니다
        /// </summary>
        public void Initialize()
        {
            RestartTimer();
        }

        private void RestartTimer()
        {
            _flushTimer?.Dispose();
            _flushTimer = new Timer(async _ => await FlushAsync().ConfigureAwait(false), null, _debounceInterval, Timeout.InfiniteTimeSpan);
        }

        private async Task FlushAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_alerts.IsEmpty)
                {
                    return;
                }

                var snapshot = _alerts.Values
                    .OrderByDescending(alert => alert.Timestamp)
                    .ToArray();

                AlertsAggregated?.Invoke(this, snapshot);
                _logger.LogDebug("알림 {Count}건 집계 완료", snapshot.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "알림 집계 중 오류 발생");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 리소스를 해제합니다
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _flushTimer?.Dispose();
            _semaphore.Dispose();
        }
    }
}


