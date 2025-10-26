using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Services;
using System.Collections.Concurrent;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 고성능 지오메트리 검수 서비스
    /// 기존 GeometryValidationService의 성능 병목을 해결
    /// </summary>
    public class HighPerformanceGeometryValidator
    {
        private readonly ILogger<HighPerformanceGeometryValidator> _logger;
        private readonly SpatialIndexService _spatialIndexService;
        private readonly MemoryOptimizationService _memoryOptimization;
        private readonly ParallelProcessingManager _parallelProcessingManager;
        private readonly Models.Config.PerformanceSettings _settings;

        public HighPerformanceGeometryValidator(
            ILogger<HighPerformanceGeometryValidator> logger,
            SpatialIndexService spatialIndexService,
            MemoryOptimizationService memoryOptimization,
            ParallelProcessingManager parallelProcessingManager,
            Models.Config.PerformanceSettings settings)
        {
            _logger = logger;
            _spatialIndexService = spatialIndexService;
            _memoryOptimization = memoryOptimization;
            _parallelProcessingManager = parallelProcessingManager;
            _settings = settings;
        }

        /// <summary>
        /// 고성능 중복 지오메트리 검수
        /// O(n²) → O(n log n) 최적화
        /// </summary>
        public async Task<List<GeometryErrorDetail>> CheckDuplicatesHighPerformanceAsync(
            Layer layer,
            double tolerance = 0.0,
            double coordinateTolerance = 0.0)
        {
            var errorDetails = new ConcurrentBag<GeometryErrorDetail>();
            var layerName = layer.GetName();
            
            try
            {
                _logger.LogInformation("고성능 중복 지오메트리 검수 시작: {LayerName} (좌표 허용오차: {CoordinateTolerance}m)",
                    layerName, coordinateTolerance);

                var startTime = DateTime.Now;
                var featureCount = (int)layer.GetFeatureCount(1);
                
                if (featureCount == 0)
                {
                    _logger.LogInformation("검수할 피처가 없습니다: {LayerName}", layerName);
                    return new List<GeometryErrorDetail>();
                }

                // 1단계: 공간 인덱스 생성 (R-tree 기반)
                _logger.LogInformation("공간 인덱스 생성 중: {FeatureCount}개 피처", featureCount);
                var spatialIndex = _spatialIndexService.CreateSpatialIndex(layerName, layer, Math.Max(tolerance, coordinateTolerance));

                var duplicateGroups = FindExactDuplicateGroups(spatialIndex, coordinateTolerance);

                // 3단계: 중복 그룹을 오류 상세로 변환
                var duplicateCount = 0;
                foreach (var group in duplicateGroups.Values.Where(g => g.Count > 1))
                {
                    // 첫 번째 객체는 유지, 나머지는 중복으로 기록
                    for (int i = 1; i < group.Count; i++)
                    {
                        var (objectId, geometry) = group[i];
                        // 오류 좌표 및 WKT 설정 (엔벨로프 중심점 사용)
                        geometry.ExportToWkt(out string dupWkt);
                        var dupEnv = new Envelope();
                        geometry.GetEnvelope(dupEnv);
                        var dupX = (dupEnv.MinX + dupEnv.MaxX) / 2.0;
                        var dupY = (dupEnv.MinY + dupEnv.MaxY) / 2.0;

                        errorDetails.Add(new GeometryErrorDetail
                        {
                            ObjectId = objectId.ToString(),
                            ErrorType = "중복 지오메트리",
                            ErrorValue = $"정확히 동일한 지오메트리 (그룹 크기: {group.Count})",
                            ThresholdValue = coordinateTolerance > 0 ? $"좌표 허용오차 {coordinateTolerance}m" : "Exact match",
                            DetailMessage = coordinateTolerance > 0
                                ? $"OBJECTID {objectId}: 좌표 허용오차 {coordinateTolerance}m 이내 동일한 지오메트리"
                                : $"OBJECTID {objectId}: 완전히 동일한 지오메트리",
                            X = dupX,
                            Y = dupY,
                            GeometryWkt = dupWkt
                        });
                        duplicateCount++;
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("고성능 중복 검수 완료: {DuplicateCount}개 중복, 소요시간: {Elapsed:F2}초, 처리속도: {Speed:F0} 피처/초", 
                    duplicateCount, elapsed, featureCount / Math.Max(elapsed, 0.0001));

                return errorDetails.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "고성능 중복 지오메트리 검수 실패: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 고성능 겹침 지오메트리 검수
        /// </summary>
        public async Task<List<GeometryErrorDetail>> CheckOverlapsHighPerformanceAsync(
            Layer layer, 
            double tolerance = 0.001)
        {
            var errorDetails = new ConcurrentBag<GeometryErrorDetail>();
            var layerName = layer.GetName();

            try
            {
                _logger.LogInformation("고성능 겹침 지오메트리 검수 시작: {LayerName}", layerName);
                var startTime = DateTime.Now;

                // 공간 인덱스 기반 겹침 검사
                var spatialIndex = _spatialIndexService.CreateSpatialIndex(layerName, layer, tolerance);
                var overlaps = _spatialIndexService.FindOverlaps(layerName, spatialIndex);

                foreach (var overlap in overlaps)
                {
                    // 대상 피처에서 좌표 및 WKT 추출 (엔벨로프 중심점)
                    double ovX = 0;
                    double ovY = 0;
                    string? ovWkt = null;
                    Feature? ovFeature = null;
                    try
                    {
                        ovFeature = layer.GetFeature(overlap.ObjectId);
                        var ovGeom = ovFeature?.GetGeometryRef();
                        if (ovGeom != null && !ovGeom.IsEmpty())
                        {
                            ovGeom.ExportToWkt(out ovWkt);
                            var ovEnv = new Envelope();
                            ovGeom.GetEnvelope(ovEnv);
                            ovX = (ovEnv.MinX + ovEnv.MaxX) / 2.0;
                            ovY = (ovEnv.MinY + ovEnv.MaxY) / 2.0;
                        }
                    }
                    finally
                    {
                        ovFeature?.Dispose();
                    }

                    errorDetails.Add(new GeometryErrorDetail
                    {
                        ObjectId = overlap.ObjectId.ToString(),
                        ErrorType = "겹침 지오메트리",
                        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
                        ThresholdValue = $"{tolerance}m",
                        DetailMessage = $"OBJECTID {overlap.ObjectId}: 겹침 영역 {overlap.OverlapArea:F2}㎡ 검출",
                        X = ovX,
                        Y = ovY,
                        GeometryWkt = ovWkt
                    });
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("고성능 겹침 검수 완료: {OverlapCount}개 겹침, 소요시간: {Elapsed:F2}초", 
                    overlaps.Count, elapsed);

                return errorDetails.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "고성능 겹침 지오메트리 검수 실패: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 스트리밍 방식 지오메트리 검수
        /// 대용량 데이터에 대한 메모리 효율적 처리
        /// </summary>
        public async Task<List<GeometryErrorDetail>> ValidateGeometryStreamingAsync(
            Layer layer,
            GeometryCheckConfig config,
            IProgress<string>? progress = null)
        {
            var allErrorDetails = new List<GeometryErrorDetail>();
            var layerName = layer.GetName();

            try
            {
                _logger.LogInformation("스트리밍 지오메트리 검수 시작: {LayerName}", layerName);
                var startTime = DateTime.Now;

                var featureCount = (int)layer.GetFeatureCount(1);
                if (featureCount == 0) return allErrorDetails;

                // 스트리밍 배치 크기 계산
                var batchSize = _memoryOptimization.GetDynamicBatchSize(_settings.StreamingBatchSize);
                var batches = CreateBatches(featureCount, batchSize);

                _logger.LogInformation("스트리밍 처리: {BatchCount}개 배치, 배치 크기: {BatchSize}", 
                    batches.Count, batchSize);

                // 배치별 순차 처리 (메모리 안정성)
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    var batch = batches[batchIndex];
                    progress?.Report($"지오메트리 검수 중... 배치 {batchIndex + 1}/{batches.Count}");

                    // 배치별 검수 수행
                    var batchErrors = await ProcessBatchValidationAsync(layer, batch, config);
                    allErrorDetails.AddRange(batchErrors);

                    // 메모리 압박 체크 및 GC 실행
                    if (_memoryOptimization.IsMemoryPressureHigh())
                    {
                        _logger.LogInformation("메모리 압박 감지, GC 실행");
                        _memoryOptimization.PerformGarbageCollection();
                    }

                    // 진행률 업데이트
                    var progressPercent = (batchIndex + 1) * 100 / batches.Count;
                    progress?.Report($"지오메트리 검수 진행률: {progressPercent}%");
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("스트리밍 지오메트리 검수 완료: {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초", 
                    allErrorDetails.Count, elapsed);

                return allErrorDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스트리밍 지오메트리 검수 실패: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 배치 생성
        /// </summary>
        private List<BatchInfo> CreateBatches(int totalItems, int batchSize)
        {
            var batches = new List<BatchInfo>();
            var remaining = totalItems;
            var startIndex = 0;

            while (remaining > 0)
            {
                var currentBatchSize = Math.Min(batchSize, remaining);
                batches.Add(new BatchInfo
                {
                    Start = startIndex,
                    Count = currentBatchSize
                });

                startIndex += currentBatchSize;
                remaining -= currentBatchSize;
            }

            return batches;
        }

        /// <summary>
        /// 배치별 중복 검사 처리
        /// </summary>
        private void ProcessBatchForDuplicates(
            Layer layer,
            BatchInfo batch,
            object spatialIndex,
            double coordinateTolerance,
            ConcurrentDictionary<string, List<(long ObjectId, Geometry Geometry)>> duplicateGroups)
        {
            try
            {
                layer.ResetReading();
                
                // 배치 범위의 피처들 수집
                var batchFeatures = new List<(long ObjectId, Geometry Geometry)>();
                var currentIndex = 0;

                Feature feature;
                while ((feature = layer.GetNextFeature()) != null && currentIndex < batch.Start + batch.Count)
                {
                    if (currentIndex >= batch.Start)
                    {
                        var geometry = feature.GetGeometryRef();
                        if (geometry != null)
                        {
                            // FID는 GetFID()로 접근 (GetFieldAsInteger로 접근 불가)
                            var objectId = (int)feature.GetFID();
                            batchFeatures.Add((objectId, geometry.Clone()));
                        }
                    }
                    feature.Dispose();
                    currentIndex++;
                }

                // 배치 내 중복 검사
                for (int i = 0; i < batchFeatures.Count; i++)
                {
                    for (int j = i + 1; j < batchFeatures.Count; j++)
                    {
                        var (objId1, geom1) = batchFeatures[i];
                        var (objId2, geom2) = batchFeatures[j];

                        try
                        {
                            var isDuplicate = AreGeometriesEqual(geom1, geom2, coordinateTolerance);
                            if (isDuplicate)
                            {
                                var key = $"{objId1}_{objId2}";
                                duplicateGroups.AddOrUpdate(key, 
                                    new List<(long, Geometry)> { (objId1, geom1), (objId2, geom2) },
                                    (k, existing) => 
                                    {
                                        existing.Add((objId2, geom2));
                                        return existing;
                                    });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "중복 검사 중 오류: OBJECTID {ObjId1}, {ObjId2}", objId1, objId2);
                        }
                    }
                }

                // 메모리 정리
                foreach (var (_, geometry) in batchFeatures)
                {
                    geometry?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "배치 중복 검사 실패: 시작={Start}, 크기={Count}", 
                    batch.Start, batch.Count);
            }
        }

        /// <summary>
        /// 배치별 검수 처리
        /// </summary>
        private async Task<List<GeometryErrorDetail>> ProcessBatchValidationAsync(
            Layer layer, 
            BatchInfo batch, 
            GeometryCheckConfig config)
        {
            var batchErrors = new List<GeometryErrorDetail>();

            try
            {
                // 배치별 검수 로직 구현
                // (기본 검수, 중복 검수, 겹침 검수 등)
                
                if (config.ShouldCheckDuplicate)
                {
                    var duplicateErrors = await CheckDuplicatesHighPerformanceAsync(layer, 0.001);
                    batchErrors.AddRange(duplicateErrors);
                }

                if (config.ShouldCheckOverlap)
                {
                    var overlapErrors = await CheckOverlapsHighPerformanceAsync(layer, 0.001);
                    batchErrors.AddRange(overlapErrors);
                }

                return batchErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "배치 검수 처리 실패: 시작={Start}, 크기={Count}", 
                    batch.Start, batch.Count);
                return batchErrors;
            }
        }

        private bool AreGeometriesEqual(Geometry geom1, Geometry geom2, double tolerance)
        {
            if (tolerance <= 0)
            {
                return geom1.Equals(geom2);
            }

            if (geom1.Equals(geom2))
            {
                return true;
            }

            // 좌표 허용오차가 있는 경우: 버퍼링하여 포함 관계 확인
            using var buffered1 = geom1.Clone();
            using var buffered2 = geom2.Clone();

            buffered1.Buffer(tolerance, 1);
            buffered2.Buffer(tolerance, 1);

            return geom1.Within(buffered2) && geom2.Within(buffered1);
        }

        private byte[] ConvertGeometryToWkb(Geometry geometry)
        {
            using var derivative = geometry.Clone();
            derivative.FlattenTo2D();
            var size = derivative.WkbSize();
            var buffer = new byte[size];
            derivative.ExportToWkb(buffer, wkbByteOrder.wkbXDR);
            return buffer;
        }

        private Dictionary<string, List<(long ObjectId, Geometry Geometry)>> FindExactDuplicateGroups(
            SpatialIndex spatialIndex,
            double coordinateTolerance)
        {
            var duplicateGroups = new Dictionary<string, List<(long ObjectId, Geometry Geometry)>>();
            var entries = spatialIndex.GetAllEntries();

            foreach (var entry in entries)
            {
                var objectId = long.Parse(entry.ObjectId);
                var geometry = entry.Geometry;

                var key = Convert.ToBase64String(ConvertGeometryToWkb(geometry));

                if (!duplicateGroups.TryGetValue(key, out var list))
                {
                    list = new List<(long, Geometry)>();
                    duplicateGroups[key] = list;
                }

                list.Add((objectId, geometry));
            }

            if (coordinateTolerance > 0)
            {
                foreach (var key in duplicateGroups.Keys.ToList())
                {
                    var group = duplicateGroups[key];
                    var refinedGroups = new List<List<(long ObjectId, Geometry Geometry)>>();

                    foreach (var item in group)
                    {
                        var matched = false;
                        foreach (var refinedGroup in refinedGroups)
                        {
                            if (AreGeometriesEqual(refinedGroup[0].Geometry, item.Geometry, coordinateTolerance))
                            {
                                refinedGroup.Add(item);
                                matched = true;
                                break;
                            }
                        }

                        if (!matched)
                        {
                            refinedGroups.Add(new List<(long, Geometry)> { item });
                        }
                    }

                    duplicateGroups.Remove(key);
                    foreach (var refinedGroup in refinedGroups.Where(g => g.Count > 0))
                    {
                        var refinedKey = Convert.ToBase64String(ConvertGeometryToWkb(refinedGroup[0].Geometry));
                        duplicateGroups[refinedKey] = refinedGroup;
                    }
                }
            }

            return duplicateGroups;
        }
    }

}
