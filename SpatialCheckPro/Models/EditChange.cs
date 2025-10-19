using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 개별 편집 변경사항을 나타내는 모델 클래스
    /// </summary>
    public class EditChange
    {
        /// <summary>변경 고유 ID</summary>
        public string ChangeId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>변경 타입</summary>
        public EditChangeType ChangeType { get; set; }

        /// <summary>변경 시간</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>변경된 속성명 (속성 변경인 경우)</summary>
        public string? AttributeName { get; set; }

        /// <summary>변경 전 값</summary>
        public object? OldValue { get; set; }

        /// <summary>변경 후 값</summary>
        public object? NewValue { get; set; }

        /// <summary>변경 전 지오메트리 (지오메트리 변경인 경우)</summary>
        public Geometry? OldGeometry { get; set; }

        /// <summary>변경 후 지오메트리 (지오메트리 변경인 경우)</summary>
        public Geometry? NewGeometry { get; set; }

        /// <summary>변경 설명</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>변경 메타데이터</summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 편집 변경 타입 열거형
    /// </summary>
    public enum EditChangeType
    {
        /// <summary>속성 값 변경</summary>
        AttributeChange,
        /// <summary>지오메트리 변경</summary>
        GeometryChange,
        /// <summary>점 이동</summary>
        PointMove,
        /// <summary>버텍스 추가</summary>
        VertexAdd,
        /// <summary>버텍스 삭제</summary>
        VertexDelete,
        /// <summary>버텍스 이동</summary>
        VertexMove,
        /// <summary>지오메트리 분할</summary>
        GeometrySplit,
        /// <summary>지오메트리 병합</summary>
        GeometryMerge,
        /// <summary>지오메트리 단순화</summary>
        GeometrySimplify
    }
}
