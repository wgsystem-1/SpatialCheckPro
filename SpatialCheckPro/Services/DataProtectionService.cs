using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 민감한 데이터 보호를 담당하는 서비스 구현체
    /// </summary>
    public class DataProtectionService : IDataProtectionService
    {
        private readonly ILogger<DataProtectionService> _logger;
        private const int CURRENT_VERSION = 1;
        private const string ALGORITHM_NAME = "AES-256-GCM";

        // 암호화 키는 실제 운영 환경에서는 보안 저장소에서 관리해야 함
        private readonly byte[] _masterKey;

        public DataProtectionService(ILogger<DataProtectionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 환경 변수에서 암호화 키를 가져오거나 기본값 사용
            var keyPassword = Environment.GetEnvironmentVariable("GDAL_VALIDATION_KEY") 
                            ?? "DefaultKeyForDevelopmentOnly";
            
            _masterKey = DeriveKeyFromPassword(keyPassword);
            
            if (keyPassword == "DefaultKeyForDevelopmentOnly")
            {
                _logger.LogWarning("개발용 기본 키를 사용 중입니다. 운영 환경에서는 GDAL_VALIDATION_KEY 환경 변수를 설정하세요.");
            }
        }

        /// <summary>
        /// 검수 결과 데이터를 암호화하여 저장합니다
        /// </summary>
        public async Task<EncryptedData> EncryptValidationResultAsync(ValidationResult validationResult)
        {
            try
            {
                _logger.LogInformation("검수 결과 데이터 암호화 시작: {ValidationId}", validationResult.ValidationId);

                // JSON 직렬화
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var jsonData = JsonSerializer.Serialize(validationResult, jsonOptions);
                var plainTextBytes = Encoding.UTF8.GetBytes(jsonData);

                // 암호화 수행
                var encryptedData = await EncryptBytesAsync(plainTextBytes);
                
                _logger.LogInformation("검수 결과 데이터 암호화 완료: {ValidationId}", validationResult.ValidationId);
                return encryptedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 데이터 암호화 중 오류 발생: {ValidationId}", validationResult?.ValidationId);
                throw new InvalidOperationException("검수 결과 데이터 암호화에 실패했습니다.", ex);
            }
        }

        /// <summary>
        /// 암호화된 검수 결과 데이터를 복호화합니다
        /// </summary>
        public async Task<ValidationResult> DecryptValidationResultAsync(EncryptedData encryptedData)
        {
            try
            {
                _logger.LogInformation("검수 결과 데이터 복호화 시작");

                // 복호화 수행
                var decryptedBytes = await DecryptBytesAsync(encryptedData);
                var jsonData = Encoding.UTF8.GetString(decryptedBytes);

                // JSON 역직렬화
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var validationResult = JsonSerializer.Deserialize<ValidationResult>(jsonData, jsonOptions);
                
                _logger.LogInformation("검수 결과 데이터 복호화 완료: {ValidationId}", validationResult?.ValidationId);
                return validationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 데이터 복호화 중 오류 발생");
                throw new InvalidOperationException("검수 결과 데이터 복호화에 실패했습니다.", ex);
            }
        }

        /// <summary>
        /// 민감한 문자열 데이터를 암호화합니다
        /// </summary>
        public async Task<string> EncryptStringAsync(string plainText)
        {
            try
            {
                if (string.IsNullOrEmpty(plainText))
                    return string.Empty;

                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedData = await EncryptBytesAsync(plainTextBytes);

                // Base64로 인코딩하여 문자열로 반환
                var encryptedJson = JsonSerializer.Serialize(encryptedData);
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(encryptedJson));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "문자열 암호화 중 오류 발생");
                throw new InvalidOperationException("문자열 암호화에 실패했습니다.", ex);
            }
        }

        /// <summary>
        /// 암호화된 문자열을 복호화합니다
        /// </summary>
        public async Task<string> DecryptStringAsync(string encryptedText)
        {
            try
            {
                if (string.IsNullOrEmpty(encryptedText))
                    return string.Empty;

                // Base64 디코딩
                var encryptedJson = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedText));
                var encryptedData = JsonSerializer.Deserialize<EncryptedData>(encryptedJson);

                var decryptedBytes = await DecryptBytesAsync(encryptedData);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "문자열 복호화 중 오류 발생");
                throw new InvalidOperationException("문자열 복호화에 실패했습니다.", ex);
            }
        }

        /// <summary>
        /// 데이터 무결성을 위한 해시값을 생성합니다
        /// </summary>
        public string GenerateHash(byte[] data)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(data);
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "해시 생성 중 오류 발생");
                throw new InvalidOperationException("해시 생성에 실패했습니다.", ex);
            }
        }

        /// <summary>
        /// 데이터 무결성을 검증합니다
        /// </summary>
        public bool VerifyIntegrity(byte[] data, string expectedHash)
        {
            try
            {
                var actualHash = GenerateHash(data);
                return string.Equals(actualHash, expectedHash, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "무결성 검증 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// 바이트 배열을 암호화합니다
        /// </summary>
        private async Task<EncryptedData> EncryptBytesAsync(byte[] plainTextBytes)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                
                // 랜덤 IV 생성
                aes.GenerateIV();
                var iv = aes.IV;

                // 랜덤 솔트 생성
                var salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                // 키 파생
                var key = DeriveKey(_masterKey, salt);
                aes.Key = key;

                byte[] encryptedBytes;
                byte[] tag = new byte[16]; // GCM 태그

                using (var encryptor = aes.CreateEncryptor())
                using (var msEncrypt = new MemoryStream())
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    await csEncrypt.WriteAsync(plainTextBytes, 0, plainTextBytes.Length);
                    csEncrypt.FlushFinalBlock();
                    encryptedBytes = msEncrypt.ToArray();
                }

                // 무결성 해시 생성
                var integrityHash = GenerateHash(encryptedBytes);

                return new EncryptedData
                {
                    Data = encryptedBytes,
                    IV = iv,
                    Salt = salt,
                    Algorithm = ALGORITHM_NAME,
                    IntegrityHash = integrityHash,
                    EncryptedAt = DateTime.UtcNow,
                    Version = CURRENT_VERSION
                };
            }
        }

        /// <summary>
        /// 암호화된 바이트 배열을 복호화합니다
        /// </summary>
        private async Task<byte[]> DecryptBytesAsync(EncryptedData encryptedData)
        {
            // 무결성 검증
            if (!VerifyIntegrity(encryptedData.Data, encryptedData.IntegrityHash))
            {
                throw new InvalidOperationException("데이터 무결성 검증에 실패했습니다.");
            }

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.IV = encryptedData.IV;

                // 키 파생
                var key = DeriveKey(_masterKey, encryptedData.Salt);
                aes.Key = key;

                using (var decryptor = aes.CreateDecryptor())
                using (var msDecrypt = new MemoryStream(encryptedData.Data))
                using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (var msPlain = new MemoryStream())
                {
                    await csDecrypt.CopyToAsync(msPlain);
                    return msPlain.ToArray();
                }
            }
        }

        /// <summary>
        /// 패스워드에서 마스터 키를 파생합니다
        /// </summary>
        private byte[] DeriveKeyFromPassword(string password)
        {
            var salt = Encoding.UTF8.GetBytes("GeoSpatialValidationSalt2024");
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32); // 256비트 키
            }
        }

        /// <summary>
        /// 마스터 키와 솔트에서 암호화 키를 파생합니다
        /// </summary>
        private byte[] DeriveKey(byte[] masterKey, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(masterKey, salt, 1000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32); // 256비트 키
            }
        }
    }
}
