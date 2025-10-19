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
    /// 점-폴리곤 포함 관계 검수를 수행하는 클래스 (대용량 데이터 최적화 포함)
    /// </summary>
    public class PointInPolygonChecker
    {
        private readonly ILogger<PointInPolygonChecker> _logger;
        private readonly ISpatialIndexManager _spatialIndexManager;
        private readonly IGdalDataReader _gdalDataReader;
        private readonly ILargeDataOptimizer _largeDataOptimizer;
        private readonly IStreamingDataProcessor _streamingProcessor;

        public PointInPolygonChecker(
            ILogger<PointInPolygonChecker> logger,
            ISpatialIndexManager spatialIndexManager,
            IGdalDataReader gdalDataReader,
            ILargeDataOptimizer largeDataOptimizer,
            IStreamingDataProcessor streamingProcessor)
        {
            _logger = logger;
            _spatialIndexManager = spatialIndexManager;
            _gdalDataReader = gdalDataReader;
            _largeDataOptimizer = largeDataOptimizer;
            _streamingProcessor = streamingProcessor;
        }

        /// <summary>
        /// 점-폴리곤 포함 관계를 검사합니다 (대용량 데이터 최적화)
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="pointLayer">점 레이어명</param>
        /// <param name="polygonLayer">폴리곤 레이어명</param>
        /// <param name="rule">공간 관계 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>공간 관계 오류 목록</returns>
        public async Task<List<SpatialRelationError>> CheckAsync(
            string gdbPath,
            string pointLayer,
            string polygonLayer,
            SpatialRelationRule rule,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("점-폴리곤 포함 관계 검수 시작: {PointLayer} -> {PolygonLayer}", 
                    pointLayer, polygonLayer);

                // 1. 레이어 존재 여부 확인
                if (!await _gdalDataReader.IsTableExistsAsync(gdbPath, pointLayer))
                {
                    _logger.LogWarning("점 레이어가 존재하지 않습니다: {PointLayer}", pointLayer);
                    return errors;
                }

                if (!await _gdalDataReader.IsTableExistsAsync(gdbPath, polygonLayer))
                {
                    _logger.LogWarning("폴리곤 레이어가 존재하지 않습니다: {PolygonLayer}", polygonLayer);
                    return errors;
                }

                // 2. 대용량 데이터 처리 전략 수립
                var strategy = await _largeDataOptimizer.AnalyzeFileAndCreateStrategyAsync(
                    gdbPath, new List<string> { pointLayer, polygonLayer }, cancellationToken);

                _logger.LogInformation("처리 전략 수립 완료 - 모드: {ProcessingMode}, 배치크기: {BatchSize}, " +
                                     "예상시간: {EstimatedTime:F1}분",
                    strategy.ProcessingMode, strategy.OptimalBatchSize, strategy.EstimatedProcessingTimeMinutes);

                // 3. 폴리곤 레이어에 대한 공간 인덱스 생성
                var polygonIndex = await _spatialIndexManager.CreateSpatialIndexAsync(
                    gdbPath, polygonLayer, SpatialIndexType.RTree);

                // 4. 대용량 데이터 스트리밍 처리
                await foreach (var error in ProcessPointsWithOptimizationAsync(
                    gdbPath, pointLayer, polygonLayer, polygonIndex, rule, strategy, cancellationToken))
                {
                    errors.Add(error);
                }

                _logger.LogInformation("점-폴리곤 포함 관계 검수 완료: {ErrorCount}개 오류 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "점-폴리곤 포함 관계 검수 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 최적화된 방식으로 점들을 처리합니다
        /// </summary>
        private async IAsyncEnumerable<SpatialRelationError> ProcessPointsWithOptimizationAsync(
            string gdbPath,
            string pointLayer,
            string polygonLayer,
            ISpatialIndex polygonIndex,
            SpatialRelationRule rule,
            LargeDataProcessingStrategy strategy,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 점 피처를 PointFeatureInfo로 변환하는 함수
            Func<Feature, PointFeatureInfo> featureConverter = feature =>
            {
                var objectId = GetObjectId(feature);
                var geometry = feature.GetGeometryRef()?.Clone();
                
                return new PointFeatureInfo
                {
                    ObjectId = objectId,
                    Geometry = geometry
                };
            };

            // 배치 단위로 점들을 처리하는 함수
            Func<IEnumerable<PointFeatureInfo>, Task<IEnumerable<SpatialRelationError>>> batchProcessor = 
                async pointBatch =>
                {
                    var batchErrors = new List<SpatialRelationError>();

                    foreach (var pointFeature in pointBatch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (pointFeature.Geometry == null)
                        {
                            _logger.LogWarning("점 피처 {ObjectId}의 지오메트리가 null입니다", pointFeature.ObjectId);
                            continue;
                        }

                        // 점의 범위를 구하고 후보 폴리곤 검색
                        var pointEnvelope = GetGeometryEnvelope(pointFeature.Geometry);
                        var candidatePolygonIds = await _spatialIndexManager.QueryIntersectingFeaturesAsync(
                            polygonIndex, pointEnvelope);

                        // 정확한 점-폴리곤 포함 관계 테스트
                        bool isContained = false;
                        long? containingPolygonId = null;

                        foreach (var polygonId in candidatePolygonIds)
                        {
                            var polygonGeometry = await GetPolygonGeometryAsync(gdbPath, polygonLayer, polygonId);
                            if (polygonGeometry != null && IsPointInPolygon(pointFeature.Geometry, polygonGeometry))
                            {
                                isContained = true;
                                containingPolygonId = polygonId;
                                break;
                            }
                        }

                        // 규칙 위반 검사
                        if (rule.IsRequired && !isContained)
                        {
                            var error = CreateSpatialRelationError(
                                pointFeature, 
                                null, 
                                rule, 
                                "점이 필수 폴리곤 내부에 포함되지 않음",
                                pointFeature.Geometry);

                            batchErrors.Add(error);
                        }
                        else if (!rule.IsRequired && isContained)
                        {
                            var error = CreateSpatialRelationError(
                                pointFeature,
                                containingPolygonId,
                                rule,
                                "점이 금지된 폴리곤 내부에 포함됨",
                                pointFeature.Geometry);

                            batchErrors.Add(error);
                        }
                    }

                    return batchErrors;
                };

            // 대용량 데이터 스트리밍 처리
            await foreach (var error in _largeDataOptimizer.ProcessLargeDataStreamAsync(
                gdbPath, pointLayer, batchProcessor, featureConverter, strategy, cancellationToken))
            {
                yield return error;
            }
        }

        /// <summary>
        /// 점 피처들을 스트리밍 방식으로 조회합니다
        /// </summary>
        private async IAsyncEnumerable<PointFeatureInfo> GetPointFeaturesStreamAsync(
            string gdbPath, 
            string layerName, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            DataSource? dataSource = null;
            Layer? layer = null;

            try
            {
                // GDAL 데이터소스 열기
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
                            yield return new PointFeatureInfo
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

                    // ObjectId로 피처 검색
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
        /// 점이 폴리곤 내부에 포함되는지 확인합니다
        /// </summary>
        private bool IsPointInPolygon(Geometry pointGeometry, Geometry polygonGeometry)
        {
            try
            {
                // GDAL의 Within 메서드 사용 (점이 폴리곤 내부에 있는지 확인)
                return pointGeometry.Within(polygonGeometry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "점-폴리곤 포함 관계 테스트 중 오류 발생");
                return false;
            }
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
            // OBJECTID 필드 우선 확인
            var objectIdIndex = feature.GetFieldIndex("OBJECTID");
            if (objectIdIndex >= 0)
            {
                return feature.GetFieldAsInteger64(objectIdIndex);
            }

            // FID 폴백
            var fidIndex = feature.GetFieldIndex("FID");
            if (fidIndex >= 0)
            {
                return feature.GetFieldAsInteger64(fidIndex);
            }

            // 기본값으로 FID 사용
            return feature.GetFID();
        }

        /// <summary>
        /// 공간 관계 오류 객체를 생성합니다
        /// </summary>
        private SpatialRelationError CreateSpatialRelationError(
            PointFeatureInfo pointFeature,
            long? targetObjectId,
            SpatialRelationRule rule,
            string message,
            Geometry pointGeometry)
        {
            var envelope = new OSGeo.OGR.Envelope();
            pointGeometry.GetEnvelope(envelope);

            return new SpatialRelationError
            {
                SourceObjectId = pointFeature.ObjectId,
                TargetObjectId = targetObjectId,
                SourceLayer = rule.SourceLayer,
                TargetLayer = rule.TargetLayer,
                RelationType = rule.RelationType,
                ErrorType = "POINT_IN_POLYGON_VIOLATION",
                Severity = rule.ViolationSeverity,
                ErrorLocationX = (envelope.MinX + envelope.MaxX) / 2, // 중심점 X
                ErrorLocationY = (envelope.MinY + envelope.MaxY) / 2, // 중심점 Y
                GeometryWKT = ExportGeometryToWkt(pointGeometry),
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
        /// 점 피처 정보를 담는 내부 클래스
        /// </summary>
        private class PointFeatureInfo
        {
            public long ObjectId { get; set; }
            public Geometry Geometry { get; set; } = null!;
        }
    }
}
