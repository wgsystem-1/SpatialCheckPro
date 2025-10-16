using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Services;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 다중 오류 일괄 상태 변경 창
    /// Requirements: 7.5 - 선택된 여러 오류의 상태 일괄 변경
    /// </summary>
    public partial class BulkStatusChangeWindow : Window
    {
        private readonly ILogger<BulkStatusChangeWindow> _logger;
        private readonly ErrorTrackingService _errorTrackingService;
        private readonly List<QcError> _selectedErrors;
        private string _selectedStatus = string.Empty;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly Stopwatch _stopwatch = new();
        private bool _isProcessing = false;

        /// <summary>
        /// 일괄 변경 결과 정보
        /// </summary>
        public class BulkChangeResult
        {
            public int TotalCount { get; set; }
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public List<string> FailedErrorIds { get; set; } = new();
            public List<string> ErrorMessages { get; set; } = new();
            public bool WasCancelled { get; set; }
        }

        /// <summary>
        /// 일괄 변경 결과
        /// </summary>
        public BulkChangeResult? Result { get; private set; }

        public BulkStatusChangeWindow(List<QcError> selectedErrors, ErrorTrackingService errorTrackingService)
        {
            InitializeComponent();
            
            // 로거 초기화
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<BulkStatusChangeWindow>();
            
            _selectedErrors = selectedErrors ?? throw new ArgumentNullException(nameof(selectedErrors));
            _errorTrackingService = errorTrackingService ?? throw new ArgumentNullException(nameof(errorTrackingService));
            
            InitializeWindow();
            
            _logger.LogInformation("일괄 상태 변경 창 초기화: {Count}개 오류 선택됨", _selectedErrors.Count);
        }

        /// <summary>
        /// 창 초기화
        /// </summary>
        private void InitializeWindow()
        {
            try
            {
                // 선택된 오류 개수 표시
                SelectedCountText.Text = $"{_selectedErrors.Count}개";
                
                // 예상 소요 시간 계산 (오류당 평균 100ms 가정)
                var estimatedSeconds = (_selectedErrors.Count * 0.1);
                EstimatedTimeText.Text = estimatedSeconds < 1 
                    ? "1초 미만" 
                    : $"약 {estimatedSeconds:F1}초";
                
                // 상태별 오류 개수 표시 (툴팁으로)
                UpdateStatusButtonTooltips();
                
                _logger.LogDebug("창 초기화 완료: 예상 소요 시간 {EstimatedTime}초", estimatedSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "창 초기화 실패");
                MessageBox.Show("창 초기화 중 오류가 발생했습니다.", "초기화 오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 상태 버튼 툴팁 업데이트
        /// </summary>
        private void UpdateStatusButtonTooltips()
        {
            try
            {
                var statusCounts = _selectedErrors.GroupBy(e => e.Status)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                OpenStatusButton.ToolTip = $"열림으로 변경 (현재 {statusCounts.GetValueOrDefault("OPEN", 0)}개)";
                FixedStatusButton.ToolTip = $"수정됨으로 변경 (현재 {statusCounts.GetValueOrDefault("FIXED", 0)}개)";
                IgnoredStatusButton.ToolTip = $"무시됨으로 변경 (현재 {statusCounts.GetValueOrDefault("IGNORED", 0)}개)";
                FalsePosStatusButton.ToolTip = $"오탐으로 변경 (현재 {statusCounts.GetValueOrDefault("FALSE_POS", 0)}개)";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "툴팁 업데이트 실패");
            }
        }

        #region 이벤트 핸들러

        /// <summary>
        /// 상태 버튼 클릭 이벤트
        /// </summary>
        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string status)
                {
                    // 이전 선택 해제
                    ResetStatusButtonStyles();
                    
                    // 새로운 선택 적용
                    _selectedStatus = status;
                    button.BorderThickness = new Thickness(3);
                    button.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Blue,
                        Direction = 0,
                        ShadowDepth = 0,
                        BlurRadius = 10
                    };
                    
                    // 시작 버튼 활성화
                    StartButton.IsEnabled = true;
                    StatusText.Text = $"{GetStatusDisplayName(status)}로 변경할 준비가 완료되었습니다";
                    
                    _logger.LogDebug("상태 선택됨: {Status}", status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "상태 버튼 클릭 처리 실패");
            }
        }

        /// <summary>
        /// 시작 버튼 클릭 이벤트
        /// </summary>
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedStatus))
                {
                    MessageBox.Show("변경할 상태를 선택해주세요.", "상태 미선택", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 확인 대화상자
                var result = MessageBox.Show(
                    $"선택된 {_selectedErrors.Count}개 오류의 상태를 '{GetStatusDisplayName(_selectedStatus)}'로 변경하시겠습니까?\n\n" +
                    "이 작업은 되돌릴 수 없습니다.",
                    "일괄 상태 변경 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                await StartBulkStatusChangeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "시작 버튼 클릭 처리 실패");
                MessageBox.Show("일괄 상태 변경 시작 중 오류가 발생했습니다.", "시작 오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 취소 버튼 클릭 이벤트
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isProcessing)
                {
                    // 진행 중인 작업 취소
                    _cancellationTokenSource?.Cancel();
                    StatusText.Text = "작업 취소 중...";
                    _logger.LogInformation("사용자가 일괄 상태 변경 작업 취소 요청");
                }
                else
                {
                    // 창 닫기
                    DialogResult = false;
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "취소 버튼 클릭 처리 실패");
            }
        }

        /// <summary>
        /// 닫기 버튼 클릭 이벤트
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "닫기 버튼 클릭 처리 실패");
            }
        }

        #endregion

        #region 일괄 상태 변경 로직

        /// <summary>
        /// 일괄 상태 변경 시작
        /// </summary>
        private async Task StartBulkStatusChangeAsync()
        {
            try
            {
                _isProcessing = true;
                _stopwatch.Start();
                _cancellationTokenSource = new CancellationTokenSource();

                // UI 상태 변경
                SetProcessingUI(true);
                
                _logger.LogInformation("일괄 상태 변경 시작: {Count}개 오류를 {Status}로 변경", 
                    _selectedErrors.Count, _selectedStatus);

                // 일괄 변경 수행
                var result = await PerformBulkStatusChangeAsync(_cancellationTokenSource.Token);
                
                _stopwatch.Stop();
                result.ElapsedTime = _stopwatch.Elapsed;
                Result = result;

                // 결과 표시
                ShowResult(result);
                
                _logger.LogInformation("일괄 상태 변경 완료: 성공 {Success}/{Total}, 소요시간 {Elapsed}", 
                    result.SuccessCount, result.TotalCount, result.ElapsedTime);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("일괄 상태 변경이 사용자에 의해 취소됨");
                ShowCancelledResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "일괄 상태 변경 실패");
                ShowErrorResult(ex);
            }
            finally
            {
                _isProcessing = false;
                _stopwatch.Stop();
                SetProcessingUI(false);
            }
        }

        /// <summary>
        /// 일괄 상태 변경 수행
        /// </summary>
        private async Task<BulkChangeResult> PerformBulkStatusChangeAsync(CancellationToken cancellationToken)
        {
            var result = new BulkChangeResult
            {
                TotalCount = _selectedErrors.Count
            };

            var processedCount = 0;
            var batchSize = Math.Max(1, _selectedErrors.Count / 20); // 최대 20개 배치로 나누어 처리

            try
            {
                // 배치별로 처리하여 진행률 표시 및 취소 기능 제공
                for (int i = 0; i < _selectedErrors.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = _selectedErrors.Skip(i).Take(batchSize).ToList();
                    var batchIds = batch.Select(e => e.GlobalID).ToList();

                    // 현재 배치 정보 표시
                    var batchStart = i + 1;
                    var batchEnd = Math.Min(i + batchSize, _selectedErrors.Count);
                    
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CurrentTaskText.Text = $"처리 중: {batchStart}-{batchEnd}번째 오류들...";
                        var progress = (double)processedCount / _selectedErrors.Count * 100;
                        OverallProgressBar.Value = progress;
                        ProgressText.Text = $"진행률: {processedCount}/{_selectedErrors.Count} ({progress:F1}%)";
                    });

                    try
                    {
                        // ErrorTrackingService를 통한 일괄 업데이트
                        var batchSuccess = await _errorTrackingService.UpdateMultipleErrorsAsync(batchIds, _selectedStatus);
                        
                        if (batchSuccess)
                        {
                            result.SuccessCount += batch.Count;
                            
                            // 로그 추가
                            await Dispatcher.InvokeAsync(() =>
                            {
                                AppendLog($"✅ 배치 {batchStart}-{batchEnd}: {batch.Count}개 성공");
                            });
                        }
                        else
                        {
                            result.FailureCount += batch.Count;
                            result.FailedErrorIds.AddRange(batchIds);
                            result.ErrorMessages.Add($"배치 {batchStart}-{batchEnd} 처리 실패");
                            
                            // 로그 추가
                            await Dispatcher.InvokeAsync(() =>
                            {
                                AppendLog($"❌ 배치 {batchStart}-{batchEnd}: {batch.Count}개 실패");
                            });
                        }
                    }
                    catch (Exception batchEx)
                    {
                        _logger.LogError(batchEx, "배치 처리 실패: {BatchStart}-{BatchEnd}", batchStart, batchEnd);
                        
                        result.FailureCount += batch.Count;
                        result.FailedErrorIds.AddRange(batchIds);
                        result.ErrorMessages.Add($"배치 {batchStart}-{batchEnd}: {batchEx.Message}");
                        
                        // 로그 추가
                        await Dispatcher.InvokeAsync(() =>
                        {
                            AppendLog($"❌ 배치 {batchStart}-{batchEnd}: 오류 - {batchEx.Message}");
                        });
                    }

                    processedCount += batch.Count;

                    // 진행률 업데이트
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var progress = (double)processedCount / _selectedErrors.Count * 100;
                        OverallProgressBar.Value = progress;
                        ProgressText.Text = $"진행률: {processedCount}/{_selectedErrors.Count} ({progress:F1}%)";
                        
                        // 경과 시간 표시
                        ElapsedTimeText.Text = $"경과 시간: {_stopwatch.Elapsed:mm\\:ss}";
                    });

                    // 배치 간 짧은 지연 (UI 응답성 및 시스템 부하 방지)
                    if (i + batchSize < _selectedErrors.Count)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                // 최종 진행률 업데이트
                await Dispatcher.InvokeAsync(() =>
                {
                    OverallProgressBar.Value = 100;
                    ProgressText.Text = $"완료: {processedCount}/{_selectedErrors.Count} (100%)";
                    CurrentTaskText.Text = "일괄 상태 변경 완료";
                });
            }
            catch (OperationCanceledException)
            {
                result.WasCancelled = true;
                result.FailureCount = _selectedErrors.Count - result.SuccessCount;
                throw;
            }

            return result;
        }

        #endregion

        #region UI 업데이트 메서드

        /// <summary>
        /// 처리 중 UI 상태 설정
        /// </summary>
        private void SetProcessingUI(bool isProcessing)
        {
            try
            {
                if (isProcessing)
                {
                    // 진행 상황 표시
                    ProgressGroupBox.Visibility = Visibility.Visible;
                    
                    // 버튼 상태 변경
                    StartButton.Visibility = Visibility.Collapsed;
                    CancelButton.Content = "⏹️ 중단";
                    CloseButton.Visibility = Visibility.Collapsed;
                    
                    // 상태 버튼 비활성화
                    OpenStatusButton.IsEnabled = false;
                    FixedStatusButton.IsEnabled = false;
                    IgnoredStatusButton.IsEnabled = false;
                    FalsePosStatusButton.IsEnabled = false;
                    
                    StatusText.Text = "일괄 상태 변경 진행 중...";
                }
                else
                {
                    // 버튼 상태 복원
                    CancelButton.Content = "❌ 취소";
                    CloseButton.Visibility = Visibility.Visible;
                    
                    // 상태 버튼 활성화
                    OpenStatusButton.IsEnabled = true;
                    FixedStatusButton.IsEnabled = true;
                    IgnoredStatusButton.IsEnabled = true;
                    FalsePosStatusButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI 상태 설정 실패");
            }
        }

        /// <summary>
        /// 상태 버튼 스타일 초기화
        /// </summary>
        private void ResetStatusButtonStyles()
        {
            try
            {
                var buttons = new[] { OpenStatusButton, FixedStatusButton, IgnoredStatusButton, FalsePosStatusButton };
                
                foreach (var button in buttons)
                {
                    button.BorderThickness = new Thickness(2);
                    button.Effect = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버튼 스타일 초기화 실패");
            }
        }

        /// <summary>
        /// 로그 메시지 추가
        /// </summary>
        private void AppendLog(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}\n";
                
                LogTextBlock.Text += logEntry;
                
                // 스크롤을 맨 아래로 이동
                if (LogTextBlock.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "로그 추가 실패");
            }
        }

        /// <summary>
        /// 성공 결과 표시
        /// </summary>
        private void ShowResult(BulkChangeResult result)
        {
            try
            {
                // 결과 요약 표시
                ResultSummaryBorder.Visibility = Visibility.Visible;
                
                if (result.FailureCount == 0)
                {
                    // 완전 성공
                    ResultSummaryBorder.Background = new SolidColorBrush(Color.FromRgb(232, 245, 232));
                    ResultSummaryBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    ResultSummaryText.Text = "✅ 일괄 상태 변경이 성공적으로 완료되었습니다!";
                    ResultSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                }
                else if (result.SuccessCount > 0)
                {
                    // 부분 성공
                    ResultSummaryBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 224));
                    ResultSummaryBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                    ResultSummaryText.Text = "⚠️ 일괄 상태 변경이 부분적으로 완료되었습니다";
                    ResultSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(230, 81, 0));
                }
                else
                {
                    // 완전 실패
                    ResultSummaryBorder.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                    ResultSummaryBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    ResultSummaryText.Text = "❌ 일괄 상태 변경에 실패했습니다";
                    ResultSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
                }
                
                ResultDetailsText.Text = $"성공: {result.SuccessCount}개, 실패: {result.FailureCount}개, " +
                                       $"소요 시간: {result.ElapsedTime.TotalSeconds:F1}초";
                
                StatusText.Text = $"일괄 상태 변경 완료 - 성공률: {(double)result.SuccessCount / result.TotalCount * 100:F1}%";
                
                // 최종 로그 추가
                AppendLog($"🏁 최종 결과: 성공 {result.SuccessCount}/{result.TotalCount}, " +
                         $"소요시간 {result.ElapsedTime.TotalSeconds:F1}초");
                
                if (result.FailureCount > 0)
                {
                    AppendLog($"⚠️ 실패한 오류 ID들: {string.Join(", ", result.FailedErrorIds.Take(10))}");
                    if (result.FailedErrorIds.Count > 10)
                    {
                        AppendLog($"   ... 및 {result.FailedErrorIds.Count - 10}개 더");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "결과 표시 실패");
            }
        }

        /// <summary>
        /// 취소 결과 표시
        /// </summary>
        private void ShowCancelledResult()
        {
            try
            {
                ResultSummaryBorder.Visibility = Visibility.Visible;
                ResultSummaryBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 224));
                ResultSummaryBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                
                ResultSummaryText.Text = "⏹️ 일괄 상태 변경이 취소되었습니다";
                ResultSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(230, 81, 0));
                ResultDetailsText.Text = $"소요 시간: {_stopwatch.Elapsed.TotalSeconds:F1}초";
                
                StatusText.Text = "작업이 사용자에 의해 취소되었습니다";
                AppendLog("⏹️ 작업이 사용자에 의해 취소되었습니다");
                
                Result = new BulkChangeResult
                {
                    TotalCount = _selectedErrors.Count,
                    WasCancelled = true,
                    ElapsedTime = _stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "취소 결과 표시 실패");
            }
        }

        /// <summary>
        /// 오류 결과 표시
        /// </summary>
        private void ShowErrorResult(Exception exception)
        {
            try
            {
                ResultSummaryBorder.Visibility = Visibility.Visible;
                ResultSummaryBorder.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                ResultSummaryBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                
                ResultSummaryText.Text = "❌ 일괄 상태 변경 중 오류가 발생했습니다";
                ResultSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
                ResultDetailsText.Text = $"오류: {exception.Message}";
                
                StatusText.Text = "일괄 상태 변경 중 오류 발생";
                AppendLog($"❌ 오류 발생: {exception.Message}");
                
                Result = new BulkChangeResult
                {
                    TotalCount = _selectedErrors.Count,
                    FailureCount = _selectedErrors.Count,
                    ElapsedTime = _stopwatch.Elapsed,
                    ErrorMessages = new List<string> { exception.Message }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 결과 표시 실패");
            }
        }

        #endregion

        #region 유틸리티 메서드

        /// <summary>
        /// 상태 코드를 표시명으로 변환
        /// </summary>
        private string GetStatusDisplayName(string status)
        {
            return status switch
            {
                "OPEN" => "열림",
                "FIXED" => "수정됨",
                "IGNORED" => "무시됨",
                "FALSE_POS" => "오탐",
                _ => status
            };
        }

        #endregion
    }
}