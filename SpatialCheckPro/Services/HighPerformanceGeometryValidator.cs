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
    /// Phase 2 Item #5: 공간 인덱스 재생성 최적화 - 중복 생성 방지
    /// </summary>
    public class HighPerformanceGeometryValidator
    {
        private readonly ILogger<HighPerformanceGeometryValidator> _logger;
        private readonly SpatialIndexService _spatialIndexService;
        private readonly MemoryOptimizationService _memoryOptimization;
        private readonly ParallelProcessingManager _parallelProcessingManager;
        private readonly Models.Config.PerformanceSettings _settings;
        private readonly GeometryCriteria _criteria;

        // Phase 2 Item #5: 공간 인덱스 캐싱 (중복 생성 방지)
        // 예상 효과: 인덱스 구축 시간 3-5초 절약, 메모리 효율 20% 향상
        private readonly ConcurrentDictionary<string, SpatialIndex> _spatialIndexCache = new();
        
        /// <summary>
        /// 현재 검수 중인 파일 경로 (캐시 키에 포함하여 파일별 캐시 분리)
        /// </summary>
        private string? _currentFilePath;

        public HighPerformanceGeometryValidator(
            ILogger<HighPerformanceGeometryValidator> logger,
            SpatialIndexService spatialIndexService,
            MemoryOptimizationService memoryOptimization,
            ParallelProcessingManager parallelProcessingManager,
            Models.Config.PerformanceSettings settings,
            GeometryCriteria criteria)
        {
            _logger = logger;
            _spatialIndexService = spatialIndexService;
            _memoryOptimization = memoryOptimization;
            _parallelProcessingManager = parallelProcessingManager;
            _settings = settings;
            _criteria = criteria ?? throw new ArgumentNullException(nameof(criteria));
        }

        /// <summary>
        /// 현재 검수 중인 파일 경로 설정 (캐시 키에 포함하여 파일별 캐시 분리)
        /// </summary>
        public void SetCurrentFilePath(string? filePath)
        {
            _currentFilePath = filePath;
            _logger.LogDebug("현재 검수 파일 경로 설정: {FilePath}", filePath ?? "(null)");
        }

        /// <summary>
        /// 공간 인덱스 캐시에서 가져오거나 새로 생성
        /// Phase 2 Item #5: 공간 인덱스 재생성 최적화
        /// 개선: 파일 경로를 캐시 키에 포함하여 파일별 캐시 분리
        /// </summary>
        private SpatialIndex GetOrBuildSpatialIndex(string layerName, Layer layer, double tolerance)
        {
            // Layer 상태 초기화 보장 (이전 검수에서 변경된 상태 방지)
            try
            {
                layer.ResetReading();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Layer.ResetReading() 호출 중 오류 발생 (계속 진행): {LayerName}", layerName);
            }

            // 캐시 키: 파일 경로 + layerName + tolerance 조합 (파일별 캐시 분리)
            var filePathHash = _currentFilePath != null 
                ? System.IO.Path.GetFullPath(_currentFilePath).GetHashCode().ToString("X8")
                : "unknown";
            var cacheKey = $"{filePathHash}_{layerName}_{tolerance:F6}";

            if (_spatialIndexCache.TryGetValue(cacheKey, out var cachedIndex))
            {
                _logger.LogDebug("공간 인덱스 캐시 적중: {CacheKey}", cacheKey);
                return cachedIndex;
            }

            _logger.LogDebug("공간 인덱스 생성 중: {CacheKey}", cacheKey);
            var startTime = DateTime.Now;

            var newIndex = _spatialIndexService.CreateSpatialIndex(layerName, layer, tolerance);
            _spatialIndexCache[cacheKey] = newIndex;

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("공간 인덱스 생성 및 캐싱 완료: {CacheKey}, 소요시간: {Elapsed:F2}초",
                cacheKey, elapsed);

            return newIndex;
        }

        /// <summary>
        /// 공간 인덱스 캐시 정리
        /// Phase 2 Item #5: 메모리 관리를 위한 캐시 클리어
        /// 주의: 모든 캐시를 정리하므로 배치 검수 중에는 파일별 정리(RemoveSpatialIndexCacheForFile) 사용 권장
        /// </summary>
        public void ClearSpatialIndexCache()
        {
            var count = _spatialIndexCache.Count;
            
            // 모든 인덱스 객체 명시적 해제
            foreach (var index in _spatialIndexCache.Values)
            {
                index?.Dispose();
            }
            
            _spatialIndexCache.Clear();
            _currentFilePath = null; // 현재 파일 경로도 초기화
            
            if (count > 0)
            {
                _logger.LogInformation("공간 인덱스 캐시 정리 완료: {Count}개 항목 제거", count);
            }
        }

        /// <summary>
        /// 특정 레이어의 공간 인덱스 캐시 제거
        /// </summary>
        public void RemoveSpatialIndexCache(string layerName)
        {
            var keysToRemove = _spatialIndexCache.Keys.Where(k => k.Contains($"_{layerName}_")).ToList();
            foreach (var key in keysToRemove)
            {
                if (_spatialIndexCache.TryRemove(key, out var index))
                {
                    index?.Dispose();
                }
            }
            _logger.LogDebug("레이어 공간 인덱스 캐시 제거: {LayerName}, {Count}개 항목", layerName, keysToRemove.Count);
        }

        /// <summary>
        /// 특정 파일의 공간 인덱스 캐시 제거 (배치 검수 성능 최적화)
        /// </summary>
        public void RemoveSpatialIndexCacheForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            var filePathHash = System.IO.Path.GetFullPath(filePath).GetHashCode().ToString("X8");
            var prefix = $"{filePathHash}_";
            var keysToRemove = _spatialIndexCache.Keys.Where(k => k.StartsWith(prefix)).ToList();
            
            foreach (var key in keysToRemove)
            {
                if (_spatialIndexCache.TryRemove(key, out var index))
                {
                    index?.Dispose();
                }
            }
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("파일별 공간 인덱스 캐시 제거: {FilePath}, {Count}개 항목", filePath, keysToRemove.Count);
            }
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

                // 1단계: 공간 인덱스 가져오기 또는 생성 (Phase 2 Item #5: 캐싱)
                _logger.LogInformation("공간 인덱스 준비 중: {FeatureCount}개 피처", featureCount);
                var spatialIndex = GetOrBuildSpatialIndex(layerName, layer, Math.Max(tolerance, coordinateTolerance));

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

                // 공간 인덱스 기반 겹침 검사 (Phase 2 Item #5: 캐싱)
                var spatialIndex = GetOrBuildSpatialIndex(layerName, layer, tolerance);
                var overlaps = _spatialIndexService.FindOverlaps(layerName, spatialIndex);

                foreach (var overlap in overlaps)
                {
                    // 겹침 면적이 tolerance를 초과하는 경우에만 오류로 기록
                    // tolerance 이하인 경우는 허용 오차 범위 내이므로 스킵
                    if (overlap.OverlapArea <= tolerance)
                    {
                        _logger.LogDebug("겹침 면적이 허용 오차 이하: OBJECTID={ObjectId}, 면적={Area:F6}㎡, 허용오차={Tolerance}㎡", 
                            overlap.ObjectId, overlap.OverlapArea, tolerance);
                        continue;
                    }

                    // 교차 영역 중심점 및 WKT 추출 (교차 지오메트리가 있으면 우선 사용)
                    double centerX = 0, centerY = 0;
                    string? intersectionWkt = null;

                    if (overlap.IntersectionGeometry != null && !overlap.IntersectionGeometry.IsEmpty())
                    {
                        var envInt = new Envelope();
                        overlap.IntersectionGeometry.GetEnvelope(envInt);
                        centerX = (envInt.MinX + envInt.MaxX) / 2.0;
                        centerY = (envInt.MinY + envInt.MaxY) / 2.0;
                        overlap.IntersectionGeometry.ExportToWkt(out intersectionWkt);
                    }
                    else
                    {
                        // 폴백: 대상 피처 중심
                        Feature? feat = null;
                        try
                        {
                            feat = layer.GetFeature(overlap.ObjectId);
                            var g = feat?.GetGeometryRef();
                            if (g != null && !g.IsEmpty())
                            {
                                var env = new Envelope();
                                g.GetEnvelope(env);
                                centerX = (env.MinX + env.MaxX) / 2.0;
                                centerY = (env.MinY + env.MaxY) / 2.0;
                                g.ExportToWkt(out intersectionWkt);
                            }
                        }
                        finally
                        {
                            feat?.Dispose();
                        }
                    }

                    errorDetails.Add(new GeometryErrorDetail
                    {
                        ObjectId = overlap.ObjectId.ToString(),
                        ErrorType = "겹침 지오메트리",
                        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
                        ThresholdValue = $"{tolerance}㎡",
                        DetailMessage = $"OBJECTID {overlap.ObjectId}: 겹침 영역 {overlap.OverlapArea:F2}㎡ 검출 (허용오차: {tolerance}㎡ 초과)",
                        X = centerX,
                        Y = centerY,
                        GeometryWkt = intersectionWkt
                    });
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                var errorCount = errorDetails.Count;
                var skippedCount = overlaps.Count - errorCount;
                _logger.LogInformation("고성능 겹침 검수 완료: 전체 겹침 {TotalCount}개 중 오류 {ErrorCount}개 (허용오차 이하 {SkippedCount}개 제외), 소요시간: {Elapsed:F2}초", 
                    overlaps.Count, errorCount, skippedCount, elapsed);

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
                    var duplicateErrors = await CheckDuplicatesHighPerformanceAsync(layer, _criteria.DuplicateCheckTolerance);
                    batchErrors.AddRange(duplicateErrors);
                }

                if (config.ShouldCheckOverlap)
                {
                    var overlapErrors = await CheckOverlapsHighPerformanceAsync(layer, _criteria.OverlapTolerance);
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
