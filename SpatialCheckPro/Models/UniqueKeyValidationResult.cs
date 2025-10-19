using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// UK(Unique Key) 검수 결과
    /// </summary>
    public class UniqueKeyValidationResult
    {
        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 필드명
        /// </summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 총 레코드 수
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// 고유값 개수
        /// </summary>
        public int UniqueValues { get; set; }

        /// <summary>
        /// 중복값 개수
        /// </summary>
        public int DuplicateValues { get; set; }

        /// <summary>
        /// 중복값 상세 정보 목록
        /// </summary>
        public List<DuplicateValueInfo> Duplicates { get; set; } = new List<DuplicateValueInfo>();

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
    /// 중복값 상세 정보
    /// </summary>
    public class DuplicateValueInfo
    {
        /// <summary>
        /// 중복된 값
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 중복 개수
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 중복된 레코드의 ObjectId 목록
        /// </summary>
        public List<long> ObjectIds { get; set; } = new List<long>();

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
