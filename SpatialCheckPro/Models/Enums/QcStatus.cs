namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// QC 오류 상태 열거형
    /// </summary>
    public enum QcStatus
    {
        /// <summary>열림</summary>
        OPEN,
        
        /// <summary>수정됨</summary>
        FIXED,
        
        /// <summary>무시됨</summary>
        IGNORED,
        
        /// <summary>오탐</summary>
        FALSE_POS
    }
}
