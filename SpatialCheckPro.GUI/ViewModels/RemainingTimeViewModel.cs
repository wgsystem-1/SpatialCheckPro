using System;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpatialCheckPro.GUI.ViewModels
{
    /// <summary>
    /// 남은 시간 표시를 위한 뷰모델
    /// </summary>
    public class RemainingTimeViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private DateTime _estimatedEndTime;
        private TimeSpan _originalEstimatedDuration;
        private DateTime _startTime;
        private bool _isOverdue;
        private string _displayText = "계산 중...";
        private double _confidencePercent = 0;
        private string _estimatedEndTimeText = "-";
        private string _speedIndicatorText = "1.0x";
        private double _speedRatio = 1.0;
        private string _remainingWorkText = "-";
        private TimeSpan _pausedDuration = TimeSpan.Zero;
        private DateTime? _pauseStartTime;

        public RemainingTimeViewModel()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// 표시할 텍스트
        /// </summary>
        public string DisplayText
        {
            get => _displayText;
            private set
            {
                if (_displayText != value)
                {
                    _displayText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 예상 시간 초과 여부
        /// </summary>
        public bool IsOverdue
        {
            get => _isOverdue;
            private set
            {
                if (_isOverdue != value)
                {
                    _isOverdue = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 신뢰도 백분율
        /// </summary>
        public double ConfidencePercent
        {
            get => _confidencePercent;
            set
            {
                if (Math.Abs(_confidencePercent - value) > 0.01)
                {
                    _confidencePercent = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 예상 완료 시각 텍스트
        /// </summary>
        public string EstimatedEndTimeText
        {
            get => _estimatedEndTimeText;
            private set
            {
                if (_estimatedEndTimeText != value)
                {
                    _estimatedEndTimeText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 속도 표시 텍스트
        /// </summary>
        public string SpeedIndicatorText
        {
            get => _speedIndicatorText;
            private set
            {
                if (_speedIndicatorText != value)
                {
                    _speedIndicatorText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 속도 비율
        /// </summary>
        public double SpeedRatio
        {
            get => _speedRatio;
            private set
            {
                if (Math.Abs(_speedRatio - value) > 0.01)
                {
                    _speedRatio = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 남은 작업량 텍스트
        /// </summary>
        public string RemainingWorkText
        {
            get => _remainingWorkText;
            set
            {
                if (_remainingWorkText != value)
                {
                    _remainingWorkText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 예상 시간 설정 및 타이머 시작
        /// </summary>
        public void SetEstimatedTime(TimeSpan estimatedDuration, double confidence = 0.8)
        {
            _startTime = DateTime.Now;
            _originalEstimatedDuration = estimatedDuration;
            _estimatedEndTime = _startTime.Add(estimatedDuration);
            ConfidencePercent = confidence;
            _isOverdue = false;
            
            _timer.Start();
            UpdateDisplay();
        }

        /// <summary>
        /// 예상 시간 업데이트 (진행 중 재계산)
        /// </summary>
        public void UpdateEstimatedTime(TimeSpan newEstimatedRemaining, double confidence)
        {
            _estimatedEndTime = DateTime.Now.Add(newEstimatedRemaining);
            ConfidencePercent = confidence;
            UpdateDisplay();
        }

        /// <summary>
        /// 타이머 정지
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
        }

        /// <summary>
        /// 리셋
        /// </summary>
        public void Reset()
        {
            _timer.Stop();
            DisplayText = "계산 중...";
            IsOverdue = false;
            ConfidencePercent = 0;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var now = DateTime.Now;
            var actualElapsed = now - _startTime - _pausedDuration;
            var remaining = _estimatedEndTime - now;

            if (remaining.TotalSeconds < 0)
            {
                // 예상 시간 초과
                IsOverdue = true;
                var overdue = -remaining;
                
                if (overdue.TotalHours >= 1)
                {
                    DisplayText = $"-{(int)overdue.TotalHours}:{overdue.Minutes:D2}:{overdue.Seconds:D2} (초과)";
                }
                else if (overdue.TotalMinutes >= 1)
                {
                    DisplayText = $"-{(int)overdue.TotalMinutes}:{overdue.Seconds:D2} (초과)";
                }
                else
                {
                    DisplayText = $"-{(int)overdue.TotalSeconds}초 (초과)";
                }
            }
            else
            {
                // 정상 진행
                IsOverdue = false;
                
                if (remaining.TotalSeconds < 1)
                {
                    DisplayText = "거의 완료";
                }
                else if (remaining.TotalHours >= 1)
                {
                    DisplayText = $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                }
                else if (remaining.TotalMinutes >= 1)
                {
                    DisplayText = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
                }
                else
                {
                    DisplayText = $"{(int)remaining.TotalSeconds}초";
                }
            }

            // 진행률 기반 동적 업데이트
            if (!IsOverdue)
            {
                var progress = actualElapsed.TotalSeconds / _originalEstimatedDuration.TotalSeconds;
                
                // 진행률이 50%를 넘었는데 예상보다 느리면 경고 색상으로 변경
                if (progress > 0.5 && actualElapsed > _originalEstimatedDuration * 0.5 * 1.2)
                {
                    DisplayText += " ⚠";
                }
            }

            // 예상 완료 시각 업데이트
            UpdateEstimatedEndTime();
            
            // 속도 비율 업데이트
            UpdateSpeedRatio(actualElapsed);
        }
        
        private void UpdateEstimatedEndTime()
        {
            if (_estimatedEndTime > DateTime.Now)
            {
                var timeFormat = _estimatedEndTime.Date == DateTime.Today ? "오늘 " : "내일 ";
                timeFormat += _estimatedEndTime.ToString("tt h:mm");
                EstimatedEndTimeText = $"{timeFormat} 완료 예정";
            }
            else
            {
                EstimatedEndTimeText = "완료 예정 시간 초과";
            }
        }
        
        private void UpdateSpeedRatio(TimeSpan actualElapsed)
        {
            if (_originalEstimatedDuration.TotalSeconds > 0 && actualElapsed.TotalSeconds > 0)
            {
                var expectedElapsed = DateTime.Now - _startTime - _pausedDuration;
                var expectedProgress = expectedElapsed.TotalSeconds / _originalEstimatedDuration.TotalSeconds;
                
                if (expectedProgress > 0.1) // 10% 이상 진행된 경우만 계산
                {
                    var actualSpeed = actualElapsed.TotalSeconds / expectedProgress;
                    var expectedSpeed = _originalEstimatedDuration.TotalSeconds;
                    SpeedRatio = expectedSpeed / actualSpeed;
                    
                    if (SpeedRatio > 1.2)
                    {
                        SpeedIndicatorText = $"{SpeedRatio:F1}x 빠름 🚀";
                    }
                    else if (SpeedRatio < 0.8)
                    {
                        SpeedIndicatorText = $"{SpeedRatio:F1}x 느림 🐢";
                    }
                    else
                    {
                        SpeedIndicatorText = $"{SpeedRatio:F1}x 정상";
                    }
                }
            }
        }
        
        /// <summary>
        /// 일시정지
        /// </summary>
        public void Pause()
        {
            _pauseStartTime = DateTime.Now;
            _timer.Stop();
        }
        
        /// <summary>
        /// 재개
        /// </summary>
        public void Resume()
        {
            if (_pauseStartTime.HasValue)
            {
                _pausedDuration += DateTime.Now - _pauseStartTime.Value;
                _pauseStartTime = null;
            }
            _timer.Start();
            UpdateDisplay();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
