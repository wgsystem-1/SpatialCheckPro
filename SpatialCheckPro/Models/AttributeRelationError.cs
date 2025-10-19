using System;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 속성 관계 오류 정보를 나타내는 모델 클래스
    /// </summary>
    public class AttributeRelationError
    {
        /// <summary>
        /// 객체 ID
        /// </summary>
        public long ObjectId { get; set; }

        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 필드명
        /// </summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// 규칙명
        /// </summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>
        /// 기대값
        /// </summary>
        public string ExpectedValue { get; set; } = string.Empty;

        /// <summary>
        /// 실제값
        /// </summary>
        public string ActualValue { get; set; } = string.Empty;

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 상세 정보
        /// </summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// 수정 제안
        /// </summary>
        public string SuggestedFix { get; set; } = string.Empty;

        /// <summary>
        /// 관련 테이블명
        /// </summary>
        public string RelatedTableName { get; set; } = string.Empty;

        /// <summary>
        /// 관련 객체 ID
        /// </summary>
        public long? RelatedObjectId { get; set; }

        /// <summary>
        /// 추가 속성 정보
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> Properties { get; set; } = new System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// 오류 감지 시간
        /// </summary>
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }
}
