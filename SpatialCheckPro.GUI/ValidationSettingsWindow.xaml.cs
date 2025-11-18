using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpatialCheckPro.Models.Config;
using CsvHelper;
using System.Globalization;
using SpatialCheckPro.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// 검수 설정 창 - 가이드 준수 버전
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class ValidationSettingsWindow : Window
    {
        /// <summary>테이블 검수 설정 파일 경로</summary>
        public string TableConfigPath { get; set; } = string.Empty;
        
        /// <summary>스키마 검수 설정 파일 경로</summary>
        public string SchemaConfigPath { get; set; } = string.Empty;
        
        /// <summary>지오메트리 검수 설정 파일 경로</summary>
        public string GeometryConfigPath { get; set; } = string.Empty;
        
        /// <summary>관계 검수 설정 파일 경로</summary>
        public string RelationConfigPath { get; set; } = string.Empty;

        /// <summary>속성 검수 설정 파일 경로</summary>
        public string AttributeConfigPath { get; set; } = string.Empty;

        /// <summary>지오메트리 검수 기준 파일 경로</summary>
        public string GeometryCriteriaPath { get; set; } = string.Empty;

        // 대상 경로 및 단계 플래그
        public string TargetPath { get; set; } = string.Empty;
        public bool EnableStage1 { get; set; } = true;
        public bool EnableStage2 { get; set; } = true;
        public bool EnableStage3 { get; set; } = true;
        public bool EnableStage4 { get; set; } = true;
        public bool EnableStage5 { get; set; } = true;

        // 시스템 리소스 설정
        public bool EnableParallelProcessing { get; set; } = true;
        public int MaxParallelism { get; set; } = Environment.ProcessorCount;
        public int BatchSize { get; set; } = 1000;
        public bool EnableRealTimeMonitoring { get; set; } = false;
        public int MonitoringIntervalSeconds { get; set; } = 5;

        private SystemResourceAnalyzer? _resourceAnalyzer;
        private ILogger<ValidationSettingsWindow>? _logger;
        private System.Windows.Threading.DispatcherTimer? _monitoringTimer;

        public ValidationSettingsWindow()
        {
            InitializeComponent();
            InitializeDefaultPaths();
            LoadGeometryCriteria();
            UpdateUI();
            InitializeSystemResourceAnalyzer();
            _ = LoadSystemResourceStatusAsync();
            InitializeRealTimeMonitoring();
        }

        /// <summary>
        /// 미리보기 행(Use 플래그 + 원본 항목)을 담는 단순 뷰모델
        /// </summary>
        private sealed class PreviewRow<T>
        {
            public bool Use { get; set; } = true; // 기본 사용
            public T Item { get; set; }
            public PreviewRow(T item) { Item = item; }
        }

        private readonly ObservableCollection<PreviewRow<TableCheckConfig>> _stage1Rows = new();
        private readonly ObservableCollection<PreviewRow<SchemaCheckConfig>> _stage2Rows = new();
        private readonly ObservableCollection<PreviewRow<GeometryCheckConfig>> _stage3Rows = new();
        private readonly ObservableCollection<PreviewRow<AttributeCheckConfig>> _stage4Rows = new();
        private readonly ObservableCollection<PreviewRow<RelationCheckConfig>> _stage5Rows = new();

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadStage1Async();
                await LoadStage2Async();
                await LoadStage3Async();
                await LoadStage4Async();
                await LoadStage5Async();

                if (FindName("Stage1Grid") is System.Windows.Controls.DataGrid g1) g1.ItemsSource = _stage1Rows;
                if (FindName("Stage2Grid") is System.Windows.Controls.DataGrid g2) g2.ItemsSource = _stage2Rows;
                if (FindName("Stage3Grid") is System.Windows.Controls.DataGrid g3) g3.ItemsSource = _stage3Rows;
                if (FindName("Stage4Grid") is System.Windows.Controls.DataGrid g4) g4.ItemsSource = _stage4Rows;
                if (FindName("Stage5Grid") is System.Windows.Controls.DataGrid g5) g5.ItemsSource = _stage5Rows;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"미리보기 로드 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 헤더 전체 선택/해제 처리기
        private void Stage1ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            foreach (var r in _stage1Rows) r.Use = isChecked;
            if (FindName("Stage1Grid") is System.Windows.Controls.DataGrid g) g.Items.Refresh();
        }
        private void Stage2ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            foreach (var r in _stage2Rows) r.Use = isChecked;
            if (FindName("Stage2Grid") is System.Windows.Controls.DataGrid g) g.Items.Refresh();
        }
        private void Stage3ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            foreach (var r in _stage3Rows) r.Use = isChecked;
            if (FindName("Stage3Grid") is System.Windows.Controls.DataGrid g) g.Items.Refresh();
        }
        private void Stage4ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            foreach (var r in _stage4Rows) r.Use = isChecked;
            if (FindName("Stage4Grid") is System.Windows.Controls.DataGrid g) g.Items.Refresh();
        }
        private void Stage5ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            foreach (var r in _stage5Rows) r.Use = isChecked;
            if (FindName("Stage5Grid") is System.Windows.Controls.DataGrid g) g.Items.Refresh();
        }

        private async Task LoadStage1Async()
        {
            _stage1Rows.Clear();
            if (!File.Exists(TableConfigPath)) return;
            try
            {
                using var reader = new StreamReader(TableConfigPath);
                using var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;
                var items = await Task.Run(() => csv.GetRecords<TableCheckConfig>().ToList());
                foreach (var it in items) _stage1Rows.Add(new PreviewRow<TableCheckConfig>(it));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"1단계 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task LoadStage2Async()
        {
            _stage2Rows.Clear();
            if (!File.Exists(SchemaConfigPath)) return;
            try
            {
                using var reader = new StreamReader(SchemaConfigPath);
                using var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;
                var items = await Task.Run(() => csv.GetRecords<SchemaCheckConfig>().ToList());
                foreach (var it in items) _stage2Rows.Add(new PreviewRow<SchemaCheckConfig>(it));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"2단계 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task LoadStage3Async()
        {
            _stage3Rows.Clear();
            if (!File.Exists(GeometryConfigPath)) return;
            try
            {
                using var reader = new StreamReader(GeometryConfigPath);
                using var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;
                var items = await Task.Run(() => csv.GetRecords<GeometryCheckConfig>().ToList());
                foreach (var it in items) _stage3Rows.Add(new PreviewRow<GeometryCheckConfig>(it));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"3단계 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task LoadStage4Async()
        {
            _stage4Rows.Clear();
            if (!File.Exists(AttributeConfigPath)) return;
            try
            {
                using var reader = new StreamReader(AttributeConfigPath, DetectCsvEncoding(AttributeConfigPath));
                using var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;
                var items = await Task.Run(() => csv.GetRecords<AttributeCheckConfig>().ToList());
                foreach (var it in items) _stage4Rows.Add(new PreviewRow<AttributeCheckConfig>(it));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"4단계 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task LoadStage5Async()
        {
            _stage5Rows.Clear();
            if (!File.Exists(RelationConfigPath)) return;
            try
            {
                using var reader = new StreamReader(RelationConfigPath, DetectCsvEncoding(RelationConfigPath), detectEncodingFromByteOrderMarks: false);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                // 수동 파싱: 존재하는 헤더만 사용
                await Task.Run(() =>
                {
                    if (!csv.Read()) return;
                    csv.ReadHeader();
                    var headers = csv.HeaderRecord ?? Array.Empty<string>();

                    while (csv.Read())
                    {
                        string Get(string name)
                        {
                            var matched = headers.FirstOrDefault(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (matched == null) return string.Empty;
                            try { return csv.GetField(matched) ?? string.Empty; } catch { return string.Empty; }
                        }

                        var cfg = new RelationCheckConfig
                        {
                            RuleId = Get("RuleId"),
                            Enabled = string.IsNullOrWhiteSpace(Get("Enabled")) ? "Y" : Get("Enabled"),
                            CaseType = Get("CaseType"),
                            MainTableId = Get("MainTableId"),
                            MainTableName = Get("MainTableName"),
                            RelatedTableId = Get("RelatedTableId"),
                            RelatedTableName = Get("RelatedTableName"),
                            FieldFilter = Get("FieldFilter"),
                            Note = Get("Note")
                        };

                        var tolStr = Get("Tolerance");
                        if (double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var tol))
                        {
                            cfg.Tolerance = tol;
                        }

                        Application.Current.Dispatcher.Invoke(() => _stage5Rows.Add(new PreviewRow<RelationCheckConfig>(cfg)));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"5단계 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// CSV 인코딩 자동 감지(간이)
        /// </summary>
        private static System.Text.Encoding DetectCsvEncoding(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var bom = new byte[3];
                var read = fs.Read(bom, 0, 3);
                if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return System.Text.Encoding.UTF8;
            }
            catch { }
            return System.Text.Encoding.GetEncoding(949);
        }

        /// <summary>
        /// 기본 설정 파일 경로를 초기화합니다
        /// </summary>
        private void InitializeDefaultPaths()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var configDirectory = Path.Combine(appDirectory, "Config");
            
            TableConfigPath = Path.Combine(configDirectory, "1_table_check.csv");
            SchemaConfigPath = Path.Combine(configDirectory, "2_schema_check.csv");
            GeometryConfigPath = Path.Combine(configDirectory, "3_geometry_check.csv");
            AttributeConfigPath = Path.Combine(configDirectory, "4_attribute_check.csv");
            RelationConfigPath = Path.Combine(configDirectory, "5_relation_check.csv");
            GeometryCriteriaPath = Path.Combine(configDirectory, "geometry_criteria.csv");
        }

        /// <summary>
        /// UI를 업데이트합니다
        /// </summary>
        private void UpdateUI()
        {
            TableConfigPathTextBox.Text = TableConfigPath;
            SchemaConfigPathTextBox.Text = SchemaConfigPath;
            GeometryConfigPathTextBox.Text = GeometryConfigPath;
            AttributeConfigPathTextBox.Text = AttributeConfigPath;
            RelationConfigPathTextBox.Text = RelationConfigPath;
            GeometryCriteriaPathTextBox.Text = GeometryCriteriaPath;

            // 대상 경로 및 단계 체크박스 동기화
            if (FindName("TargetPathTextBox") is System.Windows.Controls.TextBox t)
                t.Text = TargetPath;
            if (FindName("Stage1EnabledCheck") is System.Windows.Controls.CheckBox c1) c1.IsChecked = EnableStage1;
            if (FindName("Stage2EnabledCheck") is System.Windows.Controls.CheckBox c2) c2.IsChecked = EnableStage2;
            if (FindName("Stage3EnabledCheck") is System.Windows.Controls.CheckBox c3) c3.IsChecked = EnableStage3;
            if (FindName("Stage4EnabledCheck") is System.Windows.Controls.CheckBox c4) c4.IsChecked = EnableStage4;
            if (FindName("Stage5EnabledCheck") is System.Windows.Controls.CheckBox c5) c5.IsChecked = EnableStage5;
        }

        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = ".gdb 폴더 또는 데이터가 위치한 경로를 선택하세요";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TargetPath = dlg.SelectedPath;
                    if (FindName("TargetPathTextBox") is System.Windows.Controls.TextBox t)
                        t.Text = TargetPath;
                }
            }
        }

        /// <summary>
        /// 테이블 검수 설정 파일 선택
        /// </summary>
        private void BrowseTableConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "테이블 검수 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "1_table_check.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TableConfigPath = openFileDialog.FileName;
                TableConfigPathTextBox.Text = TableConfigPath;
            }
        }

        /// <summary>
        /// 스키마 검수 설정 파일 선택
        /// </summary>
        private void BrowseSchemaConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "스키마 검수 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "2_schema_check.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SchemaConfigPath = openFileDialog.FileName;
                SchemaConfigPathTextBox.Text = SchemaConfigPath;
            }
        }

        /// <summary>
        /// 지오메트리 검수 설정 파일 선택
        /// </summary>
        private void BrowseGeometryConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "지오메트리 검수 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "3_geometry_check.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                GeometryConfigPath = openFileDialog.FileName;
                GeometryConfigPathTextBox.Text = GeometryConfigPath;
            }
        }

        /// <summary>
        /// 관계 검수 설정 파일 선택
        /// </summary>
        private void BrowseRelationConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "관계 검수 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "5_relation_check.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                RelationConfigPath = openFileDialog.FileName;
                RelationConfigPathTextBox.Text = RelationConfigPath;
            }
        }

        /// <summary>
        /// 속성 검수 설정 파일 선택
        /// </summary>
        private void BrowseAttributeConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "속성 검수 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "4_attribute_check.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AttributeConfigPath = openFileDialog.FileName;
                AttributeConfigPathTextBox.Text = AttributeConfigPath;
            }
        }

        /// <summary>
        /// 지오메트리 검수 기준 파일 선택
        /// </summary>
        private void BrowseGeometryCriteria_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "지오메트리 검수 기준 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "geometry_criteria.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                GeometryCriteriaPath = openFileDialog.FileName;
                GeometryCriteriaPathTextBox.Text = GeometryCriteriaPath;
                LoadGeometryCriteria();
            }
        }

        /// <summary>
        /// 지오메트리 검수 기준을 로드하여 UI에 표시합니다
        /// </summary>
        private void LoadGeometryCriteria()
        {
            try
            {
                if (!File.Exists(GeometryCriteriaPath))
                {
                    // 기본값 사용
                    return;
                }

                var lines = File.ReadAllLines(GeometryCriteriaPath);
                if (lines.Length <= 1) return; // 헤더만 있는 경우

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        var item = parts[0].Trim();
                        var value = parts[1].Trim();

                        switch (item)
                        {
                            case "최소선길이":
                                if (MinLengthTextBox != null) MinLengthTextBox.Text = value;
                                break;
                            case "최소폴리곤면적":
                                if (MinAreaTextBox != null) MinAreaTextBox.Text = value;
                                break;
                            case "슬리버면적":
                                if (SliverAreaTextBox != null) SliverAreaTextBox.Text = value;
                                break;
                            case "슬리버형태지수":
                                if (SliverShapeIndexTextBox != null) SliverShapeIndexTextBox.Text = value;
                                break;
                            case "슬리버신장률":
                                if (SliverElongationTextBox != null) SliverElongationTextBox.Text = value;
                                break;
                            case "중복검사허용오차":
                                if (DuplicateToleranceTextBox != null) DuplicateToleranceTextBox.Text = value;
                                break;
                            case "겹침허용면적":
                                if (OverlapToleranceTextBox != null) OverlapToleranceTextBox.Text = value;
                                break;
                            case "자체꼬임허용각도":
                                if (SelfIntersectionAngleTextBox != null) SelfIntersectionAngleTextBox.Text = value;
                                break;
                            case "폴리곤내폴리곤최소거리":
                                if (PolygonInPolygonDistanceTextBox != null) PolygonInPolygonDistanceTextBox.Text = value;
                                break;
                            case "스파이크각도임계값":
                                if (SpikeAngleTextBox != null) SpikeAngleTextBox.Text = value;
                                break;
                            case "링폐합오차":
                                if (RingClosureToleranceTextBox != null) RingClosureToleranceTextBox.Text = value;
                                break;
                            case "네트워크탐색거리":
                                if (NetworkSearchDistanceTextBox != null) NetworkSearchDistanceTextBox.Text = value;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 생성자에서 호출되므로 MessageBox 대신 로깅 (UI 초기화 전에 MessageBox 표시 방지)
                System.Diagnostics.Debug.WriteLine($"지오메트리 검수 기준 로드 실패: {ex.Message}");
                // 기본값으로 계속 진행
            }
        }

        /// <summary>
        /// 지오메트리 검수 기준을 파일에 저장합니다
        /// </summary>
        private void SaveGeometryCriteria()
        {
            try
            {
                var lines = new[]
                {
                    "항목명,값,단위,설명",
                    $"겹침허용면적,{OverlapToleranceTextBox?.Text ?? "0.001"},제곱미터,폴리곤 겹침 허용 면적",
                    $"최소선길이,{MinLengthTextBox?.Text ?? "0.01"},미터,짧은 선 객체 판정 기준",
                    $"최소폴리곤면적,{MinAreaTextBox?.Text ?? "1.0"},제곱미터,작은 면적 객체 판정 기준",
                    $"자체꼬임허용각도,{SelfIntersectionAngleTextBox?.Text ?? "1.0"},도,자체 교차 허용 각도",
                    $"폴리곤내폴리곤최소거리,{PolygonInPolygonDistanceTextBox?.Text ?? "0.1"},미터,폴리곤 내부 폴리곤 최소 거리",
                    $"슬리버면적,{SliverAreaTextBox?.Text ?? "2.0"},제곱미터,슬리버폴리곤 면적 기준",
                    $"슬리버형태지수,{SliverShapeIndexTextBox?.Text ?? "0.1"},무차원,슬리버폴리곤 형태지수 기준",
                    $"슬리버신장률,{SliverElongationTextBox?.Text ?? "10.0"},무차원,슬리버폴리곤 신장률 기준",
                    $"스파이크각도임계값,{SpikeAngleTextBox?.Text ?? "10.0"},도,스파이크 검출 각도 임계값",
                    $"링폐합오차,{RingClosureToleranceTextBox?.Text ?? "1e-8"},미터,링 폐합 허용 오차",
                    $"네트워크탐색거리,{NetworkSearchDistanceTextBox?.Text ?? "0.1"},미터,언더슛/오버슛 탐색 거리",
                    $"중복검사허용오차,{DuplicateToleranceTextBox?.Text ?? "0.001"},미터,중복 지오메트리 판정 허용 오차"
                };

                File.WriteAllLines(GeometryCriteriaPath, lines, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"지오메트리 검수 기준 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 확인 버튼 클릭
        /// </summary>
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 지오메트리 검수 기준 저장
                SaveGeometryCriteria();

                // 단계 플래그 저장
                if (FindName("Stage1EnabledCheck") is System.Windows.Controls.CheckBox c1) EnableStage1 = c1.IsChecked == true;
                if (FindName("Stage2EnabledCheck") is System.Windows.Controls.CheckBox c2) EnableStage2 = c2.IsChecked == true;
                if (FindName("Stage3EnabledCheck") is System.Windows.Controls.CheckBox c3) EnableStage3 = c3.IsChecked == true;
                if (FindName("Stage4EnabledCheck") is System.Windows.Controls.CheckBox c4) EnableStage4 = c4.IsChecked == true;
                if (FindName("Stage5EnabledCheck") is System.Windows.Controls.CheckBox c5) EnableStage5 = c5.IsChecked == true;

                // 선택 항목 저장
                SelectedStage1Items = _stage1Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                SelectedStage2Items = _stage2Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                SelectedStage3Items = _stage3Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                SelectedStage4Items = _stage4Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                SelectedStage5Items = _stage5Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public List<TableCheckConfig> SelectedStage1Items { get; private set; } = new();
        public List<SchemaCheckConfig> SelectedStage2Items { get; private set; } = new();
        public List<GeometryCheckConfig> SelectedStage3Items { get; private set; } = new();
        public List<AttributeCheckConfig> SelectedStage4Items { get; private set; } = new();
        public List<RelationCheckConfig> SelectedStage5Items { get; private set; } = new();

        /// <summary>
        /// 취소 버튼 클릭
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 기본값 복원 버튼 클릭
        /// </summary>
        private void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("모든 설정을 기본값으로 복원하시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                InitializeDefaultPaths();
                
                // 지오메트리 검수 기준 기본값 설정 - GeometryCriteria.CreateDefault() 사용
                var defaultCriteria = SpatialCheckPro.Models.GeometryCriteria.CreateDefault();
                if (MinLengthTextBox != null) MinLengthTextBox.Text = defaultCriteria.MinLineLength.ToString();
                if (MinAreaTextBox != null) MinAreaTextBox.Text = defaultCriteria.MinPolygonArea.ToString();
                if (SliverAreaTextBox != null) SliverAreaTextBox.Text = defaultCriteria.SliverArea.ToString();
                if (SliverShapeIndexTextBox != null) SliverShapeIndexTextBox.Text = defaultCriteria.SliverShapeIndex.ToString();
                if (SliverElongationTextBox != null) SliverElongationTextBox.Text = defaultCriteria.SliverElongation.ToString();
                if (DuplicateToleranceTextBox != null) DuplicateToleranceTextBox.Text = defaultCriteria.DuplicateCheckTolerance.ToString();
                if (OverlapToleranceTextBox != null) OverlapToleranceTextBox.Text = defaultCriteria.OverlapTolerance.ToString();
                if (SelfIntersectionAngleTextBox != null) SelfIntersectionAngleTextBox.Text = defaultCriteria.SelfIntersectionAngle.ToString();
                if (PolygonInPolygonDistanceTextBox != null) PolygonInPolygonDistanceTextBox.Text = defaultCriteria.PolygonInPolygonDistance.ToString();
                if (SpikeAngleTextBox != null) SpikeAngleTextBox.Text = defaultCriteria.SpikeAngleThresholdDegrees.ToString();
                if (RingClosureToleranceTextBox != null) RingClosureToleranceTextBox.Text = defaultCriteria.RingClosureTolerance.ToString("G");
                if (NetworkSearchDistanceTextBox != null) NetworkSearchDistanceTextBox.Text = defaultCriteria.NetworkSearchDistance.ToString();
                
                UpdateUI();
                MessageBox.Show("기본값으로 복원되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 시스템 리소스 분석기 초기화
        /// </summary>
        private void InitializeSystemResourceAnalyzer()
        {
            try
            {
                // 간단한 로거 생성 (실제 환경에서는 DI에서 가져옴)
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _logger = loggerFactory.CreateLogger<ValidationSettingsWindow>();
                
                var resourceAnalyzerLogger = loggerFactory.CreateLogger<SystemResourceAnalyzer>();
                _resourceAnalyzer = new SystemResourceAnalyzer(resourceAnalyzerLogger);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"시스템 리소스 분석기 초기화 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 시스템 리소스 상태 로드
        /// </summary>
        private async Task LoadSystemResourceStatusAsync()
        {
            try
            {
                if (_resourceAnalyzer == null)
                {
                    CurrentCpuStatus.Text = "분석기 없음";
                    CurrentMemoryStatus.Text = "분석기 없음";
                    return;
                }

                CurrentCpuStatus.Text = "분석 중...";
                CurrentMemoryStatus.Text = "분석 중...";

                var resourceInfo = await Task.Run(() => _resourceAnalyzer.AnalyzeSystemResources());
                
                // CPU 상태 표시
                var cpuStatus = $"{resourceInfo.ProcessorCount}개 코어 (권장 병렬도: {resourceInfo.RecommendedMaxParallelism}개)";
                CurrentCpuStatus.Text = cpuStatus;
                
                // 메모리 상태 표시
                var memoryStatus = $"{resourceInfo.TotalMemoryGB:F1}GB 총량, {resourceInfo.AvailableMemoryGB:F1}GB 사용가능 (권장 배치크기: {resourceInfo.RecommendedBatchSize}개)";
                CurrentMemoryStatus.Text = memoryStatus;
                
                // 시스템 부하에 따른 색상 변경
                var cpuColor = resourceInfo.SystemLoadLevel == SystemLoadLevel.High ? "Red" : 
                              resourceInfo.SystemLoadLevel == SystemLoadLevel.Medium ? "Orange" : "Green";
                CurrentCpuStatus.Foreground = (System.Windows.Media.Brush)FindResource($"System.Windows.Media.Brushes.{cpuColor}");
                
                // 권장 설정 적용
                if (MaxParallelismTextBox.Text == "4") // 기본값인 경우에만 자동 적용
                {
                    MaxParallelismTextBox.Text = resourceInfo.RecommendedMaxParallelism.ToString();
                }
                
                if (BatchSizeTextBox.Text == "1000") // 기본값인 경우에만 자동 적용
                {
                    BatchSizeTextBox.Text = resourceInfo.RecommendedBatchSize.ToString();
                }
                
                // 시스템 부하가 높은 경우 병렬 처리 비활성화 권장
                if (resourceInfo.SystemLoadLevel == SystemLoadLevel.High)
                {
                    EnableParallelProcessingCheck.IsChecked = false;
                    MessageBox.Show("시스템 부하가 높습니다. 병렬 처리를 비활성화하는 것을 권장합니다.", 
                        "시스템 부하 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                CurrentCpuStatus.Text = $"오류: {ex.Message}";
                CurrentMemoryStatus.Text = "분석 실패";
                CurrentCpuStatus.Foreground = System.Windows.Media.Brushes.Red;
                CurrentMemoryStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        /// <summary>
        /// 시스템 상태 새로고침 버튼 클릭
        /// </summary>
        private async void RefreshSystemStatus_Click(object sender, RoutedEventArgs e)
        {
            await LoadSystemResourceStatusAsync();
        }

        /// <summary>
        /// 설정 저장
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                // UI에서 설정값 읽기
                EnableParallelProcessing = EnableParallelProcessingCheck.IsChecked ?? true;
                
                if (int.TryParse(MaxParallelismTextBox.Text, out var maxParallelism))
                {
                    MaxParallelism = Math.Max(1, Math.Min(maxParallelism, Environment.ProcessorCount * 2));
                }
                
                if (int.TryParse(BatchSizeTextBox.Text, out var batchSize))
                {
                    BatchSize = Math.Max(100, Math.Min(batchSize, 50000));
                }

                // 설정 파일에 저장 (선택사항)
                SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 설정 로드
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                // UI에 설정값 적용
                EnableParallelProcessingCheck.IsChecked = EnableParallelProcessing;
                MaxParallelismTextBox.Text = MaxParallelism.ToString();
                BatchSizeTextBox.Text = BatchSize.ToString();

                // 설정 파일에서 로드 (선택사항)
                LoadSettingsFromFile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 설정을 파일에 저장
        /// </summary>
        private void SaveSettingsToFile()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "SpatialCheckPro", "validation_settings.json");
                
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir!);
                }

                var settings = new
                {
                    EnableParallelProcessing,
                    MaxParallelism,
                    BatchSize,
                    EnableStage1,
                    EnableStage2,
                    EnableStage3,
                    EnableStage4,
                    EnableStage5,
                    SavedAt = DateTime.Now
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "설정 파일 저장 실패");
            }
        }

        /// <summary>
        /// 설정을 파일에서 로드
        /// </summary>
        private void LoadSettingsFromFile()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "SpatialCheckPro", "validation_settings.json");
                
                if (!File.Exists(settingsPath))
                    return;

                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                
                // JSON에서 값 추출 (간단한 구현)
                if (settings != null)
                {
                    // 실제 구현에서는 더 안전한 JSON 파싱 필요
                    _logger?.LogInformation("설정 파일에서 로드됨: {SettingsPath}", settingsPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "설정 파일 로드 실패");
            }
        }

        /// <summary>
        /// 실시간 모니터링 초기화
        /// </summary>
        private void InitializeRealTimeMonitoring()
        {
            try
            {
                _monitoringTimer = new System.Windows.Threading.DispatcherTimer();
                _monitoringTimer.Tick += MonitoringTimer_Tick;
                
                // 모니터링 간격 설정
                if (int.TryParse(MonitoringIntervalTextBox.Text, out var interval))
                {
                    MonitoringIntervalSeconds = Math.Max(1, Math.Min(interval, 60)); // 1-60초 범위
                    _monitoringTimer.Interval = TimeSpan.FromSeconds(MonitoringIntervalSeconds);
                }
                
                // 실시간 모니터링 체크박스 이벤트 연결
                EnableRealTimeMonitoringCheck.Checked += EnableRealTimeMonitoringCheck_Checked;
                EnableRealTimeMonitoringCheck.Unchecked += EnableRealTimeMonitoringCheck_Unchecked;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "실시간 모니터링 초기화 실패");
            }
        }

        /// <summary>
        /// 실시간 모니터링 타이머 이벤트
        /// </summary>
        private async void MonitoringTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                await LoadSystemResourceStatusAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "실시간 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 실시간 모니터링 활성화
        /// </summary>
        private void EnableRealTimeMonitoringCheck_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnableRealTimeMonitoring = true;
                
                // 모니터링 간격 업데이트
                if (int.TryParse(MonitoringIntervalTextBox.Text, out var interval))
                {
                    MonitoringIntervalSeconds = Math.Max(1, Math.Min(interval, 60));
                    if (_monitoringTimer != null)
                    {
                        _monitoringTimer.Interval = TimeSpan.FromSeconds(MonitoringIntervalSeconds);
                    }
                }
                
                // 타이머 시작
                _monitoringTimer?.Start();
                
                _logger?.LogInformation("실시간 모니터링 활성화: {Interval}초 간격", MonitoringIntervalSeconds);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "실시간 모니터링 활성화 실패");
            }
        }

        /// <summary>
        /// 실시간 모니터링 비활성화
        /// </summary>
        private void EnableRealTimeMonitoringCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnableRealTimeMonitoring = false;
                
                // 타이머 중지
                _monitoringTimer?.Stop();
                
                _logger?.LogInformation("실시간 모니터링 비활성화");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "실시간 모니터링 비활성화 실패");
            }
        }

        /// <summary>
        /// 창 종료 시 리소스 정리
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _monitoringTimer?.Stop();
                _monitoringTimer = null;
                
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "창 종료 시 리소스 정리 실패");
            }
        }
    }
}