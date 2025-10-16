using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 무결성 검수 요약 정보
    /// </summary>
    public class IntegrityValidationSummary
    {
        /// <summary>
        /// 총 테이블 수
        /// </summary>
        public int TotalTables { get; set; }

        /// <summary>
        /// 총 필드 수
        /// </summary>
        public int TotalFields { get; set; }

        /// <summary>
        /// UK 위반 개수
        /// </summary>
        public int UkViolations { get; set; }

        /// <summary>
        /// FK 위반 개수
        /// </summary>
        public int FkViolations { get; set; }

        /// <summary>
        /// 총 처리 시간
        /// </summary>
        public TimeSpan TotalProcessingTime { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime CompletedAt { get; set; }

        /// <summary>
        /// 오류 목록
        /// </summary>
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();

        /// <summary>
        /// 경고 목록
        /// </summary>
        public List<IntegrityValidationWarning> Warnings { get; set; } = new List<IntegrityValidationWarning>();

        /// <summary>
        /// UK 검수 결과 목록
        /// </summary>
        public List<UniqueKeyValidationResult> UniqueKeyResults { get; set; } = new List<UniqueKeyValidationResult>();

        /// <summary>
        /// FK 검수 결과 목록
        /// </summary>
        public List<ForeignKeyValidationResult> ForeignKeyResults { get; set; } = new List<ForeignKeyValidationResult>();

        /// <summary>
        /// 전체 검수 통과 여부
        /// </summary>
        public bool IsValid => UkViolations == 0 && FkViolations == 0 && Errors.Count == 0;

        /// <summary>
        /// 총 오류 개수
        /// </summary>
        public int TotalErrors => UkViolations + FkViolations + Errors.Count;

        /// <summary>
        /// 총 경고 개수
        /// </summary>
        public int TotalWarnings => Warnings.Count;
    }

    /// <summary>
    /// 무결성 검수 경고 정보
    /// </summary>
    public class IntegrityValidationWarning
    {
        /// <summary>
        /// 경고 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 경고 카테고리
        /// </summary>
        public ErrorCategory Category { get; set; } = ErrorCategory.DataIntegrity;

        /// <summary>
        /// 테이블명
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// 필드명
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// 상세 정보
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// 발생 시간
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
