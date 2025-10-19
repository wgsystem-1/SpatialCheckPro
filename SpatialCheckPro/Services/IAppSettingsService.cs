using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 애플리케이션 설정을 관리하는 서비스 인터페이스
    /// </summary>
    public interface IAppSettingsService
    {
        /// <summary>
        /// 애플리케이션 설정을 로드합니다
        /// </summary>
        /// <returns>애플리케이션 설정 객체</returns>
        AppSettings LoadSettings();

        /// <summary>
        /// 애플리케이션 설정을 저장합니다
        /// </summary>
        /// <param name="settings">저장할 설정 객체</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveSettingsAsync(AppSettings settings);

        /// <summary>
        /// 특정 설정 섹션을 가져옵니다
        /// </summary>
        /// <typeparam name="T">설정 섹션 타입</typeparam>
        /// <param name="sectionName">섹션 이름</param>
        /// <returns>설정 섹션 객체</returns>
        T GetSection<T>(string sectionName) where T : class, new();

        /// <summary>
        /// 설정 파일 경로를 가져옵니다
        /// </summary>
        /// <returns>설정 파일 경로</returns>
        string GetSettingsFilePath();

        /// <summary>
        /// 설정 파일이 존재하는지 확인합니다
        /// </summary>
        /// <returns>존재 여부</returns>
        bool SettingsFileExists();

        /// <summary>
        /// 기본 설정 파일을 생성합니다
        /// </summary>
        /// <returns>생성 성공 여부</returns>
        Task<bool> CreateDefaultSettingsAsync();

        /// <summary>
        /// 설정 유효성을 검사합니다
        /// </summary>
        /// <param name="settings">검사할 설정</param>
        /// <returns>유효성 검사 결과</returns>
        SettingsValidationResult ValidateSettings(AppSettings settings);
    }

    /// <summary>
    /// 설정 유효성 검사 결과
    /// </summary>
    public class SettingsValidationResult
    {
        /// <summary>유효성 검사 성공 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>오류 메시지 목록</summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>경고 메시지 목록</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
