using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    public class FeatureClassInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TableId { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public string GeometryType { get; set; } = string.Empty;
        public string? SpatialReference { get; set; }
        public long FeatureCount { get; set; }
        public int FieldCount { get; set; }
        public List<string> FieldNames { get; set; } = new();
        public List<DetailedFieldInfo> Fields { get; set; } = new();
        public List<GeometryFieldInfo> GeometryFields { get; set; } = new();
        public List<DomainInfo> Domains { get; set; } = new();
        public bool IsValid { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}
