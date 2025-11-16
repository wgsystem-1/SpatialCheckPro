#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.Constants;

using SpatialCheckPro.Services;
using WinForms = System.Windows.Forms;
using SpatialCheckPro.GUI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpatialCheckPro.Services.RemainingTime;

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// 메인 윈도우 클래스
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow>? _logger;
        private readonly SimpleValidationService? _validationService;
        private readonly QcErrorService? _qcErrorService;
        private readonly DispatcherTimer _timer;
        private StageSummaryCollectionViewModel _stageSummaryCollectionViewModel;
        private AlertAggregationService _alertAggregationService;
        private SpatialCheckPro.Models.ValidationResult? _currentValidationResult;
        private Views.ValidationSettingsView? _validationSettingsView;
        private string? _selectedFilePath;
        private List<string>? _selectedFilePaths;
        
        // 검수 설정 파일 경로들
        private string? _tableConfigPath;
        private string? _schemaConfigPath;
        private string? _geometryConfigPath;
        private string? _relationConfigPath;
        private string? _attributeConfigPath;
        
        // 사용자 정의 검수 설정 파일 경로들
        private string? _customTableConfigPath;
        private string? _customSchemaConfigPath;
        private string? _customGeometryConfigPath;
        private string? _customRelationConfigPath;
        private string? _customAttributeConfigPath;
        private string? _customGeometryCriteriaPath;
        private string? _customCodelistPath = null;

        // 단계별 사용 플래그 (기본값: 모두 사용)
        private bool _enableStage1 = true;
        private bool _enableStage2 = true;
        private bool _enableStage3 = true;
        private bool _enableStage4 = true;
        private bool _enableStage5 = true;

        // 설정창에서 가져온 행 단위 선택 목록 (있으면 실행 시 우선 적용)
        internal List<SpatialCheckPro.Models.Config.TableCheckConfig>? _selectedStage1Items;
        internal List<SpatialCheckPro.Models.Config.SchemaCheckConfig>? _selectedStage2Items;
        internal List<SpatialCheckPro.Models.Config.GeometryCheckConfig>? _selectedStage3Items;
        internal List<SpatialCheckPro.Models.Config.AttributeCheckConfig>? _selectedStage4Items;
        internal List<SpatialCheckPro.Models.Config.RelationCheckConfig>? _selectedStage5Items;
        
        private ValidationSettingsViewModel _validationSettingsViewModel;


        public MainWindow(
            ILogger<MainWindow> logger,
            SimpleValidationService validationService,
            QcErrorService qcErrorService,
            StageSummaryCollectionViewModel stageSummaryCollectionViewModel,
            AlertAggregationService alertAggregationService)
        {
            string debugLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
            File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow 생성자 시작\n");

            InitializeComponent();

            // DI를 통해 주입받은 서비스들 설정
            _logger = logger;
            _validationService = validationService;
            _qcErrorService = qcErrorService;
            _stageSummaryCollectionViewModel = stageSummaryCollectionViewModel;
            _alertAggregationService = alertAggregationService;
            
            // WelcomeView 이벤트 연결
            if (WelcomeView != null)
            {
                WelcomeView.QuickStartRequested += (s, e) => NavigateToFileSelection(s, new RoutedEventArgs());
            }

            File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DI 서비스 주입 완료 - ValidationService: {_validationService != null}, QcErrorService: {_qcErrorService != null}, StageSummaryCollectionViewModel: {_stageSummaryCollectionViewModel != null}, AlertAggregationService: {_alertAggregationService != null}\n");
            
            try
            {
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 어셈블리 정보 로드 시작\n");
                
                // 어셈블리 정보의 InformationalVersion에 포함된 빌드 시각을 표시
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    var attrs = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                    string? info = null;
                    if (attrs != null && attrs.Length > 0)
                    {
                        info = ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                    }
                    if (!string.IsNullOrWhiteSpace(info) && VersionText != null)
                    {
                        VersionText.Text = info;
                    }
                    File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 어셈블리 정보 로드 완료\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 어셈블리 정보 로드 실패: {ex.Message}\n");
                }

                _logger?.LogInformation("DI를 통한 서비스 초기화 완료");
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DI 서비스 초기화 성공\n");
                
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 타이머 시작\n");
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Tick += Timer_Tick;
                _timer.Start();
                
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ValidationSettingsView 초기화 시작\n");
                // ValidationSettingsView 이벤트 구독
                InitializeValidationSettingsView();
                
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 기본 설정 파일 경로 초기화 시작\n");
                // 기본 설정 파일 경로 설정
                InitializeDefaultConfigPaths();
                
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 상태 업데이트 시작\n");
                UpdateStatus("국가기본도 DB 검수 시스템이 시작되었습니다.");

                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ValidationSettingsViewModel 생성 시작\n");
                _validationSettingsViewModel = new ValidationSettingsViewModel();
                // ValidationSettingsView가 아직 로드되지 않았을 수 있으므로 안전하게 처리
                if (_validationSettingsView != null)
                {
                    File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ValidationSettingsView DataContext 설정\n");
                    // 즉시 바인딩 가능 시 바로 적용
                    _validationSettingsView.DataContext = _validationSettingsViewModel;
                }
                else
                {
                    File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Loaded 이벤트에 핸들러 등록\n");
                    // 로드 이후에 뷰를 찾아 데이터컨텍스트를 설정 (1회성)
                    this.Loaded += MainWindow_SetSettingsDataContextOnceOnLoaded;
                }
                
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow 생성자 완료\n");
            }
            catch (Exception ex)
            {
                // MainWindow 생성자에서 예외 발생 시 최소한의 처리
                System.Diagnostics.Debug.WriteLine($"MainWindow 초기화 오류: {ex.Message}");
                
                // 최소한의 타이머만 설정
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Tick += Timer_Tick;
                _timer.Start();
                
                MessageBox.Show($"초기화 중 오류가 발생했습니다:\n{ex.Message}", 
                    "초기화 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 타이머 틱 이벤트 처리
        /// </summary>
        private void Timer_Tick(object? sender, EventArgs e)
        {
            TimeText.Text = DateTime.Now.ToString(DateTimeFormats.Display);
        }

        /// <summary>
        /// 상태 메시지를 업데이트합니다
        /// </summary>
        private void UpdateStatus(string message)
        {
            try
            {
                if (SystemStatusText != null)
                {
                    SystemStatusText.Text = message;
                }
                _logger?.LogInformation(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"상태 업데이트 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// ValidationSettingsView 초기화 및 이벤트 구독
        /// </summary>
        private void InitializeValidationSettingsView()
        {
            try
            {
                // ValidationSettingsView 찾기
                _validationSettingsView = FindValidationSettingsView();
                
                if (_validationSettingsView != null)
                {
                    // 성능 설정 변경 이벤트 구독
                    // _validationSettingsView.PerformanceSettingsChanged += OnPerformanceSettingsChanged;
                    _logger?.LogInformation("ValidationSettingsView 이벤트 구독 완료");
                }
                else
                {
                    _logger?.LogWarning("ValidationSettingsView를 찾을 수 없습니다");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ValidationSettingsView 초기화 실패");
            }
        }

        /// <summary>
        /// ValidationSettingsView 인스턴스 찾기 (필요시에만 생성)
        /// </summary>
        private Views.ValidationSettingsView? FindValidationSettingsView()
        {
            try
            {
                // 설정 화면은 필요할 때만 생성하고 표시하지 않음
                // 실제 표시는 ValidationSettings_Click에서 처리
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ValidationSettingsView 찾기 실패");
                return null;
            }
        }

        /// <summary>
        /// 로드 완료 후 한 번만 ValidationSettingsView의 DataContext를 설정합니다
        /// </summary>
        private void MainWindow_SetSettingsDataContextOnceOnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_validationSettingsView == null)
                {
                    _validationSettingsView = FindValidationSettingsView();
                }
                if (_validationSettingsView != null && _validationSettingsViewModel != null)
                {
                    _validationSettingsView.DataContext = _validationSettingsViewModel;
                }
            }
            catch { }
            finally
            {
                // 1회성 처리 후 핸들러 제거
                this.Loaded -= MainWindow_SetSettingsDataContextOnceOnLoaded;
            }
        }

        /// <summary>
        /// 시각적 트리에서 특정 타입의 자식 요소 찾기
        /// </summary>
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        /// <summary>
        /// 성능 설정 변경 이벤트 핸들러
        /// </summary>
        private void OnPerformanceSettingsChanged(object? sender, Views.PerformanceSettingsChangedEventArgs e)
        {
            try
            {
                if (_validationService != null)
                {
                    // SimpleValidationService의 PerformanceSettings 업데이트
                    _validationService.UpdatePerformanceSettings(e.EnableParallelProcessing, e.MaxParallelism, e.BatchSize);
                    _logger?.LogInformation("성능 설정 업데이트: 병렬처리={ParallelProcessing}, 병렬도={Parallelism}, 배치크기={BatchSize}", 
                        e.EnableParallelProcessing, e.MaxParallelism, e.BatchSize);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "성능 설정 업데이트 실패");
            }
        }
        private void InitializeDefaultConfigPaths()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var configDirectory = Path.Combine(appDirectory, "Config");
            
            _tableConfigPath = Path.Combine(configDirectory, "1_table_check.csv");
            _schemaConfigPath = Path.Combine(configDirectory, "2_schema_check.csv");
            _geometryConfigPath = Path.Combine(configDirectory, "3_geometry_check.csv");
            _attributeConfigPath = Path.Combine(configDirectory, "4_attribute_check.csv");
            _relationConfigPath = Path.Combine(configDirectory, "5_relation_check.csv");
        }

        #region 네비게이션 메서드

        /// <summary>
        /// 파일 선택 화면으로 이동하고 폴더 선택 대화상자를 엽니다.
        /// </summary>
        private void NavigateToFileSelection(object sender, RoutedEventArgs e)
        {
            // 먼저 파일 선택 화면 UI를 구성하고 표시합니다.
            ShowFileSelectionView();
            
            // 이어서 폴더 선택 대화상자를 즉시 엽니다.
            // 사용자가 폴더를 선택하면 UI가 자동으로 새로고침됩니다.
            ShowFolderBrowserAndRefresh();
        }

        /// <summary>
        /// 검수 진행 화면으로 이동합니다
        /// </summary>
        private async void NavigateToValidation(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                MessageBox.Show("먼저 검수할 파일을 선택해주세요.", "파일 미선택", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateToFileSelection(sender, e);
                return;
            }

			// 메인 컨텐츠 영역에 진행 화면 표시

            // CompactValidationProgressView 생성 및 설정
            var progressView = new Views.CompactValidationProgressView
            {
                DataContext = _stageSummaryCollectionViewModel
            };
            progressView.ValidationStopRequested += (s, args) => StopValidation();
            
            // 예측 모델 적용
            try
            {
                await ApplyPredictedTimesToProgressView(progressView);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "예측 시간 적용 실패");
            }
            
            MainContentContainer.Content = progressView;
            UpdateNavigationButtons("Validation");
            
            // 검수 시작: 단일/배치 분기
            if (_selectedFilePaths != null && _selectedFilePaths.Count > 0)
            {
                _ = StartBatchValidationAsync(progressView);
            }
            else
            {
                _ = StartValidationAsync(progressView);
            }
        }

        /// <summary>
        /// 예측 시간을 진행 뷰에 적용합니다
        /// </summary>
        private async Task ApplyPredictedTimesToProgressView(Views.CompactValidationProgressView progressView)
        {
            try
            {
                // 선택된 파일 분석
                if (string.IsNullOrEmpty(_selectedFilePath)) return;
                
                // 기본값 설정 (GDB 열기 실패 시 사용)
                int tableCount = 52; // 로그에서 확인된 평균값
                int featureCount = 10000; // 예상 피처 수
                int schemaFieldCount = 450; // 예상 필드 수
                int geometryCheckCount = 232; // 설정 기반
                int relationRuleCount = 100; // 예상 규칙 수
                int attributeColumnCount = 0; // 속성 검수 비활성화
                
                // GDB에서 실제 정보 추출 시도
                if (_selectedFilePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var dataSource = OSGeo.OGR.Ogr.Open(_selectedFilePath, 0))
                        {
                            if (dataSource != null)
                            {
                                tableCount = dataSource.GetLayerCount();
                                featureCount = 0;
                                for (int i = 0; i < tableCount; i++)
                                {
                                    using (var layer = dataSource.GetLayerByIndex(i))
                                    {
                                        if (layer != null)
                                        {
                                            featureCount += (int)layer.GetFeatureCount(1);
                                        }
                                    }
                                }
                            }
                        }
                        
                        _logger?.LogInformation("GDB 분석 완료 - 테이블: {Tables}개, 피처: {Features}개", 
                            tableCount, featureCount);
                    }
                    catch (Exception gdbEx)
                    {
                        _logger?.LogWarning(gdbEx, "GDB 분석 실패, 기본값 사용");
                    }
                }
                
                // 메트릭 수집기에서 예측 시간 가져오기 (우선)
                Dictionary<int, double>? predictedTimes = null;
                
                var metricsCollector = ((App)Application.Current).GetService<ValidationMetricsCollector>();
                if (metricsCollector != null)
                {
                    predictedTimes = metricsCollector.GetStagePredictions(tableCount, featureCount);
                    _logger?.LogInformation("메트릭 기반 예측 시간 사용");
                }
                
                // 메트릭이 없으면 기존 예측 모델 사용
                if (predictedTimes == null || predictedTimes.Count == 0)
                {
                    var predictor = new Models.ValidationTimePredictor(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<Models.ValidationTimePredictor>.Instance);
                    
                    predictedTimes = predictor.PredictStageTimes(
                        tableCount, featureCount, schemaFieldCount,
                        geometryCheckCount, relationRuleCount, attributeColumnCount);
                    _logger?.LogInformation("기본 예측 모델 사용");
                }
                
                // 예측 시간 로그
                _logger?.LogInformation("예측 시간 계산 완료:");
                foreach (var kvp in predictedTimes)
                {
                    _logger?.LogInformation("  {Stage}단계: {Time:F1}초", kvp.Key, kvp.Value);
                }
                
                var metadata = new Dictionary<string, string>
                {
                    { "SchemaFieldCount", schemaFieldCount.ToString() },
                    { "GeometryCheckCount", geometryCheckCount.ToString() },
                    { "RelationRuleCount", relationRuleCount.ToString() }
                };

                foreach (var definition in SpatialCheckPro.GUI.Constants.StageDefinitions.All)
                {
                    metadata[$"StageId_{definition.StageNumber}"] = definition.StageId;
                    metadata[$"StageName_{definition.StageNumber}"] = definition.StageName;
                }

                var context = new SpatialCheckPro.Services.RemainingTime.Models.ValidationRunContext
                {
                    TargetFilePath = _selectedFilePath,
                    FileSizeBytes = File.Exists(_selectedFilePath) ? new FileInfo(_selectedFilePath).Length : 0,
                    FeatureCount = featureCount,
                    LayerCount = tableCount,
                    Metadata = metadata
                };

                _stageSummaryCollectionViewModel.InitializeEta(predictedTimes, context);
                
                _logger?.LogInformation("예측 시간 적용 완료 - 테이블: {Tables}개, 피처: {Features}개", 
                    tableCount, featureCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "예측 시간 계산 중 오류");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 지도 뷰어 화면으로 이동합니다
        /// </summary>
        private async void NavigateToMapView(object sender, RoutedEventArgs e)
        {
            // 지도 기능 제거: 안내 메시지로 대체
            UpdateStatus("지도 기능이 비활성화되었습니다");
            MessageBox.Show("지도 뷰어 기능이 비활성화되었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 검수 결과 화면으로 이동합니다
        /// </summary>
		private void NavigateToResults(object sender, RoutedEventArgs e)
        {
			// 결과가 없는 경우 안내 후 종료
			if (_currentValidationResult == null)
			{
				MessageBox.Show("아직 결과가 존재하지 않습니다.", "검수 결과 없음", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			var resultView = new Views.ValidationResultView();
			// 현재 검수 결과 설정
			resultView.SetValidationResult(_currentValidationResult);

			// 메인 컨텐츠 영역에 결과 화면 표시
			MainContentContainer.Content = resultView;
			UpdateNavigationButtons("Results");
        }

        /// <summary>
        /// 보고서 화면으로 이동합니다
        /// </summary>
		private void NavigateToReports(object sender, RoutedEventArgs e)
        {
			var reportView = new Views.ReportView();
			
			// 현재 검수 결과가 있으면 설정
			if (_currentValidationResult != null)
			{
				reportView.SetValidationResult(_currentValidationResult);
			}
			
			// 메인 컨텐츠 영역에 보고서 화면 표시
			MainContentContainer.Content = reportView;
			UpdateNavigationButtons("Reports");
        }

        /// <summary>
        /// 네비게이션 버튼 상태를 업데이트합니다
        /// </summary>
        private void UpdateNavigationButtons(string activeSection)
        {
            // 모든 버튼을 기본 상태로 설정
            FileSelectionNavButton.Background = System.Windows.Media.Brushes.Transparent;
            ValidationNavButton.Background = System.Windows.Media.Brushes.Transparent;
            ResultsNavButton.Background = System.Windows.Media.Brushes.Transparent;
            // 지도 버튼은 없는 레이아웃일 수 있으므로 동적 조회
            (this.FindName("MapViewNavButton") as System.Windows.Controls.Button)?.SetValue(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            ReportsNavButton.Background = System.Windows.Media.Brushes.Transparent;

            // 활성 버튼 강조: 사이드바 호버색상으로 표시
            switch (activeSection)
            {
                case "FileSelection":
                    FileSelectionNavButton.Background = (System.Windows.Media.Brush)FindResource("SidebarHoverColor");
                    break;
                case "Validation":
                    ValidationNavButton.Background = (System.Windows.Media.Brush)FindResource("SidebarHoverColor");
                    break;
                case "Results":
                    ResultsNavButton.Background = (System.Windows.Media.Brush)FindResource("SidebarHoverColor");
                    break;
                case "MapView":
                    (this.FindName("MapViewNavButton") as System.Windows.Controls.Button)?.SetValue(System.Windows.Controls.Control.BackgroundProperty, FindResource("SidebarHoverColor"));
                    break;
                case "Reports":
                    ReportsNavButton.Background = (System.Windows.Media.Brush)FindResource("SidebarHoverColor");
                    break;
            }
        }

        #endregion

        /// <summary>
        /// 외부에서 경로를 받아 즉시 검수를 시작합니다.
        /// </summary>
        /// <param name="targetPath">검수 대상 GDB 경로</param>
        public async System.Threading.Tasks.Task StartValidationForPathAsync(string targetPath)
        {
            try
            {
                _selectedFilePath = targetPath;
                _selectedFilePaths = null;

                // 메인 컨텐츠 영역에 진행 화면 표시
                var progressView = new Views.CompactValidationProgressView
                {
                    DataContext = _stageSummaryCollectionViewModel
                };
                progressView.ValidationStopRequested += (s, args) => StopValidation();

                try
                {
                    await ApplyPredictedTimesToProgressView(progressView);
                }
                catch { }

                MainContentContainer.Content = progressView;
                UpdateNavigationButtons("Validation");

                _ = StartValidationAsync(progressView);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "자동 검수 시작 실패");
                MessageBox.Show($"자동 검수 시작 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region 파일 선택 및 검수

        /// <summary>
        /// 파일 선택 UI를 표시합니다
        /// </summary>
        private void ShowFileSelectionView()
        {
			// 메인 컨텐츠 영역에 파일 선택 화면 표시
            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var title = new TextBlock
            {
                Text = "폴더 선택",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);
            
            var description = new TextBlock
            {
                Text = "검수할 File Geodatabase(.gdb) 폴더 또는 상위 폴더를 선택하세요",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(description);
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            // 파일 선택 버튼 (폴더 아이콘 포함)
            var selectButtonContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            var selectIcon = new TextBlock
            {
                Text = "\uE8B7", // Segoe MDL2 Assets 폴더 아이콘
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            selectButtonContent.Children.Add(selectIcon);
            
            var selectText = new TextBlock
            {
                Text = "폴더 선택",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            selectButtonContent.Children.Add(selectText);
            
            var selectButton = new Button
            {
                Content = selectButtonContent,
                Padding = new Thickness(20, 10, 20, 10),
                Background = System.Windows.Media.Brushes.DodgerBlue,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 10, 0)
            };
            selectButton.Click += (s, args) => ShowFolderBrowserAndRefresh();
            buttonPanel.Children.Add(selectButton);
            
            // 선택된 파일이 있으면 검수 시작 버튼 표시
            if (!string.IsNullOrEmpty(_selectedFilePath))
            {
                // 검수 시작 버튼 (재생 아이콘 포함)
                var startButtonContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                var startIcon = new TextBlock
                {
                    Text = "\uE768", // Segoe MDL2 Assets 재생 아이콘
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                startButtonContent.Children.Add(startIcon);
                
                var startText = new TextBlock
                {
                    Text = "검수 시작",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                startButtonContent.Children.Add(startText);
                
                var startButton = new Button
                {
                    Content = startButtonContent,
                    Padding = new Thickness(20, 10, 20, 10),
                    Background = System.Windows.Media.Brushes.Green,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                startButton.Click += (s, args) => NavigateToValidation(s, args);
                buttonPanel.Children.Add(startButton);
                
                // 선택된 파일 정보 표시
                var fileInfo = new TextBlock
                {
                    Text = $"선택된 폴더: {Path.GetFileName(_selectedFilePath)}",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    Foreground = System.Windows.Media.Brushes.Green
                };
                content.Children.Insert(content.Children.Count - 1, fileInfo);

                // 배치 대상 개수 표시 (.gdb 수)
                if (_selectedFilePaths != null && _selectedFilePaths.Count > 0)
                {
                    var countInfo = new TextBlock
                    {
                        Text = $"선택된 .gdb 수: {_selectedFilePaths.Count}",
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 10, 0, 20),
                        Foreground = System.Windows.Media.Brushes.DarkSlateBlue
                    };
                    content.Children.Insert(content.Children.Count - 1, countInfo);
                }
            }
            
            content.Children.Add(buttonPanel);
            
            MainContentContainer.Content = content;
            UpdateNavigationButtons("FileSelection");
        }

        /// <summary>
        /// 폴더 선택 대화상자를 열고, 선택 결과에 따라 UI를 새로고침합니다.
        /// </summary>
        private void ShowFolderBrowserAndRefresh()
        {
            // File Geodatabase 폴더 선택
            string? selectedPath = null;
            
            using (var folderDialog = new WinForms.FolderBrowserDialog())
            {
                folderDialog.Description = "File Geodatabase(.gdb) 또는 이를 포함한 상위 폴더를 선택하세요";
                folderDialog.ShowNewFolderButton = false;

                if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    var selectedFolder = folderDialog.SelectedPath;
                    
                    // 미리보기 다이얼로그 표시
                    var previewDialog = new Views.FolderSelectionPreviewDialog(selectedFolder)
                    {
                        Owner = this
                    };
                    
                    if (previewDialog.ShowDialog() == true && previewDialog.IsContinue)
                    {
                        // 사용자가 계속하기를 선택한 경우
                        selectedPath = selectedFolder;
                        _selectedFilePaths = previewDialog.SelectedGdbPaths;
                        
                        if (_selectedFilePaths.Count == 0)
                        {
                            MessageBox.Show(
                                "검수 대상 FileGDB가 없습니다.",
                                "대상 없음",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                        
                        _logger?.LogInformation("폴더 선택 확정 - 대상: {Count}개 FileGDB", _selectedFilePaths.Count);
                    }
                    else
                    {
                        // 사용자가 취소한 경우
                        _logger?.LogInformation("사용자가 폴더 선택을 취소했습니다");
                        return;
                    }
                }
            }

            // 선택된 경로가 있으면 처리
            if (!string.IsNullOrEmpty(selectedPath))
            {
                _selectedFilePath = selectedPath;
                var fileName = Path.GetFileName(selectedPath);
                var fileType = Directory.Exists(selectedPath) ? "폴더" : "파일";
                
                if (_selectedFilePaths != null && _selectedFilePaths.Count > 1)
                {
                    UpdateStatus($"상위 {fileType}이 선택되었습니다: {fileName} (.gdb {_selectedFilePaths.Count}개)");
                }
                else
                {
                    UpdateStatus($"{fileType}이 선택되었습니다: {fileName}");
                }
                
                // 파일 선택 화면 새로고침
                ShowFileSelectionView();
            }
        }

        #endregion

        #region 검수 실행

        private Views.CompactValidationProgressView? _currentProgressView;
        private System.Threading.CancellationTokenSource? _validationCancellationTokenSource;
        private bool _isValidationRunning = false;

        /// <summary>
        /// 검수를 시작합니다
        /// </summary>
        private async Task StartValidationAsync(Views.CompactValidationProgressView progressView)
        {
            if (_isValidationRunning)
            {
                MessageBox.Show("이미 검수가 진행 중입니다.", "검수 진행 중", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 검수 서비스가 초기화되지 않은 경우 처리
            if (_validationService == null || _stageSummaryCollectionViewModel == null)
            {
                MessageBox.Show("검수 서비스를 사용할 수 없습니다. 다시 시도해주세요.", "서비스 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentProgressView = progressView;
            _validationCancellationTokenSource = new System.Threading.CancellationTokenSource();
            _isValidationRunning = true;

            // 진행률 핸들러를 finally에서 해제할 수 있도록 메서드 스코프에 선언
            EventHandler<SpatialCheckPro.GUI.Services.ValidationProgressEventArgs>? progressHandlerLocal = null;

            try
            {
                UpdateStatus("검수를 시작합니다...");
                // 로그 UI 제거로 주석 처리
                // progressView.AddLogMessage("검수 시작");
                // progressView.AddLogMessage($"대상 파일: {_selectedFilePath}");
                
                // 검수 시작 시간 초기화 (단계 상세 정보의 처리 속도 계산을 위해)
                progressView.ResetStartTime();
                var startTime = DateTime.Now;
                
                // 소요시간 업데이트 타이머 (1초 간격) - 스무스한 업데이트를 위해 Normal 우선순위 사용
                var progressTimer = new DispatcherTimer(DispatcherPriority.Normal);
                progressTimer.Interval = TimeSpan.FromSeconds(1); // 1초 간격
                progressTimer.Tick += (s, e) => 
                {
                    var elapsed = DateTime.Now - startTime;
                    // BeginInvoke로 비동기 호출하여 UI 스레드 블로킹 방지 및 스무스한 업데이트 보장
                    Dispatcher.BeginInvoke(new Action(() => progressView.UpdateElapsedTime(elapsed)), DispatcherPriority.Normal);
                };
                progressTimer.Start();

                // 검수 서비스의 진행률 이벤트 구독 (정상 해제를 위해 핸들러 보관)
                int cumulativeErrorCount = 0;
                progressHandlerLocal = (sender, args) =>
                {
                    // Invoke로 동기 호출하여 즉시 반영 (우선순위 높임)
                    Dispatcher.Invoke(() =>
                    {
                        System.Console.WriteLine($">>> [ProgressHandler] Stage={args.CurrentStage}, StageName={args.StageName}, Progress={args.StageProgress:F1}%, Completed={args.IsStageCompleted}, ProcessedUnits={args.ProcessedUnits}, TotalUnits={args.TotalUnits}");
                        
                        // [해결 방향 1] 호출 순서 변경: ApplyProgress를 먼저 호출하여 CompletedStageCount를 먼저 업데이트
                        // 단계 진행 상황을 StageSummaryCollectionViewModel에 반영 (먼저 호출)
                        _stageSummaryCollectionViewModel.ApplyProgress(args);
                        
                        // 단계 완료 시 상태 강제 설정
                        if (args.IsStageCompleted)
                        {
                            System.Console.WriteLine($">>> [ProgressHandler] ✅ 단계 {args.CurrentStage} 완료! PartialResult={args.PartialResult != null}");
                            System.Console.WriteLine($">>> [ProgressHandler] 단계 상태 업데이트 시작: Stage={args.CurrentStage}");
                            
                            if (args.IsStageSkipped)
                            {
                                _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, StageStatus.Skipped, args.StatusMessage);
                            }
                            else
                            {
                                var finalStatus = args.IsStageSuccessful ? StageStatus.Completed : StageStatus.Failed;
                                _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, finalStatus, args.StatusMessage);
                            }

                            // 다음 단계 대기 상태 설정
                            var nextStageNumber = args.CurrentStage + 1;
                            var nextStage = _stageSummaryCollectionViewModel.GetStage(nextStageNumber);
                            if (nextStage != null && nextStage.Status == StageStatus.NotStarted)
                            {
                                _stageSummaryCollectionViewModel.ForceStageStatus(nextStageNumber, StageStatus.Pending, "대기 중");
                            }
                            
                            // 부분 결과 저장 및 진행 화면에 표시
                            if (args.PartialResult != null)
                            {
                                _currentValidationResult = args.PartialResult;
                                System.Console.WriteLine($">>> [ProgressHandler] 부분 결과 저장: ErrorCount={_currentValidationResult.ErrorCount}");
                                
                                // 진행 화면에 부분 결과 실시간 표시
                                try
                                {
                                    progressView.UpdatePartialResults(_currentValidationResult);
                                    System.Console.WriteLine($">>> [ProgressHandler] UpdatePartialResults 호출 완료");
                                }
                                catch (Exception ex)
                                {
                                    System.Console.WriteLine($">>> [ProgressHandler] ❌ UpdatePartialResults 오류: {ex.Message}");
                                }
                            }
                            else
                            {
                                System.Console.WriteLine($">>> [ProgressHandler] ⚠️ PartialResult가 null!");
                            }
                            
                            // 단계 완료 시 오류 카운트 누적
                            if (args.ErrorCount > 0)
                            {
                                cumulativeErrorCount += args.ErrorCount;
                                progressView.UpdateErrorCount(cumulativeErrorCount);
                            }
                            
                            // 완료 단계 카운터 확인 (업데이트 후)
                            var completedCount = _stageSummaryCollectionViewModel.CompletedStageCount;
                            System.Console.WriteLine($">>> [ProgressHandler] 완료 단계 카운터: {completedCount}/6");
                        }
                        
                        // 전체 진행률 업데이트 (ApplyProgress 이후에 호출하여 최신 CompletedStageCount 반영)
                        progressView.UpdateProgress(args.OverallProgress, args.StatusMessage);
                        
                        // 현재 단계 업데이트
                        progressView.UpdateCurrentStage(args.StageName, args.CurrentStage);

                        // 개별 단계 진행률 갱신
                        try { progressView.UpdateStageProgress(args.CurrentStage, args.StageProgress); } catch { }
                        
                        // 단위 정보가 제공되면 상세 정보 업데이트 (조건 완화)
                        try
                        {
                            if (args.ProcessedUnits >= 0 && args.TotalUnits >= 0)
                            {
                                System.Console.WriteLine($">>> [ProgressHandler] UpdateUnits 호출: Stage={args.CurrentStage}, {args.ProcessedUnits}/{args.TotalUnits}");
                                progressView.UpdateUnits(args.CurrentStage, args.ProcessedUnits, args.TotalUnits);
                            }
                            else
                            {
                                System.Console.WriteLine($">>> [ProgressHandler] ⚠️ 단위 정보 부족: ProcessedUnits={args.ProcessedUnits}, TotalUnits={args.TotalUnits}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($">>> [ProgressHandler] ❌ UpdateUnits 오류: {ex.Message}");
                            _logger?.LogError(ex, "[MainWindow] UpdateUnits 호출 중 오류");
                        }
                    }, DispatcherPriority.Render); // Render 우선순위로 즉시 반영
                };
                System.Console.WriteLine($"[MainWindow] 이벤트 핸들러 연결 전: progressHandlerLocal={progressHandlerLocal != null}");
                System.Console.WriteLine($"[MainWindow] ValidationService 인스턴스: {_validationService.GetHashCode()}");
                _validationService.ProgressUpdated += progressHandlerLocal;
                System.Console.WriteLine($"[MainWindow] 이벤트 핸들러 연결 완료!");

                // 실제 검수 서비스 호출
                // progressView.AddLogMessage("검수 서비스 실행 중...");
                
                // 사용자 정의 설정 파일이 있으면 우선 사용, 없으면 기본 설정 사용
                var tableConfig = !string.IsNullOrEmpty(_customTableConfigPath) ? _customTableConfigPath : _tableConfigPath;
                var schemaConfig = !string.IsNullOrEmpty(_customSchemaConfigPath) ? _customSchemaConfigPath : _schemaConfigPath;
                var geometryConfig = !string.IsNullOrEmpty(_customGeometryConfigPath) ? _customGeometryConfigPath : _geometryConfigPath;
                var relationConfig = !string.IsNullOrEmpty(_customRelationConfigPath) ? _customRelationConfigPath : _relationConfigPath;
                
                // 사용할 설정 파일 경로 로그 출력
                // progressView.AddLogMessage($"테이블 설정: {(tableConfig != null ? Path.GetFileName(tableConfig) : "기본값")}");
                // progressView.AddLogMessage($"스키마 설정: {(schemaConfig != null ? Path.GetFileName(schemaConfig) : "기본값")}");
                // progressView.AddLogMessage($"지오메트리 설정: {(geometryConfig != null ? Path.GetFileName(geometryConfig) : "기본값")}");
                // progressView.AddLogMessage($"관계 설정: {(relationConfig != null ? Path.GetFileName(relationConfig) : "기본값")}");
                
                // 선택된 검수 항목을 서비스에 전달
                _validationService._selectedStage1Items = _selectedStage1Items;
                _validationService._selectedStage2Items = _selectedStage2Items;
                _validationService._selectedStage3Items = _selectedStage3Items;
                _validationService._selectedStage4Items = _selectedStage4Items;
                _validationService._selectedStage5Items = _selectedStage5Items;
                
                var token = _validationCancellationTokenSource.Token;
                
                // Task.Run을 사용하여 백그라운드 스레드에서 실행
                _currentValidationResult = await Task.Run(async () => 
                {
                    return await _validationService.ValidateAsync(
                        _selectedFilePath!, 
                        tableConfig, 
                        schemaConfig, 
                        geometryConfig, 
                        relationConfig,
                        !string.IsNullOrEmpty(_customAttributeConfigPath) ? _customAttributeConfigPath : _attributeConfigPath,
                        !string.IsNullOrEmpty(_customCodelistPath) ? _customCodelistPath : null,
                        token);
                });

                // 완료
                progressView.UpdateProgress(100, "검수 완료");
                // progressView.AddLogMessage($"검수 완료 - 결과: {(_currentValidationResult.IsValid ? "성공" : "실패")}");
                // progressView.AddLogMessage($"오류: {_currentValidationResult.ErrorCount}개, 경고: {_currentValidationResult.WarningCount}개");
                
                progressTimer.Stop();
                
                UpdateStatus($"검수 완료 - {(_currentValidationResult.IsValid ? "성공" : "실패")}");
                
                // 실행 데이터 저장 (예측 모델 개선용)
                try
                {
                    await SaveValidationRunData(_currentValidationResult);
                }
                catch (Exception saveEx)
                {
                    _logger?.LogWarning(saveEx, "실행 데이터 저장 실패");
                }
                
                // 보고서 자동 생성
                try
                {
                    var targetPath = _selectedFilePath ?? string.Empty;
                    var (pdfPath, htmlPath) = GenerateReportsForTarget(_currentValidationResult, targetPath);
                    ShowCompletionDialog(_currentValidationResult, pdfPath, htmlPath);
                }
                catch (Exception rex)
                {
                    MessageBox.Show($"보고서 자동 생성 중 오류: {rex.Message}", "보고서 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 결과 화면으로 이동
                NavigateToResults(this, new RoutedEventArgs());
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("검수가 취소되었습니다.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"검수 실패: {ex.Message}");
                MessageBox.Show($"검수 중 오류가 발생했습니다:\n{ex.Message}", "검수 오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 이벤트 구독 해제: 로컬 핸들러를 안전하게 해제
                try { if (_validationService != null && progressHandlerLocal != null) _validationService.ProgressUpdated -= progressHandlerLocal; } catch { }
                
                _isValidationRunning = false;
                _validationCancellationTokenSource?.Dispose();
                _validationCancellationTokenSource = null;
                _currentProgressView = null;
            }
        }

        /// <summary>
        /// 검수 실행 데이터를 저장합니다
        /// </summary>
        private async Task SaveValidationRunData(SpatialCheckPro.Models.ValidationResult result)
        {
            try
            {
                var predictor = new Models.ValidationTimePredictor(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Models.ValidationTimePredictor>.Instance);
                
                var runData = new Models.ValidationTimePredictor.ValidationRunData
                {
                    Timestamp = result.StartedAt,
                    FilePath = result.TargetFile,
                    TableCount = result.TableCheckResult?.TotalTableCount ?? 0,
                    TotalFeatureCount = 0, // 실제로는 테이블별 피처 수 합계 필요
                    SchemaFieldCount = result.SchemaCheckResult?.TotalColumnCount ?? 0,
                    GeometryCheckItemCount = 232, // 기본 검수 항목 수
                    RelationRuleCount = 100, // 기본 관계 규칙 수
                    AttributeColumnCount = 0,
                    Stage0Time = 0.2, // FileGDB 검수는 대부분 빠름
                    Stage1Time = 0, // 개별 단계 시간은 추후 이벤트에서 수집
                    Stage2Time = 0,
                    Stage3Time = 0,
                    Stage4Time = 0,
                    Stage5Time = 0,
                    TotalTime = result.ProcessingTime.TotalSeconds
                };
                
                // 테이블별 피처 수 계산
                if (result.TableCheckResult?.TableResults != null)
                {
                    runData.TotalFeatureCount = result.TableCheckResult.TableResults
                        .Sum(t => t.FeatureCount ?? 0);
                }
                
                predictor.SaveRunData(runData);
                _logger?.LogInformation("검수 실행 데이터 저장 완료");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 실행 데이터 저장 실패");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 검수를 중지합니다
        /// </summary>
        private void StopValidation()
        {
            if (!_isValidationRunning)
            {
                return;
            }

            var result = MessageBox.Show(
                "진행 중인 검수를 중단하시겠습니까?",
                "검수 중단",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _validationCancellationTokenSource?.Cancel();
                UpdateStatus("검수를 중단하는 중...");
            }
        }

        /// <summary>
        /// 배치 검수를 시작합니다 (.gdb 여러 개 순차 처리)
        /// </summary>
        private async Task StartBatchValidationAsync(Views.CompactValidationProgressView progressView)
        {
            if (_isValidationRunning)
            {
                MessageBox.Show("이미 검수가 진행 중입니다.", "검수 진행 중", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_validationService == null)
            {
                MessageBox.Show("검수 서비스가 초기화되지 않았습니다.\n\n가능한 해결 방법:\n" +
                    "1. 애플리케이션을 재시작해주세요.\n" +
                    "2. 관리자 권한으로 실행해보세요.\n" +
                    "3. 바이러스 백신 프로그램이 차단하지 않는지 확인해주세요.", 
                    "서비스 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var targets = (_selectedFilePaths ?? new List<string>()).ToList();
            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                targets.Add(_selectedFilePath!);
            }
            if (targets.Count == 0)
            {
                MessageBox.Show("검수 대상이 없습니다.", "대상 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentProgressView = progressView;
            _validationCancellationTokenSource = new System.Threading.CancellationTokenSource();
            _isValidationRunning = true;

            // 배치 진행률 핸들러를 finally에서 해제할 수 있도록 메서드 스코프에 선언
            EventHandler<SpatialCheckPro.GUI.Services.ValidationProgressEventArgs>? batchProgressHandler = null;

            try
            {
                UpdateStatus("배치 검수를 시작합니다...");
                // progressView.AddLogMessage($"배치 검수 시작 - 대상: {targets.Count}개 .gdb");

                var startTime = DateTime.Now;
                var progressTimer = new DispatcherTimer(DispatcherPriority.Render); // Render 우선순위로 변경
                progressTimer.Interval = TimeSpan.FromMilliseconds(100); // 100ms 간격
                progressTimer.Tick += (s, e) => 
                {
                    var elapsed = DateTime.Now - startTime;
                    // Invoke로 동기 호출하여 즉시 반영
                    Dispatcher.Invoke(() => progressView.UpdateElapsedTime(elapsed), DispatcherPriority.Render);
                };
                progressTimer.Start();

                // 사용자 정의/기본 설정 파일 경로 결정
                var tableConfig = !string.IsNullOrEmpty(_customTableConfigPath) ? _customTableConfigPath : _tableConfigPath;
                var schemaConfig = !string.IsNullOrEmpty(_customSchemaConfigPath) ? _customSchemaConfigPath : _schemaConfigPath;
                var geometryConfig = !string.IsNullOrEmpty(_customGeometryConfigPath) ? _customGeometryConfigPath : _geometryConfigPath;
                var relationConfig = !string.IsNullOrEmpty(_customRelationConfigPath) ? _customRelationConfigPath : _relationConfigPath;
                var attributeConfig = !string.IsNullOrEmpty(_customAttributeConfigPath) ? _customAttributeConfigPath : _attributeConfigPath;

                var token = _validationCancellationTokenSource.Token;

                int total = targets.Count;
                int index = -1;

                // 진행률 집계 핸들러
                bool sawStage5Start = false;
                bool sawStage5Done = false;
                batchProgressHandler = (sender, args) =>
                {
                    // InvokeAsync로 변경하여 UI 스레드 블로킹 방지
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        var completed = Math.Max(0, index);
                        double batchPct = ((completed * 100.0) + args.OverallProgress) / Math.Max(1, total);
                        var status = $"[{Math.Max(1, index + 1)}/{total}] {args.StageName} - {args.StatusMessage}";
                        progressView.UpdateProgress(batchPct, status);
                        progressView.UpdateCurrentStage(args.StageName, args.CurrentStage);

                        // 개별 단계 진행률 갱신
                        try { progressView.UpdateStageProgress(args.CurrentStage, args.StageProgress); } catch { }

                        // 단위 정보가 제공되면 상세 정보 업데이트
                        try
                        {
                            if (args.ProcessedUnits >= 0 && args.TotalUnits >= 0)
                            {
                                progressView.UpdateUnits(args.CurrentStage, args.ProcessedUnits, args.TotalUnits);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "[MainWindow] UpdateUnits 호출 중 오류");
                        }

                        // 5단계 (CurrentStage = 5) 검수 진행 추적
                        // 단, RelationValidationProgressEventArgs의 CurrentStage는 RelationValidationStage enum이므로,
                        // 정수 비교가 아닌 실제 전달된 CurrentStage 값이 5인 경우로 확인
                        if (args.CurrentStage == 5)
                        {
                            if (args.StageProgress >= 0) sawStage5Start = true;
                            if (args.IsStageCompleted) sawStage5Done = true;
                        }
                        _stageSummaryCollectionViewModel.ApplyProgress(args);

                        // 부분 결과 저장 및 진행 화면에 표시 (단계 완료 시)
                        if (args.IsStageCompleted)
                        {
                            if (args.PartialResult != null)
                            {
                                _currentValidationResult = args.PartialResult;
                                progressView.UpdatePartialResults(_currentValidationResult);
                            }
                        }

                        if (args.IsStageCompleted)
                        {
                            if (args.IsStageSkipped)
                                _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, StageStatus.Skipped, args.StatusMessage);
                            else
                                _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, args.IsStageSuccessful ? StageStatus.Completed : StageStatus.Failed, args.StatusMessage);
                        }
                    }, DispatcherPriority.Normal);
                };

                _validationService.ProgressUpdated += batchProgressHandler;

                int successCount = 0;
                int failCount = 0;
                int totalErrors = 0;
                int totalWarnings = 0;
                var batchResults = new List<SpatialCheckPro.Models.ValidationResult>();

                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;
                    index = i;

                    var gdbPath = targets[i];
                    // progressView.AddLogMessage($"[{i + 1}/{total}] 대상: {gdbPath}");

                    // 파일별 단계 UI 초기화
                    Dispatcher.Invoke(() =>
                    {
                        _stageSummaryCollectionViewModel.Reset();
                    });

                    var vr = await _validationService.ValidateAsync(
                        gdbPath,
                        tableConfig,
                        schemaConfig,
                        geometryConfig,
                        relationConfig,
                        attributeConfig,
                        !string.IsNullOrEmpty(_customCodelistPath) ? _customCodelistPath : null,
                        token);

                    _currentValidationResult = vr;
                    batchResults.Add(vr);
                    totalErrors += vr.ErrorCount;
                    totalWarnings += vr.WarningCount;
                    if (vr.IsValid) successCount++; else failCount++;

                    // 5단계 진행 표시가 사용자 환경에서 드물게 누락되는 경우가 있어 보정 처리
                    if (!sawStage5Start)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressView.UpdateCurrentStage("관계 검수", 5);
                            _stageSummaryCollectionViewModel.ForceStageStatus(5, StageStatus.Completed, "관계 검수 완료");
                            double batchPctNow = ((Math.Max(0, index) * 100.0) + 100.0) / Math.Max(1, total);
                            progressView.UpdateProgress(batchPctNow, "파일 처리 완료");
                            // progressView.AddLogMessage("5단계 진행 표시 보정: 속성 관계 검수 완료로 처리");
                        });
                    }
                    else if (sawStage5Start && !sawStage5Done)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _stageSummaryCollectionViewModel.ForceStageStatus(5, StageStatus.Completed, "관계 검수 완료");
                        });
                    }
                    // 다음 파일을 위해 초기화
                    sawStage5Start = false;
                    sawStage5Done = false;

                    // 파일별 보고서 생성
                    try
                    {
                        var (pdfPath, htmlPath) = GenerateReportsForTarget(vr, gdbPath);
                        // progressView.AddLogMessage($"보고서 생성 완료: PDF={pdfPath}, HTML={htmlPath}");
                    }
                    catch (Exception rex)
                    {
                        _logger?.LogWarning(rex, "보고서 생성 중 오류가 발생했습니다.");
                        // progressView.AddLogMessage($"보고서 생성 중 오류: {rex.Message}");
                    }
                }

                _validationService.ProgressUpdated -= batchProgressHandler;

                if (!token.IsCancellationRequested)
                {
                    progressView.UpdateProgress(100, "배치 검수 완료");
                    // progressView.AddLogMessage($"배치 검수 완료 - 성공 {successCount}개, 실패 {failCount}개, 총 오류 {totalErrors}개, 총 경고 {totalWarnings}개");
                    UpdateStatus("배치 검수 완료");

                    // 결과 화면으로 이동하되, 배치 전체 결과를 전달하여 파일 선택이 가능하도록 설정
                    try
                    {
                        var resultView = new Views.ValidationResultView();
                        resultView.SetBatchResults(batchResults);
                        MainContentContainer.Content = resultView;
                        UpdateNavigationButtons("Results");
                    }
                    catch
                    {
                        NavigateToResults(this, new RoutedEventArgs());
                    }
                }
                else
                {
                    UpdateStatus("배치 검사가 취소되었습니다.");
                }

                progressTimer.Stop();
            }
            catch (Exception ex)
            {
                UpdateStatus($"배치 검수 실패: {ex.Message}");
                MessageBox.Show($"배치 검수 중 오류가 발생했습니다:\n{ex.Message}", "검수 오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 이벤트 구독 해제 보장
                try { if (_validationService != null && batchProgressHandler != null) _validationService.ProgressUpdated -= batchProgressHandler; } catch { }
                _isValidationRunning = false;
                _validationCancellationTokenSource?.Dispose();
                _validationCancellationTokenSource = null;
                _currentProgressView = null;
            }
        }

        /// <summary>
        /// 검수 설정 클릭 이벤트 핸들러
        /// </summary>
        private void ValidationSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 설정 화면을 메인 컨텐츠에 표시
                var settingsView = new Views.ValidationSettingsView();
                MainContentContainer.Content = settingsView;
                UpdateNavigationButtons("Settings");
                
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 설정 화면 표시 오류");
                MessageBox.Show($"검수 설정 화면 표시 오류: {ex.Message}", "오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 설정창에서 OK로 나온 결과를 적용하는 헬퍼 (탭 통합용)
        /// </summary>
        /// <param name="settingsWindow">설정 창 인스턴스</param>
        public void ApplyValidationSettingsFromWindow(ValidationSettingsWindow settingsWindow)
        {
            _customTableConfigPath = settingsWindow.TableConfigPath;
            _customSchemaConfigPath = settingsWindow.SchemaConfigPath;
            _customGeometryConfigPath = settingsWindow.GeometryConfigPath;
            _customRelationConfigPath = settingsWindow.RelationConfigPath;
            _customAttributeConfigPath = settingsWindow.AttributeConfigPath;
            _customGeometryCriteriaPath = settingsWindow.GeometryCriteriaPath;

            if (!string.IsNullOrWhiteSpace(settingsWindow.TargetPath))
            {
                _selectedFilePath = settingsWindow.TargetPath;
            }

            _enableStage1 = settingsWindow.EnableStage1;
            _enableStage2 = settingsWindow.EnableStage2;
            _enableStage3 = settingsWindow.EnableStage3;
            _enableStage4 = settingsWindow.EnableStage4;
            _enableStage5 = settingsWindow.EnableStage5;

            _selectedStage1Items = settingsWindow.SelectedStage1Items;
            _selectedStage2Items = settingsWindow.SelectedStage2Items;
            _selectedStage3Items = settingsWindow.SelectedStage3Items;
            _selectedStage4Items = settingsWindow.SelectedStage4Items;  // 속성 검수 (4단계)
            _selectedStage5Items = settingsWindow.SelectedStage5Items;  // 공간 관계 검수 (5단계)
        }

        /// <summary>
        /// 지도 뷰로 전환합니다
        /// </summary>
        public void SwitchToMapView()
        {
            // 지도 기능 제거: 동작 없음
            UpdateStatus("지도 기능이 비활성화되었습니다");
        }

        /// <summary>
        /// 지도에서 피처로 줌합니다
        /// </summary>
        /// <param name="tableId">테이블 ID</param>
        /// <param name="objectId">객체 ID</param>
        public void ZoomToFeatureInMap(string tableId, string objectId)
        {
            // 지도 기능 제거: 동작 없음
            UpdateStatus("지도 기능이 비활성화되었습니다");
        }

        /// <summary>
        /// 지도 이동 요청 이벤트 핸들러 - ValidationResultView에서 오류 위치로 지도 이동
        /// </summary>
        private async void OnMapNavigationRequested(object? sender, Views.MapNavigationEventArgs e)
        {
            // 지도 기능 제거: 이벤트 무시
            UpdateStatus("지도 기능 비활성화: 지도 이동 요청이 무시되었습니다");
            await Task.CompletedTask;
        }

        /// <summary>
        /// GDB 파일 분석 디버깅 메서드 (비활성화됨)
        /// </summary>
        private void DebugGdbAnalysis_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("해당 기능은 비활성화되었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region 보고서 생성 헬퍼 메서드

        /// <summary>
        /// 보고서 파일 경로를 준비합니다
        /// </summary>
        private (string PdfPath, string HtmlPath) PrepareReportPaths(string targetPath)
        {
            var baseDir = Directory.Exists(targetPath) 
                ? targetPath 
                : Path.GetDirectoryName(targetPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;

            // 검수 파일명(또는 .gdb 폴더명) 추출
            string rawName;
            if (Directory.Exists(targetPath))
            {
                rawName = Path.GetFileName(targetPath);
                if (rawName.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    rawName = Path.GetFileNameWithoutExtension(rawName);
            }
            else
            {
                rawName = Path.GetFileNameWithoutExtension(targetPath);
            }

            // 파일명에 사용할 수 없는 문자 치환
            var invalid = Path.GetInvalidFileNameChars();
            var nameToSanitize = (rawName ?? "검수결과").Trim();
            var sanitized = new string(nameToSanitize.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();

            var timestamp = DateTime.Now.ToString(DateTimeFormats.FileTimestamp);
            var baseName = $"{sanitized}_{timestamp}";
            var pdfPath = Path.Combine(parentDir, baseName + ".pdf");
            var htmlPath = Path.Combine(parentDir, baseName + ".html");

            return (pdfPath, htmlPath);
        }

        /// <summary>
        /// 검수 결과에 대한 보고서를 생성합니다
        /// </summary>
        private (string PdfPath, string HtmlPath) GenerateReportsForTarget(SpatialCheckPro.Models.ValidationResult result, string targetPath)
        {
            var (pdfPath, htmlPath) = PrepareReportPaths(targetPath);

            // PDF 생성
            try
            {
                var app = Application.Current as App;
                var pdfService = app?.GetService<PdfReportService>();
                if (pdfService != null)
                {
                    pdfService.GeneratePdfReport(result, pdfPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PDF 보고서 생성 실패: {PdfPath}", pdfPath);
            }

            // HTML 생성
            try
            {
                var html = new GUI.Views.ReportView();
                html.SetValidationResult(result);
                var htmlContent = html.GenerateHtmlReport();
                File.WriteAllText(htmlPath, htmlContent, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "HTML 보고서 생성 실패: {HtmlPath}", htmlPath);
            }

            return (pdfPath, htmlPath);
        }

        /// <summary>
        /// 검수 완료 다이얼로그를 표시합니다
        /// </summary>
        private void ShowCompletionDialog(SpatialCheckPro.Models.ValidationResult result, string pdfPath, string htmlPath)
        {
            try
            {
                var dlg = new GUI.Views.ModernMessageDialog
                {
                    Owner = this
                };
                dlg.Configure("검수 완료", "검수가 완료되었습니다!", pdfPath, htmlPath);
                
                // 결과 상태에 따라 스타일 적용
                var status = result.ErrorCount == 0 
                    ? "success" 
                    : (result.WarningCount > 0 ? "partial" : "partial");
                dlg.ApplyStyle("light", status);
                dlg.ShowDialog();
            }
            catch
            {
                // 폴백: 기존 메시지 박스
                MessageBox.Show(
                    $"검수가 완료되었습니다!\n\nPDF: {pdfPath}\nHTML: {htmlPath}", 
                    "검수 완료", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
        }

        #endregion

        /// <summary>
        /// 메인 윈도우 종료 이벤트 핸들러
        /// </summary>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _logger?.LogInformation("애플리케이션 종료 중...");

                // 타이머 정지
                _timer?.Stop();

                // 검수 진행 중이면 취소
                if (_isValidationRunning)
                {
                    _validationCancellationTokenSource?.Cancel();
                    _logger?.LogInformation("진행 중인 검수를 취소했습니다.");
                }

                // 리소스 정리
                _validationCancellationTokenSource?.Dispose();

                _logger?.LogInformation("애플리케이션 종료 완료");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "애플리케이션 종료 중 오류 발생");
            }
        }
    }
}