#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
// using 중복 제거됨
using System.Windows.Media;
using SpatialCheckPro.Models.Enums;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Data;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 지도 네비게이션 이벤트 인수
    /// </summary>
    public class MapNavigationEventArgs : EventArgs
    {
        /// <summary>객체 ID</summary>
        public string ObjectId { get; set; } = string.Empty;
        
        /// <summary>테이블명</summary>
        public string TableName { get; set; } = string.Empty;
        
        /// <summary>오류 유형</summary>
        public string ErrorType { get; set; } = string.Empty;
        
        /// <summary>GlobalID</summary>
        public Guid? GlobalId { get; set; }
        
        /// <summary>네비게이션 유형</summary>
        public MapNavigationType NavigationType { get; set; }
    }

    /// <summary>
    /// 지도 네비게이션 유형
    /// </summary>
    public enum MapNavigationType
    {
        /// <summary>일반 피처로 이동</summary>
        ZoomToFeature,
        
        /// <summary>오류 피처로 이동 (하이라이트 포함)</summary>
        ZoomToError,
        
        /// <summary>객체로 이동</summary>
        ZoomToObject,
        
        /// <summary>테이블 전체 범위로 이동</summary>
        ZoomToTable
    }

    /// <summary>
    /// 검수 결과 항목
    /// </summary>
    public class ValidationResultItem
    {
        public string Stage { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 검수 결과 화면
    /// </summary>
    public partial class ValidationResultView : UserControl
    {
        private List<ValidationResultItem> _allResults = new();
        private SpatialCheckPro.Models.ValidationResult? _validationResult;
        private List<SpatialCheckPro.Models.ValidationResult>? _batchResults;
        private readonly ILogger<ValidationResultView> _logger;
        private ICollectionView? _resultsView;
        private System.Timers.Timer? _filterDebounceTimer;

        // 보고서 생성 요청 이벤트 사용처가 없어 제거하여 경고 방지

        // 지도 이동 요청 이벤트 제거됨

        public ValidationResultView()
        {
            InitializeComponent();
            
            // 로거 초기화 (콘솔 로거 제거 - 백그라운드 동작)
            var loggerFactory = LoggerFactory.Create(builder => { /* 콘솔 로거 미사용 */ });
            _logger = loggerFactory.CreateLogger<ValidationResultView>();
            
            LoadRealValidationResults();

            // 필터 디바운스 타이머
            _filterDebounceTimer = new System.Timers.Timer(300) { AutoReset = false };
            _filterDebounceTimer.Elapsed += (_, __) => Dispatcher.Invoke(ApplyAdvancedFilter);

            // 이벤트 바인딩(존재하는 경우에만)
            TryWireAdvancedFilterEvents();
        }

        private void TryWireAdvancedFilterEvents()
        {
            try
            {
                IncludeTextBox.TextChanged += (_, __) => _filterDebounceTimer?.Start();
                ExcludeTextBox.TextChanged += (_, __) => _filterDebounceTimer?.Start();
                TableFilterSearchBox.TextChanged += (_, __) => _filterDebounceTimer?.Start();
                Stage1Check.Checked += (_, __) => _filterDebounceTimer?.Start();
                Stage1Check.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                Stage2Check.Checked += (_, __) => _filterDebounceTimer?.Start();
                Stage2Check.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                Stage3Check.Checked += (_, __) => _filterDebounceTimer?.Start();
                Stage3Check.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                Stage4Check.Checked += (_, __) => _filterDebounceTimer?.Start();
                Stage4Check.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                Stage5Check.Checked += (_, __) => _filterDebounceTimer?.Start();
                Stage5Check.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                SeverityErrorCheck.Checked += (_, __) => _filterDebounceTimer?.Start();
                SeverityErrorCheck.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                SeverityWarnCheck.Checked += (_, __) => _filterDebounceTimer?.Start();
                SeverityWarnCheck.Unchecked += (_, __) => _filterDebounceTimer?.Start();
                ClearFiltersButton.Click += (_, __) => ClearAdvancedFilters();
            }
            catch { /* 디자인 모드 등에서 컨트롤 없을 수 있음 */ }
        }

        private void InitResultsView()
        {
            if (_resultsView == null && ResultsDataGrid != null)
            {
                _resultsView = CollectionViewSource.GetDefaultView(ResultsDataGrid.ItemsSource);
                if (_resultsView != null) _resultsView.Filter = AdvancedFilterPredicate;
            }
        }

        private bool AdvancedFilterPredicate(object obj)
        {
            if (obj is not ValidationResultItem item) return true;

            // 포함/제외
            var inc = (IncludeTextBox?.Text ?? string.Empty).Trim();
            var exc = (ExcludeTextBox?.Text ?? string.Empty).Trim();
            var tableQuery = (TableFilterSearchBox?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(inc))
            {
                var hit = (item.Stage + " " + item.TableName + " " + item.ErrorType + " " + item.Message).IndexOf(inc, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) return false;
            }
            if (!string.IsNullOrEmpty(exc))
            {
                var hit = (item.Stage + " " + item.TableName + " " + item.ErrorType + " " + item.Message).IndexOf(exc, StringComparison.OrdinalIgnoreCase) >= 0;
                if (hit) return false;
            }

            // 테이블 이름 필터 (검색창 기반)
            if (!string.IsNullOrEmpty(tableQuery))
            {
                if (item.TableName?.IndexOf(tableQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // 단계 체크
            bool stageAllowed = true;
            var anyStageChecked = (Stage1Check?.IsChecked == true) || (Stage2Check?.IsChecked == true) || (Stage3Check?.IsChecked == true) || (Stage4Check?.IsChecked == true) || (Stage5Check?.IsChecked == true);
            if (anyStageChecked)
            {
                stageAllowed = (item.Stage.StartsWith("1") && Stage1Check?.IsChecked == true)
                               || (item.Stage.StartsWith("2") && Stage2Check?.IsChecked == true)
                               || (item.Stage.StartsWith("3") && Stage3Check?.IsChecked == true)
                               || (item.Stage.StartsWith("4") && Stage4Check?.IsChecked == true)
                               || (item.Stage.StartsWith("5") && Stage5Check?.IsChecked == true);
            }
            if (!stageAllowed) return false;

            // 심각도 체크(결과 항목에 명시적 Severity가 없으므로 ErrorType/메시지 키워드로 간접 판별)
            var sevError = (SeverityErrorCheck?.IsChecked == true);
            var sevWarn = (SeverityWarnCheck?.IsChecked == true);
            if (sevError || sevWarn)
            {
                var isWarn = item.ErrorType.Contains("경고") || item.Message.Contains("경고");
                if (sevError && isWarn) return false;
                if (sevWarn && !isWarn) return false;
            }

            return true;
        }

        private void ApplyAdvancedFilter()
        {
            try
            {
                if (ResultsDataGrid?.ItemsSource == null) return;
                InitResultsView();
                _resultsView?.Refresh();
                ApplyTableFilterSearch();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "고급 필터 적용 실패");
            }
        }

        private void ApplyTableFilterSearch()
        {
            try
            {
                // 리스트 제거됨: 결과 그리드에 직접 테이블명 필터 적용
                var query = (TableFilterSearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
                if (_resultsView == null)
                {
                    InitResultsView();
                }
                _resultsView?.Refresh();
            }
            catch { }
        }

        private void ClearAdvancedFilters()
        {
            try
            {
                IncludeTextBox.Text = string.Empty;
                ExcludeTextBox.Text = string.Empty;
                Stage1Check.IsChecked = Stage2Check.IsChecked = Stage3Check.IsChecked = Stage4Check.IsChecked = Stage5Check.IsChecked = false;
                SeverityErrorCheck.IsChecked = SeverityWarnCheck.IsChecked = false;
                // 테이블 리스트 제거됨: 검색창만 초기화
                TableFilterSearchBox.Text = string.Empty;
                ApplyAdvancedFilter();
            }
            catch { }
        }

        private void ExplainButton_Click(object sender, RoutedEventArgs e)
        {
            var message =
                "오류/경고 판단 근거\n\n" +
                "1단계(테이블): 정의 불일치·필수 누락 등은 오류, 타입 경미 불일치는 경고로 집계됩니다.\n" +
                "2단계(스키마): UK 중복, FK 불일치 등 제약 위반은 오류, 경미 메타 불일치는 경고입니다.\n" +
                "3단계(지오메트리): 중복/겹침/자가교차/슬리버 등 항목 카운트 합은 오류, 항목별 WarningMessages는 경고입니다.\n" +
                "4단계(공간관계): RelationCheckResult.Errors 중 REL_ 코드 기반 항목은 공간 관계 오류로 집계됩니다.\n" +
                "5단계(속성관계): attribute_check 규칙 위반은 오류/경고로 분리되어 AttributeRelationCheckResult 및 전체 합산에 반영됩니다.\n\n" +
                "종합: SimpleValidationService가 각 단계 결과를 누적하여 총 오류/경고를 산출합니다.";
            MessageBox.Show(message, "판정설명", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 검수 결과를 설정합니다
        /// </summary>
        /// <param name="result">검수 결과</param>
        public void SetValidationResult(SpatialCheckPro.Models.ValidationResult result)
        {
            _validationResult = result;
            UpdateUI();
        }

        /// <summary>
        /// 배치 결과 목록과 함께 설정합니다(선택 가능)
        /// </summary>
        public void SetBatchResults(List<SpatialCheckPro.Models.ValidationResult> results)
        {
            _batchResults = results;
            try
            {
                if (BatchResultSelector != null)
                {
                    BatchResultSelector.ItemsSource = results
                        .Select((r, idx) => new { Index = idx, Name = System.IO.Path.GetFileName(r.TargetFile) })
                        .ToList();
                    BatchResultSelector.DisplayMemberPath = "Name";
                    BatchResultSelector.SelectedValuePath = "Index";
                    BatchResultSelector.SelectedIndex = results.Count > 0 ? results.Count - 1 : -1; // 기본: 마지막 파일
                    BatchResultSelector.Visibility = results.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // 배치 통계 대시보드 표시
                if (results.Count > 1)
                {
                    UpdateBatchStatistics(results);
                    // 겹침 방지를 위해 상단 전용 행(Grid.Row=1)에만 표시
                    BatchStatisticsCard.Visibility = Visibility.Visible;
                    BatchSummaryText.Text = $"총 {results.Count}개 파일 검수 완료";
                    BatchSummaryText.Visibility = Visibility.Visible;
                }
            }
            catch { }

            if (results?.Count > 0)
            {
                _validationResult = results.Last();
                UpdateUI();
            }
        }

        /// <summary>
        /// 배치 검수 전체 통계를 업데이트합니다
        /// </summary>
        private void UpdateBatchStatistics(List<SpatialCheckPro.Models.ValidationResult> results)
        {
            try
            {
                // 총 파일 수 (단위 포함 가독성 개선)
                TotalFilesValueText.Text = $"{results.Count}개";
                
                // 성공/실패 건수
                int successCount = results.Count(r => r.IsValid);
                int failureCount = results.Count - successCount;
                SuccessCountValueText.Text = $"{successCount}개";
                FailureCountValueText.Text = $"{failureCount}개";
                
                // 총 오류 수
                int totalErrors = results.Sum(r => r.ErrorCount);
                TotalErrorsValueText.Text = $"{totalErrors}개";
                
                // 전체 소요 시간 (모든 검수 시간 합산)
                var totalDuration = TimeSpan.FromSeconds(
                    results.Sum(r => r.ProcessingTime.TotalSeconds)
                );
                
                if (totalDuration.TotalHours >= 1)
                {
                    TotalDurationText.Text = $"{(int)totalDuration.TotalHours}:{totalDuration.Minutes:D2}";
                }
                else
                {
                    TotalDurationText.Text = $"{(int)totalDuration.TotalMinutes}:{totalDuration.Seconds:D2}";
                }
                
                // 성공률 계산
                double successRate = results.Count > 0 ? (successCount / (double)results.Count) * 100 : 0;
                
                System.Diagnostics.Debug.WriteLine($"[배치 통계] 파일: {results.Count}, 성공: {successCount}, 실패: {failureCount}, 오류: {totalErrors}, 시간: {totalDuration.TotalMinutes:F1}분");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[배치 통계 오류] {ex.Message}");
            }
        }

        /// <summary>
        /// 실제 지오메트리 검수 규칙 수를 계산합니다 (검수 항목 종류 수 기반)
        /// </summary>
        /// <returns>검수 항목 종류 수</returns>
        private int CalculateActualGeometryRules()
        {
            try
            {
                // 설정 파일 경로 찾기
                var configDirectory = GetDefaultConfigDirectory();
                var geometryConfigPath = Path.Combine(configDirectory, "3_geometry_check.csv");
                
                if (!File.Exists(geometryConfigPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[규칙 수 계산] 설정 파일을 찾을 수 없습니다: {geometryConfigPath}");
                    return 0;
                }

                // 설정 파일 직접 분석
                var lines = File.ReadAllLines(geometryConfigPath, System.Text.Encoding.UTF8);
                if (lines.Length < 2)
                {
                    System.Diagnostics.Debug.WriteLine("[규칙 수 계산] 설정 파일이 비어있습니다.");
                    return 0;
                }

                // 헤더 라인에서 규칙 컬럼 수 계산 (4번째 컬럼부터)
                var headerLine = lines[0];
                var headerColumns = headerLine.Split(',');
                
                // 규칙 컬럼 수 계산 (테이블ID, 테이블명칭, 지오메트리타입 제외)
                int ruleColumnCount = headerColumns.Length - 3; // 처음 3개 컬럼 제외
                
                System.Diagnostics.Debug.WriteLine($"[규칙 수 계산] 검수 항목 종류 수: {ruleColumnCount}개");
                
                return ruleColumnCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[규칙 수 계산 오류] {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 기본 설정 디렉토리를 가져옵니다
        /// </summary>
        /// <returns>설정 디렉토리 경로</returns>
        private string GetDefaultConfigDirectory()
        {
            // 여러 경로를 시도해서 설정 파일 찾기
            var possiblePaths = new[]
            {
                // 1. 현재 실행 파일 기준 Config 폴더
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config"),
                // 2. 상위 폴더의 Config (GUI 프로젝트에서 라이브러리 프로젝트의 Config 참조)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "SpatialCheckPro", "Config"),
                // 3. 솔루션 루트의 Config
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Config"),
                // 4. 절대 경로 (개발 환경)
                Path.Combine(@"G:\SpatialCheckPro\SpatialCheckPro\Config")
            };

            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                var testFile = Path.Combine(fullPath, "3_geometry_check.csv");
                if (File.Exists(testFile))
                {
                    System.Diagnostics.Debug.WriteLine($"[설정 디렉토리] 찾음: {fullPath}");
                    return fullPath;
                }
            }

            // 기본값 반환
            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            System.Diagnostics.Debug.WriteLine($"[설정 디렉토리] 기본값 사용: {defaultPath}");
            return defaultPath;
        }

        /// <summary>
        /// 검수 진행 버튼 클릭 이벤트 핸들러
        /// </summary>
        private async void StartValidationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger?.LogInformation("검수 진행 버튼 클릭됨");
                
                // 파일 선택 다이얼로그 표시
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "검수할 GDB 파일 선택",
                    Filter = "File Geodatabase (*.gdb)|*.gdb|모든 파일 (*.*)|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var selectedFile = openFileDialog.FileName;
                    _logger?.LogInformation("선택된 파일: {FilePath}", selectedFile);

                    // 검수 진행 상태 표시
                    StartValidationButton.IsEnabled = false;
                    StartValidationButton.Content = "검수 진행 중...";

                    // 검수 실행
                    await ExecuteValidationAsync(selectedFile);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 진행 중 오류 발생");
                MessageBox.Show($"검수 진행 중 오류가 발생했습니다.\n\n오류: {ex.Message}", 
                    "검수 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 버튼 상태 복원
                StartValidationButton.IsEnabled = true;
                StartValidationButton.Content = "검수 진행";
            }
        }

        /// <summary>
        /// 검수를 실행합니다
        /// </summary>
        private async Task ExecuteValidationAsync(string gdbPath)
        {
            try
            {
                _logger?.LogInformation("검수 실행 시작: {GdbPath}", gdbPath);

                // ValidationService 가져오기
                var serviceProvider = Application.Current.Resources["ServiceProvider"] as IServiceProvider;
                if (serviceProvider == null)
                {
                    throw new InvalidOperationException("ServiceProvider를 찾을 수 없습니다.");
                }

                var validationService = serviceProvider.GetService(typeof(IValidationService)) as IValidationService;
                if (validationService == null)
                {
                    throw new InvalidOperationException("ValidationService를 찾을 수 없습니다.");
                }

                // 설정 디렉토리 경로
                var configDirectory = GetDefaultConfigDirectory();
                _logger?.LogInformation("설정 디렉토리: {ConfigDirectory}", configDirectory);

                // SpatialFileInfo 생성
                var spatialFile = new SpatialFileInfo
                {
                    FilePath = gdbPath,
                    FileName = Path.GetFileName(gdbPath),
                    FileSize = new FileInfo(gdbPath).Length,
                    CreatedAt = File.GetCreationTime(gdbPath),
                    ModifiedAt = File.GetLastWriteTime(gdbPath)
                };

                // 진행률 보고를 위한 Progress 객체
                var progress = new Progress<ValidationProgress>(progressInfo =>
                {
                    // UI 스레드에서 진행률 업데이트
                    Dispatcher.Invoke(() =>
                    {
                        UpdateProgressUI(progressInfo);
                    });
                });

                // 검수 실행
                var result = await validationService.ExecuteValidationAsync(
                    spatialFile, 
                    configDirectory, 
                    progress, 
                    CancellationToken.None);

                _logger?.LogInformation("검수 실행 완료: {Status}", result.Status);

                // 결과 업데이트
                _validationResult = result;
                UpdateUI();

                // 완료 메시지 표시
                MessageBox.Show($"검수가 완료되었습니다.\n\n상태: {result.Status}\n오류: {result.TotalErrors}개\n경고: {result.TotalWarnings}개", 
                    "검수 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 실행 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 진행률 UI를 업데이트합니다
        /// </summary>
        private void UpdateProgressUI(ValidationProgress progress)
        {
            try
            {
                // 진행률 정보를 UI에 표시
                var progressText = $"{progress.CurrentStageName} - {progress.CurrentTask} ({progress.OverallPercentage}%)";
                
                // 상태 표시 (임시로 창 제목에 표시)
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    parentWindow.Title = $"Spatial Check Pro - {progressText}";
                }

                _logger?.LogDebug("검수 진행률: {Progress}", progressText);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "진행률 UI 업데이트 중 오류 발생");
            }
        }

        /// <summary>
        /// UI를 업데이트합니다
        /// </summary>
        private void UpdateUI()
        {
            if (_validationResult == null) 
            {
                _logger?.LogWarning("검수 결과가 null입니다. UI 업데이트를 건너뜁니다.");
                return;
            }

            try
            {
                _logger?.LogInformation("검수 결과 UI 업데이트 시작: ValidationId={ValidationId}, ErrorCount={ErrorCount}, WarningCount={WarningCount}", 
                    _validationResult.ValidationId, _validationResult.ErrorCount, _validationResult.WarningCount);

                // 요약 정보 업데이트
                // 대시보드의 검수 상태는 결과의 성공/실패 여부와 무관하게 "완료"로 표기
                ValidationStatusText.Text = "완료";
                
                // 검수 시간을 적절한 형식으로 표시
                var startTime = _validationResult.StartedAt;
                var endTime = _validationResult.CompletedAt ?? DateTime.Now;
                var processingTime = endTime - startTime;

                ElapsedTimeText.Text = processingTime.ToString(@"h\:mm\:ss");
                
                // 오류/경고 수 표기 복원
                ErrorCountText.Text = _validationResult.ErrorCount.ToString();
                WarningCountText.Text = _validationResult.WarningCount.ToString();

                _logger?.LogInformation("기본 정보 업데이트 완료: 상태={Status}, 시간={Time}, 오류={Errors}, 경고={Warnings}", 
                    ValidationStatusText.Text, ElapsedTimeText.Text, ErrorCountText.Text, WarningCountText.Text);

                // 탭별 데이터 그리드 업데이트
                UpdateTabDataGrids();

                // 단계별 요약 카드 업데이트
                UpdateStageSummaries();

                // 대시보드 업데이트
                UpdateDashboard();

                // 상세 결과 테이블 업데이트
                UpdateResultsTable();

                _logger?.LogInformation("검수 결과 UI 업데이트 완료: 총 {ResultCount}개 결과 항목", _allResults.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 결과 UI 업데이트 중 오류 발생");
            }
        }

        private void BatchResultSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_batchResults == null || _batchResults.Count == 0) return;
                if (BatchResultSelector?.SelectedValue is int idx && idx >= 0 && idx < _batchResults.Count)
                {
                    _validationResult = _batchResults[idx];
                    UpdateUI();
                }
            }
            catch { }
        }

        /// <summary>
        /// 탭별 데이터 그리드를 업데이트합니다
        /// </summary>
        private void UpdateTabDataGrids()
        {
            if (_validationResult == null) return;

            try
            {
                // 1단계: 테이블 검수 결과 탭
                if (_validationResult.TableCheckResult?.TableResults != null)
                {
                    TableResultsGrid.ItemsSource = _validationResult.TableCheckResult.TableResults;
                }

                // 2단계: 스키마 검수 결과 탭
                if (_validationResult.SchemaCheckResult?.SchemaResults != null)
                {
                    SchemaResultsGrid.ItemsSource = _validationResult.SchemaCheckResult.SchemaResults;
                    _logger.LogInformation("2단계 스키마 검수 결과 표시: {Count}개 항목", _validationResult.SchemaCheckResult.SchemaResults.Count);
                    
                    // 디버깅: 결과 내용 확인
                    if (_validationResult.SchemaCheckResult.SchemaResults.Any())
                    {
                        var sample = _validationResult.SchemaCheckResult.SchemaResults.First();
                        _logger.LogInformation("스키마 결과 샘플: {TableId}.{ColumnName} = {IsValid}", 
                            sample.TableId, sample.ColumnName, sample.IsValid);
                    }
                    else
                    {
                        _logger.LogWarning("SchemaResults 리스트가 비어있습니다!");
                    }
                }
                else
                {
                    _logger.LogWarning("2단계 스키마 검수 결과가 null입니다");
                    _logger.LogWarning("SchemaCheckResult 상태: {IsNull}", _validationResult.SchemaCheckResult == null ? "null" : "not null");
                    SchemaResultsGrid.ItemsSource = new List<object>();
                }

                // 3단계: 지오메트리 검수 결과 탭
                if (_validationResult.GeometryCheckResult?.GeometryResults != null)
                {
                    GeometryResultsGrid.ItemsSource = _validationResult.GeometryCheckResult.GeometryResults;
                }

                // 4단계: 속성 검수 결과 처리
                // 5단계: 공간 관계 검수 결과 탭
                if (_validationResult.RelationCheckResult != null)
                {
                    UpdateRelationCheckResults(_validationResult.RelationCheckResult);
                }

                // 4단계: 속성값 검수 결과를 AttributeRelationErrorsGrid에 표시
                try
                {
                    _logger?.LogInformation("속성 검수 결과 표시 시작. ErrorCount: {ECount}", 
                        _validationResult.AttributeRelationCheckResult?.ErrorCount ?? 0);
                    
                    var attributes = new List<SpatialCheckPro.Models.ValidationError>();
                    if (_validationResult.AttributeRelationCheckResult?.Errors != null)
                    {
                        attributes.AddRange(_validationResult.AttributeRelationCheckResult.Errors);
                    }
                    if (_validationResult.AttributeRelationCheckResult?.Warnings != null)
                    {
                        attributes.AddRange(_validationResult.AttributeRelationCheckResult.Warnings);
                    }

                    var attrItems = attributes
                            .Select(w => new
                            {
                                TableName = string.IsNullOrWhiteSpace(w.TableName) ? (w.SourceTable ?? string.Empty) : w.TableName,
                            FieldName = !string.IsNullOrWhiteSpace(w.FieldName) ? w.FieldName : (w.Metadata != null && w.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty),
                            RuleName = string.IsNullOrWhiteSpace(w.ErrorCode) ? (w.Metadata != null && w.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : w.ErrorCode,
                                ObjectId = w.FeatureId ?? (w.SourceObjectId?.ToString() ?? string.Empty),
                            ExpectedValue = !string.IsNullOrWhiteSpace(w.ExpectedValue) ? w.ExpectedValue : (w.Metadata != null && w.Metadata.TryGetValue("Expected", out var exv) ? Convert.ToString(exv) ?? string.Empty : string.Empty),
                            ActualValue = !string.IsNullOrWhiteSpace(w.ActualValue) ? w.ActualValue : (w.Metadata != null && w.Metadata.TryGetValue("Actual", out var acv) ? Convert.ToString(acv) ?? string.Empty : string.Empty),
                                Message = w.Message
                            })
                            .ToList();

                    AttributeRelationErrorsGrid.ItemsSource = attrItems;
                    _logger?.LogInformation("AttributeRelationErrorsGrid 바인딩: {Count}개", attrItems.Count);
                }
                catch (Exception ex) 
                { 
                    _logger?.LogError(ex, "속성 검수 결과 표시 중 오류 발생");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"탭 데이터 그리드 업데이트 중 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 1,2,3단계 요약 카드를 업데이트합니다
        /// </summary>
        private void UpdateStageSummaries()
        {
            if (_validationResult == null) return;

            try
            {
                // 0단계: FileGDB 검증 요약
                if (_validationResult.FileGdbCheckResult != null)
                {
                    var fgdbResult = _validationResult.FileGdbCheckResult;
                    DashStage0StatusText.Text = fgdbResult.Status == CheckStatus.Passed ? "성공" : 
                                              fgdbResult.Status == CheckStatus.Failed ? "실패" : "경고";
                    DashStage0ErrorCountText.Text = fgdbResult.ErrorCount.ToString();
                    DashStage0WarningCountText.Text = fgdbResult.WarningCount.ToString();
                    
                    // 상태에 따른 색상 변경
                    DashStage0StatusText.Foreground = fgdbResult.Status == CheckStatus.Passed ? 
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)) : // 초록색
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); // 빨간색
                }
                else
                {
                    DashStage0StatusText.Text = "미실행";
                    DashStage0ErrorCountText.Text = "0";
                    DashStage0WarningCountText.Text = "0";
                    DashStage0StatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)); // 회색
                }

                // 1단계 요약
                if (_validationResult.TableCheckResult != null)
                {
                    Stage1TotalTablesText.Text = (_validationResult.TableCheckResult.TotalTableCount).ToString();
                    var tableResults = _validationResult.TableCheckResult.TableResults ?? new List<TableValidationItem>();
                    Stage1MissingDefinedTablesText.Text = tableResults.Count(t => string.Equals(t.TableExistsCheck, "N", StringComparison.OrdinalIgnoreCase)).ToString();
                    Stage1UndefinedTablesText.Text = tableResults.Count(t => string.Equals(t.ExpectedFeatureType?.Trim() ?? string.Empty, "정의되지 않음", StringComparison.OrdinalIgnoreCase)).ToString();
                    Stage1ZeroFeatureTablesText.Text = tableResults.Count(t => string.Equals(t.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase) && (t.FeatureCount ?? 0) == 0).ToString();
                }

                // 2단계 요약
                if (_validationResult.SchemaCheckResult != null)
                {
                    Stage2TotalColumnsText.Text = _validationResult.SchemaCheckResult.TotalColumnCount.ToString();
                    Stage2ErrorCountText.Text = _validationResult.SchemaCheckResult.ErrorCount.ToString();
                    Stage2WarningCountText.Text = _validationResult.SchemaCheckResult.WarningCount.ToString();
                }

                // 3단계 요약 로직 추가
                if (_validationResult.GeometryCheckResult != null)
                {
                    Stage3TableCountText.Text = _validationResult.GeometryCheckResult.TotalTableCount.ToString();
                    Stage3ErrorSumText.Text = _validationResult.GeometryCheckResult.ErrorCount.ToString();
                    Stage3WarningSumText.Text = _validationResult.GeometryCheckResult.WarningCount.ToString();
                }
            }
            catch (Exception ex) 
            { 
                _logger.LogError(ex, "단계별 요약 카드 업데이트 중 오류");
            }
        }

        /// <summary>
        /// 대시보드 요약을 업데이트합니다
        /// </summary>
        private void UpdateDashboard()
        {
            if (_validationResult == null) return;

            try
            {
                // 1단계
                if (_validationResult.TableCheckResult != null)
                {
                    DashStage1TotalTablesText.Text = _validationResult.TableCheckResult.TotalTableCount.ToString();

                    var tableResults = _validationResult.TableCheckResult.TableResults ?? new List<TableValidationItem>();
                    var missingDefinedCount = tableResults.Count(t => string.Equals(t.TableExistsCheck, "N", StringComparison.OrdinalIgnoreCase));
                    var undefinedTablesCount = tableResults.Count(t => string.Equals(t.ExpectedFeatureType?.Trim() ?? string.Empty, "정의되지 않음", StringComparison.OrdinalIgnoreCase));
                    var zeroFeatureTablesCount = tableResults.Count(t => string.Equals(t.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase) && (t.FeatureCount ?? 0) == 0);

                    DashStage1MissingDefinedTablesText.Text = missingDefinedCount.ToString();
                    DashStage1UndefinedTablesText.Text = undefinedTablesCount.ToString();
                    DashStage1ZeroFeatureTablesText.Text = zeroFeatureTablesCount.ToString();
                }

                // 2단계
                if (_validationResult.SchemaCheckResult != null)
                {
                    var dashTotalCols = _validationResult.SchemaCheckResult.TotalColumnCount;
                    if (dashTotalCols == 0)
                    {
                        dashTotalCols = _validationResult.SchemaCheckResult.SchemaResults?.Count ?? 0; // 폴백: 실제 검사 컬럼 수
                    }
                    DashStage2TotalColumnsText.Text = dashTotalCols.ToString();
                    DashStage2ErrorCountText.Text = _validationResult.SchemaCheckResult.ErrorCount.ToString();
                    DashStage2WarningCountText.Text = _validationResult.SchemaCheckResult.WarningCount.ToString();
                }

                // 3단계
                if (_validationResult.GeometryCheckResult?.GeometryResults != null)
                {
                    var results = _validationResult.GeometryCheckResult.GeometryResults;
                    // 개별 오류 수 업데이트
                    DashStage3DuplicateText.Text = results.Sum(r => r.DuplicateCount).ToString();
                    DashStage3OverlapText.Text = results.Sum(r => r.OverlapCount).ToString();
                    DashStage3SelfIntersectionText.Text = results.Sum(r => r.SelfIntersectionCount).ToString();
                    DashStage3SelfOverlapText.Text = results.Sum(r => r.SelfOverlapCount).ToString();
                    DashStage3SliverText.Text = results.Sum(r => r.SliverCount).ToString();
                    DashStage3SpikeText.Text = results.Sum(r => r.SpikeCount).ToString();
                    DashStage3ShortObjectText.Text = results.Sum(r => r.ShortObjectCount).ToString();
                    DashStage3SmallAreaText.Text = results.Sum(r => r.SmallAreaCount).ToString();
                    DashStage3HoleText.Text = results.Sum(r => r.PolygonInPolygonCount).ToString();
                    DashStage3MinPointText.Text = results.Sum(r => r.MinPointCount).ToString();
                    DashStage3UndershootText.Text = results.Sum(r => r.UndershootCount).ToString();
                    DashStage3OvershootText.Text = results.Sum(r => r.OvershootCount).ToString();
                    
                    // 실제 검수 규칙 수 계산 (설정 파일 기반)
                    int actualTotalRules = CalculateActualGeometryRules();
                    int totalErrors = results.Sum(r => r.DuplicateCount + r.OverlapCount + r.SelfIntersectionCount + 
                                                   r.SelfOverlapCount + r.SliverCount + r.SpikeCount + 
                                                   r.ShortObjectCount + r.SmallAreaCount + r.PolygonInPolygonCount + 
                                                   r.MinPointCount + r.UndershootCount + r.SpikeCount);
                    
                    DashStage3RuleCountText.Text = actualTotalRules.ToString();
                    DashStage3TotalErrorText.Text = totalErrors.ToString();
                }
                else
                {
                    DashStage3DuplicateText.Text = "0";
                    DashStage3OverlapText.Text = "0";
                    DashStage3SelfIntersectionText.Text = "0";
                    DashStage3SelfOverlapText.Text = "0";
                    DashStage3SliverText.Text = "0";
                    DashStage3SpikeText.Text = "0";
                    DashStage3ShortObjectText.Text = "0";
                    DashStage3SmallAreaText.Text = "0";
                    DashStage3HoleText.Text = "0";
                    DashStage3MinPointText.Text = "0";
                    DashStage3UndershootText.Text = "0";
                    DashStage3OvershootText.Text = "0";
                    
                    // 검사한 규칙 수와 총 오류 수 초기화
                    DashStage3RuleCountText.Text = "0";
                    DashStage3TotalErrorText.Text = "0";
                }

                // 4단계 (속성 관계) 
                if (_validationResult.AttributeRelationCheckResult != null)
                {
                    var attrErr = _validationResult.AttributeRelationCheckResult.ErrorCount;
                    var attrWarn = _validationResult.AttributeRelationCheckResult.WarningCount;
                    if (AttributeErrorCountText != null) AttributeErrorCountText.Text = attrErr.ToString();
                    if (AttributeWarnCountText != null) AttributeWarnCountText.Text = attrWarn.ToString();
                    if (DashStage4AttrErrorText != null) DashStage4AttrErrorText.Text = attrErr.ToString();
                    if (DashStage4AttrWarnText != null) DashStage4AttrWarnText.Text = attrWarn.ToString();

                    // 검사된 규칙 수(4단계) - Metadata 대신 직접 속성 참조
                    var ruleCount = _validationResult.AttributeRelationCheckResult.ProcessedRulesCount;
                    if (DashStage4RuleCountText != null) DashStage4RuleCountText.Text = ruleCount.ToString();
                    if (Stage5RuleCountText != null) Stage5RuleCountText.Text = ruleCount.ToString();
                }

                // 5단계 (공간 관계)
                if (_validationResult.RelationCheckResult != null)
                {
                    var spatialErrors = _validationResult.RelationCheckResult.Errors?.Count ?? 0;
                    var spatialWarn = _validationResult.RelationCheckResult.WarningCount;
                    
                    // 5단계 오류/경고 수 표시
                    if (SpatialErrorCountText != null) SpatialErrorCountText.Text = spatialErrors.ToString();
                    if (SpatialWarnCountText != null) SpatialWarnCountText.Text = spatialWarn.ToString();
                    if (DashSpatialErrorCountText != null) DashSpatialErrorCountText.Text = spatialErrors.ToString();
                    if (DashSpatialWarnCountText != null) DashSpatialWarnCountText.Text = spatialWarn.ToString();

                    // 검사된 규칙 수: Metadata 대신 직접 속성 참조
                    var spatialRuleCount = _validationResult.RelationCheckResult.ProcessedRulesCount;
                    if (TotalRulesText != null) TotalRulesText.Text = spatialRuleCount.ToString();
                    if (DashTotalRulesText != null) DashTotalRulesText.Text = spatialRuleCount.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "대시보드 업데이트 중 오류");
            }
        }

        /// <summary>
        /// 결과 테이블을 업데이트합니다
        /// </summary>
        private void UpdateResultsTable()
        {
            if (_validationResult == null) 
            {
                _logger?.LogWarning("검수 결과가 null입니다. 결과 테이블 업데이트를 건너뜁니다.");
                return;
            }

            try
            {
                _logger?.LogInformation("결과 테이블 업데이트 시작");
                
                _allResults = GenerateResultItems(_validationResult);
                ResultsDataGrid.ItemsSource = _allResults;
                
                _logger?.LogInformation("결과 테이블 업데이트 완료: {Count}개 항목 생성", _allResults.Count);
                
                // 결과 항목별 상세 로깅
                var stageGroups = _allResults.GroupBy(r => r.Stage).ToList();
                foreach (var group in stageGroups)
                {
                    _logger?.LogInformation("  {Stage}: {Count}개 항목", group.Key, group.Count());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "결과 테이블 업데이트 중 오류 발생");
            }
        }

        // 관계 검수 결과 바인딩 (공간/속성 분리하여 표출)
        private void UpdateRelationCheckResults(RelationCheckResult relationCheckResult)
        {
            try
            {
                var errors = relationCheckResult.Errors ?? new List<SpatialCheckPro.Models.ValidationError>();

                // 공간 관계 오류: ErrorCode가 "REL_"로 시작하고, 테이블/객체ID가 존재하는 항목
                var spatialItems = errors
                    .Where(e => !string.IsNullOrWhiteSpace(e.ErrorCode) && e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase))
                    .Select(e => new
                    {
                        SourceLayer = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName,
                        RelationType = e.Metadata != null && e.Metadata.TryGetValue("RelationType", out var rt) ? Convert.ToString(rt) ?? string.Empty : string.Empty,
                        ErrorType = e.ErrorCode ?? string.Empty,
                        SourceObjectId = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty),
                        Message = e.Message
                    })
                    .ToList();

                SpatialRelationErrorsGrid.ItemsSource = spatialItems;
                SpatialErrorCountText.Text = spatialItems.Count.ToString();

                // 검사된 규칙 수 표시 (메타데이터에 저장된 SpatialRuleCount 사용)
                try
                {
                    var ruleCount = 0;
                    if (relationCheckResult.Metadata != null && relationCheckResult.Metadata.TryGetValue("SpatialRuleCount", out var cntObj))
                    {
                        // 숫자/문자 모두 안전하게 처리
                        if (cntObj is int i) ruleCount = i;
                        else if (cntObj is long l) ruleCount = (int)l;
                        else if (cntObj is string s && int.TryParse(s, out var parsed)) ruleCount = parsed;
                    }
                    if (TotalRulesText != null)
                    {
                        TotalRulesText.Text = ruleCount.ToString();
                    }
                }
                catch { /* 규칙 수 표시는 필수 아님 */ }

                // 속성 관계 오류: 공간 오류 외 나머지(규칙 기반) 항목
                var attrItems = errors
                    .Where(e => string.IsNullOrWhiteSpace(e.ErrorCode) || !e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase))
                    .Select(e => new
                    {
                        TableName = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName,
                        FieldName = e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : (e.FieldName ?? string.Empty),
                        RuleName = string.IsNullOrWhiteSpace(e.ErrorCode) ? (e.Metadata != null && e.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : e.ErrorCode,
                        ObjectId = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty),
                        ExpectedValue = e.Metadata != null && e.Metadata.TryGetValue("Expected", out var exv) ? Convert.ToString(exv) ?? string.Empty : string.Empty,
                        ActualValue = e.Metadata != null && e.Metadata.TryGetValue("Actual", out var acv) ? Convert.ToString(acv) ?? string.Empty : string.Empty,
                        Message = e.Message
                    })
                    .ToList();

                AttributeRelationErrorsGrid.ItemsSource = attrItems;
                AttributeErrorCountText.Text = attrItems.Count.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "관계 검수 결과 요약 업데이트 실패");
            }
        }

        /// <summary>
        /// 검수 결과에서 테이블 항목들을 생성합니다
        /// </summary>
        private List<ValidationResultItem> GenerateResultItems(SpatialCheckPro.Models.ValidationResult result)
        {
            var items = new List<ValidationResultItem>();

            try
            {
                _logger?.LogInformation("검수 결과 항목 생성 시작: ValidationId={ValidationId}", result.ValidationId);

                // 1단계: 테이블 검수 결과 처리
                if (result.TableCheckResult?.TableResults != null)
                {
                    _logger?.LogInformation("1단계 테이블 검수 결과 처리: {Count}개 테이블", result.TableCheckResult.TableResults.Count);
                    
                    foreach (var tableResult in result.TableCheckResult.TableResults)
                    {
                        // 테이블 존재 여부 및 지오메트리 타입 검사 결과 처리
                        if (tableResult.TableExistsCheck == "N")
                        {
                            items.Add(new ValidationResultItem
                            {
                                Stage = "1단계",
                                TableName = tableResult.TableName ?? tableResult.TableId,
                                ErrorType = "테이블 검수",
                                Message = $"테이블 '{tableResult.TableId}'이 존재하지 않습니다"
                            });
                        }
                        else if (tableResult.FeatureTypeCheck == "N")
                        {
                            // 정의되지 않은 테이블인지 확인
                            if (tableResult.ExpectedFeatureType == "정의되지 않음")
                            {
                                items.Add(new ValidationResultItem
                                {
                                    Stage = "1단계",
                                    TableName = tableResult.TableName ?? tableResult.TableId,
                                    ErrorType = "테이블 검수",
                                    Message = $"정의되지 않은 테이블: '{tableResult.TableId}' ({tableResult.ActualFeatureType}, {tableResult.FeatureCount}개 피처)"
                                });
                            }
                            else
                            {
                                items.Add(new ValidationResultItem
                                {
                                    Stage = "1단계",
                                    TableName = tableResult.TableName ?? tableResult.TableId,
                                    ErrorType = "테이블 검수",
                                    Message = $"지오메트리 타입 불일치: 예상 '{tableResult.ExpectedFeatureType}', 실제 '{tableResult.ActualFeatureType}'"
                                });
                            }
                        }

                        // 기존 오류/경고 메시지 처리
                        foreach (var error in tableResult.Errors)
                        {
                            items.Add(new ValidationResultItem
                            {
                                Stage = "1단계",
                                TableName = tableResult.TableName ?? tableResult.TableId,
                                ErrorType = "테이블 검수",
                                Message = error
                            });
                        }

                        foreach (var warning in tableResult.Warnings)
                        {
                            items.Add(new ValidationResultItem
                            {
                                Stage = "1단계",
                                TableName = tableResult.TableName ?? tableResult.TableId,
                                ErrorType = "테이블 검수",
                                Message = warning
                            });
                        }
                    }
                    
                    _logger?.LogInformation("1단계 처리 완료: {Count}개 항목 추가", items.Count);
                }
                else
                {
                    _logger?.LogWarning("1단계 테이블 검수 결과가 null입니다");
                }

                // 2단계: 스키마 검수 결과 처리
                if (result.SchemaCheckResult?.SchemaResults != null)
                {
                    _logger?.LogInformation("2단계 스키마 검수 결과 처리: {Count}개 스키마", result.SchemaCheckResult.SchemaResults.Count);
                    
                    foreach (var schemaResult in result.SchemaCheckResult.SchemaResults)
                    {
                        // 실패한 경우 상세한 원인 분석
                        if (!schemaResult.IsValid || schemaResult.DuplicateValueCount > 0 || 
                            schemaResult.InvalidDomainValueCount > 0 || schemaResult.OrphanRecordCount > 0)
                        {
                            var errorMessages = new List<string>();
                            
                            // 컬럼 존재 여부 확인
                            if (!schemaResult.ColumnExists)
                            {
                                errorMessages.Add($"컬럼 '{schemaResult.ColumnName}'이 존재하지 않습니다");
                            }
                            else if (schemaResult.IsUndefinedField)
                            {
                                errorMessages.Add($"정의되지 않은 컬럼 '{schemaResult.ColumnName}' (실제 타입: {schemaResult.ActualDataType})");
                            }
                            else
                            {
                                // 데이터 타입 불일치
                                if (!schemaResult.DataTypeMatches)
                                {
                                    errorMessages.Add($"데이터 타입 불일치: 예상 '{schemaResult.ExpectedDataType}', 실제 '{schemaResult.ActualDataType}'");
                                }
                                
                                // 길이 불일치
                                if (!schemaResult.LengthMatches)
                                {
                                    errorMessages.Add($"필드 길이 불일치: 예상 '{schemaResult.ExpectedLength}', 실제 '{schemaResult.ActualLength}'");
                                }
                                
                                // Not Null 불일치
                                if (!schemaResult.NotNullMatches)
                                {
                                    var expectedMsg = schemaResult.ExpectedNotNull == "Y" ? "NOT NULL 필수" : "NULL 허용";
                                    var actualMsg = schemaResult.ActualNotNull == "Y" ? "NOT NULL" : "NULL 허용";
                                    errorMessages.Add($"NULL 제약 조건 불일치: 설정 '{expectedMsg}', 실제 '{actualMsg}'");
                                }
                                
                                // Primary Key 불일치
                                if (!schemaResult.PrimaryKeyMatches)
                                {
                                    var expectedMsg = schemaResult.ExpectedPrimaryKey == "Y" ? "PK 필수" : "PK 아님";
                                    var actualMsg = schemaResult.ActualPrimaryKey == "Y" ? "PK" : "PK 아님";
                                    errorMessages.Add($"Primary Key 불일치: 설정 '{expectedMsg}', 실제 '{actualMsg}'");
                                }
                                
                                // Unique Key 불일치
                                if (!schemaResult.UniqueKeyMatches)
                                {
                                    var expectedMsg = schemaResult.ExpectedUniqueKey == "Y" ? "UK 필수" : "UK 아님";
                                    var actualMsg = schemaResult.ActualUniqueKey == "Y" ? "UK" : "UK 아님";
                                    errorMessages.Add($"Unique Key 불일치: 설정 '{expectedMsg}', 실제 '{actualMsg}'");
                                }
                                
                                // Foreign Key 불일치
                                if (!schemaResult.ForeignKeyMatches)
                                {
                                    var expectedMsg = schemaResult.ExpectedForeignKey == "Y" ? "FK 필수" : "FK 아님";
                                    var actualMsg = schemaResult.ActualForeignKey == "Y" ? "FK" : "FK 아님";
                                    errorMessages.Add($"Foreign Key 불일치: 설정 '{expectedMsg}', 실제 '{actualMsg}'");
                                }
                            }
                            
                            // 중복값 검사 결과
                            if (schemaResult.DuplicateValueCount > 0)
                            {
                                errorMessages.Add($"UNIQUE KEY 제약 위반: {schemaResult.DuplicateValueCount}개의 중복값 발견");
                                if (schemaResult.DuplicateValues.Any())
                                {
                                    var preview = string.Join(", ", schemaResult.DuplicateValues.Take(3).Select(v => $"'{v}'"));
                                    if (schemaResult.DuplicateValues.Count > 3) preview += "...";
                                    errorMessages.Add($"중복값 예시: {preview}");
                                }
                            }
                            
                            // Domain 검증 결과
                            if (schemaResult.InvalidDomainValueCount > 0)
                            {
                                errorMessages.Add($"Domain 제약 위반: {schemaResult.InvalidDomainValueCount}개의 위반값 발견");
                                if (schemaResult.InvalidDomainValues.Any())
                                {
                                    var preview = string.Join(", ", schemaResult.InvalidDomainValues.Take(3).Select(v => $"'{v}'"));
                                    if (schemaResult.InvalidDomainValues.Count > 3) preview += "...";
                                    errorMessages.Add($"위반값 예시: {preview}");
                                }
                            }
                            
                            // FK 고아 레코드 검사 결과
                            if (schemaResult.OrphanRecordCount > 0)
                            {
                                errorMessages.Add($"FOREIGN KEY 제약 위반: {schemaResult.OrphanRecordCount}개의 고아 레코드 발견");
                                if (schemaResult.OrphanRecordValues.Any())
                                {
                                    var preview = string.Join(", ", schemaResult.OrphanRecordValues.Take(3).Select(v => $"'{v}'"));
                                    if (schemaResult.OrphanRecordValues.Count > 3) preview += "...";
                                    errorMessages.Add($"고아 레코드 예시: {preview}");
                                }
                            }
                            
                            // 상세 정보가 있는 경우 추가
                            if (!string.IsNullOrEmpty(schemaResult.DetailedValidationInfo))
                            {
                                errorMessages.Add($"상세 정보: {schemaResult.DetailedValidationInfo}");
                            }
                            
                            // 각 오류 메시지를 별도 항목으로 추가
                            foreach (var errorMessage in errorMessages)
                            {
                                items.Add(new ValidationResultItem
                                {
                                    Stage = "2단계",
                                    TableName = schemaResult.TableId,
                                    ErrorType = "스키마 검수",
                                    Message = $"컬럼 '{schemaResult.ColumnName}': {errorMessage}"
                                });
                            }
                        }
                        
                        // 기존 오류 메시지도 추가 (중복 방지를 위해 위에서 처리하지 않은 경우만)
                        foreach (var error in schemaResult.Errors)
                        {
                            items.Add(new ValidationResultItem
                            {
                                Stage = "2단계",
                                TableName = schemaResult.TableId,
                                ErrorType = "스키마 검수",
                                Message = $"컬럼 '{schemaResult.ColumnName}': {error}"
                            });
                        }

                        // 경고 메시지 추가
                        foreach (var warning in schemaResult.Warnings)
                        {
                            items.Add(new ValidationResultItem
                            {
                                Stage = "2단계",
                                TableName = schemaResult.TableId,
                                ErrorType = "스키마 검수",
                                Message = $"컬럼 '{schemaResult.ColumnName}': {warning}"
                            });
                        }
                    }
                    
                    _logger?.LogInformation("2단계 처리 완료: {Count}개 항목 추가", items.Count);
                }
                else
                {
                    _logger?.LogWarning("2단계 스키마 검수 결과가 null입니다");
                }

                // 3단계: 지오메트리 검수 결과 처리 (중복 제거)
                if (result.GeometryCheckResult != null)
                {
                    _logger?.LogInformation("3단계 지오메트리 검수 결과 처리 시작");
                    
                    if (result.GeometryCheckResult.GeometryResults != null)
                    {
                        foreach (var geometryResult in result.GeometryCheckResult.GeometryResults)
                        {
                            if (geometryResult.HasError)
                            {
                                items.Add(new ValidationResultItem
                                {
                                    Stage = "3단계",
                                    TableName = geometryResult.TableName ?? geometryResult.TableId,
                                    ErrorType = "지오메트리 오류",
                                    Message = geometryResult.ErrorTypesSummary
                                });
                            }
                        }
                    }
                    
                    _logger?.LogInformation("3단계 처리 완료: {Count}개 항목 추가", items.Count);
                }
                else
                {
                    _logger?.LogWarning("3단계 지오메트리 검수 결과가 null입니다");
                }

                // 4단계: 속성 관계 검수 결과 처리 (순서 변경 반영)
                if (result.AttributeRelationCheckResult != null)
                {
                    _logger?.LogInformation("4단계 속성 관계 검수 결과 처리: {ErrorCount}개 오류, {WarningCount}개 경고", 
                        result.AttributeRelationCheckResult.Errors.Count, result.AttributeRelationCheckResult.Warnings.Count);
                    
                    foreach (var e in result.AttributeRelationCheckResult.Errors)
                    {
                        items.Add(new ValidationResultItem
                        {
                            Stage = "4단계",
                            TableName = string.IsNullOrWhiteSpace(e.TableName) ? "전체" : e.TableName,
                            ErrorType = "속성 관계 오류",
                            Message = e.Message
                        });
                    }

                    foreach (var w in result.AttributeRelationCheckResult.Warnings)
                    {
                        items.Add(new ValidationResultItem
                        {
                            Stage = "4단계",
                            TableName = string.IsNullOrWhiteSpace(w.TableName) ? "전체" : w.TableName,
                            ErrorType = "속성 관계 경고",
                            Message = w.Message
                        });
                    }
                    
                    _logger?.LogInformation("4단계 처리 완료: {Count}개 항목 추가", items.Count);
                }
                else
                {
                    _logger?.LogWarning("4단계 속성 관계 검수 결과가 null입니다");
                }

                // 5단계: 공간 관계 검수 결과 처리
                if (result.RelationCheckResult != null)
                {
                    _logger?.LogInformation("5단계 공간 관계 검수 결과 처리: {ErrorCount}개 오류, {WarningCount}개 경고", 
                        result.RelationCheckResult.Errors.Count, result.RelationCheckResult.Warnings.Count);
                    
                    foreach (var error in result.RelationCheckResult.Errors)
                    {
                        items.Add(new ValidationResultItem
                        {
                            Stage = "5단계",
                            TableName = string.IsNullOrWhiteSpace(error.TableName) ? "전체" : error.TableName,
                            ErrorType = "공간 관계 오류",
                            Message = error.Message
                        });
                    }
                    foreach (var warning in result.RelationCheckResult.Warnings)
                    {
                        items.Add(new ValidationResultItem
                        {
                            Stage = "5단계",
                            TableName = string.IsNullOrWhiteSpace(warning.TableName) ? "전체" : warning.TableName,
                            ErrorType = "공간 관계 오류",
                            Message = warning.Message
                        });
                    }
                    
                    _logger?.LogInformation("5단계 처리 완료: {Count}개 항목 추가", items.Count);
                }
                else
                {
                    _logger?.LogWarning("5단계 공간 관계 검수 결과가 null입니다");
                }

                _logger?.LogInformation("검수 결과 항목 생성 완료: 총 {TotalCount}개 항목", items.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 결과 항목 생성 중 오류 발생");
            }

            return items;
        }

        // 3단계 지오메트리 상세/더블클릭 기능 제거됨
        // private void GeometryResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

        // 통합 상세 보기 버튼 제거됨
        // private void ShowDetails_Click(object sender, RoutedEventArgs e) { }

        // 지오메트리 상세 버튼 제거됨
        // private void ShowGeometryErrorDetails_Click(object sender, RoutedEventArgs e) { }

        // 속성 상세 버튼 제거됨
        // private void ShowAttributeErrorDetails_Click(object sender, RoutedEventArgs e) { }

        /// <summary>
        /// 지오메트리 검수 결과 더블클릭 이벤트 (지도로 이동) - 안전한 예외 처리
        /// </summary>
        private void GeometryResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                _logger.LogInformation("지오메트리 검수 결과 더블클릭 이벤트 시작");

                if (GeometryResultsGrid.SelectedItem is GeometryValidationItem selectedItem)
                {
                    _logger.LogInformation("선택된 항목: {TableId}, {CheckType}, 오류수: {ErrorCount}", 
                        selectedItem.TableId, selectedItem.CheckType, selectedItem.ErrorCount);

                    // 오류가 있는 경우에만 지도로 이동
                    if (selectedItem.HasErrors && selectedItem.ErrorCount > 0)
                    {
                        var errorDetails = selectedItem.ErrorDetails?.Where(e => !string.IsNullOrEmpty(e.ObjectId)).ToList();
                        
                        if (errorDetails != null && errorDetails.Any())
                        {
                            GeometryErrorDetail selectedError;
                            
                            // 오류가 1개면 바로 이동, 여러개면 선택 다이얼로그 표시
                            if (errorDetails.Count == 1)
                            {
                                selectedError = errorDetails.First();
                                _logger.LogInformation("단일 오류로 지도 이동: ObjectId={ObjectId}", selectedError.ObjectId);
                            }
                            else
                            {
                                // 여러 오류가 있을 때 선택 다이얼로그 표시
                                selectedError = ShowErrorSelectionDialog(errorDetails, selectedItem.TableId, selectedItem.CheckType) ?? errorDetails.First();
                                if (selectedError == null)
                                {
                                    _logger.LogInformation("사용자가 오류 선택을 취소했습니다");
                                    return; // 사용자가 취소한 경우
                                }
                                _logger.LogInformation("사용자가 선택한 오류로 지도 이동: ObjectId={ObjectId}", selectedError.ObjectId);
                            }

                            // 지도 이동 기능 제거됨
                        }
                        else
                        {
                            _logger.LogWarning("유효한 오류 객체 정보 없음: TableId={TableId}", selectedItem.TableId);
                            MessageBox.Show("이동할 오류 객체 정보를 찾을 수 없습니다.", "지도 이동", 
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("오류 없는 항목 선택: TableId={TableId}", selectedItem.TableId);
                        MessageBox.Show("오류가 없는 항목입니다. 지도로 이동할 위치가 없습니다.", "지도 이동", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    _logger.LogWarning("선택된 항목이 없거나 잘못된 타입입니다");
                    MessageBox.Show("선택된 항목이 없습니다.", "지도 이동", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 더블클릭 처리 중 심각한 오류 발생");
                
                // 안전한 오류 메시지 표시 (UI 스레드에서)
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show($"지도 이동 처리 중 예상치 못한 오류가 발생했습니다.\n\n" +
                                      $"오류 내용: {ex.Message}\n\n" +
                                      $"프로그램을 다시 시작해보세요.", "심각한 오류", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception dispatcherEx)
                {
                    _logger.LogError(dispatcherEx, "오류 메시지 표시도 실패");
                    // 최후의 수단: 콘솔에 오류 출력
                    Console.WriteLine($"심각한 오류 발생: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 여러 오류가 있을 때 사용자가 선택할 수 있는 다이얼로그를 표시합니다
        /// </summary>
        /// <param name="errorDetails">오류 목록</param>
        /// <param name="tableId">테이블 ID</param>
        /// <param name="checkType">검수 타입</param>
        /// <returns>선택된 오류 (취소 시 null)</returns>
        private GeometryErrorDetail? ShowErrorSelectionDialog(List<GeometryErrorDetail> errorDetails, string tableId, string checkType)
        {
            try
            {
                var dialog = new Window
                {
                    Title = $"오류 선택 - {tableId} ({checkType})",
                    Width = 600,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.CanResize
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 오류 목록 표시
                var listBox = new ListBox
                {
                    Margin = new Thickness(10),
                    ItemsSource = errorDetails.Select((error, index) => new
                    {
                        Index = index + 1,
                        ObjectId = error.ObjectId,
                        ErrorValue = error.ErrorValue,
                        Location = error.Location,
                        DetailMessage = error.DetailMessage,
                        Error = error
                    }).ToList()
                };

                // 리스트박스 아이템 템플릿 설정
                var template = new DataTemplate();
                var factory = new FrameworkElementFactory(typeof(StackPanel));
                factory.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
                factory.SetValue(StackPanel.MarginProperty, new Thickness(5));

                var titleFactory = new FrameworkElementFactory(typeof(TextBlock));
                titleFactory.SetBinding(TextBlock.TextProperty, new Binding("ObjectId") 
                { 
                    StringFormat = "#{0} OBJECTID: {1}" 
                });
                titleFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                factory.AppendChild(titleFactory);

                var detailFactory = new FrameworkElementFactory(typeof(TextBlock));
                detailFactory.SetBinding(TextBlock.TextProperty, new Binding("DetailMessage"));
                detailFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
                detailFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
                factory.AppendChild(detailFactory);

                var locationFactory = new FrameworkElementFactory(typeof(TextBlock));
                locationFactory.SetBinding(TextBlock.TextProperty, new Binding("Location") 
                { 
                    StringFormat = "위치: {0}" 
                });
                locationFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
                locationFactory.SetValue(TextBlock.ForegroundProperty, Brushes.DarkBlue);
                locationFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
                factory.AppendChild(locationFactory);

                template.VisualTree = factory;
                listBox.ItemTemplate = template;

                Grid.SetRow(listBox, 0);
                grid.Children.Add(listBox);

                // 버튼 패널
                var buttonPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };

                var okButton = new Button
                {
                    Content = "이동",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsDefault = true
                };

                var cancelButton = new Button
                {
                    Content = "취소",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsCancel = true
                };

                GeometryErrorDetail? selectedError = null;

                okButton.Click += (s, e) =>
                {
                    if (listBox.SelectedItem != null)
                    {
                        var selectedItem = (dynamic)listBox.SelectedItem;
                        selectedError = selectedItem.Error;
                        dialog.DialogResult = true;
                    }
                    else
                    {
                        MessageBox.Show("이동할 오류를 선택해주세요.", "선택 필요", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                };

                cancelButton.Click += (s, e) =>
                {
                    dialog.DialogResult = false;
                };

                // 더블클릭으로도 선택 가능
                listBox.MouseDoubleClick += (s, e) =>
                {
                    if (listBox.SelectedItem != null)
                    {
                        var selectedItem = (dynamic)listBox.SelectedItem;
                        selectedError = selectedItem.Error;
                        dialog.DialogResult = true;
                    }
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);

                Grid.SetRow(buttonPanel, 1);
                grid.Children.Add(buttonPanel);

                dialog.Content = grid;

                // 첫 번째 항목 자동 선택
                if (errorDetails.Any())
                {
                    listBox.SelectedIndex = 0;
                }

                var result = dialog.ShowDialog();
                return result == true ? selectedError : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 선택 다이얼로그 표시 실패");
                MessageBox.Show($"오류 선택 다이얼로그를 표시할 수 없습니다.\n\n오류: {ex.Message}", 
                    "다이얼로그 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // 실패 시 첫 번째 오류 반환
                return errorDetails.FirstOrDefault();
            }
        }

        /// <summary>
        /// 실제 검수 결과를 로드합니다
        /// </summary>
        private void LoadRealValidationResults()
        {
            // 기본 상태 표시
            ValidationStatusText.Text = "검수 결과 없음";
            ElapsedTimeText.Text = "0초";
            ErrorCountText.Text = "0";
            WarningCountText.Text = "0";

            // 빈 결과 목록 초기화
            _allResults = new List<ValidationResultItem>();
            ResultsDataGrid.ItemsSource = _allResults;
            
            // 실제 검수 결과가 있으면 로드 시도
            try
            {
                // 최근 검수 결과를 데이터베이스에서 로드 시도
                LoadRecentValidationResult();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "최근 검수 결과 로드 실패");
            }
        }

        /// <summary>
        /// 최근 검수 결과를 데이터베이스에서 로드합니다
        /// </summary>
        private async void LoadRecentValidationResult()
        {
            try
            {
                // ValidationResultService를 통해 최근 결과 조회
                // 실제 구현에서는 DI를 통해 서비스를 주입받아야 하지만,
                // 현재는 간단한 구현으로 처리
                
                // TODO: 실제 데이터베이스에서 최근 검수 결과를 로드하는 로직 구현
                // 현재는 빈 결과로 초기화
                _logger?.LogInformation("최근 검수 결과 로드 시도 (현재는 빈 결과로 초기화)");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "최근 검수 결과 로드 중 오류 발생");
            }
        }

        // 기타 이벤트 핸들러들 (간단한 구현)
        // 결과 내보내기 기능 제거됨

        private void ExportToCsv(string fileName)
        {
            var lines = new List<string>
            {
                "검수단계,테이블명,오류유형,메시지"
            };

            foreach (var item in _allResults)
            {
                lines.Add($"\"{item.Stage}\",\"{item.TableName}\",\"{item.ErrorType}\",\"{item.Message}\"");
            }

            System.IO.File.WriteAllLines(fileName, lines, System.Text.Encoding.UTF8);
        }

        private void ExportToJson(string fileName)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_allResults, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            System.IO.File.WriteAllText(fileName, json, System.Text.Encoding.UTF8);
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedFilter();
        }

        private void NavigateToMapView(ValidationResultItem item)
        {
            // 지도 기능 제거: 동작 없음
        }

        // 3단계 지오메트리 상세/더블클릭 기능 제거됨
        // private void ShowGeometryErrorDetails_Click(object sender, RoutedEventArgs e) { }

        // 속성 상세 버튼 제거됨
        // private void ShowAttributeErrorDetails_Click(object sender, RoutedEventArgs e) { }

        // 상세 정보 표시용 TextBlock을 생성합니다
        private StackPanel CreateDetailTextBlock(string label, string value, bool isMultiLine = false)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

            var labelBlock = new TextBlock
            {
                Text = $"{label}:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            panel.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                TextWrapping = isMultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
                Padding = new Thickness(8, 8, 8, 8)
            };
            panel.Children.Add(valueBlock);

            return panel;
        }
    }
}