using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// CSV 설정 파일 관리를 위한 서비스 인터페이스
    /// </summary>
    public interface ICsvConfigService
    {
        /// <summary>
        /// 전체 검수 설정 로드
        /// </summary>
        /// <param name="configDirectory">설정 파일 디렉토리 경로</param>
        /// <returns>검수 설정</returns>
        Task<ValidationConfig> LoadValidationConfigAsync(string configDirectory);

        /// <summary>
        /// 테이블 검수 설정 로드
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <returns>테이블 검수 설정 목록</returns>
        Task<List<TableCheckConfig>> LoadTableCheckConfigAsync(string filePath);

        /// <summary>
        /// 스키마 검수 설정 로드
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <returns>스키마 검수 설정 목록</returns>
        Task<List<SchemaCheckConfig>> LoadSchemaCheckConfigAsync(string filePath);

        /// <summary>
        /// 지오메트리 검수 설정 로드
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <returns>지오메트리 검수 설정 목록</returns>
        Task<List<GeometryCheckConfig>> LoadGeometryCheckConfigAsync(string filePath);

        /// <summary>
        /// 관계 검수 설정 로드
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <returns>관계 검수 설정 목록</returns>
        Task<List<RelationCheckConfig>> LoadRelationCheckConfigAsync(string filePath);

        /// <summary>
        /// 테이블 검수 설정 저장
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <param name="configs">테이블 검수 설정 목록</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveTableCheckConfigAsync(string filePath, List<TableCheckConfig> configs);

        /// <summary>
        /// 스키마 검수 설정 저장
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <param name="configs">스키마 검수 설정 목록</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveSchemaCheckConfigAsync(string filePath, List<SchemaCheckConfig> configs);

        /// <summary>
        /// 지오메트리 검수 설정 저장
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <param name="configs">지오메트리 검수 설정 목록</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveGeometryCheckConfigAsync(string filePath, List<GeometryCheckConfig> configs);

        /// <summary>
        /// 관계 검수 설정 저장
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <param name="configs">관계 검수 설정 목록</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveRelationCheckConfigAsync(string filePath, List<RelationCheckConfig> configs);

        /// <summary>
        /// CSV 파일 유효성 검증
        /// </summary>
        /// <param name="filePath">CSV 파일 경로</param>
        /// <param name="configType">설정 타입</param>
        /// <returns>검증 결과</returns>
        Task<CsvValidationResult> ValidateCsvFileAsync(string filePath, ConfigType configType);

        /// <summary>
        /// 기본 설정 파일 생성
        /// </summary>
        /// <param name="configDirectory">설정 파일 디렉토리 경로</param>
        /// <returns>생성 성공 여부</returns>
        Task<bool> CreateDefaultConfigFilesAsync(string configDirectory);
    }

    /// <summary>
    /// 설정 타입 열거형
    /// </summary>
    public enum ConfigType
    {
        /// <summary>
        /// 테이블 검수 설정
        /// </summary>
        TableCheck,

        /// <summary>
        /// 스키마 검수 설정
        /// </summary>
        SchemaCheck,

        /// <summary>
        /// 지오메트리 검수 설정
        /// </summary>
        GeometryCheck,

        /// <summary>
        /// 관계 검수 설정
        /// </summary>
        RelationCheck
    }

    /// <summary>
    /// CSV 파일 검증 결과
    /// </summary>
    public class CsvValidationResult
    {
        /// <summary>
        /// 검증 성공 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 오류 메시지 목록
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 경고 메시지 목록
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 로드된 레코드 수
        /// </summary>
        public int RecordCount { get; set; }

        /// <summary>
        /// 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 설정 타입
        /// </summary>
        public ConfigType ConfigType { get; set; }
    }
}
