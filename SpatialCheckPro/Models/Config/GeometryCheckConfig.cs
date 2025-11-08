using CsvHelper.Configuration.Attributes;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 지오메트리 검수 설정 정보
    /// </summary>
    public class GeometryCheckConfig
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        [Name("TableId")]
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 명칭
        /// </summary>
        [Name("TableName")]
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        [Name("GeometryType")]
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 객체 중복 검사 여부
        /// </summary>
        [Name("객체중복")]
        public string CheckDuplicate { get; set; } = string.Empty;

        /// <summary>
        /// 객체간 겹침 검사 여부
        /// </summary>
        [Name("객체간겹침")]
        public string CheckOverlap { get; set; } = string.Empty;

        /// <summary>
        /// 자체 꼬임 검사 여부
        /// </summary>
        [Name("자체꼬임")]
        public string CheckSelfIntersection { get; set; } = string.Empty;

        /// <summary>
        /// 슬리버 검사 여부
        /// </summary>
        [Name("슬리버")]
        public string CheckSliver { get; set; } = string.Empty;

        /// <summary>
        /// 짧은 객체 검사 여부
        /// </summary>
        [Name("짧은객체")]
        public string CheckShortObject { get; set; } = string.Empty;

        /// <summary>
        /// 작은 면적 객체 검사 여부
        /// </summary>
        [Name("작은면적객체")]
        public string CheckSmallArea { get; set; } = string.Empty;

        /// <summary>
        /// 홀 폴리곤 오류 검사 여부 (폴리곤 내 폴리곤 존재)
        /// </summary>
        [Name("홀 폴리곤 오류", "폴리곤내폴리곤존재")]
        public string CheckPolygonInPolygon { get; set; } = string.Empty;

        // 편의 속성들 (Y/N을 bool로 변환)
        public bool ShouldCheckDuplicate => CheckDuplicate.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckOverlap => CheckOverlap.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckSelfIntersection => CheckSelfIntersection.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckSliver => CheckSliver.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckShortObject => CheckShortObject.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckSmallArea => CheckSmallArea.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckPolygonInPolygon => CheckPolygonInPolygon.Equals("Y", StringComparison.OrdinalIgnoreCase);

        // 신규 항목들(Y/N) - CSV는 'Y'/'N' 값을 가지므로 문자열로 매핑 후 bool 편의 속성 제공
        [Name("최소정점개수")]
        public string CheckMinPoints { get; set; } = string.Empty;

        [Name("스파이크")]
        public string CheckSpikes { get; set; } = string.Empty;

        [Name("자기중첩")]
        public string CheckSelfOverlap { get; set; } = string.Empty;

        [Name("언더슛")]
        public string CheckUndershoot { get; set; } = string.Empty;

        [Name("오버슛")]
        public string CheckOvershoot { get; set; } = string.Empty;

        // 편의 bool 속성 (Y/N → bool)
        public bool ShouldCheckMinPoints => CheckMinPoints.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckSpikes => CheckSpikes.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckSelfOverlap => CheckSelfOverlap.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckUndershoot => CheckUndershoot.Equals("Y", StringComparison.OrdinalIgnoreCase);
        public bool ShouldCheckOvershoot => CheckOvershoot.Equals("Y", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 설정 유효성 검증
        /// </summary>
        public bool Validate()
        {
            return !string.IsNullOrEmpty(TableId) && !string.IsNullOrEmpty(TableName);
        }
    }
}
