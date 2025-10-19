namespace SpatialCheckPro.Constants
{
    /// <summary>
    /// 날짜/시간 포맷 상수 정의
    /// </summary>
    public static class DateTimeFormats
    {
        /// <summary>
        /// 파일명 타임스탬프 포맷: yyyyMMdd_HHmmss
        /// 예시: 20250105_143022
        /// </summary>
        public const string FileTimestamp = "yyyyMMdd_HHmmss";

        /// <summary>
        /// 화면 표시용 날짜/시간 포맷: yyyy-MM-dd HH:mm:ss
        /// 예시: 2025-01-05 14:30:22
        /// </summary>
        public const string Display = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 로그 타임스탬프 포맷 (밀리초 포함): yyyy-MM-dd HH:mm:ss.fff
        /// 예시: 2025-01-05 14:30:22.123
        /// </summary>
        public const string LogTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>
        /// 간단한 날짜 포맷: yyyy-MM-dd
        /// 예시: 2025-01-05
        /// </summary>
        public const string DateOnly = "yyyy-MM-dd";

        /// <summary>
        /// 간단한 시간 포맷: HH:mm:ss
        /// 예시: 14:30:22
        /// </summary>
        public const string TimeOnly = "HH:mm:ss";

        /// <summary>
        /// 보고서용 날짜/시간 포맷: yyyy년 MM월 dd일 HH시 mm분
        /// 예시: 2025년 01월 05일 14시 30분
        /// </summary>
        public const string ReportDateTime = "yyyy년 MM월 dd일 HH시 mm분";
    }
}
