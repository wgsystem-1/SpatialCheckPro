using SpatialCheckPro.Models.Enums;
using CsvHelper.Configuration.Attributes;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 속성 검수 규칙 설정
    /// </summary>
    public class AttributeCheckConfig
    {
        [Name("RuleId")]
        public string RuleId { get; set; } = string.Empty;

        [Name("Enabled")]
        public string Enabled { get; set; } = "Y";

        [Name("TableId")]
        public string TableId { get; set; } = string.Empty;

        [Name("TableName")]
        public string TableName { get; set; } = string.Empty;

        [Name("FieldName")]
        public string FieldName { get; set; } = string.Empty;

        [Name("CheckType")]
        public string CheckType { get; set; } = string.Empty; // CodeList | Range | Regex | NotNull | Unique

        [Name("Parameters")]
        public string? Parameters { get; set; } // 예: 코드리스트: PRC001|PRC002, 범위: 0..3.0, 정규식: ^[A-Z]{3}$

        [Name("Severity")]
        public string? Severity { get; set; } // INFO|MINOR|MAJOR|CRIT

        [Name("Note")]
        public string? Note { get; set; }
    }
}


