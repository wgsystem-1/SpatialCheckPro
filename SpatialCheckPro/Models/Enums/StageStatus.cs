namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 검수 단계 상태
    /// </summary>
    public enum StageStatus
    {
        /// <summary>시작되지 않음</summary>
        NotStarted = 0,
        
        /// <summary>대기 중</summary>
        Pending = 1,
        
        /// <summary>실행 중</summary>
        Running = 2,
        
        /// <summary>완료</summary>
        Completed = 3,
        
        /// <summary>경고와 함께 완료</summary>
        CompletedWithWarnings = 4,
        
        /// <summary>실패</summary>
        Failed = 5,
        
        /// <summary>건너뜀</summary>
        Skipped = 6,

        /// <summary>차단됨(전제조건 미충족)</summary>
        Blocked = 7
    }
}
