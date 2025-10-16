#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Simplify;
using NetTopologySuite.Operation.Union;
using Geometry = NetTopologySuite.Geometries.Geometry;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using GeometryValidationResult = SpatialCheckPro.Models.GeometryValidationResult;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지오메트리 편집 도구 서비스 구현 클래스
    /// </summary>
    public class GeometryEditToolService : IGeometryEditToolService
    {
        private readonly ILogger<GeometryEditToolService> _logger;
        private readonly GeometryFactory _geometryFactory;

        public GeometryEditToolService(ILogger<GeometryEditToolService> logger)
        {
            _logger = logger;
            _geometryFactory = new GeometryFactory();
        }

        /// <summary>
        /// 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateGeometry(NetTopologySuite.Geometries.Geometry geometry)
        {
            try
            {
                var result = new GeometryValidationResult
                {
                    IsValid = true,
                    ErrorMessage = string.Empty,
                    ValidationTime = DateTime.Now
                };

                if (geometry == null)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "지오메트리가 null입니다.";
                    return result;
                }

                // 기본 유효성 검사
                if (geometry.IsEmpty)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "빈 지오메트리입니다.";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검증 실패");
                return new GeometryValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"검증 중 오류 발생: {ex.Message}",
                    ValidationTime = DateTime.Now
                };
            }
        }

        /// <summary>
        /// 실시간 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateGeometryRealtime(NetTopologySuite.Geometries.Geometry geometry)
        {
            // 실시간 검증은 기본 검증과 동일하게 처리
            return ValidateGeometry(geometry);
        }

        /// <summary>
        /// 즉시 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateInstant(NetTopologySuite.Geometries.Geometry geometry)
        {
            // 즉시 검증은 기본 검증과 동일하게 처리
            return ValidateGeometry(geometry);
        }

        /// <summary>
        /// 저장용 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>저장 가능 여부, 검증 결과, 차단 사유</returns>
        public (bool canSave, GeometryValidationResult validationResult, List<string> blockingReasons) ValidateForSave(NetTopologySuite.Geometries.Geometry geometry)
        {
            var validationResult = ValidateGeometry(geometry);
            var blockingReasons = new List<string>();

            if (!validationResult.IsValid)
            {
                blockingReasons.Add(validationResult.ErrorMessage);
            }

            return (validationResult.IsValid, validationResult, blockingReasons);
        }

        /// <summary>
        /// 점 이동 (인터페이스 구현)
        /// </summary>
        /// <param name="point">이동할 점</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>이동 결과</returns>
        public GeometryEditResult MovePoint(NetTopologySuite.Geometries.Geometry point, double newX, double newY)
        {
            try
            {
                if (point is NetTopologySuite.Geometries.Point ntsPoint)
                {
                    var newPoint = _geometryFactory.CreatePoint(new Coordinate(newX, newY));
                    return GeometryEditResult.Success(newPoint, "점 이동 완료");
                }
                return GeometryEditResult.Failure("점 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "점 이동 실패");
                return GeometryEditResult.Failure($"점 이동 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 선형 지오메트리의 버텍스 편집 (인터페이스 구현)
        /// </summary>
        /// <param name="lineString">편집할 선형 지오메트리</param>
        /// <param name="vertexIndex">버텍스 인덱스</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult EditLineVertex(NetTopologySuite.Geometries.Geometry lineString, int vertexIndex, double newX, double newY)
        {
            try
            {
                if (lineString is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToArray();
                    if (vertexIndex >= 0 && vertexIndex < coordinates.Length)
                    {
                        coordinates[vertexIndex] = new Coordinate(newX, newY);
                        var newLineString = _geometryFactory.CreateLineString(coordinates);
                        return GeometryEditResult.Success(newLineString, "버텍스 편집 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 버텍스 인덱스입니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 편집 실패");
                return GeometryEditResult.Failure($"버텍스 편집 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 버텍스 추가 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="insertIndex">삽입 위치</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult AddVertex(NetTopologySuite.Geometries.Geometry geometry, int insertIndex, double x, double y)
        {
            try
            {
                if (geometry is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToList();
                    if (insertIndex >= 0 && insertIndex <= coordinates.Count)
                    {
                        coordinates.Insert(insertIndex, new Coordinate(x, y));
                        var newLineString = _geometryFactory.CreateLineString(coordinates.ToArray());
                        return GeometryEditResult.Success(newLineString, "버텍스 추가 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 삽입 위치입니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 추가 실패");
                return GeometryEditResult.Failure($"버텍스 추가 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 버텍스 제거 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="vertexIndex">제거할 버텍스 인덱스</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult RemoveVertex(NetTopologySuite.Geometries.Geometry geometry, int vertexIndex)
        {
            try
            {
                if (geometry is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToList();
                    if (vertexIndex >= 0 && vertexIndex < coordinates.Count && coordinates.Count > 2)
                    {
                        coordinates.RemoveAt(vertexIndex);
                        var newLineString = _geometryFactory.CreateLineString(coordinates.ToArray());
                        return GeometryEditResult.Success(newLineString, "버텍스 제거 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 버텍스 인덱스이거나 최소 버텍스 수를 유지해야 합니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 제거 실패");
                return GeometryEditResult.Failure($"버텍스 제거 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 지오메트리 단순화 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">단순화할 지오메트리</param>
        /// <param name="tolerance">허용 오차</param>
        /// <returns>단순화 결과</returns>
        public GeometryEditResult SimplifyGeometry(NetTopologySuite.Geometries.Geometry geometry, double tolerance)
        {
            try
            {
                var simplifiedGeometry = DouglasPeuckerSimplifier.Simplify(geometry, tolerance);
                return GeometryEditResult.Success(simplifiedGeometry, "지오메트리 단순화 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 단순화 실패");
                return GeometryEditResult.Failure($"지오메트리 단순화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 자동 수정 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">수정할 지오메트리</param>
        /// <returns>수정 결과</returns>
        public async Task<(NetTopologySuite.Geometries.Geometry? fixedGeometry, List<string> fixActions)> AutoFixGeometryAsync(NetTopologySuite.Geometries.Geometry geometry)
        {
            try
            {
                await Task.Delay(1); // 비동기 작업 시뮬레이션
                
                var fixActions = new List<string>();
                
                // 기본 유효성 검사 및 수정
                if (!geometry.IsValid)
                {
                    var fixedGeometry = geometry.Buffer(0);
                    if (fixedGeometry.IsValid)
                    {
                        fixActions.Add("Buffer(0) 적용으로 지오메트리 수정");
                        return (fixedGeometry, fixActions);
                    }
                }
                
                return (geometry, fixActions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "자동 수정 실패");
                return (null, new List<string> { $"자동 수정 실패: {ex.Message}" });
            }
        }


    }
}