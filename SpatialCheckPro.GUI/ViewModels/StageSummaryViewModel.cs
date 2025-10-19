#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.Models.Enums;
using SpatialCheckPro.Services.RemainingTime.Models;

namespace SpatialCheckPro.GUI.ViewModels
{
    /// <summary>
    /// 단일 검수 단계의 진행 상태를 표현하는 뷰모델
    /// </summary>
    public class StageSummaryViewModel : INotifyPropertyChanged
    {
        private StageStatus _status;
        private double _progress;
        private bool _isActive;
        private string _lastMessage = string.Empty;
        private DateTimeOffset? _startedAt;
        private DateTimeOffset? _completedAt;
        private DateTimeOffset _lastUpdatedAt;
        private TimeSpan? _estimatedRemaining;
        private long _processedUnits = -1;
        private long _totalUnits = -1;
        private TimeSpan? _predictedDuration;
        private double _etaConfidence;
        private string? _etaDisplayHint;

        /// <summary>
        /// 단계 식별자
        /// </summary>
        public string StageId { get; }

        /// <summary>
        /// 단계 번호
        /// </summary>
        public int StageNumber { get; }

        private string _stageName;

        /// <summary>
        /// 단계 표시 이름
        /// </summary>
        public string StageName
        {
            get => _stageName;
            set
            {
                if (_stageName == value)
                {
                    return;
                }
                _stageName = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 단계 상태
        /// </summary>
        public StageStatus Status
        {
            get => _status;
            private set
            {
                if (_status == value)
                {
                    return;
                }
                _status = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 단계 진행률 (0-100)
        /// </summary>
        public double Progress
        {
            get => _progress;
            private set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (Math.Abs(_progress - clamped) < 0.0001)
                {
                    return;
                }
                _progress = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 단계가 활성 상태인지 여부
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            private set
            {
                if (_isActive == value)
                {
                    return;
                }
                _isActive = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 마지막 상태 메시지
        /// </summary>
        public string LastMessage
        {
            get => _lastMessage;
            private set
            {
                if (_lastMessage == value)
                {
                    return;
                }
                _lastMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 단계 시작 시각
        /// </summary>
        public DateTimeOffset? StartedAt
        {
            get => _startedAt;
            private set
            {
                if (_startedAt == value)
                {
                    return;
                }
                _startedAt = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 단계 완료 시각
        /// </summary>
        public DateTimeOffset? CompletedAt
        {
            get => _completedAt;
            private set
            {
                if (_completedAt == value)
                {
                    return;
                }
                _completedAt = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 마지막 업데이트 시각
        /// </summary>
        public DateTimeOffset LastUpdatedAt
        {
            get => _lastUpdatedAt;
            private set
            {
                if (_lastUpdatedAt == value)
                {
                    return;
                }
                _lastUpdatedAt = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 남은 예상 시간
        /// </summary>
        public TimeSpan? EstimatedRemaining
        {
            get => _estimatedRemaining;
            private set
            {
                if (_estimatedRemaining == value)
                {
                    return;
                }
                _estimatedRemaining = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ETA 신뢰도
        /// </summary>
        public double EtaConfidence
        {
            get => _etaConfidence;
            private set
            {
                if (Math.Abs(_etaConfidence - value) < 0.0001)
                {
                    return;
                }
                _etaConfidence = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ETA 표시 문자열
        /// </summary>
        public string? EtaDisplayHint
        {
            get => _etaDisplayHint;
            private set
            {
                if (_etaDisplayHint == value)
                {
                    return;
                }
                _etaDisplayHint = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 처리된 단위 수
        /// </summary>
        public long ProcessedUnits
        {
            get => _processedUnits;
            private set
            {
                if (_processedUnits == value)
                {
                    return;
                }
                _processedUnits = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnitInfo));
            }
        }

        /// <summary>
        /// 전체 단위 수
        /// </summary>
        public long TotalUnits
        {
            get => _totalUnits;
            private set
            {
                if (_totalUnits == value)
                {
                    return;
                }
                _totalUnits = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnitInfo));
            }
        }

        /// <summary>
        /// 단위 기반 진행률 정보 보유 여부
        /// </summary>
        public bool HasUnitInfo => _processedUnits >= 0 && _totalUnits > 0;

        /// <summary>
        /// 단계별 알림 목록
        /// </summary>
        public ObservableCollection<AlertViewModel> Alerts { get; } = new();

        /// <summary>
        /// 알림이 존재하는지 여부
        /// </summary>
        public bool HasAlerts => Alerts.Count > 0;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="definition">단계 정의</param>
        public StageSummaryViewModel(StageDefinition definition)
        {
            StageId = definition.StageId;
            StageNumber = definition.StageNumber;
            _stageName = definition.StageName;
            _status = StageStatus.NotStarted;
            _progress = 0;
            _lastUpdatedAt = DateTimeOffset.MinValue;
        }

        /// <summary>
        /// 상태를 초기화합니다
        /// </summary>
        public void Reset()
        {
            Status = StageStatus.NotStarted;
            Progress = 0;
            IsActive = false;
            LastMessage = string.Empty;
            StartedAt = null;
            CompletedAt = null;
            LastUpdatedAt = DateTimeOffset.MinValue;
            EstimatedRemaining = null;
            EtaConfidence = 0;
            EtaDisplayHint = null;
            ProcessedUnits = -1;
            TotalUnits = -1;
            Alerts.Clear();
            _predictedDuration = null;
        }

        /// <summary>
        /// 진행률 이벤트를 반영합니다
        /// </summary>
        /// <param name="stageProgress">단계 진행률</param>
        /// <param name="statusMessage">상태 메시지</param>
        /// <param name="isCompleted">완료 여부</param>
        /// <param name="isSuccessful">성공 여부</param>
        /// <param name="isSkipped">스킵 여부</param>
        /// <param name="processedUnits">처리된 단위 수</param>
        /// <param name="totalUnits">전체 단위 수</param>
        public void ApplyProgress(double stageProgress, string statusMessage, bool isCompleted, bool isSuccessful, bool isSkipped, long processedUnits, long totalUnits)
        {
            var now = DateTimeOffset.Now;
            LastUpdatedAt = now;
            LastMessage = statusMessage;

            if (!StartedAt.HasValue && (stageProgress > 0 || isCompleted || isSkipped))
            {
                StartedAt = now;
            }

            if (processedUnits >= 0)
            {
                ProcessedUnits = processedUnits;
            }
            if (totalUnits >= 0)
            {
                TotalUnits = totalUnits;
            }

            if (isSkipped)
            {
                Progress = 100;
                Status = StageStatus.Skipped;
                IsActive = false;
                CompletedAt = now;
                EstimatedRemaining = TimeSpan.Zero;
                return;
            }

            Progress = stageProgress;

            if (isCompleted)
            {
                CompletedAt = now;
                Status = isSuccessful ? StageStatus.Completed : StageStatus.Failed;
                IsActive = false;
                EstimatedRemaining = TimeSpan.Zero;
            }
            else
            {
                if (stageProgress > 0 && stageProgress < 100)
                {
                    Status = StageStatus.Running;
                    IsActive = true;
                }
                else if (stageProgress <= 0 && Status == StageStatus.NotStarted)
                {
                    Status = StageStatus.Pending;
                    IsActive = false;
                }

                EstimatedRemaining = CalculateRemainingEstimate(now, stageProgress, processedUnits, totalUnits);
            }
        }

        /// <summary>
        /// ETA 결과를 적용합니다
        /// </summary>
        /// <param name="etaResult">ETA 결과</param>
        public void ApplyEta(StageEtaResult etaResult)
        {
            EstimatedRemaining = etaResult.EstimatedRemaining ?? _predictedDuration;
            EtaConfidence = etaResult.Confidence;
            EtaDisplayHint = etaResult.DisplayHint;
        }

        private TimeSpan? CalculateRemainingEstimate(DateTimeOffset now, double stageProgress, long processedUnits, long totalUnits)
        {
            if (CompletedAt.HasValue || Status == StageStatus.Skipped)
            {
                return TimeSpan.Zero;
            }

            if (!StartedAt.HasValue)
            {
                return _predictedDuration;
            }

            var elapsed = now - StartedAt.Value;
            if (elapsed.TotalSeconds <= 0.5)
            {
                return _predictedDuration;
            }

            if (processedUnits > 0 && totalUnits > 0 && processedUnits < totalUnits)
            {
                var rate = processedUnits / Math.Max(1.0, elapsed.TotalSeconds);
                if (rate > 0)
                {
                    var remainingUnits = totalUnits - processedUnits;
                    var remainingSeconds = remainingUnits / rate;
                    if (double.IsFinite(remainingSeconds) && remainingSeconds >= 0)
                    {
                        return TimeSpan.FromSeconds(remainingSeconds);
                    }
                }
            }

            if (stageProgress > 0 && stageProgress < 100)
            {
                var estimatedTotalSeconds = elapsed.TotalSeconds / (stageProgress / 100.0);
                var remainingSeconds = estimatedTotalSeconds - elapsed.TotalSeconds;
                if (double.IsFinite(remainingSeconds) && remainingSeconds > 0)
                {
                    return TimeSpan.FromSeconds(remainingSeconds);
                }
            }

            return _predictedDuration;
        }

        /// <summary>
        /// 단계 상태를 강제로 설정합니다
        /// </summary>
        /// <param name="status">설정할 상태</param>
        public void ForceStatus(StageStatus status)
        {
            Status = status;
            if (status == StageStatus.Blocked)
            {
                IsActive = false;
                Progress = 0;
                EstimatedRemaining = null;
            }
        }

        /// <summary>
        /// 진행률을 강제로 설정합니다
        /// </summary>
        /// <param name="progress">설정할 진행률</param>
        public void ForceProgress(double progress)
        {
            Progress = progress;
        }

        /// <summary>
        /// 단위 정보를 갱신합니다
        /// </summary>
        /// <param name="processed">처리된 단위 수</param>
        /// <param name="total">전체 단위 수</param>
        public void UpdateUnits(long processed, long total)
        {
            ProcessedUnits = processed;
            TotalUnits = total;
        }

        /// <summary>
        /// 활성 상태를 설정합니다
        /// </summary>
        /// <param name="isActive">활성 여부</param>
        public void SetActive(bool isActive)
        {
            IsActive = isActive;
        }

        /// <summary>
        /// 예측된 소요 시간을 설정합니다
        /// </summary>
        /// <param name="duration">예상 소요 시간</param>
        public void SetPredictedDuration(TimeSpan duration)
        {
            _predictedDuration = duration;
            if (Status == StageStatus.NotStarted || Status == StageStatus.Pending)
            {
                EstimatedRemaining = duration;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


