namespace SpatialCheckPro.Models
{
    /// <summary>
    /// Feature에서 추출한 데이터를 저장하는 DTO (Data Transfer Object)
    /// GDAL Feature 객체의 네이티브 메모리 누수 위험을 방지하기 위해 사용
    /// </summary>
    /// <remarks>
    /// Phase 1.1: Feature/Geometry Dispose 패턴 강화
    /// - Feature를 직접 반환하는 대신 필요한 데이터만 추출하여 DTO로 반환
    /// - 네이티브 리소스는 추출 시점에 이미 해제되어 메모리 누수 방지
    /// - 예상 효과: 500MB-1GB 메모리 절약, OOM 발생 가능성 대폭 감소
    /// </remarks>
    public class FeatureData
    {
        /// <summary>
        /// Feature ID (FID)
        /// </summary>
        public long Fid { get; set; }

        /// <summary>
        /// ObjectId 필드 값 (있는 경우)
        /// </summary>
        public long? ObjectId { get; set; }

        /// <summary>
        /// Geometry를 WKT (Well-Known Text) 형식으로 변환한 문자열
        /// </summary>
        public string? GeometryWkt { get; set; }

        /// <summary>
        /// Geometry 타입 (POINT, LINESTRING, POLYGON 등)
        /// </summary>
        public string? GeometryType { get; set; }

        /// <summary>
        /// Geometry가 유효한지 여부 (GEOS IsValid)
        /// </summary>
        public bool IsGeometryValid { get; set; }

        /// <summary>
        /// Geometry가 Simple한지 여부 (GEOS IsSimple)
        /// </summary>
        public bool IsGeometrySimple { get; set; }

        /// <summary>
        /// Geometry의 면적 (POLYGON인 경우)
        /// </summary>
        public double? Area { get; set; }

        /// <summary>
        /// Geometry의 길이 (LINESTRING인 경우) 또는 둘레 (POLYGON인 경우)
        /// </summary>
        public double? Length { get; set; }

        /// <summary>
        /// Geometry의 중심점 X 좌표
        /// </summary>
        public double? CenterX { get; set; }

        /// <summary>
        /// Geometry의 중심점 Y 좌표
        /// </summary>
        public double? CenterY { get; set; }

        /// <summary>
        /// Geometry의 Envelope (BBOX)
        /// </summary>
        public SpatialEnvelope? Envelope { get; set; }

        /// <summary>
        /// Feature의 속성 필드 값들 (필드명 → 값)
        /// </summary>
        public Dictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// 원본 테이블 이름
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// 정점(Vertex) 개수
        /// </summary>
        public int? PointCount { get; set; }

        /// <summary>
        /// 빈 FeatureData 생성
        /// </summary>
        public FeatureData()
        {
        }

        /// <summary>
        /// FID와 GeometryWkt로 FeatureData 생성
        /// </summary>
        public FeatureData(long fid, string? geometryWkt)
        {
            Fid = fid;
            GeometryWkt = geometryWkt;
        }

        /// <summary>
        /// 특정 속성 값 가져오기
        /// </summary>
        public T? GetAttribute<T>(string fieldName)
        {
            if (Attributes.TryGetValue(fieldName, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// 특정 속성 값 설정
        /// </summary>
        public void SetAttribute(string fieldName, object? value)
        {
            Attributes[fieldName] = value;
        }

        /// <summary>
        /// 디버깅용 문자열 표현
        /// </summary>
        public override string ToString()
        {
            return $"FeatureData [FID={Fid}, OID={ObjectId}, Type={GeometryType}, Attrs={Attributes.Count}]";
        }
    }
}
