using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 테이블 정보를 나타내는 모델 클래스
    /// </summary>
    public class TableInfo
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 레코드 수
        /// </summary>
        public int RecordCount { get; set; }

        /// <summary>
        /// 피처 수 (RecordCount와 동일)
        /// </summary>
        public int FeatureCount => RecordCount;

        /// <summary>
        /// 컬럼 정보 목록
        /// </summary>
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
    }

    /// <summary>
    /// 컬럼 정보를 나타내는 모델 클래스
    /// </summary>
    public class ColumnInfo
    {
        /// <summary>
        /// 컬럼명
        /// </summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 타입
        /// </summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>
        /// 길이
        /// </summary>
        public int? Length { get; set; }

        /// <summary>
        /// Null 허용 여부
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// Primary Key 여부
        /// </summary>
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// Foreign Key 여부
        /// </summary>
        public bool IsForeignKey { get; set; }
    }
}
