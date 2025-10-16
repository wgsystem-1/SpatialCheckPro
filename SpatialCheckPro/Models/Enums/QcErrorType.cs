namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// QC 오류 유형 열거형
    /// </summary>
    public enum QcErrorType
    {
        /// <summary>지오메트리 오류</summary>
        GEOM,
        
        /// <summary>관계 오류</summary>
        REL,
        
        /// <summary>속성 오류</summary>
        ATTR,
        
        /// <summary>스키마 오류</summary>
        SCHEMA
    }
}
