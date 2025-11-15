using System;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 검수 진행률 이벤트 인자
    /// </summary>
    public class ValidationProgressEventArgs : EventArgs
    {
        /// <summary>
        /// 현재 단계 (1-4)
        /// </summary>
        public int CurrentStage { get; set; }

        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 전체 진행률 (0-100)
        /// </summary>
        public double OverallProgress { get; set; }

        /// <summary>
        /// 현재 단계 진행률 (0-100)
        /// </summary>
        public double StageProgress { get; set; }

        /// <summary>
        /// 상태 메시지
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 단계 완료 여부
        /// </summary>
        public bool IsStageCompleted { get; set; }

        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsStageSuccessful { get; set; }

        /// <summary>
        /// 단계 스킵 여부
        /// </summary>
        public bool IsStageSkipped { get; set; }

        /// <summary>
        /// 현재 단계에서 처리한 작업 단위 수 (예: 규칙 수, 컬럼 수, 피처 수). 알 수 없으면 -1
        /// </summary>
        public long ProcessedUnits { get; set; } = -1;

        /// <summary>
        /// 현재 단계의 전체 작업 단위 수. 알 수 없으면 -1
        /// </summary>
        public long TotalUnits { get; set; } = -1;

        /// <summary>
        /// 현재 단계에서 발견된 오류 수
        /// </summary>
        public int ErrorCount { get; set; } = 0;

        /// <summary>
        /// 현재 단계에서 발견된 경고 수
        /// </summary>
        public int WarningCount { get; set; } = 0;

        /// <summary>
        /// 부분 검수 결과 (단계 완료 시 제공)
        /// </summary>
        public SpatialCheckPro.Models.ValidationResult? PartialResult { get; set; }
    }
}