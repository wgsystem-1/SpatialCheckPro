using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// FK(Foreign Key) 검수 결과
    /// </summary>
    public class ForeignKeyValidationResult
    {
        /// <summary>
        /// 소스 테이블명
        /// </summary>
        public string SourceTable { get; set; } = string.Empty;

        /// <summary>
        /// 소스 필드명
        /// </summary>
        public string SourceField { get; set; } = string.Empty;

        /// <summary>
        /// 참조 테이블명
        /// </summary>
        public string ReferenceTable { get; set; } = string.Empty;

        /// <summary>
        /// 참조 필드명
        /// </summary>
        public string ReferenceField { get; set; } = string.Empty;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 총 레코드 수
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// 유효한 참조 개수
        /// </summary>
        public int ValidReferences { get; set; }

        /// <summary>
        /// 고아 레코드 개수
        /// </summary>
        public int OrphanRecords { get; set; }

        /// <summary>
        /// 고아 레코드 상세 정보 목록
        /// </summary>
        public List<OrphanRecordInfo> Orphans { get; set; } = new List<OrphanRecordInfo>();

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// 검수 수행 시간
        /// </summary>
        public DateTime ValidatedAt { get; set; }

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 오류 카테고리
        /// </summary>
        public ErrorCategory Category { get; set; } = ErrorCategory.DataIntegrity;

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 상세 오류 정보
        /// </summary>
        public string Details { get; set; } = string.Empty;
    }

    /// <summary>
    /// 고아 레코드 정보
    /// </summary>
    public class OrphanRecordInfo
    {
        /// <summary>
        /// 레코드의 ObjectId
        /// </summary>
        public long ObjectId { get; set; }

        /// <summary>
        /// 참조되지 않는 값
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 참조되어야 할 테이블명
        /// </summary>
        public string ExpectedTable { get; set; } = string.Empty;

        /// <summary>
        /// 참조되어야 할 필드명
        /// </summary>
        public string ExpectedField { get; set; } = string.Empty;

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 상세 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
