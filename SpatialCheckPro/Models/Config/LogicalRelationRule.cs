using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 논리적 관계 규칙을 나타내는 클래스
    /// </summary>
    public class LogicalRelationRule
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
        /// 소스 테이블명
        /// </summary>
        public string SourceTable { get; set; } = string.Empty;

        /// <summary>
        /// 대상 테이블명
        /// </summary>
        public string TargetTable { get; set; } = string.Empty;

        /// <summary>
        /// JOIN 조건 (SQL WHERE 절 형태)
        /// 예: "source.FIELD1 = target.FIELD1"
        /// </summary>
        public string JoinCondition { get; set; } = string.Empty;

        /// <summary>
        /// 검증 표현식 (SQL WHERE 절 형태)
        /// 예: "source.STATUS = 'ACTIVE' AND target.COUNT > 0"
        /// </summary>
        public string ValidationExpression { get; set; } = string.Empty;

        /// <summary>
        /// 위반 시 심각도
        /// </summary>
        public ErrorSeverity ViolationSeverity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 규칙 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 규칙 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 오류 메시지 템플릿
        /// {0}: SourceTable, {1}: TargetTable, {2}: ObjectId 등을 사용 가능
        /// </summary>
        public string ErrorMessageTemplate { get; set; } = "논리적 관계 규칙 위반: {0}";

        /// <summary>
        /// 수정 제안 템플릿
        /// </summary>
        public string? SuggestedFixTemplate { get; set; }

        /// <summary>
        /// 예외 조건 목록
        /// 이 조건들이 만족되면 규칙을 적용하지 않음
        /// </summary>
        public List<string> ExceptionConditions { get; set; } = new List<string>();

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

            if (string.IsNullOrWhiteSpace(SourceTable))
            {
                errors.Add(new ValidationError { Message = "소스 테이블명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(TargetTable))
            {
                errors.Add(new ValidationError { Message = "대상 테이블명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(JoinCondition))
            {
                errors.Add(new ValidationError { Message = "JOIN 조건은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(ValidationExpression))
            {
                errors.Add(new ValidationError { Message = "검증 표현식은 필수입니다." });
            }

            return errors;
        }
    }
}
