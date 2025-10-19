namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// QC 실행 상태 열거형
    /// </summary>
    public enum QcRunStatus
    {
        /// <summary>실행 중</summary>
        RUNNING,
        
        /// <summary>완료</summary>
        COMPLETED,
        
        /// <summary>실패</summary>
        FAILED,
        
        /// <summary>취소됨</summary>
        CANCELLED
    }
}
