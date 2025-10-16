namespace SpatialCheckPro.Exceptions
{
    /// <summary>
    /// 검수 관련 예외를 나타내는 클래스
    /// </summary>
    public class ValidationException : Exception
    {
        /// <summary>
        /// 오류 코드
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// 검수 단계
        /// </summary>
        public string ValidationStage { get; set; } = string.Empty;

        /// <summary>
        /// 관련 규칙 ID
        /// </summary>
        public string? RuleId { get; set; }

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public ValidationException() : base() { }

        /// <summary>
        /// 메시지를 포함한 생성자
        /// </summary>
        /// <param name="message">오류 메시지</param>
        public ValidationException(string message) : base(message) { }

        /// <summary>
        /// 메시지와 내부 예외를 포함한 생성자
        /// </summary>
        /// <param name="message">오류 메시지</param>
        /// <param name="innerException">내부 예외</param>
        public ValidationException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 상세 정보를 포함한 생성자
        /// </summary>
        /// <param name="message">오류 메시지</param>
        /// <param name="errorCode">오류 코드</param>
        /// <param name="validationStage">검수 단계</param>
        /// <param name="ruleId">관련 규칙 ID</param>
        public ValidationException(string message, string errorCode, string validationStage, string? ruleId = null) 
            : base(message)
        {
            ErrorCode = errorCode;
            ValidationStage = validationStage;
            RuleId = ruleId;
        }

        /// <summary>
        /// 상세 정보와 내부 예외를 포함한 생성자
        /// </summary>
        /// <param name="message">오류 메시지</param>
        /// <param name="innerException">내부 예외</param>
        /// <param name="errorCode">오류 코드</param>
        /// <param name="validationStage">검수 단계</param>
        /// <param name="ruleId">관련 규칙 ID</param>
        public ValidationException(string message, Exception innerException, string errorCode, string validationStage, string? ruleId = null) 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            ValidationStage = validationStage;
            RuleId = ruleId;
        }
    }
}
