#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// 검수별 파일 로거 프로바이더 (개선: 검수 파일별 개별 로그)
    /// </summary>
    public class FileLoggerProvider : ILoggerProvider
    {
        private static string? _currentLogFilePath;
        private static readonly object _logPathLock = new object();

        /// <summary>
        /// 기본 생성자 - 애플리케이션 시작 로그 사용
        /// </summary>
        public FileLoggerProvider()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var logsDirectory = Path.Combine(appDirectory, "Logs");
            
            // Logs 디렉토리 생성
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            // 기본 로그 파일: application_년월일시.log
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _currentLogFilePath = Path.Combine(logsDirectory, $"application_{timestamp}.log");
            
            // 오래된 로그 파일 자동 정리 (30일 이상)
            CleanupOldLogFiles(logsDirectory, 30);
        }

        /// <summary>
        /// 파일 경로를 지정하는 생성자
        /// </summary>
        public FileLoggerProvider(string filePath)
        {
            lock (_logPathLock)
            {
                _currentLogFilePath = filePath;
            }
            
            // 로그 파일 디렉토리 확인 및 생성
            var directory = Path.GetDirectoryName(_currentLogFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// 검수 파일별 로그 파일 경로 설정
        /// </summary>
        /// <param name="gdbFilePath">검수 대상 GDB 파일 경로</param>
        public static void SetLogFileForValidation(string gdbFilePath)
        {
            lock (_logPathLock)
            {
                try
                {
                    var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var logsDirectory = Path.Combine(appDirectory, "Logs");
                    
                    // Logs 디렉토리 생성
                    if (!Directory.Exists(logsDirectory))
                    {
                        Directory.CreateDirectory(logsDirectory);
                    }
                    
                    // 검수 파일명 추출
                    var fileName = Path.GetFileNameWithoutExtension(gdbFilePath);
                    if (fileName.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                    }
                    
                    // 파일명 정제 (특수문자 제거)
                    var invalidChars = Path.GetInvalidFileNameChars();
                    fileName = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
                    
                    // 타임스탬프 추가
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var logFileName = $"{fileName}_{timestamp}.log";
                    
                    _currentLogFilePath = Path.Combine(logsDirectory, logFileName);
                    
                    Console.WriteLine($"[로그] 검수별 로그 파일 설정: {_currentLogFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[로그 오류] 로그 파일 경로 설정 실패: {ex.Message}");
                    // 실패 시 기본 로그 파일 사용
                    var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var logsDirectory = Path.Combine(appDirectory, "Logs");
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    _currentLogFilePath = Path.Combine(logsDirectory, $"validation_{timestamp}.log");
                }
            }
        }

        /// <summary>
        /// 오래된 로그 파일 자동 정리
        /// </summary>
        private void CleanupOldLogFiles(string logsDirectory, int retentionDays)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-retentionDays);
                var logFiles = Directory.GetFiles(logsDirectory, "*.log");
                
                int deletedCount = 0;
                foreach (var logFile in logFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(logFile);
                        if (fileInfo.LastWriteTime < cutoffDate)
                        {
                            File.Delete(logFile);
                            deletedCount++;
                        }
                    }
                    catch
                    {
                        // 개별 파일 삭제 실패는 무시
                    }
                }
                
                if (deletedCount > 0)
                {
                    Console.WriteLine($"[로그] {retentionDays}일 이상 오래된 로그 파일 {deletedCount}개 삭제");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[로그 정리 오류] {ex.Message}");
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            // 동적 파일 전환 반영: FileLogger가 매 로그 시점에 현재 경로를 참조하도록 provider를 전달
            lock (_logPathLock)
            {
                return new FileLogger(() =>
                {
                    lock (_logPathLock)
                    {
                        return _currentLogFilePath ?? "debug.log";
                    }
                }, categoryName);
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 간단한 파일 로거
    /// </summary>
    public class FileLogger : ILogger
    {
        private readonly Func<string> _filePathResolver;
        private readonly string _categoryName;
        private readonly object _lock = new object();

        public FileLogger(Func<string> filePathResolver, string categoryName)
        {
            _filePathResolver = filePathResolver;
            _categoryName = categoryName;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {message}";
            
            if (exception != null)
            {
                logEntry += Environment.NewLine + exception.ToString();
            }

            lock (_lock)
            {
                try
                {
                    var path = _filePathResolver();
                    // 디렉토리 존재 보장
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    // UTF-8(BOM)로 기록하고, 다른 프로세스/스레드에서 읽을 수 있도록 공유 모드 허용
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    writer.WriteLine(logEntry);
                    writer.Flush();
                }
                catch
                {
                    // 로그 쓰기 실패 시 무시
                }
            }
        }
    }
}