using System;
using System.Collections.Generic;
using OSGeo.OGR;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// QC 오류 정보 모델 - FGDB QC_ERRORS 스키마 기반
    /// </summary>
    public class QcError
    {
        /// <summary>
        /// 고유 식별자 (GUID)
        /// </summary>
        public string GlobalID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 오류 유형 (GEOM, REL, ATTR, SCHEMA)
        /// </summary>
        public string ErrType { get; set; } = string.Empty;

        /// <summary>
        /// 오류 코드 (예: RDC001, OVL002)
        /// </summary>
        public string ErrCode { get; set; } = string.Empty;

        // Severity/Status 필드는 문서 정책 변경에 따라 미사용
        public string Severity { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 규칙 ID (내부 규칙 식별자)
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 ID (SourceClass 대신 명확한 식별자)
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 명칭 (한글명)
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 원본 FeatureClass 이름
        /// </summary>
        public string SourceClass { get; set; } = string.Empty;

        /// <summary>
        /// 원본 OBJECTID
        /// </summary>
        public long SourceOID { get; set; }

        /// <summary>
        /// 원본 GlobalID (관계클래스 연결용)
        /// </summary>
        public string? SourceGlobalID { get; set; }

        /// <summary>
        /// 요약 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 추가 메타데이터 (JSON 형태)
        /// </summary>
        public string DetailsJSON { get; set; } = string.Empty;

        /// <summary>
        /// 검수 실행 ID
        /// </summary>
        public string RunID { get; set; } = string.Empty;
    
    /// <summary>
    /// RunId 속성 (RunID와 동일, 호환성용)
    /// </summary>
    public string RunId => RunID;

        /// <summary>
        /// 생성 시간 (UTC)
        /// </summary>
        public DateTime CreatedUTC { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 수정 시간 (UTC)
        /// </summary>
        public DateTime UpdatedUTC { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 담당자 (선택사항)
        /// </summary>
        public string? Assignee { get; set; }

        /// <summary>
        /// X 좌표 (오류 위치)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y 좌표 (오류 위치)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 지오메트리 WKT (실제 오류 지오메트리)
        /// </summary>
        public string? GeometryWKT { get; set; }

        /// <summary>
        /// 지오메트리 타입 (Point, LineString, Polygon)
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 객체 (기존 호환성을 위해 유지)
        /// </summary>
        public Geometry? Geometry { get; set; }

        /// <summary>
        /// 오류 값 (측정된 실제 값)
        /// </summary>
        public string ErrorValue { get; set; } = string.Empty;

        /// <summary>
        /// 기준값 (임계값)
        /// </summary>
        public string ThresholdValue { get; set; } = string.Empty;

        /// <summary>
        /// 검수 대상 파일명(FileGDB 폴더명)
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// 위치 정보 문자열 (X, Y 좌표)
        /// </summary>
        public string Location => $"{X:F2}, {Y:F2}";

        /// <summary>
        /// 사용자 친화적 오류 설명
        /// </summary>
        public string UserFriendlyDescription
        {
            get
            {
                return ErrCode switch
                {
                    "DUP001" => "동일한 지오메트리가 중복으로 존재합니다",
                    "OVL001" => "다른 객체와 겹치는 영역이 있습니다",
                    "SLF001" => "지오메트리가 자기 자신과 교차합니다",
                    "SLV001" => "매우 좁은 폴리곤입니다",
                    "SHT001" => "기준보다 짧은 선형 객체입니다",
                    "SML001" => "기준보다 작은 면적의 객체입니다",
                    "PIP001" => "다른 폴리곤 내부에 포함된 객체입니다",
                    "NUL001" => "NULL 지오메트리입니다",
                    "EMP001" => "빈 지오메트리입니다",
                    "INV001" => "무효한 지오메트리입니다",
                    _ => Message
                };
            }
        }

        /// <summary>
        /// 오류 심각도 레벨 (숫자)
        /// </summary>
        public int SeverityLevel => 0;

        /// <summary>
        /// 오류가 해결되었는지 여부
        /// </summary>
        public bool IsResolved => false;

        /// <summary>
        /// 오류가 활성 상태인지 여부
        /// </summary>
        public bool IsActive => true;

        /// <summary>
        /// 오류 유형의 표시명을 가져옵니다
        /// </summary>
        public string GetErrTypeDisplayName()
        {
            return ErrType switch
            {
                "GEOM" => "지오메트리",
                "SCHEMA" => "스키마",
                "REL" => "관계",
                "ATTR" => "속성",
                _ => ErrType
            };
        }

        /// <summary>
        /// 심각도의 표시명을 가져옵니다
        /// </summary>
        public string GetSeverityDisplayName() => string.Empty;

        /// <summary>
        /// 상태의 표시명을 가져옵니다
        /// </summary>
        public string GetStatusDisplayName() => string.Empty;

        /// <summary>
        /// 오류 유형에 따른 색상을 가져옵니다
        /// </summary>
        public string GetColorByType()
        {
            return ErrType switch
            {
                "GEOM" => "#FF6B6B",
                "SCHEMA" => "#4ECDC4",
                "REL" => "#45B7D1",
                "ATTR" => "#96CEB4",
                _ => "#95A5A6"
            };
        }

        /// <summary>
        /// 심각도에 따른 색상을 가져옵니다
        /// </summary>
        public string GetColorBySeverity()
        {
            return Severity switch
            {
                "CRIT" => "#E74C3C",
                "MAJOR" => "#F39C12",
                "MINOR" => "#F1C40F",
                "INFO" => "#3498DB",
                _ => "#95A5A6"
            };
        }

        /// <summary>
        /// 상태를 변경합니다
        /// </summary>
        public void ChangeStatus(string newStatus)
        {
            UpdatedUTC = DateTime.UtcNow;
        }

        /// <summary>
        /// 담당자를 변경합니다
        /// </summary>
        public void ChangeAssignee(string? newAssignee)
        {
            Assignee = newAssignee;
            UpdatedUTC = DateTime.UtcNow;
        }

        /// <summary>
        /// 좌표로부터 Point WKT 생성
        /// </summary>
        public static string CreatePointWKT(double x, double y)
        {
            return $"POINT ({x:F6} {y:F6})";
        }

        /// <summary>
        /// GeometryErrorDetail에서 QcError로 변환
        /// </summary>
        public static QcError FromGeometryErrorDetail(GeometryErrorDetail detail, string sourceClass, long sourceOid, string runId)
        {
            var qcError = new QcError
            {
                ErrType = "GEOM",
                ErrCode = GetErrorCode(detail.ErrorType),
                Severity = GetSeverity(detail.ErrorType),
                RuleId = $"GEOM_{detail.ErrorType.ToUpper().Replace(" ", "_")}",
                SourceClass = sourceClass,
                SourceOID = sourceOid,
                Message = detail.DetailMessage,
                RunID = runId,
                X = detail.X,
                Y = detail.Y,
                // 오류 발생 위치를 Point로 저장 (원본 지오메트리 대신)
                GeometryWKT = CreatePointWKT(detail.X, detail.Y),
                ErrorValue = detail.ErrorValue,
                ThresholdValue = detail.ThresholdValue,
                GeometryType = "Point"  // 오류 위치는 항상 Point
            };

            // DetailsJSON에 원본 지오메트리 정보 포함
            var detailsDict = new Dictionary<string, object>
            {
                ["ObjectId"] = detail.ObjectId,
                ["ErrorType"] = detail.ErrorType,
                ["Location"] = detail.Location,
                ["DetailMessage"] = detail.DetailMessage,
                ["OriginalGeometryWKT"] = detail.GeometryWkt ?? ""  // 원본 지오메트리는 상세정보에 저장
            };

            qcError.DetailsJSON = System.Text.Json.JsonSerializer.Serialize(detailsDict);

            return qcError;
        }

        /// <summary>
        /// 오류 유형에 따른 오류 코드 생성
        /// </summary>
        private static string GetErrorCode(string errorType)
        {
            return errorType.ToLower() switch
            {
                var type when type.Contains("중복") => "DUP001",
                var type when type.Contains("겹침") => "OVL001",
                var type when type.Contains("자체") || type.Contains("꼬임") => "SLF001",
                var type when type.Contains("슬리버") => "SLV001",
                var type when type.Contains("짧은") => "SHT001",
                var type when type.Contains("작은") || type.Contains("면적") => "SML001",
                var type when type.Contains("폴리곤") => "PIP001",
                var type when type.Contains("null") => "NUL001",
                var type when type.Contains("빈") || type.Contains("empty") => "EMP001",
                var type when type.Contains("무효") || type.Contains("invalid") => "INV001",
                _ => "GEN001"
            };
        }

        /// <summary>
        /// 오류 유형에 따른 심각도 결정
        /// </summary>
        private static string GetSeverity(string errorType)
        {
            return errorType.ToLower() switch
            {
                var type when type.Contains("중복") => "MAJOR",
                var type when type.Contains("겹침") => "MAJOR",
                var type when type.Contains("자체") || type.Contains("꼬임") => "MAJOR",
                var type when type.Contains("null") => "CRIT",
                var type when type.Contains("무효") => "CRIT",
                var type when type.Contains("슬리버") => "MINOR",
                var type when type.Contains("짧은") => "MINOR",
                var type when type.Contains("작은") => "MINOR",
                var type when type.Contains("폴리곤") => "MINOR",
                _ => "INFO"
            };
        }

        /// <summary>
        /// WKT에서 지오메트리 타입 결정
        /// </summary>
        public static string DetermineGeometryType(string? wkt)
        {
            if (string.IsNullOrEmpty(wkt))
                return "Unknown";

            var upperWkt = wkt.ToUpper();
            if (upperWkt.StartsWith("POINT"))
                return "Point";
            if (upperWkt.StartsWith("LINESTRING") || upperWkt.StartsWith("MULTILINESTRING"))
                return "LineString";
            if (upperWkt.StartsWith("POLYGON") || upperWkt.StartsWith("MULTIPOLYGON"))
                return "Polygon";

            return "Unknown";
        }
    }


}
