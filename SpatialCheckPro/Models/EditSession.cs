using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 편집 세션 정보를 관리하는 모델 클래스
    /// </summary>
    public class EditSession
    {
        /// <summary>편집 세션 고유 ID</summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>편집 대상 공간정보 파일 경로</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>편집 대상 피처 클래스명</summary>
        public string FeatureClassName { get; set; } = string.Empty;

        /// <summary>편집 대상 피처 ID</summary>
        public string FeatureId { get; set; } = string.Empty;

        /// <summary>편집 세션 시작 시간</summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>편집 세션 상태</summary>
        public EditSessionStatus Status { get; set; } = EditSessionStatus.Active;

        /// <summary>변경 이력 목록</summary>
        public List<EditChange> Changes { get; set; } = new List<EditChange>();

        /// <summary>원본 속성 값들 (롤백용)</summary>
        public Dictionary<string, object?> OriginalAttributes { get; set; } = new Dictionary<string, object?>();

        /// <summary>원본 지오메트리 (롤백용)</summary>
        public Geometry? OriginalGeometry { get; set; }

        /// <summary>현재 속성 값들</summary>
        public Dictionary<string, object?> CurrentAttributes { get; set; } = new Dictionary<string, object?>();

        /// <summary>현재 지오메트리</summary>
        public Geometry? CurrentGeometry { get; set; }

        /// <summary>편집 세션에 변경사항이 있는지 여부</summary>
        public bool HasChanges => Changes.Count > 0;

        /// <summary>Undo 가능한 변경사항 개수</summary>
        public int UndoCount => Changes.Count;

        /// <summary>마지막 변경 시간</summary>
        public DateTime? LastModified { get; set; }
    }

    /// <summary>
    /// 편집 세션 상태 열거형
    /// </summary>
    public enum EditSessionStatus
    {
        /// <summary>활성 상태</summary>
        Active,
        /// <summary>저장됨</summary>
        Saved,
        /// <summary>취소됨</summary>
        Cancelled,
        /// <summary>오류 발생</summary>
        Error
    }
}
