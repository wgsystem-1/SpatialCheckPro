using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using MSLogger = Microsoft.Extensions.Logging.ILogger;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 로깅 시스템 설정 및 관리를 담당하는 서비스 클래스
    /// </summary>
    public static class LoggingService
    {
        /// <summary>
        /// 로그 파일이 저장될 기본 디렉토리
        /// </summary>
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeoSpatialValidationSystem",
            "Logs"
        );

        /// <summary>
        /// Serilog 로거를 설정하고 초기화합니다
        /// </summary>
        /// <param name="minimumLevel">최소 로그 레벨</param>
        /// <returns>설정된 로거</returns>
        public static Serilog.ILogger ConfigureSerilog(LogEventLevel minimumLevel = LogEventLevel.Information)
        {
            // 로그 디렉토리 생성
            Directory.CreateDirectory(LogDirectory);

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "GeoSpatialValidationSystem")
                .Enrich.WithProperty("Version", GetApplicationVersion())
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .Enrich.WithProperty("UserName", Environment.UserName);

            // 기본 로깅만 사용 (제거된 Serilog 패키지들로 인한 오류 방지)
            // 추후 필요시 GUI 프로젝트에서 고급 로깅 기능 구현

            var logger = loggerConfiguration.CreateLogger();
            
            // Serilog를 전역 로거로 설정
            Log.Logger = logger;

            logger.Information("로깅 시스템이 초기화되었습니다. 로그 디렉토리: {LogDirectory}", LogDirectory);

            return logger;
        }

        /// <summary>
        /// Microsoft.Extensions.Logging과 Serilog를 연결하는 로거 팩토리를 생성합니다
        /// </summary>
        /// <param name="serilogLogger">Serilog 로거 인스턴스</param>
        /// <returns>로거 팩토리</returns>
        public static ILoggerFactory CreateLoggerFactory(Serilog.ILogger serilogLogger)
        {
            return LoggerFactory.Create(builder =>
            {
                builder.AddSerilog(serilogLogger, dispose: false);
            });
        }

        /// <summary>
        /// 검수 작업 시작을 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="validationId">검수 ID</param>
        /// <param name="filePath">검수 대상 파일 경로</param>
        /// <param name="fileSize">파일 크기</param>
        public static void LogValidationStarted(MSLogger logger, string validationId, string filePath, long fileSize)
        {
            logger.LogInformation("검수 작업이 시작되었습니다. ID: {ValidationId}, 파일: {FilePath}, 크기: {FileSize:N0} bytes",
                validationId, filePath, fileSize);
        }

        /// <summary>
        /// 검수 단계 시작을 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="validationId">검수 ID</param>
        /// <param name="stage">검수 단계</param>
        /// <param name="stageName">단계명</param>
        public static void LogValidationStageStarted(MSLogger logger, string validationId, int stage, string stageName)
        {
            logger.LogInformation("검수 {Stage}단계({StageName})가 시작되었습니다. ID: {ValidationId}",
                stage, stageName, validationId);
        }

        /// <summary>
        /// 검수 단계 완료를 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="validationId">검수 ID</param>
        /// <param name="stage">검수 단계</param>
        /// <param name="stageName">단계명</param>
        /// <param name="duration">소요 시간</param>
        /// <param name="errorCount">오류 개수</param>
        /// <param name="warningCount">경고 개수</param>
        public static void LogValidationStageCompleted(MSLogger logger, string validationId, int stage, string stageName, 
            TimeSpan duration, int errorCount, int warningCount)
        {
            logger.LogInformation("검수 {Stage}단계({StageName})가 완료되었습니다. ID: {ValidationId}, " +
                "소요시간: {Duration:mm\\:ss\\.fff}, 오류: {ErrorCount}, 경고: {WarningCount}",
                stage, stageName, validationId, duration, errorCount, warningCount);
        }

        /// <summary>
        /// 검수 작업 완료를 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="validationId">검수 ID</param>
        /// <param name="totalDuration">전체 소요 시간</param>
        /// <param name="totalErrors">전체 오류 개수</param>
        /// <param name="totalWarnings">전체 경고 개수</param>
        /// <param name="status">최종 상태</param>
        public static void LogValidationCompleted(MSLogger logger, string validationId, TimeSpan totalDuration, 
            int totalErrors, int totalWarnings, string status)
        {
            logger.LogInformation("검수 작업이 완료되었습니다. ID: {ValidationId}, 상태: {Status}, " +
                "전체 소요시간: {TotalDuration:mm\\:ss\\.fff}, 총 오류: {TotalErrors}, 총 경고: {TotalWarnings}",
                validationId, status, totalDuration, totalErrors, totalWarnings);
        }

        /// <summary>
        /// 파일 접근 작업을 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="operation">파일 작업 유형</param>
        /// <param name="filePath">파일 경로</param>
        /// <param name="success">성공 여부</param>
        /// <param name="duration">소요 시간</param>
        public static void LogFileAccess(MSLogger logger, string operation, string filePath, bool success, TimeSpan? duration = null)
        {
            if (success)
            {
                var message = duration.HasValue 
                    ? "파일 {Operation} 작업이 성공했습니다. 경로: {FilePath}, 소요시간: {Duration:mm\\:ss\\.fff}"
                    : "파일 {Operation} 작업이 성공했습니다. 경로: {FilePath}";
                
                if (duration.HasValue)
                    logger.LogDebug(message, operation, filePath, duration.Value);
                else
                    logger.LogDebug(message, operation, filePath);
            }
            else
            {
                logger.LogWarning("파일 {Operation} 작업이 실패했습니다. 경로: {FilePath}", operation, filePath);
            }
        }

        /// <summary>
        /// 성능 메트릭을 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="operation">작업명</param>
        /// <param name="duration">소요 시간</param>
        /// <param name="itemCount">처리된 항목 수</param>
        /// <param name="memoryUsage">메모리 사용량 (바이트)</param>
        public static void LogPerformanceMetrics(MSLogger logger, string operation, TimeSpan duration, 
            int itemCount = 0, long memoryUsage = 0)
        {
            var throughput = itemCount > 0 && duration.TotalSeconds > 0 
                ? itemCount / duration.TotalSeconds 
                : 0;

            logger.LogDebug("성능 메트릭 - 작업: {Operation}, 소요시간: {Duration:mm\\:ss\\.fff}, " +
                "처리 항목: {ItemCount}, 처리율: {Throughput:F2} items/sec, 메모리: {MemoryUsage:N0} bytes",
                operation, duration, itemCount, throughput, memoryUsage);
        }

        /// <summary>
        /// 사용자 액션을 로깅합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        /// <param name="action">사용자 액션</param>
        /// <param name="details">추가 세부사항</param>
        public static void LogUserAction(MSLogger logger, string action, object? details = null)
        {
            if (details != null)
            {
                logger.LogInformation("사용자 액션: {Action}, 세부사항: {@Details}", action, details);
            }
            else
            {
                logger.LogInformation("사용자 액션: {Action}", action);
            }
        }

        /// <summary>
        /// 애플리케이션 버전을 가져옵니다
        /// </summary>
        /// <returns>애플리케이션 버전</returns>
        private static string GetApplicationVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// 로그 파일 정리를 수행합니다
        /// </summary>
        /// <param name="logger">로거 인스턴스</param>
        public static void CleanupOldLogFiles(MSLogger logger)
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-90); // 90일 이전 파일 삭제
                var logFiles = Directory.GetFiles(LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(LogDirectory, "*.json", SearchOption.TopDirectoryOnly));

                var deletedCount = 0;
                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "로그 파일 삭제 실패: {FilePath}", file);
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    logger.LogInformation("오래된 로그 파일 {DeletedCount}개를 정리했습니다.", deletedCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "로그 파일 정리 중 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// 로깅 시스템을 종료하고 리소스를 정리합니다
        /// </summary>
        public static void Shutdown()
        {
            Log.Information("로깅 시스템을 종료합니다.");
            Log.CloseAndFlush();
        }
    }
}
