using System;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// QC 오류 상태 변경 정보
    /// </summary>
    public class QcErrorStatusChange
    {
        /// <summary>
        /// 이전 상태
        /// </summary>
        public string OldStatus { get; set; } = string.Empty;

        /// <summary>
        /// 새로운 상태
        /// </summary>
        public string NewStatus { get; set; } = string.Empty;

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
