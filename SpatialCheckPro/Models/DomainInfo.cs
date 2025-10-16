namespace SpatialCheckPro.Models
{
    public class DomainInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DomainType { get; set; } = string.Empty; // CODED, RANGE, GLOB
        public string FieldType { get; set; } = string.Empty;
        public string FieldSubType { get; set; } = string.Empty;
    }
}
