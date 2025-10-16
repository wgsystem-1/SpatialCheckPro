namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 오류 카테고리
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary>
        /// 데이터 무결성 오류
        /// </summary>
        DataIntegrity,

        /// <summary>
        /// 성능 관련 문제
        /// </summary>
        Performance,

        /// <summary>
        /// 설정 오류
        /// </summary>
        Configuration,

        /// <summary>
        /// 시스템 리소스 문제
        /// </summary>
        SystemResource,

        /// <summary>
        /// 데이터 접근 오류
        /// </summary>
        DataAccess
    }
}
