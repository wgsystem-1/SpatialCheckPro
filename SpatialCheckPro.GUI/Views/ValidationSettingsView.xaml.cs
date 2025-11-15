using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using CsvHelper;
using System.IO;
using System.Globalization;
using SpatialCheckPro.Models.Config;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Collections.Generic;
using SpatialCheckPro.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 설정 사용자 컨트롤 호스트
    /// </summary>
    public partial class ValidationSettingsView : UserControl
    {
        // 시스템 리소스 설정
        public bool EnableParallelProcessing { get; set; } = true;
        public int MaxParallelism { get; set; } = Environment.ProcessorCount;
        public int BatchSize { get; set; } = 1000;
        public bool EnableRealTimeMonitoring { get; set; } = false;
        public int MonitoringIntervalSeconds { get; set; } = 5;

        // 수동 스트리밍 모드 설정
        public bool ForceStreamingMode { get; set; } = false;
        public int CustomBatchSize { get; set; } = 1000;
        public int MaxMemoryUsageMB { get; set; } = 512;
        public bool EnablePrefetching { get; set; } = false;
        public bool EnableParallelStreaming { get; set; } = false;

        // private CentralizedResourceMonitor? _resourceMonitor;
        // private ParallelPerformanceMonitor? _performanceMonitor;
        // private PerformanceBenchmarkService? _benchmarkService;
        // private ILogger<ValidationSettingsView>? _logger;
        // private System.Windows.Threading.DispatcherTimer? _monitoringTimer;
        // private BenchmarkResult? _lastBenchmarkResult;

        // 설정 변경 이벤트
        // public event EventHandler<PerformanceSettingsChangedEventArgs>? PerformanceSettingsChanged;

        public ValidationSettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private bool _handlersAttached = false;

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                // 이벤트 중복 연결 방지
                if (!_handlersAttached)
                {
                    ApplyButton.Click -= ApplyButton_Click;
                    ApplyButton.Click += ApplyButton_Click;
                    _handlersAttached = true;
                }
                InitializeDefaultPaths();
                // 초기 로드
                _ = LoadStage1Async();
                _ = LoadStage2Async();
                _ = LoadStage3Async();
                _ = LoadStage4Async();
                _ = LoadStage5Async();
                
                // 시스템 리소스 분석 초기화

                // 수동 스트리밍 모드 UI 초기화
                InitializeManualStreamingModeUI();
                // InitializeSystemResourceAnalyzer();
                // _ = LoadSystemResourceStatusAsync();
                // InitializeRealTimeMonitoring();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 뷰 초기화 오류: {ex.Message}");
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySettingsToMain();
        }

        // 간단한 로더: 1단계만 포팅 (필요 단계 추가 가능)
        private sealed class PreviewRow<T>
        {
            public bool Use { get; set; } = true; // 기본 사용
            public T Item { get; set; }
            public PreviewRow(T item) { Item = item; }
        }

        private readonly ObservableCollection<PreviewRow<TableCheckConfig>> _stage1Rows = new();
        private readonly ObservableCollection<PreviewRow<SchemaCheckConfig>> _stage2Rows = new();
        private readonly ObservableCollection<PreviewRow<GeometryCheckConfig>> _stage3Rows = new();
        private readonly ObservableCollection<PreviewRow<AttributeCheckConfig>> _stage4Rows = new();  // 순서 변경: 속성 검수
        private readonly ObservableCollection<PreviewRow<RelationCheckConfig>> _stage5Rows = new();  // 순서 변경: 관계 검수

        private void InitializeDefaultPaths()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var configDirectory = System.IO.Path.Combine(appDirectory, "Config");
            TableConfigPathTextBox.Text = System.IO.Path.Combine(configDirectory, "1_table_check.csv");
            SchemaConfigPathTextBox.Text = System.IO.Path.Combine(configDirectory, "2_schema_check.csv");
            GeometryConfigPathTextBox.Text = System.IO.Path.Combine(configDirectory, "3_geometry_check.csv");
            AttributeConfigPathTextBox.Text = System.IO.Path.Combine(configDirectory, "4_attribute_check.csv");
            RelationConfigPathTextBox.Text = System.IO.Path.Combine(configDirectory, "5_relation_check.csv");
            GeometryCriteriaPathTextBox.Text = System.IO.Path.Combine(configDirectory, "geometry_criteria.csv");
            CodelistPathTextBox.Text = System.IO.Path.Combine(configDirectory, "codelist.csv");
        }

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

        private async System.Threading.Tasks.Task LoadStage1Async()
        {
            _stage1Rows.Clear();
            var path = TableConfigPathTextBox.Text;
            if (!File.Exists(path)) return;
            using var reader = new StreamReader(path, DetectCsvEncoding(path), detectEncodingFromByteOrderMarks: false);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Context.Configuration.HeaderValidated = null;
            csv.Context.Configuration.MissingFieldFound = null;
            var items = await System.Threading.Tasks.Task.Run(() => csv.GetRecords<TableCheckConfig>().ToList());
            foreach (var it in items) _stage1Rows.Add(new PreviewRow<TableCheckConfig>(it));
            Stage1Grid.ItemsSource = _stage1Rows;
        }

        private async System.Threading.Tasks.Task LoadStage2Async()
        {
            _stage2Rows.Clear();
            var path = SchemaConfigPathTextBox.Text;

            // 디버깅: 파일 경로 확인
            System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 경로: {path}");
            System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 파일 존재: {File.Exists(path)}");

            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 파일이 존재하지 않습니다: {path}");
                return;
            }

            try
            {
                using var reader = new StreamReader(path, DetectCsvEncoding(path), detectEncodingFromByteOrderMarks: false);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;

                var items = await System.Threading.Tasks.Task.Run(() => csv.GetRecords<SchemaCheckConfig>().ToList());

                System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 로드된 항목 수: {items.Count}");

                foreach (var it in items)
                {
                    _stage2Rows.Add(new PreviewRow<SchemaCheckConfig>(it));
                }

                // UI 스레드에서 ItemsSource 설정
                await Dispatcher.InvokeAsync(() =>
                {
                    Stage2Grid.ItemsSource = _stage2Rows;
                    System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] DataGrid 바인딩 완료: {_stage2Rows.Count}개 항목");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 예외 발생: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LoadStage2Async] 스택 트레이스: {ex.StackTrace}");

                // 사용자에게 오류 표시
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"스키마 설정 파일 로드 중 오류가 발생했습니다:\n\n{ex.Message}",
                                  "파일 로드 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private async System.Threading.Tasks.Task LoadStage3Async()
        {
            _stage3Rows.Clear();
            var path = GeometryConfigPathTextBox.Text;
            if (!File.Exists(path)) return;
            using var reader = new StreamReader(path, DetectCsvEncoding(path), detectEncodingFromByteOrderMarks: false);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Context.Configuration.HeaderValidated = null;
            csv.Context.Configuration.MissingFieldFound = null;
            var items = await System.Threading.Tasks.Task.Run(() => csv.GetRecords<GeometryCheckConfig>().ToList());
            foreach (var it in items) _stage3Rows.Add(new PreviewRow<GeometryCheckConfig>(it));
            Stage3Grid.ItemsSource = _stage3Rows;
        }

        private async System.Threading.Tasks.Task LoadStage4Async()
        {
            _stage4Rows.Clear();
            var path = AttributeConfigPathTextBox.Text;  // 4단계 = 속성 검수
            if (!File.Exists(path)) return;
            using var reader = new StreamReader(path, DetectCsvEncoding(path), detectEncodingFromByteOrderMarks: false);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Context.Configuration.HeaderValidated = null;
            csv.Context.Configuration.MissingFieldFound = null;
            var items = await System.Threading.Tasks.Task.Run(() => csv.GetRecords<AttributeCheckConfig>().ToList());
            foreach (var it in items) _stage4Rows.Add(new PreviewRow<AttributeCheckConfig>(it));
            Stage4Grid.ItemsSource = _stage4Rows;
        }

        private async System.Threading.Tasks.Task LoadStage5Async()
        {
            _stage5Rows.Clear();
            var path = RelationConfigPathTextBox.Text;  // 5단계 = 관계 검수
            if (!File.Exists(path)) return;
            // 파일 잠금/인코딩 이슈를 피하기 위해 ReadWrite 공유와 유연한 CSV 설정 적용
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, DetectCsvEncoding(path), detectEncodingFromByteOrderMarks: false);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Context.Configuration.HeaderValidated = null;
            csv.Context.Configuration.MissingFieldFound = null;
            await System.Threading.Tasks.Task.Run(() =>
            {
                if (!csv.Read()) return;
                csv.ReadHeader();
                var headers = csv.HeaderRecord ?? Array.Empty<string>();
                while (csv.Read())
                {
                    string Get(string n)
                    {
                        if (!headers.Contains(n, StringComparer.OrdinalIgnoreCase)) return string.Empty;
                        try { return csv.GetField(n) ?? string.Empty; } catch { return string.Empty; }
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
                    if (double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var tol)) cfg.Tolerance = tol;
                    Application.Current.Dispatcher.Invoke(() => _stage5Rows.Add(new PreviewRow<RelationCheckConfig>(cfg)));
                }
            });
            Stage5Grid.ItemsSource = _stage5Rows;
        }

        private async void ApplySettingsToMain()
        {
            var main = Application.Current?.MainWindow as MainWindow;
            if (main == null) return;

            string warn = string.Empty;
            string err = string.Empty;

            // 체크박스 플래그 적용
            main.GetType().GetField("_enableStage1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, (Stage1EnabledCheck.IsChecked == true));
            main.GetType().GetField("_enableStage2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, (Stage2EnabledCheck.IsChecked == true));
            main.GetType().GetField("_enableStage3", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, (Stage3EnabledCheck.IsChecked == true));
            main.GetType().GetField("_enableStage4", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, (Stage4EnabledCheck.IsChecked == true));
            main.GetType().GetField("_enableStage5", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, (Stage5EnabledCheck.IsChecked == true));

            // 경로 적용
            if (!string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
            {
                var target = TargetPathTextBox.Text;
                main.GetType().GetField("_selectedFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, target);
                try
                {
                    // 대상이 상위 폴더인 경우 하위 .gdb를 수집하여 배치 대상 설정
                    List<string> gdbs = new List<string>();

                    if (!System.IO.Directory.Exists(target) && !System.IO.File.Exists(target))
                    {
                        warn += $"\n- 검수 대상 경로를 찾을 수 없습니다: {target}";
                        System.Windows.MessageBox.Show($"검수 대상 폴더 또는 파일이 존재하지 않습니다.\n\n경로: {target}",
                                                       "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                        main.GetType().GetField("_selectedFilePaths",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                            .SetValue(main, new List<string>());
                        return;
                    }
                    else if (System.IO.Directory.Exists(target) && !target.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            gdbs = System.IO.Directory.GetDirectories(target, "*.gdb", System.IO.SearchOption.AllDirectories).ToList();
                            if (gdbs.Count == 0)
                            {
                                warn += $"\n- 지정된 폴더에 .gdb 파일이 없습니다: {target}";
                                warn += "\n  FileGDB 폴더를 직접 선택하거나 .gdb 파일이 있는 상위 폴더를 선택해주세요.";
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            err += $"\n- 폴더 접근 권한이 없습니다: {target}";
                        }
                        catch (System.IO.IOException ex)
                        {
                            err += $"\n- 폴더 읽기 오류: {target} ({ex.Message})";
                        }
                        main.GetType().GetField("_selectedFilePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, gdbs);
                    }
                    else if (System.IO.Directory.Exists(target) && target.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    {
                        // FileGDB 폴더인 경우 추가 검증
                        if (!IsValidFileGdbDirectory(target))
                        {
                            warn += $"\n- 유효하지 않은 FileGDB 폴더: {target}";
                            System.Windows.MessageBox.Show($"유효하지 않은 FileGDB 폴더입니다.\n\n경로: {target}\n\n올바른 FileGDB 폴더를 선택해주세요.",
                                                           "FileGDB 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        gdbs = new List<string> { target };
                        main.GetType().GetField("_selectedFilePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, gdbs);
                    }
                    else
                    {
                        warn += $"\n- 잘못된 경로 형식: {target}";
                        warn += "\n  FileGDB 폴더 또는 .gdb 파일이 있는 폴더를 선택해주세요.";
                        main.GetType().GetField("_selectedFilePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, gdbs);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"경로 확인 중 오류가 발생했습니다.\n\n{ex.Message}",
                                                   "예외 처리", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 파일 경로들 유효성 검증
            void SetPath(string field, string value, bool mustExist)
            {
                main.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, value);
                if (mustExist && !string.IsNullOrWhiteSpace(value) && !System.IO.File.Exists(value))
                {
                    err += $"\n- 파일을 찾을 수 없음: {value}";
                }
                // 포맷 검증: csv 확장자
                if (!string.IsNullOrWhiteSpace(value) && !value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    warn += $"\n- CSV 형식이 아님: {value}";
                }
            }

            SetPath("_customTableConfigPath", TableConfigPathTextBox.Text, true);
            SetPath("_customSchemaConfigPath", SchemaConfigPathTextBox.Text, true);
            SetPath("_customGeometryConfigPath", GeometryConfigPathTextBox.Text, true);
            SetPath("_customRelationConfigPath", RelationConfigPathTextBox.Text, true);
            SetPath("_customAttributeConfigPath", AttributeConfigPathTextBox.Text, true);
            SetPath("_customGeometryCriteriaPath", GeometryCriteriaPathTextBox.Text, true);
            SetPath("_customCodelistPath", CodelistPathTextBox.Text, true);

            // 필수 경로: 대상 경로, 3단계 사용 시 기준 파일
            if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
            {
                MessageBox.Show("검수 대상 경로가 지정되지 않았습니다.", "필수 경로 누락", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Stage3EnabledCheck.IsChecked == true && string.IsNullOrWhiteSpace(GeometryCriteriaPathTextBox.Text))
            {
                MessageBox.Show("3단계 사용 시 지오메트리 기준 파일이 필요합니다.", "필수 경로 누락", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(err))
            {
                MessageBox.Show($"일부 설정 파일을 확인하세요:{err}", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else
            {
                // 행 단위 선택 전달: 체크된 항목만 런타임 대상에 반영
                try
                {
                    // internal 필드에 직접 할당 (동일 어셈블리)
                    main._selectedStage1Items = _stage1Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                    main._selectedStage2Items = _stage2Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                    main._selectedStage3Items = _stage3Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                    main._selectedStage4Items = _stage4Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                    main._selectedStage5Items = _stage5Rows.Where(r => r.Use).Select(r => r.Item).ToList();
                }
                catch { /* 선택 전달 실패는致命아님: 기본 전체 실행 */ }

                var msg = "설정이 적용되었습니다. 다음 검수 실행 시 반영됩니다.";
                if (!string.IsNullOrEmpty(warn)) msg += $"\n\n참고:{warn}";
                MessageBox.Show(msg, "설정 적용", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // 파일/경로 브라우저 및 토글 핸들러 (간단 포팅)
        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = ".gdb 폴더 또는 데이터 경로를 선택하세요";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TargetPathTextBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void BrowseTableConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "1단계 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "1_table_check.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                TableConfigPathTextBox.Text = ofd.FileName;
            }
        }

        private void BrowseSchemaConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "2단계 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "2_schema_check.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                SchemaConfigPathTextBox.Text = ofd.FileName;
            }
        }

        private void BrowseGeometryConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "3단계 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "3_geometry_check.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                GeometryConfigPathTextBox.Text = ofd.FileName;
            }
        }

        private void BrowseRelationConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "5단계 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "5_relation_check.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                RelationConfigPathTextBox.Text = ofd.FileName;
                // 선택 직후 미리보기 갱신 (5단계)
                _ = LoadStage5Async();
            }
        }

        private void BrowseAttributeConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "4단계 설정 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "4_attribute_check.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                AttributeConfigPathTextBox.Text = ofd.FileName;
                // 선택 직후 미리보기 갱신 (4단계)
                _ = LoadStage4Async();
            }
        }

        private void BrowseGeometryCriteria_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "지오메트리 기준 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "geometry_criteria.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                GeometryCriteriaPathTextBox.Text = ofd.FileName;
            }
        }

        private void BrowseCodelist_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "코드리스트 파일 선택",
                Filter = "CSV 파일|*.csv|모든 파일|*.*",
                FilterIndex = 1,
                FileName = "codelist.csv"
            };
            if (ofd.ShowDialog() == true)
            {
                CodelistPathTextBox.Text = ofd.FileName;
            }
        }

        private void Stage1ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            if (Stage1Grid?.ItemsSource is System.Collections.IEnumerable rows)
            {
                bool? isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked;
                foreach (var row in rows)
                {
                    var prop = row.GetType().GetProperty("Use");
                    if (prop != null) prop.SetValue(row, isChecked == true);
                }
                Stage1Grid.Items.Refresh();
            }
        }

        private void Stage1Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (Stage1SearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                Stage1Grid.ItemsSource = _stage1Rows;
                return;
            }
            var filtered = new ObservableCollection<PreviewRow<TableCheckConfig>>(
                _stage1Rows.Where(r =>
                    (r.Item.TableId ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.TableName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.GeometryType ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.CoordinateSystem ?? string.Empty).ToLowerInvariant().Contains(q)));
            Stage1Grid.ItemsSource = filtered;
        }

        private void Stage2ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            if (Stage2Grid?.ItemsSource is System.Collections.IEnumerable rows)
            {
                bool? isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked;
                foreach (var row in rows)
                {
                    var prop = row.GetType().GetProperty("Use");
                    if (prop != null) prop.SetValue(row, isChecked == true);
                }
                Stage2Grid.Items.Refresh();
            }
        }

        private void Stage2Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (Stage2SearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) { Stage2Grid.ItemsSource = _stage2Rows; return; }
            var filtered = new ObservableCollection<PreviewRow<SchemaCheckConfig>>(
                _stage2Rows.Where(r =>
                    (r.Item.TableId ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.ColumnName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.DataType ?? string.Empty).ToLowerInvariant().Contains(q)));
            Stage2Grid.ItemsSource = filtered;
        }

        private void Stage3ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            if (Stage3Grid?.ItemsSource is System.Collections.IEnumerable rows)
            {
                bool? isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked;
                foreach (var row in rows)
                {
                    var prop = row.GetType().GetProperty("Use");
                    if (prop != null) prop.SetValue(row, isChecked == true);
                }
                Stage3Grid.Items.Refresh();
            }
        }

        private void Stage3Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (Stage3SearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) { Stage3Grid.ItemsSource = _stage3Rows; return; }
            // 다중 조건: 공백으로 구분된 토큰 모두 포함
            var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool MatchAll(string s) => tokens.All(t => s.Contains(t));
            var filtered = new ObservableCollection<PreviewRow<GeometryCheckConfig>>(
                _stage3Rows.Where(r =>
                {
                    var a = (r.Item.TableId ?? string.Empty).ToLowerInvariant();
                    var b = (r.Item.TableName ?? string.Empty).ToLowerInvariant();
                    var c = (r.Item.GeometryType ?? string.Empty).ToLowerInvariant();
                    return MatchAll(a) || MatchAll(b) || MatchAll(c);
                }));
            Stage3Grid.ItemsSource = filtered;
        }

        private void Stage4ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            if (Stage4Grid?.ItemsSource is System.Collections.IEnumerable rows)
            {
                bool? isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked;
                foreach (var row in rows)
                {
                    var prop = row.GetType().GetProperty("Use");
                    if (prop != null) prop.SetValue(row, isChecked == true);
                }
                Stage4Grid.Items.Refresh();
            }
        }

        private void Stage4Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (Stage4SearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) { Stage4Grid.ItemsSource = _stage4Rows; return; }
            // 4단계 = 속성 검수 (순서 변경)
            var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool MatchAll(string s) => tokens.All(t => s.Contains(t));
            var filtered = new ObservableCollection<PreviewRow<AttributeCheckConfig>>(
                _stage4Rows.Where(r =>
                {
                    var a = (r.Item.RuleId ?? string.Empty).ToLowerInvariant();
                    var b = (r.Item.TableId ?? string.Empty).ToLowerInvariant();
                    var c = (r.Item.TableName ?? string.Empty).ToLowerInvariant();
                    var d = (r.Item.FieldName ?? string.Empty).ToLowerInvariant();
                    var e = (r.Item.CheckType ?? string.Empty).ToLowerInvariant();
                    var f = (r.Item.Parameters ?? string.Empty).ToLowerInvariant();
                    var g = (r.Item.Severity ?? string.Empty).ToLowerInvariant();
                    return MatchAll(a) || MatchAll(b) || MatchAll(c) || MatchAll(d) || MatchAll(e) || MatchAll(f) || MatchAll(g);
                }));
            Stage4Grid.ItemsSource = filtered;
        }

        private void Stage5ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            if (Stage5Grid?.ItemsSource is System.Collections.IEnumerable rows)
            {
                bool? isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked;
                foreach (var row in rows)
                {
                    var prop = row.GetType().GetProperty("Use");
                    if (prop != null) prop.SetValue(row, isChecked == true);
                }
                Stage5Grid.Items.Refresh();
            }
        }

        private void Stage5Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (Stage5SearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) { Stage5Grid.ItemsSource = _stage5Rows; return; }
            // 5단계 = 관계 검수 (순서 변경)
            var filtered = new ObservableCollection<PreviewRow<RelationCheckConfig>>(
                _stage5Rows.Where(r =>
                    (r.Item.RuleId ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.MainTableId ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (r.Item.RelatedTableId ?? string.Empty).ToLowerInvariant().Contains(q)));
            Stage5Grid.ItemsSource = filtered;
        }

        // 정렬 핸들러: 컬럼 헤더 클릭 시 속성명 기준 정렬 토글
        private ListSortDirection ToggleDirection(ListSortDirection dir)
            => dir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;

        private void Stage3Grid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column as DataGridColumn;
            var binding = (column as DataGridBoundColumn)?.Binding as System.Windows.Data.Binding;
            var propPath = binding?.Path?.Path ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propPath)) return;

            // 다중 조건 정렬: Shift 키 누르면 누적, 아니면 초기화
            var view = CollectionViewSource.GetDefaultView(Stage3Grid.ItemsSource);
            if (view == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                view.SortDescriptions.Clear();
                foreach (var col in Stage3Grid.Columns) col.SortDirection = null;
            }

            var dir = column.SortDirection ?? ListSortDirection.Ascending;
            dir = ToggleDirection(dir);
            column.SortDirection = dir;

            // Item.* 또는 Use 지원
            var sortProp = propPath;
            if (sortProp == nameof(PreviewRow<GeometryCheckConfig>.Use))
            {
                view.SortDescriptions.Add(new SortDescription(sortProp, dir));
            }
            else
            {
                if (!sortProp.StartsWith("Item.")) sortProp = "Item." + sortProp;
                view.SortDescriptions.Add(new SortDescription(sortProp, dir));
            }
        }

        private void Stage5Grid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column as DataGridColumn;
            var binding = (column as DataGridBoundColumn)?.Binding as System.Windows.Data.Binding;
            var propPath = binding?.Path?.Path ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propPath)) return;

            var view = CollectionViewSource.GetDefaultView(Stage5Grid.ItemsSource);
            if (view == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                view.SortDescriptions.Clear();
                foreach (var col in Stage5Grid.Columns) col.SortDirection = null;
            }

            var dir = column.SortDirection ?? ListSortDirection.Ascending;
            dir = ToggleDirection(dir);
            column.SortDirection = dir;

            var sortProp = propPath;
            if (sortProp == nameof(PreviewRow<RelationCheckConfig>.Use))
            {
                view.SortDescriptions.Add(new SortDescription(sortProp, dir));
            }
            else
            {
                if (!sortProp.StartsWith("Item.")) sortProp = "Item." + sortProp;
                view.SortDescriptions.Add(new SortDescription(sortProp, dir));
            }
        }

        private static object? GetPropValue(object obj, string propName)
        {
            try { return obj.GetType().GetProperty(propName)?.GetValue(obj); } catch { return null; }
        }

        // ====================================================================
        // 아래의 모든 부가 기능 관련 메서드들을 주석 처리합니다.
        // ====================================================================

        /*
        /// <summary>
        /// 시스템 리소스 분석기 초기화
        /// </summary>
        // private void InitializeSystemResourceAnalyzer()
        // {
        //     try
        //     {
        //         // 간단한 로거 생성 (실제 환경에서는 DI에서 가져옴)
        //         var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        //         _logger = loggerFactory.CreateLogger<ValidationSettingsView>();
                
        //         // 중앙집중식 모니터 사용 (실제 환경에서는 DI에서 가져옴)
        //         var resourceAnalyzerLogger = loggerFactory.CreateLogger<SystemResourceAnalyzer>();
        //         var resourceAnalyzer = new SystemResourceAnalyzer(resourceAnalyzerLogger);
        //         var settingsLogger = loggerFactory.CreateLogger<PerformanceSettings>();
        //         var settings = new PerformanceSettings();
        //         var monitorLogger = loggerFactory.CreateLogger<CentralizedResourceMonitor>();
        //         _resourceMonitor = new CentralizedResourceMonitor(monitorLogger, resourceAnalyzer, settings);
                
        //         // 성능 모니터링 초기화
        //         var performanceMonitorLogger = loggerFactory.CreateLogger<ParallelPerformanceMonitor>();
        //         _performanceMonitor = new ParallelPerformanceMonitor(performanceMonitorLogger);
                
        //         // 벤치마크 서비스 초기화
        //         var benchmarkLogger = loggerFactory.CreateLogger<PerformanceBenchmarkService>();
        //         var memoryOptimizationLogger = loggerFactory.CreateLogger<MemoryOptimizationService>();
        //         var memoryOptimization = new MemoryOptimizationService(memoryOptimizationLogger, settings);
        //         _benchmarkService = new PerformanceBenchmarkService(benchmarkLogger, _performanceMonitor, resourceAnalyzer, memoryOptimization);
        //     }
        //     catch (Exception ex)
        //     {
        //         MessageBox.Show($"시스템 리소스 분석기 초기화 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        //     }
        // }

        /// <summary>
        /// 시스템 리소스 상태 로드
        /// </summary>
        private async Task LoadSystemResourceStatusAsync()
        {
            try
            {
                if (_resourceMonitor == null)
                {
                    CurrentCpuStatus.Text = "분석기 없음";
                    CurrentMemoryStatus.Text = "분석기 없음";
                    return;
                }

                CurrentCpuStatus.Text = "분석 중...";
                CurrentMemoryStatus.Text = "분석 중...";

                var resourceInfo = await Task.Run(() => _resourceMonitor.GetResourceInfoForRequester("ValidationSettingsView"));
                
                // CPU 상태 표시
                var cpuStatus = $"{resourceInfo.ProcessorCount}개 코어 (권장 병렬도: {resourceInfo.RecommendedMaxParallelism}개)";
                CurrentCpuStatus.Text = cpuStatus;
                
                // 메모리 상태 표시
                var memoryStatus = $"{resourceInfo.TotalMemoryGB:F1}GB 총량, {resourceInfo.AvailableMemoryGB:F1}GB 사용가능 (권장 배치크기: {resourceInfo.RecommendedBatchSize}개)";
                CurrentMemoryStatus.Text = memoryStatus;
                
                // 시스템 부하에 따른 색상 변경
                var cpuColor = resourceInfo.SystemLoadLevel == SystemLoadLevel.High ? System.Windows.Media.Brushes.Red : 
                              resourceInfo.SystemLoadLevel == SystemLoadLevel.Medium ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Green;
                CurrentCpuStatus.Foreground = cpuColor;
                
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

                EnableRealTimeMonitoring = EnableRealTimeMonitoringCheck.IsChecked ?? false;
                
                if (int.TryParse(MonitoringIntervalTextBox.Text, out var interval))
                {
                    MonitoringIntervalSeconds = Math.Max(1, Math.Min(interval, 60));
                }

                _logger?.LogInformation("설정 저장 완료: 병렬처리={ParallelProcessing}, 병렬도={Parallelism}, 배치크기={BatchSize}", 
                    EnableParallelProcessing, MaxParallelism, BatchSize);
                
                // 설정 변경 이벤트 발생
                NotifySettingsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 설정 변경 시 이벤트 발생
        /// </summary>
        private void NotifySettingsChanged()
        {
            try
            {
                PerformanceSettingsChanged?.Invoke(this, new PerformanceSettingsChangedEventArgs
                {
                    EnableParallelProcessing = EnableParallelProcessing,
                    MaxParallelism = MaxParallelism,
                    BatchSize = BatchSize,
                    EnableRealTimeMonitoring = EnableRealTimeMonitoring,
                    MonitoringIntervalSeconds = MonitoringIntervalSeconds
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "설정 변경 이벤트 발생 실패");
            }
        }

        /// <summary>
        /// 성능 모니터링 초기화
        /// </summary>
        private void InitializePerformanceMonitoring()
        {
            try
            {
                if (_performanceMonitor != null)
                {
                    // 성능 보고서 이벤트 구독
                    _performanceMonitor.PerformanceReportGenerated += OnPerformanceReportGenerated;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"성능 모니터링 초기화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 성능 정보 로드
        /// </summary>
        private async Task LoadPerformanceInfoAsync()
        {
            try
            {
                if (_performanceMonitor == null)
                {
                    PerformanceInfoText.Text = "성능 모니터 없음";
                    PerformanceStatsText.Text = "성능 모니터 없음";
                    PerformanceTrendText.Text = "성능 모니터 없음";
                    return;
                }

                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var summary = _performanceMonitor.GetPerformanceSummary();
                            var allMetrics = _performanceMonitor.GetAllMetrics();
                            
                            // 실시간 성능 정보
                            PerformanceInfoText.Text = $"실행 중 작업: {summary.RunningOperations}개\n" +
                                                      $"완료된 작업: {summary.CompletedOperations}개\n" +
                                                      $"실패한 작업: {summary.FailedOperations}개\n" +
                                                      $"현재 CPU 사용률: {summary.CurrentCpuUsage:F1}%\n" +
                                                      $"현재 메모리 사용량: {summary.CurrentMemoryUsage}MB";
                            
                            // 성능 통계
                            PerformanceStatsText.Text = $"평균 처리 시간: {summary.AverageDuration:F1}초\n" +
                                                       $"평균 처리 속도: {summary.AverageItemsPerSecond:F1} items/sec\n" +
                                                       $"총 처리된 항목: {summary.TotalItemsProcessed}개\n" +
                                                       $"최근 스냅샷: {summary.RecentSnapshots}개";
                            
                            // 성능 트렌드 (간단한 표시)
                            var recentMetrics = allMetrics.Where(m => m.Status == OperationStatus.Completed)
                                                         .OrderByDescending(m => m.EndTime)
                                                         .Take(5)
                                                         .ToList();
                            
                            if (recentMetrics.Any())
                            {
                                var avgRecentSpeed = recentMetrics.Average(m => m.FinalItemsPerSecond);
                                PerformanceTrendText.Text = $"최근 평균 처리 속도: {avgRecentSpeed:F1} items/sec\n" +
                                                           $"최근 완료된 작업: {recentMetrics.Count}개\n" +
                                                           $"최고 성능 작업: {recentMetrics.Max(m => m.FinalItemsPerSecond):F1} items/sec";
                            }
                            else
                            {
                                PerformanceTrendText.Text = "아직 완료된 작업이 없습니다.";
                            }
                        }
                        catch (Exception ex)
                        {
                            PerformanceInfoText.Text = $"오류: {ex.Message}";
                            PerformanceStatsText.Text = $"오류: {ex.Message}";
                            PerformanceTrendText.Text = $"오류: {ex.Message}";
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"성능 정보 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 성능 보고서 생성 이벤트 핸들러
        /// </summary>
        private void OnPerformanceReportGenerated(object? sender, PerformanceReportEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var report = e.Report;
                    System.Diagnostics.Debug.WriteLine($"성능 보고서 생성됨: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
                    System.Diagnostics.Debug.WriteLine($"완료된 작업: {report.Summary.CompletedOperations}개");
                    System.Diagnostics.Debug.WriteLine($"평균 처리 속도: {report.Summary.AverageItemsPerSecond:F1} items/sec");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"성능 보고서 처리 실패: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 성능 정보 새로고침 버튼 클릭
        /// </summary>
        private async void RefreshPerformance_Click(object sender, RoutedEventArgs e)
        {
            await LoadPerformanceInfoAsync();
        }

        /// <summary>
        /// 성능 보고서 내보내기 버튼 클릭
        /// </summary>
        private void ExportPerformance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_performanceMonitor == null)
                {
                    MessageBox.Show("성능 모니터가 초기화되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var summary = _performanceMonitor.GetPerformanceSummary();
                var allMetrics = _performanceMonitor.GetAllMetrics();
                
                var report = $"=== 성능 보고서 ===\n" +
                            $"생성 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                            $"총 작업 수: {summary.TotalOperations}개\n" +
                            $"완료된 작업: {summary.CompletedOperations}개\n" +
                            $"실패한 작업: {summary.FailedOperations}개\n" +
                            $"실행 중인 작업: {summary.RunningOperations}개\n" +
                            $"평균 처리 시간: {summary.AverageDuration:F1}초\n" +
                            $"평균 처리 속도: {summary.AverageItemsPerSecond:F1} items/sec\n" +
                            $"총 처리된 항목: {summary.TotalItemsProcessed}개\n" +
                            $"현재 CPU 사용률: {summary.CurrentCpuUsage:F1}%\n" +
                            $"현재 메모리 사용량: {summary.CurrentMemoryUsage}MB\n\n" +
                            $"=== 최근 작업 상세 ===\n";
                
                var recentMetrics = allMetrics.OrderByDescending(m => m.StartTime).Take(10);
                foreach (var metric in recentMetrics)
                {
                    report += $"{metric.OperationName}: {metric.FinalItemsPerSecond:F1} items/sec, " +
                             $"{(metric.EndTime ?? DateTime.Now) - metric.StartTime:F1}초\n";
                }
                
                var fileName = $"PerformanceReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                File.WriteAllText(filePath, report);
                
                MessageBox.Show($"성능 보고서가 저장되었습니다.\n경로: {filePath}", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"성능 보고서 내보내기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 성능 기록 초기화 버튼 클릭
        /// </summary>
        private void ClearPerformance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_performanceMonitor == null)
                {
                    MessageBox.Show("성능 모니터가 초기화되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show("성능 기록을 초기화하시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    // 성능 기록 초기화 (현재는 수동으로 메트릭 초기화)
                    PerformanceInfoText.Text = "성능 기록이 초기화되었습니다.";
                    PerformanceStatsText.Text = "성능 기록이 초기화되었습니다.";
                    PerformanceTrendText.Text = "성능 기록이 초기화되었습니다.";
                    
                    MessageBox.Show("성능 기록이 초기화되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"성능 기록 초기화 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 벤치마크 실행 버튼 클릭 이벤트
        /// </summary>
        // private async void RunBenchmark_Click(object sender, RoutedEventArgs e)
        // {
        //     try
        //     {
        //         if (_benchmarkService == null)
        //         {
        //             MessageBox.Show("벤치마크 서비스가 초기화되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        //             return;
        //         }

        //         // 샘플 GDB 파일 경로 (실제 환경에서는 사용자가 선택)
        //         var sampleGdbPath = @"G:\SpatialCheckPro\sample.gdb";
                
        //         if (!File.Exists(sampleGdbPath.Replace(".gdb", "")))
        //         {
        //             MessageBox.Show($"샘플 GDB 파일을 찾을 수 없습니다: {sampleGdbPath}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        //             return;
        //         }

        //         BenchmarkStatusText.Text = "벤치마크 실행 중...";
        //         BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Orange;
        //         RunBenchmarkButton.IsEnabled = false;

        //         var result = await Task.Run(() => _benchmarkService.RunFullBenchmarkAsync(sampleGdbPath));

        //         _lastBenchmarkResult = result;

        //         if (string.IsNullOrEmpty(result.ErrorMessage))
        //         {
        //             BenchmarkStatusText.Text = $"벤치마크 완료 - 종합 점수: {result.OverallScore:F1}/100";
        //             BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    
        //             // 성능 정보 업데이트
        //             UpdatePerformanceInfoWithBenchmark(result);
        //         }
        //         else
        //         {
        //             BenchmarkStatusText.Text = $"벤치마크 실패: {result.ErrorMessage}";
        //             BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Red;
        //         }

        //         RunBenchmarkButton.IsEnabled = true;
        //     }
        //     catch (Exception ex)
        //     {
        //         BenchmarkStatusText.Text = $"벤치마크 실행 중 오류 발생: {ex.Message}";
        //         BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Red;
        //         RunBenchmarkButton.IsEnabled = true;
        //         MessageBox.Show($"벤치마크 실행 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        //     }
        // }

        /// <summary>
        /// 벤치마크 결과 내보내기 버튼 클릭 이벤트
        /// </summary>
        private async void ExportBenchmark_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastBenchmarkResult == null)
                {
                    MessageBox.Show("내보낼 벤치마크 결과가 없습니다. 먼저 벤치마크를 실행해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_benchmarkService == null)
                {
                    MessageBox.Show("벤치마크 서비스가 초기화되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "벤치마크 결과 저장",
                    Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"benchmark_result_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    BenchmarkStatusText.Text = "벤치마크 결과 내보내기 중...";
                    BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Orange;

                    await Task.Run(() => _benchmarkService.ExportBenchmarkResultAsync(_lastBenchmarkResult, saveFileDialog.FileName));

                    BenchmarkStatusText.Text = "벤치마크 결과 내보내기 완료";
                    BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    MessageBox.Show($"벤치마크 결과가 저장되었습니다:\n{saveFileDialog.FileName}", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                BenchmarkStatusText.Text = $"벤치마크 결과 내보내기 실패: {ex.Message}";
                BenchmarkStatusText.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show($"벤치마크 결과 내보내기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 벤치마크 결과로 성능 정보 업데이트
        /// </summary>
        private void UpdatePerformanceInfoWithBenchmark(BenchmarkResult result)
        {
            try
            {
                var performanceInfo = new System.Text.StringBuilder();
                performanceInfo.AppendLine($"벤치마크 실행 시간: {result.Duration.TotalSeconds:F1}초");
                performanceInfo.AppendLine($"종합 성능 점수: {result.OverallScore:F1}/100");

                if (result.SystemInfo != null)
                {
                    performanceInfo.AppendLine($"CPU 코어 수: {result.SystemInfo.ProcessorCount}");
                    performanceInfo.AppendLine($"사용 가능 메모리: {result.SystemInfo.AvailableMemoryGB:F1}GB");
                    performanceInfo.AppendLine($"권장 병렬도: {result.SystemInfo.RecommendedMaxParallelism}");
                }

                if (result.CpuBenchmark != null)
                {
                    performanceInfo.AppendLine($"최적 병렬도: {result.CpuBenchmark.OptimalParallelism}");
                    performanceInfo.AppendLine($"성능 향상 배수: {result.CpuBenchmark.SpeedupFactor:F1}x");
                }

                PerformanceInfoText.Text = performanceInfo.ToString();

                var statsInfo = new System.Text.StringBuilder();
                if (result.MemoryBenchmark != null)
                {
                    statsInfo.AppendLine($"메모리 증가량: {result.MemoryBenchmark.MemoryIncreaseMB}MB");
                    statsInfo.AppendLine($"평균 할당 시간: {result.MemoryBenchmark.AverageAllocationTime:F1}ms");
                    statsInfo.AppendLine($"GC 효율성: {result.MemoryBenchmark.GcEfficiency:F2}");
                }

                if (result.GdalBenchmark != null)
                {
                    statsInfo.AppendLine($"GDAL 성공률: {result.GdalBenchmark.SuccessRate:F1}%");
                    statsInfo.AppendLine($"평균 작업 시간: {result.GdalBenchmark.AverageOperationTime:F1}ms");
                }

                PerformanceStatsText.Text = statsInfo.ToString();

                var trendInfo = new System.Text.StringBuilder();
                trendInfo.AppendLine($"테스트 완료 시간: {result.EndTime:yyyy-MM-dd HH:mm:ss}");
                trendInfo.AppendLine($"테스트 대상: {result.GdbPath}");
                
                if (result.OverallScore >= 80)
                    trendInfo.AppendLine("성능 상태: 우수");
                else if (result.OverallScore >= 60)
                    trendInfo.AppendLine("성능 상태: 양호");
                else if (result.OverallScore >= 40)
                    trendInfo.AppendLine("성능 상태: 보통");
                else
                    trendInfo.AppendLine("성능 상태: 개선 필요");

                PerformanceTrendText.Text = trendInfo.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "벤치마크 결과로 성능 정보 업데이트 실패");
            }
        }
        */
    }

    /// <summary>
    /// 성능 설정 변경 이벤트 인수
    /// </summary>
    public class PerformanceSettingsChangedEventArgs : EventArgs
    {
        public bool EnableParallelProcessing { get; set; }
        public int MaxParallelism { get; set; }
        public int BatchSize { get; set; }
        public bool EnableRealTimeMonitoring { get; set; }
        public int MonitoringIntervalSeconds { get; set; }
    }
}

namespace SpatialCheckPro.GUI.Views
{
    public partial class ValidationSettingsView : UserControl
    {
        /// <summary>
        /// 수동 스트리밍 모드 UI 초기화
        /// </summary>
    private void InitializeManualStreamingModeUI()
    {
        // UI 컨트롤 값 설정
        ForceStreamingModeCheck.IsChecked = ForceStreamingMode;
        BatchSizeTextBox.Text = CustomBatchSize.ToString();
        MemoryUsageTextBox.Text = MaxMemoryUsageMB.ToString();
        EnablePrefetchingCheck.IsChecked = EnablePrefetching;
        EnableParallelStreamingCheck.IsChecked = EnableParallelStreaming;

        // 이벤트 핸들러 연결
        ForceStreamingModeCheck.Checked += (s, e) => ForceStreamingMode = true;
        ForceStreamingModeCheck.Unchecked += (s, e) => ForceStreamingMode = false;

        BatchSizeTextBox.TextChanged += BatchSizeTextBox_TextChanged;
        MemoryUsageTextBox.TextChanged += MemoryUsageTextBox_TextChanged;

        EnablePrefetchingCheck.Checked += (s, e) => EnablePrefetching = true;
        EnablePrefetchingCheck.Unchecked += (s, e) => EnablePrefetching = false;

        EnableParallelStreamingCheck.Checked += (s, e) => EnableParallelStreaming = true;
        EnableParallelStreamingCheck.Unchecked += (s, e) => EnableParallelStreaming = false;
    }

    /// <summary>
    /// 배치 크기 텍스트박스 변경 이벤트
    /// </summary>
    private void BatchSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(BatchSizeTextBox.Text, out int value))
        {
            CustomBatchSize = Math.Max(100, Math.Min(10000, value));
            BatchSizeTextBox.Text = CustomBatchSize.ToString();
        }
        else
        {
            BatchSizeTextBox.Text = CustomBatchSize.ToString();
        }
    }

    /// <summary>
    /// 메모리 사용량 텍스트박스 변경 이벤트
    /// </summary>
    private void MemoryUsageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(MemoryUsageTextBox.Text, out int value))
        {
            MaxMemoryUsageMB = Math.Max(128, Math.Min(4096, value));
            MemoryUsageTextBox.Text = MaxMemoryUsageMB.ToString();
        }
        else
        {
            MemoryUsageTextBox.Text = MaxMemoryUsageMB.ToString();
        }
    }

    /// <summary>
    /// 수동 스트리밍 모드 설정을 PerformanceSettings로 변환
    /// </summary>
    public PerformanceSettings ToPerformanceSettings()
    {
        return new PerformanceSettings
        {
            ForceStreamingMode = ForceStreamingMode,
            CustomBatchSize = CustomBatchSize,
            MaxMemoryUsageMB = MaxMemoryUsageMB,
            EnablePrefetching = EnablePrefetching,
            EnableParallelStreaming = EnableParallelStreaming,
            EnableDynamicParallelismAdjustment = true,
            MaxDegreeOfParallelism = MaxParallelism,
            ResourceMonitoringIntervalSeconds = MonitoringIntervalSeconds,
            EnableTableParallelProcessing = EnableParallelProcessing
        };
    }

    /// <summary>
    /// FileGDB 폴더가 유효한지 기본 검증
    /// </summary>
    /// <param name="gdbPath">FileGDB 폴더 경로</param>
    /// <returns>유효한 FileGDB 폴더인지 여부</returns>
    private bool IsValidFileGdbDirectory(string gdbPath)
    {
        try
        {
            if (!System.IO.Directory.Exists(gdbPath))
                return false;

            var hasGdbTable = System.IO.Directory.EnumerateFiles(gdbPath, "*.gdbtable", System.IO.SearchOption.TopDirectoryOnly).Any();
            if (!hasGdbTable)
                return false;

            var hasIndexOrSystemFile = System.IO.Directory.EnumerateFiles(gdbPath, "*.gdbtablx", System.IO.SearchOption.TopDirectoryOnly).Any()
                || System.IO.File.Exists(System.IO.Path.Combine(gdbPath, "gdb"));

            return hasIndexOrSystemFile || hasGdbTable;
        }
        catch
        {
            return false;
        }
    }
}
}

