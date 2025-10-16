namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 공간 관계 타입
    /// </summary>
    public enum SpatialRelationType
    {
        /// <summary>
        /// 포함 관계 (Contains)
        /// </summary>
        Contains,

        /// <summary>
        /// 내부에 위치 (Within)
        /// </summary>
        Within,

        /// <summary>
        /// 교차 관계 (Intersects)
        /// </summary>
        Intersects,

        /// <summary>
        /// 접촉 관계 (Touches)
        /// </summary>
        Touches,

        /// <summary>
        /// 겹침 관계 (Overlaps)
        /// </summary>
        Overlaps,

        /// <summary>
        /// 분리 관계 (Disjoint)
        /// </summary>
        Disjoint,

        /// <summary>
        /// 횡단 관계 (Crosses)
        /// </summary>
        Crosses,

        /// <summary>
        /// 동일 관계 (Equals)
        /// </summary>
        Equals
    }
}
