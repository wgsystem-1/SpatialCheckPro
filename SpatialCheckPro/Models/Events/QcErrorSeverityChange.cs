using System;

namespace SpatialCheckPro.Models.Events
{
    /// <summary>
    /// QC 오류 심각도 변경 이벤트 인자
    /// </summary>
    public class QcErrorSeverityChange : EventArgs
    {
        /// <summary>
        /// 오류 ID
        /// </summary>
        public string ErrorId { get; set; } = string.Empty;

        /// <summary>
        /// 이전 심각도
        /// </summary>
        public string OldSeverity { get; set; } = string.Empty;

        /// <summary>
        /// 새로운 심각도
        /// </summary>
        public string NewSeverity { get; set; } = string.Empty;

        /// <summary>
        /// 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 변경자
        /// </summary>
        public string? ChangedBy { get; set; }

        /// <summary>
        /// 변경 사유
        /// </summary>
        public string? Reason { get; set; }
    }
}
