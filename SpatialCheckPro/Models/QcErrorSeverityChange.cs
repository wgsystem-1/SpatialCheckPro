using System;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// QC 오류 심각도 변경 정보
    /// </summary>
    public class QcErrorSeverityChange
    {
        /// <summary>
        /// 이전 심각도
        /// </summary>
        public string OldSeverity { get; set; } = string.Empty;

        /// <summary>
        /// 새로운 심각도
        /// </summary>
        public string NewSeverity { get; set; } = string.Empty;

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
