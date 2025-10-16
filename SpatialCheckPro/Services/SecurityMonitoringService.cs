using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 보안 이벤트 모니터링 및 알림을 담당하는 서비스 구현체
    /// </summary>
    public class SecurityMonitoringService : ISecurityMonitoringService, IDisposable
    {
        private readonly ILogger<SecurityMonitoringService> _logger;
        private readonly IAuditLogService _auditLogService;
        private readonly Timer _monitoringTimer;
        private readonly object _lockObject = new object();

        // 모니터링 상태
        private bool _isMonitoring = false;
        private readonly Dictionary<string, DateTime> _fileAccessHistory = new();
        private readonly Dictionary<string, int> _failureAttempts = new();

        // 알림 설정
        private Action<SecurityAlert> _alertCallback = _ => { };
        private SecurityAlertType[] _alertTypes = Array.Empty<SecurityAlertType>();

        // 보안 정책 설정
        private readonly Dictionary<string, object> _securityPolicies = new()
        {
            { "MaxFileAccessPerMinute", 100 },
            { "MaxFailureAttempts", 5 },
            { "CpuUsageThreshold", 80.0 },
            { "MemoryUsageThreshold", 85.0 },
            { "DiskUsageThreshold", 90.0 },
            { "AllowedWorkingHours", new { Start = 6, End = 22 } }
        };

        // 통계 데이터
        private readonly Dictionary<SecurityEventType, int> _eventCounts = new();
        private readonly List<SecurityEvent> _recentEvents = new();

        public SecurityMonitoringService(ILogger<SecurityMonitoringService> logger, IAuditLogService auditLogService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));

            // 5분마다 모니터링 실행
            _monitoringTimer = new Timer(PerformPeriodicMonitoring, null, Timeout.Infinite, (int)TimeSpan.FromMinutes(5).TotalMilliseconds);

            _logger.LogInformation("보안 모니터링 서비스 초기화 완료");
        }

        /// <summary>
        /// 보안 모니터링을 시작합니다
        /// </summary>
        public async Task StartMonitoringAsync()
        {
            try
            {
                lock (_lockObject)
                {
                    if (_isMonitoring)
                    {
                        _logger.LogWarning("보안 모니터링이 이미 실행 중입니다");
                        return;
                    }

                    _isMonitoring = true;
                }

                // 타이머 시작
                _monitoringTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(5));

                await _auditLogService.LogSystemEventAsync(SystemEventType.ApplicationStarted, "보안 모니터링 시작");

                _logger.LogInformation("보안 모니터링이 시작되었습니다");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 모니터링 시작 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 보안 모니터링을 중지합니다
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            try
            {
                lock (_lockObject)
                {
                    if (!_isMonitoring)
                    {
                        _logger.LogWarning("보안 모니터링이 실행 중이 아닙니다");
                        return;
                    }

                    _isMonitoring = false;
                }

                // 타이머 중지
                _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);

                await _auditLogService.LogSystemEventAsync(SystemEventType.ApplicationStopped, "보안 모니터링 중지");

                _logger.LogInformation("보안 모니터링이 중지되었습니다");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 모니터링 중지 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 파일 접근 패턴을 분석하여 이상 행위를 감지합니다
        /// </summary>
        public async Task<AnomalyDetectionResult> AnalyzeFileAccessPatternAsync(string filePath, FileAccessAction action)
        {
            try
            {
                var result = new AnomalyDetectionResult { IsAnomalous = false, RiskScore = 0 };

                // 1. 파일 접근 빈도 분석
                var accessFrequencyAnomaly = AnalyzeAccessFrequency(filePath);
                if (accessFrequencyAnomaly.IsAnomalous)
                {
                    result = accessFrequencyAnomaly;
                }

                // 2. 시간대 분석
                var timeAnomaly = AnalyzeAccessTime();
                if (timeAnomaly.IsAnomalous && timeAnomaly.RiskScore > result.RiskScore)
                {
                    result = timeAnomaly;
                }

                // 3. 파일 유형 분석
                var fileTypeAnomaly = AnalyzeFileType(filePath, action);
                if (fileTypeAnomaly.IsAnomalous && fileTypeAnomaly.RiskScore > result.RiskScore)
                {
                    result = fileTypeAnomaly;
                }

                // 이상 행위 감지 시 로깅
                if (result.IsAnomalous)
                {
                    await _auditLogService.LogSecurityEventAsync(
                        SecurityEventType.AnomalousActivity,
                        GetSeverityFromRiskScore(result.RiskScore),
                        $"파일 접근 이상 행위 감지: {result.Reason}",
                        new Dictionary<string, object>
                        {
                            { "FilePath", filePath },
                            { "Action", action.ToString() },
                            { "RiskScore", result.RiskScore },
                            { "AnomalyType", result.AnomalyType.ToString() }
                        });

                    _logger.LogWarning("파일 접근 이상 행위 감지: {FilePath} - {Reason} (위험도: {RiskScore})", 
                        filePath, result.Reason, result.RiskScore);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 접근 패턴 분석 중 오류 발생: {FilePath}", filePath);
                return new AnomalyDetectionResult
                {
                    IsAnomalous = true,
                    AnomalyType = AnomalyType.SuspiciousFileOperation,
                    RiskScore = 50,
                    Reason = "파일 접근 패턴 분석 중 오류 발생",
                    RecommendedAction = "시스템 관리자에게 문의하세요"
                };
            }
        }

        /// <summary>
        /// 시스템 리소스 사용량을 모니터링합니다
        /// </summary>
        public async Task<ResourceMonitoringResult> MonitorSystemResourcesAsync()
        {
            try
            {
                var result = new ResourceMonitoringResult
                {
                    Timestamp = DateTime.UtcNow
                };

                // CPU 사용률 측정
                using (var process = Process.GetCurrentProcess())
                {
                    var startTime = DateTime.UtcNow;
                    var startCpuUsage = process.TotalProcessorTime;
                    
                    await Task.Delay(1000); // 1초 대기
                    
                    var endTime = DateTime.UtcNow;
                    var endCpuUsage = process.TotalProcessorTime;
                    
                    var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                    var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    
                    result.CpuUsage = Math.Round(cpuUsageTotal * 100, 2);
                }

                // 메모리 사용률 측정
                var totalMemory = GC.GetTotalMemory(false);
                var workingSet = Environment.WorkingSet;
                result.MemoryUsage = Math.Round((double)workingSet / (1024 * 1024 * 1024) * 100, 2); // GB 단위

                // 디스크 사용률 측정
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                var totalSpace = drives.Sum(d => d.TotalSize);
                var freeSpace = drives.Sum(d => d.AvailableFreeSpace);
                result.DiskUsage = Math.Round((double)(totalSpace - freeSpace) / totalSpace * 100, 2);

                // 네트워크 활동 (기본값 설정)
                result.NetworkActivity = new NetworkActivity
                {
                    BytesSent = 0,
                    BytesReceived = 0,
                    ActiveConnections = 0,
                    SuspiciousConnections = 0
                };

                // 임계값 검사 및 경고 생성
                CheckResourceThresholds(result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "시스템 리소스 모니터링 중 오류 발생");
                return new ResourceMonitoringResult
                {
                    Timestamp = DateTime.UtcNow,
                    Warnings = { new ResourceWarning { ResourceType = "System", Message = "리소스 모니터링 오류 발생" } }
                };
            }
        }

        /// <summary>
        /// 보안 정책 위반을 검사합니다
        /// </summary>
        public async Task<PolicyViolationResult> CheckSecurityPolicyViolationAsync(string operation, Dictionary<string, object> context)
        {
            try
            {
                var result = new PolicyViolationResult { IsViolation = false };

                // 작업 시간 정책 검사
                var timeViolation = CheckWorkingHoursPolicy();
                if (timeViolation.IsViolation)
                {
                    result = timeViolation;
                }

                // 파일 접근 빈도 정책 검사
                var accessViolation = CheckFileAccessFrequencyPolicy(context);
                if (accessViolation.IsViolation && accessViolation.Severity > result.Severity)
                {
                    result = accessViolation;
                }

                // 정책 위반 시 로깅
                if (result.IsViolation)
                {
                    await _auditLogService.LogSecurityEventAsync(
                        SecurityEventType.PolicyViolation,
                        result.Severity,
                        $"보안 정책 위반: {result.Description}",
                        new Dictionary<string, object>
                        {
                            { "Operation", operation },
                            { "ViolatedPolicies", result.ViolatedPolicies },
                            { "Context", context }
                        });

                    _logger.LogWarning("보안 정책 위반 감지: {Operation} - {Description}", operation, result.Description);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 정책 위반 검사 중 오류 발생: {Operation}", operation);
                return new PolicyViolationResult
                {
                    IsViolation = true,
                    Severity = SecuritySeverity.High,
                    Description = "보안 정책 검사 중 오류 발생",
                    RecommendedAction = "시스템 관리자에게 문의하세요"
                };
            }
        }

        /// <summary>
        /// 실시간 보안 알림을 설정합니다
        /// </summary>
        public void SetSecurityAlerts(SecurityAlertType[] alertTypes, Action<SecurityAlert> alertCallback)
        {
            _alertTypes = alertTypes ?? Array.Empty<SecurityAlertType>();
            _alertCallback = alertCallback;

            _logger.LogInformation("보안 알림 설정 완료: {AlertTypes}", string.Join(", ", _alertTypes));
        }

        /// <summary>
        /// 보안 대시보드 데이터를 가져옵니다
        /// </summary>
        public async Task<SecurityDashboardData> GetSecurityDashboardDataAsync()
        {
            try
            {
                var today = DateTime.Today;
                var todayEvents = _recentEvents.Count(e => e.Timestamp.Date == today);

                var dashboardData = new SecurityDashboardData
                {
                    OverallStatus = DetermineOverallSecurityStatus(),
                    TodaySecurityEvents = todayEvents,
                    ActiveAlerts = _recentEvents.Count(e => e.Severity >= SecuritySeverity.High),
                    RecentRiskEvents = _recentEvents.Where(e => e.Severity >= SecuritySeverity.Medium)
                                                   .OrderByDescending(e => e.Timestamp)
                                                   .Take(10)
                                                   .ToList(),
                    SystemResources = await MonitorSystemResourcesAsync(),
                    SecurityScore = CalculateSecurityScore(),
                    LastUpdated = DateTime.UtcNow
                };

                return dashboardData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 대시보드 데이터 조회 중 오류 발생");
                return new SecurityDashboardData
                {
                    OverallStatus = SecurityStatus.Critical,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// 보안 이벤트 통계를 가져옵니다
        /// </summary>
        public async Task<SecurityEventStatistics> GetSecurityEventStatisticsAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var events = _recentEvents.Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate).ToList();

                var statistics = new SecurityEventStatistics
                {
                    TotalEvents = events.Count
                };

                // 심각도별 통계
                foreach (SecuritySeverity severity in Enum.GetValues<SecuritySeverity>())
                {
                    statistics.EventsBySeverity[severity] = events.Count(e => e.Severity == severity);
                }

                // 유형별 통계
                foreach (SecurityEventType eventType in Enum.GetValues<SecurityEventType>())
                {
                    statistics.EventsByType[eventType] = events.Count(e => e.EventType == eventType);
                }

                // 일별 추이
                var dailyGroups = events.GroupBy(e => e.Timestamp.Date);
                foreach (var group in dailyGroups)
                {
                    statistics.DailyEventTrend[group.Key] = group.Count();
                }

                // 빈발 이벤트
                var frequentEvents = events.GroupBy(e => e.EventType)
                                          .Select(g => new SecurityEventFrequency
                                          {
                                              EventType = g.Key,
                                              Count = g.Count(),
                                              LastOccurrence = g.Max(e => e.Timestamp)
                                          })
                                          .OrderByDescending(f => f.Count)
                                          .Take(5)
                                          .ToList();

                statistics.MostFrequentEvents = frequentEvents;

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 이벤트 통계 조회 중 오류 발생");
                return new SecurityEventStatistics();
            }
        }

        /// <summary>
        /// 주기적 모니터링을 수행합니다
        /// </summary>
        private async void PerformPeriodicMonitoring(object state)
        {
            if (!_isMonitoring)
                return;

            try
            {
                // 시스템 리소스 모니터링
                var resourceResult = await MonitorSystemResourcesAsync();
                
                // 리소스 경고가 있으면 알림 발송
                foreach (var warning in resourceResult.Warnings)
                {
                    await SendSecurityAlert(SecurityAlertType.ResourceThresholdExceeded, SecuritySeverity.Warning,
                        $"리소스 임계값 초과: {warning.ResourceType}", warning.Message);
                }

                // 실패 시도 기록 정리 (1시간 이상 된 것들)
                CleanupFailureAttempts();

                _logger.LogDebug("주기적 보안 모니터링 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "주기적 보안 모니터링 중 오류 발생");
            }
        }

        /// <summary>
        /// 파일 접근 빈도를 분석합니다
        /// </summary>
        private AnomalyDetectionResult AnalyzeAccessFrequency(string filePath)
        {
            var now = DateTime.UtcNow;
            var key = $"{filePath}_{now:yyyyMMddHHmm}";

            lock (_lockObject)
            {
                if (_fileAccessHistory.ContainsKey(key))
                {
                    var accessCount = _fileAccessHistory.Count(kvp => kvp.Key.StartsWith(filePath) && 
                                                                     (now - kvp.Value).TotalMinutes <= 1);

                    var maxAccess = (int)_securityPolicies["MaxFileAccessPerMinute"];
                    if (accessCount > maxAccess)
                    {
                        return new AnomalyDetectionResult
                        {
                            IsAnomalous = true,
                            AnomalyType = AnomalyType.UnusualFileAccessPattern,
                            RiskScore = Math.Min(100, accessCount * 2),
                            Reason = $"1분 내 과도한 파일 접근 ({accessCount}회)",
                            RecommendedAction = "파일 접근 패턴을 검토하세요"
                        };
                    }
                }

                _fileAccessHistory[key] = now;
            }

            return new AnomalyDetectionResult { IsAnomalous = false };
        }

        /// <summary>
        /// 접근 시간을 분석합니다
        /// </summary>
        private AnomalyDetectionResult AnalyzeAccessTime()
        {
            var now = DateTime.Now;
            var workingHours = _securityPolicies["AllowedWorkingHours"];
            
            // 간단한 시간 검사 (실제로는 더 복잡한 로직 필요)
            if (now.Hour < 6 || now.Hour > 22)
            {
                return new AnomalyDetectionResult
                {
                    IsAnomalous = true,
                    AnomalyType = AnomalyType.UnusualTimeActivity,
                    RiskScore = 30,
                    Reason = "비정상적인 시간대 활동",
                    RecommendedAction = "업무 시간 외 활동을 검토하세요"
                };
            }

            return new AnomalyDetectionResult { IsAnomalous = false };
        }

        /// <summary>
        /// 파일 유형을 분석합니다
        /// </summary>
        private AnomalyDetectionResult AnalyzeFileType(string filePath, FileAccessAction action)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var suspiciousExtensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js" };

            if (suspiciousExtensions.Contains(extension))
            {
                return new AnomalyDetectionResult
                {
                    IsAnomalous = true,
                    AnomalyType = AnomalyType.SuspiciousFileOperation,
                    RiskScore = 70,
                    Reason = $"의심스러운 파일 형식 접근: {extension}",
                    RecommendedAction = "파일 내용을 검토하고 필요시 격리하세요"
                };
            }

            return new AnomalyDetectionResult { IsAnomalous = false };
        }

        /// <summary>
        /// 리소스 임계값을 검사합니다
        /// </summary>
        private void CheckResourceThresholds(ResourceMonitoringResult result)
        {
            var cpuThreshold = (double)_securityPolicies["CpuUsageThreshold"];
            var memoryThreshold = (double)_securityPolicies["MemoryUsageThreshold"];
            var diskThreshold = (double)_securityPolicies["DiskUsageThreshold"];

            if (result.CpuUsage > cpuThreshold)
            {
                result.Warnings.Add(new ResourceWarning
                {
                    ResourceType = "CPU",
                    CurrentUsage = result.CpuUsage,
                    Threshold = cpuThreshold,
                    Message = $"CPU 사용률이 임계값을 초과했습니다 ({result.CpuUsage:F1}% > {cpuThreshold}%)"
                });
            }

            if (result.MemoryUsage > memoryThreshold)
            {
                result.Warnings.Add(new ResourceWarning
                {
                    ResourceType = "Memory",
                    CurrentUsage = result.MemoryUsage,
                    Threshold = memoryThreshold,
                    Message = $"메모리 사용률이 임계값을 초과했습니다 ({result.MemoryUsage:F1}% > {memoryThreshold}%)"
                });
            }

            if (result.DiskUsage > diskThreshold)
            {
                result.Warnings.Add(new ResourceWarning
                {
                    ResourceType = "Disk",
                    CurrentUsage = result.DiskUsage,
                    Threshold = diskThreshold,
                    Message = $"디스크 사용률이 임계값을 초과했습니다 ({result.DiskUsage:F1}% > {diskThreshold}%)"
                });
            }
        }

        /// <summary>
        /// 작업 시간 정책을 검사합니다
        /// </summary>
        private PolicyViolationResult CheckWorkingHoursPolicy()
        {
            var now = DateTime.Now;
            if (now.Hour < 6 || now.Hour > 22)
            {
                return new PolicyViolationResult
                {
                    IsViolation = true,
                    ViolatedPolicies = { "AllowedWorkingHours" },
                    Severity = SecuritySeverity.Low,
                    Description = "허용된 작업 시간을 벗어난 활동",
                    RecommendedAction = "업무 시간 내에 작업하세요"
                };
            }

            return new PolicyViolationResult { IsViolation = false };
        }

        /// <summary>
        /// 파일 접근 빈도 정책을 검사합니다
        /// </summary>
        private PolicyViolationResult CheckFileAccessFrequencyPolicy(Dictionary<string, object> context)
        {
            // 구현 생략 (실제로는 더 복잡한 로직 필요)
            return new PolicyViolationResult { IsViolation = false };
        }

        /// <summary>
        /// 보안 알림을 발송합니다
        /// </summary>
        private async Task SendSecurityAlert(SecurityAlertType alertType, SecuritySeverity severity, string title, string message)
        {
            if (_alertCallback == null || !_alertTypes.Contains(alertType))
                return;

            try
            {
                var alert = new SecurityAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    AlertType = alertType,
                    Severity = severity,
                    Title = title,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };

                await Task.Run(() => _alertCallback(alert));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "보안 알림 발송 중 오류 발생: {AlertType}", alertType);
            }
        }

        /// <summary>
        /// 전체 보안 상태를 결정합니다
        /// </summary>
        private SecurityStatus DetermineOverallSecurityStatus()
        {
            var recentCriticalEvents = _recentEvents.Count(e => e.Severity == SecuritySeverity.Critical && 
                                                               (DateTime.UtcNow - e.Timestamp).TotalHours <= 24);
            var recentHighEvents = _recentEvents.Count(e => e.Severity == SecuritySeverity.High && 
                                                           (DateTime.UtcNow - e.Timestamp).TotalHours <= 24);

            if (recentCriticalEvents > 0)
                return SecurityStatus.Critical;
            if (recentHighEvents > 3)
                return SecurityStatus.Risk;
            if (recentHighEvents > 0)
                return SecurityStatus.Warning;

            return SecurityStatus.Secure;
        }

        /// <summary>
        /// 보안 점수를 계산합니다
        /// </summary>
        private int CalculateSecurityScore()
        {
            var baseScore = 100;
            var recentEvents = _recentEvents.Where(e => (DateTime.UtcNow - e.Timestamp).TotalDays <= 7);

            foreach (var evt in recentEvents)
            {
                baseScore -= evt.Severity switch
                {
                    SecuritySeverity.Critical => 20,
                    SecuritySeverity.High => 10,
                    SecuritySeverity.Medium => 5,
                    SecuritySeverity.Low => 2,
                    _ => 0
                };
            }

            return Math.Max(0, baseScore);
        }

        /// <summary>
        /// 위험 점수에서 심각도를 결정합니다
        /// </summary>
        private SecuritySeverity GetSeverityFromRiskScore(int riskScore)
        {
            return riskScore switch
            {
                >= 80 => SecuritySeverity.Critical,
                >= 60 => SecuritySeverity.High,
                >= 40 => SecuritySeverity.Medium,
                >= 20 => SecuritySeverity.Low,
                _ => SecuritySeverity.Info
            };
        }

        /// <summary>
        /// 실패 시도 기록을 정리합니다
        /// </summary>
        private void CleanupFailureAttempts()
        {
            lock (_lockObject)
            {
                var keysToRemove = _fileAccessHistory.Where(kvp => (DateTime.UtcNow - kvp.Value).TotalHours > 1)
                                                    .Select(kvp => kvp.Key)
                                                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _fileAccessHistory.Remove(key);
                }
            }
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _logger.LogInformation("보안 모니터링 서비스가 종료되었습니다");
        }
    }
}
