using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 고급 필터 및 검색 창
    /// Requirements: 사용성 개선 - 오류 유형별 표시/숨김 토글, 심각도별 필터링, 상태별 필터링
    /// </summary>
    public partial class AdvancedFilterWindow : Window
    {
        private readonly ILogger<AdvancedFilterWindow> _logger;
        private bool _isUpdatingSelectAll = false;

        /// <summary>
        /// 필터 설정 정보
        /// </summary>
        public class FilterSettings
        {
            // 텍스트 검색
            public string SearchText { get; set; } = string.Empty;
            public bool CaseSensitive { get; set; } = false;
            public bool UseRegex { get; set; } = false;

            // 오류 유형 필터
            public HashSet<string> SelectedErrorTypes { get; set; } = new();

            // 심각도 필터
            public HashSet<string> SelectedSeverities { get; set; } = new();

            // 상태 필터
            public HashSet<string> SelectedStatuses { get; set; } = new();

            // 날짜 범위 필터
            public bool EnableDateFilter { get; set; } = false;
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }

            // 위치 기반 필터
            public bool EnableLocationFilter { get; set; } = false;
            public double? MinX { get; set; }
            public double? MaxX { get; set; }
            public double? MinY { get; set; }
            public double? MaxY { get; set; }

            /// <summary>
            /// 기본 설정으로 초기화
            /// </summary>
            public void SetDefaults()
            {
                SearchText = string.Empty;
                CaseSensitive = false;
                UseRegex = false;

                SelectedErrorTypes.Clear();
                SelectedErrorTypes.Add("GEOM");
                SelectedErrorTypes.Add("SCHEMA");
                SelectedErrorTypes.Add("REL");
                SelectedErrorTypes.Add("ATTR");

                SelectedSeverities.Clear();
                SelectedSeverities.Add("CRIT");
                SelectedSeverities.Add("MAJOR");
                SelectedSeverities.Add("MINOR");
                SelectedSeverities.Add("INFO");

                SelectedStatuses.Clear();
                SelectedStatuses.Add("OPEN");
                SelectedStatuses.Add("FIXED");
                SelectedStatuses.Add("IGNORED");
                SelectedStatuses.Add("FALSE_POS");

                EnableDateFilter = false;
                StartDate = null;
                EndDate = null;

                EnableLocationFilter = false;
                MinX = null;
                MaxX = null;
                MinY = null;
                MaxY = null;
            }

            /// <summary>
            /// 오류가 필터 조건을 만족하는지 확인
            /// </summary>
            public bool MatchesFilter(QcError error)
            {
                try
                {
                    // 텍스트 검색 확인
                    if (!string.IsNullOrEmpty(SearchText))
                    {
                        var searchFields = new[]
                        {
                            error.Message ?? string.Empty,
                            error.ErrCode ?? string.Empty,
                            error.SourceClass ?? string.Empty,
                            error.UserFriendlyDescription ?? string.Empty
                        };

                        var searchText = CaseSensitive ? SearchText : SearchText.ToLower();
                        var found = false;

                        foreach (var field in searchFields)
                        {
                            var fieldText = CaseSensitive ? field : field.ToLower();

                            if (UseRegex)
                            {
                                try
                                {
                                    var regex = new Regex(searchText, CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                                    if (regex.IsMatch(fieldText))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                catch
                                {
                                    // 정규식 오류 시 일반 텍스트 검색으로 폴백
                                    if (fieldText.Contains(searchText))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (fieldText.Contains(searchText))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (!found) return false;
                    }

                    // 오류 유형 확인
                    if (SelectedErrorTypes.Any() && !SelectedErrorTypes.Contains(error.ErrType))
                        return false;

                    // 심각도 확인
                    if (SelectedSeverities.Any() && !SelectedSeverities.Contains(error.Severity))
                        return false;

                    // 상태 확인
                    if (SelectedStatuses.Any() && !SelectedStatuses.Contains(error.Status))
                        return false;

                    // 날짜 범위 확인
                    if (EnableDateFilter)
                    {
                        if (StartDate.HasValue && error.CreatedUTC < StartDate.Value)
                            return false;

                        if (EndDate.HasValue && error.CreatedUTC > EndDate.Value.AddDays(1))
                            return false;
                    }

                    // 위치 기반 확인
                    if (EnableLocationFilter)
                    {
                        if (MinX.HasValue && error.X < MinX.Value)
                            return false;

                        if (MaxX.HasValue && error.X > MaxX.Value)
                            return false;

                        if (MinY.HasValue && error.Y < MinY.Value)
                            return false;

                        if (MaxY.HasValue && error.Y > MaxY.Value)
                            return false;
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 현재 필터 설정
        /// </summary>
        public FilterSettings CurrentSettings { get; private set; } = new();

        /// <summary>
        /// 필터 적용 결과
        /// </summary>
        public bool FilterApplied { get; private set; } = false;

        public AdvancedFilterWindow()
        {
            InitializeComponent();
            
            // 로거 초기화
            var loggerFactory = LoggerFactory.Create(builder => { /* 콘솔 로거 제거 */ });
            _logger = loggerFactory.CreateLogger<AdvancedFilterWindow>();
            
            // 기본 설정 적용
            CurrentSettings.SetDefaults();
            ApplySettingsToUI();
            UpdateFilterSummary();
            
            _logger.LogInformation("고급 필터 창 초기화 완료");
        }

        /// <summary>
        /// 기존 필터 설정으로 초기화
        /// </summary>
        public AdvancedFilterWindow(FilterSettings existingSettings) : this()
        {
            if (existingSettings != null)
            {
                CurrentSettings = existingSettings;
                ApplySettingsToUI();
                UpdateFilterSummary();
                
                _logger.LogInformation("기존 필터 설정으로 초기화");
            }
        }

        #region UI 이벤트 핸들러

        /// <summary>
        /// 검색 텍스트 변경 이벤트
        /// </summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CurrentSettings.SearchText = SearchTextBox.Text;
            UpdateFilterSummary();
        }

        /// <summary>
        /// 오류 유형 전체 선택/해제
        /// </summary>
        private void SelectAllErrorTypes_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            GeomErrorTypeCheckBox.IsChecked = true;
            SchemaErrorTypeCheckBox.IsChecked = true;
            RelErrorTypeCheckBox.IsChecked = true;
            AttrErrorTypeCheckBox.IsChecked = true;
            
            UpdateErrorTypeFilter();
        }

        private void SelectAllErrorTypes_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            GeomErrorTypeCheckBox.IsChecked = false;
            SchemaErrorTypeCheckBox.IsChecked = false;
            RelErrorTypeCheckBox.IsChecked = false;
            AttrErrorTypeCheckBox.IsChecked = false;
            
            UpdateErrorTypeFilter();
        }

        /// <summary>
        /// 심각도 전체 선택/해제
        /// </summary>
        private void SelectAllSeverities_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            CritSeverityCheckBox.IsChecked = true;
            MajorSeverityCheckBox.IsChecked = true;
            MinorSeverityCheckBox.IsChecked = true;
            InfoSeverityCheckBox.IsChecked = true;
            
            UpdateSeverityFilter();
        }

        private void SelectAllSeverities_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            CritSeverityCheckBox.IsChecked = false;
            MajorSeverityCheckBox.IsChecked = false;
            MinorSeverityCheckBox.IsChecked = false;
            InfoSeverityCheckBox.IsChecked = false;
            
            UpdateSeverityFilter();
        }

        /// <summary>
        /// 상태 전체 선택/해제
        /// </summary>
        private void SelectAllStatuses_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            OpenStatusCheckBox.IsChecked = true;
            FixedStatusCheckBox.IsChecked = true;
            IgnoredStatusCheckBox.IsChecked = true;
            FalsePosStatusCheckBox.IsChecked = true;
            
            UpdateStatusFilter();
        }

        private void SelectAllStatuses_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;
            
            OpenStatusCheckBox.IsChecked = false;
            FixedStatusCheckBox.IsChecked = false;
            IgnoredStatusCheckBox.IsChecked = false;
            FalsePosStatusCheckBox.IsChecked = false;
            
            UpdateStatusFilter();
        }

        /// <summary>
        /// 날짜 필터 활성화/비활성화
        /// </summary>
        private void EnableDateFilter_Checked(object sender, RoutedEventArgs e)
        {
            StartDatePicker.IsEnabled = true;
            EndDatePicker.IsEnabled = true;
            CurrentSettings.EnableDateFilter = true;
            
            // 기본값 설정
            if (!StartDatePicker.SelectedDate.HasValue)
                StartDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
            if (!EndDatePicker.SelectedDate.HasValue)
                EndDatePicker.SelectedDate = DateTime.Today;
                
            UpdateFilterSummary();
        }

        private void EnableDateFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            StartDatePicker.IsEnabled = false;
            EndDatePicker.IsEnabled = false;
            CurrentSettings.EnableDateFilter = false;
            UpdateFilterSummary();
        }

        /// <summary>
        /// 위치 필터 활성화/비활성화
        /// </summary>
        private void EnableLocationFilter_Checked(object sender, RoutedEventArgs e)
        {
            LocationFilterGrid.IsEnabled = true;
            CurrentSettings.EnableLocationFilter = true;
            UpdateFilterSummary();
        }

        private void EnableLocationFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            LocationFilterGrid.IsEnabled = false;
            CurrentSettings.EnableLocationFilter = false;
            UpdateFilterSummary();
        }

        /// <summary>
        /// 현재 지도 범위 사용 버튼
        /// </summary>
        private void UseCurrentMapExtent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: 실제 지도 서비스에서 현재 범위 가져오기
                // 임시로 샘플 값 설정
                MinXTextBox.Text = "200000";
                MaxXTextBox.Text = "300000";
                MinYTextBox.Text = "400000";
                MaxYTextBox.Text = "500000";
                
                MessageBox.Show("현재 지도 범위가 적용되었습니다.", "범위 적용", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateLocationFilter();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "현재 지도 범위 가져오기 실패");
                MessageBox.Show("현재 지도 범위를 가져올 수 없습니다.", "범위 가져오기 실패", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 초기화 버튼
        /// </summary>
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CurrentSettings.SetDefaults();
                ApplySettingsToUI();
                UpdateFilterSummary();
                
                _logger.LogInformation("필터 설정 초기화");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필터 초기화 실패");
            }
        }

        /// <summary>
        /// 취소 버튼
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            FilterApplied = false;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 적용 버튼
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI에서 현재 설정 수집
                CollectSettingsFromUI();
                
                FilterApplied = true;
                DialogResult = true;
                
                _logger.LogInformation("필터 설정 적용");
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필터 적용 실패");
                MessageBox.Show("필터 설정 적용 중 오류가 발생했습니다.", "적용 실패", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 저장 버튼
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: 필터 설정을 파일로 저장하는 기능 구현
                MessageBox.Show("필터 설정 저장 기능은 향후 구현 예정입니다.", "저장", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                _logger.LogInformation("필터 설정 저장 요청");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필터 저장 실패");
            }
        }

        #endregion

        #region 내부 메서드

        /// <summary>
        /// 설정을 UI에 적용
        /// </summary>
        private void ApplySettingsToUI()
        {
            try
            {
                _isUpdatingSelectAll = true;

                // 텍스트 검색
                SearchTextBox.Text = CurrentSettings.SearchText;
                CaseSensitiveCheckBox.IsChecked = CurrentSettings.CaseSensitive;
                RegexSearchCheckBox.IsChecked = CurrentSettings.UseRegex;

                // 오류 유형
                GeomErrorTypeCheckBox.IsChecked = CurrentSettings.SelectedErrorTypes.Contains("GEOM");
                SchemaErrorTypeCheckBox.IsChecked = CurrentSettings.SelectedErrorTypes.Contains("SCHEMA");
                RelErrorTypeCheckBox.IsChecked = CurrentSettings.SelectedErrorTypes.Contains("REL");
                AttrErrorTypeCheckBox.IsChecked = CurrentSettings.SelectedErrorTypes.Contains("ATTR");

                // 심각도
                CritSeverityCheckBox.IsChecked = CurrentSettings.SelectedSeverities.Contains("CRIT");
                MajorSeverityCheckBox.IsChecked = CurrentSettings.SelectedSeverities.Contains("MAJOR");
                MinorSeverityCheckBox.IsChecked = CurrentSettings.SelectedSeverities.Contains("MINOR");
                InfoSeverityCheckBox.IsChecked = CurrentSettings.SelectedSeverities.Contains("INFO");

                // 상태
                OpenStatusCheckBox.IsChecked = CurrentSettings.SelectedStatuses.Contains("OPEN");
                FixedStatusCheckBox.IsChecked = CurrentSettings.SelectedStatuses.Contains("FIXED");
                IgnoredStatusCheckBox.IsChecked = CurrentSettings.SelectedStatuses.Contains("IGNORED");
                FalsePosStatusCheckBox.IsChecked = CurrentSettings.SelectedStatuses.Contains("FALSE_POS");

                // 날짜 필터
                EnableDateFilterCheckBox.IsChecked = CurrentSettings.EnableDateFilter;
                StartDatePicker.SelectedDate = CurrentSettings.StartDate;
                EndDatePicker.SelectedDate = CurrentSettings.EndDate;
                StartDatePicker.IsEnabled = CurrentSettings.EnableDateFilter;
                EndDatePicker.IsEnabled = CurrentSettings.EnableDateFilter;

                // 위치 필터
                EnableLocationFilterCheckBox.IsChecked = CurrentSettings.EnableLocationFilter;
                LocationFilterGrid.IsEnabled = CurrentSettings.EnableLocationFilter;
                MinXTextBox.Text = CurrentSettings.MinX?.ToString() ?? string.Empty;
                MaxXTextBox.Text = CurrentSettings.MaxX?.ToString() ?? string.Empty;
                MinYTextBox.Text = CurrentSettings.MinY?.ToString() ?? string.Empty;
                MaxYTextBox.Text = CurrentSettings.MaxY?.ToString() ?? string.Empty;

                _isUpdatingSelectAll = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI 설정 적용 실패");
            }
        }

        /// <summary>
        /// UI에서 설정 수집
        /// </summary>
        private void CollectSettingsFromUI()
        {
            try
            {
                // 텍스트 검색
                CurrentSettings.SearchText = SearchTextBox.Text;
                CurrentSettings.CaseSensitive = CaseSensitiveCheckBox.IsChecked == true;
                CurrentSettings.UseRegex = RegexSearchCheckBox.IsChecked == true;

                // 오류 유형
                UpdateErrorTypeFilter();

                // 심각도
                UpdateSeverityFilter();

                // 상태
                UpdateStatusFilter();

                // 날짜 필터
                CurrentSettings.EnableDateFilter = EnableDateFilterCheckBox.IsChecked == true;
                CurrentSettings.StartDate = StartDatePicker.SelectedDate;
                CurrentSettings.EndDate = EndDatePicker.SelectedDate;

                // 위치 필터
                UpdateLocationFilter();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI 설정 수집 실패");
            }
        }

        /// <summary>
        /// 오류 유형 필터 업데이트
        /// </summary>
        private void UpdateErrorTypeFilter()
        {
            CurrentSettings.SelectedErrorTypes.Clear();
            
            if (GeomErrorTypeCheckBox.IsChecked == true)
                CurrentSettings.SelectedErrorTypes.Add("GEOM");
            if (SchemaErrorTypeCheckBox.IsChecked == true)
                CurrentSettings.SelectedErrorTypes.Add("SCHEMA");
            if (RelErrorTypeCheckBox.IsChecked == true)
                CurrentSettings.SelectedErrorTypes.Add("REL");
            if (AttrErrorTypeCheckBox.IsChecked == true)
                CurrentSettings.SelectedErrorTypes.Add("ATTR");
                
            UpdateFilterSummary();
        }

        /// <summary>
        /// 심각도 필터 업데이트
        /// </summary>
        private void UpdateSeverityFilter()
        {
            CurrentSettings.SelectedSeverities.Clear();
            
            if (CritSeverityCheckBox.IsChecked == true)
                CurrentSettings.SelectedSeverities.Add("CRIT");
            if (MajorSeverityCheckBox.IsChecked == true)
                CurrentSettings.SelectedSeverities.Add("MAJOR");
            if (MinorSeverityCheckBox.IsChecked == true)
                CurrentSettings.SelectedSeverities.Add("MINOR");
            if (InfoSeverityCheckBox.IsChecked == true)
                CurrentSettings.SelectedSeverities.Add("INFO");
                
            UpdateFilterSummary();
        }

        /// <summary>
        /// 상태 필터 업데이트
        /// </summary>
        private void UpdateStatusFilter()
        {
            CurrentSettings.SelectedStatuses.Clear();
            
            if (OpenStatusCheckBox.IsChecked == true)
                CurrentSettings.SelectedStatuses.Add("OPEN");
            if (FixedStatusCheckBox.IsChecked == true)
                CurrentSettings.SelectedStatuses.Add("FIXED");
            if (IgnoredStatusCheckBox.IsChecked == true)
                CurrentSettings.SelectedStatuses.Add("IGNORED");
            if (FalsePosStatusCheckBox.IsChecked == true)
                CurrentSettings.SelectedStatuses.Add("FALSE_POS");
                
            UpdateFilterSummary();
        }

        /// <summary>
        /// 위치 필터 업데이트
        /// </summary>
        private void UpdateLocationFilter()
        {
            CurrentSettings.EnableLocationFilter = EnableLocationFilterCheckBox.IsChecked == true;
            
            if (CurrentSettings.EnableLocationFilter)
            {
                if (double.TryParse(MinXTextBox.Text, out var minX))
                    CurrentSettings.MinX = minX;
                else
                    CurrentSettings.MinX = null;

                if (double.TryParse(MaxXTextBox.Text, out var maxX))
                    CurrentSettings.MaxX = maxX;
                else
                    CurrentSettings.MaxX = null;

                if (double.TryParse(MinYTextBox.Text, out var minY))
                    CurrentSettings.MinY = minY;
                else
                    CurrentSettings.MinY = null;

                if (double.TryParse(MaxYTextBox.Text, out var maxY))
                    CurrentSettings.MaxY = maxY;
                else
                    CurrentSettings.MaxY = null;
            }
            
            UpdateFilterSummary();
        }

        /// <summary>
        /// 필터 요약 업데이트
        /// </summary>
        private void UpdateFilterSummary()
        {
            try
            {
                var summaryParts = new List<string>();

                // 텍스트 검색
                if (!string.IsNullOrEmpty(CurrentSettings.SearchText))
                {
                    summaryParts.Add($"텍스트: '{CurrentSettings.SearchText}'");
                }

                // 오류 유형
                if (CurrentSettings.SelectedErrorTypes.Count < 4)
                {
                    summaryParts.Add($"유형: {string.Join(", ", CurrentSettings.SelectedErrorTypes)}");
                }

                // 심각도
                if (CurrentSettings.SelectedSeverities.Count < 4)
                {
                    summaryParts.Add($"심각도: {string.Join(", ", CurrentSettings.SelectedSeverities)}");
                }

                // 상태
                if (CurrentSettings.SelectedStatuses.Count < 4)
                {
                    summaryParts.Add($"상태: {string.Join(", ", CurrentSettings.SelectedStatuses)}");
                }

                // 날짜 범위
                if (CurrentSettings.EnableDateFilter)
                {
                    var dateRange = "날짜 범위";
                    if (CurrentSettings.StartDate.HasValue || CurrentSettings.EndDate.HasValue)
                    {
                        var start = CurrentSettings.StartDate?.ToString("yyyy-MM-dd") ?? "시작";
                        var end = CurrentSettings.EndDate?.ToString("yyyy-MM-dd") ?? "끝";
                        dateRange += $": {start} ~ {end}";
                    }
                    summaryParts.Add(dateRange);
                }

                // 위치 범위
                if (CurrentSettings.EnableLocationFilter)
                {
                    summaryParts.Add("위치 범위 적용");
                }

                FilterSummaryText.Text = summaryParts.Any() 
                    ? string.Join(", ", summaryParts) 
                    : "모든 오류 표시";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필터 요약 업데이트 실패");
                FilterSummaryText.Text = "필터 요약 오류";
            }
        }

        #endregion
    }
}