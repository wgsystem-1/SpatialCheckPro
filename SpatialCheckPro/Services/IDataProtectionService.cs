using System;
using System.Threading.Tasks;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 민감한 데이터 보호를 담당하는 서비스 인터페이스
    /// </summary>
    public interface IDataProtectionService
    {
        /// <summary>
        /// 검수 결과 데이터를 암호화하여 저장합니다
        /// </summary>
        /// <param name="validationResult">암호화할 검수 결과</param>
        /// <returns>암호화된 데이터</returns>
        Task<EncryptedData> EncryptValidationResultAsync(ValidationResult validationResult);

        /// <summary>
        /// 암호화된 검수 결과 데이터를 복호화합니다
        /// </summary>
        /// <param name="encryptedData">복호화할 암호화된 데이터</param>
        /// <returns>복호화된 검수 결과</returns>
        Task<ValidationResult> DecryptValidationResultAsync(EncryptedData encryptedData);

        /// <summary>
        /// 민감한 문자열 데이터를 암호화합니다
        /// </summary>
        /// <param name="plainText">암호화할 평문</param>
        /// <returns>암호화된 문자열</returns>
        Task<string> EncryptStringAsync(string plainText);

        /// <summary>
        /// 암호화된 문자열을 복호화합니다
        /// </summary>
        /// <param name="encryptedText">복호화할 암호화된 문자열</param>
        /// <returns>복호화된 평문</returns>
        Task<string> DecryptStringAsync(string encryptedText);

        /// <summary>
        /// 데이터 무결성을 위한 해시값을 생성합니다
        /// </summary>
        /// <param name="data">해시를 생성할 데이터</param>
        /// <returns>해시값</returns>
        string GenerateHash(byte[] data);

        /// <summary>
        /// 데이터 무결성을 검증합니다
        /// </summary>
        /// <param name="data">검증할 데이터</param>
        /// <param name="expectedHash">예상 해시값</param>
        /// <returns>무결성 검증 결과</returns>
        bool VerifyIntegrity(byte[] data, string expectedHash);
    }

    /// <summary>
    /// 암호화된 데이터 모델
    /// </summary>
    public class EncryptedData
    {
        /// <summary>암호화된 데이터</summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>초기화 벡터</summary>
        public byte[] IV { get; set; } = Array.Empty<byte>();

        /// <summary>솔트</summary>
        public byte[] Salt { get; set; } = Array.Empty<byte>();

        /// <summary>암호화 알고리즘</summary>
        public string Algorithm { get; set; } = string.Empty;

        /// <summary>데이터 무결성 해시</summary>
        public string IntegrityHash { get; set; } = string.Empty;

        /// <summary>암호화 시간</summary>
        public DateTime EncryptedAt { get; set; }

        /// <summary>암호화 버전</summary>
        public int Version { get; set; }
    }
}
