#nullable enable
using System;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 오류 상태 열거형
    /// </summary>
    public enum ErrorStatus
    {
        /// <summary>
        /// 열림 (미해결)
        /// </summary>
        Open = 0,

        /// <summary>
        /// 수정됨
        /// </summary>
        Fixed = 1,

        /// <summary>
        /// 무시됨
        /// </summary>
        Ignored = 2,

        /// <summary>
        /// 거짓 양성 (False Positive)
        /// </summary>
        FalsePositive = 3,

        /// <summary>
        /// 검토 중
        /// </summary>
        UnderReview = 4,

        /// <summary>
        /// 승인됨
        /// </summary>
        Approved = 5
    }
}