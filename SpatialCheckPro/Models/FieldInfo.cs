namespace SpatialCheckPro.Models
{
    public class FieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int Length { get; set; }
        public bool IsNotNull { get; set; }
    }
}
