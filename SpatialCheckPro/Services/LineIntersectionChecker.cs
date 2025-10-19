using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 선-폴리곤 교차 관계 검수를 수행하는 클래스
    /// </summary>
    public class LineIntersectionChecker
    {
        private readonly ILogger<LineIntersectionChecker> _logger;
        private readonly ISpatialIndexManager _spatialIndexManager;
        private readonly IGdalDataReader _gdalDataReader;

        public LineIntersectionChecker(
            ILogger<LineIntersectionChecker> logger,
            ISpatialIndexManager spatialIndexManager,
            IGdalDataReader gdalDataReader)
        {
            _logger = logger;
            _spatialIndexManager = spatialIndexManager;
            _gdalDataReader = gdalDataReader;
        }

        /// <summary>
        /// 선-폴리곤 교차 관계를 검사합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="lineLayer">선 레이어명</param>
        /// <param name="polygonLayer">폴리곤 레이어명</param>
        /// <param name="rule">공간 관계 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>공간 관계 오류 목록</returns>
        public async Task<List<SpatialRelationError>> CheckAsync(
            string gdbPath,
            string lineLayer,
            string polygonLayer,
            SpatialRelationRule rule,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("선-폴리곤 교차 관계 검수 시작: {LineLayer} -> {PolygonLayer}", 
                    lineLayer, polygonLayer);

                // 1. 레이어 존재 여부 확인
                if (!await _gdalDataReader.IsTableExistsAsync(gdbPath, lineLayer))
                {
                    _logger.LogWarning("선 레이어가 존재하지 않습니다: {LineLayer}", lineLayer);
                    return errors;
                }

                if (!await _gdalDataReader.IsTableExistsAsync(gdbPath, polygonLayer))
                {
                    _logger.LogWarning("폴리곤 레이어가 존재하지 않습니다: {PolygonLayer}", polygonLayer);
                    return errors;
                }

                // 2. 폴리곤 레이어에 대한 공간 인덱스 생성
                var polygonIndex = await _spatialIndexManager.CreateSpatialIndexAsync(
                    gdbPath, polygonLayer, SpatialIndexType.RTree);

                // 3. 선 피처들을 스트리밍 방식으로 처리
                await foreach (var lineFeature in GetLineFeaturesStreamAsync(gdbPath, lineLayer, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var lineGeometry = lineFeature.Geometry;
                    if (lineGeometry == null)
                    {
                        _logger.LogWarning("선 피처 {ObjectId}의 지오메트리가 null입니다", lineFeature.ObjectId);
                        continue;
                    }

                    // 4. 선의 범위를 구하고 후보 폴리곤 검색
                    var lineEnvelope = GetGeometryEnvelope(lineGeometry);
                    var candidatePolygonIds = await _spatialIndexManager.QueryIntersectingFeaturesAsync(
                        polygonIndex, lineEnvelope);

                    // 5. 정확한 선-폴리곤 교차 관계 테스트
                    var intersectionResults = new List<LinePolygonIntersection>();

                    foreach (var polygonId in candidatePolygonIds)
                    {
                        var polygonGeometry = await GetPolygonGeometryAsync(gdbPath, polygonLayer, polygonId);
                        if (polygonGeometry != null)
                        {
                            var intersection = AnalyzeLinePolygonIntersection(lineGeometry, polygonGeometry, polygonId);
                            if (intersection != null)
                            {
                                intersectionResults.Add(intersection);
                            }
                        }
                    }

                    // 6. 규칙 위반 검사
                    await CheckIntersectionViolationsAsync(
                        lineFeature, intersectionResults, rule, errors);
                }

                _logger.LogInformation("선-폴리곤 교차 관계 검수 완료: {ErrorCount}개 오류 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선-폴리곤 교차 관계 검수 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 선 피처들을 스트리밍 방식으로 조회합니다
        /// </summary>
        private async IAsyncEnumerable<LineFeatureInfo> GetLineFeaturesStreamAsync(
            string gdbPath, 
            string layerName, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            DataSource? dataSource = null;
            Layer? layer = null;

            try
            {
                dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                layer = dataSource.GetLayerByName(layerName);
                if (layer == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {layerName}");
                }

                layer.ResetReading();
                Feature? feature;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var objectId = GetObjectId(feature);
                        var geometry = feature.GetGeometryRef();

                        if (geometry != null)
                        {
                            yield return new LineFeatureInfo
                            {
                                ObjectId = objectId,
                                Geometry = geometry.Clone()
                            };
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }
            }
            finally
            {
                layer?.Dispose();
                dataSource?.Dispose();
            }
        }

        /// <summary>
        /// 폴리곤 지오메트리를 조회합니다
        /// </summary>
        private async Task<Geometry?> GetPolygonGeometryAsync(string gdbPath, string layerName, long objectId)
        {
            return await Task.Run(() =>
            {
                DataSource? dataSource = null;
                Layer? layer = null;

                try
                {
                    dataSource = Ogr.Open(gdbPath, 0);
                    if (dataSource == null) return null;

                    layer = dataSource.GetLayerByName(layerName);
                    if (layer == null) return null;

                    layer.SetAttributeFilter($"OBJECTID = {objectId}");
                    layer.ResetReading();

                    var feature = layer.GetNextFeature();
                    if (feature != null)
                    {
                        var geometry = feature.GetGeometryRef();
                        var clonedGeometry = geometry?.Clone();
                        feature.Dispose();
                        return clonedGeometry;
                    }

                    return null;
                }
                finally
                {
                    layer?.Dispose();
                    dataSource?.Dispose();
                }
            });
        }

        /// <summary>
        /// 선-폴리곤 교차 관계를 분석합니다
        /// </summary>
        private LinePolygonIntersection? AnalyzeLinePolygonIntersection(
            Geometry lineGeometry, 
            Geometry polygonGeometry, 
            long polygonId)
        {
            try
            {
                var intersects = lineGeometry.Intersects(polygonGeometry);
                if (!intersects) return null;

                // 교차점 계산
                var intersectionGeometry = lineGeometry.Intersection(polygonGeometry);
                if (intersectionGeometry == null) return null;

                // 교차 타입 분석
                var intersectionType = DetermineIntersectionType(lineGeometry, polygonGeometry);
                
                // 교차점들의 좌표 수집
                var intersectionPoints = ExtractIntersectionPoints(intersectionGeometry);

                return new LinePolygonIntersection
                {
                    PolygonId = polygonId,
                    IntersectionType = intersectionType,
                    IntersectionGeometry = intersectionGeometry,
                    IntersectionPoints = intersectionPoints,
                    IntersectionLength = CalculateIntersectionLength(intersectionGeometry)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "선-폴리곤 교차 분석 중 오류 발생: PolygonId={PolygonId}", polygonId);
                return null;
            }
        }

        /// <summary>
        /// 교차 타입을 결정합니다
        /// </summary>
        private LineIntersectionType DetermineIntersectionType(Geometry lineGeometry, Geometry polygonGeometry)
        {
            try
            {
                if (lineGeometry.Within(polygonGeometry))
                {
                    return LineIntersectionType.Within; // 선이 폴리곤 내부에 완전히 포함
                }
                else if (lineGeometry.Crosses(polygonGeometry))
                {
                    return LineIntersectionType.Crosses; // 선이 폴리곤을 횡단
                }
                else if (lineGeometry.Touches(polygonGeometry))
                {
                    return LineIntersectionType.Touches; // 선이 폴리곤 경계에 접촉
                }
                else if (lineGeometry.Overlaps(polygonGeometry))
                {
                    return LineIntersectionType.Overlaps; // 선이 폴리곤과 겹침
                }
                else
                {
                    return LineIntersectionType.Intersects; // 일반적인 교차
                }
            }
            catch
            {
                return LineIntersectionType.Intersects;
            }
        }

        /// <summary>
        /// 교차점들의 좌표를 추출합니다
        /// </summary>
        private List<(double X, double Y)> ExtractIntersectionPoints(Geometry intersectionGeometry)
        {
            var points = new List<(double X, double Y)>();

            try
            {
                var geometryType = intersectionGeometry.GetGeometryType();

                switch (geometryType)
                {
                    case wkbGeometryType.wkbPoint:
                        points.Add((intersectionGeometry.GetX(0), intersectionGeometry.GetY(0)));
                        break;

                    case wkbGeometryType.wkbMultiPoint:
                        for (int i = 0; i < intersectionGeometry.GetGeometryCount(); i++)
                        {
                            var point = intersectionGeometry.GetGeometryRef(i);
                            points.Add((point.GetX(0), point.GetY(0)));
                        }
                        break;

                    case wkbGeometryType.wkbLineString:
                        // 선분의 시작점과 끝점 추가
                        var pointCount = intersectionGeometry.GetPointCount();
                        if (pointCount > 0)
                        {
                            points.Add((intersectionGeometry.GetX(0), intersectionGeometry.GetY(0)));
                            if (pointCount > 1)
                            {
                                points.Add((intersectionGeometry.GetX(pointCount - 1), intersectionGeometry.GetY(pointCount - 1)));
                            }
                        }
                        break;

                    case wkbGeometryType.wkbMultiLineString:
                        for (int i = 0; i < intersectionGeometry.GetGeometryCount(); i++)
                        {
                            var line = intersectionGeometry.GetGeometryRef(i);
                            var linePointCount = line.GetPointCount();
                            if (linePointCount > 0)
                            {
                                points.Add((line.GetX(0), line.GetY(0)));
                                if (linePointCount > 1)
                                {
                                    points.Add((line.GetX(linePointCount - 1), line.GetY(linePointCount - 1)));
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "교차점 추출 중 오류 발생");
            }

            return points;
        }

        /// <summary>
        /// 교차 길이를 계산합니다
        /// </summary>
        private double CalculateIntersectionLength(Geometry intersectionGeometry)
        {
            try
            {
                var geometryType = intersectionGeometry.GetGeometryType();
                
                if (geometryType == wkbGeometryType.wkbLineString || 
                    geometryType == wkbGeometryType.wkbMultiLineString)
                {
                    return intersectionGeometry.Length();
                }

                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// 교차 관계 위반을 검사합니다
        /// </summary>
        private async Task CheckIntersectionViolationsAsync(
            LineFeatureInfo lineFeature,
            List<LinePolygonIntersection> intersections,
            SpatialRelationRule rule,
            List<SpatialRelationError> errors)
        {
            await Task.Run(() =>
            {
                switch (rule.RelationType)
                {
                    case SpatialRelationType.Intersects:
                        // 교차해야 하는데 교차하지 않는 경우
                        if (rule.IsRequired && intersections.Count == 0)
                        {
                            var error = CreateSpatialRelationError(
                                lineFeature, null, rule,
                                "선이 필수 폴리곤과 교차하지 않음",
                                lineFeature.Geometry);
                            errors.Add(error);
                        }
                        // 교차하면 안 되는데 교차하는 경우
                        else if (!rule.IsRequired && intersections.Count > 0)
                        {
                            foreach (var intersection in intersections)
                            {
                                var error = CreateIntersectionError(
                                    lineFeature, intersection, rule,
                                    "선이 금지된 폴리곤과 교차함");
                                errors.Add(error);
                            }
                        }
                        break;

                    case SpatialRelationType.Crosses:
                        // 횡단 관계 검사
                        var crossingIntersections = intersections.Where(i => 
                            i.IntersectionType == LineIntersectionType.Crosses).ToList();
                        
                        if (rule.IsRequired && crossingIntersections.Count == 0)
                        {
                            var error = CreateSpatialRelationError(
                                lineFeature, null, rule,
                                "선이 필수 폴리곤을 횡단하지 않음",
                                lineFeature.Geometry);
                            errors.Add(error);
                        }
                        else if (!rule.IsRequired && crossingIntersections.Count > 0)
                        {
                            foreach (var intersection in crossingIntersections)
                            {
                                var error = CreateIntersectionError(
                                    lineFeature, intersection, rule,
                                    "선이 금지된 폴리곤을 횡단함");
                                errors.Add(error);
                            }
                        }
                        break;

                    case SpatialRelationType.Within:
                        // 포함 관계 검사
                        var withinIntersections = intersections.Where(i => 
                            i.IntersectionType == LineIntersectionType.Within).ToList();
                        
                        if (rule.IsRequired && withinIntersections.Count == 0)
                        {
                            var error = CreateSpatialRelationError(
                                lineFeature, null, rule,
                                "선이 필수 폴리곤 내부에 포함되지 않음",
                                lineFeature.Geometry);
                            errors.Add(error);
                        }
                        break;
                }
            });
        }

        /// <summary>
        /// 지오메트리의 범위를 구합니다
        /// </summary>
        private SpatialEnvelope GetGeometryEnvelope(Geometry geometry)
        {
            var envelope = new OSGeo.OGR.Envelope();
            geometry.GetEnvelope(envelope);

            return new SpatialEnvelope(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
        }

        /// <summary>
        /// 피처에서 ObjectId를 추출합니다
        /// </summary>
        private long GetObjectId(Feature feature)
        {
            var objectIdIndex = feature.GetFieldIndex("OBJECTID");
            if (objectIdIndex >= 0)
            {
                return feature.GetFieldAsInteger64(objectIdIndex);
            }

            var fidIndex = feature.GetFieldIndex("FID");
            if (fidIndex >= 0)
            {
                return feature.GetFieldAsInteger64(fidIndex);
            }

            return feature.GetFID();
        }

        /// <summary>
        /// 공간 관계 오류 객체를 생성합니다
        /// </summary>
        private SpatialRelationError CreateSpatialRelationError(
            LineFeatureInfo lineFeature,
            long? targetObjectId,
            SpatialRelationRule rule,
            string message,
            Geometry lineGeometry)
        {
            var envelope = new OSGeo.OGR.Envelope();
            lineGeometry.GetEnvelope(envelope);

            return new SpatialRelationError
            {
                SourceObjectId = lineFeature.ObjectId,
                TargetObjectId = targetObjectId,
                SourceLayer = rule.SourceLayer,
                TargetLayer = rule.TargetLayer,
                RelationType = rule.RelationType,
                ErrorType = "LINE_POLYGON_INTERSECTION_VIOLATION",
                Severity = rule.ViolationSeverity,
                ErrorLocationX = (envelope.MinX + envelope.MaxX) / 2,
                ErrorLocationY = (envelope.MinY + envelope.MaxY) / 2,
                GeometryWKT = ExportGeometryToWkt(lineGeometry),
                Message = message,
                Properties = new Dictionary<string, object>
                {
                    ["RuleId"] = rule.RuleId,
                    ["RuleName"] = rule.RuleName,
                    ["Tolerance"] = rule.Tolerance
                },
                DetectedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 교차 오류 객체를 생성합니다
        /// </summary>
        private SpatialRelationError CreateIntersectionError(
            LineFeatureInfo lineFeature,
            LinePolygonIntersection intersection,
            SpatialRelationRule rule,
            string message)
        {
            var firstPoint = intersection.IntersectionPoints.FirstOrDefault();
            
            return new SpatialRelationError
            {
                SourceObjectId = lineFeature.ObjectId,
                TargetObjectId = intersection.PolygonId,
                SourceLayer = rule.SourceLayer,
                TargetLayer = rule.TargetLayer,
                RelationType = rule.RelationType,
                ErrorType = "LINE_POLYGON_INTERSECTION_VIOLATION",
                Severity = rule.ViolationSeverity,
                ErrorLocationX = firstPoint.X,
                ErrorLocationY = firstPoint.Y,
                GeometryWKT = ExportGeometryToWkt(intersection.IntersectionGeometry),
                Message = message,
                Properties = new Dictionary<string, object>
                {
                    ["RuleId"] = rule.RuleId,
                    ["RuleName"] = rule.RuleName,
                    ["IntersectionType"] = intersection.IntersectionType.ToString(),
                    ["IntersectionLength"] = intersection.IntersectionLength,
                    ["IntersectionPointCount"] = intersection.IntersectionPoints.Count
                },
                DetectedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 선 피처 정보를 담는 내부 클래스
        /// </summary>
        private class LineFeatureInfo
        {
            public long ObjectId { get; set; }
            public Geometry Geometry { get; set; } = null!;
        }

        /// <summary>
        /// 선-폴리곤 교차 정보를 담는 내부 클래스
        /// </summary>
        private class LinePolygonIntersection
        {
            public long PolygonId { get; set; }
            public LineIntersectionType IntersectionType { get; set; }
            public Geometry IntersectionGeometry { get; set; } = null!;
            public List<(double X, double Y)> IntersectionPoints { get; set; } = new();
            public double IntersectionLength { get; set; }
        }

        /// <summary>
        /// 지오메트리를 WKT 형식으로 내보냅니다
        /// </summary>
        private string ExportGeometryToWkt(Geometry geometry)
        {
            try
            {
                string wkt;
                geometry.ExportToWkt(out wkt);
                return wkt ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 선-폴리곤 교차 타입 열거형
        /// </summary>
        private enum LineIntersectionType
        {
            Intersects,  // 일반적인 교차
            Crosses,     // 횡단
            Within,      // 포함
            Touches,     // 접촉
            Overlaps     // 겹침
        }
    }
}
