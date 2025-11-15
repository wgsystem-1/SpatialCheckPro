using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.ViewModels;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 컴팩트한 검수 진행 화면
    /// </summary>
    public partial class CompactValidationProgressView : UserControl
    {
        public event EventHandler? ValidationStopRequested;
        private StageSummaryCollectionViewModel _stageSummaries;
        private readonly RemainingTimeViewModel _remainingTimeViewModel;
        private DateTime _startTime; // 전체 검수 시작 시간
        private DateTime? _currentStageStartTime; // 현재 단계 시작 시간
        private int _currentStageNumber = -1; // 현재 단계 번호
        private bool _isDetailExpanded = true;
        private int _totalErrorCount = 0;

        public CompactValidationProgressView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            _stageSummaries = EnsureStageSummaryViewModel();
            _remainingTimeViewModel = new RemainingTimeViewModel();
            _startTime = DateTime.Now; // 시작 시간 초기화 (UpdateUnits에서 사용)
            InitializeStageCards();
            ResetHeader();
        }

        private StageSummaryCollectionViewModel EnsureStageSummaryViewModel()
        {
            if (DataContext is StageSummaryCollectionViewModel existing)
            {
                return existing;
            }

            var fallback = ((App)Application.Current).GetService<StageSummaryCollectionViewModel>() 
                ?? throw new InvalidOperationException("StageSummaryCollectionViewModel 서비스를 찾을 수 없습니다.");
            DataContext = fallback;
            return fallback;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is StageSummaryCollectionViewModel vm)
            {
                _stageSummaries = vm;
                InitializeStageCards();
                ResetHeader();
            }
        }

        /// <summary>
        /// 시작 시간을 초기화합니다 (외부에서 호출 가능)
        /// </summary>
        public void ResetStartTime()
        {
            _startTime = DateTime.Now;
            _currentStageStartTime = null;
            _currentStageNumber = -1;
            System.Console.WriteLine($"[ResetStartTime] 검수 시작 시간 초기화: {_startTime:HH:mm:ss.fff}");
        }

        private void ResetHeader()
        {
            ProgressBar.Value = 0;
            ProgressPercentageText.Text = "0%";
            CurrentStageText.Text = "대기 중";
            EstimatedTimeText.Text = "계산 중...";
            CompletedStagesText.Text = $"0 / {_stageSummaries.Stages.Count}";
            TotalErrorsText.Text = "0";
            _remainingTimeViewModel?.Reset();
        }

        /// <summary>
        /// 단계별 카드 초기화
        /// </summary>
        private void InitializeStageCards()
        {
            StageCardsPanel.Children.Clear();
            
            var stageList = _stageSummaries.Stages.ToList();
            for (int i = 0; i < stageList.Count; i++)
            {
                var stage = stageList[i];
                var card = CreateStageCard(stage, i == 0, i == stageList.Count - 1);
                StageCardsPanel.Children.Add(card);
            }
        }

        /// <summary>
        /// 단계 카드 생성
        /// </summary>
        private Border CreateStageCard(StageSummaryViewModel stage, bool isFirst = false, bool isLast = false)
        {
            var card = new Border
            {
                Style = Resources["StageCard"] as Style,
                Tag = stage.StageNumber
            };
            
            // UniformGrid에서 균등 배치하므로 첫/마지막 카드 여백 조정
            if (isFirst)
            {
                card.Margin = new Thickness(0, 0, 4, 0);
            }
            else if (isLast)
            {
                card.Margin = new Thickness(4, 0, 0, 0);
            }
            else
            {
                card.Margin = new Thickness(4, 0, 4, 0);
            }

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 단계 번호 + 아이콘
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            
            var numberBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var numberText = new TextBlock
            {
                Text = stage.StageNumber.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            numberBorder.Child = numberText;
            headerPanel.Children.Add(numberBorder);

            var stageIcon = new TextBlock
            {
                Text = GetStageIcon(stage.StageNumber),
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(stageIcon);

            grid.Children.Add(headerPanel);
            Grid.SetRow(headerPanel, 0);

            // 단계명
            var nameText = new TextBlock
            {
                Text = stage.StageName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            grid.Children.Add(nameText);
            Grid.SetRow(nameText, 1);

            // 진행률 바
            var progressBar = new ProgressBar
            {
                Height = 6,
                Background = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            progressBar.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("Progress") 
            { 
                Source = stage,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
            grid.Children.Add(progressBar);
            Grid.SetRow(progressBar, 2);

            // 상태 정보
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var progressText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246))
            };
            progressText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Progress") 
            { 
                Source = stage,
                StringFormat = "{0:F0}%",
                Mode = System.Windows.Data.BindingMode.OneWay
            });
            statusPanel.Children.Add(progressText);

            var statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(8, 0, 0, 0)
            };
            statusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusText") 
            { 
                Source = stage,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
            statusPanel.Children.Add(statusText);

            grid.Children.Add(statusPanel);
            Grid.SetRow(statusPanel, 3);

            card.Child = grid;

            // 단계 상태에 따른 스타일 변경
            stage.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(stage.IsActive))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (stage.IsActive)
                        {
                            card.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                            card.BorderThickness = new Thickness(2);
                            numberBorder.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                            numberText.Foreground = Brushes.White;
                        }
                        else if (stage.Progress >= 100)
                        {
                            numberBorder.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                            numberText.Foreground = Brushes.White;
                            card.BorderBrush = Brushes.Transparent;
                            card.BorderThickness = new Thickness(0);
                        }
                        else
                        {
                            card.BorderBrush = Brushes.Transparent;
                            card.BorderThickness = new Thickness(0);
                        }
                    });
                }
            };

            return card;
        }

        private string GetStageIcon(int stageNumber)
        {
            return stageNumber switch
            {
                0 => "📦",
                1 => "📋",
                2 => "🔍",
                3 => "🗺️",
                4 => "📊",
                5 => "🔗",
                _ => "⚙️"
            };
        }

        /// <summary>
        /// 검수 중지 버튼 클릭
        /// </summary>
        private void StopValidation_Click(object sender, RoutedEventArgs e)
        {
            ValidationStopRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 상세 정보 접기/펴기
        /// </summary>
        private void ToggleDetail_Click(object sender, RoutedEventArgs e)
        {
            _isDetailExpanded = !_isDetailExpanded;
            
            if (_isDetailExpanded)
            {
                DetailScrollViewer.Visibility = Visibility.Visible;
                ToggleDetailButton.Content = "▲ 접기";
            }
            else
            {
                DetailScrollViewer.Visibility = Visibility.Collapsed;
                ToggleDetailButton.Content = "▼ 펼치기";
            }
        }

        /// <summary>
        /// 진행률을 업데이트합니다
        /// </summary>
        public void UpdateProgress(double percentage, string status)
        {
            ProgressBar.Value = percentage;
            ProgressPercentageText.Text = $"{percentage:F0}%";
            UpdateRemainingTime();
            
            var completedCount = _stageSummaries.CompletedStageCount;
            var totalCount = _stageSummaries.Stages.Count;
            CompletedStagesText.Text = $"{completedCount} / {totalCount}";
            
            System.Console.WriteLine($"[UpdateProgress] 완료 단계: {completedCount}/{totalCount}");
        }

        public void UpdateCurrentStage(string stageName, int stageNumber)
        {
            CurrentStageText.Text = string.IsNullOrWhiteSpace(stageName)
                ? StageDefinitions.GetByNumber(stageNumber).StageName
                : stageName;
            
            DetailHeaderText.Text = $"{stageName} 상세 정보";
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
            System.Console.WriteLine($"[UpdateUnits] 호출됨 - Stage={stageNumber}, {processedUnits}/{totalUnits}");
            
            // 단계가 변경되면 시작 시간 초기화
            if (_currentStageNumber != stageNumber)
            {
                _currentStageNumber = stageNumber;
                _currentStageStartTime = DateTime.Now;
                System.Console.WriteLine($"[UpdateUnits] 단계 변경 감지: Stage {stageNumber} 시작 시간 = {_currentStageStartTime:HH:mm:ss.fff}");
            }
            
            var stage = _stageSummaries.GetStage(stageNumber);
            stage?.UpdateUnits(processedUnits, totalUnits);
            UpdateRemainingTime();
            
            // 상세 정보 업데이트
            ProcessedItemsText.Text = processedUnits.ToString("N0");
            TotalItemsText.Text = totalUnits.ToString("N0");
            
            System.Console.WriteLine($"[UpdateUnits] 상세 정보 텍스트 업데이트 완료: {processedUnits:N0}/{totalUnits:N0}");
            
            // 처리 속도 계산 (단계별 시작 시간 사용)
            var stageElapsed = _currentStageStartTime.HasValue 
                ? DateTime.Now - _currentStageStartTime.Value 
                : DateTime.Now - _startTime;
                
            if (stageElapsed.TotalSeconds > 0 && processedUnits > 0)
            {
                var speed = processedUnits / stageElapsed.TotalSeconds;
                ProcessingSpeedText.Text = $"{speed:F0}/초";
                
                System.Console.WriteLine($"[UpdateUnits] 처리 속도: {speed:F0}/초 (경과 시간: {stageElapsed.TotalSeconds:F1}초)");
                
                // 이 단계 남은 시간 계산
                if (speed > 0 && totalUnits > processedUnits)
                {
                    var remaining = (totalUnits - processedUnits) / speed;
                    StageRemainingTimeText.Text = FormatTime(remaining);
                    System.Console.WriteLine($"[UpdateUnits] 남은 시간: {FormatTime(remaining)}");
                }
                else
                {
                    StageRemainingTimeText.Text = "계산 중...";
                }
            }
            else
            {
                ProcessingSpeedText.Text = "0/초";
                StageRemainingTimeText.Text = "-";
            }
            
            // 현재 처리 정보
            var stageName = stage?.StageName ?? "처리 중";
            CurrentProcessingText.Text = stageName;
            CurrentProgressText.Text = $"{processedUnits:N0} / {totalUnits:N0} 항목 ({(processedUnits * 100.0 / Math.Max(totalUnits, 1)):F1}%)";
            
            System.Console.WriteLine($"[UpdateUnits] 현재 처리: {stageName}, 진행: {CurrentProgressText.Text}");
        }

        public void UpdateErrorCount(int errorCount)
        {
            _totalErrorCount = errorCount;
            TotalErrorsText.Text = errorCount.ToString("N0");
        }

        /// <summary>
        /// 부분 검수 결과를 업데이트합니다
        /// </summary>
        public void UpdatePartialResults(SpatialCheckPro.Models.ValidationResult? partialResult)
        {
            var logMsg = $"[UpdatePartialResults] 호출됨 - partialResult: {(partialResult != null ? "있음" : "null")}";
            System.Diagnostics.Debug.WriteLine(logMsg);
            System.Console.WriteLine(logMsg);
            
            if (partialResult == null)
            {
                PartialResultsPanel.Visibility = Visibility.Collapsed;
                var nullMsg = "[UpdatePartialResults] partialResult가 null이므로 패널 숨김";
                System.Diagnostics.Debug.WriteLine(nullMsg);
                System.Console.WriteLine(nullMsg);
                return;
            }

            try
            {
                var startMsg = $"[UpdatePartialResults] 부분 결과 처리 시작 - ErrorCount: {partialResult.ErrorCount}";
                System.Diagnostics.Debug.WriteLine(startMsg);
                System.Console.WriteLine(startMsg);
                var stageResults = new System.Collections.Generic.List<StageResultSummary>();

                // 완료된 단계별 오류 개수 수집
                if (partialResult.TableCheckResult != null)
                {
                    var msg = $"[UpdatePartialResults] 1단계 테이블: {partialResult.TableCheckResult.ErrorCount}개 오류";
                    System.Diagnostics.Debug.WriteLine(msg);
                    System.Console.WriteLine(msg);
                    stageResults.Add(new StageResultSummary
                    {
                        StageName = "1단계: 테이블",
                        ErrorCount = partialResult.TableCheckResult.ErrorCount
                    });
                }

                if (partialResult.SchemaCheckResult != null)
                {
                    stageResults.Add(new StageResultSummary
                    {
                        StageName = "2단계: 스키마",
                        ErrorCount = partialResult.SchemaCheckResult.ErrorCount
                    });
                }

                if (partialResult.GeometryCheckResult != null)
                {
                    stageResults.Add(new StageResultSummary
                    {
                        StageName = "3단계: 지오메트리",
                        ErrorCount = partialResult.GeometryCheckResult.ErrorCount
                    });
                }

                if (partialResult.AttributeRelationCheckResult != null)
                {
                    stageResults.Add(new StageResultSummary
                    {
                        StageName = "4단계: 속성관계",
                        ErrorCount = partialResult.AttributeRelationCheckResult.ErrorCount
                    });
                }

                if (partialResult.RelationCheckResult != null)
                {
                    stageResults.Add(new StageResultSummary
                    {
                        StageName = "5단계: 공간관계",
                        ErrorCount = partialResult.RelationCheckResult.ErrorCount
                    });
                }

                // 결과가 있으면 표시
                if (stageResults.Any())
                {
                    var msg1 = $"[UpdatePartialResults] {stageResults.Count}개 단계 결과 표시 준비";
                    System.Diagnostics.Debug.WriteLine(msg1);
                    System.Console.WriteLine(msg1);
                    
                    StageResultsList.ItemsSource = stageResults;
                    CumulativeErrorCountText.Text = partialResult.ErrorCount.ToString("N0");
                    
                    // 상단 "발견된 오류" 카운터도 함께 업데이트
                    TotalErrorsText.Text = partialResult.ErrorCount.ToString("N0");
                    _totalErrorCount = partialResult.ErrorCount;
                    
                    PartialResultsPanel.Visibility = Visibility.Visible;
                    
                    var msg2 = $"[UpdatePartialResults] ✅ PartialResultsPanel 표시됨! Visibility={PartialResultsPanel.Visibility}";
                    System.Diagnostics.Debug.WriteLine(msg2);
                    System.Console.WriteLine(msg2);
                }
                else
                {
                    PartialResultsPanel.Visibility = Visibility.Collapsed;
                    var msg = $"[UpdatePartialResults] ⚠️ stageResults가 비어있어 패널 숨김 (Count={stageResults.Count})";
                    System.Diagnostics.Debug.WriteLine(msg);
                    System.Console.WriteLine(msg);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdatePartialResults] 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdatePartialResults] StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 단계별 결과 요약 (내부 클래스)
        /// </summary>
        public class StageResultSummary
        {
            public string StageName { get; set; } = string.Empty;
            public int ErrorCount { get; set; }
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
            }
        }

        private void UpdateRemainingTimeDisplay()
        {
            if (_remainingTimeViewModel != null)
            {
                EstimatedTimeText.Text = _remainingTimeViewModel.DisplayText;
                
                // 초과 시 빨간색으로 표시
                if (_remainingTimeViewModel.IsOverdue)
                {
                    EstimatedTimeText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #EF4444
                }
                else
                {
                    EstimatedTimeText.Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)); // #1F2937
                }
                
                // 추가 정보 업데이트
                EstimatedEndTimeText.Text = _remainingTimeViewModel.EstimatedEndTimeText;
                SpeedIndicatorText.Text = _remainingTimeViewModel.SpeedIndicatorText;
                
                // 속도에 따른 색상 변경
                if (_remainingTimeViewModel.SpeedRatio < 0.8)
                {
                    SpeedIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 느림 - 빨간색
                }
                else if (_remainingTimeViewModel.SpeedRatio > 1.2)
                {
                    SpeedIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // 빠름 - 초록색
                }
                else
                {
                    SpeedIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // 정상 - 회색
                }
                
                // 남은 작업량은 외부에서 설정
                if (!string.IsNullOrEmpty(_remainingTimeViewModel.RemainingWorkText))
                {
                    RemainingWorkText.Text = _remainingTimeViewModel.RemainingWorkText;
                }
            }
        }

        public void UpdateElapsedTime(TimeSpan elapsed)
        {
            ElapsedTimeText.Text = elapsed.ToString("hh\\:mm\\:ss");
            
            // 남은 시간도 함께 업데이트
            UpdateRemainingTimeDisplay();
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

        private string FormatTime(double seconds)
        {
            if (seconds < 1) return "1초 이내";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            return $"{Math.Ceiling(ts.TotalSeconds):0}초";
        }
    }
}
