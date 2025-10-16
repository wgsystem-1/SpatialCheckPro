namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 위상 규칙 타입
    /// </summary>
    public enum TopologyRuleType
    {
        /// <summary>
        /// 겹치면 안됨 (Must Not Overlap)
        /// </summary>
        MustNotOverlap,

        /// <summary>
        /// 틈이 있으면 안됨 (Must Not Have Gaps)
        /// </summary>
        MustNotHaveGaps,

        /// <summary>
        /// 다른 레이어에 의해 덮여야 함 (Must Be Covered By)
        /// </summary>
        MustBeCoveredBy,

        /// <summary>
        /// 다른 레이어를 덮어야 함 (Must Cover)
        /// </summary>
        MustCover,

        /// <summary>
        /// 교차하면 안됨 (Must Not Intersect)
        /// </summary>
        MustNotIntersect,

        /// <summary>
        /// 적절히 내부에 있어야 함 (Must Be Properly Inside)
        /// </summary>
        MustBeProperlyInside,

        /// <summary>
        /// 자체 겹침 금지 (Must Not Self Overlap)
        /// </summary>
        MustNotSelfOverlap,

        /// <summary>
        /// 자체 교차 금지 (Must Not Self Intersect)
        /// </summary>
        MustNotSelfIntersect
    }
}
