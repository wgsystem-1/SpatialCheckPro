using System;

namespace SpatialCheckPro.Models.Events
{
    /// <summary>
    /// QC 오류 상태 변경 이벤트 인자
    /// </summary>
    public class QcErrorStatusChange : EventArgs
    {
        /// <summary>
        /// 오류 ID
        /// </summary>
        public string ErrorId { get; set; } = string.Empty;

        /// <summary>
        /// 이전 상태
        /// </summary>
        public string OldStatus { get; set; } = string.Empty;

        /// <summary>
        /// 새로운 상태
        /// </summary>
        public string NewStatus { get; set; } = string.Empty;

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
