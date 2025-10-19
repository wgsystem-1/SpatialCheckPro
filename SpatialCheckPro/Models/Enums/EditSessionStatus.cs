namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 편집 세션 상태
    /// </summary>
    public enum EditSessionStatus
    {
        /// <summary>활성 상태</summary>
        Active,

        /// <summary>저장됨</summary>
        Saved,

        /// <summary>취소됨</summary>
        Cancelled,

        /// <summary>오류 발생</summary>
        Error
    }
}
