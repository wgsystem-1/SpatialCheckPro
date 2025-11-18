using System;
using System.Windows.Controls;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.ViewModels;
using SpatialCheckPro.Models.Enums;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 단계 타임라인 샌드박스 뷰
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class StageTimelineSandboxView : UserControl
    {
        public StageTimelineSandboxView()
        {
            InitializeComponent();
            var app = System.Windows.Application.Current as App;
            if (app != null)
            {
                DataContext = app.GetService<StageSummaryCollectionViewModel>();
            }

            if (DataContext is StageSummaryCollectionViewModel vm)
            {
                SeedSampleData(vm);
            }
        }

        private static void SeedSampleData(StageSummaryCollectionViewModel vm)
        {
            vm.Reset();

            var random = new Random();
            foreach (var definition in StageDefinitions.All)
            {
                vm.ForceStageStatus(definition.StageNumber, StageStatus.NotStarted, "샌드박스 초기화");
            }

            // 샘플 진행 상황 설정
            vm.ApplyProgress(new Services.ValidationProgressEventArgs
            {
                CurrentStage = 1,
                StageName = StageDefinitions.GetStageName(1),
                StageProgress = 100,
                OverallProgress = 20,
                StatusMessage = "테이블 검수 완료",
                IsStageCompleted = true,
                IsStageSuccessful = true
            });

            vm.ApplyProgress(new Services.ValidationProgressEventArgs
            {
                CurrentStage = 2,
                StageName = StageDefinitions.GetStageName(2),
                StageProgress = 45,
                OverallProgress = 40,
                StatusMessage = "스키마 규칙 45% 진행",
                IsStageCompleted = false,
                IsStageSuccessful = true,
                ProcessedUnits = 45,
                TotalUnits = 100
            });

            vm.RegisterAlert(2, ErrorSeverity.Warning, "필드 길이 불일치 감지", "테이블 B의 FieldX 길이가 설정과 다릅니다.", StageStatus.Running);

            vm.ApplyProgress(new Services.ValidationProgressEventArgs
            {
                CurrentStage = 3,
                StageName = StageDefinitions.GetStageName(3),
                StageProgress = 10,
                OverallProgress = 45,
                StatusMessage = "지오메트리 첫 배치 검수 중",
                IsStageCompleted = false,
                IsStageSuccessful = true,
                ProcessedUnits = 12,
                TotalUnits = 200
            });

            vm.RegisterAlert(3, ErrorSeverity.Error, "자기 교차 폴리곤 발견", "도로중심선 레이어에서 자기 교차가 발생했습니다.", StageStatus.Running);

            vm.ApplyProgress(new Services.ValidationProgressEventArgs
            {
                CurrentStage = 4,
                StageName = StageDefinitions.GetStageName(4),
                StageProgress = 0,
                OverallProgress = 45,
                StatusMessage = "속성 관계 검수 대기 중",
                IsStageCompleted = false,
                IsStageSuccessful = true
            });

            vm.ApplyProgress(new Services.ValidationProgressEventArgs
            {
                CurrentStage = 0,
                StageName = StageDefinitions.GetStageName(0),
                StageProgress = 100,
                OverallProgress = 20,
                StatusMessage = "사전 검수 완료",
                IsStageCompleted = true,
                IsStageSuccessful = true
            });
        }
    }
}


