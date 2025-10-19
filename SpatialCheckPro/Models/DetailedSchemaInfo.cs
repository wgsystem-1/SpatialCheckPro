using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    public class DetailedSchemaInfo
    {
        public string FeatureClassName { get; set; } = string.Empty;
        public long FeatureCount { get; set; }
        public List<DetailedFieldInfo> Fields { get; set; } = new();
        public List<GeometryFieldInfo> GeometryFields { get; set; } = new();
        public Dictionary<string, DomainInfo> Domains { get; set; } = new();
    }
}
