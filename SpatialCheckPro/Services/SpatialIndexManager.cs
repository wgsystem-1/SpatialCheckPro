using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 인덱스 관리자 구현
    /// </summary>
    public class SpatialIndexManager : ISpatialIndexManager
    {
        private readonly ILogger<SpatialIndexManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, ISpatialIndex> _indexCache;
        private readonly object _cacheLock = new object();

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="serviceProvider">서비스 제공자</param>
        public SpatialIndexManager(ILogger<SpatialIndexManager> logger, IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _indexCache = new Dictionary<string, ISpatialIndex>();
        }

        /// <summary>
        /// 공간 인덱스를 생성합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <param name="indexType">인덱스 타입</param>
        /// <returns>생성된 공간 인덱스</returns>
        public async Task<ISpatialIndex> CreateSpatialIndexAsync(
            string gdbPath,
            string layerName,
            SpatialIndexType indexType = SpatialIndexType.RTree)
        {
            var cacheKey = $"{gdbPath}:{layerName}:{indexType}";
            
            lock (_cacheLock)
            {
                // 캐시에서 기존 인덱스 확인
                if (_indexCache.TryGetValue(cacheKey, out var cachedIndex))
                {
                    _logger.LogDebug("캐시된 공간 인덱스 반환: {LayerName}", layerName);
                    return cachedIndex;
                }
            }

            try
            {
                _logger.LogInformation("공간 인덱스 생성 시작: {LayerName}, 타입: {IndexType}", layerName, indexType);

                ISpatialIndex spatialIndex = indexType switch
                {
                    SpatialIndexType.RTree => _serviceProvider.GetRequiredService<RTreeSpatialIndex>(),
                    SpatialIndexType.QuadTree => _serviceProvider.GetRequiredService<QuadTreeSpatialIndex>(),
                    SpatialIndexType.GridIndex => _serviceProvider.GetRequiredService<GridSpatialIndex>(),
                    SpatialIndexType.HashIndex => throw new NotImplementedException("HashIndex는 아직 구현되지 않았습니다"),
                    _ => throw new ArgumentException($"지원되지 않는 인덱스 타입: {indexType}")
                };

                // 인덱스 구축
                var success = await spatialIndex.BuildIndexAsync(gdbPath, layerName);
                if (!success)
                {
                    throw new InvalidOperationException($"공간 인덱스 구축 실패: {layerName}");
                }

                // 캐시에 저장
                lock (_cacheLock)
                {
                    _indexCache[cacheKey] = spatialIndex;
                }

                _logger.LogInformation("공간 인덱스 생성 완료: {LayerName}, 피처 수: {FeatureCount}", 
                    layerName, spatialIndex.GetFeatureCount());

                return spatialIndex;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 인덱스 생성 중 오류 발생: {LayerName}", layerName);
                throw;
            }
        }

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색합니다
        /// </summary>
        /// <param name="index">공간 인덱스</param>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <returns>교차하는 피처 ID 목록</returns>
        public async Task<List<long>> QueryIntersectingFeaturesAsync(
            ISpatialIndex index,
            SpatialEnvelope searchEnvelope)
        {
            if (index == null)
                throw new ArgumentNullException(nameof(index));
            
            if (searchEnvelope == null)
                throw new ArgumentNullException(nameof(searchEnvelope));

            try
            {
                var results = await index.QueryIntersectingFeaturesAsync(searchEnvelope);
                
                _logger.LogDebug("공간 질의 완료: {ResultCount}개 피처 검색됨", results.Count);
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 질의 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 두 인덱스 간의 공간 관계를 질의합니다
        /// </summary>
        /// <param name="sourceIndex">원본 인덱스</param>
        /// <param name="targetIndex">대상 인덱스</param>
        /// <param name="relationType">공간 관계 타입</param>
        /// <returns>공간 질의 결과 목록</returns>
        public async Task<List<SpatialQueryResult>> QuerySpatialRelationAsync(
            ISpatialIndex sourceIndex,
            ISpatialIndex targetIndex,
            SpatialRelationType relationType)
        {
            if (sourceIndex == null)
                throw new ArgumentNullException(nameof(sourceIndex));
            
            if (targetIndex == null)
                throw new ArgumentNullException(nameof(targetIndex));

            try
            {
                _logger.LogDebug("공간 관계 질의 시작: {RelationType}", relationType);
                
                var results = new List<SpatialQueryResult>();
                
                // 현재는 기본적인 교차 검사만 구현
                // 향후 각 관계 타입별로 세부 구현 필요
                switch (relationType)
                {
                    case SpatialRelationType.Intersects:
                    case SpatialRelationType.Overlaps:
                    case SpatialRelationType.Touches:
                        results = await QueryIntersectionRelationAsync(sourceIndex, targetIndex);
                        break;
                    
                    default:
                        _logger.LogWarning("아직 구현되지 않은 공간 관계 타입: {RelationType}", relationType);
                        break;
                }

                _logger.LogDebug("공간 관계 질의 완료: {ResultCount}개 관계 검색됨", results.Count);
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 관계 질의 중 오류 발생: {RelationType}", relationType);
                throw;
            }
        }

        /// <summary>
        /// 교차 관계 질의 (기본 구현)
        /// </summary>
        /// <param name="sourceIndex">원본 인덱스</param>
        /// <param name="targetIndex">대상 인덱스</param>
        /// <returns>교차 관계 결과</returns>
        private async Task<List<SpatialQueryResult>> QueryIntersectionRelationAsync(
            ISpatialIndex sourceIndex,
            ISpatialIndex targetIndex)
        {
            var results = new List<SpatialQueryResult>();
            
            // R-tree 인덱스인 경우 특별 처리
            if (sourceIndex is RTreeSpatialIndex sourceRTree && targetIndex is RTreeSpatialIndex targetRTree)
            {
                // 모든 원본 피처에 대해 대상 피처들과의 교차 검사
                var sourceStats = sourceRTree.GetStatistics();
                var sourceFeatureCount = (int)sourceStats["FeatureCount"];
                
                for (long sourceId = 0; sourceId < sourceFeatureCount; sourceId++)
                {
                    var sourceEnvelope = sourceRTree.GetFeatureEnvelope(sourceId);
                    if (sourceEnvelope != null)
                    {
                        var intersectingTargets = await targetIndex.QueryIntersectingFeaturesAsync(sourceEnvelope);
                        
                        foreach (var targetId in intersectingTargets)
                        {
                            results.Add(new SpatialQueryResult
                            {
                                SourceId = sourceId,
                                TargetId = targetId,
                                Distance = 0.0, // 교차하는 경우 거리는 0
                                IntersectionArea = 0.0 // 실제 면적 계산은 별도 구현 필요
                            });
                        }
                    }
                }
            }
            
            return results;
        }

        /// <summary>
        /// 인덱스 캐시를 정리합니다
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var index in _indexCache.Values)
                {
                    if (index is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                
                _indexCache.Clear();
                _logger.LogInformation("공간 인덱스 캐시가 정리되었습니다");
            }
        }

        /// <summary>
        /// 캐시 통계 정보 반환
        /// </summary>
        /// <returns>캐시 통계</returns>
        public Dictionary<string, object> GetCacheStatistics()
        {
            lock (_cacheLock)
            {
                var stats = new Dictionary<string, object>
                {
                    ["CachedIndexCount"] = _indexCache.Count,
                    ["CacheKeys"] = _indexCache.Keys.ToList()
                };

                var totalFeatures = 0;
                foreach (var index in _indexCache.Values)
                {
                    totalFeatures += index.GetFeatureCount();
                }
                stats["TotalCachedFeatures"] = totalFeatures;

                return stats;
            }
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            ClearCache();
            _logger.LogDebug("SpatialIndexManager 리소스가 해제되었습니다");
        }
    }
}
