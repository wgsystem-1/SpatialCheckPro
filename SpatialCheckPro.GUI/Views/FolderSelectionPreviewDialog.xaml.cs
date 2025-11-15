using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 폴더 선택 시 검수 대상 미리보기 다이얼로그
    /// </summary>
    public partial class FolderSelectionPreviewDialog : Window
    {
        private readonly ILogger<FolderSelectionPreviewDialog>? _logger;
        private readonly QcErrorsPathManager _pathManager;
        private string _selectedPath = string.Empty;
        private List<QcErrorsPathManager.FileGdbInfo> _targetGdbs = new();

        public bool IsContinue { get; private set; }
        public List<string> SelectedGdbPaths => _targetGdbs.Select(g => g.FullPath).ToList();

        public FolderSelectionPreviewDialog(string selectedPath)
        {
            InitializeComponent();
            
            // 서비스 가져오기
            var app = Application.Current as App;
            _logger = app?.GetService<ILogger<FolderSelectionPreviewDialog>>();
            
            // QcErrorsPathManager는 직접 생성 (또는 DI에서 가져오기)
            var loggerFactory = app?.GetService<ILoggerFactory>();
            var pathManagerLogger = loggerFactory?.CreateLogger<QcErrorsPathManager>();
            _pathManager = new QcErrorsPathManager(pathManagerLogger ?? 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<QcErrorsPathManager>.Instance);
            
            _selectedPath = selectedPath;
            LoadPreview();
        }

        /// <summary>
        /// 미리보기 정보를 로드합니다
        /// </summary>
        private void LoadPreview()
        {
            try
            {
                // 선택한 경로 표시
                SelectedPathText.Text = $"선택한 폴더: {_selectedPath}";
                
                // QC_errors 경로 표시
                var qcErrorsPath = _pathManager.GetQcErrorsDirectory(_selectedPath);
                QcErrorsPathText.Text = qcErrorsPath;
                
                // 단일 FileGDB 선택인지 확인
                if (_pathManager.IsFileGdb(_selectedPath))
                {
                    // 단일 FileGDB를 직접 선택한 경우
                    _targetGdbs = new List<QcErrorsPathManager.FileGdbInfo>
                    {
                        new QcErrorsPathManager.FileGdbInfo
                        {
                            FullPath = _selectedPath,
                            Name = Path.GetFileName(_selectedPath),
                            RelativePath = Path.GetFileName(_selectedPath),
                            SizeInBytes = _pathManager.CalculateDirectorySize(_selectedPath)
                        }
                    };
                }
                else
                {
                    // 폴더를 선택한 경우 - 하위 FileGDB 검색
                    _targetGdbs = _pathManager.FindValidationTargets(_selectedPath);
                }
                
                TargetGdbGrid.ItemsSource = _targetGdbs;
                TargetCountText.Text = $"({_targetGdbs.Count}개)";
                
                // 제외된 항목 찾기 (폴더 선택시에만)
                if (!_pathManager.IsFileGdb(_selectedPath))
                {
                    var excludedItems = _pathManager.FindExcludedItems(_selectedPath);
                    if (excludedItems.Any())
                    {
                        ExcludedSection.Visibility = Visibility.Visible;
                        ExcludedGdbGrid.ItemsSource = excludedItems;
                        ExcludedCountText.Text = $"({excludedItems.Count}개)";
                    }
                }
                
                // 검수 대상이 있으면 계속 버튼 활성화
                if (_targetGdbs.Any())
                {
                    ContinueButton.IsEnabled = true;
                    
                    // 총 크기 계산
                    var totalSize = _targetGdbs.Sum(g => g.SizeInBytes);
                    var totalSizeText = FormatFileSize(totalSize);
                    
                    _logger?.LogInformation("검수 대상 FileGDB {Count}개 발견, 총 크기: {Size}", 
                        _targetGdbs.Count, totalSizeText);
                }
                else
                {
                    // 검수 대상이 없는 경우 경고
                    WarningText.Text = "검수 대상 FileGDB가 없습니다.";
                    WarningText.Visibility = Visibility.Visible;
                    
                    _logger?.LogWarning("검수 대상 FileGDB를 찾을 수 없습니다: {Path}", _selectedPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "미리보기 로드 중 오류");
                MessageBox.Show(
                    $"미리보기 로드 중 오류가 발생했습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// 파일 크기를 포맷팅합니다
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// 계속 버튼 클릭
        /// </summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            IsContinue = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 취소 버튼 클릭
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsContinue = false;
            DialogResult = false;
            Close();
        }
    }
}
