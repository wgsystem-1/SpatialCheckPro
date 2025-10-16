using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 지오메트리 유효성 검사 결과를 나타내는 모델 클래스
    /// </summary>
    public class GeometryValidationResult
    {
        /// <summary>지오메트리가 유효한지 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>오류 메시지 (호환성을 위해 추가)</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>유효성 검사 오류 목록</summary>
        public List<GeometryValidationError> Errors { get; set; } = new List<GeometryValidationError>();

        /// <summary>경고 목록</summary>
        public List<GeometryValidationWarning> Warnings { get; set; } = new List<GeometryValidationWarning>();

        /// <summary>검사된 지오메트리</summary>
        public Geometry? Geometry { get; set; }

        /// <summary>검사 시간</summary>
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;

        /// <summary>자동 수정 제안 목록</summary>
        public List<GeometryFixSuggestion> FixSuggestions { get; set; } = new List<GeometryFixSuggestion>();

        /// <summary>유효한 지오메트리 결과 생성</summary>
        public static GeometryValidationResult Valid(Geometry geometry)
        {
            return new GeometryValidationResult
            {
                IsValid = true,
                Geometry = geometry
            };
        }

        /// <summary>무효한 지오메트리 결과 생성</summary>
        public static GeometryValidationResult Invalid(Geometry geometry, string errorMessage)
        {
            return new GeometryValidationResult
            {
                IsValid = false,
                Geometry = geometry,
                Errors = new List<GeometryValidationError>
                {
                    new GeometryValidationError
                    {
                        ErrorType = GeometryErrorType.InvalidGeometry,
                        Message = errorMessage
                    }
                }
            };
        }
    }

    /// <summary>
    /// 지오메트리 유효성 검사 오류
    /// </summary>
    public class GeometryValidationError
    {
        /// <summary>오류 타입</summary>
        public GeometryErrorType ErrorType { get; set; }

        /// <summary>오류 메시지</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>오류 위치 (좌표)</summary>
        public Coordinate? Location { get; set; }

        /// <summary>심각도</summary>
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
    }

    /// <summary>
    /// 지오메트리 유효성 검사 경고
    /// </summary>
    public class GeometryValidationWarning
    {
        /// <summary>경고 타입</summary>
        public GeometryWarningType WarningType { get; set; }

        /// <summary>경고 메시지</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>경고 위치 (좌표)</summary>
        public Coordinate? Location { get; set; }
    }

    /// <summary>
    /// 지오메트리 자동 수정 제안
    /// </summary>
    public class GeometryFixSuggestion
    {
        /// <summary>수정 타입</summary>
        public GeometryFixType FixType { get; set; }

        /// <summary>수정 설명</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>수정된 지오메트리</summary>
        public Geometry? FixedGeometry { get; set; }

        /// <summary>자동 적용 가능 여부</summary>
        public bool CanAutoApply { get; set; }
    }

    /// <summary>
    /// 지오메트리 오류 타입 열거형
    /// </summary>
    public enum GeometryErrorType
    {
        /// <summary>무효한 지오메트리</summary>
        InvalidGeometry,
        /// <summary>자체 교차</summary>
        SelfIntersection,
        /// <summary>중복 점</summary>
        DuplicatePoints,
        /// <summary>빈 지오메트리</summary>
        EmptyGeometry,
        /// <summary>NULL 지오메트리</summary>
        NullGeometry,
        /// <summary>슬리버 폴리곤</summary>
        SliverPolygon,
        /// <summary>짧은 선분</summary>
        ShortSegment,
        /// <summary>작은 면적</summary>
        SmallArea,
        /// <summary>무효한 좌표</summary>
        InvalidCoordinate,
        /// <summary>점 개수 부족</summary>
        InsufficientPoints,
        /// <summary>닫히지 않은 링</summary>
        UnclosedRing
    }

    /// <summary>
    /// 지오메트리 경고 타입 열거형
    /// </summary>
    public enum GeometryWarningType
    {
        /// <summary>거의 중복된 점</summary>
        NearDuplicatePoints,
        /// <summary>거의 직선인 곡선</summary>
        NearStraightCurve,
        /// <summary>복잡한 지오메트리</summary>
        ComplexGeometry,
        /// <summary>높은 정밀도</summary>
        HighPrecision,
        /// <summary>범위를 벗어남</summary>
        OutOfBounds
    }

    /// <summary>
    /// 지오메트리 수정 타입 열거형
    /// </summary>
    public enum GeometryFixType
    {
        /// <summary>자체 교차 제거</summary>
        RemoveSelfIntersection,
        /// <summary>중복 점 제거</summary>
        RemoveDuplicatePoints,
        /// <summary>지오메트리 단순화</summary>
        SimplifyGeometry,
        /// <summary>버퍼 적용</summary>
        ApplyBuffer,
        /// <summary>유효성 강제 적용</summary>
        ForceValid,
        /// <summary>링 닫기</summary>
        CloseRing
    }

    /// <summary>
    /// 유효성 검사 심각도 열거형
    /// </summary>
    public enum ValidationSeverity
    {
        /// <summary>정보</summary>
        Info,
        /// <summary>경고</summary>
        Warning,
        /// <summary>오류</summary>
        Error,
        /// <summary>치명적 오류</summary>
        Critical
    }
}
