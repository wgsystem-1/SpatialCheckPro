namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 검수 상태
    /// </summary>
    public enum ValidationStatus
    {
        /// <summary>
        /// 시작되지 않음
        /// </summary>
        NotStarted,

        /// <summary>
        /// 대기 중
        /// </summary>
        Pending,

        /// <summary>
        /// 실행 중
        /// </summary>
        Running,

        /// <summary>
        /// 완료
        /// </summary>
        Completed,

        /// <summary>
        /// 실패
        /// </summary>
        Failed,

        /// <summary>
        /// 취소됨
        /// </summary>
        Cancelled
    }
}
