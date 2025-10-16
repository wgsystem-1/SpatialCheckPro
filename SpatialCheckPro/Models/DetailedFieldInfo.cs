namespace SpatialCheckPro.Models
{
    public class DetailedFieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string SubType { get; set; } = string.Empty;
        public int Length { get; set; }
        public int Precision { get; set; }
        public bool IsNullable { get; set; }
        public string? DefaultValue { get; set; }
        public string? DomainName { get; set; }
        public bool IsFidField { get; set; }
    }
}
