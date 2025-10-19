using SpatialCheckPro.Models;
using OSGeo.OGR;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// R-tree 기반 공간 인덱스 구현
    /// </summary>
    public class RTreeSpatialIndex : ISpatialIndex
    {
        private readonly ILogger<RTreeSpatialIndex> _logger;
        private RTreeNode _root;
        private int _featureCount;
        private readonly int _maxNodeCapacity;
        private readonly Dictionary<long, SpatialEnvelope> _featureEnvelopes;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="maxNodeCapacity">노드 최대 용량</param>
        public RTreeSpatialIndex(ILogger<RTreeSpatialIndex> logger, int maxNodeCapacity = 16)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxNodeCapacity = maxNodeCapacity;
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
                _logger.LogInformation("R-tree 인덱스 구축 시작: {LayerName}", layerName);
                
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
                                envelope.MinX, // MinX
                                envelope.MinY, // MinY  
                                envelope.MaxX, // MaxX
                                envelope.MaxY  // MaxY
                            );

                            var featureId = feature.GetFID();
                            
                            // 피처 범위 저장
                            _featureEnvelopes[featureId] = spatialEnvelope;
                            
                            // R-tree에 삽입
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
                _logger.LogInformation("R-tree 인덱스 구축 완료: {LayerName}, {FeatureCount}개 피처", layerName, _featureCount);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "R-tree 인덱스 구축 중 오류 발생: {LayerName}", layerName);
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
            _root = new RTreeNode(_maxNodeCapacity);
            _featureCount = 0;
            _featureEnvelopes.Clear();
            _logger.LogDebug("R-tree 인덱스가 초기화되었습니다");
        }

        /// <summary>
        /// 피처를 R-tree에 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        private async Task InsertFeatureAsync(long featureId, SpatialEnvelope envelope)
        {
            await Task.Run(() =>
            {
                var splitNode = _root.Insert(featureId, envelope);
                
                // 루트 노드가 분할된 경우 새로운 루트 생성
                if (splitNode != null)
                {
                    var newRoot = new RTreeNode(_maxNodeCapacity);
                    newRoot.Children.Add(_root);
                    newRoot.Children.Add(splitNode);
                    newRoot.UpdateEnvelope();
                    _root = newRoot;
                }
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
                ["MaxNodeCapacity"] = _maxNodeCapacity,
                ["IndexType"] = "R-tree"
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
            Clear();
            _logger.LogDebug("R-tree 인덱스 리소스가 해제되었습니다");
        }
    }
}
