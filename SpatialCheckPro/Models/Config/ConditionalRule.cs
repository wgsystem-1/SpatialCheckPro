using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 조건부 규칙을 나타내는 클래스
    /// </summary>
    public class ConditionalRule
    {
        /// <summary>
        /// 규칙 고유 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 규칙명
        /// </summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>
        /// 대상 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 조건 표현식 (SQL WHERE 절 형태)
        /// 예: "STATUS = 'ACTIVE' AND TYPE IN ('A', 'B')"
        /// </summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>
        /// 검증 표현식 (SQL WHERE 절 형태)
        /// 조건이 만족될 때 이 표현식도 만족해야 함
        /// 예: "AREA > 100 AND LENGTH > 10"
        /// </summary>
        public string ValidationExpression { get; set; } = string.Empty;

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 규칙 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 규칙 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 수정 제안 템플릿
        /// </summary>
        public string? SuggestedFixTemplate { get; set; }

        /// <summary>
        /// 규칙 우선순위 (낮을수록 먼저 실행)
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// 의존 규칙 ID 목록
        /// 이 규칙들이 모두 통과해야 현재 규칙을 실행
        /// </summary>
        public List<string> DependentRuleIds { get; set; } = new List<string>();

        /// <summary>
        /// 규칙 카테고리
        /// </summary>
        public string Category { get; set; } = "General";

        /// <summary>
        /// 규칙 태그 목록
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// 규칙 유효성을 검증합니다
        /// </summary>
        /// <returns>검증 오류 목록</returns>
        public List<ValidationError> Validate()
        {
            var errors = new List<ValidationError>();

            if (string.IsNullOrWhiteSpace(RuleId))
            {
                errors.Add(new ValidationError { Message = "규칙 ID는 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(RuleName))
            {
                errors.Add(new ValidationError { Message = "규칙명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(TableName))
            {
                errors.Add(new ValidationError { Message = "테이블명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(Condition))
            {
                errors.Add(new ValidationError { Message = "조건 표현식은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(ValidationExpression))
            {
                errors.Add(new ValidationError { Message = "검증 표현식은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(ErrorMessage))
            {
                errors.Add(new ValidationError { Message = "오류 메시지는 필수입니다." });
            }

            return errors;
        }
    }
}
