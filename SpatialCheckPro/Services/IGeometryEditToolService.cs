using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 지오메트리 편집 도구 서비스 인터페이스
    /// </summary>
    public interface IGeometryEditToolService
    {
        /// <summary>
        /// 점 지오메트리 이동
        /// </summary>
        /// <param name="pointGeometry">점 지오메트리</param>
        /// <param name="newLocation">새로운 위치</param>
        /// <returns>수정된 지오메트리</returns>
        Geometry MovePoint(Geometry pointGeometry, Coordinate newLocation);

        /// <summary>
        /// 선 지오메트리 버텍스 편집
        /// </summary>
        /// <param name="lineGeometry">선 지오메트리</param>
        /// <param name="vertexIndex">버텍스 인덱스</param>
        /// <param name="newLocation">새로운 위치</param>
        /// <returns>수정된 지오메트리</returns>
        Geometry EditLineVertex(Geometry lineGeometry, int vertexIndex, Coordinate newLocation);

        /// <summary>
        /// 폴리곤 지오메트리 버텍스 편집
        /// </summary>
        /// <param name="polygonGeometry">폴리곤 지오메트리</param>
        /// <param name="ringIndex">링 인덱스</param>
        /// <param name="vertexIndex">버텍스 인덱스</param>
        /// <param name="newLocation">새로운 위치</param>
        /// <returns>수정된 지오메트리</returns>
        Geometry EditPolygonVertex(Geometry polygonGeometry, int ringIndex, int vertexIndex, Coordinate newLocation);

        /// <summary>
        /// 버텍스 추가
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="insertIndex">삽입할 위치 인덱스</param>
        /// <param name="newVertex">새로운 버텍스 좌표</param>
        /// <returns>수정된 지오메트리</returns>
        Geometry AddVertex(Geometry geometry, int insertIndex, Coordinate newVertex);

        /// <summary>
        /// 버텍스 삭제
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="vertexIndex">삭제할 버텍스 인덱스</param>
        /// <returns>수정된 지오메트리</returns>
        Geometry RemoveVertex(Geometry geometry, int vertexIndex);

        /// <summary>
        /// 지오메트리 분할
        /// </summary>
        /// <param name="geometry">분할할 지오메트리</param>
        /// <param name="splitLine">분할선</param>
        /// <returns>분할된 지오메트리 목록</returns>
        List<Geometry> SplitGeometry(Geometry geometry, LineString splitLine);

        /// <summary>
        /// 지오메트리 병합
        /// </summary>
        /// <param name="geometries">병합할 지오메트리 목록</param>
        /// <returns>병합된 지오메트리</returns>
        Geometry MergeGeometries(IEnumerable<Geometry> geometries);

        /// <summary>
        /// 지오메트리 단순화
        /// </summary>
        /// <param name="geometry">단순화할 지오메트리</param>
        /// <param name="tolerance">허용 오차</param>
        /// <returns>단순화된 지오메트리</returns>
        Geometry SimplifyGeometry(Geometry geometry, double tolerance);

        /// <summary>
        /// 스냅 기능 - 버텍스에 스냅
        /// </summary>
        /// <param name="targetPoint">스냅할 점</param>
        /// <param name="snapGeometry">스냅 대상 지오메트리</param>
        /// <param name="tolerance">스냅 허용 거리</param>
        /// <returns>스냅된 좌표 (스냅되지 않으면 원본 반환)</returns>
        Coordinate SnapToVertex(Coordinate targetPoint, Geometry snapGeometry, double tolerance);

        /// <summary>
        /// 스냅 기능 - 엣지에 스냅
        /// </summary>
        /// <param name="targetPoint">스냅할 점</param>
        /// <param name="snapGeometry">스냅 대상 지오메트리</param>
        /// <param name="tolerance">스냅 허용 거리</param>
        /// <returns>스냅된 좌표 (스냅되지 않으면 원본 반환)</returns>
        Coordinate SnapToEdge(Coordinate targetPoint, Geometry snapGeometry, double tolerance);

        /// <summary>
        /// 스냅 기능 - 끝점에 스냅
        /// </summary>
        /// <param name="targetPoint">스냅할 점</param>
        /// <param name="snapGeometry">스냅 대상 지오메트리</param>
        /// <param name="tolerance">스냅 허용 거리</param>
        /// <returns>스냅된 좌표 (스냅되지 않으면 원본 반환)</returns>
        Coordinate SnapToEndpoint(Coordinate targetPoint, Geometry snapGeometry, double tolerance);

        /// <summary>
        /// 지오메트리 유효성 검사
        /// </summary>
        /// <param name="geometry">검사할 지오메트리</param>
        /// <returns>유효성 검사 결과</returns>
        GeometryValidationResult ValidateGeometry(Geometry geometry);

        /// <summary>
        /// 실시간 지오메트리 유효성 검사 (편집 중 즉시 검사)
        /// </summary>
        /// <param name="geometry">검사할 지오메트리</param>
        /// <returns>유효성 검사 결과</returns>
        GeometryValidationResult ValidateGeometryRealtime(Geometry geometry);

        /// <summary>
        /// 즉시 유효성 검사 (편집 중 실시간 피드백용)
        /// </summary>
        /// <param name="geometry">검사할 지오메트리</param>
        /// <returns>유효성 검사 결과</returns>
        GeometryValidationResult ValidateInstant(Geometry geometry);

        /// <summary>
        /// 저장 전 유효성 검사 (저장 차단용)
        /// </summary>
        /// <param name="geometry">검사할 지오메트리</param>
        /// <returns>저장 가능 여부, 검사 결과, 차단 사유</returns>
        (bool canSave, GeometryValidationResult validationResult, List<string> blockingReasons) ValidateForSave(Geometry geometry);

        /// <summary>
        /// 지오메트리 자동 수정
        /// </summary>
        /// <param name="geometry">수정할 지오메트리</param>
        /// <returns>수정된 지오메트리와 수정 결과</returns>
        Task<(Geometry fixedGeometry, List<string> fixActions)> AutoFixGeometryAsync(Geometry geometry);
    }

    /// <summary>
    /// 지오메트리 유효성 검사 결과
    /// </summary>
    public class GeometryValidationResult
    {
        /// <summary>유효성 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }

        /// <summary>경고 메시지 목록</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>자동 수정 가능 여부</summary>
        public bool CanAutoFix { get; set; }

        /// <summary>자동 수정된 지오메트리</summary>
        public Geometry FixedGeometry { get; set; }

        /// <summary>검사 세부 정보</summary>
        public Dictionary<string, object> Details { get; set; } = new();
    }

    /// <summary>
    /// 버텍스 정보
    /// </summary>
    public class VertexInfo
    {
        /// <summary>버텍스 인덱스</summary>
        public int Index { get; set; }

        /// <summary>버텍스 좌표</summary>
        public Coordinate Coordinate { get; set; }

        /// <summary>거리</summary>
        public double Distance { get; set; }

        /// <summary>링 인덱스 (폴리곤인 경우)</summary>
        public int RingIndex { get; set; } = -1;

        /// <summary>지오메트리 타입</summary>
        public string GeometryType { get; set; }

        /// <summary>편집 가능 여부</summary>
        public bool IsEditable { get; set; } = true;
    }

    /// <summary>
    /// 엣지 정보
    /// </summary>
    public class EdgeInfo
    {
        /// <summary>시작 버텍스 인덱스</summary>
        public int StartVertexIndex { get; set; }

        /// <summary>끝 버텍스 인덱스</summary>
        public int EndVertexIndex { get; set; }

        /// <summary>시작 좌표</summary>
        public Coordinate StartCoordinate { get; set; }

        /// <summary>끝 좌표</summary>
        public Coordinate EndCoordinate { get; set; }

        /// <summary>가장 가까운 점</summary>
        public Coordinate NearestPoint { get; set; }

        /// <summary>거리</summary>
        public double Distance { get; set; }

        /// <summary>링 인덱스 (폴리곤인 경우)</summary>
        public int RingIndex { get; set; } = -1;

        /// <summary>지오메트리 타입</summary>
        public string GeometryType { get; set; }
    }

    /// <summary>
    /// 지오메트리 편집 도구 타입
    /// </summary>
    public enum GeometryEditTool
    {
        /// <summary>선택</summary>
        Select,

        /// <summary>버텍스 이동</summary>
        MoveVertex,

        /// <summary>버텍스 추가</summary>
        AddVertex,

        /// <summary>버텍스 삭제</summary>
        DeleteVertex,

        /// <summary>지오메트리 이동</summary>
        MoveGeometry,

        /// <summary>지오메트리 회전</summary>
        RotateGeometry,

        /// <summary>지오메트리 스케일</summary>
        ScaleGeometry
    }
}
