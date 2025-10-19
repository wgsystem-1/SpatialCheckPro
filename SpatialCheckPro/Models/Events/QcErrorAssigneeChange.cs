using System;

namespace SpatialCheckPro.Models.Events
{
    /// <summary>
    /// QC 오류 담당자 변경 이벤트 인자
    /// </summary>
    public class QcErrorAssigneeChange : EventArgs
    {
        /// <summary>
        /// 오류 ID
        /// </summary>
        public string ErrorId { get; set; } = string.Empty;

        /// <summary>
        /// 이전 담당자
        /// </summary>
        public string? OldAssignee { get; set; }

        /// <summary>
        /// 새로운 담당자
        /// </summary>
        public string? NewAssignee { get; set; }

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
