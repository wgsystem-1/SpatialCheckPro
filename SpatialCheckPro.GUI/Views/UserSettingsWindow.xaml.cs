using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 사용자 설정 및 커스터마이징 창
    /// Requirements: 사용성 개선 - 클릭 허용 거리, 심볼 크기 및 색상 커스터마이징, 설정 저장 및 복원
    /// </summary>
    public partial class UserSettingsWindow : Window
    {
        private readonly ILogger<UserSettingsWindow> _logger;
        private UserSettings _originalSettings;
        private UserSettings _currentSettings;
        private bool _isUpdatingUI = false;

        /// <summary>
        /// 설정이 변경되었는지 여부
        /// </summary>
        public bool SettingsChanged { get; private set; } = false;

        /// <summary>
        /// 현재 설정
        /// </summary>
        public UserSettings Settings => _currentSettings;

        public UserSettingsWindow(UserSettings currentSettings)
        {
            InitializeComponent();
            
            // 로거 초기화
            var loggerFactory = LoggerFactory.Create(builder => { /* 콘솔 로거 제거 */ });
            _logger = loggerFactory.CreateLogger<UserSettingsWindow>();
            
            // 설정 복사 (원본 보존)
            _originalSettings = currentSettings.Clone();
            _currentSettings = currentSettings.Clone();
            
            // UI 초기화
            InitializeUI();
            
            _logger.LogInformation("사용자 설정 창 초기화 완료");
        }

        #region UI 초기화

        /// <summary>
        /// UI 초기화
        /// </summary>
        private void InitializeUI()
        {
            try
            {
                _isUpdatingUI = true;
                
                // 지도 상호작용 설정
                ClickToleranceSlider.Value = _currentSettings.ClickTolerance;
                HighlightDurationSlider.Value = _currentSettings.HighlightDuration;
                AnimationDurationSlider.Value = _currentSettings.AnimationDuration;
                
                // 심볼 및 색상 설정
                SymbolSizeSlider.Value = _currentSettings.SymbolSize;
                SelectedSymbolSizeSlider.Value = _currentSettings.SelectedSymbolSize;
                SymbolTransparencySlider.Value = _currentSettings.SymbolTransparency;
                
                // 심각도별 색상
                CritColorTextBox.Text = _currentSettings.SeverityColors["CRIT"];
                MajorColorTextBox.Text = _currentSettings.SeverityColors["MAJOR"];
                MinorColorTextBox.Text = _currentSettings.SeverityColors["MINOR"];
                InfoColorTextBox.Text = _currentSettings.SeverityColors["INFO"];
                
                // 클러스터링 설정
                EnableClusteringCheckBox.IsChecked = _currentSettings.EnableClustering;
                ClusterToleranceSlider.Value = _currentSettings.ClusterTolerance;
                MinClusterSizeSlider.Value = _currentSettings.MinClusterSize;
                
                // 성능 설정
                MaxRenderCountSlider.Value = _currentSettings.MaxRenderCount;
                EnableProgressiveRenderingCheckBox.IsChecked = _currentSettings.EnableProgressiveRendering;
                
                // UI 설정
                ShowTooltipsCheckBox.IsChecked = _currentSettings.ShowTooltips;
                EnableKeyboardShortcutsCheckBox.IsChecked = _currentSettings.EnableKeyboardShortcuts;
                
                // 언어 설정
                foreach (ComboBoxItem item in LanguageComboBox.Items)
                {
                    if (item.Tag?.ToString() == _currentSettings.Language)
                    {
                        LanguageComboBox.SelectedItem = item;
                        break;
                    }
                }
                
                // 색상 버튼 업데이트
                UpdateColorButtons();
                
                // 텍스트 업데이트
                UpdateDisplayTexts();
                
                _isUpdatingUI = false;
                
                _logger.LogDebug("UI 초기화 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI 초기화 실패");
                _isUpdatingUI = false;
            }
        }

        /// <summary>
        /// 색상 버튼 업데이트
        /// </summary>
        private void UpdateColorButtons()
        {
            try
            {
                CritColorButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_currentSettings.SeverityColors["CRIT"]));
                MajorColorButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_currentSettings.SeverityColors["MAJOR"]));
                MinorColorButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_currentSettings.SeverityColors["MINOR"]));
                InfoColorButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_currentSettings.SeverityColors["INFO"]));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "색상 버튼 업데이트 실패");
            }
        }

        /// <summary>
        /// 표시 텍스트 업데이트
        /// </summary>
        private void UpdateDisplayTexts()
        {
            try
            {
                ClickToleranceText.Text = $"{_currentSettings.ClickTolerance:F0} 미터";
                HighlightDurationText.Text = $"{_currentSettings.HighlightDuration:F1} 초";
                AnimationDurationText.Text = $"{_currentSettings.AnimationDuration:F1} 초";
                SymbolSizeText.Text = $"{_currentSettings.SymbolSize:F0} 픽셀";
                SymbolTransparencyText.Text = $"{_currentSettings.SymbolTransparency * 100:F0}%";
                ClusterToleranceText.Text = $"{_currentSettings.ClusterTolerance:F0} 미터";
                MinClusterSizeText.Text = $"{_currentSettings.MinClusterSize} 개";
                MaxRenderCountText.Text = $"{_currentSettings.MaxRenderCount:N0} 개";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "표시 텍스트 업데이트 실패");
            }
        }

        #endregion

        #region 이벤트 핸들러

        /// <summary>
        /// 클릭 허용 거리 슬라이더 변경
        /// </summary>
        private void ClickToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.ClickTolerance = e.NewValue;
            ClickToleranceText.Text = $"{e.NewValue:F0} 미터";
            MarkAsChanged();
        }

        /// <summary>
        /// 하이라이트 지속 시간 슬라이더 변경
        /// </summary>
        private void HighlightDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.HighlightDuration = e.NewValue;
            HighlightDurationText.Text = $"{e.NewValue:F1} 초";
            MarkAsChanged();
        }

        /// <summary>
        /// 애니메이션 지속 시간 슬라이더 변경
        /// </summary>
        private void AnimationDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.AnimationDuration = e.NewValue;
            AnimationDurationText.Text = $"{e.NewValue:F1} 초";
            MarkAsChanged();
        }

        /// <summary>
        /// 심볼 크기 슬라이더 변경
        /// </summary>
        private void SymbolSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.SymbolSize = e.NewValue;
            SymbolSizeText.Text = $"{e.NewValue:F0} 픽셀";
            MarkAsChanged();
        }

        /// <summary>
        /// 선택된 심볼 크기 슬라이더 변경
        /// </summary>
        private void SelectedSymbolSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.SelectedSymbolSize = e.NewValue;
            MarkAsChanged();
        }

        /// <summary>
        /// 심볼 투명도 슬라이더 변경
        /// </summary>
        private void SymbolTransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.SymbolTransparency = e.NewValue;
            SymbolTransparencyText.Text = $"{e.NewValue * 100:F0}%";
            MarkAsChanged();
        }

        /// <summary>
        /// 클러스터링 활성화 체크박스 변경
        /// </summary>
        private void EnableClusteringCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.EnableClustering = EnableClusteringCheckBox.IsChecked == true;
            MarkAsChanged();
        }

        /// <summary>
        /// 클러스터링 허용 거리 슬라이더 변경
        /// </summary>
        private void ClusterToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.ClusterTolerance = e.NewValue;
            ClusterToleranceText.Text = $"{e.NewValue:F0} 미터";
            MarkAsChanged();
        }

        /// <summary>
        /// 최소 클러스터 크기 슬라이더 변경
        /// </summary>
        private void MinClusterSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.MinClusterSize = (int)e.NewValue;
            MinClusterSizeText.Text = $"{(int)e.NewValue} 개";
            MarkAsChanged();
        }

        /// <summary>
        /// 최대 렌더링 개수 슬라이더 변경
        /// </summary>
        private void MaxRenderCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUI) return;
            
            _currentSettings.MaxRenderCount = (int)e.NewValue;
            MaxRenderCountText.Text = $"{(int)e.NewValue:N0} 개";
            MarkAsChanged();
        }

        /// <summary>
        /// 색상 버튼 클릭
        /// </summary>
        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string severity)
                {
                    // TODO: 색상 선택 대화상자 구현
                    // 임시로 메시지 박스 표시
                    MessageBox.Show($"{severity} 색상 선택 기능은 향후 구현 예정입니다.", "색상 선택", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    _logger.LogInformation("색상 선택 요청: {Severity}", severity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "색상 버튼 클릭 처리 실패");
            }
        }

        /// <summary>
        /// 기본값 복원 버튼 클릭
        /// </summary>
        private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "모든 설정을 기본값으로 복원하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
                    "기본값 복원 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _currentSettings.SetDefaults();
                    InitializeUI();
                    MarkAsChanged();
                    
                    _logger.LogInformation("설정을 기본값으로 복원");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본값 복원 실패");
                MessageBox.Show("기본값 복원 중 오류가 발생했습니다.", "복원 실패", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 취소 버튼 클릭
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsChanged)
                {
                    var result = MessageBox.Show(
                        "변경된 설정이 있습니다. 저장하지 않고 닫으시겠습니까?",
                        "변경사항 확인",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                SettingsChanged = false;
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "취소 버튼 클릭 처리 실패");
            }
        }

        /// <summary>
        /// 적용 버튼 클릭
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApplySettings())
                {
                    SettingsChanged = false;
                    MessageBox.Show("설정이 적용되었습니다.", "적용 완료", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "적용 버튼 클릭 처리 실패");
                MessageBox.Show("설정 적용 중 오류가 발생했습니다.", "적용 실패", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 확인 버튼 클릭
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApplySettings())
                {
                    SettingsChanged = true;
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "확인 버튼 클릭 처리 실패");
                MessageBox.Show("설정 저장 중 오류가 발생했습니다.", "저장 실패", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 내부 메서드

        /// <summary>
        /// 설정 변경 표시
        /// </summary>
        private void MarkAsChanged()
        {
            if (!_isUpdatingUI)
            {
                SettingsChanged = true;
                Title = "사용자 설정 *"; // 변경 표시
            }
        }

        /// <summary>
        /// 설정 적용
        /// </summary>
        private bool ApplySettings()
        {
            try
            {
                // UI에서 색상 설정 수집
                CollectColorSettings();
                
                // 언어 설정 수집
                if (LanguageComboBox.SelectedItem is ComboBoxItem selectedLanguage)
                {
                    _currentSettings.Language = selectedLanguage.Tag?.ToString() ?? "ko-KR";
                }
                
                // 체크박스 설정 수집
                _currentSettings.EnableProgressiveRendering = EnableProgressiveRenderingCheckBox.IsChecked == true;
                _currentSettings.ShowTooltips = ShowTooltipsCheckBox.IsChecked == true;
                _currentSettings.EnableKeyboardShortcuts = EnableKeyboardShortcutsCheckBox.IsChecked == true;
                
                // 설정 유효성 검사
                if (!_currentSettings.Validate())
                {
                    MessageBox.Show("설정값이 유효하지 않습니다. 올바른 값을 입력해주세요.", "유효성 검사 실패", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                // 설정 저장
                if (_currentSettings.Save(_logger))
                {
                    _logger.LogInformation("사용자 설정 적용 및 저장 완료");
                    return true;
                }
                else
                {
                    MessageBox.Show("설정 파일 저장에 실패했습니다.", "저장 실패", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "설정 적용 실패");
                return false;
            }
        }

        /// <summary>
        /// UI에서 색상 설정 수집
        /// </summary>
        private void CollectColorSettings()
        {
            try
            {
                // 심각도별 색상
                if (IsValidColor(CritColorTextBox.Text))
                    _currentSettings.SeverityColors["CRIT"] = CritColorTextBox.Text;
                
                if (IsValidColor(MajorColorTextBox.Text))
                    _currentSettings.SeverityColors["MAJOR"] = MajorColorTextBox.Text;
                
                if (IsValidColor(MinorColorTextBox.Text))
                    _currentSettings.SeverityColors["MINOR"] = MinorColorTextBox.Text;
                
                if (IsValidColor(InfoColorTextBox.Text))
                    _currentSettings.SeverityColors["INFO"] = InfoColorTextBox.Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "색상 설정 수집 실패");
            }
        }

        /// <summary>
        /// 유효한 색상 코드인지 확인
        /// </summary>
        private bool IsValidColor(string colorString)
        {
            try
            {
                ColorConverter.ConvertFromString(colorString);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}