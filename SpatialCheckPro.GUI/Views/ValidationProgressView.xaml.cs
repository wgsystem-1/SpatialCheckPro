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
        private DispatcherTimer? _resourceMonitorTimer;
        private DateTime _startTime;
        
        public ValidationProgressView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            _stageSummaries = EnsureStageSummaryViewModel();
            InitializeElapsedTimer();
            InitializeResourceMonitorTimer();
            ResetHeader();
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

        /// <summary>
        /// 리소스 모니터링 타이머 초기화
        /// </summary>
        private void InitializeResourceMonitorTimer()
        {
            _resourceMonitorTimer?.Stop();
            _resourceMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // 2초마다 업데이트
            };
            _resourceMonitorTimer.Tick += (_, __) => UpdateResourceMetrics();
            _resourceMonitorTimer.Start();
        }

        /// <summary>
        /// 리소스 메트릭 업데이트 (Phase 2 UI)
        /// </summary>
        private void UpdateResourceMetrics()
        {
            try
            {
                // CPU 사용률 업데이트
                var cpuUsage = _stageSummaries.CpuUsagePercent;
                CpuUsageText.Text = cpuUsage.ToString("F0");
                CpuProgressBar.Value = cpuUsage;

                // CPU 상태 색상 업데이트
                if (cpuUsage > 80)
                {
                    CpuIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                    CpuStatusText.Text = "높음 (80% 이상)";
                }
                else if (cpuUsage > 70)
                {
                    CpuIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16)); // Orange
                    CpuStatusText.Text = "보통 (70-80%)";
                }
                else
                {
                    CpuIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
                    CpuStatusText.Text = "적정 범위 (50-70%)";
                }

                // 메모리 압박 업데이트
                var memoryPressure = _stageSummaries.MemoryPressurePercent;
                MemoryPressureText.Text = memoryPressure.ToString("F0");
                MemoryProgressBar.Value = memoryPressure;

                // 메모리 상태 색상 업데이트
                if (memoryPressure > 80)
                {
                    MemoryIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                    MemoryStatusText.Text = "높음 (80% 이상)";
                }
                else if (memoryPressure > 60)
                {
                    MemoryIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16)); // Orange
                    MemoryStatusText.Text = "보통 (60-80%)";
                }
                else
                {
                    MemoryIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
                    MemoryStatusText.Text = "낮음 (60% 미만)";
                }

                // 병렬도 업데이트
                ParallelismCurrentText.Text = _stageSummaries.CurrentParallelism.ToString();
                ParallelismMaxText.Text = _stageSummaries.MaxParallelism.ToString();

                // 캐시 히트율 업데이트
                var cacheHitRatio = _stageSummaries.CacheHitRatio * 100;
                CacheHitRatioText.Text = cacheHitRatio.ToString("F0");
                CachedItemsText.Text = $"Layer: {_stageSummaries.CachedLayerCount}개 | Schema: {_stageSummaries.CachedSchemaCount}개";

                // 스트리밍 모드 업데이트
                if (_stageSummaries.StreamingModeActive)
                {
                    StreamingStatusBadge.Content = "활성";
                    StreamingStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
                    StreamedErrorCountText.Text = _stageSummaries.StreamedErrorCount.ToString("N0");
                    StreamingFilePathText.Text = System.IO.Path.GetFileName(_stageSummaries.StreamingFilePath);
                }
                else
                {
                    StreamingStatusBadge.Content = "비활성";
                    StreamingStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)); // Gray
                    StreamedErrorCountText.Text = "0";
                    StreamingFilePathText.Text = "없음";
                }

                // 배치 처리 업데이트
                BatchSizeText.Text = _stageSummaries.BatchSize.ToString("N0");
                BatchThroughputText.Text = _stageSummaries.BatchThroughput.ToString("N0");
            }
            catch (Exception)
            {
                // UI 업데이트 실패는 무시 (요소가 아직 로드되지 않았을 수 있음)
            }
        }

        // 기존 단계별 UI 메서드 제거됨
    }
}