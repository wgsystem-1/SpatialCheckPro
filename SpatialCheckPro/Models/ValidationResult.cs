using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 전체 검수 결과
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 검수 ID
        /// </summary>
        public string ValidationId { get; set; } = string.Empty;

        /// <summary>
        /// 검수 대상 파일
        /// </summary>
        public string TargetFile { get; set; } = string.Empty;

        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 상태
        /// </summary>
        public ValidationStatus Status { get; set; }

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 총 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 총 경고 개수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 검수 소요 시간
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// 1단계 테이블 검수 결과
        /// </summary>
        public TableCheckResult? TableCheckResult { get; set; }

        /// <summary>
        /// 2단계 스키마 검수 결과
        /// </summary>
        public SchemaCheckResult? SchemaCheckResult { get; set; }

        /// <summary>
        /// 3단계 지오메트리 검수 결과
        /// </summary>
        public GeometryCheckResult? GeometryCheckResult { get; set; }

        /// <summary>
        /// 4단계 관계 검수 결과
        /// </summary>
        public RelationCheckResult? RelationCheckResult { get; set; }

        /// <summary>
        /// 5단계 속성 관계 검수 결과
        /// </summary>
        public AttributeRelationCheckResult? AttributeRelationCheckResult { get; set; }

        /// <summary>
        /// 0단계 FileGDB 완전성 검수 결과
        /// </summary>
        public CheckResult? FileGdbCheckResult { get; set; }

        /// <summary>
        /// 총 오류 개수 (별칭)
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// 총 경고 개수 (별칭)
        /// </summary>
        public int TotalWarnings { get; set; }

        /// <summary>
        /// 오류 메시지 (별칭)
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 오류 목록
        /// </summary>
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();

        /// <summary>
        /// 경고 목록
        /// </summary>
        public List<ValidationError> Warnings { get; set; } = new List<ValidationError>();
    }
}
