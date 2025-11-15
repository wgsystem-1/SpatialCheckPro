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
                
                // 필터 적용
                var rc = layer.SetAttributeFilter(normalized);
                var afterCount = layer.GetFeatureCount(1);
                
                _logger.LogInformation("AttributeFilter 적용: Layer={Layer}, Filter='{Filter}' -> Normalized='{Norm}', RC={RC}, 전체={Before} -> 필터후={After}", 
                    layer.GetName(), fieldFilter, normalized, rc, beforeCount, afterCount);
                
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

            // IN
            filter = Regex.Replace(filter,
                pattern: @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+IN\s*\(([^)]*)\)",
                evaluator: m => Rebuild(m.Groups[1].Value, "IN", m.Groups[2].Value));

            // NOT IN
            filter = Regex.Replace(filter,
                pattern: @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+NOT\s+IN\s*\(([^)]*)\)",
                evaluator: m => Rebuild(m.Groups[1].Value, "NOT IN", m.Groups[2].Value));

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
            var boundary = getLayer(config.MainTableId);
            var centerline = getLayer(config.RelatedTableId);
            if (boundary == null || centerline == null) return;

            using var _filter = ApplyAttributeFilterIfMatch(centerline, fieldFilter);

            // 경계면 내부에 최소 한 개의 중심선이 있어야 한다고 가정하고, 내부에 전혀 교차/포함이 없으면 누락으로 처리
            boundary.ResetReading();
            var total = boundary.GetFeatureCount(1);
            Feature? bf;
            var processed = 0;
            while ((bf = boundary.GetNextFeature()) != null)
            {
                using (bf)
                {
                    processed++;
                    if (processed % 200 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                    }
                    var bg = bf.GetGeometryRef();
                    if (bg == null) continue;
                    centerline.SetSpatialFilter(bg);
                    var hasAny = centerline.GetNextFeature() != null;
                    centerline.ResetReading();
                    if (!hasAny)
                    {
                        var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, "REL_BNDRY_CENTERLINE_MISSING", "도로경계면에 도로중심선이 누락됨", config.MainTableId, oid, "", bg);
                    }
                }
            }
            centerline.SetSpatialFilter(null);
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNoOverlap(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, double tolerance, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 건물, 관련: 대상 폴리곤(예: 도로경계면/보도경계면 등) - 겹침 금지
            var polyA = getLayer(config.MainTableId);
            var polyB = getLayer(config.RelatedTableId);
            if (polyA == null || polyB == null) return;

            using var _fa = ApplyAttributeFilterIfMatch(polyA, fieldFilter);

            polyA.ResetReading();
            var total = polyA.GetFeatureCount(1);
            Feature? fa;
            var processed = 0;
            while ((fa = polyA.GetNextFeature()) != null)
            {
                using (fa)
                {
                    processed++;
                    if (processed % 200 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                    }
                    var ga = fa.GetGeometryRef();
                    if (ga == null) continue;
                    polyB.SetSpatialFilter(ga);

                    Feature? fb;
                    bool overlapped = false;
                    while ((fb = polyB.GetNextFeature()) != null)
                    {
                        using (fb)
                        {
                            var gb = fb.GetGeometryRef();
                            if (gb == null) continue;
                            using var inter = ga.Intersection(gb);
                            if (inter != null && !inter.IsEmpty())
                            {
                                // 겹침 면적이 tolerance 초과면 오류 (면 지오메트리인 경우에만 계산)
                                var area = GetSurfaceArea(inter);
                                if (area > tolerance)
                                {
                                    overlapped = true;
                                    break;
                                }
                            }
                        }
                    }

                    polyB.SetSpatialFilter(null);
                    if (overlapped)
                    {
                        var oid = fa.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, "REL_BULD_OVERLAP", $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함", config.MainTableId, oid, "", ga);
                    }
                }
            }
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNotIntersectLine(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 건물(폴리곤), 관련: 선(철도중심선 등) - 선과 교차 금지
            var buld = getLayer(config.MainTableId);
            var line = getLayer(config.RelatedTableId);
            if (buld == null || line == null) return;

            using var _fl = ApplyAttributeFilterIfMatch(line, fieldFilter);

            buld.ResetReading();
            var total = buld.GetFeatureCount(1);
            Feature? bf;
            var processed = 0;
            while ((bf = buld.GetNextFeature()) != null)
            {
                using (bf)
                {
                    processed++;
                    if (processed % 200 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                    }
                    var pg = bf.GetGeometryRef();
                    if (pg == null) continue;
                    line.SetSpatialFilter(pg);
                    var hasCross = false;
                    Feature? lf;
                    while ((lf = line.GetNextFeature()) != null)
                    {
                        using (lf)
                        {
                            var lg = lf.GetGeometryRef();
                            if (lg == null) continue;
                            using var inter = pg.Intersection(lg);
                            if (inter != null && !inter.IsEmpty()) { hasCross = true; break; }
                        }
                    }
                    line.SetSpatialFilter(null);
                    if (hasCross)
                    {
                        var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, "REL_BULD_INTERSECT_LINE", $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함", config.MainTableId, oid, "", pg);
                    }
                }
            }
            RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, total, total, completed: true);
        }

        private void EvaluatePolygonNotContainPoint(DataSource ds, Func<string, Layer?> getLayer, ValidationResult result, string fieldFilter, CancellationToken token, RelationCheckConfig config)
        {
            // 메인: 금지 폴리곤, 관련: 점 - 폴리곤 내부에 점 존재 금지
            var poly = getLayer(config.MainTableId);
            var pt = getLayer(config.RelatedTableId);
            if (poly == null || pt == null) return;

            using var _fp = ApplyAttributeFilterIfMatch(pt, fieldFilter);

            poly.ResetReading();
            var total = poly.GetFeatureCount(1);
            Feature? pf;
            var processed = 0;
            while ((pf = poly.GetNextFeature()) != null)
            {
                using (pf)
                {
                    processed++;
                    if (processed % 200 == 0 || processed == total)
                    {
                        RaiseProgress(config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, processed, total);
                    }
                    var pg = pf.GetGeometryRef();
                    if (pg == null) continue;
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
        
        #endregion
    }
}
