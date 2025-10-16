using System;
using System.Collections.Generic;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Events;

namespace SpatialCheckPro.GUI.Models.Events
{
    /// <summary>
    /// 오류 상태 변경 이벤트 인자
    /// </summary>
    public class ErrorStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 변경된 QC 오류
        /// </summary>
        public QcError? QcError { get; set; }

        /// <summary>
        /// 변경된 오류 목록
        /// </summary>
        public List<ErrorFeature>? ChangedErrors { get; set; }

        /// <summary>
        /// 새로운 상태
        /// </summary>
        public string NewStatus { get; set; } = string.Empty;

        /// <summary>
        /// 이전 상태
        /// </summary>
        public string PreviousStatus { get; set; } = string.Empty;

        /// <summary>
        /// 사용자 ID
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// 변경 코멘트
        /// </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 변경 타입
        /// </summary>
        public string ChangeType { get; set; } = string.Empty;

        /// <summary>
        /// 상태 변경 정보
        /// </summary>
        public SpatialCheckPro.Models.QcErrorStatusChange? StatusChange { get; set; }

        /// <summary>
        /// 담당자 변경 정보
        /// </summary>
        public SpatialCheckPro.Models.QcErrorAssigneeChange? AssigneeChange { get; set; }
    }
}