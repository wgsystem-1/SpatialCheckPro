using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 관계 검수 진행률 이벤트 인자
    /// </summary>
    public class RelationValidationProgressEventArgs : EventArgs
    {
        /// <summary>
        /// 현재 단계
        /// </summary>
        public RelationValidationStage CurrentStage { get; set; }

        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 전체 진행률 (0-100)
        /// </summary>
        public int OverallProgress { get; set; }

        /// <summary>
        /// 단계별 진행률 (0-100)
        /// </summary>
        public int StageProgress { get; set; }

        /// <summary>
        /// 상태 메시지
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 현재 처리 중인 규칙명
        /// </summary>
        public string? CurrentRule { get; set; }

        /// <summary>
        /// 처리된 규칙 수
        /// </summary>
        public int ProcessedRules { get; set; }

        /// <summary>
        /// 전체 규칙 수
        /// </summary>
        public int TotalRules { get; set; }

        /// <summary>
        /// 단계 완료 여부
        /// </summary>
        public bool IsStageCompleted { get; set; }

        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsStageSuccessful { get; set; }

        /// <summary>
        /// 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 개수
        /// </summary>
        public int WarningCount { get; set; }
    }
}
