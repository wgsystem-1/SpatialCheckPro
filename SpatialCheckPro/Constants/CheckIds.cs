namespace SpatialCheckPro.Constants
{
    /// <summary>
    /// 검수 단계 및 검사 항목 ID 상수 정의
    /// </summary>
    public static class CheckIds
    {
        /// <summary>
        /// 0단계: FileGDB 완전성 검수
        /// </summary>
        public const string FileGdbIntegrity = "FILEGDB_INTEGRITY_CHECK";

        /// <summary>
        /// 1단계: 테이블 리스트 검증
        /// </summary>
        public const string TableList = "TABLE_LIST_CHECK";

        /// <summary>
        /// 1단계: 테이블 구조 검증
        /// </summary>
        public const string TableStructure = "TABLE_STRUCTURE_CHECK";

        /// <summary>
        /// 2단계: 스키마 검증
        /// </summary>
        public const string SchemaValidation = "SCHEMA_VALIDATION_CHECK";

        /// <summary>
        /// 2단계: 컬럼 구조 검증
        /// </summary>
        public const string ColumnStructure = "COLUMN_STRUCTURE_CHECK";

        /// <summary>
        /// 3단계: 지오메트리 검증
        /// </summary>
        public const string GeometryValidation = "GEOMETRY_VALIDATION_CHECK";

        /// <summary>
        /// 3단계: 지오메트리 유효성 검사
        /// </summary>
        public const string GeometryValidity = "GEOMETRY_VALIDITY_CHECK";

        /// <summary>
        /// 4단계: 관계 검증
        /// </summary>
        public const string RelationValidation = "RELATION_VALIDATION_CHECK";

        /// <summary>
        /// 4단계: 공간 관계 검증
        /// </summary>
        public const string SpatialRelation = "SPATIAL_RELATION_CHECK";

        /// <summary>
        /// 4단계: 외래키 검증
        /// </summary>
        public const string ForeignKey = "FOREIGN_KEY_CHECK";

        /// <summary>
        /// 4단계: 고유키 검증
        /// </summary>
        public const string UniqueKey = "UNIQUE_KEY_CHECK";

        /// <summary>
        /// 5단계: 속성 관계 검증
        /// </summary>
        public const string AttributeRelation = "ATTRIBUTE_RELATION_CHECK";

        /// <summary>
        /// 5단계: 속성값 검증
        /// </summary>
        public const string AttributeValue = "ATTRIBUTE_VALUE_CHECK";
    }
}
