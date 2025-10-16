using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 감사 로깅을 담당하는 서비스 구현체
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly ILogger<AuditLogService> _logger;
        private readonly IDataProtectionService _dataProtectionService;
        private readonly string _auditLogPath;
        private readonly string _sessionId;
        private readonly object _lockObject = new object();

        // 보안 이벤트 알림 콜백
        private Action<SecurityEventNotification> _securityNotificationCallback = _ => { };
        private SecurityEventType[] _notificationEventTypes = Array.Empty<SecurityEventType>();

        public AuditLogService(ILogger<AuditLogService> logger, IDataProtectionService dataProtectionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataProtectionService = dataProtectionService ?? throw new ArgumentNullException(nameof(dataProtectionService));

            // 감사 로그 저장 경로 설정
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var auditLogDir = Path.Combine(appDataPath, "GeoSpatialValidationSystem", "AuditLogs");
            Directory.CreateDirectory(auditLogDir);
            
            _auditLogPath = Path.Combine(auditLogDir, $"audit_{DateTime.Now:yyyyMM}.log");
            _sessionId = Guid.NewGuid().ToString("N")[..8];

            _logger.LogInformation("감사 로그 서비스 초기화 완료. 세션 ID: {SessionId}, 로그 경로: {LogPath}", _sessionId, _auditLogPath);
        }

        /// <summary>
        /// 파일 접근 이벤트를 기록합니다
        /// </summary>
        public async Task LogFileAccessAsync(string filePath, FileAccessAction action, AccessResult result, Dictionary<string, object>? additionalInfo = null)
        {
            try
            {
                var logEntry = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    EventType = AuditEventType.FileAccess,
                    Timestamp = DateTime.UtcNow,
                    UserInfo = Environment.UserName,
                    SessionId = _sessionId,
                    Description = $"파일 {action} 작업: {result}",
                    TargetResource = filePath,
                    Result = result.ToString(),
                    Severity = GetSeverityForFileAccess(action, result),
                    IpAddress = GetLocalIpAddress(),
                    UserAgent = "GeoSpatialValidationSystem"
                };

                if (additionalInfo != null)
                {
                    foreach (var kvp in additionalInfo)
                    {
                        logEntry.Metadata[kvp.Key] = kvp.Value;
                    }
                }

                await WriteAuditLogAsync(logEntry);

                _logger.LogInformation("파일 접근 감사 로그 기록: {Action} {FilePath} - {Result}", action, filePath, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 접근 감사 로그 기록 중 오류 발생: {FilePath}", filePath);
            }
        }

        /// <summary>
        /// 검수 작업 이벤트를 기록합니다
        /// </summary>
        public async Task LogValidationEventAsync(string validationId, ValidationAction action, int? stage, ValidationEventResult result, Dictionary<string, object> additionalInfo = null)
        {
            try
            {
                var description = stage.HasValue 
                    ? $"검수 {action} (단계 {stage}): {result}"
                    : $"검수 {action}: {result}";

                var logEntry = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    EventType = AuditEventType.ValidationEvent,
                    Timestamp = DateTime.UtcNow,
                    UserInfo = Environment.UserName,
                    SessionId = _sessionId,
                    Description = description,
                    TargetResource = validationId,
                    Result = result.ToString(),
                    Severity = GetSeverityForValidation(action, result),
                    IpAddress = GetLocalIpAddress(),
                    UserAgent = "GeoSpatialValidationSystem"
                };

                if (stage.HasValue)
                {
                    logEntry.Metadata["Stage"] = stage.Value;
                }

                if (additionalInfo != null)
                {
                    foreach (var kvp in additionalInfo)
                    {
                        logEntry.Metadata[kvp.Key] = kvp.Value;
                    }
                }

                await WriteAuditLogAsync(logEntry);

                _logger.LogInformation("검수 작업 감사 로그 기록: {ValidationId} {Action} - {Result}", validationId, action, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 작업 감사 로그 기록 중 오류 발생: {ValidationId}", validationId);
            }
        }

        /// <summary>
        /// 보안 이벤트를 기록합니다
        /// </summary>
        public async Task LogSecurityEventAsync(SecurityEventType eventType, SecuritySeverity severity, string description, Dictionary<string, object> additionalInfo = null)
        {
            try
            {
                var logEntry = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    EventType = AuditEventType.SecurityEvent,
                    Timestamp = DateTime.UtcNow,
                    UserInfo = Environment.UserName,
                    SessionId = _sessionId,
                    Description = $"보안 이벤트 ({eventType}): {description}",
                    TargetResource = eventType.ToString(),
                    Result = "Detected",
                    Severity = severity.ToString(),
                    IpAddress = GetLocalIpAddress(),
                    UserAgent = "GeoSpatialValidationSystem"
                };

                logEntry.Metadata["SecurityEventType"] = eventType.ToString();
                logEntry.Metadata["SecuritySeverity"] = severity.ToString();

                if (additionalInfo != null)
                {
                    foreach (var kvp in additionalInfo)
                    {
                        logEntry.Metadata[kvp.Key] = kvp.Value;
                    }
                }

                await WriteAuditLogAsync(logEntry);

                // 보안 이벤트 알림 발송
                await SendSecurityNotificationAsync(eventType, severity, description, additionalInfo);

                _logger.LogWarning("보안 이벤트 감사 로그 기록: {EventType} ({Severity}) - {Description}", eventType, severity, description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 이벤트 감사 로그 기록 중 오류 발생: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 데이터 편집 이벤트를 기록합니다
        /// </summary>
        public async Task LogDataEditEventAsync(string filePath, string featureId, EditAction action, Dictionary<string, object> changes)
        {
            try
            {
                var logEntry = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    EventType = AuditEventType.DataEdit,
                    Timestamp = DateTime.UtcNow,
                    UserInfo = Environment.UserName,
                    SessionId = _sessionId,
                    Description = $"데이터 편집 {action}: 피처 {featureId}",
                    TargetResource = filePath,
                    Result = "Success",
                    Severity = "Info",
                    IpAddress = GetLocalIpAddress(),
                    UserAgent = "GeoSpatialValidationSystem"
                };

                logEntry.Metadata["FeatureId"] = featureId;
                logEntry.Metadata["EditAction"] = action.ToString();

                if (changes != null)
                {
                    logEntry.Metadata["Changes"] = changes;
                }

                await WriteAuditLogAsync(logEntry);

                _logger.LogInformation("데이터 편집 감사 로그 기록: {FilePath} 피처 {FeatureId} {Action}", filePath, featureId, action);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "데이터 편집 감사 로그 기록 중 오류 발생: {FilePath} 피처 {FeatureId}", filePath, featureId);
            }
        }

        /// <summary>
        /// 시스템 이벤트를 기록합니다
        /// </summary>
        public async Task LogSystemEventAsync(SystemEventType eventType, string description, Dictionary<string, object> additionalInfo = null)
        {
            try
            {
                var logEntry = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    EventType = AuditEventType.SystemEvent,
                    Timestamp = DateTime.UtcNow,
                    UserInfo = Environment.UserName,
                    SessionId = _sessionId,
                    Description = $"시스템 이벤트 ({eventType}): {description}",
                    TargetResource = "System",
                    Result = "Info",
                    Severity = GetSeverityForSystemEvent(eventType),
                    IpAddress = GetLocalIpAddress(),
                    UserAgent = "GeoSpatialValidationSystem"
                };

                logEntry.Metadata["SystemEventType"] = eventType.ToString();

                if (additionalInfo != null)
                {
                    foreach (var kvp in additionalInfo)
                    {
                        logEntry.Metadata[kvp.Key] = kvp.Value;
                    }
                }

                await WriteAuditLogAsync(logEntry);

                _logger.LogInformation("시스템 이벤트 감사 로그 기록: {EventType} - {Description}", eventType, description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "시스템 이벤트 감사 로그 기록 중 오류 발생: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 감사 로그를 조회합니다
        /// </summary>
        public async Task<IEnumerable<AuditLogEntry>> GetAuditLogsAsync(DateTime? startDate = null, DateTime? endDate = null, AuditEventType[] eventTypes = null)
        {
            try
            {
                var logs = new List<AuditLogEntry>();

                // 현재 월과 이전 월 로그 파일들을 검색
                var logFiles = GetLogFiles(startDate, endDate);

                foreach (var logFile in logFiles)
                {
                    if (!File.Exists(logFile))
                        continue;

                    var lines = await File.ReadAllLinesAsync(logFile);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        try
                        {
                            var logEntry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                            
                            // 날짜 필터링
                            if (startDate.HasValue && logEntry.Timestamp < startDate.Value)
                                continue;
                            if (endDate.HasValue && logEntry.Timestamp > endDate.Value)
                                continue;

                            // 이벤트 유형 필터링
                            if (eventTypes != null && eventTypes.Length > 0 && !eventTypes.Contains(logEntry.EventType))
                                continue;

                            logs.Add(logEntry);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "감사 로그 파싱 오류: {Line}", line);
                        }
                    }
                }

                return logs.OrderByDescending(l => l.Timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "감사 로그 조회 중 오류 발생");
                return Enumerable.Empty<AuditLogEntry>();
            }
        }

        /// <summary>
        /// 보안 이벤트 알림을 설정합니다
        /// </summary>
        public void SetSecurityEventNotification(SecurityEventType[] eventTypes, Action<SecurityEventNotification> notificationCallback)
        {
            _notificationEventTypes = eventTypes ?? Array.Empty<SecurityEventType>();
            _securityNotificationCallback = notificationCallback;

            _logger.LogInformation("보안 이벤트 알림 설정 완료: {EventTypes}", string.Join(", ", _notificationEventTypes));
        }

        /// <summary>
        /// 감사 로그를 파일에 기록합니다
        /// </summary>
        private async Task WriteAuditLogAsync(AuditLogEntry logEntry)
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var logJson = JsonSerializer.Serialize(logEntry, jsonOptions);

                lock (_lockObject)
                {
                    File.AppendAllText(_auditLogPath, logJson + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "감사 로그 파일 쓰기 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 보안 이벤트 알림을 발송합니다
        /// </summary>
        private async Task SendSecurityNotificationAsync(SecurityEventType eventType, SecuritySeverity severity, string description, Dictionary<string, object> additionalInfo)
        {
            try
            {
                if (_securityNotificationCallback == null || !_notificationEventTypes.Contains(eventType))
                    return;

                var notification = new SecurityEventNotification
                {
                    EventType = eventType,
                    Severity = severity,
                    Timestamp = DateTime.UtcNow,
                    Description = description,
                    AdditionalInfo = additionalInfo ?? new Dictionary<string, object>()
                };

                // 비동기로 알림 발송 (UI 스레드 블로킹 방지)
                await Task.Run(() => _securityNotificationCallback(notification));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 이벤트 알림 발송 중 오류 발생: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 파일 접근 작업의 심각도를 결정합니다
        /// </summary>
        private string GetSeverityForFileAccess(FileAccessAction action, AccessResult result)
        {
            return result switch
            {
                AccessResult.Success => "Info",
                AccessResult.PartialSuccess => "Warning",
                AccessResult.Failed => "Warning",
                AccessResult.Denied => "High",
                _ => "Info"
            };
        }

        /// <summary>
        /// 검수 작업의 심각도를 결정합니다
        /// </summary>
        private string GetSeverityForValidation(ValidationAction action, ValidationEventResult result)
        {
            return result switch
            {
                ValidationEventResult.Success => "Info",
                ValidationEventResult.InProgress => "Info",
                ValidationEventResult.Warning => "Warning",
                ValidationEventResult.Failed => "High",
                _ => "Info"
            };
        }

        /// <summary>
        /// 시스템 이벤트의 심각도를 결정합니다
        /// </summary>
        private string GetSeverityForSystemEvent(SystemEventType eventType)
        {
            return eventType switch
            {
                SystemEventType.ErrorOccurred => "High",
                SystemEventType.PerformanceWarning => "Warning",
                SystemEventType.DatabaseDisconnected => "Warning",
                _ => "Info"
            };
        }

        /// <summary>
        /// 로컬 IP 주소를 가져옵니다
        /// </summary>
        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var localIp = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return localIp?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>
        /// 날짜 범위에 해당하는 로그 파일 목록을 가져옵니다
        /// </summary>
        private IEnumerable<string> GetLogFiles(DateTime? startDate, DateTime? endDate)
        {
            var logDir = Path.GetDirectoryName(_auditLogPath);
            var logFiles = new List<string>();

            var start = startDate ?? DateTime.Now.AddMonths(-3);
            var end = endDate ?? DateTime.Now;

            for (var date = new DateTime(start.Year, start.Month, 1); date <= end; date = date.AddMonths(1))
            {
                var logFile = Path.Combine(logDir, $"audit_{date:yyyyMM}.log");
                logFiles.Add(logFile);
            }

            return logFiles;
        }
    }
}
