namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 전체 검수 설정을 담는 모델
    /// </summary>
    public class ValidationConfig
    {
        /// <summary>설정 파일 경로 정보</summary>
        public ConfigFilePaths FilePaths { get; set; } = new();

        /// <summary>테이블 검수 설정 목록</summary>
        public List<TableCheckConfig> TableChecks { get; set; } = new();

        /// <summary>스키마 검수 설정 목록</summary>
        public List<SchemaCheckConfig> SchemaChecks { get; set; } = new();

        /// <summary>지오메트리 검수 설정 목록</summary>
        public List<GeometryCheckConfig> GeometryChecks { get; set; } = new();

        /// <summary>관계 검수 설정 목록</summary>
        public List<RelationCheckConfig> RelationChecks { get; set; } = new();

        /// <summary>설정 로드 시간</summary>
        public DateTime LoadedAt { get; set; }

        /// <summary>설정 해시 (중복 검수 방지용)</summary>
        public string ConfigHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// 설정 파일 경로 정보
    /// </summary>
    public class ConfigFilePaths
    {
        /// <summary>테이블 검수 설정 파일 경로</summary>
        public string TableCheckFile { get; set; } = string.Empty;

        /// <summary>스키마 검수 설정 파일 경로</summary>
        public string SchemaCheckFile { get; set; } = string.Empty;

        /// <summary>지오메트리 검수 설정 파일 경로</summary>
        public string GeometryCheckFile { get; set; } = string.Empty;

        /// <summary>관계 검수 설정 파일 경로</summary>
        public string RelationCheckFile { get; set; } = string.Empty;
    }
}
