using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 오류 상태 변경 이벤트 인자 (공통 모델)
    /// </summary>
    public class ErrorStatusChangedEventArgs : EventArgs
    {
        /// <summary>변경된 오류 ID</summary>
        public string ErrorId { get; set; } = string.Empty;

        /// <summary>이전 상태</summary>
        public string OldStatus { get; set; } = string.Empty;

        /// <summary>새로운 상태</summary>
        public string NewStatus { get; set; } = string.Empty;

        /// <summary>변경 시간</summary>
        public DateTime ChangeTime { get; set; } = DateTime.UtcNow;

        /// <summary>변경된 QC 오류 (선택적)</summary>
        public QcError? QcError { get; set; }

        /// <summary>상태 변경 정보 (선택적)</summary>
        public QcErrorStatusChange? StatusChange { get; set; }

        /// <summary>담당자 변경 정보 (선택적)</summary>
        public QcErrorAssigneeChange? AssigneeChange { get; set; }

        /// <summary>심각도 변경 정보 (선택적)</summary>
        public QcErrorSeverityChange? SeverityChange { get; set; }

        /// <summary>변경 유형</summary>
        public string ChangeType { get; set; } = string.Empty;

        /// <summary>상태가 변경된 ErrorFeature 목록 (선택적)</summary>
        public List<object> ChangedErrors { get; set; } = new List<object>();
    }
}
