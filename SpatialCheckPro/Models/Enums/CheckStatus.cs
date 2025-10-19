namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 검수 항목 상태
    /// </summary>
    public enum CheckStatus
    {
        /// <summary>시작되지 않음</summary>
        NotStarted = 0,
        
        /// <summary>실행 중</summary>
        Running = 1,
        
        /// <summary>통과</summary>
        Passed = 2,
        
        /// <summary>실패</summary>
        Failed = 3,
        
        /// <summary>경고</summary>
        Warning = 4,
        
        /// <summary>건너뜀</summary>
        Skipped = 5
    }
}
