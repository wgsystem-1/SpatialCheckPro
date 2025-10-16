#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지오메트리 편집 도구 서비스 인터페이스 (GUI 전용)
    /// </summary>
    public interface IGeometryEditToolService
    {
        /// <summary>
        /// 지오메트리 검증
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        GeometryValidationResult ValidateGeometry(NetTopologySuite.Geometries.Geometry geometry);

        /// <summary>
        /// 실시간 지오메트리 검증
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        GeometryValidationResult ValidateGeometryRealtime(NetTopologySuite.Geometries.Geometry geometry);

        /// <summary>
        /// 즉시 지오메트리 검증
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        GeometryValidationResult ValidateInstant(NetTopologySuite.Geometries.Geometry geometry);

        /// <summary>
        /// 저장용 지오메트리 검증
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>저장 가능 여부, 검증 결과, 차단 사유</returns>
        (bool canSave, GeometryValidationResult validationResult, List<string> blockingReasons) ValidateForSave(NetTopologySuite.Geometries.Geometry geometry);

        /// <summary>
        /// 점 이동
        /// </summary>
        /// <param name="point">이동할 점</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>이동 결과</returns>
        GeometryEditResult MovePoint(NetTopologySuite.Geometries.Geometry point, double newX, double newY);

        /// <summary>
        /// 선형 지오메트리의 버텍스 편집
        /// </summary>
        /// <param name="lineString">편집할 선형 지오메트리</param>
        /// <param name="vertexIndex">버텍스 인덱스</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>편집 결과</returns>
        GeometryEditResult EditLineVertex(NetTopologySuite.Geometries.Geometry lineString, int vertexIndex, double newX, double newY);

        /// <summary>
        /// 버텍스 추가
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="insertIndex">삽입 위치</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>편집 결과</returns>
        GeometryEditResult AddVertex(NetTopologySuite.Geometries.Geometry geometry, int insertIndex, double x, double y);

        /// <summary>
        /// 버텍스 제거
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="vertexIndex">제거할 버텍스 인덱스</param>
        /// <returns>편집 결과</returns>
        GeometryEditResult RemoveVertex(NetTopologySuite.Geometries.Geometry geometry, int vertexIndex);

        /// <summary>
        /// 지오메트리 단순화
        /// </summary>
        /// <param name="geometry">단순화할 지오메트리</param>
        /// <param name="tolerance">허용 오차</param>
        /// <returns>단순화 결과</returns>
        GeometryEditResult SimplifyGeometry(NetTopologySuite.Geometries.Geometry geometry, double tolerance);

        /// <summary>
        /// 자동 수정
        /// </summary>
        /// <param name="geometry">수정할 지오메트리</param>
        /// <returns>수정 결과</returns>
        Task<(NetTopologySuite.Geometries.Geometry? fixedGeometry, List<string> fixActions)> AutoFixGeometryAsync(NetTopologySuite.Geometries.Geometry geometry);
    }
}