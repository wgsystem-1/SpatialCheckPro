namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 캐시 통계 정보
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// 최대 용량
        /// </summary>
        public int MaxCapacity { get; set; }

        /// <summary>
        /// 현재 크기
        /// </summary>
        public int CurrentSize { get; set; }

        /// <summary>
        /// 캐시 히트 횟수
        /// </summary>
        public long HitCount { get; set; }

        /// <summary>
        /// 캐시 미스 횟수
        /// </summary>
        public long MissCount { get; set; }

        /// <summary>
        /// 전체 요청 횟수
        /// </summary>
        public long TotalRequests { get; set; }

        /// <summary>
        /// 캐시 히트율
        /// </summary>
        public double HitRatio { get; set; }

        /// <summary>
        /// 캐시 사용률
        /// </summary>
        public double UtilizationRatio { get; set; }
    }
}

