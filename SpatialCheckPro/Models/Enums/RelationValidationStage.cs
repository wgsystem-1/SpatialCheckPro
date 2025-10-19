namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 관계 검수 단계
    /// </summary>
    public enum RelationValidationStage
    {
        /// <summary>
        /// 초기화
        /// </summary>
        Initialization = 0,

        /// <summary>
        /// 공간 관계 검수
        /// </summary>
        SpatialRelationValidation = 1,

        /// <summary>
        /// 속성 관계 검수
        /// </summary>
        AttributeRelationValidation = 2,

        /// <summary>
        /// 교차 테이블 관계 검수
        /// </summary>
        CrossTableRelationValidation = 3,

        /// <summary>
        /// 결과 통합
        /// </summary>
        ResultIntegration = 4,

        /// <summary>
        /// 완료
        /// </summary>
        Completed = 5
    }
}
