using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// R-tree 노드를 나타내는 클래스
    /// </summary>
    internal class RTreeNode
    {
        /// <summary>
        /// 노드의 공간 범위
        /// </summary>
        public SpatialEnvelope Envelope { get; set; }

        /// <summary>
        /// 자식 노드들 (내부 노드인 경우)
        /// </summary>
        public List<RTreeNode> Children { get; set; }

        /// <summary>
        /// 피처 ID들 (리프 노드인 경우)
        /// </summary>
        public List<long> FeatureIds { get; set; }

        /// <summary>
        /// 리프 노드 여부
        /// </summary>
        public bool IsLeaf => Children == null || Children.Count == 0;

        /// <summary>
        /// 최대 자식 수 (노드 용량)
        /// </summary>
        public int MaxCapacity { get; set; }

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public RTreeNode(int maxCapacity = 16)
        {
            MaxCapacity = maxCapacity;
            Children = new List<RTreeNode>();
            FeatureIds = new List<long>();
            Envelope = new SpatialEnvelope(0, 0, 0, 0);
        }

        /// <summary>
        /// 노드가 가득 찼는지 확인
        /// </summary>
        /// <returns>가득 참 여부</returns>
        public bool IsFull()
        {
            return IsLeaf ? FeatureIds.Count >= MaxCapacity : Children.Count >= MaxCapacity;
        }

        /// <summary>
        /// 노드의 범위를 업데이트
        /// </summary>
        public void UpdateEnvelope()
        {
            if (IsLeaf)
            {
                // 리프 노드의 경우 피처들의 범위는 이미 설정되어 있음
                return;
            }

            if (Children.Count == 0)
            {
                Envelope = new SpatialEnvelope(0, 0, 0, 0);
                return;
            }

            // 자식 노드들의 범위를 합쳐서 현재 노드의 범위 계산
            var firstChild = Children[0];
            Envelope = new SpatialEnvelope(
                firstChild.Envelope.MinX,
                firstChild.Envelope.MinY,
                firstChild.Envelope.MaxX,
                firstChild.Envelope.MaxY);

            for (int i = 1; i < Children.Count; i++)
            {
                var child = Children[i];
                Envelope.MinX = Math.Min(Envelope.MinX, child.Envelope.MinX);
                Envelope.MinY = Math.Min(Envelope.MinY, child.Envelope.MinY);
                Envelope.MaxX = Math.Max(Envelope.MaxX, child.Envelope.MaxX);
                Envelope.MaxY = Math.Max(Envelope.MaxY, child.Envelope.MaxY);
            }
        }

        /// <summary>
        /// 지정된 범위와 교차하는 피처 ID들을 검색
        /// </summary>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <param name="results">결과 목록</param>
        public void Search(SpatialEnvelope searchEnvelope, List<long> results)
        {
            // 현재 노드의 범위와 검색 범위가 교차하지 않으면 검색 중단
            if (!Envelope.Intersects(searchEnvelope))
                return;

            if (IsLeaf)
            {
                // 리프 노드인 경우 피처 ID들을 결과에 추가
                results.AddRange(FeatureIds);
            }
            else
            {
                // 내부 노드인 경우 자식 노드들을 재귀적으로 검색
                foreach (var child in Children)
                {
                    child.Search(searchEnvelope, results);
                }
            }
        }

        /// <summary>
        /// 새로운 피처를 노드에 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        /// <returns>분할된 노드 (분할이 발생한 경우)</returns>
        public RTreeNode Insert(long featureId, SpatialEnvelope envelope)
        {
            if (IsLeaf)
            {
                // 리프 노드에 피처 추가
                FeatureIds.Add(featureId);
                
                // 노드의 범위 확장
                if (FeatureIds.Count == 1)
                {
                    Envelope = new SpatialEnvelope(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
                }
                else
                {
                    Envelope.MinX = Math.Min(Envelope.MinX, envelope.MinX);
                    Envelope.MinY = Math.Min(Envelope.MinY, envelope.MinY);
                    Envelope.MaxX = Math.Max(Envelope.MaxX, envelope.MaxX);
                    Envelope.MaxY = Math.Max(Envelope.MaxY, envelope.MaxY);
                }

                // 노드가 가득 찬 경우 분할
                if (IsFull())
                {
                    return SplitLeafNode();
                }
            }
            else
            {
                // 내부 노드인 경우 가장 적합한 자식 노드 선택
                var bestChild = ChooseBestChild(envelope);
                var splitNode = bestChild.Insert(featureId, envelope);

                // 자식 노드가 분할된 경우 새 노드를 추가
                if (splitNode != null)
                {
                    Children.Add(splitNode);
                    
                    // 현재 노드가 가득 찬 경우 분할
                    if (IsFull())
                    {
                        return SplitInternalNode();
                    }
                }

                // 범위 업데이트
                UpdateEnvelope();
            }

            return null;
        }

        /// <summary>
        /// 삽입할 피처에 가장 적합한 자식 노드 선택
        /// </summary>
        /// <param name="envelope">피처의 공간 범위</param>
        /// <returns>선택된 자식 노드</returns>
        private RTreeNode ChooseBestChild(SpatialEnvelope envelope)
        {
            RTreeNode bestChild = Children[0];
            double minEnlargement = CalculateEnlargement(bestChild.Envelope, envelope);

            for (int i = 1; i < Children.Count; i++)
            {
                double enlargement = CalculateEnlargement(Children[i].Envelope, envelope);
                if (enlargement < minEnlargement)
                {
                    minEnlargement = enlargement;
                    bestChild = Children[i];
                }
            }

            return bestChild;
        }

        /// <summary>
        /// 범위 확장 크기 계산
        /// </summary>
        /// <param name="original">원본 범위</param>
        /// <param name="toAdd">추가할 범위</param>
        /// <returns>확장 크기</returns>
        private double CalculateEnlargement(SpatialEnvelope original, SpatialEnvelope toAdd)
        {
            double originalArea = original.Width * original.Height;
            
            double newMinX = Math.Min(original.MinX, toAdd.MinX);
            double newMinY = Math.Min(original.MinY, toAdd.MinY);
            double newMaxX = Math.Max(original.MaxX, toAdd.MaxX);
            double newMaxY = Math.Max(original.MaxY, toAdd.MaxY);
            
            double newArea = (newMaxX - newMinX) * (newMaxY - newMinY);
            
            return newArea - originalArea;
        }

        /// <summary>
        /// 리프 노드 분할
        /// </summary>
        /// <returns>새로 생성된 노드</returns>
        private RTreeNode SplitLeafNode()
        {
            var newNode = new RTreeNode(MaxCapacity);
            
            // 간단한 분할 알고리즘: 절반씩 나누기
            int splitIndex = FeatureIds.Count / 2;
            
            // 새 노드로 절반 이동
            for (int i = splitIndex; i < FeatureIds.Count; i++)
            {
                newNode.FeatureIds.Add(FeatureIds[i]);
            }
            
            // 현재 노드에서 이동된 피처들 제거
            FeatureIds.RemoveRange(splitIndex, FeatureIds.Count - splitIndex);
            
            // 각 노드의 범위 재계산 (실제 구현에서는 피처의 실제 범위를 사용해야 함)
            // 여기서는 간단히 현재 범위를 유지
            newNode.Envelope = new SpatialEnvelope(Envelope.MinX, Envelope.MinY, Envelope.MaxX, Envelope.MaxY);
            
            return newNode;
        }

        /// <summary>
        /// 내부 노드 분할
        /// </summary>
        /// <returns>새로 생성된 노드</returns>
        private RTreeNode SplitInternalNode()
        {
            var newNode = new RTreeNode(MaxCapacity);
            
            // 간단한 분할 알고리즘: 절반씩 나누기
            int splitIndex = Children.Count / 2;
            
            // 새 노드로 절반 이동
            for (int i = splitIndex; i < Children.Count; i++)
            {
                newNode.Children.Add(Children[i]);
            }
            
            // 현재 노드에서 이동된 자식들 제거
            Children.RemoveRange(splitIndex, Children.Count - splitIndex);
            
            // 각 노드의 범위 재계산
            UpdateEnvelope();
            newNode.UpdateEnvelope();
            
            return newNode;
        }
    }
}
