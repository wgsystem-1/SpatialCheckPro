namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 오류 유형
    /// </summary>
    public enum ErrorType
    {
        /// <summary>
        /// 테이블 오류
        /// </summary>
        Table,

        /// <summary>
        /// 스키마 오류
        /// </summary>
        Schema,

        /// <summary>
        /// 지오메트리 오류
        /// </summary>
        Geometry,

        /// <summary>
        /// 관계 오류
        /// </summary>
        Relation,

        /// <summary>
        /// 데이터 오류
        /// </summary>
        Data,

        /// <summary>
        /// 시스템 오류
        /// </summary>
        System,

        /// <summary>
        /// 스키마 오류 (별칭)
        /// </summary>
        SchemaError,

        /// <summary>
        /// 지오메트리 오류 (별칭)
        /// </summary>
        GeometryError,

        /// <summary>
        /// 관계 오류 (별칭)
        /// </summary>
        RelationError,

        /// <summary>
        /// 일반 오류
        /// </summary>
        GeneralError
    }
}
