using System;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 상태 변경 이벤트 인자
    /// </summary>
    public class StatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 변경된 오류 피처
        /// </summary>
        public ErrorFeature ErrorFeature { get; set; }

        /// <summary>
        /// 이전 상태
        /// </summary>
        public ErrorStatus OldStatus { get; set; }

        /// <summary>
        /// 새로운 상태
        /// </summary>
        public ErrorStatus NewStatus { get; set; }

        /// <summary>
        /// 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>
        /// 오류 피처 ID (호환성을 위해 추가)
        /// </summary>
        public string ErrorFeatureId { get; set; } = string.Empty;

        /// <summary>
        /// 이전 상태 (호환성을 위해 추가)
        /// </summary>
        public ErrorStatus PreviousStatus { get; set; }

        /// <summary>
        /// 사용자 ID (호환성을 위해 추가)
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        public StatusChangedEventArgs(ErrorFeature errorFeature, ErrorStatus oldStatus, ErrorStatus newStatus)
        {
            ErrorFeature = errorFeature;
            OldStatus = oldStatus;
            NewStatus = newStatus;
            PreviousStatus = oldStatus;
            ErrorFeatureId = errorFeature.Id;
            ChangedAt = DateTime.Now;
        }
    }
}