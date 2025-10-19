using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 검수 오류 정보
    /// </summary>
    public class ValidationError
    {
        /// <summary>
        /// 오류 ID
        /// </summary>
        public string ErrorId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 오류 코드
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 오류 발생 시간
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 오류 발생 시간 (별칭)
        /// </summary>
        public DateTime OccurredAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 관련 테이블 ID
        /// </summary>
        public string? TableId { get; set; }

        /// <summary>
        /// 관련 피처 ID
        /// </summary>
        public string? FeatureId { get; set; }

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 오류 유형
        /// </summary>
        public ErrorType ErrorType { get; set; } = ErrorType.System;

        /// <summary>
        /// 오류 위치 정보
        /// </summary>
        public GeographicLocation? Location { get; set; }

        /// <summary>
        /// 해결 여부
        /// </summary>
        public bool IsResolved { get; set; } = false;

        /// <summary>
        /// 해결 시간
        /// </summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>
        /// 해결 방법
        /// </summary>
        public string? ResolutionMethod { get; set; }

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        // 호환성을 위한 추가 속성들

        /// <summary>
        /// 소스 테이블명
        /// </summary>
        public string? SourceTable { get; set; }

        /// <summary>
        /// 소스 객체 ID
        /// </summary>
        public long? SourceObjectId { get; set; }

        /// <summary>
        /// 대상 테이블명
        /// </summary>
        public string? TargetTable { get; set; }

        /// <summary>
        /// 대상 객체 ID
        /// </summary>
        public long? TargetObjectId { get; set; }

        /// <summary>
        /// 필드명
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// 실제 값
        /// </summary>
        public string? ActualValue { get; set; }

        /// <summary>
        /// 기대 값
        /// </summary>
        public string? ExpectedValue { get; set; }

        /// <summary>
        /// X 좌표
        /// </summary>
        public double? X { get; set; }

        /// <summary>
        /// Y 좌표
        /// </summary>
        public double? Y { get; set; }

        /// <summary>
        /// 지오메트리 WKT
        /// </summary>
        public string? GeometryWKT { get; set; }

        /// <summary>
        /// 상세 정보
        /// </summary>
        public Dictionary<string, string>? Details { get; set; }
    }
}
