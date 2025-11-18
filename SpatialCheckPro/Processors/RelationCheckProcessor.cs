using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Enums;
using OSGeo.OGR;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SpatialCheckPro.Services;
using SpatialCheckPro.Utils;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 4단계 관계 검수 구현
    /// - Case1: TN_BULD 다각형 내 TN_BULD_CTPT 점 존재 여부, 건물 밖 점 존재 여부
    /// - Case2: TN_RODWAY_CTLN 선이 TN_RODWAY_BNDRY 면에 완전히 포함되는지(0.001 허용 오차)
    /// - Case3: TN_ARRFC 중 PG_RDFC_SE in {PRC002..PRC005}가 TN_RODWAY_BNDRY에 반드시 포함되어야 함
    /// </summary>
    public sealed class RelationCheckProcessor : IRelationCheckProcessor, IDisposable
    {
        private readonly ILogger<RelationCheckProcessor> _logger;
        private readonly ParallelProcessingManager? _parallelProcessingManager;
        private readonly SpatialCheckPro.Models.Config.PerformanceSettings _performanceSettings;
        private readonly StreamingGeometryProcessor? _streamingProcessor;
        private readonly SpatialCheckPro.Models.GeometryCriteria _geometryCriteria;
        private readonly IFeatureFilterService _featureFilterService;
        
        /// <summary>
        /// Union 지오메트리 캐시 (성능 최적화)
        /// </summary>
        private readonly Dictionary<string, Geometry?> _unionGeometryCache = new();
        
        /// <summary>
        /// 캐시 생성 시간 추적 (메모리 관리용)
        /// </summary>
        private readonly Dictionary<string, DateTime> _cacheTimestamps = new();

        /// <summary>
        /// 진행률 업데이트 시간 제어 (UI 부하 감소)
        /// </summary>
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private const int PROGRESS_UPDATE_INTERVAL_MS = 200; // 200ms

        public RelationCheckProcessor(ILogger<RelationCheckProcessor> logger, 
            SpatialCheckPro.Models.GeometryCriteria geometryCriteria,
            ParallelProcessingManager? parallelProcessingManager = null,
            SpatialCheckPro.Models.Config.PerformanceSettings? performanceSettings = null,
            StreamingGeometryProcessor? streamingProcessor = null,
            IFeatureFilterService? featureFilterService = null)
        {
            _logger = logger;
            _geometryCriteria = geometryCriteria ?? SpatialCheckPro.Models.GeometryCriteria.CreateDefault();
            _parallelProcessingManager = parallelProcessingManager;
            _performanceSettings = performanceSettings ?? new SpatialCheckPro.Models.Config.PerformanceSettings();
            _streamingProcessor = streamingProcessor;
            _featureFilterService = featureFilterService ?? new FeatureFilterService(
                logger as ILogger<FeatureFilterService> ?? new LoggerFactory().CreateLogger<FeatureFilterService>(),
                _performanceSettings);
        }

        /// <summary>
        /// 마지막 실행에서 제외된 피처 수
        /// </summary>
        public int LastSkippedFeatureCount { get; private set; }

        /// <summary>
        /// 관계 검수 진행률 업데이트 이벤트
        /// </summary>
        public event EventHandler<RelationValidationProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 진행률 이벤트를 발생시킵니다 (시간 기반 제어로 UI 부하 감소)
        /// </summary>
        private void RaiseProgress(string ruleId, string caseType, long processedLong, long totalLong, bool completed = false, bool successful = true)
        {
            // 시간 기반 업데이트 제어 (너무 자주 업데이트하지 않음)
            var now = DateTime.Now;
            if (!completed && (now - _lastProgressUpdate).TotalMilliseconds < PROGRESS_UPDATE_INTERVAL_MS)
            {
                return; // 200ms 미만이면 스킵
            }
            _lastProgressUpdate = now;

            var processed = (int)Math.Min(int.MaxValue, Math.Max(0, processedLong));
            var total = (int)Math.Min(int.MaxValue, Math.Max(0, totalLong));
            var pct = total > 0 ? (int)Math.Min(100, Math.Round(processed * 100.0 / (double)total)) : (completed ? 100 : 0);
            
            var eventArgs = new RelationValidationProgressEventArgs
            {
                CurrentStage = RelationValidationStage.SpatialRelationValidation,
                StageName = string.IsNullOrWhiteSpace(caseType) ? "공간 관계 검수" : caseType,
                OverallProgress = pct,
                StageProgress = completed ? 100 : pct,
                StatusMessage = completed
                    ? $"규칙 {ruleId} 처리 완료 ({processed}/{total})"
                    : $"규칙 {ruleId} 처리 중... {processed}/{total}",
                CurrentRule = ruleId,
                ProcessedRules = processed,
                TotalRules = total,
                IsStageCompleted = completed,
                IsStageSuccessful = successful,
                ErrorCount = 0,
                WarningCount = 0
            };
            
            _logger.LogDebug("진행률 이벤트 발생: {RuleId}, {Progress}%, {Message}", ruleId, pct, eventArgs.StatusMessage);
            ProgressUpdated?.Invoke(this, eventArgs);
        }

        public async Task<ValidationResult> ProcessAsync(string filePath, RelationCheckConfig config, CancellationToken cancellationToken = default)
        {
            var overall = new ValidationResult { IsValid = true, Message = "관계 검수 완료" };
            
            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, FieldFilter={FieldFilter}", 
                config.RuleId, config.CaseType, config.FieldFilter);

            using var ds = Ogr.Open(filePath, 0);
            if (ds == null)
            {
                return new ValidationResult { IsValid = false, ErrorCount = 1, Message = "FileGDB를 열 수 없습니다" };
            }

            LastSkippedFeatureCount = 0;
            for (int i = 0; i < ds.GetLayerCount(); i++)
            {
                var datasetLayer = ds.GetLayerByIndex(i);
                if (datasetLayer == null)
                {
                    continue;
                }

                var layerName = datasetLayer.GetName() ?? $"Layer_{i}";
                var filterResult = _featureFilterService.ApplyObjectChangeFilter(datasetLayer, "Relation", layerName);
                if (filterResult.Applied && filterResult.ExcludedCount > 0)
                {
                    LastSkippedFeatureCount += filterResult.ExcludedCount;
                }
            }
            overall.SkippedCount = LastSkippedFeatureCount;

            // 레이어 헬퍼
            Layer? FindLayer(string name)
            {
                for (int i = 0; i < ds.GetLayerCount(); i++)
                {
                    var layer = ds.GetLayerByIndex(i);
                    if (layer == null) continue;
                    var lname = layer.GetName() ?? string.Empty;
                    if (string.Equals(lname, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return layer;
                    }
                }
                return null;
            }

            // CaseType 별 분기 (CSV 1행 단위)
            var caseType = (config.CaseType ?? string.Empty).Trim();
            var fieldFilter = (config.FieldFilter ?? string.Empty).Trim();
            if (caseType.Equals("PointInsidePolygon", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluateBuildingCenterPoints(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineWithinPolygon", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineWithinPolygonTolerance;
                await Task.Run(() => EvaluateCenterlineInRoadBoundary(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonNotWithinPolygon", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.PolygonNotWithinPolygonTolerance;
                await Task.Run(() => EvaluateArrfcMustBeInsideBoundary(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineConnectivity", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateLineConnectivity(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonMissingLine", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluateBoundaryMissingCenterline(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonNotOverlap", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? 0.0; // 면적 허용 오차
                await Task.Run(() => EvaluatePolygonNoOverlap(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonNotIntersectLine", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluatePolygonNotIntersectLine(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonNotContainPoint", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluatePolygonNotContainPoint(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("ConnectedLinesSameAttribute", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateConnectedLinesSameAttribute(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("CenterlineAttributeMismatch", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateCenterlineAttributeMismatch(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("ContourIntersection", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluateContourIntersection(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("ContourSharpBend", StringComparison.OrdinalIgnoreCase))
            {
                var angleThreshold = config.Tolerance ?? _geometryCriteria?.ContourSharpBendDefault ?? 90.0; // geometry_criteria.csv 또는 기본값 90도
                await Task.Run(() => EvaluateContourSharpBend(ds, FindLayer, overall, angleThreshold, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("RoadSharpBend", StringComparison.OrdinalIgnoreCase))
            {
                var angleThreshold = config.Tolerance ?? _geometryCriteria?.RoadSharpBendDefault ?? 6.0; // geometry_criteria.csv 또는 기본값 6도
                await Task.Run(() => EvaluateRoadSharpBend(ds, FindLayer, overall, angleThreshold, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("BridgeRiverNameMatch", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateBridgeRiverNameMatch(ds, FindLayer, overall, tol, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonWithinPolygon", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.PolygonNotWithinPolygonTolerance;
                await Task.Run(() => EvaluatePolygonWithinPolygon(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonContainsLine", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineWithinPolygonTolerance;
                await Task.Run(() => EvaluatePolygonContainsLine(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonContainsObjects", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? 0.0;
                await Task.Run(() => EvaluatePolygonContainsObjects(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("HoleDuplicateCheck", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.DuplicateCheckTolerance;
                await Task.Run(() => EvaluateHoleDuplicateCheck(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineConnectivityWithFilter", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateLineConnectivityWithFilter(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineDisconnection", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateLineDisconnection(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineDisconnectionWithAttribute", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateLineDisconnectionWithAttribute(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonBoundaryMatch", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineWithinPolygonTolerance;
                await Task.Run(() => EvaluatePolygonBoundaryMatch(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineEndpointWithinPolygon", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria?.LineEndpointDefault ?? 0.3; // geometry_criteria.csv 또는 기본값 0.3m
                await Task.Run(() => EvaluateLineEndpointWithinPolygon(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("DefectiveConnection", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineConnectivityTolerance;
                await Task.Run(() => EvaluateDefectiveConnection(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("LineIntersectionWithAttribute", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluateLineIntersectionWithAttribute(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PolygonIntersectionWithAttribute", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? 0.0;
                await Task.Run(() => EvaluatePolygonIntersectionWithAttribute(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("AttributeSpatialMismatch", StringComparison.OrdinalIgnoreCase))
            {
                var tol = config.Tolerance ?? _geometryCriteria.LineWithinPolygonTolerance;
                await Task.Run(() => EvaluateAttributeSpatialMismatch(ds, FindLayer, overall, fieldFilter, tol, cancellationToken, config), cancellationToken);
            }
            else if (caseType.Equals("PointSpacingCheck", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => EvaluatePointSpacingCheck(ds, FindLayer, overall, fieldFilter, cancellationToken, config), cancellationToken);
            }
            else
            {
                _logger.LogWarning("알 수 없는 CaseType: {CaseType}", caseType);
            }

            return overall;
        }

        public Task<ValidationResult> ValidateSpatialRelationsAsync(string filePath, RelationCheckConfig config, CancellationToken cancellationToken = default)
        {
            return ProcessAsync(filePath, config, cancellationToken);
        }

        public Task<ValidationResult> ValidateAttributeRelationsAsync(string filePath, RelationCheckConfig config, CancellationToken cancellationToken = default)
        {
            return ProcessAsync(filePath, config, cancellationToken);
        }

        public Task<ValidationResult> ValidateCrossTableRelationsAsync(string filePath, RelationCheckConfig config, CancellationToken cancellationToken = default)
        {
            return ProcessAsync(filePath, config, cancellationToken);
        }

        private static void AddError(ValidationResult result, string errType, string message, string table = "", string objectId = "", Geometry? geometry = null)
        {
            result.IsValid = false;
            result.ErrorCount += 1;
            
            var (x, y) = ExtractCentroid(geometry);
            result.Errors.Add(new ValidationError
            {
                ErrorCode = errType,
                Message = message,
                TableName = table,
                FeatureId = objectId,
                SourceTable = table,
                SourceObjectId = long.TryParse(objectId, NumberStyles.Any, CultureInfo.InvariantCulture, out var oid) ? oid : null,
                Severity = Models.Enums.ErrorSeverity.Error,
                X = x,
                Y = y,
                GeometryWKT = QcError.CreatePointWKT(x, y)
            });
        }

        /// <summary>
        /// 더 상세한 오류 정보를 포함한 오류 추가 (지오메트리 정보 포함)
        /// </summary>
        private static void AddDetailedError(ValidationResult result, string errType, string message, string table = "", string objectId = "", string additionalInfo = "", Geometry? geometry = null)
        {
            result.IsValid = false;
            result.ErrorCount += 1;
            
            var fullMessage = string.IsNullOrWhiteSpace(additionalInfo) ? message : $"{message} ({additionalInfo})";
            var (x, y) = ExtractCentroid(geometry);
            
            result.Errors.Add(new ValidationError
            {
                ErrorCode = errType,
                Message = fullMessage,
                TableName = table,
                FeatureId = objectId,
                SourceTable = table,
                SourceObjectId = long.TryParse(objectId, NumberStyles.Any, CultureInfo.InvariantCulture, out var oid) ? oid : null,
                Severity = Models.Enums.ErrorSeverity.Error,
                X = x,
                Y = y,
                GeometryWKT = QcError.CreatePointWKT(x, y)
            });
        }

        /// <summary>
        /// 지오메트리에서 중심점 좌표를 추출합니다
        /// - Polygon: PointOnSurface (내부 보장) → Centroid → Envelope 중심
        /// - Line: 중간 정점
        /// - Point: 그대로
        /// </summary>
        private static (double X, double Y) ExtractCentroid(Geometry? geometry)
        {
            if (geometry == null)
                return (0, 0);

            try
            {
                var geomType = geometry.GetGeometryType();
                var flatType = (wkbGeometryType)((int)geomType & 0xFF); // Flatten to 2D type

                // Polygon 또는 MultiPolygon: 내부 중심점 사용
                if (flatType == wkbGeometryType.wkbPolygon || flatType == wkbGeometryType.wkbMultiPolygon)
                {
                    return GeometryCoordinateExtractor.GetPolygonInteriorPoint(geometry);
                }

                // LineString 또는 MultiLineString: 중간 정점
                if (flatType == wkbGeometryType.wkbLineString || flatType == wkbGeometryType.wkbMultiLineString)
                {
                    return GeometryCoordinateExtractor.GetLineStringMidpoint(geometry);
                }

                // Point: 첫 번째 정점
                if (flatType == wkbGeometryType.wkbPoint || flatType == wkbGeometryType.wkbMultiPoint)
                {
                    return GeometryCoordinateExtractor.GetFirstVertex(geometry);
                }

                // 기타: Envelope 중심
                return GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// 레이어에서 OID로 Feature를 조회하여 Geometry를 반환합니다
        /// </summary>
        private static Geometry? GetGeometryByOID(Layer? layer, long oid)
        {
            if (layer == null)
                return null;

            try
            {
                layer.SetAttributeFilter($"OBJECTID = {oid}");
                layer.ResetReading();
                var feature = layer.GetNextFeature();
                layer.SetAttributeFilter(null);

                if (feature != null)
                {
                    using (feature)
                    {
                        var geometry = feature.GetGeometryRef();
                        return geometry?.Clone();
                    }
                }
            }
            catch
            {
                layer.SetAttributeFilter(null);
            }

            return null;
        }

        /// <summary>
        /// 지오메트리에서 WKT 문자열을 추출합니다
        /// </summary>
        private static string? ExtractWktFromGeometry(Geometry? geometry)
        {
            if (geometry == null) return null;
            
            try
            {
                string wkt;
                var result = geometry.ExportToWkt(out wkt);
                return result == 0 ? wkt : null; // OGRERR_NONE = 0
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 건물-중심점 관계 검사 (역 접근법 최적화)
        /// - 기존: 건물 순회 → 점 검색 (느림)
        /// - 최적화: 점 순회 → 건물 검색 (빠름, Union 불필요)
        /// </summary>
        private void EvaluateBuildingCenterPoints(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var buld = getLayer(config.MainTableId);
            var ctpt = getLayer(config.RelatedTableId);
            if (buld == null || ctpt == null)
            {
                _logger.LogWarning("Case1: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            // 필드 필터 적용 (RelatedTableId에만 적용: TN_BULD_CTPT)
            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(ctpt, fieldFilter);

            _logger.LogInformation("건물중심점 검사 시작 (역 접근법 최적화: 점→건물 순서)");
            var startTime = DateTime.Now;

            // 1단계: 모든 건물 ID 수집 (빠름, O(N))
            var allBuildingIds = new HashSet<long>();
            buld.ResetReading();
            Feature? f;
            while ((f = buld.GetNextFeature()) != null)
            {
                using (f)
                {
                    allBuildingIds.Add(f.GetFID());
                }
            }
            
            _logger.LogInformation("건물 ID 수집 완료: {Count}개", allBuildingIds.Count);

            // 2단계: 점을 순회하며 포함하는 건물 찾기 (역 접근법, O(M log N))
            var buildingsWithPoints = new HashSet<long>();
            var pointsOutsideBuildings = new List<long>();
            
            ctpt.ResetReading();
            var pointCount = ctpt.GetFeatureCount(1);
            var processedPoints = 0;
            
            Feature? pf;
            while ((pf = ctpt.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedPoints++;
                
                if (processedPoints % 50 == 0 || processedPoints == pointCount)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                        processedPoints, pointCount);
                }
                
                using (pf)
                {
                    var pg = pf.GetGeometryRef();
                    if (pg == null) continue;
                    
                    var pointOid = pf.GetFID();
                    
                    // 점 위치로 공간 필터 설정 (GDAL 내부 공간 인덱스 활용)
                    buld.SetSpatialFilter(pg);
                    
                    bool foundBuilding = false;
                    Feature? bf;
                    while ((bf = buld.GetNextFeature()) != null)
                    {
                        using (bf)
                        {
                            var bg = bf.GetGeometryRef();
                            if (bg != null && (pg.Within(bg) || bg.Contains(pg)))
                            {
                                buildingsWithPoints.Add(bf.GetFID());
                                foundBuilding = true;
                                break; // 하나만 찾으면 충분
                            }
                        }
                    }
                    
                    if (!foundBuilding)
                    {
                        pointsOutsideBuildings.Add(pointOid);
                    }
                    
                    buld.ResetReading();
                }
            }
            
            buld.SetSpatialFilter(null);
            
            _logger.LogInformation("점→건물 검사 완료: 점 {PointCount}개 처리, 건물 매칭 {MatchCount}개", 
                pointCount, buildingsWithPoints.Count);

            // 3단계: 점이 없는 건물 찾기 (집합 차집합, O(N))
            var buildingsWithoutPoints = allBuildingIds.Except(buildingsWithPoints).ToList();
            
            foreach (var bldOid in buildingsWithoutPoints)
            {
                var geometry = GetGeometryByOID(buld, bldOid);
                AddError(result, "REL_BULD_CTPT_MISSING", 
                    "건물 내 건물중심점이 없습니다", 
                    config.MainTableId, bldOid.ToString(CultureInfo.InvariantCulture), geometry);
                geometry?.Dispose();
            }
            
            // 4단계: 건물 밖 점 오류 추가
            foreach (var ptOid in pointsOutsideBuildings)
            {
                var geometry = GetGeometryByOID(ctpt, ptOid);
                AddError(result, "REL_CTPT_OUTSIDE_BULD", 
                    "건물 외부에 건물중심점이 존재합니다", 
                    config.RelatedTableId, ptOid.ToString(CultureInfo.InvariantCulture), geometry);
                geometry?.Dispose();
            }
            
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("건물중심점 검사 완료 (역 접근법 최적화): 건물 {BuildingCount}개, 점 {PointCount}개, " +
                "점 없는 건물 {MissingCount}개, 밖 점 {OutsideCount}개, 소요시간: {Elapsed:F2}초 " +
                "(Union 생성 없음, SetSpatialFilter 활용)", 
                allBuildingIds.Count, pointCount, buildingsWithoutPoints.Count, 
                pointsOutsideBuildings.Count, elapsed);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                pointCount, pointCount, completed: true);
        }

        private void EvaluateCenterlineInRoadBoundary(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var boundary = getLayer(config.MainTableId);
            var centerline = getLayer(config.RelatedTableId);
            if (boundary == null || centerline == null)
            {
                _logger.LogWarning("Case2: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            // 필드 필터 적용 (RelatedTableId에만 적용: TN_RODWAY_CTLN)
            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(centerline, fieldFilter);

            var boundaryUnion = BuildUnionGeometryWithCache(boundary, $"{config.MainTableId}_UNION");
            if (boundaryUnion == null) return;

            // 위상 정리: MakeValid 사용
            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            // 필터 적용 후 피처 개수 확인
            centerline.ResetReading();
            var totalFeatures = centerline.GetFeatureCount(1);
            _logger.LogInformation("도로중심선 필터 적용 후 피처 수: {Count}, 원본필터: {Filter}", totalFeatures, fieldFilter);
            
            // 필터 적용 전후 비교를 위한 로깅
            centerline.ResetReading();
            var sampleRoadSeValues = new List<string>();
            Feature? sampleF;
            int sampleCount = 0;
            while ((sampleF = centerline.GetNextFeature()) != null && sampleCount < 10)
            {
                using (sampleF)
                {
                    var roadSe = sampleF.GetFieldAsString("road_se") ?? string.Empty;
                    sampleRoadSeValues.Add(roadSe);
                    sampleCount++;
                }
            }
            _logger.LogInformation("필터 적용 후 샘플 ROAD_SE 값들: {Values}", string.Join(", ", sampleRoadSeValues));

            centerline.ResetReading();
            Feature? lf;
            var processedCount = 0;
            while ((lf = centerline.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                if (processedCount % 50 == 0 || processedCount == totalFeatures)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, totalFeatures);
                }
                using (lf)
                {
                    var lg = lf.GetGeometryRef();
                    if (lg == null) continue;

                    var oid = lf.GetFID().ToString(CultureInfo.InvariantCulture);
                    var roadSe = lf.GetFieldAsString("road_se") ?? string.Empty;
                    
                    _logger.LogDebug("도로중심선 피처 처리: OID={OID}, ROAD_SE={RoadSe}", oid, roadSe);
                    
                    // 선형 객체가 면형 객체 영역을 벗어나는지 검사
                    bool isWithinTolerance = false;
                    try
                    {
                        // 1차: Difference로 경계 밖 길이 계산
                        using var diff = lg.Difference(boundaryUnion);
                        double outsideLength = 0.0;
                        if (diff != null && !diff.IsEmpty())
                        {
                            outsideLength = Math.Abs(diff.Length());
                        }

                        // 2차: 경계면 경계선과의 거리 기반 허용오차 보정
                        if (outsideLength > 0 && tolerance > 0)
                        {
                            using var boundaryLines = boundaryUnion.GetBoundary();
                            // 선의 모든 점이 경계선으로부터 tolerance 이내면 허용
                            bool allNear = IsLineWithinPolygonWithTolerance(lg, boundaryUnion, tolerance);
                            isWithinTolerance = allNear && outsideLength <= tolerance; // 길이도 허용오차 이내로 허용
                        }
                        else
                        {
                            // 밖으로 나간 길이가 거의 없는 경우 통과
                            isWithinTolerance = outsideLength <= tolerance;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "도로중심선 Within 검사 중 오류: OID={OID}", oid);
                        isWithinTolerance = false;
                    }

                    // 필터를 통과한 피처들은 검수 대상이므로, 허용오차를 초과하여 벗어나면 오류
                    if (!isWithinTolerance)
                    {
                        _logger.LogDebug("도로중심선 오류 검출: OID={OID}, ROAD_SE={RoadSe} - 허용오차를 초과하여 경계면을 벗어남", oid, roadSe);
                        AddDetailedError(result, "REL_CTLN_OUTSIDE_BNDRY", 
                            "도로중심선이 도로경계면을 허용오차를 초과하여 벗어났습니다", 
                            config.RelatedTableId, oid, 
                            $"ROAD_SE={roadSe}, 허용오차={tolerance}m", lg);
                    }
                    else
                    {
                        _logger.LogDebug("도로중심선 정상: OID={OID}, ROAD_SE={RoadSe} - 허용오차 내에서 경계면 내부에 있음", oid, roadSe);
                    }
                }
            }
            
            _logger.LogInformation("도로중심선 관계 검수 완료: 처리된 피처 수 {ProcessedCount}, 오류 수 {ErrorCount}", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, totalFeatures, totalFeatures, completed: true);
            
            // 최종 검수 결과 로깅
            if (result.ErrorCount > 0)
            {
                _logger.LogWarning("도로중심선 관계 검수에서 {ErrorCount}개 오류 발견!", result.ErrorCount);
                foreach (var error in result.Errors.Where(e => e.ErrorCode == "REL_CTLN_OUTSIDE_BNDRY"))
                {
                    _logger.LogWarning("오류 상세: {Message}", error.Message);
                }
            }
            else
            {
                _logger.LogInformation("도로중심선 관계 검수: 오류 없음 (필터 정상 작동)");
            }
        }

        private void EvaluateArrfcMustBeInsideBoundary(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var arrfc = getLayer(config.MainTableId);
            var boundary = getLayer(config.RelatedTableId);
            if (arrfc == null || boundary == null)
            {
                _logger.LogWarning("Case3: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            // 필드 필터 적용 (MainTableId에만 적용: TN_ARRFC)
            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(arrfc, fieldFilter);

            var boundaryUnion = BuildUnionGeometryWithCache(boundary, $"{config.RelatedTableId}_UNION");
            if (boundaryUnion == null) return;

            // 위상 정리: MakeValid 사용
            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            // 필터가 이미 적용되어 있으므로 (PG_RDFC_SE IN (...)) 
            // 필터를 통과한 피처만 처리됨
            arrfc.ResetReading();
            var totalFeatures = arrfc.GetFeatureCount(1);
            _logger.LogInformation("면형도로시설 필터 적용 후 피처 수: {Count}, 필터: {Filter}", totalFeatures, fieldFilter);

            arrfc.ResetReading();
            Feature? pf;
            var processedCount = 0;
            while ((pf = arrfc.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                if (processedCount % 50 == 0 || processedCount == totalFeatures)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, totalFeatures);
                }
                using (pf)
                {
                    var pg = pf.GetGeometryRef();
                    if (pg == null) continue;

                    var code = pf.GetFieldAsString("PG_RDFC_SE") ?? string.Empty;
                    var oid = pf.GetFID().ToString(CultureInfo.InvariantCulture);
                    _logger.LogDebug("면형도로시설 검증: OID={OID}, PG_RDFC_SE={Code}", oid, code);

                    // 버텍스 일치 검사: 면형도로시설의 버텍스가 도로경계면의 버텍스와 일치하는지 확인
                    bool verticesMatch = false;
                    try
                    {
                        verticesMatch = ArePolygonVerticesAlignedWithBoundary(pg, boundaryUnion, tolerance);

                        // 추가 보정: 경계선과 실제로 접해있는 구간이 있는 경우만 검사 대상
                        if (verticesMatch)
                        {
                            using var inter = pg.GetBoundary()?.Intersection(boundaryUnion.GetBoundary());
                            if (inter == null || inter.IsEmpty() || inter.Length() <= 0)
                            {
                                // 경계 접합이 전혀 없으면 이 규칙 비대상 → 통과 처리
                                verticesMatch = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "면형도로시설 버텍스 일치 검사 중 오류: OID={OID}", oid);
                        verticesMatch = false;
                    }

                    if (!verticesMatch)
                    {
                        _logger.LogDebug("면형도로시설 오류 검출: OID={OID}, PG_RDFC_SE={Code} - 버텍스가 경계면과 일치하지 않음", oid, code);
                        AddDetailedError(result, "REL_ARRFC_VERTEX_MISMATCH", 
                            $"면형도로시설의 버텍스가 도로경계면의 버텍스와 일치하지 않습니다", 
                            config.MainTableId, oid, 
                            $"PG_RDFC_SE={code}, 허용오차={tolerance}m", pg);
                    }
                }
            }
            
            _logger.LogInformation("면형도로시설 버텍스 일치 검수 완료: 처리된 피처 수 {ProcessedCount}, 오류 수 {ErrorCount}", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, totalFeatures, totalFeatures, completed: true);
            
            // 최종 검수 결과 로깅
            if (result.ErrorCount > 0)
            {
                _logger.LogWarning("면형도로시설 버텍스 일치 검수에서 {ErrorCount}개 오류 발견!", result.ErrorCount);
                foreach (var error in result.Errors.Where(e => e.ErrorCode == "REL_ARRFC_VERTEX_MISMATCH"))
                {
                    _logger.LogWarning("오류 상세: {Message}", error.Message);
                }
            }
            else
            {
                _logger.LogInformation("면형도로시설 버텍스 일치 검수: 오류 없음 (필터 정상 작동)");
            }
        }

        /// <summary>
        /// 폴리곤 경계선과 도로경계선이 중첩되는 구간에서 폴리곤 버텍스가 경계선과 일치하는지 검사
        /// - 경계선과 멀리 떨어진(중첩되지 않는) 버텍스는 검사 대상에서 제외
        /// - 엣지 중간점 검사는 제거 (버텍스 기준)
        /// </summary>
        private bool AreOverlappingVerticesAlignedWithBoundary(Geometry polygon, Geometry boundaryLines, double tolerance)
        {
            if (polygon == null || boundaryLines == null) return false;
            using var polyBoundary = polygon.GetBoundary();
            if (polyBoundary == null) return false;

            // 1) 중첩 여부 판단: 경계와의 교차가 없으면 이 규칙 적용 대상 아님 → 통과
            using var inter = polyBoundary.Intersection(boundaryLines);
            if (inter == null || inter.IsEmpty() || inter.Length() <= 0)
            {
                return true; // 중첩 구간 없음 → 이 규칙 비대상
            }

            // 2) 중첩 구간이 있는 경우: 경계선에 근접한 버텍스는 모두 일치해야 함
            var pointCount = polyBoundary.GetPointCount();
            var proximity = Math.Max(tolerance * 5.0, tolerance + 1e-9);

            for (int i = 0; i < pointCount; i++)
            {
                var x = polyBoundary.GetX(i);
                var y = polyBoundary.GetY(i);

                using var pt = new Geometry(wkbGeometryType.wkbPoint);
                pt.AddPoint(x, y, 0);

                var dist = pt.Distance(boundaryLines);
                if (dist <= proximity)
                {
                    if (dist > tolerance)
                    {
                        _logger.LogDebug("버텍스-경계 불일치: ({X},{Y}), dist={Dist:F6}", x, y, dist);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 면형도로시설의 버텍스가 도로경계면의 버텍스와 일치하는지 검사
        /// - 면형도로시설의 모든 버텍스가 도로경계면의 경계선 위에 있는지 확인
        /// - 허용오차 내에서 버텍스가 경계선과 일치해야 함
        /// - 면형 객체의 버텍스 좌표가 면형 객체 영역의 버텍스 좌표와 일치하는지 검사
        /// </summary>
        private bool ArePolygonVerticesAlignedWithBoundary(Geometry polygon, Geometry boundary, double tolerance)
        {
            if (polygon == null || boundary == null) return false;

            try
            {
                // 도로경계면의 경계선(라인스트링 집합) 추출
                using var boundaryLines = boundary.GetBoundary();
                if (boundaryLines == null) return false;

                // 면형도로시설의 모든 외곽 링 버텍스를 순회 (멀티폴리곤 포함)
                int parts = polygon.GetGeometryType() == wkbGeometryType.wkbMultiPolygon
                    ? polygon.GetGeometryCount()
                    : 1;

                // 단일 폴리곤인 경우에도 for 루프를 동일하게 처리하기 위해 래핑
                for (int p = 0; p < parts; p++)
                {
                    Geometry? polyPart = polygon.GetGeometryType() == wkbGeometryType.wkbMultiPolygon
                        ? polygon.GetGeometryRef(p)
                        : polygon;

                    if (polyPart == null || polyPart.GetGeometryType() != wkbGeometryType.wkbPolygon)
                        continue;

                    // 외곽 링(0)만 검사 (요구사항: 경계 정합)
                    var exterior = polyPart.GetGeometryRef(0);
                    if (exterior == null) continue;

                    int vertexCount = exterior.GetPointCount();
                    for (int i = 0; i < vertexCount; i++)
                    {
                        double x = exterior.GetX(i);
                        double y = exterior.GetY(i);
                        using var pt = new Geometry(wkbGeometryType.wkbPoint);
                        pt.AddPoint(x, y, 0);

                        double dist = pt.Distance(boundaryLines);
                        if (dist > tolerance)
                        {
                            _logger.LogDebug("버텍스 불일치: ({X},{Y}), 경계선까지 거리={Dist:F6} > Tol={Tol}", x, y, dist, tolerance);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "면형도로시설 버텍스 일치 검사 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// 폴리곤의 모든 버텍스와 엣지가 경계면 위에 정확히 있는지 검증 (구버전)
        /// </summary>
        /// <remarks>
        /// 현재 미사용 메서드이나 향후 사용을 대비하여 tolerance 기본값을 GeometryCriteria에서 가져오도록 수정
        /// </remarks>
        private bool IsPolygonStrictlyOnBoundary(Geometry polygon, Geometry boundary, double? tolerance = null)
        {
            // 유지: 다른 케이스에서 사용할 수 있으니 남겨두되, 현재 Case3에서는 사용하지 않음
            // tolerance가 지정되지 않으면 GeometryCriteria의 PolygonNotWithinPolygonTolerance 사용
            var actualTolerance = tolerance ?? _geometryCriteria.PolygonNotWithinPolygonTolerance;
            
            var exteriorRing = polygon.GetBoundary();
            if (exteriorRing == null) return false;

            var pointCount = exteriorRing.GetPointCount();
            for (int i = 0; i < pointCount; i++)
            {
                var x = exteriorRing.GetX(i);
                var y = exteriorRing.GetY(i);
                using var point = new Geometry(wkbGeometryType.wkbPoint);
                point.AddPoint(x, y, 0);
                var distance = point.Distance(boundary);
                if (distance > actualTolerance) return false;
            }

            return true;
        }

        /// <summary>
        /// 레이어의 모든 지오메트리를 Union하여 반환합니다 (스트리밍 최적화 버전)
        /// </summary>
        private Geometry? BuildUnionGeometryWithCache(Layer layer, string cacheKey)
        {
            // 캐시 확인
            if (_unionGeometryCache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogInformation("Union 캐시 히트: {Key} (성능 최적화 적용)", cacheKey);
                return cached;
            }
            
            _logger.LogInformation("Union 지오메트리 생성 시작: {Key}", cacheKey);
            var startTime = DateTime.Now;
            
            // 만료된 캐시 정리 (메모리 최적화)
            if (_unionGeometryCache.Count > 5)
            {
                ClearExpiredCache(TimeSpan.FromMinutes(15)); // 15분 이상 된 캐시 정리
            }
            
            Geometry? union = null;
            
            // 스트리밍 프로세서가 있으면 스트리밍 방식 사용
            if (_streamingProcessor != null)
            {
                _logger.LogInformation("스트리밍 방식으로 Union 생성: {Key}", cacheKey);
                union = _streamingProcessor.CreateUnionGeometryStreaming(layer, null);
            }
            else
            {
                // 기존 방식 (fallback)
                _logger.LogInformation("기존 방식으로 Union 생성: {Key}", cacheKey);
                union = BuildUnionGeometryLegacy(layer);
            }
            
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            var featureCount = (int)layer.GetFeatureCount(1);
            _logger.LogInformation("Union 지오메트리 생성 완료: {Key}, {Count}개 피처, 소요시간: {Elapsed:F2}초", 
                cacheKey, featureCount, elapsed);
            
            // 캐시 저장 (타임스탬프와 함께)
            _unionGeometryCache[cacheKey] = union;
            _cacheTimestamps[cacheKey] = DateTime.Now;
            
            // 메모리 사용량 모니터링 및 경고
            if (_unionGeometryCache.Count > 10)
            {
                _logger.LogWarning("Union 캐시 항목 수 과다: {Count}개, 메모리 사용량 주의", _unionGeometryCache.Count);
            }
            
            return union;
        }
        
        /// <summary>
        /// 기존 방식으로 Union 지오메트리 생성 (fallback)
        /// </summary>
        private Geometry? BuildUnionGeometryLegacy(Layer layer)
        {
            layer.ResetReading();
            var geometries = new List<Geometry>();
            Feature? f;
            int featureCount = 0;
            
            // 모든 지오메트리 수집
            while ((f = layer.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g != null) 
                    {
                        geometries.Add(g.Clone());
                        featureCount++;
                        
                        if (featureCount % 1000 == 0)
                        {
                            _logger.LogDebug("Union 지오메트리 수집 중: {Count}개 처리됨", featureCount);
                        }
                    }
                }
            }
            
            if (geometries.Count == 0)
            {
                _logger.LogWarning("Union 대상 지오메트리 없음");
                return null;
            }
            
            if (geometries.Count == 1)
            {
                _logger.LogInformation("단일 지오메트리 Union");
                return geometries[0];
            }
            
            // UnaryUnion 사용 (GEOS 최적화 알고리즘)
            try
            {
                _logger.LogDebug("UnaryUnion 시작: {Count}개 지오메트리", geometries.Count);
                
                var collection = new Geometry(wkbGeometryType.wkbGeometryCollection);
                foreach (var g in geometries)
                {
                    collection.AddGeometry(g);
                }
                var union = collection.UnaryUnion();
                
                _logger.LogDebug("UnaryUnion 성공");
                
                // 임시 지오메트리 객체들 정리
                foreach (var g in geometries)
                {
                    g?.Dispose();
                }
                
                return union;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UnaryUnion 실패, 순차 Union으로 폴백");
                
                // 폴백: 순차 Union (안전하지만 느림)
                var union = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    try
                    {
                        var newUnion = union.Union(geometries[i]);
                        union.Dispose();
                        union = newUnion;
                    }
                    catch (Exception unionEx)
                    {
                        _logger.LogWarning(unionEx, "순차 Union 실패 (인덱스 {Index})", i);
                    }
                }
                
                // 임시 지오메트리 객체들 정리
                foreach (var g in geometries.Skip(1))
                {
                    g?.Dispose();
                }
                
                return union;
            }
        }

        /// <summary>
        /// Union 캐시 정리 (메모리 최적화)
        /// </summary>
        public void ClearUnionCache()
        {
            var clearedCount = _unionGeometryCache.Count;
            
            // 캐시된 Geometry 객체들 명시적 해제
            foreach (var geometry in _unionGeometryCache.Values)
            {
                geometry?.Dispose();
            }
            
            _unionGeometryCache.Clear();
            _cacheTimestamps.Clear();
            
            if (clearedCount > 0)
            {
                _logger.LogInformation("Union 캐시 정리 완료: {Count}개 항목 해제", clearedCount);
            }
        }
        
        /// <summary>
        /// 오래된 캐시 항목 정리 (30분 이상 된 항목)
        /// </summary>
        public void ClearExpiredCache(TimeSpan? maxAge = null)
        {
            var cutoffTime = DateTime.Now - (maxAge ?? TimeSpan.FromMinutes(30));
            var expiredKeys = _cacheTimestamps
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expiredKeys)
            {
                if (_unionGeometryCache.TryGetValue(key, out var geometry))
                {
                    geometry?.Dispose();
                    _unionGeometryCache.Remove(key);
                }
                _cacheTimestamps.Remove(key);
            }
            
            if (expiredKeys.Count > 0)
            {
                _logger.LogInformation("만료된 Union 캐시 정리: {Count}개 항목 해제", expiredKeys.Count);
            }
        }
        
        /// <summary>
        /// 레거시 메서드 호환성 유지
        /// </summary>
        private static Geometry? BuildUnionGeometry(Layer layer)
        {
            layer.ResetReading();
            Geometry? union = null;
            Feature? f;
            while ((f = layer.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;
                    union = union == null ? g.Clone() : union.Union(g);
                }
            }
            return union;
        }

        /// <summary>
        /// 레이어의 필드 목록을 확인해 필터에 등장하는 필드가 하나라도 있으면 AttributeFilter를 적용하고, 해제용 IDisposable을 반환합니다.
        /// 필터가 비어있으면 아무 것도 하지 않습니다.
        /// </summary>
        private IDisposable ApplyAttributeFilterIfMatch(Layer layer, string fieldFilter)
        {
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogDebug("AttributeFilter 스킵: 필터가 비어있음");
                return new ActionOnDispose(() => { });
            }

            try
            {
                var fieldNames = GetFieldNames(layer);
                var usedIds = GetExpressionIdentifiers(fieldFilter);
                
                _logger.LogDebug("필터 파싱 결과: Layer={Layer}, Filter='{Filter}', 사용된 필드={UsedFields}, 레이어 필드={LayerFields}", 
                    layer.GetName(), fieldFilter, string.Join(",", usedIds), string.Join(",", fieldNames));
                
                if (!usedIds.All(id => fieldNames.Contains(id)))
                {
                    // 1차: 대소문자 차이로 인한 미스매치일 수 있으므로 필드명 재매핑을 시도
                    var fieldMap = fieldNames.ToDictionary(fn => fn.ToLowerInvariant(), fn => fn, StringComparer.OrdinalIgnoreCase);
                    string RemapIdentifiers(string filter)
                    {
                        // 간단한 토큰 치환: 사용된 식별자만 정확 매핑
                        var remapped = filter;
                        foreach (var uid in usedIds)
                        {
                            var key = uid.ToLowerInvariant();
                            if (fieldMap.TryGetValue(key, out var actual))
                            {
                                // 단어 경계에서만 치환
                                remapped = Regex.Replace(remapped, $@"(?i)\b{Regex.Escape(uid)}\b", actual);
                            }
                        }
                        return remapped;
                    }

                    var remappedFilter = RemapIdentifiers(fieldFilter);
                    var stillMissing = GetExpressionIdentifiers(remappedFilter).Any(id => !fieldNames.Contains(id));
                    if (!stillMissing)
                    {
                        _logger.LogInformation("필드명 대소문자 자동 보정 적용: '{Original}' -> '{Remapped}'", fieldFilter, remappedFilter);
                        fieldFilter = remappedFilter;
                    }
                    else
                    {
                        var missing2 = string.Join(",", GetExpressionIdentifiers(remappedFilter).Where(id => !fieldNames.Contains(id)));
                        _logger.LogInformation("AttributeFilter 스킵: Layer={Layer}, Filter='{Filter}' - 존재하지 않는 필드: {Missing}", layer.GetName(), fieldFilter, missing2);
                        return new ActionOnDispose(() => { });
                    }
                }

                // 필터 정규화 (IN/NOT IN 목록 문자열에 따옴표 자동 보정 등)
                var normalized = NormalizeFilterExpression(fieldFilter);

                // 필터 적용 전 피처 수 확인
                layer.SetAttributeFilter(null);
                var beforeCount = layer.GetFeatureCount(1);
                
                // 필터 적용 (예외 처리 포함)
                int rc = 6; // 기본값: OGRERR_FAILURE
                long afterCount = 0;
                try
                {
                    rc = layer.SetAttributeFilter(normalized);
                    afterCount = layer.GetFeatureCount(1);
                    _logger.LogInformation("AttributeFilter 적용: Layer={Layer}, Filter='{Filter}' -> Normalized='{Norm}', RC={RC}, 전체={Before} -> 필터후={After}", 
                        layer.GetName(), fieldFilter, normalized, rc, beforeCount, afterCount);
                }
                catch (Exception setFilterEx)
                {
                    _logger.LogWarning(setFilterEx, "SetAttributeFilter 호출 중 예외 발생: Layer={Layer}, Filter='{Filter}' -> Normalized='{Norm}'", 
                        layer.GetName(), fieldFilter, normalized);
                    // 필터 해제 후 재시도하지 않고 계속 진행
                    layer.SetAttributeFilter(null);
                    afterCount = beforeCount; // 필터 적용 실패 시 전체 피처 수로 설정
                }
                
                // 필터 적용 후 샘플 데이터 확인 (필드 존재 여부 확인)
                if (afterCount > 0)
                {
                    layer.ResetReading();
                    var sampleValues = new List<string>();
                    Feature? sampleF;
                    int sampleCount = 0;
                    
                    // 필터에 사용된 필드명 추출
                    var filterFieldName = ExtractFirstFieldNameFromFilter(normalized);
                    
                    while ((sampleF = layer.GetNextFeature()) != null && sampleCount < 5)
                    {
                        using (sampleF)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(filterFieldName))
                                {
                                    var fieldValue = GetFieldValueSafe(sampleF, filterFieldName);
                                    sampleValues.Add(fieldValue ?? "NULL");
                                }
                                sampleCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "샘플 데이터 확인 중 오류");
                            }
                        }
                    }
                    
                    if (sampleValues.Any())
                    {
                        _logger.LogInformation("필터 적용 후 샘플 {Field} 값들: {Values}", 
                            filterFieldName, string.Join(", ", sampleValues));
                    }
                }
                
                // SetAttributeFilter 반환코드 의미:
                // 0 = OGRERR_NONE: 성공
                // 1 = OGRERR_NOT_ENOUGH_DATA
                // 2 = OGRERR_NOT_ENOUGH_MEMORY
                // 3 = OGRERR_UNSUPPORTED_GEOMETRY_TYPE
                // 4 = OGRERR_UNSUPPORTED_OPERATION
                // 5 = OGRERR_CORRUPT_DATA
                // 6 = OGRERR_FAILURE
                if (rc != 0)
                {
                    _logger.LogWarning("SetAttributeFilter 실패: 반환코드={RC}, 필터='{Filter}'", rc, normalized);
                }
                else
                {
                    _logger.LogDebug("SetAttributeFilter 성공: 필터가 정상적으로 적용됨");
                }

                // 재시도 로직: 필터 적용 후 카운트가 0이고, 원본 카운트는 0이 아닌 경우 경고 로그
                if (beforeCount > 0 && afterCount == 0)
                {
                    _logger.LogWarning("AttributeFilter 적용 결과 0건: Layer={Layer}, Filter='{Filter}', Before={Before}, After={After}", layer.GetName(), normalized, beforeCount, afterCount);
                }
                
                return new ActionOnDispose(() =>
                {
                    layer.SetAttributeFilter(null);
                    _logger.LogDebug("AttributeFilter 해제: Layer={Layer}", layer.GetName());
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AttributeFilter 적용 실패: Layer={Layer}, Filter='{Filter}'", layer.GetName(), fieldFilter);
                return new ActionOnDispose(() => { });
            }
        }

        private static HashSet<string> GetFieldNames(Layer layer)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defn = layer.GetLayerDefn();
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                set.Add(fd.GetName());
            }
            return set;
        }
        
        private static string? GetFieldValueSafe(Feature feature, string fieldName)
        {
            try
            {
                var defn = feature.GetDefnRef();
                for (int i = 0; i < defn.GetFieldCount(); i++)
                {
                    using var fd = defn.GetFieldDefn(i);
                    if (fd.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        return feature.IsFieldNull(i) ? null : feature.GetFieldAsString(i);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        private static string ExtractFirstFieldNameFromFilter(string filter)
        {
            try
            {
                // "field_name IN (...)" 또는 "field_name = value" 패턴에서 필드명 추출
                var match = Regex.Match(filter, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s+(?:IN|=|<>|>=|<=|>|<)", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HashSet<string> GetExpressionIdentifiers(string filter)
        {
            // 연산자(=, <>, >=, <=, >, <, IN, NOT IN, LIKE) 좌측에 오는 토큰만 필드로 간주
            var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1) IN / NOT IN 패턴 처리: "FIELD IN (...)" 형태의 FIELD만 수집
                foreach (Match m in Regex.Matches(filter, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+NOT\s+IN\s*\(", RegexOptions.Compiled))
                {
                    identifiers.Add(m.Groups[1].Value);
                }
                foreach (Match m in Regex.Matches(filter, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+IN\s*\(", RegexOptions.Compiled))
                {
                    identifiers.Add(m.Groups[1].Value);
                }

                // 2) 이항 연산자(=, <>, >=, <=, >, <, LIKE) 패턴 처리
                foreach (Match m in Regex.Matches(filter, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s*(=|<>|>=|<=|>|<|LIKE)\b", RegexOptions.Compiled))
                {
                    identifiers.Add(m.Groups[1].Value);
                }

                // 3) 추가 패턴: 괄호 없이 사용되는 IN/NOT IN (예: "field IN value1,value2")
                foreach (Match m in Regex.Matches(filter, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+NOT\s+IN\s+[^=<>]", RegexOptions.Compiled))
                {
                    identifiers.Add(m.Groups[1].Value);
                }
                foreach (Match m in Regex.Matches(filter, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+IN\s+[^=<>]", RegexOptions.Compiled))
                {
                    identifiers.Add(m.Groups[1].Value);
                }
            }
            catch
            {
                // 파싱 실패 시, 보수적으로 전체 토큰에서 키워드 제거 (기존 로직 폴백)
            var tokens = System.Text.RegularExpressions.Regex.Matches(filter, "[A-Za-z_][A-Za-z0-9_]*")
                .Select(m => m.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            tokens.Remove("IN"); tokens.Remove("NOT"); tokens.Remove("AND"); tokens.Remove("OR"); tokens.Remove("LIKE"); tokens.Remove("NULL");
                identifiers = tokens;
            }

            return identifiers;
        }

        private string NormalizeFilterExpression(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return filter;

            _logger.LogDebug("필터 정규화 시작: 원본='{Original}'", filter);

            string QuoteIfNeeded(string v)
            {
                var s = v.Trim().Trim('\'', '"');
                // 숫자형이면 그대로 반환, 아니면 작은따옴표 부여
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return s;
                return $"'{s}'";
            }

            string Rebuild(string field, string op, string list)
            {
                // 파이프(|) 구분자를 쉼표로 변환 (CSV 호환성을 위해)
                var normalizedList = list.Replace('|', ',');
                var parts = normalizedList.Split(',').Select(p => QuoteIfNeeded(p)).ToArray();
                var result = $"{field} {op} (" + string.Join(",", parts) + ")";
                _logger.LogDebug("리빌드 결과: field={Field}, op={Op}, list='{List}' -> '{Result}'", field, op, list, result);
                return result;
            }

            // IN/NOT IN을 먼저 처리 (IN 절 내부의 값들은 나중에 따옴표가 추가됨)
            // IN
            filter = Regex.Replace(filter,
                pattern: @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+IN\s*\(([^)]*)\)",
                evaluator: m => Rebuild(m.Groups[1].Value, "IN", m.Groups[2].Value));

            // NOT IN
            filter = Regex.Replace(filter,
                pattern: @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+NOT\s+IN\s*\(([^)]*)\)",
                evaluator: m => Rebuild(m.Groups[1].Value, "NOT IN", m.Groups[2].Value));

            // 단순 등식 처리 (field=value 형식) - 문자열 값에 따옴표 추가
            // 예: road_se=RDS014 -> road_se='RDS014'
            // IN/NOT IN 절을 먼저 처리했으므로, 남은 등식만 처리
            filter = Regex.Replace(filter,
                pattern: @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z0-9_]+)",
                evaluator: m =>
                {
                    var field = m.Groups[1].Value;
                    var value = m.Groups[2].Value;
                    // 이미 따옴표가 있으면 그대로 사용
                    if (value.StartsWith("'") || value.StartsWith("\""))
                    {
                        return m.Value; // 이미 따옴표가 있으면 변경하지 않음
                    }
                    // 숫자형이면 그대로, 아니면 따옴표 추가
                    var quotedValue = QuoteIfNeeded(value);
                    var result = $"{field} = {quotedValue}";
                    _logger.LogDebug("등식 필터 정규화: '{Original}' -> '{Result}'", m.Value, result);
                    return result;
                });

            _logger.LogDebug("필터 정규화 완료: 결과='{Result}'", filter);
            return filter;
        }

        /// <summary>
        /// 선형 객체가 면형 객체 영역을 허용오차 내에서 벗어나는지 검사
        /// </summary>
        private bool IsLineWithinPolygonWithTolerance(Geometry line, Geometry polygon, double tolerance)
        {
            if (line == null || polygon == null) return false;
            
            try
            {
                // 선형 객체의 모든 점이 면형 객체 경계로부터 허용오차 이내에 있는지 확인
                var pointCount = line.GetPointCount();
                var proximity = Math.Max(tolerance * 2.0, tolerance + 1e-9); // 허용오차의 2배를 근접거리로 사용

                for (int i = 0; i < pointCount; i++)
                {
                    var x = line.GetX(i);
                    var y = line.GetY(i);

                    using var pt = new Geometry(wkbGeometryType.wkbPoint);
                    pt.AddPoint(x, y, 0);

                    // 점에서 면형 객체 경계까지의 최단 거리 계산
                    var dist = pt.Distance(polygon);
                    
                    // 허용오차를 초과하면 선형 객체가 면형 객체 영역을 벗어남
                    if (dist > tolerance)
                    {
                        _logger.LogDebug("선형 객체 점이 면형 객체 영역을 벗어남: ({X},{Y}), 거리={Dist:F6}, 허용오차={Tolerance}", x, y, dist, tolerance);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "선형 객체 허용오차 검사 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// 연결된 선분끼리 속성값이 같은지 검사 (예: 등고선의 높이값)
        /// </summary>
        private void EvaluateConnectedLinesSameAttribute(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string attributeFieldName, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 등고선, 관련: 등고선 (동일 레이어 내 검사)
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            // FieldFilter에 속성 필드명이 지정되어 있는지 확인
            if (string.IsNullOrWhiteSpace(attributeFieldName))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다. FieldFilter에 필드명을 지정하세요.");
                return;
            }

            _logger.LogInformation("연결된 선분 속성값 일치 검사 시작: 레이어={Layer}, 속성필드={Field}, 허용오차={Tolerance}m", 
                config.MainTableId, attributeFieldName, tolerance);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집 (속성값 포함)
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var attributeValues = new Dictionary<long, double?>(); // OID -> 속성값

            Feature? f;
            int fieldIndex = -1;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;

                    // 속성값 읽기 (첫 번째 피처에서 필드 인덱스 확인)
                    if (fieldIndex < 0)
                    {
                        var defn = line.GetLayerDefn();
                        fieldIndex = defn.GetFieldIndex(attributeFieldName);
                        if (fieldIndex < 0)
                        {
                            _logger.LogError("속성 필드를 찾을 수 없습니다: {Field} (레이어: {Layer})", attributeFieldName, config.MainTableId);
                            return;
                        }
                    }

                    // 속성값 읽기 (NUMERIC 타입)
                    double? attrValue = null;
                    if (fieldIndex >= 0)
                    {
                        var fieldDefn = f.GetFieldDefnRef(fieldIndex);
                        if (fieldDefn != null)
                        {
                            var fieldType = fieldDefn.GetFieldType();
                            if (fieldType == FieldType.OFTReal || fieldType == FieldType.OFTInteger || fieldType == FieldType.OFTInteger64)
                            {
                                attrValue = f.GetFieldAsDouble(fieldIndex);
                            }
                            else
                            {
                                // 문자열인 경우 숫자로 변환 시도
                                var strValue = f.GetFieldAsString(fieldIndex);
                                if (!string.IsNullOrWhiteSpace(strValue) && double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
                                {
                                    attrValue = parsedValue;
                                }
                            }
                        }
                    }

                    attributeValues[oid] = attrValue;

                    var pCount = lineString.GetPointCount();
                    var sx = lineString.GetX(0);
                    var sy = lineString.GetY(0);
                    var ex = lineString.GetX(pCount - 1);
                    var ey = lineString.GetY(pCount - 1);

                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = g.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    allSegments.Add(segmentInfo);

                    // 끝점을 공간 인덱스에 추가
                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, tolerance);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, tolerance);
                }
            }

            _logger.LogInformation("선분 수집 완료: {Count}개, 끝점 인덱스 그리드 수: {GridCount}", 
                allSegments.Count, endpointIndex.Count);

            // 2단계: 연결된 선분끼리 속성값 비교
            var total = allSegments.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지 (OID1_OID2 형식)

            foreach (var segment in allSegments)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var oid = segment.Oid;
                var currentAttrValue = attributeValues.GetValueOrDefault(oid);

                // 현재 선분의 속성값이 없으면 스킵
                if (!currentAttrValue.HasValue)
                {
                    continue;
                }

                var sx = segment.StartX;
                var sy = segment.StartY;
                var ex = segment.EndX;
                var ey = segment.EndY;

                // 공간 인덱스를 사용하여 연결된 선분 검색
                var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                // 시작점에 연결된 선분 확인
                foreach (var candidate in startCandidates)
                {
                    if (candidate.Oid == oid) continue;

                    var dist = Distance(sx, sy, candidate.X, candidate.Y);
                    if (dist <= tolerance)
                    {
                        var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                        if (checkedPairs.Contains(pairKey)) continue;
                        checkedPairs.Add(pairKey);

                        var connectedAttrValue = attributeValues.GetValueOrDefault(candidate.Oid);
                        if (connectedAttrValue.HasValue)
                        {
                            // 속성값 비교 (NUMERIC 타입이므로 부동소수점 오차 고려)
                            var diff = Math.Abs(currentAttrValue.Value - connectedAttrValue.Value);
                            if (diff > 0.01) // 0.01 이상 차이나면 오류 (NUMERIC(7,2)이므로 소수점 2자리까지)
                            {
                                var oidStr = oid.ToString(CultureInfo.InvariantCulture);
                                var connectedOidStr = candidate.Oid.ToString(CultureInfo.InvariantCulture);
                                AddDetailedError(result, "REL_CONNECTED_LINES_ATTR_MISMATCH",
                                    $"연결된 등고선의 높이값이 일치하지 않음: {currentAttrValue.Value:F2}m vs {connectedAttrValue.Value:F2}m (차이: {diff:F2}m)",
                                    config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", segment.Geom);
                            }
                        }
                    }
                }

                // 끝점에 연결된 선분 확인
                foreach (var candidate in endCandidates)
                {
                    if (candidate.Oid == oid) continue;

                    var dist = Distance(ex, ey, candidate.X, candidate.Y);
                    if (dist <= tolerance)
                    {
                        var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                        if (checkedPairs.Contains(pairKey)) continue;
                        checkedPairs.Add(pairKey);

                        var connectedAttrValue = attributeValues.GetValueOrDefault(candidate.Oid);
                        if (connectedAttrValue.HasValue)
                        {
                            // 속성값 비교
                            var diff = Math.Abs(currentAttrValue.Value - connectedAttrValue.Value);
                            if (diff > 0.01) // 0.01 이상 차이나면 오류
                            {
                                var oidStr = oid.ToString(CultureInfo.InvariantCulture);
                                var connectedOidStr = candidate.Oid.ToString(CultureInfo.InvariantCulture);
                                AddDetailedError(result, "REL_CONNECTED_LINES_ATTR_MISMATCH",
                                    $"연결된 등고선의 높이값이 일치하지 않음: {currentAttrValue.Value:F2}m vs {connectedAttrValue.Value:F2}m (차이: {diff:F2}m)",
                                    config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", segment.Geom);
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("연결된 선분 속성값 일치 검사 완료: {Count}개 선분, 소요시간: {Elapsed:F2}초", 
                total, elapsed);

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);

            // 메모리 정리
            foreach (var seg in allSegments)
            {
                seg.Geom?.Dispose();
            }
        }

        private void EvaluateLineConnectivity(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 도로중심선, 관련: 도로중심선 (동일 레이어 내 검사)
            var line = getLayer(config.MainTableId);
            if (line == null) return;

            using var _attrFilter = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("선 연결성 검사 시작 (공간 인덱스 최적화 적용): 허용오차={Tolerance}m", tolerance);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>(); // 그리드 기반 공간 인덱스
            
            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;
                    
                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;
                    
                    var pCount = lineString.GetPointCount();
                    var sx = lineString.GetX(0);
                    var sy = lineString.GetY(0);
                    var ex = lineString.GetX(pCount - 1);
                    var ey = lineString.GetY(pCount - 1);
                    
                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = g.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    allSegments.Add(segmentInfo);
                    
                    // 끝점을 공간 인덱스에 추가 (그리드 기반)
                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, tolerance);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, tolerance);
                }
            }

            _logger.LogInformation("선분 수집 완료: {Count}개, 끝점 인덱스 그리드 수: {GridCount}", 
                allSegments.Count, endpointIndex.Count);

            // 2단계: 공간 인덱스를 사용하여 빠른 연결성 검사 (O(N) 또는 O(N log N))
            var total = allSegments.Count;
            var idx = 0;
            foreach (var segment in allSegments)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var oid = segment.Oid;
                var sx = segment.StartX;
                var sy = segment.StartY;
                var ex = segment.EndX;
                var ey = segment.EndY;

                // 공간 인덱스를 사용하여 후보 검색 (O(1) 또는 O(log N))
                var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                // 시작점 연결 확인 (같은 선 제외)
                bool startConnected = startCandidates.Any(c => c.Oid != oid && 
                    Distance(sx, sy, c.X, c.Y) <= tolerance);

                // 끝점 연결 확인 (같은 선 제외)
                bool endConnected = endCandidates.Any(c => c.Oid != oid && 
                    Distance(ex, ey, c.X, c.Y) <= tolerance);

                // 선분과의 거리 확인 (근접하지만 연결되지 않은 경우)
                bool startNearAnyLine = false;
                bool endNearAnyLine = false;

                using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                startPt.AddPoint(sx, sy, 0);
                using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                endPt.AddPoint(ex, ey, 0);

                // 후보군만 확인 (전체가 아닌 근처 선분만)
                var nearbySegments = GetNearbySegments(allSegments, sx, sy, ex, ey, tolerance * 5);
                foreach (var nearby in nearbySegments)
                {
                    if (nearby.Oid == oid) continue;
                    
                    if (!startNearAnyLine && startPt.Distance(nearby.Geom) <= tolerance) 
                        startNearAnyLine = true;
                    if (!endNearAnyLine && endPt.Distance(nearby.Geom) <= tolerance) 
                        endNearAnyLine = true;
                    
                    if (startNearAnyLine && endNearAnyLine) break;
                }

                // 오류 조건: 각 끝점별로 "반경 tol 이내에 타 선 존재" AND "끝점-끝점 연결 아님"
                if ((startNearAnyLine && !startConnected) || (endNearAnyLine && !endConnected))
                {
                    var length = Math.Abs(segment.Geom.Length());
                    if (length <= tolerance)
                    {
                        AddDetailedError(result, "REL_CTLN_END_SHORT", 
                            $"도로중심선 끝점이 {tolerance}m 이내 타 선과 근접하나 스냅되지 않음(엔더숏)", 
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), "", segment.Geom);
                    }
                    else
                    {
                        string which = (startNearAnyLine && !startConnected) && (endNearAnyLine && !endConnected) 
                            ? "양쪽" 
                            : ((startNearAnyLine && !startConnected) ? "시작점" : "끝점");
                        AddDetailedError(result, "REL_CTLN_DANGLING", 
                            $"도로중심선 {which}이(가) {tolerance}m 이내 타 선과 근접하나 연결되지 않음", 
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), "", segment.Geom);
                    }
                }
            }
            
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("선 연결성 검사 완료: {Count}개 선분, 소요시간: {Elapsed:F2}초 (공간 인덱스 최적화 적용)", 
                total, elapsed);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
            
            // 메모리 정리
            foreach (var seg in allSegments)
            {
                seg.Geom?.Dispose();
            }
        }

        private void EvaluateBoundaryMissingCenterline(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 도로경계면, 관련: 도로중심선
            // 최적화: SetSpatialFilter를 사용하여 공간 인덱스 활용 (이미 최적화됨)
            // 선형 레이어이지만 단순 존재 여부만 확인하므로 Union보다 SetSpatialFilter가 더 효율적
            var boundary = getLayer(config.MainTableId);
            var centerline = getLayer(config.RelatedTableId);
            if (boundary == null || centerline == null) return;

            using var _filter = ApplyAttributeFilterIfMatch(centerline, fieldFilter);

            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, MainTable={MainTable}, RelatedTable={RelatedTable}", 
                config.RuleId, config.CaseType, config.MainTableId, config.RelatedTableId);

            // 경계면 내부에 최소 한 개의 중심선이 있어야 한다고 가정하고, 내부에 전혀 교차/포함이 없으면 누락으로 처리
            boundary.ResetReading();
            var total = boundary.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: MainTable={MainTable} {Count}개", config.MainTableId, total);
            
            var startTime = DateTime.Now;
            Feature? bf;
            var processed = 0;
            
            while ((bf = boundary.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                
                using (bf)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        if (processed > 0 && elapsed > 0)
                        {
                            var speed = processed / elapsed;
                            _logger.LogDebug("진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 피처/초", 
                                processed, total, (processed * 100.0 / total), speed);
                        }
                    }
                    
                    var bg = bf.GetGeometryRef();
                    if (bg == null || bg.IsEmpty()) continue;
                    
                    // SetSpatialFilter를 사용하여 공간 인덱스 활용 (GDAL 내부 최적화)
                    centerline.SetSpatialFilter(bg);
                    var hasAny = centerline.GetNextFeature() != null;
                    centerline.ResetReading();
                    centerline.SetSpatialFilter(null);
                    
                    if (!hasAny)
                    {
                        var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, "REL_BNDRY_CENTERLINE_MISSING", "도로경계면에 도로중심선이 누락됨", config.MainTableId, oid, "", bg);
                    }
                }
            }
            
            var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("관계 검수 완료: RuleId={RuleId}, 처리 {Processed}개, 소요시간 {Elapsed:F2}초, 속도 {Speed:F1} 피처/초", 
                config.RuleId, processed, totalElapsed, processed > 0 && totalElapsed > 0 ? processed / totalElapsed : 0);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNoOverlap(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 건물, 관련: 대상 폴리곤(예: 도로경계면/보도경계면 등) - 겹침 금지
            var polyA = getLayer(config.MainTableId);
            var polyB = getLayer(config.RelatedTableId);
            if (polyA == null || polyB == null) return;

            using var _fa = ApplyAttributeFilterIfMatch(polyA, fieldFilter);
            using var _fb = ApplyAttributeFilterIfMatch(polyB, fieldFilter);

            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, MainTable={MainTable}, RelatedTable={RelatedTable}", 
                config.RuleId, config.CaseType, config.MainTableId, config.RelatedTableId);

            // 성능 최적화: Union Geometry 캐시 사용
            // 관련 레이어를 한 번 Union하여 캐시하고, 각 건물과 Union 지오메트리만 비교
            // 이 방식은 피처 수와 무관하게 일정한 성능을 제공 (O(건물 수) → O(건물 수))
            var cacheKey = $"union_{config.RelatedTableId}_{fieldFilter}";
            var unionGeometry = BuildUnionGeometryWithCache(polyB, cacheKey);
            
            if (unionGeometry == null || unionGeometry.IsEmpty())
            {
                _logger.LogInformation("관련 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 0, 0, completed: true);
                return;
            }

            // Union 지오메트리 유효성 보장
            try 
            { 
                unionGeometry = unionGeometry.MakeValid(Array.Empty<string>()); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Union 지오메트리 유효성 보정 실패, 원본 사용");
            }

            polyA.ResetReading();
            var total = polyA.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: MainTable={MainTable} {Count}개", config.MainTableId, total);
            
            var startTime = DateTime.Now;
            Feature? fa;
            var processed = 0;
            
            while ((fa = polyA.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                
                using (fa)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        if (processed > 0 && elapsed > 0)
                        {
                            var speed = processed / elapsed;
                            _logger.LogDebug("진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 피처/초", 
                                processed, total, (processed * 100.0 / total), speed);
                        }
                    }
                    
                    var ga = fa.GetGeometryRef();
                    if (ga == null || ga.IsEmpty()) continue;
                    
                    // Envelope 기반 사전 필터링 (빠른 제외)
                    var envelope = new Envelope();
                    ga.GetEnvelope(envelope);
                    var unionEnvelope = new Envelope();
                    unionGeometry.GetEnvelope(unionEnvelope);
                    
                    // Envelope이 겹치지 않으면 교차 불가능
                    if (envelope.MaxX < unionEnvelope.MinX || envelope.MinX > unionEnvelope.MaxX ||
                        envelope.MaxY < unionEnvelope.MinY || envelope.MinY > unionEnvelope.MaxY)
                    {
                        continue; // 교차 없음
                    }
                    
                    // Union 지오메트리와 교차 검사 (O(1) 연산)
                    try
                    {
                        using var inter = ga.Intersection(unionGeometry);
                        if (inter != null && !inter.IsEmpty())
                        {
                            // 겹침 면적이 tolerance 초과면 오류
                            var area = GetSurfaceArea(inter);
                            if (area > tolerance)
                            {
                                var oid = fa.GetFID().ToString(CultureInfo.InvariantCulture);
                                AddDetailedError(result, "REL_BULD_OVERLAP", 
                                    $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함", 
                                    config.MainTableId, oid, "", ga);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교차 검사 중 오류 발생: OID={Oid}", fa.GetFID());
                    }
                }
            }
            
            var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("관계 검수 완료: RuleId={RuleId}, 처리 {Processed}개, 소요시간 {Elapsed:F2}초, 속도 {Speed:F1} 피처/초", 
                config.RuleId, processed, totalElapsed, processed > 0 && totalElapsed > 0 ? processed / totalElapsed : 0);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNotIntersectLine(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 건물(폴리곤), 관련: 선(철도중심선 등) - 선과 교차 금지
            var buld = getLayer(config.MainTableId);
            var line = getLayer(config.RelatedTableId);
            if (buld == null || line == null) return;

            using var _fl = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, MainTable={MainTable}, RelatedTable={RelatedTable}", 
                config.RuleId, config.CaseType, config.MainTableId, config.RelatedTableId);

            // 성능 최적화: Union Geometry 캐시 사용
            // 선형 레이어를 한 번 Union하여 캐시하고, 각 건물과 Union 지오메트리만 비교
            // 이 방식은 O(N*M) → O(N)으로 성능을 대폭 개선
            var cacheKey = $"union_line_{config.RelatedTableId}_{fieldFilter}";
            var lineUnion = BuildUnionGeometryWithCache(line, cacheKey);
            
            if (lineUnion == null || lineUnion.IsEmpty())
            {
                _logger.LogInformation("선형 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 0, 0, completed: true);
                return;
            }

            // Union 지오메트리 유효성 보장
            try 
            { 
                lineUnion = lineUnion.MakeValid(Array.Empty<string>()); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Union 지오메트리 유효성 보정 실패, 원본 사용");
            }

            buld.ResetReading();
            var total = buld.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: MainTable={MainTable} {Count}개", config.MainTableId, total);
            
            var startTime = DateTime.Now;
            Feature? bf;
            var processed = 0;
            
            while ((bf = buld.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                
                using (bf)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        if (processed > 0 && elapsed > 0)
                        {
                            var speed = processed / elapsed;
                            _logger.LogDebug("진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 피처/초", 
                                processed, total, (processed * 100.0 / total), speed);
                        }
                    }
                    
                    var pg = bf.GetGeometryRef();
                    if (pg == null || pg.IsEmpty()) continue;
                    
                    // Envelope 기반 사전 필터링 (빠른 제외)
                    var envelope = new Envelope();
                    pg.GetEnvelope(envelope);
                    var unionEnvelope = new Envelope();
                    lineUnion.GetEnvelope(unionEnvelope);
                    
                    // Envelope이 겹치지 않으면 교차 불가능
                    if (envelope.MaxX < unionEnvelope.MinX || envelope.MinX > unionEnvelope.MaxX ||
                        envelope.MaxY < unionEnvelope.MinY || envelope.MinY > unionEnvelope.MaxY)
                    {
                        continue; // 교차 없음
                    }
                    
                    // Union 지오메트리와 교차 검사 (O(1) 연산)
                    try
                    {
                        using var inter = pg.Intersection(lineUnion);
                        if (inter != null && !inter.IsEmpty())
                        {
                            var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);
                            AddDetailedError(result, "REL_BULD_INTERSECT_LINE", $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함", config.MainTableId, oid, "", pg);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교차 검사 중 오류 발생: OID={Oid}", bf.GetFID());
                    }
                }
            }
            
            var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("관계 검수 완료: RuleId={RuleId}, 처리 {Processed}개, 소요시간 {Elapsed:F2}초, 속도 {Speed:F1} 피처/초", 
                config.RuleId, processed, totalElapsed, processed > 0 && totalElapsed > 0 ? processed / totalElapsed : 0);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNotContainPoint(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 금지 폴리곤, 관련: 점 - 폴리곤 내부에 점 존재 금지
            // 최적화: SetSpatialFilter를 사용하여 공간 인덱스 활용 (이미 최적화됨)
            // 점 레이어는 Union을 사용할 수 없으므로 SetSpatialFilter 방식 유지
            var poly = getLayer(config.MainTableId);
            var pt = getLayer(config.RelatedTableId);
            if (poly == null || pt == null) return;

            using var _fp = ApplyAttributeFilterIfMatch(pt, fieldFilter);

            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, MainTable={MainTable}, RelatedTable={RelatedTable}", 
                config.RuleId, config.CaseType, config.MainTableId, config.RelatedTableId);

            poly.ResetReading();
            var total = poly.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: MainTable={MainTable} {Count}개", config.MainTableId, total);
            
            var startTime = DateTime.Now;
            Feature? pf;
            var processed = 0;
            
            while ((pf = poly.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                
                using (pf)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        if (processed > 0 && elapsed > 0)
                        {
                            var speed = processed / elapsed;
                            _logger.LogDebug("진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 피처/초", 
                                processed, total, (processed * 100.0 / total), speed);
                        }
                    }
                    
                    var pg = pf.GetGeometryRef();
                    if (pg == null || pg.IsEmpty()) continue;
                    
                    // SetSpatialFilter를 사용하여 공간 인덱스 활용 (GDAL 내부 최적화)
                    pt.SetSpatialFilter(pg);
                    var hasInside = pt.GetNextFeature() != null;
                    pt.ResetReading();
                    pt.SetSpatialFilter(null);
                    
                    if (hasInside)
                    {
                        var oid = pf.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, "REL_POLY_CONTAIN_POINT", $"{config.MainTableName}(이) {config.RelatedTableName}을 포함함", config.MainTableId, oid, "", pg);
                    }
                }
            }
            
            var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("관계 검수 완료: RuleId={RuleId}, 처리 {Processed}개, 소요시간 {Elapsed:F2}초, 속도 {Speed:F1} 피처/초", 
                config.RuleId, processed, totalElapsed, processed > 0 && totalElapsed > 0 ? processed / totalElapsed : 0);
            
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            var dx = x1 - x2; var dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 면적 계산 시 타입 가드: 폴리곤/멀티폴리곤에서만 면적 반환, 그 외 0
        /// </summary>
        private static double GetSurfaceArea(Geometry geometry)
        {
            try
            {
                if (geometry == null || geometry.IsEmpty()) return 0.0;
                var t = geometry.GetGeometryType();
                return t == wkbGeometryType.wkbPolygon || t == wkbGeometryType.wkbMultiPolygon
                    ? geometry.GetArea()
                    : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private sealed class ActionOnDispose : IDisposable
        {
            private readonly Action _onDispose;
            public ActionOnDispose(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() { _onDispose?.Invoke(); }
        }

        #region 공간 인덱스 헬퍼 (성능 최적화)

        /// <summary>
        /// 선분 정보 구조체
        /// </summary>
        private class LineSegmentInfo
        {
            public long Oid { get; set; }
            public Geometry Geom { get; set; } = null!;
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
        }

        /// <summary>
        /// 끝점 정보 구조체
        /// </summary>
        private class EndpointInfo
        {
            public long Oid { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public bool IsStart { get; set; }
        }

        /// <summary>
        /// 그리드 기반 공간 인덱스에 끝점 추가
        /// </summary>
        private void AddEndpointToIndex(Dictionary<string, List<EndpointInfo>> index, 
            double x, double y, long oid, bool isStart, double gridSize)
        {
            // 그리드 키 생성 (gridSize 단위로 그리드 분할)
            int gridX = (int)Math.Floor(x / gridSize);
            int gridY = (int)Math.Floor(y / gridSize);
            string key = $"{gridX}_{gridY}";
            
            if (!index.ContainsKey(key))
            {
                index[key] = new List<EndpointInfo>();
            }
            
            index[key].Add(new EndpointInfo
            {
                Oid = oid,
                X = x,
                Y = y,
                IsStart = isStart
            });
        }

        /// <summary>
        /// 좌표 주변의 끝점 검색 (공간 인덱스 사용)
        /// </summary>
        private List<EndpointInfo> SearchEndpointsNearby(Dictionary<string, List<EndpointInfo>> index, 
            double x, double y, double searchRadius)
        {
            var results = new List<EndpointInfo>();
            
            // 인접한 그리드 셀 검색 (3x3 영역)
            int centerGridX = (int)Math.Floor(x / searchRadius);
            int centerGridY = (int)Math.Floor(y / searchRadius);
            
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    string key = $"{centerGridX + dx}_{centerGridY + dy}";
                    if (index.TryGetValue(key, out var endpoints))
                    {
                        results.AddRange(endpoints);
                    }
                }
            }
            
            return results;
        }

        /// <summary>
        /// 바운딩 박스 기반 근처 선분 필터링
        /// </summary>
        private List<LineSegmentInfo> GetNearbySegments(List<LineSegmentInfo> allSegments, 
            double sx, double sy, double ex, double ey, double searchRadius)
        {
            var minX = Math.Min(sx, ex) - searchRadius;
            var maxX = Math.Max(sx, ex) + searchRadius;
            var minY = Math.Min(sy, ey) - searchRadius;
            var maxY = Math.Max(sy, ey) + searchRadius;
            
            return allSegments.Where(seg =>
            {
                // 바운딩 박스 교차 확인 (빠른 필터링)
                var segMinX = Math.Min(seg.StartX, seg.EndX);
                var segMaxX = Math.Max(seg.StartX, seg.EndX);
                var segMinY = Math.Min(seg.StartY, seg.EndY);
                var segMaxY = Math.Max(seg.StartY, seg.EndY);
                
                return !(maxX < segMinX || minX > segMaxX || maxY < segMinY || minY > segMaxY);
            }).ToList();
        }

        #endregion
        
        #region IDisposable Implementation
        
        /// <summary>
        /// 두 벡터 간의 각도 차이 계산 (0~180도)
        /// </summary>
        private double CalculateAngleDifference(double v1x, double v1y, double v2x, double v2y)
        {
            // 벡터 정규화
            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            
            if (len1 == 0 || len2 == 0) return 180.0; // 영벡터는 180도 차이로 간주
            
            var cosAngle = (v1x * v2x + v1y * v2y) / (len1 * len2);
            cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle)); // Clamp to [-1, 1]
            
            var angleRad = Math.Acos(cosAngle);
            var angleDeg = angleRad * 180.0 / Math.PI;
            
            return angleDeg;
        }

        /// <summary>
        /// 연결된 중심선끼리 속성값이 일치하는지 검사 (도로/철도/하천 중심선)
        /// 하이브리드 방식: Phase 1 (교차로 감지) + Phase 2 (각도 기반 연속성 판단)
        /// </summary>
        private void EvaluateCenterlineAttributeMismatch(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 중심선 레이어 (도로/철도/하천)
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            // FieldFilter 파싱: 속성 필드명과 임계값 파라미터
            // 형식: "field1|field2|field3;intersection_threshold=3;angle_threshold=30"
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다. FieldFilter에 필드명을 파이프(|)로 구분하여 지정하세요.");
                return;
            }

            // 세미콜론으로 분리: 속성 필드 부분과 파라미터 부분
            var parts = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var attributeFieldsPart = parts[0]; // 첫 번째 부분은 속성 필드명들
            
            // 기본값: geometry_criteria.csv에서 읽거나 하드코딩된 기본값
            var intersectionThreshold = _geometryCriteria?.CenterlineIntersectionThreshold ?? 3;
            var angleThreshold = _geometryCriteria?.CenterlineAngleThreshold ?? 30.0;

            // 파라미터 파싱 (세미콜론으로 구분된 부분들)
            for (int i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();
                if (param.StartsWith("intersection_threshold=", StringComparison.OrdinalIgnoreCase))
                {
                    var valueStr = param.Substring("intersection_threshold=".Length).Trim();
                    if (int.TryParse(valueStr, System.Globalization.NumberStyles.Integer, 
                        System.Globalization.CultureInfo.InvariantCulture, out int value))
                    {
                        intersectionThreshold = value;
                    }
                }
                else if (param.StartsWith("angle_threshold=", StringComparison.OrdinalIgnoreCase))
                {
                    var valueStr = param.Substring("angle_threshold=".Length).Trim();
                    if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float, 
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        angleThreshold = value;
                    }
                }
            }

            // 속성 필드명 파싱
            var attributeFields = attributeFieldsPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (attributeFields.Length == 0)
            {
                _logger.LogWarning("속성 필드명이 올바르게 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return;
            }

            _logger.LogInformation("중심선 속성 불일치 검사 시작 (하이브리드 방식): 레이어={Layer}, 속성필드={Fields}, 허용오차={Tolerance}m, 교차로임계값={IntersectionThreshold}개, 각도임계값={AngleThreshold}도", 
                config.MainTableId, string.Join(", ", attributeFields), tolerance, intersectionThreshold, angleThreshold);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집 (속성값 포함)
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var attributeValues = new Dictionary<long, Dictionary<string, string?>>(); // OID -> (필드명 -> 속성값)

            Feature? f;
            var fieldIndices = new Dictionary<string, int>(); // 필드명 -> 필드 인덱스

            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;

                    // 필드 인덱스 확인 (첫 번째 피처에서)
                    if (fieldIndices.Count == 0)
                    {
                        var defn = line.GetLayerDefn();
                        foreach (var fieldName in attributeFields)
                        {
                            var fieldIdx = defn.GetFieldIndex(fieldName);
                            if (fieldIdx >= 0)
                            {
                                fieldIndices[fieldName] = fieldIdx;
                            }
                            else
                            {
                                _logger.LogWarning("속성 필드를 찾을 수 없습니다: {Field} (레이어: {Layer})", fieldName, config.MainTableId);
                            }
                        }
                        if (fieldIndices.Count == 0)
                        {
                            _logger.LogError("모든 속성 필드를 찾을 수 없습니다: {Fields}", string.Join(", ", attributeFields));
                            return;
                        }
                    }

                    // 속성값 읽기
                    var attrDict = new Dictionary<string, string?>();
                    foreach (var kvp in fieldIndices)
                    {
                        var fieldName = kvp.Key;
                        var fieldIdx = kvp.Value;
                        var strValue = f.GetFieldAsString(fieldIdx);
                        attrDict[fieldName] = string.IsNullOrWhiteSpace(strValue) ? null : strValue;
                    }
                    attributeValues[oid] = attrDict;

                    var pCount = lineString.GetPointCount();
                    var sx = lineString.GetX(0);
                    var sy = lineString.GetY(0);
                    var ex = lineString.GetX(pCount - 1);
                    var ey = lineString.GetY(pCount - 1);

                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = g.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    allSegments.Add(segmentInfo);

                    // 끝점을 공간 인덱스에 추가
                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, tolerance);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, tolerance);
                }
            }

            _logger.LogInformation("선분 수집 완료: {Count}개, 끝점 인덱스 그리드 수: {GridCount}", 
                allSegments.Count, endpointIndex.Count);

            // 2단계: 연결된 선분끼리 속성값 비교 (하이브리드 방식)
            var total = allSegments.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지
            
            // 통계 추적
            var intersectionExcludedCount = 0; // 교차로로 제외된 연결 수
            var angleExcludedCount = 0; // 각도로 제외된 연결 수
            var checkedCount = 0; // 실제 검사한 연결 수

            foreach (var segment in allSegments)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var oid = segment.Oid;
                var currentAttrs = attributeValues.GetValueOrDefault(oid);
                if (currentAttrs == null || currentAttrs.Count == 0) continue;

                var sx = segment.StartX;
                var sy = segment.StartY;
                var ex = segment.EndX;
                var ey = segment.EndY;

                // 공간 인덱스를 사용하여 연결된 선분 검색
                var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                // 현재 선분의 방향 벡터 계산
                var currentVectorX = ex - sx;
                var currentVectorY = ey - sy;

                // 시작점에 연결된 선분 확인
                foreach (var candidate in startCandidates)
                {
                    if (candidate.Oid == oid) continue;

                    var dist = Distance(sx, sy, candidate.X, candidate.Y);
                    if (dist <= tolerance)
                    {
                        var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                        if (checkedPairs.Contains(pairKey)) continue;
                        checkedPairs.Add(pairKey);

                        // === Phase 1: 교차로 감지 ===
                        // 시작점에서 연결된 선분 개수 확인 (현재 선분 제외)
                        var startConnectionCount = startCandidates.Count(c => c.Oid != oid && Distance(sx, sy, c.X, c.Y) <= tolerance);
                        bool isIntersection = startConnectionCount >= intersectionThreshold;

                        if (isIntersection)
                        {
                            // 교차로 지점이므로 속성 불일치 검사 제외
                            intersectionExcludedCount++;
                            _logger.LogDebug("교차로 지점 감지 (Phase 1): OID={Oid}, 연결된 선분 수={Count}", 
                                oid, startConnectionCount);
                            continue;
                        }

                        // === Phase 2: 각도 기반 연속성 판단 ===
                        var connectedSegment = allSegments.FirstOrDefault(s => s.Oid == candidate.Oid);
                        if (connectedSegment != null)
                        {
                            // 연결된 선분의 방향 벡터 계산 (연결 지점 기준)
                            double connectedVectorX, connectedVectorY;
                            
                            // 연결 지점이 시작점인지 끝점인지 확인
                            if (candidate.IsStart)
                            {
                                // 연결된 선분의 시작점이 연결 지점이므로, 끝점 방향으로 벡터 계산
                                connectedVectorX = connectedSegment.EndX - connectedSegment.StartX;
                                connectedVectorY = connectedSegment.EndY - connectedSegment.StartY;
                            }
                            else
                            {
                                // 연결된 선분의 끝점이 연결 지점이므로, 시작점 방향으로 벡터 계산 (역방향)
                                connectedVectorX = connectedSegment.StartX - connectedSegment.EndX;
                                connectedVectorY = connectedSegment.StartY - connectedSegment.EndY;
                            }
                            
                            // 각도 차이 계산
                            var angleDiff = CalculateAngleDifference(
                                currentVectorX, currentVectorY,
                                connectedVectorX, connectedVectorY);
                            
                            // 각도 차이가 임계값을 초과하면 교차로로 판단하여 제외
                            if (angleDiff > angleThreshold)
                            {
                                angleExcludedCount++;
                                _logger.LogDebug("교차로로 판단 (Phase 2, 각도 차이: {Angle:F1}도): OID={Oid1} <-> {Oid2}", 
                                    angleDiff, oid, candidate.Oid);
                                continue;
                            }
                        }

                        // === 속성 불일치 검사 수행 ===
                        checkedCount++;
                        var connectedAttrs = attributeValues.GetValueOrDefault(candidate.Oid);
                        if (connectedAttrs != null)
                        {
                            // 속성값 비교 (하나라도 다르면 오류)
                            var mismatchedFields = new List<string>();
                            foreach (var fieldName in attributeFields)
                            {
                                var currentValue = currentAttrs.GetValueOrDefault(fieldName);
                                var connectedValue = connectedAttrs.GetValueOrDefault(fieldName);
                                
                                // 둘 다 null이면 일치로 간주, 하나만 null이면 불일치
                                if (currentValue != connectedValue)
                                {
                                    mismatchedFields.Add(fieldName);
                                }
                            }

                            if (mismatchedFields.Count > 0)
                            {
                                var oidStr = oid.ToString(CultureInfo.InvariantCulture);
                                var connectedOidStr = candidate.Oid.ToString(CultureInfo.InvariantCulture);
                                var mismatchDetails = string.Join(", ", mismatchedFields.Select(f => $"{f}: {currentAttrs.GetValueOrDefault(f) ?? "NULL"} vs {connectedAttrs.GetValueOrDefault(f) ?? "NULL"}"));
                                AddDetailedError(result, "REL_CENTERLINE_ATTR_MISMATCH",
                                    $"연결된 중심선의 속성값이 불일치함: {mismatchDetails}",
                                    config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", segment.Geom);
                            }
                        }
                    }
                }

                // 끝점에 연결된 선분 확인
                foreach (var candidate in endCandidates)
                {
                    if (candidate.Oid == oid) continue;

                    var dist = Distance(ex, ey, candidate.X, candidate.Y);
                    if (dist <= tolerance)
                    {
                        var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                        if (checkedPairs.Contains(pairKey)) continue;
                        checkedPairs.Add(pairKey);

                        // === Phase 1: 교차로 감지 ===
                        // 끝점에서 연결된 선분 개수 확인 (현재 선분 제외)
                        var endConnectionCount = endCandidates.Count(c => c.Oid != oid && Distance(ex, ey, c.X, c.Y) <= tolerance);
                        bool isIntersection = endConnectionCount >= intersectionThreshold;

                        if (isIntersection)
                        {
                            // 교차로 지점이므로 속성 불일치 검사 제외
                            intersectionExcludedCount++;
                            _logger.LogDebug("교차로 지점 감지 (Phase 1): OID={Oid}, 연결된 선분 수={Count}", 
                                oid, endConnectionCount);
                            continue;
                        }

                        // === Phase 2: 각도 기반 연속성 판단 ===
                        var connectedSegment = allSegments.FirstOrDefault(s => s.Oid == candidate.Oid);
                        if (connectedSegment != null)
                        {
                            // 연결된 선분의 방향 벡터 계산 (연결 지점 기준)
                            double connectedVectorX, connectedVectorY;
                            
                            // 연결 지점이 시작점인지 끝점인지 확인
                            if (candidate.IsStart)
                            {
                                // 연결된 선분의 시작점이 연결 지점이므로, 끝점 방향으로 벡터 계산
                                connectedVectorX = connectedSegment.EndX - connectedSegment.StartX;
                                connectedVectorY = connectedSegment.EndY - connectedSegment.StartY;
                            }
                            else
                            {
                                // 연결된 선분의 끝점이 연결 지점이므로, 시작점 방향으로 벡터 계산 (역방향)
                                connectedVectorX = connectedSegment.StartX - connectedSegment.EndX;
                                connectedVectorY = connectedSegment.StartY - connectedSegment.EndY;
                            }
                            
                            // 각도 차이 계산
                            var angleDiff = CalculateAngleDifference(
                                currentVectorX, currentVectorY,
                                connectedVectorX, connectedVectorY);
                            
                            // 각도 차이가 임계값을 초과하면 교차로로 판단하여 제외
                            if (angleDiff > angleThreshold)
                            {
                                angleExcludedCount++;
                                _logger.LogDebug("교차로로 판단 (Phase 2, 각도 차이: {Angle:F1}도): OID={Oid1} <-> {Oid2}", 
                                    angleDiff, oid, candidate.Oid);
                                continue;
                            }
                        }

                        // === 속성 불일치 검사 수행 ===
                        checkedCount++;
                        var connectedAttrs = attributeValues.GetValueOrDefault(candidate.Oid);
                        if (connectedAttrs != null)
                        {
                            // 속성값 비교
                            var mismatchedFields = new List<string>();
                            foreach (var fieldName in attributeFields)
                            {
                                var currentValue = currentAttrs.GetValueOrDefault(fieldName);
                                var connectedValue = connectedAttrs.GetValueOrDefault(fieldName);
                                
                                if (currentValue != connectedValue)
                                {
                                    mismatchedFields.Add(fieldName);
                                }
                            }

                            if (mismatchedFields.Count > 0)
                            {
                                var oidStr = oid.ToString(CultureInfo.InvariantCulture);
                                var connectedOidStr = candidate.Oid.ToString(CultureInfo.InvariantCulture);
                                var mismatchDetails = string.Join(", ", mismatchedFields.Select(f => $"{f}: {currentAttrs.GetValueOrDefault(f) ?? "NULL"} vs {connectedAttrs.GetValueOrDefault(f) ?? "NULL"}"));
                                AddDetailedError(result, "REL_CENTERLINE_ATTR_MISMATCH",
                                    $"연결된 중심선의 속성값이 불일치함: {mismatchDetails}",
                                    config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", segment.Geom);
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("중심선 속성 불일치 검사 완료 (하이브리드 방식): {Count}개 선분, 소요시간: {Elapsed:F2}초, 교차로 제외: {IntersectionExcluded}개, 각도 제외: {AngleExcluded}개, 실제 검사: {Checked}개", 
                total, elapsed, intersectionExcludedCount, angleExcludedCount, checkedCount);

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);

            // 메모리 정리
            foreach (var seg in allSegments)
            {
                seg.Geom?.Dispose();
            }
        }

        /// <summary>
        /// 등고선이 다른 등고선과 교차하는지 검사
        /// </summary>
        private void EvaluateContourIntersection(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            using var _attrFilter = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("등고선 교차 검사 시작: 레이어={Layer}", config.MainTableId);
            var startTime = DateTime.Now;

            // 모든 등고선 피처 수집
            line.ResetReading();
            var allLines = new List<(long Oid, Geometry Geom)>();
            
            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    allLines.Add((oid, g.Clone()));
                }
            }

            _logger.LogInformation("등고선 수집 완료: {Count}개", allLines.Count);

            // 각 등고선이 다른 등고선과 교차하는지 확인
            var total = allLines.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지

            for (int i = 0; i < allLines.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var (oid1, geom1) = allLines[i];

                for (int j = i + 1; j < allLines.Count; j++)
                {
                    var (oid2, geom2) = allLines[j];

                    var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                    if (checkedPairs.Contains(pairKey)) continue;
                    checkedPairs.Add(pairKey);

                    try
                    {
                        // 교차 여부 확인
                        if (geom1.Intersects(geom2))
                        {
                            // 실제로 교차하는지 확인 (단순히 겹치는 것이 아닌)
                            using var intersection = geom1.Intersection(geom2);
                            if (intersection != null && !intersection.IsEmpty())
                            {
                                var intersectionType = intersection.GetGeometryType();
                                // 점이 아닌 교차(선 또는 면)인 경우만 오류
                                // wkbPoint, wkbMultiPoint가 아닌 경우 (wkbLineString, wkbPolygon 등)
                                if (intersectionType != wkbGeometryType.wkbPoint && 
                                    intersectionType != wkbGeometryType.wkbMultiPoint)
                                {
                                    var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                    var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                    AddDetailedError(result, "REL_CONTOUR_INTERSECTION",
                                        $"등고선이 다른 등고선과 교차함: 피처 {oid1Str}와 {oid2Str}",
                                        config.MainTableId, oid1Str, $"교차 피처: {oid2Str}", geom1);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "등고선 교차 검사 중 오류: OID={Oid1}, {Oid2}", oid1, oid2);
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("등고선 교차 검사 완료: {Count}개 등고선, 소요시간: {Elapsed:F2}초", 
                total, elapsed);

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);

            // 메모리 정리
            foreach (var (_, geom) in allLines)
            {
                geom?.Dispose();
            }
        }

        /// <summary>
        /// 등고선이 90도 미만으로 꺽이는지 검사
        /// </summary>
        private void EvaluateContourSharpBend(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double angleThreshold, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            using var _attrFilter = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("등고선 꺽임 검사 시작: 레이어={Layer}, 각도임계값={Threshold}도", config.MainTableId, angleThreshold);
            var startTime = DateTime.Now;

            line.ResetReading();
            var total = line.GetFeatureCount(1);
            var idx = 0;

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                    }

                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var oidStr = oid.ToString(CultureInfo.InvariantCulture);

                    // 멀티라인스트링 처리
                    int geometryCount = g.GetGeometryType() == wkbGeometryType.wkbMultiLineString ? g.GetGeometryCount() : 1;
                    
                    for (int geomIdx = 0; geomIdx < geometryCount; geomIdx++)
                    {
                        var lineString = geometryCount > 1 ? g.GetGeometryRef(geomIdx) : g;
                        if (lineString == null || lineString.GetPointCount() < 3) continue; // 최소 3개 점 필요

                        var pointCount = lineString.GetPointCount();
                        
                        // 연속된 3개 점으로 각도 계산
                        for (int i = 1; i < pointCount - 1; i++)
                        {
                            var x0 = lineString.GetX(i - 1);
                            var y0 = lineString.GetY(i - 1);
                            var x1 = lineString.GetX(i);
                            var y1 = lineString.GetY(i);
                            var x2 = lineString.GetX(i + 1);
                            var y2 = lineString.GetY(i + 1);

                            // 벡터 계산
                            var v1x = x1 - x0;
                            var v1y = y1 - y0;
                            var v2x = x2 - x1;
                            var v2y = y2 - y1;

                            // 벡터 길이
                            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                            if (len1 < 1e-10 || len2 < 1e-10) continue; // 너무 짧은 선분은 스킵

                            // 내적 계산
                            var dot = v1x * v2x + v1y * v2y;
                            var cosAngle = dot / (len1 * len2);
                            
                            // 각도 계산 (0~180도)
                            var angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosAngle))) * 180.0 / Math.PI;

                            // 90도 미만이면 오류 (각도가 작을수록 더 날카롭게 꺽임)
                            if (angle < angleThreshold)
                            {
                                AddDetailedError(result, "REL_CONTOUR_SHARP_BEND",
                                    $"등고선이 {angle:F1}도로 꺽임 (임계값: {angleThreshold}도 미만)",
                                    config.MainTableId, oidStr, $"정점 {i}에서 꺽임", g);
                                break; // 한 피처당 하나의 오류만 보고
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("등고선 꺽임 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초", 
                total, elapsed);

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        /// <summary>
        /// 도로중심선이 6도 이하로 꺽이는지 검사
        /// </summary>
        private void EvaluateRoadSharpBend(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double angleThreshold, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            using var _attrFilter = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("도로중심선 꺽임 검사 시작: 레이어={Layer}, 각도임계값={Threshold}도", config.MainTableId, angleThreshold);
            var startTime = DateTime.Now;

            line.ResetReading();
            var total = line.GetFeatureCount(1);
            var idx = 0;

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                    }

                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var oidStr = oid.ToString(CultureInfo.InvariantCulture);

                    // 멀티라인스트링 처리
                    int parts = g.GetGeometryType() == wkbGeometryType.wkbMultiLineString
                        ? g.GetGeometryCount()
                        : 1;

                    for (int p = 0; p < parts; p++)
                    {
                        Geometry? linePart = g.GetGeometryType() == wkbGeometryType.wkbMultiLineString
                            ? g.GetGeometryRef(p)
                            : g;

                        if (linePart == null || linePart.GetGeometryType() != wkbGeometryType.wkbLineString) continue;

                        var pointCount = linePart.GetPointCount();
                        if (pointCount < 3) continue; // 최소 3개 점 필요

                        // 연속된 세 점으로 각도 계산
                        for (int i = 1; i < pointCount - 1; i++)
                        {
                            var x1 = linePart.GetX(i - 1);
                            var y1 = linePart.GetY(i - 1);
                            var x2 = linePart.GetX(i);
                            var y2 = linePart.GetY(i);
                            var x3 = linePart.GetX(i + 1);
                            var y3 = linePart.GetY(i + 1);

                            // 벡터 계산
                            var v1x = x1 - x2;
                            var v1y = y1 - y2;
                            var v2x = x3 - x2;
                            var v2y = y3 - y2;

                            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                            if (len1 < 1e-10 || len2 < 1e-10) continue; // 너무 짧은 선분은 스킵

                            // 내적 계산
                            var dot = v1x * v2x + v1y * v2y;
                            var cosAngle = dot / (len1 * len2);
                            
                            // 각도 계산 (0~180도)
                            var angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosAngle))) * 180.0 / Math.PI;

                            // 6도 이하이면 오류 (각도가 작을수록 더 날카롭게 꺽임)
                            if (angle <= angleThreshold)
                            {
                                AddDetailedError(result, "REL_ROAD_SHARP_BEND",
                                    $"도로중심선이 {angle:F1}도로 꺽임 (임계값: {angleThreshold}도 이하)",
                                    config.MainTableId, oidStr, $"정점 {i}에서 꺽임", g);
                                break; // 한 피처당 하나의 오류만 보고
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("도로중심선 꺽임 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초", 
                total, elapsed);

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        /// <summary>
        /// 교량과 하천중심선의 하천명 일치 검사
        /// </summary>
        private void EvaluateBridgeRiverNameMatch(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // FieldFilter 형식: "pg_rdfc_se IN (PRC002|PRC003|PRC004|PRC005);feat_nm"
            var filterParts = (fieldFilter ?? string.Empty).Split(';');
            var bridgeFilter = filterParts.Length > 0 ? filterParts[0] : string.Empty;
            var riverNameField = filterParts.Length > 1 ? filterParts[1] : "feat_nm";

            var bridgeLayer = getLayer(config.MainTableId);
            var riverLayer = getLayer(config.RelatedTableId);
            if (bridgeLayer == null || riverLayer == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: Bridge={Bridge}, River={River}", config.MainTableId, config.RelatedTableId);
                return;
            }

            using var _bridgeFilter = ApplyAttributeFilterIfMatch(bridgeLayer, bridgeFilter);

            _logger.LogInformation("교량하천명 일치 검사 시작: 교량 레이어={BridgeLayer}, 하천 레이어={RiverLayer}, 하천명 필드={Field}", 
                config.MainTableId, config.RelatedTableId, riverNameField);
            var startTime = DateTime.Now;

            // 하천중심선의 하천명 인덱스
            var riverDefn = riverLayer.GetLayerDefn();
            int riverNameIdx = GetFieldIndexIgnoreCase(riverDefn, riverNameField);
            if (riverNameIdx < 0)
            {
                _logger.LogWarning("하천명 필드를 찾을 수 없습니다: {Field}", riverNameField);
                return;
            }

            // 교량 레이어의 하천명 필드 인덱스
            var bridgeDefn = bridgeLayer.GetLayerDefn();
            int bridgeNameIdx = GetFieldIndexIgnoreCase(bridgeDefn, riverNameField);
            if (bridgeNameIdx < 0)
            {
                _logger.LogWarning("교량의 하천명 필드를 찾을 수 없습니다: {Field}", riverNameField);
                return;
            }

            // 하천중심선을 공간 인덱스로 구성 (성능 최적화)
            var riverFeatures = new List<(long oid, Geometry geom, string name)>();
            riverLayer.ResetReading();
            Feature? rf;
            while ((rf = riverLayer.GetNextFeature()) != null)
            {
                using (rf)
                {
                    var rg = rf.GetGeometryRef();
                    // NULL 체크 및 안전한 IsEmpty() 호출
                    if (rg == null) continue;
                    try
                    {
                        if (rg.IsEmpty()) continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "하천중심선 지오메트리 IsEmpty() 검사 중 오류: FID={FID}", rf.GetFID());
                        continue;
                    }
                    
                    // 지오메트리 복제 및 유효성 검사
                    Geometry? clonedGeom = null;
                    try
                    {
                        clonedGeom = rg.Clone();
                        if (clonedGeom == null)
                        {
                            _logger.LogWarning("하천중심선 지오메트리 복제 실패: FID={FID}", rf.GetFID());
                            continue;
                        }
                        
                        // 복제된 지오메트리 유효성 검사
                        try
                        {
                            if (clonedGeom.IsEmpty())
                            {
                                _logger.LogWarning("하천중심선 지오메트리가 비어있음: FID={FID}", rf.GetFID());
                                clonedGeom.Dispose();
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "하천중심선 복제 지오메트리 IsEmpty() 검사 중 오류: FID={FID}", rf.GetFID());
                            clonedGeom.Dispose();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "하천중심선 지오메트리 복제 중 오류: FID={FID}", rf.GetFID());
                        clonedGeom?.Dispose();
                        continue;
                    }
                    
                    var name = rf.IsFieldNull(riverNameIdx) ? string.Empty : (rf.GetFieldAsString(riverNameIdx) ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(name)) 
                    {
                        clonedGeom.Dispose(); // 하천명이 없으면 복제된 지오메트리도 정리
                        continue;
                    }
                    
                    riverFeatures.Add((rf.GetFID(), clonedGeom, name));
                }
            }

            _logger.LogInformation("하천중심선 수집 완료: {Count}개", riverFeatures.Count);

            // 교량 검사
            bridgeLayer.ResetReading();
            var total = bridgeLayer.GetFeatureCount(1);
            var processed = 0;

            Feature? bf;
            while ((bf = bridgeLayer.GetNextFeature()) != null)
            {
                using (bf)
                {
                    token.ThrowIfCancellationRequested();
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                    }

                    var bg = bf.GetGeometryRef();
                    if (bg == null) continue;

                    // 교량 지오메트리 유효성 검사
                    try
                    {
                        if (bg.IsEmpty()) continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교량 지오메트리 IsEmpty() 검사 중 오류: FID={FID}", bf.GetFID());
                        continue;
                    }

                    var bridgeOid = bf.GetFID();
                    var bridgeName = bf.IsFieldNull(bridgeNameIdx) ? string.Empty : (bf.GetFieldAsString(bridgeNameIdx) ?? string.Empty).Trim();

                    // 교량의 버퍼 영역 내 하천중심선 검색
                    Geometry? buffer = null;
                    try
                    {
                        buffer = bg.Buffer(tolerance, 0);
                        if (buffer == null) continue;
                        
                        // Buffer 결과 유효성 검사
                        try
                        {
                            if (buffer.IsEmpty())
                            {
                                buffer.Dispose();
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "교량 버퍼 지오메트리 IsEmpty() 검사 중 오류: 교량 FID={FID}", bf.GetFID());
                            buffer?.Dispose();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교량 버퍼 생성 중 오류: 교량 FID={FID}", bf.GetFID());
                        buffer?.Dispose();
                        continue;
                    }
                    
                    using (buffer)
                    {
                        foreach (var (riverOid, riverGeom, riverName) in riverFeatures)
                        {
                            using (riverGeom)
                            {
                                // 지오메트리 유효성 검사
                                if (riverGeom == null)
                                {
                                    _logger.LogDebug("하천중심선 지오메트리가 NULL: OID={OID}", riverOid);
                                    continue;
                                }
                                
                                try
                                {
                                    if (riverGeom.IsEmpty())
                                    {
                                        _logger.LogDebug("하천중심선 지오메트리가 비어있음: OID={OID}", riverOid);
                                        continue;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "하천중심선 지오메트리 IsEmpty() 검사 중 오류: OID={OID}", riverOid);
                                    continue;
                                }
                                
                                // 교량과 하천중심선이 교차하거나 근접한지 확인 (NULL 포인터 예외 방지)
                                bool intersects = false;
                                try
                                {
                                    intersects = buffer.Intersects(riverGeom);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "교량-하천중심선 교차 검사 중 오류: 교량 OID={BridgeOID}, 하천 OID={RiverOID}", 
                                        bridgeOid, riverOid);
                                    continue; // 오류 발생 시 다음 하천으로 진행
                                }
                                
                                if (intersects)
                                {
                                    // 하천명 일치 여부 확인
                                    if (!string.IsNullOrEmpty(bridgeName) && !string.IsNullOrEmpty(riverName))
                                    {
                                        if (!string.Equals(bridgeName, riverName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            AddDetailedError(result, "REL_BRIDGE_RIVER_NAME_MISMATCH",
                                                $"교량의 하천명('{bridgeName}')과 하천중심선의 하천명('{riverName}')이 일치하지 않습니다",
                                                config.MainTableId, bridgeOid.ToString(CultureInfo.InvariantCulture),
                                                $"교량 하천명='{bridgeName}', 하천중심선 하천명='{riverName}'", bg);
                                        }
                                        // 일치하거나 불일치하는 경우 모두 처리 완료이므로 다음 교량으로 진행
                                        break;
                                    }
                                }
                            }
                        }
                    } // using (buffer) 블록 종료
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("교량하천명 일치 검사 완료: 교량 {BridgeCount}개, 하천 {RiverCount}개, 소요시간: {Elapsed:F2}초", 
                total, riverFeatures.Count, elapsed);

            // 메모리 정리
            foreach (var (_, geom, _) in riverFeatures)
            {
                geom?.Dispose();
            }

            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        /// <summary>
        /// 대소문자 무시하고 필드 인덱스 찾기
        /// </summary>
        private int GetFieldIndexIgnoreCase(FeatureDefn defn, string fieldName)
        {
            if (defn == null || string.IsNullOrEmpty(fieldName)) return -1;
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                if (fd != null && string.Equals(fd.GetName(), fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        #region 새로운 검수 메서드 (G26, G32, G37, G38, G41, G42, G43, G44, G48, G50)

        /// <summary>
        /// G26 - 경계불일치 검사: 교량/터널/입체교차부가 도로경계면/철도경계면에 포함되어야 함
        /// </summary>
        private void EvaluatePolygonWithinPolygon(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var mainLayer = getLayer(config.MainTableId);
            var relatedLayer = getLayer(config.RelatedTableId);
            if (mainLayer == null || relatedLayer == null)
            {
                _logger.LogWarning("PolygonWithinPolygon: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(mainLayer, fieldFilter);

            var boundaryUnion = BuildUnionGeometryWithCache(relatedLayer, $"{config.RelatedTableId}_UNION");
            if (boundaryUnion == null) return;

            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            mainLayer.ResetReading();
            var totalFeatures = mainLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            _logger.LogInformation("경계불일치 검사 시작: {MainTable} → {RelatedTable}, 필터: {Filter}, 최대반복: {MaxIter}", 
                config.MainTableId, config.RelatedTableId, fieldFilter, useDynamicCounting ? "동적" : maxIterations.ToString());

            Feature? feature;
            while ((feature = mainLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                if (processedCount % 50 == 0 || processedCount == totalFeatures)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, totalFeatures);
                }

                using (feature)
                {
                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    var oid = feature.GetFID().ToString(CultureInfo.InvariantCulture);
                    var code = feature.GetFieldAsString("PG_RDFC_SE") ?? string.Empty;

                    try
                    {
                        // Envelope 기반 사전 필터링
                        var env = new Envelope();
                        geom.GetEnvelope(env);
                        var boundaryEnv = new Envelope();
                        boundaryUnion.GetEnvelope(boundaryEnv);
                        
                        if (env.MaxX < boundaryEnv.MinX || env.MinX > boundaryEnv.MaxX ||
                            env.MaxY < boundaryEnv.MinY || env.MinY > boundaryEnv.MaxY)
                        {
                            // Envelope가 겹치지 않으면 포함 관계 불가능
                            AddDetailedError(result, "REL_POLYGON_NOT_WITHIN",
                                $"{config.MainTableName}이 {config.RelatedTableName}에 포함되지 않습니다",
                                config.MainTableId, oid, $"PG_RDFC_SE={code}", geom);
                            continue;
                        }

                        // 실제 포함 관계 검사
                        bool isWithin = false;
                        try
                        {
                            isWithin = geom.Within(boundaryUnion) || boundaryUnion.Contains(geom);
                            
                            // 허용오차 고려: 거의 포함되는 경우 (차이가 tolerance 이내)
                            if (!isWithin && tolerance > 0)
                            {
                                using var diff = geom.Difference(boundaryUnion);
                                if (diff != null && !diff.IsEmpty())
                                {
                                    var diffArea = Math.Abs(diff.GetArea());
                                    var geomArea = Math.Abs(geom.GetArea());
                                    var onePercentThreshold = _geometryCriteria?.PolygonWithinPolygon1PercentThreshold ?? 0.01;
                                    if (diffArea <= tolerance * tolerance || diffArea / geomArea < onePercentThreshold) // 설정 가능한 1% 임계값
                                    {
                                        isWithin = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "포함 관계 검사 중 오류: OID={OID}", oid);
                            isWithin = false;
                        }

                        if (!isWithin)
                        {
                            AddDetailedError(result, "REL_POLYGON_NOT_WITHIN",
                                $"{config.MainTableName}이 {config.RelatedTableName}에 포함되지 않습니다",
                                config.MainTableId, oid, $"PG_RDFC_SE={code}, 허용오차={tolerance}m", geom);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "경계불일치 검사 중 오류: OID={OID}", oid);
                    }
                }
            }

            _logger.LogInformation("경계불일치 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        /// <summary>
        /// G32 - 실폭하천 하천경계 누락 검사: 실폭하천에 하천경계가 포함되어야 함
        /// </summary>
        private void EvaluatePolygonContainsLine(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var polygonLayer = getLayer(config.MainTableId); // 실폭하천
            var lineLayer = getLayer(config.RelatedTableId); // 하천경계
            if (polygonLayer == null || lineLayer == null)
            {
                _logger.LogWarning("PolygonContainsLine: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            // 실폭하천 Union 생성
            var polygonUnion = BuildUnionGeometryWithCache(polygonLayer, $"{config.MainTableId}_UNION");
            if (polygonUnion == null) return;

            try { polygonUnion = polygonUnion.MakeValid(Array.Empty<string>()); } catch { }

            lineLayer.ResetReading();
            var totalFeatures = lineLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            _logger.LogInformation("실폭하천 하천경계 누락 검사 시작: 하천경계 {Count}개", totalFeatures);

            Feature? feature;
            while ((feature = lineLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                if (processedCount % 50 == 0 || processedCount == totalFeatures)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, totalFeatures);
                }

                using (feature)
                {
                    var lineGeom = feature.GetGeometryRef();
                    if (lineGeom == null || lineGeom.IsEmpty()) continue;

                    var oid = feature.GetFID().ToString(CultureInfo.InvariantCulture);

                    try
                    {
                        // Envelope 기반 사전 필터링
                        var lineEnv = new Envelope();
                        lineGeom.GetEnvelope(lineEnv);
                        var polyEnv = new Envelope();
                        polygonUnion.GetEnvelope(polyEnv);
                        
                        if (lineEnv.MaxX < polyEnv.MinX || lineEnv.MinX > polyEnv.MaxX ||
                            lineEnv.MaxY < polyEnv.MinY || lineEnv.MinY > polyEnv.MaxY)
                        {
                            // Envelope가 겹치지 않으면 포함 관계 불가능
                            AddDetailedError(result, "REL_LINE_NOT_IN_POLYGON",
                                $"하천경계가 실폭하천에 포함되지 않습니다",
                                config.RelatedTableId, oid, string.Empty, lineGeom);
                            continue;
                        }

                        // 실제 포함 관계 검사
                        bool isWithin = false;
                        try
                        {
                            isWithin = lineGeom.Within(polygonUnion) || polygonUnion.Contains(lineGeom);
                            
                            // 허용오차 고려
                            if (!isWithin && tolerance > 0)
                            {
                                using var diff = lineGeom.Difference(polygonUnion);
                                if (diff != null && !diff.IsEmpty())
                                {
                                    var diffLength = Math.Abs(diff.Length());
                                    var lineLength = Math.Abs(lineGeom.Length());
                                    var onePercentThreshold = _geometryCriteria?.PolygonContainsLine1PercentThreshold ?? 0.01;
                                    if (diffLength <= tolerance || diffLength / lineLength < onePercentThreshold) // 설정 가능한 1% 임계값
                                    {
                                        isWithin = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "포함 관계 검사 중 오류: OID={OID}", oid);
                            isWithin = false;
                        }

                        if (!isWithin)
                        {
                            AddDetailedError(result, "REL_LINE_NOT_IN_POLYGON",
                                $"하천경계가 실폭하천에 포함되지 않습니다",
                                config.RelatedTableId, oid, $"허용오차={tolerance}m", lineGeom);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "실폭하천 하천경계 누락 검사 중 오류: OID={OID}", oid);
                    }
                }
            }

            _logger.LogInformation("실폭하천 하천경계 누락 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        /// <summary>
        /// G37 - 경지경계 내부존재 검사: 경지경계 내부에 포함되거나 겹치는 객체 검사 (산지경계 제외)
        /// 성능 최적화: 역 접근법 사용 (경지경계를 순회하고 SetSpatialFilter로 내부 객체 검색)
        /// </summary>
        private void EvaluatePolygonContainsObjects(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var boundaryLayer = getLayer(config.MainTableId); // 경지경계
            if (boundaryLayer == null)
            {
                _logger.LogWarning("PolygonContainsObjects: 레이어를 찾을 수 없습니다: {MainTable}", config.MainTableId);
                return;
            }

            // 성능을 위해 주요 테이블만 검사 (건물, 도로시설 등)
            var targetTables = new[] { "tn_buld", "tn_arrfc", "tn_rodway_bndry", "tn_rodway_ctln" };
            
            // 각 대상 테이블 레이어 미리 로드
            var targetLayers = new Dictionary<string, Layer?>();
            foreach (var tableId in targetTables)
            {
                targetLayers[tableId] = getLayer(tableId);
            }

            // 경지경계 피처 수 확인
            boundaryLayer.ResetReading();
            var boundaryCount = boundaryLayer.GetFeatureCount(1);
            var processedBoundaries = 0;
            var maxIterations = boundaryCount > 0 ? Math.Max(10000, (int)(boundaryCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = boundaryCount == 0;

            _logger.LogInformation("경지경계 내부객체 검사 시작 (역 접근법 최적화): 경지경계 {BoundaryCount}개, 대상 테이블 {TableCount}개", 
                boundaryCount, targetTables.Length);

            var startTime = DateTime.Now;
            var totalErrors = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지: "tableId_oid_boundaryOid"

            // 경지경계를 순회하며 각 경지경계 내부 객체 검사 (역 접근법)
            Feature? boundaryFeature;
            while ((boundaryFeature = boundaryLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedBoundaries++;
                
                if (processedBoundaries > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                if (processedBoundaries % 50 == 0 || processedBoundaries == boundaryCount)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedBoundaries, boundaryCount);
                    var elapsedTime = (DateTime.Now - startTime).TotalSeconds;
                    if (processedBoundaries > 0 && elapsedTime > 0)
                    {
                        var speed = processedBoundaries / elapsedTime;
                        _logger.LogDebug("경지경계 처리 진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 경지경계/초", 
                            processedBoundaries, boundaryCount, (processedBoundaries * 100.0 / boundaryCount), speed);
                    }
                }

                using (boundaryFeature)
                {
                    var boundaryGeom = boundaryFeature.GetGeometryRef();
                    if (boundaryGeom == null || boundaryGeom.IsEmpty()) continue;

                    var boundaryOid = boundaryFeature.GetFID();

                    // 각 대상 테이블에 대해 경지경계 내부 객체 검사
                    foreach (var (tableId, targetLayer) in targetLayers)
                    {
                        if (targetLayer == null) continue;

                        try
                        {
                            // SetSpatialFilter를 사용하여 경지경계와 겹치는 피처만 검색 (GDAL 공간 인덱스 활용)
                            targetLayer.SetSpatialFilter(boundaryGeom);
                            
                            Feature? targetFeature;
                            while ((targetFeature = targetLayer.GetNextFeature()) != null)
                            {
                                token.ThrowIfCancellationRequested();

                                using (targetFeature)
                                {
                                    var targetGeom = targetFeature.GetGeometryRef();
                                    if (targetGeom == null || targetGeom.IsEmpty()) continue;

                                    var targetOid = targetFeature.GetFID();
                                    var pairKey = $"{tableId}_{targetOid}_{boundaryOid}";
                                    
                                    // 중복 검사 방지
                                    if (checkedPairs.Contains(pairKey)) continue;
                                    checkedPairs.Add(pairKey);

                                    try
                                    {
                                        // 실제 포함/겹침 관계 검사
                                        bool isInside = false;
                                        bool isOverlap = false;
                                        
                                        try
                                        {
                                            // SetSpatialFilter로 이미 후보가 필터링되었으므로 빠른 검사만 수행
                                            isInside = targetGeom.Within(boundaryGeom) || boundaryGeom.Contains(targetGeom);
                                            
                                            // 포함되지 않으면 겹침 검사 (더 비용이 큰 연산)
                                            if (!isInside)
                                            {
                                                isOverlap = targetGeom.Overlaps(boundaryGeom) || boundaryGeom.Overlaps(targetGeom);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "포함/겹침 관계 검사 중 오류: OID={OID}, Table={Table}, BoundaryOID={BoundaryOID}", 
                                                targetOid, tableId, boundaryOid);
                                            continue;
                                        }

                                        if (isInside || isOverlap)
                                        {
                                            totalErrors++;
                                            AddDetailedError(result, "REL_OBJECT_IN_FMLND_BOUNDARY",
                                                $"{tableId} 객체가 경지경계 내부에 포함되거나 겹칩니다",
                                                tableId, targetOid.ToString(CultureInfo.InvariantCulture), 
                                                isInside ? "포함" : "겹침", targetGeom);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "경지경계 내부객체 검사 중 오류: OID={OID}, Table={Table}", targetOid, tableId);
                                    }
                                }
                            }

                            // 공간 필터 해제
                            targetLayer.SetSpatialFilter(null);
                            targetLayer.ResetReading();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "경지경계 내부객체 검사 중 테이블 오류: Table={Table}, BoundaryOID={BoundaryOID}", 
                                tableId, boundaryOid);
                            // 필터 해제 시도
                            try
                            {
                                targetLayer?.SetSpatialFilter(null);
                                targetLayer?.ResetReading();
                            }
                            catch { }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("경지경계 내부객체 검사 완료: 경지경계 {BoundaryCount}개 처리, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초 ({Speed:F1} 경지경계/초)", 
                processedBoundaries, totalErrors, elapsed, processedBoundaries > 0 && elapsed > 0 ? processedBoundaries / elapsed : 0);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedBoundaries, processedBoundaries, completed: true);
        }

        /// <summary>
        /// G38 - 홀겹침 객체 검사: 홀과 동일한 객체 존재 여부 검사
        /// </summary>
        private void EvaluateHoleDuplicateCheck(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // 모든 폴리곤 레이어에서 홀 추출
            var allHoles = new List<(Geometry Hole, string SourceTable, long SourceOid, int HoleIndex)>();
            var polygonTables = new[] { "tn_buld", "tn_arrfc", "tn_rodway_bndry", "tn_river_bndry", "tn_fmlnd_bndry" };

            _logger.LogInformation("홀 추출 시작");

            foreach (var tableId in polygonTables)
            {
                var layer = getLayer(tableId);
                if (layer == null) continue;

                layer.ResetReading();
                var featureCount = layer.GetFeatureCount(1);
                var processedCount = 0;
                var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
                var useDynamicCounting = featureCount == 0;

                Feature? feature;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processedCount++;
                    
                    if (processedCount > maxIterations && !useDynamicCounting)
                    {
                        _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                        break;
                    }

                    using (feature)
                    {
                        var geom = feature.GetGeometryRef();
                        if (geom == null || geom.IsEmpty()) continue;

                        var oid = feature.GetFID();
                        var geomType = geom.GetGeometryType();
                        var flatType = (wkbGeometryType)((int)geomType & 0xFF);

                        try
                        {
                            if (flatType == wkbGeometryType.wkbPolygon)
                            {
                                // 단일 폴리곤의 홀 추출
                                var ringCount = geom.GetGeometryCount();
                                for (int i = 1; i < ringCount; i++) // 0번은 외곽 링, 1번부터가 홀
                                {
                                    using var ring = geom.GetGeometryRef(i);
                                    if (ring != null && !ring.IsEmpty())
                                    {
                                        // 홀을 폴리곤으로 변환
                                        using var holePoly = new Geometry(wkbGeometryType.wkbPolygon);
                                        using var holeRing = ring.Clone();
                                        holePoly.AddGeometry(holeRing);
                                        allHoles.Add((holePoly.Clone(), tableId, oid, i));
                                    }
                                }
                            }
                            else if (flatType == wkbGeometryType.wkbMultiPolygon)
                            {
                                // 멀티폴리곤의 각 폴리곤에서 홀 추출
                                var polyCount = geom.GetGeometryCount();
                                for (int p = 0; p < polyCount; p++)
                                {
                                    using var poly = geom.GetGeometryRef(p);
                                    if (poly == null) continue;
                                    
                                    var ringCount = poly.GetGeometryCount();
                                    for (int i = 1; i < ringCount; i++)
                                    {
                                        using var ring = poly.GetGeometryRef(i);
                                        if (ring != null && !ring.IsEmpty())
                                        {
                                            using var holePoly = new Geometry(wkbGeometryType.wkbPolygon);
                                            using var holeRing = ring.Clone();
                                            holePoly.AddGeometry(holeRing);
                                            allHoles.Add((holePoly.Clone(), tableId, oid, i));
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "홀 추출 중 오류: OID={OID}, Table={Table}", oid, tableId);
                        }
                    }
                }
            }

            _logger.LogInformation("홀 추출 완료: {Count}개", allHoles.Count);

            // 모든 객체와 홀 비교 (성능 최적화: Envelope 기반 사전 필터링)
            var totalProcessed = 0;
            var totalErrors = 0;

            foreach (var tableId in polygonTables)
            {
                var layer = getLayer(tableId);
                if (layer == null) continue;

                layer.ResetReading();
                var featureCount = layer.GetFeatureCount(1);
                var processedCount = 0;
                var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
                var useDynamicCounting = featureCount == 0;

                Feature? feature;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processedCount++;
                    totalProcessed++;
                    
                    if (processedCount > maxIterations && !useDynamicCounting)
                    {
                        _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                        break;
                    }

                    using (feature)
                    {
                        var geom = feature.GetGeometryRef();
                        if (geom == null || geom.IsEmpty()) continue;

                        var oid = feature.GetFID();
                        var geomEnv = new Envelope();
                        geom.GetEnvelope(geomEnv);

                        // Envelope 기반 사전 필터링으로 후보 홀만 검사
                        foreach (var (holeGeom, sourceTable, sourceOid, holeIdx) in allHoles)
                        {
                            try
                            {
                                var holeEnv = new Envelope();
                                holeGeom.GetEnvelope(holeEnv);
                                
                                // Envelope가 겹치지 않으면 스킵
                                if (geomEnv.MaxX < holeEnv.MinX || geomEnv.MinX > holeEnv.MaxX ||
                                    geomEnv.MaxY < holeEnv.MinY || geomEnv.MinY > holeEnv.MaxY)
                                {
                                    continue;
                                }

                                // 실제 지오메트리 비교
                                bool isEqual = false;
                                try
                                {
                                    isEqual = geom.Equals(holeGeom) || 
                                             (geom.Within(holeGeom) && holeGeom.Within(geom) && 
                                              Math.Abs(geom.GetArea() - holeGeom.GetArea()) < tolerance * tolerance);
                                }
                                catch
                                {
                                    continue;
                                }

                                if (isEqual)
                                {
                                    totalErrors++;
                                    AddDetailedError(result, "REL_HOLE_DUPLICATE",
                                        $"홀과 동일한 객체가 존재합니다 (홀 출처: {sourceTable} OID={sourceOid}, 홀 인덱스={holeIdx})",
                                        tableId, oid.ToString(CultureInfo.InvariantCulture), 
                                        $"홀 출처={sourceTable}:{sourceOid}", geom);
                                    break; // 하나만 찾으면 충분
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "홀 중복 검사 중 오류: OID={OID}, Table={Table}", oid, tableId);
                            }
                        }
                    }
                }
            }

            // 메모리 정리
            foreach (var (holeGeom, _, _, _) in allHoles)
            {
                holeGeom?.Dispose();
            }

            _logger.LogInformation("홀겹침 객체 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", totalProcessed, totalErrors);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, totalProcessed, totalProcessed, completed: true);
        }

        // 나머지 메서드들은 다음 단계에서 구현 (파일 크기 제한으로 인해)
        // G41, G42, G43, G44, G48, G50

        private void EvaluateLineConnectivityWithFilter(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G41 - 소로 미연결 검사: LineConnectivity와 동일하지만 필터 적용
            // 기존 EvaluateLineConnectivity 재사용
            EvaluateLineConnectivity(ds, getLayer, result, tolerance, fieldFilter, token, config);
        }

        private void EvaluateLineDisconnection(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G42 - 도로중심선 단절 검사: 연결되지 않은 끝점 검사
            // LineConnectivity의 역 검사
            var layer = getLayer(config.MainTableId);
            if (layer == null) return;

            layer.ResetReading();
            var totalFeatures = layer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            // 끝점 인덱스 구축 (기존 로직 재사용)
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var allSegments = new List<LineSegmentInfo>();
            double gridSize = Math.Max(tolerance, 1.0);

            Feature? feature;
            while ((feature = layer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (feature)
                {
                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    ExtractLineSegments(geom, oid, allSegments, endpointIndex, gridSize);
                }
            }

            // 연결되지 않은 끝점 검사
            var disconnectedEndpoints = new HashSet<long>();
            foreach (var segment in allSegments)
            {
                var startCandidates = SearchEndpointsNearby(endpointIndex, segment.StartX, segment.StartY, tolerance);
                var endCandidates = SearchEndpointsNearby(endpointIndex, segment.EndX, segment.EndY, tolerance);

                bool startConnected = startCandidates.Any(c => c.Oid != segment.Oid && 
                    Distance(segment.StartX, segment.StartY, c.X, c.Y) <= tolerance);
                bool endConnected = endCandidates.Any(c => c.Oid != segment.Oid && 
                    Distance(segment.EndX, segment.EndY, c.X, c.Y) <= tolerance);

                if (!startConnected && !endConnected)
                {
                    disconnectedEndpoints.Add(segment.Oid);
                }
            }

            foreach (var oid in disconnectedEndpoints)
            {
                var geom = GetGeometryByOID(layer, oid);
                AddDetailedError(result, "REL_LINE_DISCONNECTED",
                    "도로중심선이 중간에 단절되었습니다",
                    config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom);
                geom?.Dispose();
            }

            _logger.LogInformation("도로중심선 단절 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, disconnectedEndpoints.Count);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        private void EvaluateLineDisconnectionWithAttribute(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G43 - 도로경계선 단절 검사: 동일 ROAD_SE를 가진 도로경계선이 단절
            // LineDisconnection과 동일하지만 속성 필터 적용
            var layer = getLayer(config.MainTableId);
            if (layer == null) return;

            // 속성별로 그룹화하여 검사
            var segmentsByAttribute = new Dictionary<string, List<(long Oid, double StartX, double StartY, double EndX, double EndY)>>();

            layer.ResetReading();
            var totalFeatures = layer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            Feature? feature;
            while ((feature = layer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (feature)
                {
                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    var attrValue = feature.GetFieldAsString(fieldFilter) ?? "UNKNOWN";
                    
                    if (!segmentsByAttribute.ContainsKey(attrValue))
                    {
                        segmentsByAttribute[attrValue] = new List<(long, double, double, double, double)>();
                    }

                    ExtractLineEndpoints(geom, oid, segmentsByAttribute[attrValue]);
                }
            }

            // 각 속성값별로 단절 검사
            var totalErrors = 0;
            foreach (var (attrValue, segments) in segmentsByAttribute)
            {
                var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
                double gridSize = Math.Max(tolerance, 1.0);

                foreach (var (oid, sx, sy, ex, ey) in segments)
                {
                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, gridSize);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, gridSize);
                }

                foreach (var (oid, sx, sy, ex, ey) in segments)
                {
                    var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                    bool startConnected = startCandidates.Any(c => c.Oid != oid && 
                        Distance(sx, sy, c.X, c.Y) <= tolerance);
                    bool endConnected = endCandidates.Any(c => c.Oid != oid && 
                        Distance(ex, ey, c.X, c.Y) <= tolerance);

                    if (!startConnected && !endConnected)
                    {
                        totalErrors++;
                        var geom = GetGeometryByOID(layer, oid);
                        AddDetailedError(result, "REL_LINE_DISCONNECTED_WITH_ATTR",
                            $"동일 {fieldFilter}({attrValue})를 가진 도로경계선이 단절되었습니다",
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"{fieldFilter}={attrValue}", geom);
                        geom?.Dispose();
                    }
                }
            }

            _logger.LogInformation("도로경계선 단절 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, totalErrors);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        private void EvaluatePolygonBoundaryMatch(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G44 - 도로면형-경계선 불일치 검사: 도로 면형과 경계선이 일치해야 함
            var polygonLayer = getLayer(config.MainTableId); // 도로경계면
            var lineLayer = getLayer(config.RelatedTableId); // 도로경계선
            if (polygonLayer == null || lineLayer == null) return;

            _logger.LogInformation("관계 검수 시작: RuleId={RuleId}, CaseType={CaseType}, MainTable={MainTable}, RelatedTable={RelatedTable}", 
                config.RuleId, config.CaseType, config.MainTableId, config.RelatedTableId);

            polygonLayer.ResetReading();
            var totalFeatures = polygonLayer.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: MainTable={MainTable} {Count}개", config.MainTableId, totalFeatures);
            
            var startTime = DateTime.Now;
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            Feature? feature;
            while ((feature = polygonLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                if (processedCount % 50 == 0 || processedCount == totalFeatures)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, totalFeatures);
                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    if (processedCount > 0 && elapsed > 0)
                    {
                        var speed = processedCount / elapsed;
                        _logger.LogDebug("진행률: {Processed}/{Total} ({Percent:F1}%), 속도: {Speed:F1} 피처/초", 
                            processedCount, totalFeatures, (processedCount * 100.0 / totalFeatures), speed);
                    }
                }

                using (feature)
                {
                    var polyGeom = feature.GetGeometryRef();
                    if (polyGeom == null || polyGeom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    using var boundary = polyGeom.GetBoundary();
                    if (boundary == null || boundary.IsEmpty()) continue;

                    // 도로경계선과 비교
                    lineLayer.SetSpatialFilter(polyGeom);
                    bool foundMatch = false;

                    Feature? lineFeature;
                    while ((lineFeature = lineLayer.GetNextFeature()) != null)
                    {
                        using (lineFeature)
                        {
                            var lineGeom = lineFeature.GetGeometryRef();
                            if (lineGeom == null || lineGeom.IsEmpty()) continue;

                            try
                            {
                                // 경계선이 폴리곤 경계와 일치하는지 검사
                                using var intersection = boundary.Intersection(lineGeom);
                                if (intersection != null && !intersection.IsEmpty())
                                {
                                    var intersectionLength = Math.Abs(intersection.Length());
                                    var boundaryLength = Math.Abs(boundary.Length());
                                    
                                    if (boundaryLength <= 0)
                                    {
                                        // 경계 길이가 0이면 일치로 간주
                                        foundMatch = true;
                                        break;
                                    }
                                    
                                    // Tolerance를 고려한 일치율 계산
                                    // Tolerance는 거리 단위이므로, 경계 길이에 대한 상대적 허용 오차로 변환
                                    // 예: tolerance=0.1m, boundaryLength=10m -> 허용 오차 = 0.1/10 = 1%
                                    // 최소 일치 비율 = 1.0 - (tolerance / boundaryLength)
                                    // 단, boundaryLength가 tolerance보다 작으면 최소 0.5 (50%) 이상 일치 요구
                                    var toleranceRatio = boundaryLength > tolerance 
                                        ? tolerance / boundaryLength 
                                        : 0.5; // 경계 길이가 tolerance보다 작으면 50% 허용 오차
                                    
                                    var minMatchRatio = Math.Max(0.5, 1.0 - toleranceRatio); // 최소 50% 일치 요구
                                    var actualMatchRatio = intersectionLength / boundaryLength;
                                    
                                    if (actualMatchRatio >= minMatchRatio)
                                    {
                                        foundMatch = true;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }

                    lineLayer.SetSpatialFilter(null);

                    if (!foundMatch)
                    {
                        AddDetailedError(result, "REL_POLYGON_BOUNDARY_MISMATCH",
                            "도로 면형과 경계선이 일치하지 않습니다",
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), string.Empty, polyGeom);
                    }
                }
            }

            _logger.LogInformation("도로면형-경계선 불일치 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        private void EvaluateLineEndpointWithinPolygon(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G48 - 선형 끝점 초과미달 검사: 중심선 끝점이 경계면 내부에 포함되어야 함 (0.3m 범위)
            var lineLayer = getLayer(config.MainTableId); // 중심선
            var polygonLayer = getLayer(config.RelatedTableId); // 경계면
            if (lineLayer == null || polygonLayer == null) return;

            var polygonUnion = BuildUnionGeometryWithCache(polygonLayer, $"{config.RelatedTableId}_UNION");
            if (polygonUnion == null) return;

            try { polygonUnion = polygonUnion.MakeValid(Array.Empty<string>()); } catch { }

            lineLayer.ResetReading();
            var totalFeatures = lineLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            Feature? feature;
            while ((feature = lineLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (feature)
                {
                    var lineGeom = feature.GetGeometryRef();
                    if (lineGeom == null || lineGeom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    var pointCount = lineGeom.GetPointCount();
                    if (pointCount < 2) continue;

                    // 시작점과 끝점 추출
                    double startX = lineGeom.GetX(0);
                    double startY = lineGeom.GetY(0);
                    double endX = lineGeom.GetX(pointCount - 1);
                    double endY = lineGeom.GetY(pointCount - 1);

                    using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                    startPt.AddPoint(startX, startY, 0);
                    using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                    endPt.AddPoint(endX, endY, 0);

                    // 끝점이 경계면 내부에 포함되는지 검사 (tolerance 범위 내)
                    bool startWithin = false;
                    bool endWithin = false;

                    try
                    {
                        startWithin = polygonUnion.Contains(startPt) || startPt.Within(polygonUnion);
                        endWithin = polygonUnion.Contains(endPt) || endPt.Within(polygonUnion);

                        // 허용오차 고려: 버퍼 생성하여 검사
                        if (!startWithin && tolerance > 0)
                        {
                            using var startBuffer = startPt.Buffer(tolerance, 8);
                            startWithin = polygonUnion.Intersects(startBuffer) || polygonUnion.Contains(startBuffer);
                        }

                        if (!endWithin && tolerance > 0)
                        {
                            using var endBuffer = endPt.Buffer(tolerance, 8);
                            endWithin = polygonUnion.Intersects(endBuffer) || polygonUnion.Contains(endBuffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "끝점 포함 검사 중 오류: OID={OID}", oid);
                        continue;
                    }

                    if (!startWithin)
                    {
                        AddDetailedError(result, "REL_LINE_ENDPOINT_OUTSIDE",
                            "중심선 시작점이 경계면 내부에 포함되지 않습니다",
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"허용오차={tolerance}m", lineGeom);
                    }

                    if (!endWithin)
                    {
                        AddDetailedError(result, "REL_LINE_ENDPOINT_OUTSIDE",
                            "중심선 끝점이 경계면 내부에 포함되지 않습니다",
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"허용오차={tolerance}m", lineGeom);
                    }
                }
            }

            _logger.LogInformation("선형 끝점 초과미달 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        private void EvaluateDefectiveConnection(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            // G50 - 결함있는 연결 검사: 중심선 연결 결함 검사
            var centerlineLayer = getLayer(config.MainTableId); // 중심선
            var boundaryLayer = getLayer(config.RelatedTableId); // 경계면
            if (centerlineLayer == null || boundaryLayer == null) return;

            var boundaryUnion = BuildUnionGeometryWithCache(boundaryLayer, $"{config.RelatedTableId}_UNION");
            if (boundaryUnion == null) return;

            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            // 끝점 인덱스 구축
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var allSegments = new List<LineSegmentInfo>();
            double gridSize = Math.Max(tolerance, 1.0);

            centerlineLayer.ResetReading();
            var totalFeatures = centerlineLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            Feature? feature;
            while ((feature = centerlineLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (feature)
                {
                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    ExtractLineSegments(geom, oid, allSegments, endpointIndex, gridSize);
                }
            }

            // 결함 검사
            foreach (var segment in allSegments)
            {
                var startCandidates = SearchEndpointsNearby(endpointIndex, segment.StartX, segment.StartY, tolerance);
                var endCandidates = SearchEndpointsNearby(endpointIndex, segment.EndX, segment.EndY, tolerance);

                // 1. 한점에서 불일치하거나 1m 미만인지 확인
                bool startConnected = startCandidates.Any(c => c.Oid != segment.Oid && 
                    Distance(segment.StartX, segment.StartY, c.X, c.Y) <= tolerance);
                bool endConnected = endCandidates.Any(c => c.Oid != segment.Oid && 
                    Distance(segment.EndX, segment.EndY, c.X, c.Y) <= tolerance);

                // 2. 끝점에 붙어있지 않으면 바운더리 면형에 붙어있어야 함
                if (!startConnected)
                {
                    using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                    startPt.AddPoint(segment.StartX, segment.StartY, 0);
                    bool nearBoundary = false;
                    try
                    {
                        using var buffer = startPt.Buffer(tolerance, 8);
                        nearBoundary = boundaryUnion.Intersects(buffer) || boundaryUnion.Contains(buffer);
                    }
                    catch { }

                    if (!nearBoundary)
                    {
                        var geom = GetGeometryByOID(centerlineLayer, segment.Oid);
                        AddDetailedError(result, "REL_DEFECTIVE_CONNECTION",
                            "중심선 시작점이 다른 중심선에 붙어있지 않고 바운더리 면형에도 붙어있지 않습니다",
                            config.MainTableId, segment.Oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom);
                        geom?.Dispose();
                    }
                }

                if (!endConnected)
                {
                    using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                    endPt.AddPoint(segment.EndX, segment.EndY, 0);
                    bool nearBoundary = false;
                    try
                    {
                        using var buffer = endPt.Buffer(tolerance, 8);
                        nearBoundary = boundaryUnion.Intersects(buffer) || boundaryUnion.Contains(buffer);
                    }
                    catch { }

                    if (!nearBoundary)
                    {
                        var geom = GetGeometryByOID(centerlineLayer, segment.Oid);
                        AddDetailedError(result, "REL_DEFECTIVE_CONNECTION",
                            "중심선 끝점이 다른 중심선에 붙어있지 않고 바운더리 면형에도 붙어있지 않습니다",
                            config.MainTableId, segment.Oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom);
                        geom?.Dispose();
                    }
                }

                // 3. 중심선의 끝점이 다른 중심선에 붙어있으면 오류 (교차지점은 단락되어야 함)
                if (startConnected || endConnected)
                {
                    // 이미 연결되어 있으므로 정상 (이 조건은 추가 검사 필요)
                }
            }

            _logger.LogInformation("결함있는 연결 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", processedCount, result.ErrorCount);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processedCount, processedCount, completed: true);
        }

        /// <summary>
        /// 선분의 끝점 추출 헬퍼
        /// </summary>
        private void ExtractLineEndpoints(Geometry geom, long oid, List<(long Oid, double StartX, double StartY, double EndX, double EndY)> segments)
        {
            if (geom == null || geom.IsEmpty()) return;

            var geomType = geom.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            if (flatType == wkbGeometryType.wkbLineString)
            {
                var pointCount = geom.GetPointCount();
                if (pointCount >= 2)
                {
                    double startX = geom.GetX(0);
                    double startY = geom.GetY(0);
                    double endX = geom.GetX(pointCount - 1);
                    double endY = geom.GetY(pointCount - 1);
                    segments.Add((oid, startX, startY, endX, endY));
                }
            }
            else if (flatType == wkbGeometryType.wkbMultiLineString)
            {
                var geomCount = geom.GetGeometryCount();
                for (int i = 0; i < geomCount; i++)
                {
                    using var line = geom.GetGeometryRef(i);
                    if (line != null && !line.IsEmpty())
                    {
                        ExtractLineEndpoints(line, oid, segments);
                    }
                }
            }
        }

        /// <summary>
        /// 선분 정보 추출 헬퍼 (LineSegmentInfo 리스트에 추가)
        /// </summary>
        private void ExtractLineSegments(Geometry geom, long oid, List<LineSegmentInfo> segments, Dictionary<string, List<EndpointInfo>> endpointIndex, double gridSize)
        {
            if (geom == null || geom.IsEmpty()) return;

            var geomType = geom.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            if (flatType == wkbGeometryType.wkbLineString)
            {
                var pointCount = geom.GetPointCount();
                if (pointCount >= 2)
                {
                    var sx = geom.GetX(0);
                    var sy = geom.GetY(0);
                    var ex = geom.GetX(pointCount - 1);
                    var ey = geom.GetY(pointCount - 1);

                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = geom.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    segments.Add(segmentInfo);

                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, gridSize);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, gridSize);
                }
            }
            else if (flatType == wkbGeometryType.wkbMultiLineString)
            {
                var geomCount = geom.GetGeometryCount();
                for (int i = 0; i < geomCount; i++)
                {
                    using var line = geom.GetGeometryRef(i);
                    if (line != null && !line.IsEmpty())
                    {
                        ExtractLineSegments(line, oid, segments, endpointIndex, gridSize);
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// G33 - 차도간 교차검사 (선형): 동일 속성값을 가진 선형 객체 간 교차 검사
        /// </summary>
        private void EvaluateLineIntersectionWithAttribute(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            // FieldFilter에 속성 필드명 지정 (예: "road_se")
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return;
            }

            var attributeField = fieldFilter.Trim();
            _logger.LogInformation("차도간 교차검사 시작 (선형): 레이어={Layer}, 속성필드={Field}", config.MainTableId, attributeField);
            var startTime = DateTime.Now;

            // 모든 선형 피처 수집 (속성값 포함)
            line.ResetReading();
            var featureCount = line.GetFeatureCount(1);
            var allLines = new List<(long Oid, Geometry Geom, string? AttrValue)>();
            var defn = line.GetLayerDefn();
            int attrFieldIdx = GetFieldIndexIgnoreCase(defn, attributeField);
            
            if (attrFieldIdx < 0)
            {
                _logger.LogWarning("속성 필드를 찾을 수 없습니다: {Field}", attributeField);
                return;
            }

            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var attrValue = f.IsFieldNull(attrFieldIdx) ? null : f.GetFieldAsString(attrFieldIdx);
                    allLines.Add((oid, g.Clone(), attrValue));
                }
            }

            _logger.LogInformation("선형 피처 수집 완료: {Count}개", allLines.Count);

            // 동일 속성값을 가진 선형 객체 간 교차 검사
            var total = allLines.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지
            var errorCount = 0;

            for (int i = 0; i < allLines.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var (oid1, geom1, attr1) = allLines[i];
                if (string.IsNullOrWhiteSpace(attr1)) continue; // 속성값이 없으면 스킵

                for (int j = i + 1; j < allLines.Count; j++)
                {
                    var (oid2, geom2, attr2) = allLines[j];
                    if (string.IsNullOrWhiteSpace(attr2)) continue;
                    
                    // 동일 속성값인 경우에만 검사
                    if (!attr1.Equals(attr2, StringComparison.OrdinalIgnoreCase)) continue;

                    var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                    if (checkedPairs.Contains(pairKey)) continue;
                    checkedPairs.Add(pairKey);

                    try
                    {
                        // Envelope 기반 사전 필터링
                        var env1 = new Envelope();
                        geom1.GetEnvelope(env1);
                        var env2 = new Envelope();
                        geom2.GetEnvelope(env2);
                        
                        bool envelopesIntersect = !(env1.MaxX < env2.MinX || env1.MinX > env2.MaxX ||
                                                     env1.MaxY < env2.MinY || env1.MinY > env2.MaxY);
                        if (!envelopesIntersect) continue;

                        // 교차 여부 확인
                        if (geom1.Intersects(geom2))
                        {
                            using var intersection = geom1.Intersection(geom2);
                            if (intersection != null && !intersection.IsEmpty())
                            {
                                var intersectionType = intersection.GetGeometryType();
                                // 점이 아닌 교차(선 또는 면)인 경우만 오류
                                if (intersectionType != wkbGeometryType.wkbPoint && 
                                    intersectionType != wkbGeometryType.wkbMultiPoint)
                                {
                                    errorCount++;
                                    var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                    var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                    var (x, y) = ExtractCentroid(intersection);
                                    AddDetailedError(result, "REL_LINE_INTERSECTION_SAME_ATTR",
                                        $"동일 {attributeField}({attr1})를 가진 선형 객체가 교차함: OID {oid1Str} <-> {oid2Str}",
                                        config.MainTableId, oid1Str, $"교차 객체: {oid2Str}", intersection);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교차 검사 중 오류: OID={Oid1} vs {Oid2}", oid1, oid2);
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("차도간 교차검사 완료 (선형): 처리 {Count}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초", 
                total, errorCount, elapsed);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);

            // 메모리 정리
            foreach (var (_, geom, _) in allLines)
            {
                geom?.Dispose();
            }
        }

        /// <summary>
        /// G33 - 차도간 교차검사 (면형): 동일 속성값을 가진 폴리곤 객체 간 교차 검사
        /// </summary>
        private void EvaluatePolygonIntersectionWithAttribute(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var poly = getLayer(config.MainTableId);
            if (poly == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            // FieldFilter에 속성 필드명 지정 (예: "road_se")
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return;
            }

            var attributeField = fieldFilter.Trim();
            _logger.LogInformation("차도간 교차검사 시작 (면형): 레이어={Layer}, 속성필드={Field}, 허용오차={Tolerance}", 
                config.MainTableId, attributeField, tolerance);
            var startTime = DateTime.Now;

            // 모든 폴리곤 피처 수집 (속성값 포함)
            poly.ResetReading();
            var featureCount = poly.GetFeatureCount(1);
            var allPolygons = new List<(long Oid, Geometry Geom, string? AttrValue)>();
            var defn = poly.GetLayerDefn();
            int attrFieldIdx = GetFieldIndexIgnoreCase(defn, attributeField);
            
            if (attrFieldIdx < 0)
            {
                _logger.LogWarning("속성 필드를 찾을 수 없습니다: {Field}", attributeField);
                return;
            }

            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            Feature? f;
            while ((f = poly.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var attrValue = f.IsFieldNull(attrFieldIdx) ? null : f.GetFieldAsString(attrFieldIdx);
                    allPolygons.Add((oid, g.Clone(), attrValue));
                }
            }

            _logger.LogInformation("폴리곤 피처 수집 완료: {Count}개", allPolygons.Count);

            // 동일 속성값을 가진 폴리곤 객체 간 교차 검사
            var total = allPolygons.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지
            var errorCount = 0;

            for (int i = 0; i < allPolygons.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                var (oid1, geom1, attr1) = allPolygons[i];
                if (string.IsNullOrWhiteSpace(attr1)) continue; // 속성값이 없으면 스킵

                for (int j = i + 1; j < allPolygons.Count; j++)
                {
                    var (oid2, geom2, attr2) = allPolygons[j];
                    if (string.IsNullOrWhiteSpace(attr2)) continue;
                    
                    // 동일 속성값인 경우에만 검사
                    if (!attr1.Equals(attr2, StringComparison.OrdinalIgnoreCase)) continue;

                    var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                    if (checkedPairs.Contains(pairKey)) continue;
                    checkedPairs.Add(pairKey);

                    try
                    {
                        // Envelope 기반 사전 필터링
                        var env1 = new Envelope();
                        geom1.GetEnvelope(env1);
                        var env2 = new Envelope();
                        geom2.GetEnvelope(env2);
                        
                        bool envelopesIntersect = !(env1.MaxX < env2.MinX || env1.MinX > env2.MaxX ||
                                                     env1.MaxY < env2.MinY || env1.MinY > env2.MaxY);
                        if (!envelopesIntersect) continue;

                        // 교차 여부 확인
                        using var intersection = geom1.Intersection(geom2);
                        if (intersection != null && !intersection.IsEmpty())
                        {
                            var area = GetSurfaceArea(intersection);
                            // 겹침 면적이 tolerance를 초과하는 경우만 오류
                            if (area > tolerance)
                            {
                                errorCount++;
                                var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                var (x, y) = ExtractCentroid(intersection);
                                AddDetailedError(result, "REL_POLYGON_INTERSECTION_SAME_ATTR",
                                    $"동일 {attributeField}({attr1})를 가진 폴리곤 객체가 교차함 (겹침 면적: {area:F2}㎡): OID {oid1Str} <-> {oid2Str}",
                                    config.MainTableId, oid1Str, $"교차 객체: {oid2Str}", intersection);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "교차 검사 중 오류: OID={Oid1} vs {Oid2}", oid1, oid2);
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("차도간 교차검사 완료 (면형): 처리 {Count}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초", 
                total, errorCount, elapsed);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);

            // 메모리 정리
            foreach (var (_, geom, _) in allPolygons)
            {
                geom?.Dispose();
            }
        }

        /// <summary>
        /// G34 - 차도와 도로시설물관계 검사: 도로경계면/중심선에 도로시설 레이어가 공간중첩되나 속성이 없거나, 속성이 있으나 레이어가 없는 경우
        /// </summary>
        private void EvaluateAttributeSpatialMismatch(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, double tolerance, CancellationToken token, RelationCheckConfig config)
        {
            var mainLayer = getLayer(config.MainTableId); // 도로경계면 또는 중심선
            var facilityLayer = getLayer(config.RelatedTableId); // 도로시설 레이어
            if (mainLayer == null || facilityLayer == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: Main={MainTable}, Related={RelatedTable}", 
                    config.MainTableId, config.RelatedTableId);
                return;
            }

            // FieldFilter 형식: "road_se;pg_rdfc_se" (도로 속성 필드;도로시설 속성 필드)
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return;
            }

            var fields = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                _logger.LogWarning("속성 필드명이 올바르게 지정되지 않았습니다: {FieldFilter} (형식: '도로필드;도로시설필드')", fieldFilter);
                return;
            }

            var roadField = fields[0];
            var facilityField = fields[1];

            _logger.LogInformation("차도와 도로시설물관계 검사 시작: 도로={RoadLayer}, 도로시설={FacilityLayer}, 도로필드={RoadField}, 시설필드={FacilityField}, 허용오차={Tolerance}", 
                config.MainTableId, config.RelatedTableId, roadField, facilityField, tolerance);
            var startTime = DateTime.Now;

            // 도로 레이어 필드 인덱스 확인
            var roadDefn = mainLayer.GetLayerDefn();
            int roadFieldIdx = GetFieldIndexIgnoreCase(roadDefn, roadField);
            if (roadFieldIdx < 0)
            {
                _logger.LogWarning("도로 속성 필드를 찾을 수 없습니다: {Field}", roadField);
                return;
            }

            // 도로시설 레이어 필드 인덱스 확인
            var facilityDefn = facilityLayer.GetLayerDefn();
            int facilityFieldIdx = GetFieldIndexIgnoreCase(facilityDefn, facilityField);
            if (facilityFieldIdx < 0)
            {
                _logger.LogWarning("도로시설 속성 필드를 찾을 수 없습니다: {Field}", facilityField);
                return;
            }

            // 도로 피처 수집
            mainLayer.ResetReading();
            var roadFeatures = new List<(long Oid, Geometry Geom, string? RoadAttr)>();
            var roadFeatureCount = mainLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = roadFeatureCount > 0 ? Math.Max(10000, (int)(roadFeatureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = roadFeatureCount == 0;

            Feature? f;
            while ((f = mainLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var roadAttr = f.IsFieldNull(roadFieldIdx) ? null : f.GetFieldAsString(roadFieldIdx);
                    roadFeatures.Add((oid, g.Clone(), roadAttr));
                }
            }

            _logger.LogInformation("도로 피처 수집 완료: {Count}개", roadFeatures.Count);

            // 도로시설 피처 수집 (속성값 포함)
            facilityLayer.ResetReading();
            var facilityFeatures = new List<(long Oid, Geometry Geom, string? FacilityAttr)>();
            var facilityFeatureCount = facilityLayer.GetFeatureCount(1);
            processedCount = 0;
            maxIterations = facilityFeatureCount > 0 ? Math.Max(10000, (int)(facilityFeatureCount * 2.0)) : int.MaxValue;
            useDynamicCounting = facilityFeatureCount == 0;

            while ((f = facilityLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var facilityAttr = f.IsFieldNull(facilityFieldIdx) ? null : f.GetFieldAsString(facilityFieldIdx);
                    facilityFeatures.Add((oid, g.Clone(), facilityAttr));
                }
            }

            _logger.LogInformation("도로시설 피처 수집 완료: {Count}개", facilityFeatures.Count);

            // 검사 1: 도로에 도로시설 레이어가 공간중첩되나 속성이 없는 경우
            var errorCount = 0;
            var checkedPairs = new HashSet<string>();

            foreach (var (roadOid, roadGeom, roadAttr) in roadFeatures)
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(roadAttr)) continue; // 도로 속성이 없으면 스킵

                try
                {
                    // 도로 지오메트리와 중첩되는 도로시설 검색
                    facilityLayer.SetSpatialFilter(roadGeom);

                    Feature? facilityF;
                    while ((facilityF = facilityLayer.GetNextFeature()) != null)
                    {
                        using (facilityF)
                        {
                            var facilityGeom = facilityF.GetGeometryRef();
                            if (facilityGeom == null || facilityGeom.IsEmpty()) continue;

                            var facilityOid = facilityF.GetFID();
                            var pairKey = $"{roadOid}_{facilityOid}";
                            if (checkedPairs.Contains(pairKey)) continue;
                            checkedPairs.Add(pairKey);

                            // 공간 중첩 확인
                            bool isOverlapping = false;
                            try
                            {
                                if (roadGeom.Overlaps(facilityGeom) || facilityGeom.Overlaps(roadGeom) ||
                                    roadGeom.Contains(facilityGeom) || facilityGeom.Within(roadGeom))
                                {
                                    isOverlapping = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "공간 중첩 확인 중 오류: RoadOID={RoadOid}, FacilityOID={FacilityOid}", roadOid, facilityOid);
                                continue;
                            }

                            if (isOverlapping)
                            {
                                var facilityAttr = facilityF.IsFieldNull(facilityFieldIdx) ? null : facilityF.GetFieldAsString(facilityFieldIdx);
                                
                                // 오류 1: 도로시설 레이어가 중첩되나 속성이 없는 경우
                                if (string.IsNullOrWhiteSpace(facilityAttr))
                                {
                                    errorCount++;
                                    var roadOidStr = roadOid.ToString(CultureInfo.InvariantCulture);
                                    var facilityOidStr = facilityOid.ToString(CultureInfo.InvariantCulture);
                                    var (x, y) = ExtractCentroid(facilityGeom);
                                    AddDetailedError(result, "REL_ATTRIBUTE_SPATIAL_MISMATCH",
                                        $"도로({roadField}={roadAttr})에 도로시설 레이어가 중첩되나 {facilityField} 속성이 없음: OID {facilityOidStr}",
                                        config.RelatedTableId, facilityOidStr, $"도로 OID: {roadOidStr}", facilityGeom);
                                }
                            }
                        }
                    }

                    facilityLayer.SetSpatialFilter(null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "도로시설 검사 중 오류: RoadOID={RoadOid}", roadOid);
                    try { facilityLayer?.SetSpatialFilter(null); } catch { }
                }
            }

            // 검사 2: 도로에 도로시설물 속성이 있으나 레이어가 없는 경우
            // (도로 속성값에 도로시설 코드가 포함되어 있으나 실제 레이어에는 해당 피처가 없는 경우)
            // 이 검사는 복잡하므로, 도로 속성값을 파싱하여 도로시설 코드 목록을 추출하고,
            // 해당 코드를 가진 도로시설 피처가 공간적으로 중첩되는지 확인해야 함
            // 현재는 검사 1만 구현 (검사 2는 추가 분석 필요)

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("차도와 도로시설물관계 검사 완료: 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초", 
                errorCount, elapsed);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, roadFeatures.Count, roadFeatures.Count, completed: true);

            // 메모리 정리
            foreach (var (_, geom, _) in roadFeatures)
            {
                geom?.Dispose();
            }
            foreach (var (_, geom, _) in facilityFeatures)
            {
                geom?.Dispose();
            }
        }

        /// <summary>
        /// G39 - 표고점 위치간격 검사: 표고점 간 최소 거리 검사 (축척별/도로별)
        /// </summary>
        private void EvaluatePointSpacingCheck(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            var pointLayer = getLayer(config.MainTableId);
            if (pointLayer == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.MainTableId);
                return;
            }

            // FieldFilter 형식: "scale=5K;flatland=200;road_sidewalk=20;road_carriageway=30"
            // 또는 간단히: "200" (평탄지 기준 거리만)
            var spacingParams = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fieldFilter))
            {
                var parts = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    {
                        spacingParams[kv[0]] = value;
                    }
                    else if (kv.Length == 1 && double.TryParse(kv[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var singleValue))
                    {
                        // 단일 값인 경우 평탄지 기준으로 사용
                        spacingParams["flatland"] = singleValue;
                    }
                }
            }

            // 기본값 설정 (geometry_criteria.csv에서 읽거나 하드코딩된 기본값)
            var defaultFlatland = _geometryCriteria?.PointSpacingFlatland ?? 200.0;
            var defaultSidewalk = _geometryCriteria?.PointSpacingSidewalk ?? 20.0;
            var defaultCarriageway = _geometryCriteria?.PointSpacingCarriageway ?? 30.0;
            
            var flatlandSpacing = spacingParams.GetValueOrDefault("flatland", defaultFlatland); // 평탄지 기본값
            var roadSidewalkSpacing = spacingParams.GetValueOrDefault("road_sidewalk", defaultSidewalk); // 인도 기본값
            var roadCarriagewaySpacing = spacingParams.GetValueOrDefault("road_carriageway", defaultCarriageway); // 차도 기본값

            _logger.LogInformation("표고점 위치간격 검사 시작: 레이어={Layer}, 평탄지={Flatland}m, 인도={Sidewalk}m, 차도={Carriageway}m", 
                config.MainTableId, flatlandSpacing, roadSidewalkSpacing, roadCarriagewaySpacing);
            var startTime = DateTime.Now;

            // 모든 표고점 수집
            pointLayer.ResetReading();
            var featureCount = pointLayer.GetFeatureCount(1);
            var allPoints = new List<(long Oid, double X, double Y)>();
            
            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            Feature? f;
            while ((f = pointLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var pointType = g.GetGeometryType();
                    double x = 0, y = 0;

                    if (pointType == wkbGeometryType.wkbPoint)
                    {
                        x = g.GetX(0);
                        y = g.GetY(0);
                    }
                    else if (pointType == wkbGeometryType.wkbMultiPoint)
                    {
                        // 첫 번째 점 사용
                        using var firstPoint = g.GetGeometryRef(0);
                        if (firstPoint != null && !firstPoint.IsEmpty())
                        {
                            x = firstPoint.GetX(0);
                            y = firstPoint.GetY(0);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    allPoints.Add((oid, x, y));
                }
            }

            _logger.LogInformation("표고점 수집 완료: {Count}개", allPoints.Count);

            // 도로 레이어 미리 로드 (평지/도로 구분 검사용)
            var sidewalkLayer = getLayer("tn_ftpth_bndry");
            var roadLayer = getLayer("tn_rodway_bndry");
            var sidewalkPolygons = new List<Geometry>();
            var roadPolygons = new List<Geometry>();
            
            if (sidewalkLayer != null)
            {
                sidewalkLayer.ResetReading();
                Feature? sf;
                while ((sf = sidewalkLayer.GetNextFeature()) != null)
                {
                    using (sf)
                    {
                        var geom = sf.GetGeometryRef();
                        if (geom != null && !geom.IsEmpty())
                        {
                            sidewalkPolygons.Add(geom.Clone());
                        }
                    }
                }
                _logger.LogDebug("보도경계면 로드 완료: {Count}개", sidewalkPolygons.Count);
            }
            
            if (roadLayer != null)
            {
                roadLayer.ResetReading();
                Feature? rf;
                while ((rf = roadLayer.GetNextFeature()) != null)
                {
                    using (rf)
                    {
                        var geom = rf.GetGeometryRef();
                        if (geom != null && !geom.IsEmpty())
                        {
                            roadPolygons.Add(geom.Clone());
                        }
                    }
                }
                _logger.LogDebug("도로경계면 로드 완료: {Count}개", roadPolygons.Count);
            }

            // 공간 인덱스 기반 거리 검사 (성능 최적화)
            // 그리드 기반 공간 인덱스 사용
            var gridSize = Math.Max(flatlandSpacing, Math.Max(roadSidewalkSpacing, roadCarriagewaySpacing)) * 2.0;
            var gridIndex = new Dictionary<string, List<(long Oid, double X, double Y)>>();

            // 그리드 인덱스에 점 추가
            foreach (var (oid, x, y) in allPoints)
            {
                var gridKey = $"{(int)(x / gridSize)}_{(int)(y / gridSize)}";
                if (!gridIndex.ContainsKey(gridKey))
                {
                    gridIndex[gridKey] = new List<(long Oid, double X, double Y)>();
                }
                gridIndex[gridKey].Add((oid, x, y));
            }

            // 각 점에 대해 인접 그리드의 점들과 거리 검사
            var total = allPoints.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지
            var errorCount = 0;

            foreach (var (oid1, x1, y1) in allPoints)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 100 == 0 || idx == total)
                {
                    RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, idx, total);
                }

                // 인접 그리드 검색
                var gridX = (int)(x1 / gridSize);
                var gridY = (int)(y1 / gridSize);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var neighborKey = $"{gridX + dx}_{gridY + dy}";
                        if (!gridIndex.ContainsKey(neighborKey)) continue;

                        foreach (var (oid2, x2, y2) in gridIndex[neighborKey])
                        {
                            if (oid1 >= oid2) continue; // 중복 검사 방지

                            var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                            if (checkedPairs.Contains(pairKey)) continue;
                            checkedPairs.Add(pairKey);

                            try
                            {
                                var distance = Distance(x1, y1, x2, y2);
                                
                                if (distance <= 0) continue;
                                
                                // 평지/도로 구분 검사
                                // 표고점이 도로 위에 있는지 확인하여 적절한 최소 거리 적용
                                double requiredSpacing = flatlandSpacing; // 기본값: 평지 기준
                                string locationType = "평지";
                                
                                // 표고점 1이 도로 위에 있는지 확인
                                bool point1OnRoad = IsPointOnRoad(x1, y1, sidewalkPolygons, roadPolygons, out bool point1OnSidewalk);
                                // 표고점 2가 도로 위에 있는지 확인
                                bool point2OnRoad = IsPointOnRoad(x2, y2, sidewalkPolygons, roadPolygons, out bool point2OnSidewalk);
                                
                                // 두 표고점 모두 도로 위에 있는 경우
                                if (point1OnRoad && point2OnRoad)
                                {
                                    // 둘 다 인도 위에 있으면 인도 기준
                                    if (point1OnSidewalk && point2OnSidewalk)
                                    {
                                        requiredSpacing = roadSidewalkSpacing;
                                        locationType = "인도";
                                    }
                                    // 둘 다 차도 위에 있으면 차도 기준
                                    else if (!point1OnSidewalk && !point2OnSidewalk)
                                    {
                                        requiredSpacing = roadCarriagewaySpacing;
                                        locationType = "차도";
                                    }
                                    // 하나는 인도, 하나는 차도인 경우 더 작은 값 사용
                                    else
                                    {
                                        requiredSpacing = Math.Min(roadSidewalkSpacing, roadCarriagewaySpacing);
                                        locationType = "도로(혼합)";
                                    }
                                }
                                // 하나만 도로 위에 있는 경우 평지 기준 사용
                                else if (point1OnRoad || point2OnRoad)
                                {
                                    requiredSpacing = flatlandSpacing;
                                    locationType = "도로/평지 혼합";
                                }
                                
                                // 최소 거리 검사
                                if (distance < requiredSpacing)
                                {
                                    errorCount++;
                                    var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                    var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                    
                                    // Point Geometry 생성 (좌표값 0,0 문제 해결)
                                    var pointGeom = new Geometry(wkbGeometryType.wkbPoint);
                                    pointGeom.AddPoint((x1 + x2) / 2.0, (y1 + y2) / 2.0, 0); // 두 점의 중점 사용
                                    
                                    AddDetailedError(result, "REL_POINT_SPACING_VIOLATION",
                                        $"표고점 간 거리가 최소 간격({requiredSpacing}m, {locationType}) 미만: OID {oid1Str} <-> {oid2Str} (거리: {distance:F2}m)",
                                        config.MainTableId, oid1Str, $"인접 표고점: {oid2Str}", pointGeom);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "거리 계산 중 오류: OID={Oid1} vs {Oid2}", oid1, oid2);
                            }
                        }
                    }
                }
            }

            // 메모리 정리
            foreach (var geom in sidewalkPolygons)
            {
                geom?.Dispose();
            }
            foreach (var geom in roadPolygons)
            {
                geom?.Dispose();
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("표고점 위치간격 검사 완료: 처리 {Count}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초", 
                total, errorCount, elapsed);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        /// <summary>
        /// 표고점이 도로 위에 있는지 확인하고, 인도인지 차도인지 구분 (성능 최적화: 미리 로드된 폴리곤 사용)
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="sidewalkPolygons">보도경계면 폴리곤 목록</param>
        /// <param name="roadPolygons">도로경계면 폴리곤 목록</param>
        /// <param name="isOnSidewalk">인도 위에 있는지 여부 (출력 파라미터)</param>
        /// <returns>도로 위에 있으면 true, 아니면 false</returns>
        private bool IsPointOnRoad(double x, double y, List<Geometry> sidewalkPolygons, List<Geometry> roadPolygons, out bool isOnSidewalk)
        {
            isOnSidewalk = false;
            
            try
            {
                var pointGeom = new Geometry(wkbGeometryType.wkbPoint);
                pointGeom.AddPoint(x, y, 0);
                
                // 먼저 보도경계면 확인 (인도)
                foreach (var sidewalkPoly in sidewalkPolygons)
                {
                    if (sidewalkPoly != null && !sidewalkPoly.IsEmpty())
                    {
                        // Envelope 기반 사전 필터링 (성능 최적화)
                        var env = new Envelope();
                        sidewalkPoly.GetEnvelope(env);
                        if (x >= env.MinX && x <= env.MaxX && y >= env.MinY && y <= env.MaxY)
                        {
                            // 점이 폴리곤 내부에 있는지 확인
                            if (pointGeom.Within(sidewalkPoly) || sidewalkPoly.Contains(pointGeom))
                            {
                                isOnSidewalk = true;
                                pointGeom.Dispose();
                                return true;
                            }
                        }
                    }
                }
                
                // 보도경계면에 없으면 도로경계면 확인 (차도)
                foreach (var roadPoly in roadPolygons)
                {
                    if (roadPoly != null && !roadPoly.IsEmpty())
                    {
                        // Envelope 기반 사전 필터링 (성능 최적화)
                        var env = new Envelope();
                        roadPoly.GetEnvelope(env);
                        if (x >= env.MinX && x <= env.MaxX && y >= env.MinY && y <= env.MaxY)
                        {
                            // 점이 폴리곤 내부에 있는지 확인
                            if (pointGeom.Within(roadPoly) || roadPoly.Contains(pointGeom))
                            {
                                isOnSidewalk = false; // 차도
                                pointGeom.Dispose();
                                return true;
                            }
                        }
                    }
                }
                
                pointGeom.Dispose();
                return false; // 도로 위에 없음 (평지)
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "표고점 도로 위치 확인 중 오류: ({X}, {Y})", x, y);
                return false;
            }
        }

        #endregion

        private bool _disposed = false;
        
        /// <summary>
        /// 리소스 정리 (Union 캐시 포함)
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                ClearUnionCache();
                _disposed = true;
                _logger.LogInformation("RelationCheckProcessor 리소스 정리 완료");
            }
        }
    }
}
