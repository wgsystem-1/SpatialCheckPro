using System;
using System.Collections.Generic;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 공간 관계 오류 정보를 나타내는 모델 클래스
    /// </summary>
    public class SpatialRelationError
    {
        /// <summary>
        /// 원본 객체 ID
        /// </summary>
        public long SourceObjectId { get; set; }

        /// <summary>
        /// 대상 객체 ID (선택적)
        /// </summary>
        public long? TargetObjectId { get; set; }

        /// <summary>
        /// 원본 레이어명
        /// </summary>
        public string SourceLayer { get; set; } = string.Empty;

        /// <summary>
        /// 대상 레이어명
        /// </summary>
        public string TargetLayer { get; set; } = string.Empty;

        /// <summary>
        /// 공간 관계 타입
        /// </summary>
        public SpatialRelationType RelationType { get; set; }

        /// <summary>
        /// 오류 타입
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// 오류 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; }

        /// <summary>
        /// 오류 위치 좌표
        /// </summary>
        public Point ErrorLocation { get; set; } = new Point();

        /// <summary>
        /// 오류 위치 X 좌표
        /// </summary>
        public double ErrorLocationX 
        { 
            get => ErrorLocation.X; 
            set => ErrorLocation.X = value; 
        }

        /// <summary>
        /// 오류 위치 Y 좌표
        /// </summary>
        public double ErrorLocationY 
        { 
            get => ErrorLocation.Y; 
            set => ErrorLocation.Y = value; 
        }

        /// <summary>
        /// 지오메트리 WKT 표현
        /// </summary>
        public string GeometryWKT { get; set; } = string.Empty;

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 추가 속성 정보
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 오류 감지 시간
        /// </summary>
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 점 좌표 클래스
    /// </summary>
    public class Point
    {
        /// <summary>X 좌표</summary>
        public double X { get; set; }
        /// <summary>Y 좌표</summary>
        public double Y { get; set; }

        public Point() { }

        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
