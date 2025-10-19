using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 공간 관계 규칙을 나타내는 클래스
    /// </summary>
    public class SpatialRelationRule
    {
        /// <summary>
        /// 규칙 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 규칙명
        /// </summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>
        /// 원본 레이어명
        /// </summary>
        public string SourceLayer { get; set; } = string.Empty;

        /// <summary>
        /// 대상 레이어명
        /// </summary>
        public string TargetLayer { get; set; } = string.Empty;

        /// <summary>
        /// 공간 관계 타입
        /// </summary>
        public SpatialRelationType RelationType { get; set; }

        /// <summary>
        /// 필수 관계 여부
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// 허용 오차
        /// </summary>
        public double Tolerance { get; set; }

        /// <summary>
        /// 위반 시 심각도
        /// </summary>
        public ErrorSeverity ViolationSeverity { get; set; }

        /// <summary>
        /// 규칙 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
