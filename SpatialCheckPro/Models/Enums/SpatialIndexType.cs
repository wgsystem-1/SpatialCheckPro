namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 공간 인덱스 타입
    /// </summary>
    public enum SpatialIndexType
    {
        /// <summary>
        /// R-tree 인덱스
        /// </summary>
        RTree,

        /// <summary>
        /// Quad-tree 인덱스
        /// </summary>
        QuadTree,

        /// <summary>
        /// 격자 인덱스 (Grid Index)
        /// </summary>
        GridIndex,

        /// <summary>
        /// 해시 인덱스 (Hash Index)
        /// </summary>
        HashIndex
    }
}
