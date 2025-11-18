using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 애플리케이션 설정을 관리하는 서비스 구현 클래스
    /// </summary>
    public class AppSettingsService : IAppSettingsService
    {
        private readonly ILogger<AppSettingsService> _logger;
        private readonly string _settingsFilePath;
        private AppSettings? _cachedSettings;

        /// <summary>
        /// AppSettingsService 생성자
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        public AppSettingsService(ILogger<AppSettingsService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        }

        /// <summary>
        /// 애플리케이션 설정을 로드합니다
        /// </summary>
        /// <returns>애플리케이션 설정 객체</returns>
        public AppSettings LoadSettings()
        {
            try
            {
                // 캐시된 설정이 있으면 반환
                if (_cachedSettings != null)
                {
                    return _cachedSettings;
                }

                // 설정 파일이 존재하지 않으면 기본 설정 생성 (정상적인 초기화 동작)
                if (!SettingsFileExists())
                {
                    _logger.LogInformation("설정 파일이 없어 기본 설정을 생성합니다: {FilePath}", _settingsFilePath);
                    CreateDefaultSettingsAsync().Wait();
                }

                // 설정 파일 읽기
                var jsonContent = File.ReadAllText(_settingsFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(jsonContent, options) ?? new AppSettings();

                // 설정 유효성 검사
                var validationResult = ValidateSettings(_cachedSettings);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("설정 파일에 오류가 있습니다: {Errors}", string.Join(", ", validationResult.Errors));
                }

                _logger.LogInformation("애플리케이션 설정을 성공적으로 로드했습니다");
                return _cachedSettings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "설정 파일 로드 중 오류가 발생했습니다: {FilePath}", _settingsFilePath);
                
                // 오류 발생 시 기본 설정 반환
                _cachedSettings = new AppSettings();
                return _cachedSettings;
            }
        }

        /// <summary>
        /// 애플리케이션 설정을 저장합니다
        /// </summary>
        /// <param name="settings">저장할 설정 객체</param>
        /// <returns>저장 성공 여부</returns>
        public async Task<bool> SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                if (settings == null)
                {
                    throw new ArgumentNullException(nameof(settings));
                }

                // 설정 유효성 검사
                var validationResult = ValidateSettings(settings);
                if (!validationResult.IsValid)
                {
                    _logger.LogError("유효하지 않은 설정입니다: {Errors}", string.Join(", ", validationResult.Errors));
                    return false;
                }

                // JSON 직렬화 옵션
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                // 설정 파일 디렉토리 생성
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 설정 파일 저장
                var jsonContent = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(_settingsFilePath, jsonContent);

                // 캐시 업데이트
                _cachedSettings = settings;

                _logger.LogInformation("애플리케이션 설정을 성공적으로 저장했습니다: {FilePath}", _settingsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "설정 파일 저장 중 오류가 발생했습니다: {FilePath}", _settingsFilePath);
                return false;
            }
        }

        /// <summary>
        /// 특정 설정 섹션을 가져옵니다
        /// </summary>
        /// <typeparam name="T">설정 섹션 타입</typeparam>
        /// <param name="sectionName">섹션 이름</param>
        /// <returns>설정 섹션 객체</returns>
        public T GetSection<T>(string sectionName) where T : class, new()
        {
            try
            {
                var settings = LoadSettings();
                var property = typeof(AppSettings).GetProperty(sectionName);
                
                if (property != null && property.GetValue(settings) is T sectionValue)
                {
                    return sectionValue;
                }

                _logger.LogWarning("설정 섹션을 찾을 수 없습니다: {SectionName}", sectionName);
                return new T();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "설정 섹션 로드 중 오류가 발생했습니다: {SectionName}", sectionName);
                return new T();
            }
        }

        /// <summary>
        /// 설정 파일 경로를 가져옵니다
        /// </summary>
        /// <returns>설정 파일 경로</returns>
        public string GetSettingsFilePath()
        {
            return _settingsFilePath;
        }

        /// <summary>
        /// 설정 파일이 존재하는지 확인합니다
        /// </summary>
        /// <returns>존재 여부</returns>
        public bool SettingsFileExists()
        {
            return File.Exists(_settingsFilePath);
        }

        /// <summary>
        /// 기본 설정 파일을 생성합니다
        /// </summary>
        /// <returns>생성 성공 여부</returns>
        public async Task<bool> CreateDefaultSettingsAsync()
        {
            try
            {
                var defaultSettings = new AppSettings();
                return await SaveSettingsAsync(defaultSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본 설정 파일 생성 중 오류가 발생했습니다");
                return false;
            }
        }

        /// <summary>
        /// 설정 유효성을 검사합니다
        /// </summary>
        /// <param name="settings">검사할 설정</param>
        /// <returns>유효성 검사 결과</returns>
        public SettingsValidationResult ValidateSettings(AppSettings settings)
        {
            var result = new SettingsValidationResult { IsValid = true };

            if (settings == null)
            {
                result.IsValid = false;
                result.Errors.Add("설정 객체가 null입니다");
                return result;
            }

            try
            {
                // 데이터 어노테이션 기반 유효성 검사
                var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
                var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
                
                if (!Validator.TryValidateObject(settings, validationContext, validationResults, true))
                {
                    result.IsValid = false;
                    result.Errors.AddRange(validationResults.Select(vr => vr.ErrorMessage ?? "알 수 없는 유효성 검사 오류"));
                }

                // 추가 비즈니스 로직 검증
                ValidateBusinessRules(settings, result);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"유효성 검사 중 오류 발생: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 비즈니스 규칙 유효성을 검사합니다
        /// </summary>
        /// <param name="settings">검사할 설정</param>
        /// <param name="result">검사 결과</param>
        private void ValidateBusinessRules(AppSettings settings, SettingsValidationResult result)
        {
            // 파일 처리 설정 검증
            if (settings.FileProcessing.MaxFileSizeBytes <= 0)
            {
                result.Errors.Add("최대 파일 크기는 0보다 커야 합니다");
                result.IsValid = false;
            }

            if (settings.FileProcessing.ChunkSizeBytes <= 0 || 
                settings.FileProcessing.ChunkSizeBytes > settings.FileProcessing.MaxFileSizeBytes)
            {
                result.Errors.Add("청크 크기는 0보다 크고 최대 파일 크기보다 작아야 합니다");
                result.IsValid = false;
            }

            // 데이터베이스 설정 검증
            if (string.IsNullOrWhiteSpace(settings.Database.ConnectionString))
            {
                result.Errors.Add("데이터베이스 연결 문자열이 필요합니다");
                result.IsValid = false;
            }

            if (settings.Database.CommandTimeout <= 0)
            {
                result.Warnings.Add("데이터베이스 명령 타임아웃이 0 이하입니다");
            }

            // 검수 설정 검증
            if (settings.Validation.MaxErrorsPerCheck <= 0)
            {
                result.Warnings.Add("검수 항목당 최대 오류 수가 0 이하입니다");
            }

            // 성능 설정 검증
            if (settings.Performance.MaxMemoryUsageMB <= 0)
            {
                result.Warnings.Add("최대 메모리 사용량이 0 이하입니다");
            }
        }
    }

    /// <summary>
    /// 재귀적 유효성 검사를 위한 확장 메서드
    /// </summary>
    public static class ValidatorExtensions
    {
        /// <summary>
        /// 객체와 그 속성들을 재귀적으로 유효성 검사합니다
        /// </summary>
        /// <param name="obj">검사할 객체</param>
        /// <param name="validationContext">유효성 검사 컨텍스트</param>
        /// <param name="results">검사 결과 목록</param>
        /// <returns>유효성 검사 성공 여부</returns>
        public static bool TryValidateObjectRecursively(object obj, System.ComponentModel.DataAnnotations.ValidationContext validationContext, ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results)
        {
            bool isValid = Validator.TryValidateObject(obj, validationContext, results, true);

            var properties = obj.GetType().GetProperties()
                .Where(prop => prop.CanRead && prop.GetValue(obj) != null);

            foreach (var property in properties)
            {
                var value = property.GetValue(obj);
                if (value == null) continue;

                if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                {
                    var nestedContext = new System.ComponentModel.DataAnnotations.ValidationContext(value);
                    if (!TryValidateObjectRecursively(value, nestedContext, results))
                    {
                        isValid = false;
                    }
                }
            }

            return isValid;
        }
    }
}
