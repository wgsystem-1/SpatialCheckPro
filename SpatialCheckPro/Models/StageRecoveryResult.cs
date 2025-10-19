namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 단계 복구 결과를 나타내는 클래스
    /// </summary>
    public class StageRecoveryResult
    {
        /// <summary>
        /// 복구 성공 여부
        /// </summary>
        public bool IsRecovered { get; set; }

        /// <summary>
        /// 복구 시도 횟수
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// 복구 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 복구된 결과 (복구 성공 시)
        /// </summary>
        public ValidationResult? RecoveredResult { get; set; }

        /// <summary>
        /// 복구 실패 원인 (복구 실패 시)
        /// </summary>
        public Exception? FailureReason { get; set; }

        /// <summary>
        /// 복구 소요 시간
        /// </summary>
        public TimeSpan RecoveryTime { get; set; }
    }
}
