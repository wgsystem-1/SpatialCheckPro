using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 속성 검수 규칙 설정
    /// </summary>
    public class AttributeCheckConfig
    {
        public string RuleId { get; set; } = string.Empty;
        public string Enabled { get; set; } = "Y";
        public string TableId { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string CheckType { get; set; } = string.Empty; // CodeList | Range | Regex | NotNull | Unique
        public string? Parameters { get; set; } // 예: 코드리스트: PRC001|PRC002, 범위: 0..3.0, 정규식: ^[A-Z]{3}$
        public string? Severity { get; set; } // INFO|MINOR|MAJOR|CRIT
        public string? Note { get; set; }
    }
}


