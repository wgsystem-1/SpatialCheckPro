using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 파일 시스템 보안 검증을 담당하는 서비스 인터페이스
    /// </summary>
    public interface IFileSecurityService
    {
        /// <summary>
        /// 파일 경로의 보안성을 검증합니다
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <returns>보안 검증 결과</returns>
        Task<FileSecurityResult> ValidateFilePathAsync(string filePath);

        /// <summary>
        /// 파일 확장자가 허용된 형식인지 검증합니다
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <returns>확장자 검증 결과</returns>
        bool ValidateFileExtension(string filePath);

        /// <summary>
        /// 파일의 매직 바이트를 검증하여 실제 파일 형식을 확인합니다
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <returns>매직 바이트 검증 결과</returns>
        Task<MagicByteValidationResult> ValidateMagicBytesAsync(string filePath);

        /// <summary>
        /// 파일 크기가 허용 범위 내인지 검증합니다
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <returns>파일 크기 검증 결과</returns>
        Task<FileSizeValidationResult> ValidateFileSizeAsync(string filePath);

        /// <summary>
        /// 파일 내용에 의심스러운 패턴이 있는지 검사합니다
        /// </summary>
        /// <param name="filePath">검사할 파일 경로</param>
        /// <returns>내용 검사 결과</returns>
        Task<ContentSecurityResult> ScanFileContentAsync(string filePath);

        /// <summary>
        /// 종합적인 파일 보안 검증을 수행합니다
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <returns>종합 보안 검증 결과</returns>
        Task<ComprehensiveSecurityResult> PerformComprehensiveSecurityCheckAsync(string filePath);
    }

    /// <summary>
    /// 파일 보안 검증 결과
    /// </summary>
    public class FileSecurityResult
    {
        /// <summary>검증 성공 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }

        /// <summary>보안 위험 수준</summary>
        public SecurityRiskLevel RiskLevel { get; set; }

        /// <summary>차단된 이유</summary>
        public List<string> BlockedReasons { get; set; } = new();
    }

    /// <summary>
    /// 매직 바이트 검증 결과
    /// </summary>
    public class MagicByteValidationResult
    {
        /// <summary>검증 성공 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>감지된 실제 파일 형식</summary>
        public string DetectedFileType { get; set; }

        /// <summary>예상 파일 형식</summary>
        public string ExpectedFileType { get; set; }

        /// <summary>형식 불일치 여부</summary>
        public bool IsTypeMismatch { get; set; }

        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 파일 크기 검증 결과
    /// </summary>
    public class FileSizeValidationResult
    {
        /// <summary>검증 성공 여부</summary>
        public bool IsValid { get; set; }

        /// <summary>파일 크기 (바이트)</summary>
        public long FileSize { get; set; }

        /// <summary>최대 허용 크기 (바이트)</summary>
        public long MaxAllowedSize { get; set; }

        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 파일 내용 보안 검사 결과
    /// </summary>
    public class ContentSecurityResult
    {
        /// <summary>검사 성공 여부</summary>
        public bool IsSafe { get; set; }

        /// <summary>발견된 위험 패턴 목록</summary>
        public List<string> DetectedThreats { get; set; } = new();

        /// <summary>보안 위험 수준</summary>
        public SecurityRiskLevel RiskLevel { get; set; }

        /// <summary>상세 메시지</summary>
        public string Details { get; set; }
    }

    /// <summary>
    /// 종합 보안 검증 결과
    /// </summary>
    public class ComprehensiveSecurityResult
    {
        /// <summary>전체 검증 성공 여부</summary>
        public bool IsSecure { get; set; }

        /// <summary>파일 경로 검증 결과</summary>
        public FileSecurityResult PathValidation { get; set; }

        /// <summary>확장자 검증 성공 여부</summary>
        public bool ExtensionValidation { get; set; }

        /// <summary>매직 바이트 검증 결과</summary>
        public MagicByteValidationResult MagicByteValidation { get; set; }

        /// <summary>파일 크기 검증 결과</summary>
        public FileSizeValidationResult SizeValidation { get; set; }

        /// <summary>내용 보안 검사 결과</summary>
        public ContentSecurityResult ContentSecurity { get; set; }

        /// <summary>전체 보안 위험 수준</summary>
        public SecurityRiskLevel OverallRiskLevel { get; set; }

        /// <summary>검증 요약 메시지</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// 보안 위험 수준
    /// </summary>
    public enum SecurityRiskLevel
    {
        /// <summary>안전</summary>
        Safe = 0,

        /// <summary>낮은 위험</summary>
        Low = 1,

        /// <summary>중간 위험</summary>
        Medium = 2,

        /// <summary>높은 위험</summary>
        High = 3,

        /// <summary>매우 높은 위험</summary>
        Critical = 4
    }
}
