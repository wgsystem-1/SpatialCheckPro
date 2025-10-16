namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 테이블 검수 항목 결과
    /// </summary>
    public class TableValidationItem
    {
        /// <summary>
        /// 테이블 ID (실제 테이블명)
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 명칭 (한글명)
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 피처 개수 (테이블이 존재하지 않으면 null)
        /// </summary>
        public int? FeatureCount { get; set; }

        /// <summary>
        /// 피처 타입 (지오메트리 타입)
        /// </summary>
        public string FeatureType { get; set; } = string.Empty;

        /// <summary>
        /// 피처 타입 확인 (Y or N)
        /// </summary>
        public string FeatureTypeCheck { get; set; } = "N";

        /// <summary>
        /// 테이블 존재 확인 (Y or N)
        /// </summary>
        public string TableExistsCheck { get; set; } = "N";

        /// <summary>
        /// 예상 피처 타입
        /// </summary>
        public string ExpectedFeatureType { get; set; } = string.Empty;

        /// <summary>
        /// 실제 발견된 피처 타입
        /// </summary>
        public string ActualFeatureType { get; set; } = string.Empty;

        /// <summary>
        /// 실제 발견된 FeatureClass 이름
        /// </summary>
        public string ActualFeatureClassName { get; set; } = string.Empty;

        /// <summary>
        /// 화면 표시용 실제 FeatureClass 이름 (테이블이 없으면 "테이블 없음" 표시)
        /// </summary>
        public string DisplayActualFeatureClassName => 
            string.IsNullOrEmpty(ActualFeatureClassName) ? "테이블 없음" : ActualFeatureClassName;

        /// <summary>
        /// 좌표계
        /// </summary>
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 검수 상태
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 오류 메시지 목록
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 경고 메시지 목록
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// 검수를 수행했는지 여부 (객체가 0개면 스킵)
        /// </summary>
        public bool IsProcessed { get; set; } = true;
    }
}
