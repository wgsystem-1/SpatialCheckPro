namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 공간 질의 결과를 나타내는 클래스
    /// </summary>
    public class SpatialQueryResult
    {
        /// <summary>
        /// 원본 피처 ID
        /// </summary>
        public long SourceId { get; set; }

        /// <summary>
        /// 대상 피처 ID
        /// </summary>
        public long TargetId { get; set; }

        /// <summary>
        /// 공간 관계 거리 (해당하는 경우)
        /// </summary>
        public double Distance { get; set; }

        /// <summary>
        /// 교차 면적 (해당하는 경우)
        /// </summary>
        public double IntersectionArea { get; set; }

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}
