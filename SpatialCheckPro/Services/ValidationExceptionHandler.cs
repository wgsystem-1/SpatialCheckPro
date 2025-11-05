using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using System.IO;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 표준화된 예외 처리 핸들러
    /// Phase 2 Item #8: 예외 처리 표준화
    ///
    /// 목적:
    /// - 복구 가능한 예외: 로그 + 기본값 반환
    /// - 복구 불가능한 예외: 로그 + 재발생
    /// - 일관된 예외 처리 패턴 제공
    /// </summary>
    public class ValidationExceptionHandler
    {
        private readonly ILogger<ValidationExceptionHandler> _logger;

        public ValidationExceptionHandler(ILogger<ValidationExceptionHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 복구 가능한 예외 처리 - 로그 기록 후 기본값 반환
        /// 사용 시나리오: 파일 없음, 작업 취소, 타임아웃 등
        /// </summary>
        public async Task<ValidationResult> SafeExecuteAsync(
            Func<Task<ValidationResult>> action,
            string operationName = "작업")
        {
            try
            {
                return await action();
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 파일을 찾을 수 없습니다 - {FilePath}",
                    operationName, ex.FileName ?? "Unknown");
                return ValidationResult.CreateFileNotFound(ex.FileName ?? string.Empty);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 디렉토리를 찾을 수 없습니다",
                    operationName);
                return ValidationResult.CreateError("디렉토리를 찾을 수 없습니다", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 파일 접근 권한이 없습니다",
                    operationName);
                return ValidationResult.CreateError("파일 접근 권한이 없습니다", ex);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{OperationName}이 취소되었습니다", operationName);
                return ValidationResult.CreateCancelled($"{operationName}이 취소되었습니다");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 작업 시간 초과", operationName);
                return ValidationResult.CreateError("작업 시간이 초과되었습니다", ex);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: I/O 오류 발생", operationName);
                return ValidationResult.CreateError($"I/O 오류가 발생했습니다: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // 예상하지 못한 예외 - 로그만 하고 재발생하지 않음 (안전 모드)
                _logger.LogError(ex, "{OperationName} 중 예상하지 못한 오류 발생", operationName);
                return ValidationResult.CreateError($"예상하지 못한 오류가 발생했습니다: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 복구 불가능한 예외 처리 - 로그 기록 후 재발생
        /// 사용 시나리오: OOM, 시스템 오류, 치명적 데이터 손상 등
        /// </summary>
        public async Task<ValidationResult> ExecuteAsync(
            Func<Task<ValidationResult>> action,
            string operationName = "작업")
        {
            try
            {
                return await action();
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 메모리 부족 (OOM)", operationName);
                // OOM은 복구 불가능하므로 재발생
                throw;
            }
            catch (StackOverflowException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 스택 오버플로우", operationName);
                // 스택 오버플로우는 복구 불가능하므로 재발생
                throw;
            }
            catch (AccessViolationException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 메모리 액세스 위반", operationName);
                // 메모리 액세스 위반은 복구 불가능하므로 재발생
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "{OperationName} 실패: 잘못된 작업", operationName);
                return ValidationResult.CreateError($"잘못된 작업: {ex.Message}", ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "{OperationName} 실패: 잘못된 인수", operationName);
                return ValidationResult.CreateError($"잘못된 인수: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // 기타 예외는 로그만 기록하고 오류 결과 반환
                _logger.LogError(ex, "{OperationName} 중 오류 발생", operationName);
                return ValidationResult.CreateError($"오류가 발생했습니다: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 동기 작업에 대한 복구 가능한 예외 처리
        /// </summary>
        public ValidationResult SafeExecute(
            Func<ValidationResult> action,
            string operationName = "작업")
        {
            try
            {
                return action();
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 파일을 찾을 수 없습니다 - {FilePath}",
                    operationName, ex.FileName ?? "Unknown");
                return ValidationResult.CreateFileNotFound(ex.FileName ?? string.Empty);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 디렉토리를 찾을 수 없습니다",
                    operationName);
                return ValidationResult.CreateError("디렉토리를 찾을 수 없습니다", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 파일 접근 권한이 없습니다",
                    operationName);
                return ValidationResult.CreateError("파일 접근 권한이 없습니다", ex);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{OperationName}이 취소되었습니다", operationName);
                return ValidationResult.CreateCancelled($"{operationName}이 취소되었습니다");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: 작업 시간 초과", operationName);
                return ValidationResult.CreateError("작업 시간이 초과되었습니다", ex);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "{OperationName} 실패: I/O 오류 발생", operationName);
                return ValidationResult.CreateError($"I/O 오류가 발생했습니다: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{OperationName} 중 예상하지 못한 오류 발생", operationName);
                return ValidationResult.CreateError($"예상하지 못한 오류가 발생했습니다: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 동기 작업에 대한 복구 불가능한 예외 처리
        /// </summary>
        public ValidationResult Execute(
            Func<ValidationResult> action,
            string operationName = "작업")
        {
            try
            {
                return action();
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 메모리 부족 (OOM)", operationName);
                throw;
            }
            catch (StackOverflowException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 스택 오버플로우", operationName);
                throw;
            }
            catch (AccessViolationException ex)
            {
                _logger.LogCritical(ex, "{OperationName} 실패: 메모리 액세스 위반", operationName);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "{OperationName} 실패: 잘못된 작업", operationName);
                return ValidationResult.CreateError($"잘못된 작업: {ex.Message}", ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "{OperationName} 실패: 잘못된 인수", operationName);
                return ValidationResult.CreateError($"잘못된 인수: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{OperationName} 중 오류 발생", operationName);
                return ValidationResult.CreateError($"오류가 발생했습니다: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 일반 작업에 대한 안전한 실행 (T 반환)
        /// </summary>
        public async Task<T?> SafeExecuteAsync<T>(
            Func<Task<T>> action,
            string operationName = "작업",
            T? defaultValue = default) where T : class
        {
            try
            {
                return await action();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{OperationName}이 취소되었습니다", operationName);
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{OperationName} 중 오류 발생", operationName);
                return defaultValue;
            }
        }

        /// <summary>
        /// 일반 작업에 대한 안전한 실행 (동기, T 반환)
        /// </summary>
        public T? SafeExecute<T>(
            Func<T> action,
            string operationName = "작업",
            T? defaultValue = default) where T : class
        {
            try
            {
                return action();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{OperationName}이 취소되었습니다", operationName);
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{OperationName} 중 오류 발생", operationName);
                return defaultValue;
            }
        }
    }
}
