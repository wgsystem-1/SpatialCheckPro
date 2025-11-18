using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.ViewModels;
using SpatialCheckPro.GUI.Converters;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 검수 진행 화면
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class ValidationProgressView : UserControl
    {
        public event EventHandler? ValidationStopRequested;
        private StageSummaryCollectionViewModel _stageSummaries;
        private readonly RemainingTimeViewModel _remainingTimeViewModel;
        private DispatcherTimer? _elapsedTimer;
        private DateTime _startTime;
        
        public ValidationProgressView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            _stageSummaries = EnsureStageSummaryViewModel();
            _remainingTimeViewModel = new RemainingTimeViewModel();
            InitializeElapsedTimer();
            ResetHeader();

            // CompletedStageCount 변경 시 자동으로 UI 업데이트
            _stageSummaries.PropertyChanged += OnStageSummariesPropertyChanged;
        }

        private StageSummaryCollectionViewModel EnsureStageSummaryViewModel()
        {
            if (DataContext is StageSummaryCollectionViewModel existing)
            {
                return existing;
            }

            var fallback = ((App)Application.Current).GetService<StageSummaryCollectionViewModel>() ?? throw new InvalidOperationException("StageSummaryCollectionViewModel 서비스를 찾을 수 없습니다.");
            DataContext = fallback;
            return fallback;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 기존 이벤트 구독 해제
            if (e.OldValue is StageSummaryCollectionViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnStageSummariesPropertyChanged;
            }

            // 새 ViewModel 설정 및 이벤트 구독
            if (e.NewValue is StageSummaryCollectionViewModel vm)
            {
                _stageSummaries = vm;
                vm.PropertyChanged += OnStageSummariesPropertyChanged;
                ResetHeader();
            }
        }

        /// <summary>
        /// StageSummaryCollectionViewModel의 속성 변경 시 UI 자동 업데이트
        /// </summary>
        private void OnStageSummariesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StageSummaryCollectionViewModel.CompletedStageCount))
            {
                Dispatcher.Invoke(() =>
                {
                    var completedCount = _stageSummaries.CompletedStageCount;
                    var totalCount = _stageSummaries.Stages.Count;
                    CompletedStagesText.Text = $"{completedCount} / {totalCount}";
                    System.Console.WriteLine($"[ValidationProgressView.OnStageSummariesPropertyChanged] 완료 단계 자동 업데이트: {completedCount}/{totalCount}");
                });
            }
        }

        private void InitializeElapsedTimer()
        {
            _startTime = DateTime.Now;
            _elapsedTimer?.Stop();
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (_, __) => UpdateElapsedTime(DateTime.Now - _startTime);
            _elapsedTimer.Start();
        }

        private void ResetHeader()
        {
            ProgressBar.Value = 0;
            ProgressPercentageText.Text = "0%";
            CurrentStageText.Text = "대기 중";
            EstimatedTimeText.Text = "계산 중...";
            CompletedStagesText.Text = $"0 / {_stageSummaries.Stages.Count}";
            _remainingTimeViewModel?.Reset();
        }
        
        /// <summary>
        /// 검수 중지 버튼 클릭
        /// </summary>
        private void StopValidation_Click(object sender, RoutedEventArgs e)
        {
            ValidationStopRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 진행률을 업데이트합니다
        /// </summary>
        /// <param name="percentage">진행률 (0-100)</param>
        /// <param name="status">상태 메시지</param>
        public void UpdateProgress(double percentage, string status)
        {
            ProgressBar.Value = percentage;
            ProgressPercentageText.Text = $"{percentage:F0}%";
            ProgressStatusText.Text = status;
            UpdateRemainingTime();
            CompletedStagesText.Text = $"{_stageSummaries.CompletedStageCount} / {_stageSummaries.Stages.Count}";
        }

        public void UpdateCurrentStage(string stageName, int stageNumber)
        {
            CurrentStageText.Text = string.IsNullOrWhiteSpace(stageName)
                ? StageDefinitions.GetByNumber(stageNumber).StageName
                : stageName;
            HighlightActiveStage(stageNumber);
        }

        public void UpdateStageProgress(int stageNumber, double percentage)
        {
            var stage = _stageSummaries.GetStage(stageNumber);
            stage?.ForceProgress(percentage);
            UpdateRemainingTime();
            
            // 진행률이 변경될 때마다 예상 시간 업데이트
            if (_stageSummaries.RemainingTotalEta.HasValue && _remainingTimeViewModel != null)
            {
                _remainingTimeViewModel.UpdateEstimatedTime(
                    _stageSummaries.RemainingTotalEta.Value, 
                    _stageSummaries.RemainingEtaConfidence);
            }
        }

        public void UpdateUnits(int stageNumber, long processedUnits, long totalUnits)
        {
            System.Console.WriteLine($"[ValidationProgressView.UpdateUnits] 호출됨 - Stage={stageNumber}, {processedUnits}/{totalUnits}, Thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");

            // UI 스레드에서 실행되도록 보장
            if (!Dispatcher.CheckAccess())
            {
                System.Console.WriteLine($"[ValidationProgressView.UpdateUnits] ⚠️ UI 스레드가 아님 - Dispatcher로 전환");
                Dispatcher.Invoke(() => UpdateUnits(stageNumber, processedUnits, totalUnits));
                return;
            }

            System.Console.WriteLine($"[ValidationProgressView.UpdateUnits] ✅ UI 스레드에서 실행 중");

            var stage = _stageSummaries.GetStage(stageNumber);
            stage?.UpdateUnits(processedUnits, totalUnits);
            UpdateRemainingTime();

            System.Console.WriteLine($"[ValidationProgressView.UpdateUnits] ✅ UpdateUnits 완료");
        }

        private void HighlightActiveStage(int stageNumber)
        {
            foreach (var stage in _stageSummaries.Stages)
            {
                stage.SetActive(stage.StageNumber == stageNumber);
            }
        }

        private void UpdateRemainingTime()
        {
            if (_stageSummaries.RemainingTotalEta.HasValue)
            {
                var remainingTime = _stageSummaries.RemainingTotalEta.Value;
                var confidence = _stageSummaries.RemainingEtaConfidence;
                
                // RemainingTimeViewModel에 예상 시간 설정
                _remainingTimeViewModel.SetEstimatedTime(remainingTime, confidence);
                
                // 직접 업데이트 방식으로 변경 (더 간단하고 안정적)
                UpdateRemainingTimeDisplay();
            }
            else
            {
                EstimatedTimeText.Text = "계산 중...";
                EstimatedTimeText.ClearValue(TextBlock.DataContextProperty);
            }
        }

        private static string FormatRemainingLabel(double seconds)
        {
            seconds = Math.Max(0, seconds);
            if (seconds < 1) return "1초 이내";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            return $"{Math.Ceiling(ts.TotalSeconds):0}초";
        }

        /// <summary>
        /// 로그 메시지를 추가합니다 (Deprecated - 로그 UI가 제거됨)
        /// </summary>
        /// <param name="message">로그 메시지</param>
        public void AddLogMessage(string message)
        {
            // 로그 UI가 제거되었으므로 아무 동작도 하지 않음
            // 이 메서드는 호환성을 위해 유지되며, 향후 제거될 예정
        }

        public void UpdateElapsedTime(TimeSpan elapsed)
        {
            ElapsedTimeText.Text = elapsed.ToString("hh\\:mm\\:ss");
            
            // 남은 시간도 함께 업데이트
            UpdateRemainingTimeDisplay();
        }
        
        private void UpdateRemainingTimeDisplay()
        {
            if (_remainingTimeViewModel != null)
            {
                EstimatedTimeText.Text = _remainingTimeViewModel.DisplayText;
                
                // 초과 시 빨간색으로 표시
                if (_remainingTimeViewModel.IsOverdue)
                {
                    EstimatedTimeText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444
                }
                else
                {
                    EstimatedTimeText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(31, 41, 55)); // #1F2937
                }
                
                // 추가 정보 업데이트
                EstimatedEndTimeText.Text = _remainingTimeViewModel.EstimatedEndTimeText;
                SpeedIndicatorText.Text = _remainingTimeViewModel.SpeedIndicatorText;
                
                // 속도에 따른 색상 변경
                if (_remainingTimeViewModel.SpeedRatio < 0.8)
                {
                    SpeedIndicatorText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(239, 68, 68)); // 느림 - 빨간색
                }
                else if (_remainingTimeViewModel.SpeedRatio > 1.2)
                {
                    SpeedIndicatorText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(16, 185, 129)); // 빠름 - 초록색
                }
                else
                {
                    SpeedIndicatorText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(107, 114, 128)); // 정상 - 회색
                }
                
                // 남은 작업량은 외부에서 설정
                if (!string.IsNullOrEmpty(_remainingTimeViewModel.RemainingWorkText))
                {
                    RemainingWorkText.Text = _remainingTimeViewModel.RemainingWorkText;
                }
            }
        }
        
        /// <summary>
        /// 남은 작업량 업데이트
        /// </summary>
        public void UpdateRemainingWork(int remainingTables, int remainingFeatures, double percentComplete)
        {
            if (_remainingTimeViewModel != null)
            {
                var workText = $"남은 작업: 테이블 {remainingTables}개, 피처 {remainingFeatures:N0}개 ({100 - percentComplete:F1}%)";
                _remainingTimeViewModel.RemainingWorkText = workText;
                UpdateRemainingTimeDisplay();
            }
        }

        // 기존 단계별 UI 메서드 제거됨
    }
}