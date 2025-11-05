using System.ComponentModel.DataAnnotations;
using CsvHelper.Configuration.Attributes;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 1단계 테이블 검수 설정 모델 클래스
    /// </summary>
    public class TableCheckConfig
    {
        /// <summary>
        /// 테이블 ID (고유 식별자)
        /// </summary>
        [Name("TableId")]
        [Required(ErrorMessage = "테이블ID는 필수 입력값입니다.")]
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 명칭 (한글명)
        /// </summary>
        [Name("TableName")]
        [Required(ErrorMessage = "테이블명칭은 필수 입력값입니다.")]
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입 (POINT, LINESTRING, POLYGON)
        /// </summary>
        [Name("GeometryType")]
        [Required(ErrorMessage = "지오메트리타입은 필수 입력값입니다.")]
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 좌표계 정보 (EPSG 코드)
        /// </summary>
        [Name("CRS")]
        [Required(ErrorMessage = "좌표계는 필수 입력값입니다.")]
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입이 유효한지 검증
        /// </summary>
        /// <returns>유효성 검증 결과</returns>
        public bool IsValidGeometryType()
        {
            var validTypes = new[]
            {
                "POINT", "LINESTRING", "POLYGON",
                "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON",
                "Point", "LineString", "Polygon",
                "MultiPoint", "MultiLineString", "MultiPolygon"
            };

            return validTypes.Contains(GeometryType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 좌표계 정보가 EPSG 코드인지 확인
        /// </summary>
        /// <returns>EPSG 코드 여부</returns>
        public bool IsEpsgCode()
        {
            return CoordinateSystem.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// EPSG 코드 추출
        /// </summary>
        /// <returns>EPSG 코드 번호 (추출 실패 시 null)</returns>
        public int? GetEpsgCode()
        {
            if (!IsEpsgCode()) return null;

            var codeStr = CoordinateSystem.Substring(5);
            return int.TryParse(codeStr, out var code) ? code : null;
        }

        /// <summary>
        /// 설정 유효성 검증
        /// </summary>
        /// <returns>검증 결과 목록</returns>
        public List<System.ComponentModel.DataAnnotations.ValidationResult> Validate()
        {
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var context = new ValidationContext(this);

            Validator.TryValidateObject(this, context, results, true);

            // 추가 비즈니스 로직 검증
            if (!IsValidGeometryType())
            {
                results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"지원하지 않는 지오메트리 타입입니다: {GeometryType}",
                    new[] { nameof(GeometryType) }));
            }

            // 테이블ID 필수 입력 검증
            if (string.IsNullOrWhiteSpace(TableId))
            {
                results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                    "테이블ID는 필수 입력값입니다",
                    new[] { nameof(TableId) }));
            }

            return results;
        }

        /// <summary>
        /// 문자열 표현
        /// </summary>
        /// <returns>테이블 정보 문자열</returns>
        public override string ToString()
        {
            return $"{TableId}: {TableName} ({GeometryType}, {CoordinateSystem})";
        }
    }
}
