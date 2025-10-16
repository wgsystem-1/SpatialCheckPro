namespace SpatialCheckPro.Models
{
    public class GeometryFieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string GeometryType { get; set; } = string.Empty;
        public string? SpatialReference { get; set; }
    }
}
