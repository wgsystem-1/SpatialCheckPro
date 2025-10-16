using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 위상 규칙을 나타내는 클래스
    /// </summary>
    public class TopologyRule
    {
        /// <summary>
        /// 규칙 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 위상 규칙 타입
        /// </summary>
        public TopologyRuleType RuleType { get; set; }

        /// <summary>
        /// 원본 레이어명
        /// </summary>
        public string SourceLayer { get; set; } = string.Empty;

        /// <summary>
        /// 대상 레이어명
        /// </summary>
        public string TargetLayer { get; set; } = string.Empty;

        /// <summary>
        /// 허용 오차
        /// </summary>
        public double Tolerance { get; set; }

        /// <summary>
        /// 예외 허용 여부
        /// </summary>
        public bool AllowExceptions { get; set; }

        /// <summary>
        /// 예외 조건 목록
        /// </summary>
        public List<string> ExceptionConditions { get; set; } = new List<string>();
    }
}
