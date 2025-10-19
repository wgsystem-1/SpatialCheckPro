using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 애플리케이션 전체 설정을 관리하는 클래스
    /// </summary>
    public class AppSettings
    {
        /// <summary>로깅 설정</summary>
        public LoggingSettings Logging { get; set; } = new();

        /// <summary>데이터베이스 설정</summary>
        public DatabaseSettings Database { get; set; } = new();

        /// <summary>파일 처리 설정</summary>
        public FileProcessingSettings FileProcessing { get; set; } = new();

        /// <summary>검수 설정</summary>
        public ValidationSettings Validation { get; set; } = new();

        /// <summary>UI 설정</summary>
        public UISettings UI { get; set; } = new();

        /// <summary>보안 설정</summary>
        public SecuritySettings Security { get; set; } = new();

        /// <summary>성능 설정</summary>
        public PerformanceSettings Performance { get; set; } = new();
    }

    /// <summary>
    /// 로깅 관련 설정
    /// </summary>
    public class LoggingSettings
    {
        /// <summary>로그 레벨 설정</summary>
        public Dictionary<string, string> LogLevel { get; set; } = new();

        /// <summary>파일 로깅 설정</summary>
        public FileLoggingSettings File { get; set; } = new();
    }

    /// <summary>
    /// 파일 로깅 설정
    /// </summary>
    public class FileLoggingSettings
    {
        /// <summary>로그 파일 경로 패턴</summary>
        [Required]
        public string Path { get; set; } = "Logs/app-{Date}.log";

        /// <summary>로그 파일 롤링 간격</summary>
        public string RollingInterval { get; set; } = "Day";

        /// <summary>보관할 로그 파일 수</summary>
        public int RetainedFileCountLimit { get; set; } = 30;

        /// <summary>로그 파일 최대 크기 (바이트)</summary>
        public long FileSizeLimitBytes { get; set; } = 10485760; // 10MB

        /// <summary>파일 크기 제한 시 롤링 여부</summary>
        public bool RollOnFileSizeLimit { get; set; } = true;
    }

    /// <summary>
    /// 데이터베이스 관련 설정
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>데이터베이스 연결 문자열</summary>
        [Required]
        public string ConnectionString { get; set; } = "Data Source=ValidationResults.db;Cache=Shared";

        /// <summary>명령 타임아웃 (초)</summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>민감한 데이터 로깅 활성화 여부</summary>
        public bool EnableSensitiveDataLogging { get; set; } = false;
    }

    /// <summary>
    /// 파일 처리 관련 설정
    /// </summary>
    public class FileProcessingSettings
    {
        /// <summary>최대 파일 크기 (바이트)</summary>
        public long MaxFileSizeBytes { get; set; } = 2147483648; // 2GB

        /// <summary>청크 크기 (바이트)</summary>
        public int ChunkSizeBytes { get; set; } = 104857600; // 100MB

        /// <summary>임시 디렉토리</summary>
        public string TempDirectory { get; set; } = "Temp";

        /// <summary>지원되는 파일 형식</summary>
        public List<string> SupportedFormats { get; set; } = new() { "SHP", "FileGDB", "GeoPackage" };

        /// <summary>동시 처리 가능한 최대 파일 수</summary>
        public int MaxConcurrentFiles { get; set; } = 5;
    }

    /// <summary>
    /// 검수 관련 설정
    /// </summary>
    public class ValidationSettings
    {
        /// <summary>설정 파일 디렉토리</summary>
        public string ConfigDirectory { get; set; } = "Config";

        /// <summary>레포트 출력 디렉토리</summary>
        public string ReportOutputDirectory { get; set; } = "Reports";

        /// <summary>오류 SHP 파일 디렉토리</summary>
        public string ErrorShapefileDirectory { get; set; } = "ErrorShapefiles";

        /// <summary>검수 항목당 최대 오류 수</summary>
        public int MaxErrorsPerCheck { get; set; } = 10000;

        /// <summary>상세 로깅 활성화 여부</summary>
        public bool EnableDetailedLogging { get; set; } = true;
    }

    /// <summary>
    /// UI 관련 설정
    /// </summary>
    public class UISettings
    {
        /// <summary>테마 설정</summary>
        public string Theme { get; set; } = "Light";

        /// <summary>언어 설정</summary>
        public string Language { get; set; } = "ko-KR";

        /// <summary>자동 저장 간격 (초)</summary>
        public int AutoSaveInterval { get; set; } = 300;

        /// <summary>진행률 상세 표시 여부</summary>
        public bool ShowProgressDetails { get; set; } = true;

        /// <summary>지도 기본 확대 레벨</summary>
        public int MapZoomLevel { get; set; } = 10;
    }

    /// <summary>
    /// 보안 관련 설정
    /// </summary>
    public class SecuritySettings
    {
        /// <summary>파일 경로 검증 활성화 여부</summary>
        public bool EnableFilePathValidation { get; set; } = true;

        /// <summary>허용되는 파일 확장자</summary>
        public List<string> AllowedFileExtensions { get; set; } = new() { ".shp", ".gdb", ".gpkg", ".csv" };

        /// <summary>최대 파일 경로 길이</summary>
        public int MaxFilePathLength { get; set; } = 260;

        /// <summary>감사 로깅 활성화 여부</summary>
        public bool EnableAuditLogging { get; set; } = true;

        /// <summary>민감한 데이터 암호화 여부</summary>
        public bool EncryptSensitiveData { get; set; } = true;
    }

    /// <summary>
    /// 성능 관련 설정
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>최대 메모리 사용량 (MB)</summary>
        public int MaxMemoryUsageMB { get; set; } = 2048;

        /// <summary>성능 카운터 활성화 여부</summary>
        public bool EnablePerformanceCounters { get; set; } = true;

        /// <summary>가비지 컬렉션 모드</summary>
        public string GarbageCollectionMode { get; set; } = "Interactive";
    }
}
