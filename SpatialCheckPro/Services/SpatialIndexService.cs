using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 인덱스 기반 지오메트리 검수 최적화 서비스
    /// R-tree 기반 공간 인덱스를 사용하여 O(n²) 알고리즘을 O(n log n)으로 최적화
    /// </summary>
    public class SpatialIndexService : IDisposable
    {
        private readonly ILogger<SpatialIndexService> _logger;
        private readonly Dictionary<string, SpatialIndex> _spatialIndexes = new();
        private bool _disposed = false;

        public SpatialIndexService(ILogger<SpatialIndexService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 공간 인덱스 생성
        /// </summary>
        /// <param name="layerName">레이어 이름</param>
        /// <param name="layer">GDAL 레이어</param>
        /// <param name="tolerance">검색 허용 오차 (기본값: 0.001)</param>
        /// <param name="timeoutMinutes">타임아웃(분), 0이면 무제한</param>
        /// <param name="validationErrors">사전 검증 오류를 수집할 리스트</param>
        /// <returns>공간 인덱스</returns>
        /// <remarks>
        /// tolerance 기본값은 GeometryCriteria.DuplicateCheckTolerance와 동일하게 설정됨
        /// </remarks>
        public SpatialIndex CreateSpatialIndex(string layerName, Layer layer, double tolerance = 0.001, int timeoutMinutes = 5, List<Models.ValidationError> validationErrors = null)
        {
            try
            {
                _logger.LogInformation("공간 인덱스 생성 시작: {LayerName}, 허용오차: {Tolerance}m", layerName, tolerance);
                var startTime = DateTime.Now;

                // 1단계: 전체 범위 파악 (적응형 그리드 크기 결정용)
                var totalEnvelope = CalculateTotalEnvelope(layer);
                var adaptiveGridSize = CalculateAdaptiveGridSize(totalEnvelope, tolerance);
                
                _logger.LogDebug("적응형 그리드 크기 계산: {GridSize}m (데이터 범위: {Width}×{Height}m)", 
                    adaptiveGridSize, totalEnvelope.Width, totalEnvelope.Height);

                var spatialIndex = new SpatialIndex(tolerance, _logger, adaptiveGridSize);
                var featureCount = 0;
                var estimatedTotal = layer.GetFeatureCount(1);
                var timeoutTime = timeoutMinutes > 0 ? startTime.AddMinutes(timeoutMinutes) : DateTime.MaxValue;

                // 2단계: 공간 인덱스 생성
                layer.ResetReading();
                Feature feature;
                
                var lastFeatureTime = DateTime.Now;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    try
                    {
                        // 타임아웃 체크 (100개마다)
                        if (featureCount % 100 == 0 && DateTime.Now > timeoutTime)
                        {
                            var elapsedMinutes = (DateTime.Now - startTime).TotalMinutes;
                            _logger.LogError("공간 인덱스 생성 타임아웃: {LayerName}, 경과시간: {Elapsed:F1}분, 처리: {Count}/{Total}", 
                                layerName, elapsedMinutes, featureCount, estimatedTotal);
                            throw new TimeoutException($"공간 인덱스 생성이 {timeoutMinutes}분을 초과했습니다. (레이어: {layerName})");
                        }
                        
                        var geometry = feature.GetGeometryRef();
                        if (geometry != null && !geometry.IsEmpty())
                        {
                            var objectId = GetObjectId(feature);
                            var featureStartTime = DateTime.Now;

                            // 사전 검증 1: 지오메트리 유효성 검사
                            if (!geometry.IsValid())
                            {
                                var errorMsg = $"유효하지 않은 지오메트리 (FID: {objectId}, Type: {geometry.GetGeometryName()})";
                                _logger.LogWarning(errorMsg);
                                validationErrors?.Add(new Models.ValidationError
                                {
                                    TableName = layerName,
                                    FeatureId = objectId,
                                    ErrorCode = "GEOM_INVALID",
                                    Message = errorMsg,
                                    Severity = Models.Enums.ErrorSeverity.Critical
                                });
                                continue;
                            }

                            // 사전 검증 2: 과도한 정점 수 필터링
                            var vertexCount = geometry.GetPointCount();
                            const int MAX_VERTEX_COUNT = 500000;
                            if (vertexCount > MAX_VERTEX_COUNT)
                            {
                                var errorMsg = $"정점 수가 과도하게 많은 지오메트리 (FID: {objectId}, 정점수: {vertexCount})";
                                _logger.LogWarning(errorMsg);
                                validationErrors?.Add(new Models.ValidationError
                                {
                                    TableName = layerName,
                                    FeatureId = objectId,
                                    ErrorCode = "GEOM_TOO_COMPLEX",
                                    Message = errorMsg,
                                    Severity = Models.Enums.ErrorSeverity.Warning,
                                    Metadata = { { "VertexCount", vertexCount } }
                                });
                                continue;
                            }

                            var envelope = GetEnvelope(geometry);
                            
                            spatialIndex.Insert(objectId, geometry.Clone(), envelope);
                            featureCount++;

                            var featureElapsed = (DateTime.Now - featureStartTime).TotalSeconds;
                            const double FEATURE_PROCESSING_TIMEOUT_SECONDS = 5.0;
                            if (featureElapsed > FEATURE_PROCESSING_TIMEOUT_SECONDS)
                            {
                                var errorMsg = $"지오메트리 처리 시간 초과 (FID: {objectId}, 처리시간: {featureElapsed:F2}초, 정점수: {vertexCount})";
                                _logger.LogError(errorMsg);
                                validationErrors?.Add(new Models.ValidationError
                                {
                                    TableName = layerName,
                                    FeatureId = objectId,
                                    ErrorCode = "GEOM_PROCESSING_TIMEOUT",
                                    Message = errorMsg,
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    Metadata = { { "ProcessingTime", featureElapsed }, { "VertexCount", vertexCount } }
                                });
                                
                                // 이미 추가되었으므로 제거
                                spatialIndex.Remove(objectId); 
                                continue;
                            }
                            else if (featureElapsed > 1.0)
                            {
                                _logger.LogWarning("복잡한 지오메트리 처리 지연: FID={FID}, 처리시간={Elapsed:F2}초, 정점수={VertexCount}",
                                    objectId, featureElapsed, vertexCount);
                            }
                            
                            // 진행률 보고 (10개마다)
                            if (featureCount % 10 == 0 && estimatedTotal > 0)
                            {
                                var progress = (featureCount * 100.0) / estimatedTotal;
                                var batchElapsed = (DateTime.Now - lastFeatureTime).TotalSeconds;
                                
                                // 배치 처리 시간이 비정상적으로 긴 경우 경고
                                if (batchElapsed > 5.0 && featureCount > 10)
                                {
                                    _logger.LogWarning("배치 처리 지연 감지: {Count}~{End}, 소요시간: {Elapsed:F2}초 (평균 {AvgPerFeature:F3}초/피처)", 
                                        featureCount - 10, featureCount, batchElapsed, batchElapsed / 10.0);
                                }
                                
                                _logger.LogDebug("공간 인덱스 생성 진행: {Count}/{Total} ({Progress:F1}%)", 
                                    featureCount, estimatedTotal, progress);
                                lastFeatureTime = DateTime.Now;
                            }
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                _spatialIndexes[layerName] = spatialIndex;
                
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("공간 인덱스 생성 완료: {LayerName}, 피처 {Count}개, 소요시간: {Elapsed:F2}초, 그리드크기: {GridSize}m", 
                    layerName, featureCount, elapsed, adaptiveGridSize);

                return spatialIndex;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 인덱스 생성 실패: {LayerName}", layerName);
                throw;
            }
        }

        public Dictionary<long, Geometry> GetGeometryDictionary(string layerName, SpatialIndex spatialIndex)
        {
            return spatialIndex.GetAllEntries().ToDictionary(e => long.Parse(e.ObjectId), e => e.Geometry);
        }

        /// <summary>
        /// 레이어의 전체 범위(Envelope) 계산 (빠른 스캔, 샘플링 방식)
        /// </summary>
        private SpatialEnvelope CalculateTotalEnvelope(Layer layer)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            layer.ResetReading();
            Feature feature;
            var sampleCount = 0;
            var maxSamples = 1000; // 최대 1000개 샘플링 (성능 향상)
            
            while ((feature = layer.GetNextFeature()) != null && sampleCount < maxSamples)
            {
                try
                {
                    var geometry = feature.GetGeometryRef();
                    if (geometry != null && !geometry.IsEmpty())
                    {
                        var env = GetEnvelope(geometry);
                        minX = Math.Min(minX, env.MinX);
                        minY = Math.Min(minY, env.MinY);
                        maxX = Math.Max(maxX, env.MaxX);
                        maxY = Math.Max(maxY, env.MaxY);
                        sampleCount++;
                    }
                }
                finally
                {
                    feature.Dispose();
                }
            }

            // 데이터가 없는 경우 기본 범위 반환
            if (minX == double.MaxValue)
            {
                return new SpatialEnvelope(0, 0, 100, 100);
            }

            return new SpatialEnvelope(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 데이터 범위에 따른 적응형 그리드 크기 계산 (개선판)
        /// 개별 지오메트리 크기를 고려하여 셀 폭발을 방지
        /// </summary>
        private double CalculateAdaptiveGridSize(SpatialEnvelope envelope, double baseTolerance)
        {
            var width = envelope.Width;
            var height = envelope.Height;
            var maxDimension = Math.Max(width, height);
            
            // 목표: 최대 지오메트리가 100×100 = 10,000 셀 이내로 인덱싱되도록 조정
            // 예상 최대 지오메트리 크기를 전체 범위의 5%로 가정
            var estimatedMaxGeometrySize = maxDimension * 0.05; // 예: 2900m → 145m
            
            // 최대 지오메트리가 100×100 셀 이내가 되도록 그리드 크기 계산
            const int TARGET_MAX_CELLS_PER_DIM = 100; // 100×100 = 10,000 셀
            var minGridSizeForSafety = estimatedMaxGeometrySize / TARGET_MAX_CELLS_PER_DIM;
            
            // 데이터 범위가 매우 큰 경우: 큰 그리드 사용 (성능 우선)
            double baseGridSize;
            if (maxDimension > 100000) // 100km 이상
            {
                baseGridSize = maxDimension / 100; // 100개 그리드로 분할
            }
            else if (maxDimension > 10000) // 10km 이상
            {
                baseGridSize = maxDimension / 500; // 500개 그리드로 분할
            }
            else if (maxDimension > 1000) // 1km 이상 (2443m × 2900m 데이터가 여기에 해당)
            {
                // 개선: tolerance * 100 (0.1m) 대신 안전한 그리드 크기 사용
                baseGridSize = Math.Max(baseTolerance * 100, minGridSizeForSafety);
            }
            else
            {
                baseGridSize = Math.Max(baseTolerance * 10, minGridSizeForSafety); // 기본: 허용오차의 10배
            }
            
            // 추가 안전장치: 너무 작은 그리드 방지 (최소 1m)
            baseGridSize = Math.Max(baseGridSize, 1.0);
            
            _logger?.LogDebug("적응형 그리드 크기 계산: 데이터범위={MaxDim:F1}m, 추정최대지오메트리={EstMaxGeom:F1}m, " +
                "안전그리드크기={MinSafe:F3}m, 최종그리드크기={Final:F3}m",
                maxDimension, estimatedMaxGeometrySize, minGridSizeForSafety, baseGridSize);
            
            return baseGridSize;
        }

        /// <summary>
        /// 공간 인덱스를 사용한 중복 검사
        /// </summary>
        /// <param name="layerName">레이어 이름</param>
        /// <param name="spatialIndex">공간 인덱스</param>
        /// <returns>중복 검사 결과</returns>
        public List<DuplicateResult> FindDuplicates(string layerName, SpatialIndex spatialIndex)
        {
            try
            {
                _logger.LogInformation("공간 인덱스 기반 중복 검사 시작: {LayerName}", layerName);
                var startTime = DateTime.Now;

                var duplicates = new List<DuplicateResult>();
                var processedObjects = new HashSet<string>();

                // 모든 객체에 대해 공간 인덱스 기반 검색
                foreach (var entry in spatialIndex.GetAllEntries())
                {
                    if (processedObjects.Contains(entry.ObjectId))
                        continue;

                    // 허용 오차 내의 인접 객체들 검색
                    var candidates = spatialIndex.Search(entry.Envelope, entry.Tolerance);
                    
                    foreach (var candidate in candidates)
                    {
                        if (candidate.ObjectId == entry.ObjectId || processedObjects.Contains(candidate.ObjectId))
                            continue;

                        try
                        {
                            // 정확한 거리 계산
                            var distance = entry.Geometry.Distance(candidate.Geometry);
                            if (distance < entry.Tolerance)
                            {
                                duplicates.Add(new DuplicateResult
                                {
                                    PrimaryObjectId = entry.ObjectId,
                                    DuplicateObjectId = candidate.ObjectId,
                                    Distance = distance,
                                    PrimaryGeometry = entry.Geometry,
                                    DuplicateGeometry = candidate.Geometry
                                });

                                processedObjects.Add(candidate.ObjectId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "거리 계산 실패: {ObjId1} vs {ObjId2}", 
                                entry.ObjectId, candidate.ObjectId);
                        }
                    }

                    processedObjects.Add(entry.ObjectId);
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("공간 인덱스 기반 중복 검사 완료: {LayerName}, 중복 {Count}개, 소요시간: {Elapsed:F2}초", 
                    layerName, duplicates.Count, elapsed);

                return duplicates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 인덱스 기반 중복 검사 실패: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 공간 인덱스를 사용한 겹침 검사
        /// </summary>
        /// <param name="layerName">레이어 이름</param>
        /// <param name="spatialIndex">공간 인덱스</param>
        /// <returns>겹침 검사 결과</returns>
        public List<OverlapResult> FindOverlaps(string layerName, SpatialIndex spatialIndex)
        {
            try
            {
                _logger.LogInformation("공간 인덱스 기반 겹침 검사 시작: {LayerName}", layerName);
                var startTime = DateTime.Now;

                var overlaps = new List<OverlapResult>();
                var processedObjects = new HashSet<string>();

                // 모든 객체에 대해 공간 인덱스 기반 검색
                foreach (var entry in spatialIndex.GetAllEntries())
                {
                    if (processedObjects.Contains(entry.ObjectId))
                        continue;

                    // 겹침 가능성이 있는 객체들 검색
                    var candidates = spatialIndex.Search(entry.Envelope, entry.Tolerance);
                    
                    foreach (var candidate in candidates)
                    {
                        if (candidate.ObjectId == entry.ObjectId || processedObjects.Contains(candidate.ObjectId))
                            continue;

                        try
                        {
                            // 교차 영역 계산
                            var intersection = entry.Geometry.Intersection(candidate.Geometry);
                            if (intersection != null && !intersection.IsEmpty())
                            {
                                var overlapArea = GetSurfaceArea(intersection);
                                if (overlapArea > 0)
                                {
                                    overlaps.Add(new OverlapResult
                                    {
                                        ObjectId = long.Parse(entry.ObjectId),
                                        OverlapArea = overlapArea,
                                        OverlappingObjectId = long.Parse(candidate.ObjectId),
                                        IntersectionGeometry = intersection.Clone()
                                    });
                                }
                            }
                            intersection?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "겹침 계산 실패: {ObjId1} vs {ObjId2}", 
                                entry.ObjectId, candidate.ObjectId);
                        }
                    }

                    processedObjects.Add(entry.ObjectId);
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("공간 인덱스 기반 겹침 검사 완료: {LayerName}, 겹침 {Count}개, 소요시간: {Elapsed:F2}초", 
                    layerName, overlaps.Count, elapsed);

                return overlaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 인덱스 기반 겹침 검사 실패: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 면적 계산 시 지오메트리 타입을 검사하여 면(Polygon/MultiPolygon)일 때만 면적을 반환합니다
        /// 면 형식이 아니면 0을 반환합니다.
        /// </summary>
        private static double GetSurfaceArea(Geometry geometry)
        {
            try
            {
                if (geometry == null || geometry.IsEmpty()) return 0.0;
                var type = geometry.GetGeometryType();
                return type == wkbGeometryType.wkbPolygon || type == wkbGeometryType.wkbMultiPolygon
                    ? geometry.GetArea()
                    : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// ObjectId 추출
        /// </summary>
        private string GetObjectId(Feature feature)
        {
            try
            {
                // OBJECTID 필드 시도
                var objectIdField = feature.GetFieldIndex("OBJECTID");
                if (objectIdField >= 0)
                {
                    var objectId = feature.GetFieldAsString(objectIdField);
                    if (!string.IsNullOrEmpty(objectId))
                        return objectId;
                }

                // FID 사용
                return feature.GetFID().ToString();
            }
            catch
            {
                return feature.GetFID().ToString();
            }
        }

        /// <summary>
        /// 지오메트리 엔벨로프 추출
        /// </summary>
        private SpatialEnvelope GetEnvelope(Geometry geometry)
        {
            var envelope = new OSGeo.OGR.Envelope();
            geometry.GetEnvelope(envelope);
            return new SpatialEnvelope(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var index in _spatialIndexes.Values)
                {
                    index.Dispose();
                }
                _spatialIndexes.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 공간 인덱스 구현 (R-tree 기반)
    /// </summary>
    public class SpatialIndex : IDisposable
    {
        private readonly double _tolerance;
        private readonly Dictionary<string, SpatialIndexEntry> _entries = new();
        private readonly Dictionary<string, List<string>> _gridIndex = new();
        private readonly double _gridSize;
        private readonly ILogger? _logger;
        private bool _disposed = false;

        public SpatialIndex(double tolerance, ILogger? logger = null, double? customGridSize = null)
        {
            _tolerance = tolerance;
            _gridSize = customGridSize ?? Math.Max(tolerance * 10, 1.0); // 커스텀 또는 기본 그리드 크기
            _logger = logger;
        }

        /// <summary>
        /// 공간 인덱스에 객체 삽입
        /// </summary>
        public void Insert(string objectId, Geometry geometry, SpatialEnvelope envelope)
        {
            var entry = new SpatialIndexEntry
            {
                ObjectId = objectId,
                Geometry = geometry,
                Envelope = envelope,
                Tolerance = _tolerance
            };

            _entries[objectId] = entry;

            // 그리드 기반 인덱스에 추가
            var gridKeys = GetGridKeys(envelope);
            foreach (var key in gridKeys)
            {
                if (!_gridIndex.ContainsKey(key))
                    _gridIndex[key] = new List<string>();
                _gridIndex[key].Add(objectId);
            }
        }

        /// <summary>
        /// 공간 인덱스에서 객체 제거
        /// </summary>
        public void Remove(string objectId)
        {
            if (_entries.TryGetValue(objectId, out var entry))
            {
                _entries.Remove(objectId);

                var gridKeys = GetGridKeys(entry.Envelope);
                foreach (var key in gridKeys)
                {
                    if (_gridIndex.TryGetValue(key, out var idList))
                    {
                        idList.Remove(objectId);
                    }
                }
            }
        }

        /// <summary>
        /// 공간 검색
        /// </summary>
        public List<SpatialIndexEntry> Search(SpatialEnvelope envelope, double tolerance)
        {
            var results = new List<SpatialIndexEntry>();
            var searchEnvelope = new SpatialEnvelope(
                envelope.MinX - tolerance,
                envelope.MinY - tolerance,
                envelope.MaxX + tolerance,
                envelope.MaxY + tolerance
            );

            var gridKeys = GetGridKeys(searchEnvelope);
            var candidateIds = new HashSet<string>();

            foreach (var key in gridKeys)
            {
                if (_gridIndex.TryGetValue(key, out var ids))
                {
                    foreach (var id in ids)
                    {
                        candidateIds.Add(id);
                    }
                }
            }

            foreach (var id in candidateIds)
            {
                if (_entries.TryGetValue(id, out var entry))
                {
                    // 바운딩 박스 교차 확인
                    if (EnvelopesIntersect(entry.Envelope, searchEnvelope))
                    {
                        results.Add(entry);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 모든 엔트리 반환
        /// </summary>
        public IEnumerable<SpatialIndexEntry> GetAllEntries()
        {
            return _entries.Values;
        }

        /// <summary>
        /// 그리드 키 생성 (개선판 - 다단계 폴백 전략)
        /// </summary>
        private List<string> GetGridKeys(SpatialEnvelope envelope)
        {
            var keys = new List<string>();
            
            var minGridX = (int)Math.Floor(envelope.MinX / _gridSize);
            var maxGridX = (int)Math.Floor(envelope.MaxX / _gridSize);
            var minGridY = (int)Math.Floor(envelope.MinY / _gridSize);
            var maxGridY = (int)Math.Floor(envelope.MaxY / _gridSize);

            var gridRangeX = maxGridX - minGridX + 1;
            var gridRangeY = maxGridY - minGridY + 1;
            
            // 안전장치: 총 그리드 셀 개수로 제한 (무한 루프 방지)
            // 개선: 동적 임계값 - 그리드 크기에 따라 조정
            long maxTotalCells = CalculateMaxCellsThreshold(_gridSize);
            long totalCells = (long)gridRangeX * gridRangeY;
            
            if (totalCells > maxTotalCells)
            {
                // 개선된 폴백 전략: 다단계 샘플링 (단일 셀보다 효율적)
                // 전략 1: 중대형 지오메트리 (10,000~maxTotalCells 셀) → 경계 샘플링
                if (totalCells <= maxTotalCells * 5)
                {
                    keys = GetBoundarySampledKeys(minGridX, maxGridX, minGridY, maxGridY, maxTotalCells);
                    
                    // 로그 레벨을 Debug → Trace로 변경 (상세 로그 감소)
                    // 필요시 Trace 레벨을 활성화하여 디버깅 가능
                    if (_logger?.IsEnabled(LogLevel.Trace) == true)
                    {
                        _logger.LogTrace(
                            "큰 지오메트리 경계 샘플링: 예상셀={TotalCells:N0}, 샘플={SampledCells}, " +
                            "범위=({RangeX}×{RangeY}), GridSize={GridSize}m, 효율={Efficiency:F2}%",
                            totalCells, keys.Count, gridRangeX, gridRangeY, _gridSize,
                            (1.0 - (double)keys.Count / totalCells) * 100);
                    }
                }
                // 전략 2: 초대형 지오메트리 (maxTotalCells * 5 초과) → 9개 대표 셀 (3×3 그리드)
                else
                {
                    keys = GetRepresentativeKeys(minGridX, maxGridX, minGridY, maxGridY);
                    
                    _logger?.LogWarning(
                        "초대형 지오메트리로 인한 대표 셀 샘플링 - 예상셀: {TotalCells:N0} (범위: {RangeX}×{RangeY}), " +
                        "Envelope: ({MinX:F2}, {MinY:F2}) - ({MaxX:F2}, {MaxY:F2}), GridSize: {GridSize}m, 대표셀={SampledCells}개",
                        totalCells, gridRangeX, gridRangeY, 
                        envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY, _gridSize, keys.Count);
                }
                
                return keys;
            }

            // 정상 범위: 모든 그리드 셀 추가
            for (int x = minGridX; x <= maxGridX; x++)
            {
                for (int y = minGridY; y <= maxGridY; y++)
                {
                    keys.Add($"{x}_{y}");
                }
            }

            return keys;
        }
        
        /// <summary>
        /// 그리드 크기에 따른 최대 셀 임계값 계산 (동적 조정)
        /// </summary>
        private long CalculateMaxCellsThreshold(double gridSize)
        {
            // 그리드가 클수록 더 많은 셀 허용 (상대적으로 안전)
            if (gridSize >= 10.0) // 10m 이상
            {
                return 500000; // 50만 셀 (예: 707×707)
            }
            else if (gridSize >= 1.0) // 1m 이상
            {
                return 250000; // 25만 셀 (예: 500×500)
            }
            else // 1m 미만 (세밀한 그리드)
            {
                return 100000; // 10만 셀 (예: 316×316) - 기존 값 유지
            }
        }
        
        /// <summary>
        /// 경계 샘플링: 지오메트리 경계를 따라 셀을 샘플링 (중대형 지오메트리용)
        /// </summary>
        private List<string> GetBoundarySampledKeys(int minX, int maxX, int minY, int maxY, long maxCells)
        {
            var keys = new HashSet<string>(); // 중복 제거
            
            int rangeX = maxX - minX + 1;
            int rangeY = maxY - minY + 1;
            
            // 샘플링 간격 계산 (최대 셀 수 이내로 조정)
            int totalBoundaryPoints = 2 * rangeX + 2 * rangeY - 4; // 경계 둘레
            int samplingStep = Math.Max(1, totalBoundaryPoints / (int)Math.Sqrt(maxCells));
            
            // 상단 경계
            for (int x = minX; x <= maxX; x += samplingStep)
            {
                keys.Add($"{x}_{minY}");
            }
            
            // 하단 경계
            for (int x = minX; x <= maxX; x += samplingStep)
            {
                keys.Add($"{x}_{maxY}");
            }
            
            // 좌측 경계
            for (int y = minY; y <= maxY; y += samplingStep)
            {
                keys.Add($"{minX}_{y}");
            }
            
            // 우측 경계
            for (int y = minY; y <= maxY; y += samplingStep)
            {
                keys.Add($"{maxX}_{y}");
            }
            
            // 중심 셀 추가 (검색 효율성 향상)
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;
            keys.Add($"{centerX}_{centerY}");
            
            return keys.ToList();
        }
        
        /// <summary>
        /// 대표 셀 선택: 3×3 그리드로 9개 대표 셀 추출 (초대형 지오메트리용)
        /// </summary>
        private List<string> GetRepresentativeKeys(int minX, int maxX, int minY, int maxY)
        {
            var keys = new List<string>();
            
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;
            
            int quarterX = (maxX - minX) / 4;
            int quarterY = (maxY - minY) / 4;
            
            // 3×3 그리드로 9개 대표 셀 생성
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = centerX + dx * quarterX;
                    int y = centerY + dy * quarterY;
                    keys.Add($"{x}_{y}");
                }
            }
            
            return keys;
        }

        /// <summary>
        /// 엔벨로프 교차 확인
        /// </summary>
        private bool EnvelopesIntersect(SpatialEnvelope env1, SpatialEnvelope env2)
        {
            return !(env1.MaxX < env2.MinX || env1.MinX > env2.MaxX ||
                     env1.MaxY < env2.MinY || env1.MinY > env2.MaxY);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var entry in _entries.Values)
                {
                    entry.Geometry?.Dispose();
                }
                _entries.Clear();
                _gridIndex.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 공간 인덱스 엔트리
    /// </summary>
    public class SpatialIndexEntry
    {
        public string ObjectId { get; set; } = string.Empty;
        public Geometry Geometry { get; set; } = null!;
        public SpatialEnvelope Envelope { get; set; }
        public double Tolerance { get; set; }
    }

    /// <summary>
    /// 중복 검사 결과
    /// </summary>
    public class DuplicateResult
    {
        public string PrimaryObjectId { get; set; } = string.Empty;
        public string DuplicateObjectId { get; set; } = string.Empty;
        public double Distance { get; set; }
        public Geometry PrimaryGeometry { get; set; } = null!;
        public Geometry DuplicateGeometry { get; set; } = null!;
    }


    /// <summary>
    /// 겹침 검사 결과
    /// </summary>
    public class OverlapResult
    {
        public long ObjectId { get; set; }
        public double OverlapArea { get; set; }
        public long OverlappingObjectId { get; set; }
        public Geometry? IntersectionGeometry { get; set; }
    }
}
