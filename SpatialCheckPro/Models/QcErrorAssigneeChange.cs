using System;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// QC 오류 담당자 변경 정보
    /// </summary>
    public class QcErrorAssigneeChange
    {
        /// <summary>
        /// 이전 담당자
        /// </summary>
        public string? OldAssignee { get; set; }

        /// <summary>
        /// 새로운 담당자
        /// </summary>
        public string? NewAssignee { get; set; }

        /// <summary>
        /// 변경 사유
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// 변경한 사용자
        /// </summary>
        public string ChangedBy { get; set; } = string.Empty;

        /// <summary>
        /// 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
