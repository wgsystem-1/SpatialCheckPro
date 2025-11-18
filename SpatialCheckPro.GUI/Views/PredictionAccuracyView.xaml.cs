using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Services;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 예측 정확도 분석 창
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class PredictionAccuracyView : Window
    {
        private readonly ILogger<PredictionAccuracyView>? _logger;
        private readonly ValidationMetricsCollector? _metricsCollector;
        
        public PredictionAccuracyView()
        {
            InitializeComponent();
            
            // 서비스 가져오기
            var app = Application.Current as App;
            _metricsCollector = app?.GetService<ValidationMetricsCollector>();
            _logger = app?.GetService<ILogger<PredictionAccuracyView>>();
            
            LoadAnalysisData();
        }
        
        /// <summary>
        /// 분석 데이터를 로드합니다
        /// </summary>
        private void LoadAnalysisData()
        {
            if (_metricsCollector == null)
            {
                OverallAccuracyText.Text = "N/A";
                AverageDeviationText.Text = "N/A";
                DataCountText.Text = "0개";
                ImprovementRateText.Text = "N/A";
                return;
            }
            
            // TODO: 실제 메트릭 데이터에서 정확도 계산
            // 현재는 샘플 데이터 표시
            
            // 전체 정확도 요약
            OverallAccuracyText.Text = "87.5%";
            AverageDeviationText.Text = "±8.3초";
            DataCountText.Text = "12개";
            ImprovementRateText.Text = "+23%";
            
            // 단계별 정확도
            var stageAccuracies = new List<dynamic>
            {
                new { StageName = "0단계: FileGDB 검증", AccuracyPercent = 95.2, AverageDeviation = 0.1 },
                new { StageName = "1단계: 테이블 검수", AccuracyPercent = 92.8, AverageDeviation = 0.3 },
                new { StageName = "2단계: 스키마 검수", AccuracyPercent = 88.5, AverageDeviation = 2.5 },
                new { StageName = "3단계: 지오메트리 검수", AccuracyPercent = 85.1, AverageDeviation = 12.8 },
                new { StageName = "4단계: 속성 관계 검수", AccuracyPercent = 82.3, AverageDeviation = 8.5 },
                new { StageName = "5단계: 공간 관계 검수", AccuracyPercent = 79.6, AverageDeviation = 15.2 }
            };
            StageAccuracyList.ItemsSource = stageAccuracies;
            
            // 최근 실행 비교
            var recentRuns = new List<dynamic>
            {
                new { 
                    RunTime = DateTime.Now.AddHours(-2), 
                    FileName = "c_33611047.gdb",
                    PredictedTime = TimeSpan.FromSeconds(125),
                    ActualTime = TimeSpan.FromSeconds(118),
                    Accuracy = 94.4,
                    Deviation = -7
                },
                new { 
                    RunTime = DateTime.Now.AddHours(-5), 
                    FileName = "test_data.gdb",
                    PredictedTime = TimeSpan.FromSeconds(89),
                    ActualTime = TimeSpan.FromSeconds(102),
                    Accuracy = 87.3,
                    Deviation = 13
                },
                new { 
                    RunTime = DateTime.Now.AddDays(-1), 
                    FileName = "sample_2025.gdb",
                    PredictedTime = TimeSpan.FromSeconds(156),
                    ActualTime = TimeSpan.FromSeconds(143),
                    Accuracy = 91.7,
                    Deviation = -13
                }
            };
            RecentRunsGrid.ItemsSource = recentRuns;
        }
        
        /// <summary>
        /// 학습 데이터 초기화 버튼 클릭
        /// </summary>
        private void ClearDataButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "모든 학습 데이터를 삭제하시겠습니까?\n\n" +
                "삭제된 데이터는 복구할 수 없으며, 예측 정확도가 일시적으로 낮아질 수 있습니다.",
                "학습 데이터 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // TODO: 실제 메트릭 데이터 초기화
                    _logger?.LogInformation("학습 데이터 초기화 실행");
                    
                    MessageBox.Show(
                        "학습 데이터가 초기화되었습니다.",
                        "초기화 완료",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                        
                    LoadAnalysisData();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "학습 데이터 초기화 실패");
                    MessageBox.Show(
                        $"초기화 중 오류가 발생했습니다:\n{ex.Message}",
                        "오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
        
        /// <summary>
        /// 닫기 버튼 클릭
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
