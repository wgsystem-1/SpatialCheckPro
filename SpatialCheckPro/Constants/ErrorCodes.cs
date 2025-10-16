namespace SpatialCheckPro.Constants
{
    /// <summary>
    /// 검수 오류 코드 상수 정의
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// File Geodatabase 관련 오류 코드
        /// </summary>
        public static class FileGdb
        {
            /// <summary>
            /// 경로가 존재하지 않거나 디렉터리가 아닙니다
            /// </summary>
            public const string PathNotFound = "GDB000";
            
            /// <summary>
            /// 폴더명이 .gdb로 끝나지 않습니다
            /// </summary>
            public const string InvalidExtension = "GDB001";
            
            /// <summary>
            /// .gdbtablx 인덱스 페어가 누락된 테이블
            /// </summary>
            public const string IndexPairMissing = "GDB010";
            
            /// <summary>
            /// 핵심 시스템 테이블이 누락되었습니다
            /// </summary>
            public const string CoreTablesMissing = "GDB020";
            
            /// <summary>
            /// OGR가 폴더를 FileGDB로 열지 못했습니다
            /// </summary>
            public const string CannotOpen = "GDB030";
            
            /// <summary>
            /// OpenFileGDB 드라이버가 아닌 다른 드라이버로 열렸습니다
            /// </summary>
            public const string WrongDriver = "GDB031";
        }

        /// <summary>
        /// 테이블 검수 관련 오류 코드
        /// </summary>
        public static class Table
        {
            /// <summary>
            /// 필수 테이블이 누락되었습니다
            /// </summary>
            public const string MissingTable = "TBL001";
            
            /// <summary>
            /// 테이블이 설정에 정의되지 않았습니다
            /// </summary>
            public const string UndefinedTable = "TBL002";
            
            /// <summary>
            /// 테이블을 열 수 없습니다
            /// </summary>
            public const string CannotOpenTable = "TBL003";
        }

        /// <summary>
        /// 스키마 검수 관련 오류 코드
        /// </summary>
        public static class Schema
        {
            /// <summary>
            /// 필수 컬럼이 누락되었습니다
            /// </summary>
            public const string MissingColumn = "SCH001";
            
            /// <summary>
            /// 컬럼 타입이 일치하지 않습니다
            /// </summary>
            public const string InvalidColumnType = "SCH002";
            
            /// <summary>
            /// 정의되지 않은 컬럼이 존재합니다
            /// </summary>
            public const string UndefinedColumn = "SCH003";
        }

        /// <summary>
        /// 지오메트리 검수 관련 오류 코드
        /// </summary>
        public static class Geometry
        {
            /// <summary>
            /// 지오메트리가 유효하지 않습니다
            /// </summary>
            public const string Invalid = "GEO001";
            
            /// <summary>
            /// 지오메트리가 비어있습니다
            /// </summary>
            public const string Empty = "GEO002";
            
            /// <summary>
            /// 자가교차가 발견되었습니다
            /// </summary>
            public const string SelfIntersection = "GEO003";
        }

        /// <summary>
        /// 관계 검수 관련 오류 코드
        /// </summary>
        public static class Relation
        {
            /// <summary>
            /// 공간 관계 오류
            /// </summary>
            public const string SpatialError = "REL001";
            
            /// <summary>
            /// 외래키 제약 위반
            /// </summary>
            public const string ForeignKeyViolation = "REL002";
            
            /// <summary>
            /// 고유키 제약 위반
            /// </summary>
            public const string UniqueKeyViolation = "REL003";
        }

        /// <summary>
        /// 속성 검수 관련 오류 코드
        /// </summary>
        public static class Attribute
        {
            /// <summary>
            /// 속성값이 유효하지 않습니다
            /// </summary>
            public const string InvalidValue = "ATT001";
            
            /// <summary>
            /// 필수 속성이 누락되었습니다
            /// </summary>
            public const string MissingValue = "ATT002";
        }
    }
}
