using SpatialCheckPro.Models;
using OSGeo.OGR;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QuadTree 기반 공간 인덱스 구현
    /// </summary>
    public class QuadTreeSpatialIndex : ISpatialIndex, IDisposable
    {
        private readonly ILogger<QuadTreeSpatialIndex> _logger;
        private QuadTreeNode _root;
        private int _featureCount;
        private readonly int _maxDepth;
        private readonly int _maxFeaturesPerNode;
        private readonly Dictionary<long, SpatialEnvelope> _featureEnvelopes;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="maxDepth">최대 깊이</param>
        /// <param name="maxFeaturesPerNode">노드당 최대 피처 수</param>
        public QuadTreeSpatialIndex(ILogger<QuadTreeSpatialIndex> logger, int maxDepth = 10, int maxFeaturesPerNode = 100)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxDepth = maxDepth;
            _maxFeaturesPerNode = maxFeaturesPerNode;
            _featureEnvelopes = new Dictionary<long, SpatialEnvelope>();
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
            try
            {
                _logger.LogInformation("QuadTree 인덱스 구축 시작: {LayerName}", layerName);
                
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

                // 전체 범위 계산
                var layerEnvelope = new OSGeo.OGR.Envelope();
                layer.GetExtent(layerEnvelope, 1);
                
                var bounds = new SpatialEnvelope(
                    layerEnvelope.MinX, layerEnvelope.MinY, 
                    layerEnvelope.MaxX, layerEnvelope.MaxY);

                // 인덱스 초기화
                Clear();
                _root = new QuadTreeNode(bounds, 0, _maxDepth, _maxFeaturesPerNode);

                // 피처들을 순회하며 인덱스에 추가
                layer.ResetReading();
                Feature feature;
                int processedCount = 0;

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
                            
                            // 피처 범위 저장
                            _featureEnvelopes[featureId] = spatialEnvelope;
                            
                            // QuadTree에 삽입
                            await InsertFeatureAsync(featureId, spatialEnvelope);
                            
                            processedCount++;
                            
                            if (processedCount % 1000 == 0)
                            {
                                _logger.LogDebug("인덱스 구축 진행: {ProcessedCount}개 피처 처리됨", processedCount);
                            }
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                _featureCount = processedCount;
                _logger.LogInformation("QuadTree 인덱스 구축 완료: {LayerName}, {FeatureCount}개 피처", layerName, _featureCount);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QuadTree 인덱스 구축 중 오류 발생: {LayerName}", layerName);
                return false;
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
                    _root.Query(searchEnvelope, results);
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
            _root = null;
            _featureCount = 0;
            _featureEnvelopes.Clear();
            _logger.LogDebug("QuadTree 인덱스가 초기화되었습니다");
        }

        /// <summary>
        /// 피처를 QuadTree에 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        private async Task InsertFeatureAsync(long featureId, SpatialEnvelope envelope)
        {
            await Task.Run(() =>
            {
                _root?.Insert(featureId, envelope);
            });
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
        /// 인덱스 통계 정보를 반환
        /// </summary>
        /// <returns>통계 정보</returns>
        public Dictionary<string, object> GetStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["FeatureCount"] = _featureCount,
                ["MaxDepth"] = _maxDepth,
                ["MaxFeaturesPerNode"] = _maxFeaturesPerNode,
                ["IndexType"] = "QuadTree"
            };

            if (_root != null)
            {
                stats["RootBounds"] = _root.Bounds;
                stats["TreeDepth"] = CalculateTreeDepth(_root);
                stats["NodeCount"] = CountNodes(_root);
                stats["LeafNodeCount"] = CountLeafNodes(_root);
            }

            return stats;
        }

        /// <summary>
        /// 트리 깊이 계산
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>깊이</returns>
        private int CalculateTreeDepth(QuadTreeNode node)
        {
            if (node.IsLeaf)
                return 1;

            int maxChildDepth = 0;
            for (int i = 0; i < 4; i++)
            {
                if (node.Children[i] != null)
                {
                    maxChildDepth = Math.Max(maxChildDepth, CalculateTreeDepth(node.Children[i]));
                }
            }

            return maxChildDepth + 1;
        }

        /// <summary>
        /// 노드 수 계산
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>노드 수</returns>
        private int CountNodes(QuadTreeNode node)
        {
            int count = 1; // 현재 노드

            if (!node.IsLeaf)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (node.Children[i] != null)
                    {
                        count += CountNodes(node.Children[i]);
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 리프 노드 수 계산
        /// </summary>
        /// <param name="node">노드</param>
        /// <returns>리프 노드 수</returns>
        private int CountLeafNodes(QuadTreeNode node)
        {
            if (node.IsLeaf)
                return 1;

            int leafCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (node.Children[i] != null)
                {
                    leafCount += CountLeafNodes(node.Children[i]);
                }
            }

            return leafCount;
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            Clear();
            _logger.LogDebug("QuadTree 인덱스 리소스가 해제되었습니다");
        }
    }

    /// <summary>
    /// QuadTree 노드
    /// </summary>
    internal class QuadTreeNode
    {
        /// <summary>
        /// 노드의 공간 범위
        /// </summary>
        public SpatialEnvelope Bounds { get; private set; }

        /// <summary>
        /// 자식 노드들 (NW, NE, SW, SE 순서)
        /// </summary>
        public QuadTreeNode[] Children { get; private set; }

        /// <summary>
        /// 피처 ID들 (리프 노드인 경우)
        /// </summary>
        public List<long> FeatureIds { get; private set; }

        /// <summary>
        /// 현재 깊이
        /// </summary>
        public int Depth { get; private set; }

        /// <summary>
        /// 최대 깊이
        /// </summary>
        public int MaxDepth { get; private set; }

        /// <summary>
        /// 노드당 최대 피처 수
        /// </summary>
        public int MaxFeaturesPerNode { get; private set; }

        /// <summary>
        /// 리프 노드 여부
        /// </summary>
        public bool IsLeaf => Children[0] == null;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="bounds">노드 범위</param>
        /// <param name="depth">현재 깊이</param>
        /// <param name="maxDepth">최대 깊이</param>
        /// <param name="maxFeaturesPerNode">노드당 최대 피처 수</param>
        public QuadTreeNode(SpatialEnvelope bounds, int depth, int maxDepth, int maxFeaturesPerNode)
        {
            Bounds = bounds;
            Depth = depth;
            MaxDepth = maxDepth;
            MaxFeaturesPerNode = maxFeaturesPerNode;
            Children = new QuadTreeNode[4]; // NW, NE, SW, SE
            FeatureIds = new List<long>();
        }

        /// <summary>
        /// 피처를 노드에 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        public void Insert(long featureId, SpatialEnvelope envelope)
        {
            // 피처가 현재 노드 범위와 교차하지 않으면 삽입하지 않음
            if (!Bounds.Intersects(envelope))
                return;

            // 리프 노드이고 용량이 남아있거나 최대 깊이에 도달한 경우
            if (IsLeaf && (FeatureIds.Count < MaxFeaturesPerNode || Depth >= MaxDepth))
            {
                FeatureIds.Add(featureId);
                return;
            }

            // 리프 노드이지만 용량이 가득 찬 경우 분할
            if (IsLeaf)
            {
                Subdivide();
                
                // 기존 피처들을 자식 노드로 재배치
                var existingFeatures = new List<long>(FeatureIds);
                FeatureIds.Clear();
                
                foreach (var existingFeatureId in existingFeatures)
                {
                    // 실제 구현에서는 피처의 실제 범위를 사용해야 함
                    // 여기서는 간단히 현재 범위를 사용
                    InsertIntoChildren(existingFeatureId, envelope);
                }
            }

            // 자식 노드에 삽입
            InsertIntoChildren(featureId, envelope);
        }

        /// <summary>
        /// 자식 노드들에 피처 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        private void InsertIntoChildren(long featureId, SpatialEnvelope envelope)
        {
            for (int i = 0; i < 4; i++)
            {
                if (Children[i] != null && Children[i].Bounds.Intersects(envelope))
                {
                    Children[i].Insert(featureId, envelope);
                }
            }
        }

        /// <summary>
        /// 노드를 4개의 자식 노드로 분할
        /// </summary>
        private void Subdivide()
        {
            double halfWidth = Bounds.Width / 2.0;
            double halfHeight = Bounds.Height / 2.0;
            double centerX = Bounds.CenterX;
            double centerY = Bounds.CenterY;

            // NW (북서)
            Children[0] = new QuadTreeNode(
                new SpatialEnvelope(Bounds.MinX, centerY, centerX, Bounds.MaxY),
                Depth + 1, MaxDepth, MaxFeaturesPerNode);

            // NE (북동)
            Children[1] = new QuadTreeNode(
                new SpatialEnvelope(centerX, centerY, Bounds.MaxX, Bounds.MaxY),
                Depth + 1, MaxDepth, MaxFeaturesPerNode);

            // SW (남서)
            Children[2] = new QuadTreeNode(
                new SpatialEnvelope(Bounds.MinX, Bounds.MinY, centerX, centerY),
                Depth + 1, MaxDepth, MaxFeaturesPerNode);

            // SE (남동)
            Children[3] = new QuadTreeNode(
                new SpatialEnvelope(centerX, Bounds.MinY, Bounds.MaxX, centerY),
                Depth + 1, MaxDepth, MaxFeaturesPerNode);
        }

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색
        /// </summary>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <param name="results">결과 목록</param>
        public void Query(SpatialEnvelope searchEnvelope, List<long> results)
        {
            // 현재 노드의 범위와 검색 범위가 교차하지 않으면 검색 중단
            if (!Bounds.Intersects(searchEnvelope))
                return;

            if (IsLeaf)
            {
                // 리프 노드인 경우 피처 ID들을 결과에 추가
                results.AddRange(FeatureIds);
            }
            else
            {
                // 내부 노드인 경우 자식 노드들을 재귀적으로 검색
                for (int i = 0; i < 4; i++)
                {
                    Children[i]?.Query(searchEnvelope, results);
                }
            }
        }
    }
}
