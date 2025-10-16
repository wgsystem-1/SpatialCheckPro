using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.OSR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 폴리곤 간 위상 관계 검수를 수행하는 클래스
    /// </summary>
    public class PolygonTopologyChecker
    {
        private readonly ILogger<PolygonTopologyChecker> _logger;
        private readonly ISpatialIndexManager _spatialIndexManager;
        private readonly IGdalDataReader _gdalDataReader;
        private readonly IMemoryManager _memoryManager;

        // 메모리 효율적인 처리를 위한 설정값들
        private const int DEFAULT_BATCH_SIZE = 1000;
        private const int MIN_BATCH_SIZE = 100;
        private const int MAX_BATCH_SIZE = 5000;
        private const long LARGE_GEOMETRY_THRESHOLD = 1024 * 1024; // 1MB

        public PolygonTopologyChecker(
            ILogger<PolygonTopologyChecker> logger,
            ISpatialIndexManager spatialIndexManager,
            IGdalDataReader gdalDataReader,
            IMemoryManager memoryManager)
        {
            _logger = logger;
            _spatialIndexManager = spatialIndexManager;
            _gdalDataReader = gdalDataReader;
            _memoryManager = memoryManager;

            // 메모리 압박 이벤트 구독
            _memoryManager.MemoryPressureDetected += OnMemoryPressureDetected;
        }

        /// <summary>
        /// 폴리곤 간 위상 관계를 검사합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="sourceLayer">원본 레이어명</param>
        /// <param name="targetLayer">대상 레이어명</param>
        /// <param name="rule">위상 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>공간 관계 오류 목록</returns>
        public async Task<List<SpatialRelationError>> CheckAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("폴리곤 위상 관계 검수 시작: {SourceLayer} -> {TargetLayer}, 규칙: {RuleType}", 
                    sourceLayer, targetLayer, rule.RuleType);

                var errors = new List<SpatialRelationError>();

                // 위상 규칙 타입에 따라 적절한 검수 메서드 호출
                switch (rule.RuleType)
                {
                    case TopologyRuleType.MustNotOverlap:
                        errors = await CheckOverlapWithMemoryOptimizationAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                        break;

                    case TopologyRuleType.MustNotHaveGaps:
                        errors = await CheckGapsAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                        break;

                    case TopologyRuleType.MustBeCoveredBy:
                        errors = await CheckCoveredByAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                        break;

                    case TopologyRuleType.MustCover:
                        errors = await CheckCoverAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                        break;

                    case TopologyRuleType.MustNotIntersect:
                        errors = await CheckIntersectionAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                        break;

                    default:
                        _logger.LogWarning("지원되지 않는 위상 규칙 타입: {RuleType}", rule.RuleType);
                        break;
                }

                _logger.LogInformation("폴리곤 위상 관계 검수 완료: {ErrorCount}개 오류 발견", errors.Count);
                return errors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "폴리곤 위상 관계 검수 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 인접성(Touches) 관계를 검사합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="sourceLayer">원본 레이어명</param>
        /// <param name="targetLayer">대상 레이어명</param>
        /// <param name="rule">위상 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>공간 관계 오류 목록</returns>
        public async Task<List<SpatialRelationError>> CheckTouchesAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("인접성(Touches) 관계 검수 시작: {SourceLayer} -> {TargetLayer}", 
                    sourceLayer, targetLayer);

                // 1. 공간 인덱스 생성
                var sourceIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, sourceLayer);
                var targetIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, targetLayer);

                // 2. 공간 질의로 접촉 후보 검색
                var touchCandidates = await _spatialIndexManager.QuerySpatialRelationAsync(
                    sourceIndex, targetIndex, SpatialRelationType.Touches);

                // 3. GDAL을 사용하여 정확한 접촉 관계 검사
                using var sourceDataSource = Ogr.Open(gdbPath, 0);
                using var targetDataSource = Ogr.Open(gdbPath, 0);

                if (sourceDataSource == null || targetDataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = sourceDataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = targetDataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer} 또는 {targetLayer}");
                }

                foreach (var candidate in touchCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 원본 피처 조회
                    sourceLayerObj.SetAttributeFilter($"OBJECTID = {candidate.SourceId}");
                    var sourceFeature = sourceLayerObj.GetNextFeature();
                    
                    if (sourceFeature == null) continue;

                    var sourceGeometry = sourceFeature.GetGeometryRef();
                    if (sourceGeometry == null) continue;

                    // 대상 피처 조회
                    targetLayerObj.SetAttributeFilter($"OBJECTID = {candidate.TargetId}");
                    var targetFeature = targetLayerObj.GetNextFeature();
                    
                    if (targetFeature == null) continue;

                    var targetGeometry = targetFeature.GetGeometryRef();
                    if (targetGeometry == null) continue;

                    // 정확한 접촉 관계 검사
                    if (sourceGeometry.Touches(targetGeometry))
                    {
                        // 접촉 지점 계산
                        var intersection = sourceGeometry.Intersection(targetGeometry);
                        var centroid = intersection.Centroid();

                        var error = new SpatialRelationError
                        {
                            SourceObjectId = candidate.SourceId,
                            TargetObjectId = candidate.TargetId,
                            SourceLayer = sourceLayer,
                            TargetLayer = targetLayer,
                            RelationType = SpatialRelationType.Touches,
                            ErrorType = "TOUCHES_DETECTED",
                            Severity = rule.AllowExceptions ? ErrorSeverity.Warning : ErrorSeverity.Error,
                            ErrorLocationX = centroid.GetX(0),
                            ErrorLocationY = centroid.GetY(0),
                            GeometryWKT = GetWktFromGeometry(intersection),
                            Message = $"폴리곤 간 접촉 관계 감지: {sourceLayer}({candidate.SourceId}) - {targetLayer}({candidate.TargetId})",
                            DetectedAt = DateTime.UtcNow
                        };

                        errors.Add(error);
                    }

                    // 리소스 정리
                    sourceFeature.Dispose();
                    targetFeature.Dispose();
                }

                _logger.LogInformation("인접성(Touches) 관계 검수 완료: {ErrorCount}개 접촉 관계 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "인접성(Touches) 관계 검수 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 겹침(Overlaps) 관계를 검사하고 겹침 면적을 계산합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="sourceLayer">원본 레이어명</param>
        /// <param name="targetLayer">대상 레이어명</param>
        /// <param name="rule">위상 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>공간 관계 오류 목록</returns>
        public async Task<List<SpatialRelationError>> CheckOverlapAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("겹침(Overlaps) 관계 검수 시작: {SourceLayer} -> {TargetLayer}", 
                    sourceLayer, targetLayer);

                // 1. 공간 인덱스 생성
                var sourceIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, sourceLayer);
                var targetIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, targetLayer);

                // 2. 공간 질의로 겹침 후보 검색
                var overlapCandidates = await _spatialIndexManager.QuerySpatialRelationAsync(
                    sourceIndex, targetIndex, SpatialRelationType.Overlaps);

                // 3. GDAL을 사용하여 정확한 겹침 관계 검사
                using var sourceDataSource = Ogr.Open(gdbPath, 0);
                using var targetDataSource = Ogr.Open(gdbPath, 0);

                if (sourceDataSource == null || targetDataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = sourceDataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = targetDataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer} 또는 {targetLayer}");
                }

                foreach (var candidate in overlapCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 원본 피처 조회
                    sourceLayerObj.SetAttributeFilter($"OBJECTID = {candidate.SourceId}");
                    var sourceFeature = sourceLayerObj.GetNextFeature();
                    
                    if (sourceFeature == null) continue;

                    var sourceGeometry = sourceFeature.GetGeometryRef();
                    if (sourceGeometry == null) continue;

                    // 대상 피처 조회
                    targetLayerObj.SetAttributeFilter($"OBJECTID = {candidate.TargetId}");
                    var targetFeature = targetLayerObj.GetNextFeature();
                    
                    if (targetFeature == null) continue;

                    var targetGeometry = targetFeature.GetGeometryRef();
                    if (targetGeometry == null) continue;

                    // 정확한 겹침 관계 검사
                    if (sourceGeometry.Overlaps(targetGeometry))
                    {
                        // 겹침 영역 계산
                        var intersection = sourceGeometry.Intersection(targetGeometry);
                        var overlapArea = intersection.GetArea();

                        // 허용 오차 범위 확인
                        if (overlapArea > rule.Tolerance)
                        {
                            var centroid = intersection.Centroid();

                            var error = new SpatialRelationError
                            {
                                SourceObjectId = candidate.SourceId,
                                TargetObjectId = candidate.TargetId,
                                SourceLayer = sourceLayer,
                                TargetLayer = targetLayer,
                                RelationType = SpatialRelationType.Overlaps,
                                ErrorType = "OVERLAP_VIOLATION",
                                Severity = ErrorSeverity.Error,
                                ErrorLocationX = centroid.GetX(0),
                                ErrorLocationY = centroid.GetY(0),
                                GeometryWKT = GetWktFromGeometry(intersection),
                                Message = $"폴리곤 겹침 위반: {sourceLayer}({candidate.SourceId}) - {targetLayer}({candidate.TargetId}), 겹침 면적: {overlapArea:F2}㎡",
                                Properties = new Dictionary<string, object>
                                {
                                    { "OverlapArea", overlapArea },
                                    { "Tolerance", rule.Tolerance }
                                },
                                DetectedAt = DateTime.UtcNow
                            };

                            errors.Add(error);
                        }
                    }

                    // 리소스 정리
                    sourceFeature.Dispose();
                    targetFeature.Dispose();
                }

                _logger.LogInformation("겹침(Overlaps) 관계 검수 완료: {ErrorCount}개 겹침 위반 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "겹침(Overlaps) 관계 검수 중 오류 발생");
                throw;
            }

            return errors;
        }

        #region 메모리 효율적인 대용량 처리 메서드들

        /// <summary>
        /// 메모리 효율적인 대용량 폴리곤 겹침 검사
        /// </summary>
        public async Task<List<SpatialRelationError>> CheckOverlapWithMemoryOptimizationAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("메모리 최적화된 겹침 검사 시작: {SourceLayer} -> {TargetLayer}", 
                    sourceLayer, targetLayer);

                // 1. 레이어 크기 확인 및 처리 전략 결정
                var sourceCount = await _gdalDataReader.GetRecordCountAsync(gdbPath, sourceLayer);
                var targetCount = await _gdalDataReader.GetRecordCountAsync(gdbPath, targetLayer);
                
                _logger.LogInformation("레이어 크기: {SourceLayer}={SourceCount}개, {TargetLayer}={TargetCount}개", 
                    sourceLayer, sourceCount, targetLayer, targetCount);

                // 2. 대용량 데이터인 경우 청크 단위 처리
                if (sourceCount > 10000 || targetCount > 10000)
                {
                    errors = await ProcessLargeDatasetInChunksAsync(
                        gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                }
                else
                {
                    // 일반 크기 데이터는 기존 방식 사용
                    errors = await CheckOverlapAsync(gdbPath, sourceLayer, targetLayer, rule, cancellationToken);
                }

                _logger.LogInformation("메모리 최적화된 겹침 검사 완료: {ErrorCount}개 오류", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "메모리 최적화된 겹침 검사 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 대용량 데이터셋을 청크 단위로 처리합니다
        /// </summary>
        private async Task<List<SpatialRelationError>> ProcessLargeDatasetInChunksAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var allErrors = new List<SpatialRelationError>();
            var initialBatchSize = _memoryManager.GetOptimalBatchSize(DEFAULT_BATCH_SIZE, MIN_BATCH_SIZE);
            var currentBatchSize = initialBatchSize;

            _logger.LogInformation("청크 단위 처리 시작 - 초기 배치 크기: {BatchSize}", currentBatchSize);

            try
            {
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = dataSource.GetLayerByName(sourceLayer);
                if (sourceLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer}");
                }

                var totalFeatures = sourceLayerObj.GetFeatureCount(1);
                var processedFeatures = 0;
                var chunkIndex = 0;

                // 청크 단위로 처리
                while (processedFeatures < totalFeatures)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 메모리 상태 확인 및 배치 크기 동적 조정
                    currentBatchSize = await AdjustBatchSizeBasedOnMemoryAsync(currentBatchSize);

                    _logger.LogDebug("청크 {ChunkIndex} 처리 시작 - 배치 크기: {BatchSize}, 진행률: {Progress:P1}", 
                        chunkIndex, currentBatchSize, (double)processedFeatures / totalFeatures);

                    // 현재 청크의 피처들 처리
                    var chunkErrors = await ProcessFeatureChunkAsync(
                        dataSource, sourceLayer, targetLayer, rule, 
                        processedFeatures, currentBatchSize, cancellationToken);

                    allErrors.AddRange(chunkErrors);
                    processedFeatures += currentBatchSize;
                    chunkIndex++;

                    // 청크 처리 후 메모리 정리
                    await PerformChunkCleanupAsync();

                    // 진행률 로깅
                    if (chunkIndex % 10 == 0)
                    {
                        var memoryStats = _memoryManager.GetMemoryStatistics();
                        _logger.LogInformation("청크 처리 진행률: {Progress:P1}, 메모리 사용량: {MemoryMB:F2}MB", 
                            (double)processedFeatures / totalFeatures, 
                            memoryStats.CurrentMemoryUsage / (1024.0 * 1024.0));
                    }
                }

                _logger.LogInformation("청크 단위 처리 완료 - 총 {ChunkCount}개 청크, {ErrorCount}개 오류", 
                    chunkIndex, allErrors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "청크 단위 처리 중 오류 발생");
                throw;
            }

            return allErrors;
        }

        /// <summary>
        /// 피처 청크를 처리합니다
        /// </summary>
        private async Task<List<SpatialRelationError>> ProcessFeatureChunkAsync(
            DataSource dataSource,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            int startIndex,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                var sourceLayerObj = dataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = dataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    return errors;
                }

                // 원본 레이어에서 현재 청크의 피처들 가져오기
                var sourceFeatures = await GetFeatureChunkAsync(sourceLayerObj, startIndex, batchSize, cancellationToken);

                // 각 원본 피처에 대해 대상 레이어와의 관계 검사
                foreach (var sourceFeature in sourceFeatures)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceGeometry = sourceFeature.GetGeometryRef();
                    if (sourceGeometry == null) continue;

                    var sourceObjectId = GetObjectId(sourceFeature);

                    // 복잡한 지오메트리인 경우 단순화 처리
                    var processedGeometry = await SimplifyComplexGeometryIfNeededAsync(sourceGeometry);

                    // 대상 레이어와의 관계 검사
                    var featureErrors = await CheckFeatureAgainstTargetLayerAsync(
                        processedGeometry, sourceObjectId, sourceLayer, targetLayerObj, targetLayer, rule, cancellationToken);

                    errors.AddRange(featureErrors);

                    // 단순화된 지오메트리 정리
                    if (processedGeometry != sourceGeometry)
                    {
                        processedGeometry.Dispose();
                    }
                }

                // 피처들 정리
                foreach (var feature in sourceFeatures)
                {
                    feature.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "피처 청크 처리 중 오류 발생: StartIndex={StartIndex}, BatchSize={BatchSize}", 
                    startIndex, batchSize);
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 피처 청크를 가져옵니다
        /// </summary>
        private async Task<List<Feature>> GetFeatureChunkAsync(
            Layer layer, 
            int startIndex, 
            int batchSize, 
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var features = new List<Feature>();
                layer.ResetReading();

                // 시작 인덱스까지 건너뛰기
                for (int i = 0; i < startIndex; i++)
                {
                    var skipFeature = layer.GetNextFeature();
                    skipFeature?.Dispose();
                }

                // 배치 크기만큼 피처 수집
                for (int i = 0; i < batchSize; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var feature = layer.GetNextFeature();
                    if (feature == null) break;

                    features.Add(feature);
                }

                return features;
            });
        }

        /// <summary>
        /// 복잡한 지오메트리를 필요시 단순화합니다
        /// </summary>
        private async Task<Geometry> SimplifyComplexGeometryIfNeededAsync(Geometry geometry)
        {
            return await Task.Run(() =>
            {
                // 지오메트리 크기 확인
                string wkt;
                geometry.ExportToWkt(out wkt);
                var geometrySize = System.Text.Encoding.UTF8.GetByteCount(wkt);

                // 대용량 지오메트리인 경우 단순화
                if (geometrySize > LARGE_GEOMETRY_THRESHOLD)
                {
                    _logger.LogDebug("대용량 지오메트리 단순화 수행: {SizeKB}KB", geometrySize / 1024);
                    
                    // Douglas-Peucker 알고리즘으로 단순화 (허용 오차: 1m)
                    var simplifiedGeometry = geometry.Simplify(1.0);
                    return simplifiedGeometry ?? geometry.Clone();
                }

                return geometry.Clone();
            });
        }

        /// <summary>
        /// 개별 피처를 대상 레이어와 비교 검사합니다
        /// </summary>
        private async Task<List<SpatialRelationError>> CheckFeatureAgainstTargetLayerAsync(
            Geometry sourceGeometry,
            long sourceObjectId,
            string sourceLayer,
            Layer targetLayer,
            string targetLayerName,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                // 공간 필터 설정으로 후보 피처들만 검색
                var envelope = new OSGeo.OGR.Envelope();
                sourceGeometry.GetEnvelope(envelope);
                targetLayer.SetSpatialFilterRect(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);

                targetLayer.ResetReading();
                Feature targetFeature;

                while ((targetFeature = targetLayer.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var targetGeometry = targetFeature.GetGeometryRef();
                    if (targetGeometry == null) continue;

                    var targetObjectId = GetObjectId(targetFeature);

                    // 위상 규칙에 따른 검사 수행
                    var error = await CheckTopologyRuleAsync(
                        sourceGeometry, sourceObjectId, sourceLayer,
                        targetGeometry, targetObjectId, targetLayerName,
                        rule, cancellationToken);

                    if (error != null)
                    {
                        errors.Add(error);
                    }

                    targetFeature.Dispose();
                }

                // 공간 필터 해제
                targetLayer.SetSpatialFilter(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "피처 대 레이어 검사 중 오류 발생: SourceObjectId={SourceObjectId}", sourceObjectId);
            }

            return errors;
        }

        /// <summary>
        /// 위상 규칙을 검사합니다
        /// </summary>
        private async Task<SpatialRelationError?> CheckTopologyRuleAsync(
            Geometry sourceGeometry, long sourceObjectId, string sourceLayer,
            Geometry targetGeometry, long targetObjectId, string targetLayer,
            TopologyRule rule, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    switch (rule.RuleType)
                    {
                        case TopologyRuleType.MustNotOverlap:
                            if (sourceGeometry.Overlaps(targetGeometry))
                            {
                                var intersection = sourceGeometry.Intersection(targetGeometry);
                                var overlapArea = intersection.GetArea();
                                
                                if (overlapArea > rule.Tolerance)
                                {
                                    var centroid = intersection.Centroid();
                                    var error = CreateSpatialRelationError(
                                        sourceObjectId, targetObjectId, sourceLayer, targetLayer,
                                        SpatialRelationType.Overlaps, "OVERLAP_VIOLATION",
                                        centroid, intersection, 
                                        $"겹침 위반: 면적 {overlapArea:F2}㎡", overlapArea, rule.Tolerance);
                                    
                                    intersection.Dispose();
                                    centroid.Dispose();
                                    return error;
                                }
                                intersection.Dispose();
                            }
                            break;

                        case TopologyRuleType.MustNotIntersect:
                            if (sourceGeometry.Intersects(targetGeometry) && !sourceGeometry.Touches(targetGeometry))
                            {
                                var intersection = sourceGeometry.Intersection(targetGeometry);
                                var intersectionArea = intersection.GetArea();
                                
                                if (intersectionArea > rule.Tolerance)
                                {
                                    var centroid = intersection.Centroid();
                                    var error = CreateSpatialRelationError(
                                        sourceObjectId, targetObjectId, sourceLayer, targetLayer,
                                        SpatialRelationType.Intersects, "INTERSECTION_VIOLATION",
                                        centroid, intersection,
                                        $"교차 금지 위반: 면적 {intersectionArea:F2}㎡", intersectionArea, rule.Tolerance);
                                    
                                    intersection.Dispose();
                                    centroid.Dispose();
                                    return error;
                                }
                                intersection.Dispose();
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "위상 규칙 검사 중 오류 발생: {SourceObjectId} - {TargetObjectId}", 
                        sourceObjectId, targetObjectId);
                }

                return null;
            });
        }

        /// <summary>
        /// 메모리 상태에 따라 배치 크기를 동적으로 조정합니다
        /// </summary>
        private async Task<int> AdjustBatchSizeBasedOnMemoryAsync(int currentBatchSize)
        {
            var memoryStats = _memoryManager.GetMemoryStatistics();
            
            // 메모리 압박 상황인 경우 배치 크기 감소
            if (memoryStats.IsUnderPressure)
            {
                var newBatchSize = Math.Max(MIN_BATCH_SIZE, currentBatchSize / 2);
                _logger.LogWarning("메모리 압박으로 배치 크기 감소: {OldSize} -> {NewSize}", 
                    currentBatchSize, newBatchSize);
                
                // 메모리 정리 시도
                await _memoryManager.TryReduceMemoryPressureAsync();
                
                return newBatchSize;
            }
            
            // 메모리 여유가 있는 경우 배치 크기 증가 (최대값 제한)
            if (memoryStats.PressureRatio < 0.5 && currentBatchSize < MAX_BATCH_SIZE)
            {
                var newBatchSize = Math.Min(MAX_BATCH_SIZE, (int)(currentBatchSize * 1.2));
                if (newBatchSize != currentBatchSize)
                {
                    _logger.LogDebug("메모리 여유로 배치 크기 증가: {OldSize} -> {NewSize}", 
                        currentBatchSize, newBatchSize);
                }
                return newBatchSize;
            }
            
            return currentBatchSize;
        }

        /// <summary>
        /// 청크 처리 후 메모리 정리를 수행합니다
        /// </summary>
        private async Task PerformChunkCleanupAsync()
        {
            // 메모리 압박 상황인 경우 강제 정리
            if (_memoryManager.IsMemoryPressureHigh())
            {
                await _memoryManager.TryReduceMemoryPressureAsync();
            }
            
            // 일정 간격으로 가벼운 정리 수행
            _memoryManager.ForceGarbageCollection();
            
            // 정리 후 잠시 대기
            await Task.Delay(50);
        }

        /// <summary>
        /// 메모리 압박 이벤트 핸들러
        /// </summary>
        private void OnMemoryPressureDetected(object? sender, MemoryPressureEventArgs e)
        {
            _logger.LogWarning("메모리 압박 상황 감지: 사용률 {PressureRatio:P1}, 권장조치: {RecommendedAction}", 
                e.PressureRatio, e.RecommendedAction);
        }

        /// <summary>
        /// 공간 관계 오류 객체를 생성합니다
        /// </summary>
        private SpatialRelationError CreateSpatialRelationError(
            long sourceObjectId, long targetObjectId, string sourceLayer, string targetLayer,
            SpatialRelationType relationType, string errorType,
            Geometry centroid, Geometry errorGeometry,
            string message, double errorValue, double tolerance)
        {
            return new SpatialRelationError
            {
                SourceObjectId = sourceObjectId,
                TargetObjectId = targetObjectId,
                SourceLayer = sourceLayer,
                TargetLayer = targetLayer,
                RelationType = relationType,
                ErrorType = errorType,
                Severity = ErrorSeverity.Error,
                ErrorLocationX = centroid.GetX(0),
                ErrorLocationY = centroid.GetY(0),
                GeometryWKT = GetWktFromGeometry(errorGeometry),
                Message = message,
                Properties = new Dictionary<string, object>
                {
                    { "ErrorValue", errorValue },
                    { "Tolerance", tolerance },
                    { "ProcessedWithMemoryOptimization", true }
                },
                DetectedAt = DateTime.UtcNow
            };
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// 틈(Gaps) 검사를 수행합니다 - MustNotHaveGaps 규칙 구현
        /// </summary>
        private async Task<List<SpatialRelationError>> CheckGapsAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("틈(Gaps) 검사 시작: {SourceLayer} -> {TargetLayer}", sourceLayer, targetLayer);

                // 1. GDAL을 사용하여 레이어 열기
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = dataSource.GetLayerByName(sourceLayer);
                if (sourceLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer}");
                }

                // 2. 모든 폴리곤을 하나의 MultiPolygon으로 결합
                var allPolygons = new List<Geometry>();
                sourceLayerObj.ResetReading();

                Feature feature;
                while ((feature = sourceLayerObj.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var geometry = feature.GetGeometryRef();
                    if (geometry != null && geometry.GetGeometryType() == wkbGeometryType.wkbPolygon)
                    {
                        // 지오메트리 복사본 생성
                        var clonedGeometry = geometry.Clone();
                        allPolygons.Add(clonedGeometry);
                    }
                    feature.Dispose();
                }

                if (allPolygons.Count < 2)
                {
                    _logger.LogInformation("틈 검사를 위한 충분한 폴리곤이 없습니다: {Count}개", allPolygons.Count);
                    return errors;
                }

                // 3. 전체 영역의 경계 계산
                var envelope = new OSGeo.OGR.Envelope();
                foreach (var polygon in allPolygons)
                {
                    var polyEnvelope = new OSGeo.OGR.Envelope();
                    polygon.GetEnvelope(polyEnvelope);
                    
                    if (envelope.MinX == 0 && envelope.MaxX == 0) // 첫 번째 폴리곤
                    {
                        envelope = polyEnvelope;
                    }
                    else
                    {
                        envelope.MinX = Math.Min(envelope.MinX, polyEnvelope.MinX);
                        envelope.MinY = Math.Min(envelope.MinY, polyEnvelope.MinY);
                        envelope.MaxX = Math.Max(envelope.MaxX, polyEnvelope.MaxX);
                        envelope.MaxY = Math.Max(envelope.MaxY, polyEnvelope.MaxY);
                    }
                }

                // 4. 전체 영역을 덮는 사각형 생성
                var boundingBox = CreateBoundingBoxGeometry(envelope);

                // 5. 모든 폴리곤의 합집합 계산
                Geometry unionGeometry = allPolygons[0].Clone();
                for (int i = 1; i < allPolygons.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var tempUnion = unionGeometry.Union(allPolygons[i]);
                    unionGeometry.Dispose();
                    unionGeometry = tempUnion;
                }

                // 6. 경계 사각형에서 합집합을 빼서 틈 영역 계산
                var gapsGeometry = boundingBox.Difference(unionGeometry);

                // 7. 틈 영역이 존재하는지 확인
                if (gapsGeometry != null && !gapsGeometry.IsEmpty())
                {
                    var gapArea = gapsGeometry.GetArea();
                    
                    // 허용 오차보다 큰 틈만 오류로 처리
                    if (gapArea > rule.Tolerance)
                    {
                        var centroid = gapsGeometry.Centroid();
                        
                        var error = new SpatialRelationError
                        {
                            SourceObjectId = 0, // 틈은 특정 객체에 속하지 않음
                            TargetObjectId = null,
                            SourceLayer = sourceLayer,
                            TargetLayer = targetLayer,
                            RelationType = SpatialRelationType.Disjoint,
                            ErrorType = "GAP_DETECTED",
                            Severity = ErrorSeverity.Critical,
                            ErrorLocationX = centroid.GetX(0),
                            ErrorLocationY = centroid.GetY(0),
                            GeometryWKT = GetWktFromGeometry(gapsGeometry),
                            Message = $"폴리곤 레이어에 틈 발견: {sourceLayer}, 틈 면적: {gapArea:F2}㎡",
                            Properties = new Dictionary<string, object>
                            {
                                { "GapArea", gapArea },
                                { "Tolerance", rule.Tolerance }
                            },
                            DetectedAt = DateTime.UtcNow
                        };

                        errors.Add(error);
                    }
                }

                // 8. 리소스 정리
                foreach (var polygon in allPolygons)
                {
                    polygon.Dispose();
                }
                unionGeometry.Dispose();
                boundingBox.Dispose();
                gapsGeometry?.Dispose();

                _logger.LogInformation("틈(Gaps) 검사 완료: {ErrorCount}개 틈 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "틈(Gaps) 검사 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 덮임(Covered By) 검사를 수행합니다 - MustBeCoveredBy 규칙 구현
        /// </summary>
        private async Task<List<SpatialRelationError>> CheckCoveredByAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("덮임(Covered By) 검사 시작: {SourceLayer} -> {TargetLayer}", sourceLayer, targetLayer);

                // 1. 공간 인덱스 생성
                var sourceIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, sourceLayer);
                var targetIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, targetLayer);

                // 2. GDAL을 사용하여 레이어 열기
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = dataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = dataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer} 또는 {targetLayer}");
                }

                // 3. 대상 레이어의 모든 폴리곤을 합집합으로 생성
                var targetUnion = await CreateLayerUnionAsync(targetLayerObj, cancellationToken);

                // 4. 원본 레이어의 각 폴리곤이 대상 레이어에 완전히 덮여있는지 확인
                sourceLayerObj.ResetReading();
                Feature sourceFeature;
                
                while ((sourceFeature = sourceLayerObj.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceGeometry = sourceFeature.GetGeometryRef();
                    if (sourceGeometry == null) continue;

                    var sourceObjectId = GetObjectId(sourceFeature);

                    // 원본 폴리곤이 대상 합집합에 완전히 포함되는지 확인
                    if (!targetUnion.Contains(sourceGeometry))
                    {
                        // 덮이지 않은 부분 계산
                        var uncoveredArea = sourceGeometry.Difference(targetUnion);
                        
                        if (uncoveredArea != null && !uncoveredArea.IsEmpty())
                        {
                            var uncoveredAreaSize = uncoveredArea.GetArea();
                            
                            // 허용 오차보다 큰 덮이지 않은 영역만 오류로 처리
                            if (uncoveredAreaSize > rule.Tolerance)
                            {
                                var centroid = uncoveredArea.Centroid();
                                
                                var error = new SpatialRelationError
                                {
                                    SourceObjectId = sourceObjectId,
                                    TargetObjectId = null,
                                    SourceLayer = sourceLayer,
                                    TargetLayer = targetLayer,
                                    RelationType = SpatialRelationType.Within,
                                    ErrorType = "NOT_COVERED_BY",
                                    Severity = ErrorSeverity.Warning,
                                    ErrorLocationX = centroid.GetX(0),
                                    ErrorLocationY = centroid.GetY(0),
                                    GeometryWKT = GetWktFromGeometry(uncoveredArea),
                                    Message = $"폴리곤이 완전히 덮이지 않음: {sourceLayer}({sourceObjectId}), 덮이지 않은 면적: {uncoveredAreaSize:F2}㎡",
                                    Properties = new Dictionary<string, object>
                                    {
                                        { "UncoveredArea", uncoveredAreaSize },
                                        { "Tolerance", rule.Tolerance }
                                    },
                                    DetectedAt = DateTime.UtcNow
                                };

                                errors.Add(error);
                            }
                            
                            uncoveredArea.Dispose();
                        }
                    }

                    sourceFeature.Dispose();
                }

                // 5. 리소스 정리
                targetUnion.Dispose();

                _logger.LogInformation("덮임(Covered By) 검사 완료: {ErrorCount}개 덮임 위반 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "덮임(Covered By) 검사 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 덮기(Cover) 검사를 수행합니다 - MustCover 규칙 구현
        /// </summary>
        private async Task<List<SpatialRelationError>> CheckCoverAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("덮기(Cover) 검사 시작: {SourceLayer} -> {TargetLayer}", sourceLayer, targetLayer);

                // 1. GDAL을 사용하여 레이어 열기
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = dataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = dataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer} 또는 {targetLayer}");
                }

                // 2. 원본 레이어의 모든 폴리곤을 합집합으로 생성
                var sourceUnion = await CreateLayerUnionAsync(sourceLayerObj, cancellationToken);

                // 3. 대상 레이어의 각 폴리곤이 원본 레이어에 완전히 덮여있는지 확인
                targetLayerObj.ResetReading();
                Feature targetFeature;
                
                while ((targetFeature = targetLayerObj.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var targetGeometry = targetFeature.GetGeometryRef();
                    if (targetGeometry == null) continue;

                    var targetObjectId = GetObjectId(targetFeature);

                    // 대상 폴리곤이 원본 합집합에 완전히 포함되는지 확인
                    if (!sourceUnion.Contains(targetGeometry))
                    {
                        // 덮이지 않은 부분 계산
                        var uncoveredArea = targetGeometry.Difference(sourceUnion);
                        
                        if (uncoveredArea != null && !uncoveredArea.IsEmpty())
                        {
                            var uncoveredAreaSize = uncoveredArea.GetArea();
                            
                            // 허용 오차보다 큰 덮이지 않은 영역만 오류로 처리
                            if (uncoveredAreaSize > rule.Tolerance)
                            {
                                var centroid = uncoveredArea.Centroid();
                                
                                var error = new SpatialRelationError
                                {
                                    SourceObjectId = targetObjectId,
                                    TargetObjectId = null,
                                    SourceLayer = targetLayer,
                                    TargetLayer = sourceLayer,
                                    RelationType = SpatialRelationType.Contains,
                                    ErrorType = "NOT_COVERED",
                                    Severity = ErrorSeverity.Warning,
                                    ErrorLocationX = centroid.GetX(0),
                                    ErrorLocationY = centroid.GetY(0),
                                    GeometryWKT = GetWktFromGeometry(uncoveredArea),
                                    Message = $"폴리곤이 완전히 덮지 못함: {targetLayer}({targetObjectId}), 덮지 못한 면적: {uncoveredAreaSize:F2}㎡",
                                    Properties = new Dictionary<string, object>
                                    {
                                        { "UncoveredArea", uncoveredAreaSize },
                                        { "Tolerance", rule.Tolerance }
                                    },
                                    DetectedAt = DateTime.UtcNow
                                };

                                errors.Add(error);
                            }
                            
                            uncoveredArea.Dispose();
                        }
                    }

                    targetFeature.Dispose();
                }

                // 4. 리소스 정리
                sourceUnion.Dispose();

                _logger.LogInformation("덮기(Cover) 검사 완료: {ErrorCount}개 덮기 위반 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "덮기(Cover) 검사 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 교차 금지(Must Not Intersect) 검사를 수행합니다 - MustNotIntersect 규칙 구현
        /// </summary>
        private async Task<List<SpatialRelationError>> CheckIntersectionAsync(
            string gdbPath,
            string sourceLayer,
            string targetLayer,
            TopologyRule rule,
            CancellationToken cancellationToken)
        {
            var errors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("교차 금지(Must Not Intersect) 검사 시작: {SourceLayer} -> {TargetLayer}", sourceLayer, targetLayer);

                // 1. 공간 인덱스 생성
                var sourceIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, sourceLayer);
                var targetIndex = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, targetLayer);

                // 2. 공간 질의로 교차 후보 검색
                var intersectionCandidates = await _spatialIndexManager.QuerySpatialRelationAsync(
                    sourceIndex, targetIndex, SpatialRelationType.Intersects);

                // 3. GDAL을 사용하여 정확한 교차 관계 검사
                using var sourceDataSource = Ogr.Open(gdbPath, 0);
                using var targetDataSource = Ogr.Open(gdbPath, 0);

                if (sourceDataSource == null || targetDataSource == null)
                {
                    throw new InvalidOperationException($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                var sourceLayerObj = sourceDataSource.GetLayerByName(sourceLayer);
                var targetLayerObj = targetDataSource.GetLayerByName(targetLayer);

                if (sourceLayerObj == null || targetLayerObj == null)
                {
                    throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {sourceLayer} 또는 {targetLayer}");
                }

                foreach (var candidate in intersectionCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 원본 피처 조회
                    sourceLayerObj.SetAttributeFilter($"OBJECTID = {candidate.SourceId}");
                    var sourceFeature = sourceLayerObj.GetNextFeature();
                    
                    if (sourceFeature == null) continue;

                    var sourceGeometry = sourceFeature.GetGeometryRef();
                    if (sourceGeometry == null) continue;

                    // 대상 피처 조회
                    targetLayerObj.SetAttributeFilter($"OBJECTID = {candidate.TargetId}");
                    var targetFeature = targetLayerObj.GetNextFeature();
                    
                    if (targetFeature == null) continue;

                    var targetGeometry = targetFeature.GetGeometryRef();
                    if (targetGeometry == null) continue;

                    // 정확한 교차 관계 검사 (접촉은 제외)
                    if (sourceGeometry.Intersects(targetGeometry) && !sourceGeometry.Touches(targetGeometry))
                    {
                        // 교차 영역 계산
                        var intersection = sourceGeometry.Intersection(targetGeometry);
                        var intersectionArea = intersection.GetArea();

                        // 허용 오차 범위 확인
                        if (intersectionArea > rule.Tolerance)
                        {
                            var centroid = intersection.Centroid();

                            var error = new SpatialRelationError
                            {
                                SourceObjectId = candidate.SourceId,
                                TargetObjectId = candidate.TargetId,
                                SourceLayer = sourceLayer,
                                TargetLayer = targetLayer,
                                RelationType = SpatialRelationType.Intersects,
                                ErrorType = "INTERSECTION_VIOLATION",
                                Severity = ErrorSeverity.Error,
                                ErrorLocationX = centroid.GetX(0),
                                ErrorLocationY = centroid.GetY(0),
                                GeometryWKT = GetWktFromGeometry(intersection),
                                Message = $"교차 금지 위반: {sourceLayer}({candidate.SourceId}) - {targetLayer}({candidate.TargetId}), 교차 면적: {intersectionArea:F2}㎡",
                                Properties = new Dictionary<string, object>
                                {
                                    { "IntersectionArea", intersectionArea },
                                    { "Tolerance", rule.Tolerance }
                                },
                                DetectedAt = DateTime.UtcNow
                            };

                            errors.Add(error);
                        }
                        
                        intersection.Dispose();
                    }

                    // 리소스 정리
                    sourceFeature.Dispose();
                    targetFeature.Dispose();
                }

                _logger.LogInformation("교차 금지(Must Not Intersect) 검사 완료: {ErrorCount}개 교차 위반 발견", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "교차 금지(Must Not Intersect) 검사 중 오류 발생");
                throw;
            }

            return errors;
        }

        /// <summary>
        /// 경계 사각형 지오메트리를 생성합니다
        /// </summary>
        private Geometry CreateBoundingBoxGeometry(OSGeo.OGR.Envelope envelope)
        {
            var ring = new Geometry(wkbGeometryType.wkbLinearRing);
            ring.AddPoint_2D(envelope.MinX, envelope.MinY);
            ring.AddPoint_2D(envelope.MaxX, envelope.MinY);
            ring.AddPoint_2D(envelope.MaxX, envelope.MaxY);
            ring.AddPoint_2D(envelope.MinX, envelope.MaxY);
            ring.AddPoint_2D(envelope.MinX, envelope.MinY); // 닫기

            var polygon = new Geometry(wkbGeometryType.wkbPolygon);
            polygon.AddGeometry(ring);
            
            ring.Dispose();
            return polygon;
        }

        /// <summary>
        /// 레이어의 모든 폴리곤을 합집합으로 생성합니다
        /// </summary>
        private async Task<Geometry> CreateLayerUnionAsync(Layer layer, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                layer.ResetReading();
                Geometry unionGeometry = null;

                Feature feature;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var geometry = feature.GetGeometryRef();
                    if (geometry != null && geometry.GetGeometryType() == wkbGeometryType.wkbPolygon)
                    {
                        if (unionGeometry == null)
                        {
                            unionGeometry = geometry.Clone();
                        }
                        else
                        {
                            var tempUnion = unionGeometry.Union(geometry);
                            unionGeometry.Dispose();
                            unionGeometry = tempUnion;
                        }
                    }
                    feature.Dispose();
                }

                // 빈 지오메트리 반환 (합집합할 폴리곤이 없는 경우)
                if (unionGeometry == null)
                {
                    unionGeometry = new Geometry(wkbGeometryType.wkbPolygon);
                }

                return unionGeometry;
            });
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
                var objectIdValue = feature.GetFieldAsString(objectIdIndex);
                if (long.TryParse(objectIdValue, out long objectId))
                {
                    return objectId;
                }
            }

            // FID 폴백 사용
            var fidIndex = feature.GetFieldIndex("FID");
            if (fidIndex >= 0)
            {
                var fidValue = feature.GetFieldAsString(fidIndex);
                if (long.TryParse(fidValue, out long fid))
                {
                    return fid;
                }
            }

            // 기본값으로 FID 사용
            return feature.GetFID();
        }

        /// <summary>
        /// 지오메트리에서 WKT 문자열을 추출합니다
        /// </summary>
        private string GetWktFromGeometry(Geometry geometry)
        {
            if (geometry == null) return string.Empty;
            
            string wkt;
            geometry.ExportToWkt(out wkt);
            return wkt ?? string.Empty;
        }

        #endregion
    }
}
