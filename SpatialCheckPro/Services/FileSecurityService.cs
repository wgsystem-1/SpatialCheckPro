using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 파일 시스템 보안 검증을 담당하는 서비스 구현체
    /// </summary>
    public class FileSecurityService : IFileSecurityService
    {
        private readonly ILogger<FileSecurityService> _logger;

        // 허용된 파일 확장자 목록
        private readonly HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",  // Shapefile 관련
            ".gdb",                                                   // FileGDB
            ".gpkg",                                                  // GeoPackage
            ".csv"                                                    // 설정 파일
        };

        // 차단된 시스템 디렉토리 목록
        private readonly HashSet<string> _blockedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            @"C:\Boot",
            @"C:\EFI"
        };

        // 매직 바이트 패턴 정의
        private readonly Dictionary<string, byte[]> _magicBytePatterns = new()
        {
            { "SHP", new byte[] { 0x00, 0x00, 0x27, 0x0A } },        // Shapefile
            { "DBF", new byte[] { 0x03 } },                          // dBASE III
            { "GPKG", Encoding.UTF8.GetBytes("SQLite format 3") },   // GeoPackage (SQLite)
            { "CSV", new byte[] { } }                                // CSV는 텍스트 파일이므로 별도 처리
        };

        // 최대 파일 크기 (10GB)
        private const long MAX_FILE_SIZE = 10L * 1024 * 1024 * 1024;

        // 의심스러운 내용 패턴
        private readonly string[] _suspiciousPatterns = new[]
        {
            "javascript:",
            "<script",
            "eval(",
            "document.cookie",
            "window.location",
            "XMLHttpRequest",
            "ActiveXObject"
        };

        public FileSecurityService(ILogger<FileSecurityService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 파일 경로의 보안성을 검증합니다
        /// </summary>
        public Task<FileSecurityResult> ValidateFilePathAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(new FileSecurityResult
                    {
                        IsValid = false,
                        ErrorMessage = "파일 경로가 비어있습니다.",
                        RiskLevel = SecurityRiskLevel.High,
                        BlockedReasons = { "빈 파일 경로" }
                    });
                }

                var fullPath = Path.GetFullPath(filePath);
                var result = new FileSecurityResult { IsValid = true, RiskLevel = SecurityRiskLevel.Safe };

                // 시스템 디렉토리 접근 차단 검사
                foreach (var blockedDir in _blockedDirectories)
                {
                    if (fullPath.StartsWith(blockedDir, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsValid = false;
                        result.RiskLevel = SecurityRiskLevel.Critical;
                        result.ErrorMessage = $"시스템 디렉토리 접근이 차단되었습니다: {blockedDir}";
                        result.BlockedReasons.Add($"시스템 디렉토리 접근: {blockedDir}");
                        
                        _logger.LogWarning("시스템 디렉토리 접근 시도 차단: {FilePath}", fullPath);
                        break;
                    }
                }

                // 경로 순회 공격 검사 (../ 패턴)
                if (filePath.Contains(".."))
                {
                    result.IsValid = false;
                    result.RiskLevel = SecurityRiskLevel.High;
                    result.ErrorMessage = "경로 순회 공격 패턴이 감지되었습니다.";
                    result.BlockedReasons.Add("경로 순회 공격 패턴");
                    
                    _logger.LogWarning("경로 순회 공격 패턴 감지: {FilePath}", filePath);
                }

                // 네트워크 경로 차단
                if (filePath.StartsWith(@"\\") || filePath.StartsWith("//"))
                {
                    result.IsValid = false;
                    result.RiskLevel = SecurityRiskLevel.Medium;
                    result.ErrorMessage = "네트워크 경로는 허용되지 않습니다.";
                    result.BlockedReasons.Add("네트워크 경로");
                    
                    _logger.LogWarning("네트워크 경로 접근 시도: {FilePath}", filePath);
                }

                // 파일 존재 여부 확인
                if (result.IsValid && !File.Exists(fullPath))
                {
                    result.IsValid = false;
                    result.RiskLevel = SecurityRiskLevel.Low;
                    result.ErrorMessage = "파일이 존재하지 않습니다.";
                    result.BlockedReasons.Add("파일 없음");
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 경로 보안 검증 중 오류 발생: {FilePath}", filePath);
                return Task.FromResult(new FileSecurityResult
                {
                    IsValid = false,
                    ErrorMessage = $"파일 경로 검증 중 오류가 발생했습니다: {ex.Message}",
                    RiskLevel = SecurityRiskLevel.High,
                    BlockedReasons = { "검증 오류" }
                });
            }
        }

        /// <summary>
        /// 파일 확장자가 허용된 형식인지 검증합니다
        /// </summary>
        public bool ValidateFileExtension(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    _logger.LogWarning("빈 파일 경로로 확장자 검증 시도");
                    return false;
                }

                var extension = Path.GetExtension(filePath);
                var isValid = _allowedExtensions.Contains(extension);

                if (!isValid)
                {
                    _logger.LogWarning("허용되지 않은 파일 확장자: {Extension} (파일: {FilePath})", extension, filePath);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 확장자 검증 중 오류 발생: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// 파일의 매직 바이트를 검증하여 실제 파일 형식을 확인합니다
        /// </summary>
        public async Task<MagicByteValidationResult> ValidateMagicBytesAsync(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToUpperInvariant().TrimStart('.');
                var result = new MagicByteValidationResult
                {
                    ExpectedFileType = extension,
                    IsValid = true
                };

                // CSV 파일은 텍스트 파일이므로 별도 처리
                if (extension == "CSV")
                {
                    result.DetectedFileType = "CSV";
                    return result;
                }

                // 매직 바이트 패턴이 정의되지 않은 확장자는 통과
                if (!_magicBytePatterns.ContainsKey(extension))
                {
                    result.DetectedFileType = extension;
                    return result;
                }

                var expectedPattern = _magicBytePatterns[extension];
                var buffer = new byte[Math.Max(expectedPattern.Length, 16)];

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                    
                    if (bytesRead < expectedPattern.Length)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = "파일이 너무 작아 매직 바이트를 확인할 수 없습니다.";
                        return result;
                    }

                    // 매직 바이트 비교
                    var matches = true;
                    for (int i = 0; i < expectedPattern.Length; i++)
                    {
                        if (buffer[i] != expectedPattern[i])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                    {
                        result.IsValid = false;
                        result.IsTypeMismatch = true;
                        result.DetectedFileType = "알 수 없음";
                        result.ErrorMessage = $"파일 확장자({extension})와 실제 파일 형식이 일치하지 않습니다.";
                        
                        _logger.LogWarning("매직 바이트 불일치 감지: {FilePath} (예상: {Expected})", filePath, extension);
                    }
                    else
                    {
                        result.DetectedFileType = extension;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "매직 바이트 검증 중 오류 발생: {FilePath}", filePath);
                return new MagicByteValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"매직 바이트 검증 중 오류가 발생했습니다: {ex.Message}",
                    ExpectedFileType = Path.GetExtension(filePath).TrimStart('.'),
                    DetectedFileType = "오류"
                };
            }
        }

        /// <summary>
        /// 파일 크기가 허용 범위 내인지 검증합니다
        /// </summary>
        public Task<FileSizeValidationResult> ValidateFileSizeAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var result = new FileSizeValidationResult
                {
                    FileSize = fileInfo.Length,
                    MaxAllowedSize = MAX_FILE_SIZE,
                    IsValid = fileInfo.Length <= MAX_FILE_SIZE
                };

                if (!result.IsValid)
                {
                    result.ErrorMessage = $"파일 크기({fileInfo.Length:N0} bytes)가 최대 허용 크기({MAX_FILE_SIZE:N0} bytes)를 초과했습니다.";
                    _logger.LogWarning("파일 크기 제한 초과: {FilePath} ({FileSize} bytes)", filePath, fileInfo.Length);
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 크기 검증 중 오류 발생: {FilePath}", filePath);
                return Task.FromResult(new FileSizeValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"파일 크기 검증 중 오류가 발생했습니다: {ex.Message}",
                    MaxAllowedSize = MAX_FILE_SIZE
                });
            }
        }

        /// <summary>
        /// 파일 내용에 의심스러운 패턴이 있는지 검사합니다
        /// </summary>
        public async Task<ContentSecurityResult> ScanFileContentAsync(string filePath)
        {
            try
            {
                var result = new ContentSecurityResult
                {
                    IsSafe = true,
                    RiskLevel = SecurityRiskLevel.Safe
                };

                // 텍스트 파일만 내용 검사 (CSV, PRJ 등)
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".csv" && extension != ".prj" && extension != ".txt")
                {
                    result.Details = "바이너리 파일은 내용 검사를 건너뜁니다.";
                    return result;
                }

                // 파일 크기가 너무 큰 경우 일부만 검사 (1MB)
                const int maxScanSize = 1024 * 1024;
                var buffer = new byte[maxScanSize];
                
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                    var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // 의심스러운 패턴 검사
                    foreach (var pattern in _suspiciousPatterns)
                    {
                        if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsSafe = false;
                            result.DetectedThreats.Add($"의심스러운 패턴 발견: {pattern}");
                            result.RiskLevel = SecurityRiskLevel.Medium;
                        }
                    }

                    if (!result.IsSafe)
                    {
                        result.Details = $"총 {result.DetectedThreats.Count}개의 의심스러운 패턴이 발견되었습니다.";
                        _logger.LogWarning("파일 내용에서 의심스러운 패턴 발견: {FilePath} ({ThreatCount}개)", 
                            filePath, result.DetectedThreats.Count);
                    }
                    else
                    {
                        result.Details = "파일 내용이 안전합니다.";
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 내용 보안 검사 중 오류 발생: {FilePath}", filePath);
                return new ContentSecurityResult
                {
                    IsSafe = false,
                    RiskLevel = SecurityRiskLevel.High,
                    Details = $"파일 내용 검사 중 오류가 발생했습니다: {ex.Message}",
                    DetectedThreats = { "검사 오류" }
                };
            }
        }

        /// <summary>
        /// 종합적인 파일 보안 검증을 수행합니다
        /// </summary>
        public async Task<ComprehensiveSecurityResult> PerformComprehensiveSecurityCheckAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("파일 종합 보안 검증 시작: {FilePath}", filePath);

                var result = new ComprehensiveSecurityResult();

                // 1. 파일 경로 검증
                result.PathValidation = await ValidateFilePathAsync(filePath);

                // 2. 확장자 검증
                result.ExtensionValidation = ValidateFileExtension(filePath);

                // 3. 매직 바이트 검증
                result.MagicByteValidation = await ValidateMagicBytesAsync(filePath);

                // 4. 파일 크기 검증
                result.SizeValidation = await ValidateFileSizeAsync(filePath);

                // 5. 내용 보안 검사
                result.ContentSecurity = await ScanFileContentAsync(filePath);

                // 전체 결과 평가
                result.IsSecure = result.PathValidation.IsValid &&
                                 result.ExtensionValidation &&
                                 result.MagicByteValidation.IsValid &&
                                 result.SizeValidation.IsValid &&
                                 result.ContentSecurity.IsSafe;

                // 전체 위험 수준 결정
                var riskLevels = new[]
                {
                    result.PathValidation.RiskLevel,
                    result.ContentSecurity.RiskLevel
                };

                result.OverallRiskLevel = riskLevels.Max();

                // 요약 메시지 생성
                if (result.IsSecure)
                {
                    result.Summary = "파일이 모든 보안 검증을 통과했습니다.";
                    _logger.LogInformation("파일 보안 검증 성공: {FilePath}", filePath);
                }
                else
                {
                    var issues = new List<string>();
                    if (!result.PathValidation.IsValid) issues.Add("경로 보안");
                    if (!result.ExtensionValidation) issues.Add("확장자");
                    if (!result.MagicByteValidation.IsValid) issues.Add("파일 형식");
                    if (!result.SizeValidation.IsValid) issues.Add("파일 크기");
                    if (!result.ContentSecurity.IsSafe) issues.Add("내용 보안");

                    result.Summary = $"다음 항목에서 보안 문제가 발견되었습니다: {string.Join(", ", issues)}";
                    _logger.LogWarning("파일 보안 검증 실패: {FilePath} - {Issues}", filePath, string.Join(", ", issues));
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "종합 파일 보안 검증 중 오류 발생: {FilePath}", filePath);
                return new ComprehensiveSecurityResult
                {
                    IsSecure = false,
                    OverallRiskLevel = SecurityRiskLevel.Critical,
                    Summary = $"보안 검증 중 오류가 발생했습니다: {ex.Message}"
                };
            }
        }
    }
}
