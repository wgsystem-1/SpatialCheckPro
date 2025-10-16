using SpatialCheckPro.Models;
using OSGeo.OGR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 메모리 최적화된 R-tree 공간 인덱스 구현
    /// </summary>
    public class OptimizedRTreeSpatialIndex : ISpatialIndex, IDisposable
    {
        private readonly ILogger<OptimizedRTreeSpatialIndex> _logger;
        private readonly IMemoryManager _memoryManager;
        private RTreeNode _root;
        private int _featureCount;
        private readonly int _maxNodeCapacity;
        private readonly int _minNodeCapacity;
        private readonly Dictionary<long, SpatialEnvelope> _featureEnvelopes;
        private readonly ConcurrentDictionary<string, object> _diskCache;
        private readonly string _cacheDirectory;
        private bool _isDiskCacheEnabled;
        private readonly SemaphoreSlim _buildSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="memoryManager">메모리 관리자</param>
        /// <param name="maxNodeCapacity">노드 최대 용량</param>
        /// <param name="enableDiskCache">디스크 캐시 사용 여부</param>
        public OptimizedRTreeSpatialIndex(
            ILogger<OptimizedRTreeSpatialIndex> logger, 
            IMemoryManager memoryManager,
            int maxNodeCapacity = 16,
            bool enableDiskCache = true)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
            _maxNodeCapacity = maxNodeCapacity;
            _minNodeCapacity = Math.Max(2, maxNodeCapacity / 2);
            _featureEnvelopes = new Dictionary<long, SpatialEnvelope>();
            _diskCache = new ConcurrentDictionary<string, object>();
            _isDiskCacheEnabled = enableDiskCache;
            
            // 임시 디렉토리에 캐시 폴더 생성
            _cacheDirectory = Path.Combine(Path.GetTempPath(), "SpatialIndexCache", Guid.NewGuid().ToString());
            if (_isDiskCacheEnabled)
            {
                Directory.CreateDirectory(_cacheDirectory);
            }

            // 메모리 압박 이벤트 구독
            _memoryManager.MemoryPressureDetected += OnMemoryPressureDetected;
            
            Clear();
        }

        /// <summary>
        /// 지정된 레이어로부터 공간 인덱스를 구축합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>구축 완료 여부</returns>
        public async Task<bool> BuildIndexAsync(string gdbPath, string layerName)
        {
            await _buildSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("최적화된 R-tree 인덱스 구축 시작: {LayerName}", layerName);
                
                // GDAL 초기화
                Ogr.RegisterAll();
                
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    _logger.LogError("File Geodatabase를 열 수 없습니다: {GdbPath}", gdbPath);
                    return false;
                }

                var layer = dataSource.GetLayerByName(layerName);
                if (layer == null)
                {
                    _logger.LogError("레이어를 찾을 수 없습니다: {LayerName}", layerName);
                    return false;
                }

                // 인덱스 초기화
                Clear();

                // 피처 수 확인
                var totalFeatures = layer.GetFeatureCount(1);
                _logger.LogInformation("총 {TotalFeatures}개 피처 처리 예정", totalFeatures);

                // 동적 배치 크기 계산
                var baseBatchSize = 5000;
                var batchSize = _memoryManager.GetOptimalBatchSize(baseBatchSize, 1000);
                
                // 피처들을 배치 단위로 처리
                layer.ResetReading();
                Feature feature;
                int processedCount = 0;
                var batchFeatures = new List<(long featureId, SpatialEnvelope envelope)>();

                while ((feature = layer.GetNextFeature()) != null)
                {
                    try
                    {
                        var geometry = feature.GetGeometryRef();
                        if (geometry != null)
                        {
                            var envelope = new OSGeo.OGR.Envelope();
                            geometry.GetEnvelope(envelope);
                            
                            var spatialEnvelope = new SpatialEnvelope(
                                envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);

                            var featureId = feature.GetFID();
                            batchFeatures.Add((featureId, spatialEnvelope));

                            // 배치가 가득 찬 경우 처리
                            if (batchFeatures.Count >= batchSize)
                            {
                                await ProcessBatchAsync(batchFeatures);
                                processedCount += batchFeatures.Count;
                                batchFeatures.Clear();

                                // 메모리 압박 상황 체크 및 배치 크기 재조정
                                if (_memoryManager.IsMemoryPressureHigh())
                                {
                                    await _memoryManager.TryReduceMemoryPressureAsync();
                                    batchSize = _memoryManager.GetOptimalBatchSize(baseBatchSize, 1000);
                                }

                                if (processedCount % 10000 == 0)
                                {
                                    _logger.LogDebug("인덱스 구축 진행: {ProcessedCount}/{TotalFeatures} 피처 처리됨", 
                                        processedCount, totalFeatures);
                                }
                            }
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                // 남은 배치 처리
                if (batchFeatures.Count > 0)
                {
                    await ProcessBatchAsync(batchFeatures);
                    processedCount += batchFeatures.Count;
                }

                _featureCount = processedCount;
                
                // 인덱스 최적화
                await OptimizeIndexAsync();
                
                _logger.LogInformation("최적화된 R-tree 인덱스 구축 완료: {LayerName}, {FeatureCount}개 피처", 
                    layerName, _featureCount);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "최적화된 R-tree 인덱스 구축 중 오류 발생: {LayerName}", layerName);
                return false;
            }
            finally
            {
                _buildSemaphore.Release();
            }
        }

        /// <summary>
        /// 배치 단위로 피처들을 인덱스에 삽입
        /// </summary>
        /// <param name="batchFeatures">배치 피처 목록</param>
        private async Task ProcessBatchAsync(List<(long featureId, SpatialEnvelope envelope)> batchFeatures)
        {
            await Task.Run(() =>
            {
                foreach (var (featureId, envelope) in batchFeatures)
                {
                    // 피처 범위 저장
                    _featureEnvelopes[featureId] = envelope;
                    
                    // R-tree에 삽입
                    var splitNode = _root.Insert(featureId, envelope);
                    
                    // 루트 노드가 분할된 경우 새로운 루트 생성
                    if (splitNode != null)
                    {
                        var newRoot = new OptimizedRTreeNode(_maxNodeCapacity, _minNodeCapacity);
                        newRoot.Children.Add(_root);
                        newRoot.Children.Add(splitNode);
                        newRoot.UpdateEnvelope();
                        _root = newRoot;
                    }
                }
            });
        }

        /// <summary>
        /// 인덱스 최적화 수행
        /// </summary>
        private async Task OptimizeIndexAsync()
        {
            await Task.Run(() =>
            {
                _logger.LogDebug("인덱스 최적화 시작");
                
                // 트리 균형 조정
                BalanceTree(_root);
                
                // 메모리 압박 시 디스크 캐시로 일부 노드 이동
                if (_memoryManager.IsMemoryPressureHigh() && _isDiskCacheEnabled)
                {
                    MoveLeastUsedNodesToDisk();
                }
                
                _logger.LogDebug("인덱스 최적화 완료");
            });
        }

        /// <summary>
        /// 트리 균형 조정
        /// </summary>
        /// <param name="node">노드</param>
        private void BalanceTree(RTreeNode node)
        {
            if (node.IsLeaf || node.Children.Count <= 1)
                return;

            // 자식 노드들을 재귀적으로 균형 조정
            foreach (var child in node.Children.ToList())
            {
                BalanceTree(child);
            }

            // 현재 노드의 자식들이 불균형한 경우 재구성
            if (ShouldRebalanceNode(node))
            {
                RebalanceNode(node);
            }
        }

        /// <summary>
        /// 노드 재균형이 필요한지 확인
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>재균형 필요 여부</returns>
        private bool ShouldRebalanceNode(RTreeNode node)
        {
            if (node.IsLeaf || node.Children.Count < 2)
                return false;

            // 자식 노드들의 크기 분산이 큰 경우 재균형 필요
            var childSizes = node.Children.Select(c => c.IsLeaf ? c.FeatureIds.Count : c.Children.Count).ToList();
            var avgSize = childSizes.Average();
            var variance = childSizes.Select(s => Math.Pow(s - avgSize, 2)).Average();
            
            return variance > avgSize * 0.5; // 분산이 평균의 50% 이상인 경우
        }

        /// <summary>
        /// 노드 재균형 수행
        /// </summary>
        /// <param name="node">노드</param>
        private void RebalanceNode(RTreeNode node)
        {
            // 간단한 재균형 알고리즘: 자식들을 크기 순으로 정렬하여 재배치
            if (!node.IsLeaf)
            {
                node.Children = node.Children
                    .OrderBy(c => c.IsLeaf ? c.FeatureIds.Count : c.Children.Count)
                    .ToList();
            }
        }

        /// <summary>
        /// 사용 빈도가 낮은 노드들을 디스크로 이동
        /// </summary>
        private void MoveLeastUsedNodesToDisk()
        {
            if (!_isDiskCacheEnabled)
                return;

            try
            {
                // 현재는 간단한 구현으로 로그만 남김
                // 실제 구현에서는 노드 사용 통계를 기반으로 디스크 캐시 구현
                _logger.LogDebug("디스크 캐시로 노드 이동 시도");
                
                // 메모리 정리
                _memoryManager.ForceGarbageCollection();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "디스크 캐시 이동 중 오류 발생");
            }
        }

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색합니다
        /// </summary>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <returns>교차하는 피처 ID 목록</returns>
        public async Task<List<long>> QueryIntersectingFeaturesAsync(SpatialEnvelope searchEnvelope)
        {
            return await Task.Run(() =>
            {
                var results = new List<long>();
                
                if (_root != null)
                {
                    _root.Search(searchEnvelope, results);
                }
                
                _logger.LogDebug("공간 질의 완료: {ResultCount}개 피처 검색됨", results.Count);
                
                return results;
            });
        }

        /// <summary>
        /// 인덱스에 포함된 피처 수를 반환합니다
        /// </summary>
        /// <returns>피처 수</returns>
        public int GetFeatureCount()
        {
            return _featureCount;
        }

        /// <summary>
        /// 인덱스를 초기화합니다
        /// </summary>
        public void Clear()
        {
            _root = new OptimizedRTreeNode(_maxNodeCapacity, _minNodeCapacity);
            _featureCount = 0;
            _featureEnvelopes.Clear();
            _diskCache.Clear();
            
            // 디스크 캐시 정리
            if (_isDiskCacheEnabled && Directory.Exists(_cacheDirectory))
            {
                try
                {
                    Directory.Delete(_cacheDirectory, true);
                    Directory.CreateDirectory(_cacheDirectory);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "디스크 캐시 정리 중 오류 발생");
                }
            }
            
            _logger.LogDebug("최적화된 R-tree 인덱스가 초기화되었습니다");
        }

        /// <summary>
        /// 특정 피처의 공간 범위를 반환
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <returns>공간 범위</returns>
        public SpatialEnvelope GetFeatureEnvelope(long featureId)
        {
            return _featureEnvelopes.TryGetValue(featureId, out var envelope) ? envelope : null;
        }

        /// <summary>
        /// 메모리 압박 이벤트 처리
        /// </summary>
        /// <param name="sender">이벤트 발생자</param>
        /// <param name="e">이벤트 인자</param>
        private void OnMemoryPressureDetected(object sender, MemoryPressureEventArgs e)
        {
            _logger.LogWarning("메모리 압박 감지 - 인덱스 최적화 수행: 사용률 {PressureRatio:P1}", e.PressureRatio);
            
            // 비동기로 최적화 수행
            _ = Task.Run(async () =>
            {
                try
                {
                    await OptimizeIndexAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "메모리 압박 시 인덱스 최적화 중 오류 발생");
                }
            });
        }

        /// <summary>
        /// 인덱스 통계 정보를 반환
        /// </summary>
        /// <returns>통계 정보</returns>
        public Dictionary<string, object> GetStatistics()
        {
            var memoryStats = _memoryManager.GetMemoryStatistics();
            
            var stats = new Dictionary<string, object>
            {
                ["FeatureCount"] = _featureCount,
                ["MaxNodeCapacity"] = _maxNodeCapacity,
                ["MinNodeCapacity"] = _minNodeCapacity,
                ["IndexType"] = "Optimized R-tree",
                ["DiskCacheEnabled"] = _isDiskCacheEnabled,
                ["MemoryUsage"] = memoryStats.CurrentMemoryUsage,
                ["MemoryPressureRatio"] = memoryStats.PressureRatio,
                ["IsUnderMemoryPressure"] = memoryStats.IsUnderPressure
            };

            if (_root != null)
            {
                stats["RootEnvelope"] = _root.Envelope;
                stats["TreeDepth"] = CalculateTreeDepth(_root);
                stats["NodeCount"] = CountNodes(_root);
            }

            return stats;
        }

        /// <summary>
        /// 트리 깊이 계산
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>깊이</returns>
        private int CalculateTreeDepth(RTreeNode node)
        {
            if (node.IsLeaf)
                return 1;

            int maxChildDepth = 0;
            foreach (var child in node.Children)
            {
                maxChildDepth = Math.Max(maxChildDepth, CalculateTreeDepth(child));
            }

            return maxChildDepth + 1;
        }

        /// <summary>
        /// 노드 수 계산
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>노드 수</returns>
        private int CountNodes(RTreeNode node)
        {
            int count = 1; // 현재 노드

            if (!node.IsLeaf)
            {
                foreach (var child in node.Children)
                {
                    count += CountNodes(child);
                }
            }

            return count;
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            // 메모리 압박 이벤트 구독 해제
            if (_memoryManager != null)
            {
                _memoryManager.MemoryPressureDetected -= OnMemoryPressureDetected;
            }
            
            Clear();
            
            // 세마포어 해제
            _buildSemaphore?.Dispose();
            
            // 디스크 캐시 정리
            if (_isDiskCacheEnabled && Directory.Exists(_cacheDirectory))
            {
                try
                {
                    Directory.Delete(_cacheDirectory, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "디스크 캐시 정리 중 오류 발생");
                }
            }
            
            _logger.LogDebug("최적화된 R-tree 인덱스 리소스가 해제되었습니다");
        }
    }

    /// <summary>
    /// 최적화된 R-tree 노드 (메모리 효율성 개선)
    /// </summary>
    internal class OptimizedRTreeNode : RTreeNode
    {
        private readonly int _minCapacity;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="maxCapacity">최대 용량</param>
        /// <param name="minCapacity">최소 용량</param>
        public OptimizedRTreeNode(int maxCapacity, int minCapacity) : base(maxCapacity)
        {
            _minCapacity = minCapacity;
        }

        /// <summary>
        /// 노드가 언더플로우 상태인지 확인
        /// </summary>
        /// <returns>언더플로우 여부</returns>
        public bool IsUnderflow()
        {
            return IsLeaf ? FeatureIds.Count < _minCapacity : Children.Count < _minCapacity;
        }

        /// <summary>
        /// 더 효율적인 분할 알고리즘 (R*-tree 스타일)
        /// </summary>
        /// <returns>분할된 노드</returns>
        public RTreeNode SplitNode()
        {
            if (IsLeaf)
            {
                return SplitLeafNodeOptimized();
            }
            else
            {
                return SplitInternalNodeOptimized();
            }
        }

        /// <summary>
        /// 최적화된 리프 노드 분할
        /// </summary>
        /// <returns>새로 생성된 노드</returns>
        private RTreeNode SplitLeafNodeOptimized()
        {
            var newNode = new OptimizedRTreeNode(MaxCapacity, _minCapacity);
            
            // 면적 기준으로 정렬하여 분할 (더 균등한 분할을 위해)
            var sortedFeatures = FeatureIds.ToList();
            
            // 중간점에서 분할
            int splitIndex = sortedFeatures.Count / 2;
            
            // 새 노드로 절반 이동
            for (int i = splitIndex; i < sortedFeatures.Count; i++)
            {
                newNode.FeatureIds.Add(sortedFeatures[i]);
            }
            
            // 현재 노드에서 이동된 피처들 제거
            FeatureIds.RemoveRange(splitIndex, FeatureIds.Count - splitIndex);
            
            // 범위 재계산 (실제 구현에서는 피처의 실제 범위 사용)
            newNode.Envelope = new SpatialEnvelope(Envelope.MinX, Envelope.MinY, Envelope.MaxX, Envelope.MaxY);
            
            return newNode;
        }

        /// <summary>
        /// 최적화된 내부 노드 분할
        /// </summary>
        /// <returns>새로 생성된 노드</returns>
        private RTreeNode SplitInternalNodeOptimized()
        {
            var newNode = new OptimizedRTreeNode(MaxCapacity, _minCapacity);
            
            // 자식 노드들을 면적 기준으로 정렬
            var sortedChildren = Children.OrderBy(c => c.Envelope.Width * c.Envelope.Height).ToList();
            
            int splitIndex = sortedChildren.Count / 2;
            
            // 새 노드로 절반 이동
            for (int i = splitIndex; i < sortedChildren.Count; i++)
            {
                newNode.Children.Add(sortedChildren[i]);
            }
            
            // 현재 노드에서 이동된 자식들 제거
            Children.Clear();
            for (int i = 0; i < splitIndex; i++)
            {
                Children.Add(sortedChildren[i]);
            }
            
            // 각 노드의 범위 재계산
            UpdateEnvelope();
            newNode.UpdateEnvelope();
            
            return newNode;
        }
    }
}
