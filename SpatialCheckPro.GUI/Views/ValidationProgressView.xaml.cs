using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.ViewModels;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 검수 진행 화면
    /// </summary>
    public partial class ValidationProgressView : UserControl
    {
        public event EventHandler? ValidationStopRequested;
        private StageSummaryCollectionViewModel _stageSummaries;
        private DispatcherTimer? _elapsedTimer;
        private DateTime _startTime;
        
        public ValidationProgressView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            _stageSummaries = EnsureStageSummaryViewModel();
            InitializeElapsedTimer();
            ResetHeader();
        }

        private StageSummaryCollectionViewModel EnsureStageSummaryViewModel()
        {
            if (DataContext is StageSummaryCollectionViewModel existing)
            {
                return existing;
            }

            var fallback = new StageSummaryCollectionViewModel();
            DataContext = fallback;
            return fallback;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is StageSummaryCollectionViewModel vm)
            {
                _stageSummaries = vm;
                ResetHeader();
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
        }

        public void UpdateUnits(int stageNumber, long processedUnits, long totalUnits)
        {
            var stage = _stageSummaries.GetStage(stageNumber);
            stage?.UpdateUnits(processedUnits, totalUnits);
            UpdateRemainingTime();
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
            EstimatedTimeText.Text = _stageSummaries.RemainingTotalEta.HasValue
                ? FormatRemainingLabel(_stageSummaries.RemainingTotalEta.Value.TotalSeconds)
                : "계산 중...";
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
        /// 로그 메시지를 추가합니다
        /// </summary>
        /// <param name="message">로그 메시지</param>
        public void AddLogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            
            if (string.IsNullOrEmpty(LogTextBlock.Text) || LogTextBlock.Text == "검수 로그가 여기에 표시됩니다...")
            {
                LogTextBlock.Text = logEntry;
            }
            else
            {
                LogTextBlock.Text += Environment.NewLine + logEntry;
            }
            
            // 스크롤을 맨 아래로 이동
            LogScrollViewer.ScrollToEnd();
        }

        public void UpdateElapsedTime(TimeSpan elapsed)
        {
            ElapsedTimeText.Text = elapsed.ToString("hh\\:mm\\:ss");
        }

        // 기존 단계별 UI 메서드 제거됨
    }
}