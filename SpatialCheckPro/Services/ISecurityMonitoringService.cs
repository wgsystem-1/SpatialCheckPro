using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 보안 이벤트 모니터링 및 알림을 담당하는 서비스 인터페이스
    /// </summary>
    public interface ISecurityMonitoringService
    {
        /// <summary>
        /// 보안 모니터링을 시작합니다
        /// </summary>
        Task StartMonitoringAsync();

        /// <summary>
        /// 보안 모니터링을 중지합니다
        /// </summary>
        Task StopMonitoringAsync();

        /// <summary>
        /// 파일 접근 패턴을 분석하여 이상 행위를 감지합니다
        /// </summary>
        /// <param name="filePath">접근한 파일 경로</param>
        /// <param name="action">수행한 작업</param>
        /// <returns>이상 행위 감지 결과</returns>
        Task<AnomalyDetectionResult> AnalyzeFileAccessPatternAsync(string filePath, FileAccessAction action);

        /// <summary>
        /// 시스템 리소스 사용량을 모니터링합니다
        /// </summary>
        /// <returns>리소스 모니터링 결과</returns>
        Task<ResourceMonitoringResult> MonitorSystemResourcesAsync();

        /// <summary>
        /// 보안 정책 위반을 검사합니다
        /// </summary>
        /// <param name="operation">수행하려는 작업</param>
        /// <param name="context">작업 컨텍스트</param>
        /// <returns>정책 위반 검사 결과</returns>
        Task<PolicyViolationResult> CheckSecurityPolicyViolationAsync(string operation, Dictionary<string, object> context);

        /// <summary>
        /// 실시간 보안 알림을 설정합니다
        /// </summary>
        /// <param name="alertTypes">알림받을 보안 이벤트 유형</param>
        /// <param name="alertCallback">알림 콜백</param>
        void SetSecurityAlerts(SecurityAlertType[] alertTypes, Action<SecurityAlert> alertCallback);

        /// <summary>
        /// 보안 대시보드 데이터를 가져옵니다
        /// </summary>
        /// <returns>보안 대시보드 데이터</returns>
        Task<SecurityDashboardData> GetSecurityDashboardDataAsync();

        /// <summary>
        /// 보안 이벤트 통계를 가져옵니다
        /// </summary>
        /// <param name="startDate">시작 날짜</param>
        /// <param name="endDate">종료 날짜</param>
        /// <returns>보안 이벤트 통계</returns>
        Task<SecurityEventStatistics> GetSecurityEventStatisticsAsync(DateTime startDate, DateTime endDate);
    }

    /// <summary>
    /// 이상 행위 감지 결과
    /// </summary>
    public class AnomalyDetectionResult
    {
        /// <summary>이상 행위 감지 여부</summary>
        public bool IsAnomalous { get; set; }

        /// <summary>이상 행위 유형</summary>
        public AnomalyType AnomalyType { get; set; }

        /// <summary>위험 점수 (0-100)</summary>
        public int RiskScore { get; set; }

        /// <summary>감지 이유</summary>
        public string Reason { get; set; }

        /// <summary>권장 조치</summary>
        public string RecommendedAction { get; set; }

        /// <summary>추가 세부 정보</summary>
        public Dictionary<string, object> Details { get; set; } = new();
    }

    /// <summary>
    /// 리소스 모니터링 결과
    /// </summary>
    public class ResourceMonitoringResult
    {
        /// <summary>CPU 사용률 (%)</summary>
        public double CpuUsage { get; set; }

        /// <summary>메모리 사용률 (%)</summary>
        public double MemoryUsage { get; set; }

        /// <summary>디스크 사용률 (%)</summary>
        public double DiskUsage { get; set; }

        /// <summary>네트워크 활동</summary>
        public NetworkActivity NetworkActivity { get; set; }

        /// <summary>리소스 경고 목록</summary>
        public List<ResourceWarning> Warnings { get; set; } = new();

        /// <summary>모니터링 시간</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 정책 위반 검사 결과
    /// </summary>
    public class PolicyViolationResult
    {
        /// <summary>정책 위반 여부</summary>
        public bool IsViolation { get; set; }

        /// <summary>위반된 정책 목록</summary>
        public List<string> ViolatedPolicies { get; set; } = new();

        /// <summary>위반 심각도</summary>
        public SecuritySeverity Severity { get; set; }

        /// <summary>위반 설명</summary>
        public string Description { get; set; }

        /// <summary>권장 조치</summary>
        public string RecommendedAction { get; set; }
    }

    /// <summary>
    /// 보안 알림
    /// </summary>
    public class SecurityAlert
    {
        /// <summary>알림 ID</summary>
        public string AlertId { get; set; }

        /// <summary>알림 유형</summary>
        public SecurityAlertType AlertType { get; set; }

        /// <summary>심각도</summary>
        public SecuritySeverity Severity { get; set; }

        /// <summary>제목</summary>
        public string Title { get; set; }

        /// <summary>메시지</summary>
        public string Message { get; set; }

        /// <summary>발생 시간</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>관련 리소스</summary>
        public string RelatedResource { get; set; }

        /// <summary>권장 조치</summary>
        public string RecommendedAction { get; set; }

        /// <summary>추가 정보</summary>
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    /// <summary>
    /// 보안 대시보드 데이터
    /// </summary>
    public class SecurityDashboardData
    {
        /// <summary>전체 보안 상태</summary>
        public SecurityStatus OverallStatus { get; set; }

        /// <summary>오늘 발생한 보안 이벤트 수</summary>
        public int TodaySecurityEvents { get; set; }

        /// <summary>활성 보안 알림 수</summary>
        public int ActiveAlerts { get; set; }

        /// <summary>최근 위험 이벤트 목록</summary>
        public List<SecurityEvent> RecentRiskEvents { get; set; } = new();

        /// <summary>시스템 리소스 상태</summary>
        public ResourceMonitoringResult SystemResources { get; set; }

        /// <summary>보안 점수 (0-100)</summary>
        public int SecurityScore { get; set; }

        /// <summary>마지막 업데이트 시간</summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// 보안 이벤트 통계
    /// </summary>
    public class SecurityEventStatistics
    {
        /// <summary>총 이벤트 수</summary>
        public int TotalEvents { get; set; }

        /// <summary>심각도별 이벤트 수</summary>
        public Dictionary<SecuritySeverity, int> EventsBySeverity { get; set; } = new();

        /// <summary>유형별 이벤트 수</summary>
        public Dictionary<SecurityEventType, int> EventsByType { get; set; } = new();

        /// <summary>일별 이벤트 추이</summary>
        public Dictionary<DateTime, int> DailyEventTrend { get; set; } = new();

        /// <summary>가장 빈번한 보안 이벤트</summary>
        public List<SecurityEventFrequency> MostFrequentEvents { get; set; } = new();
    }

    /// <summary>
    /// 네트워크 활동 정보
    /// </summary>
    public class NetworkActivity
    {
        /// <summary>송신 바이트</summary>
        public long BytesSent { get; set; }

        /// <summary>수신 바이트</summary>
        public long BytesReceived { get; set; }

        /// <summary>활성 연결 수</summary>
        public int ActiveConnections { get; set; }

        /// <summary>의심스러운 연결 수</summary>
        public int SuspiciousConnections { get; set; }
    }

    /// <summary>
    /// 리소스 경고
    /// </summary>
    public class ResourceWarning
    {
        /// <summary>리소스 유형</summary>
        public string ResourceType { get; set; }

        /// <summary>현재 사용률</summary>
        public double CurrentUsage { get; set; }

        /// <summary>임계값</summary>
        public double Threshold { get; set; }

        /// <summary>경고 메시지</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// 보안 이벤트
    /// </summary>
    public class SecurityEvent
    {
        /// <summary>이벤트 ID</summary>
        public string EventId { get; set; }

        /// <summary>이벤트 유형</summary>
        public SecurityEventType EventType { get; set; }

        /// <summary>심각도</summary>
        public SecuritySeverity Severity { get; set; }

        /// <summary>발생 시간</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>설명</summary>
        public string Description { get; set; }

        /// <summary>관련 리소스</summary>
        public string RelatedResource { get; set; }
    }

    /// <summary>
    /// 보안 이벤트 빈도
    /// </summary>
    public class SecurityEventFrequency
    {
        /// <summary>이벤트 유형</summary>
        public SecurityEventType EventType { get; set; }

        /// <summary>발생 횟수</summary>
        public int Count { get; set; }

        /// <summary>마지막 발생 시간</summary>
        public DateTime LastOccurrence { get; set; }
    }

    /// <summary>
    /// 이상 행위 유형
    /// </summary>
    public enum AnomalyType
    {
        /// <summary>비정상적인 파일 접근 패턴</summary>
        UnusualFileAccessPattern,

        /// <summary>과도한 리소스 사용</summary>
        ExcessiveResourceUsage,

        /// <summary>의심스러운 파일 작업</summary>
        SuspiciousFileOperation,

        /// <summary>비정상적인 시간대 활동</summary>
        UnusualTimeActivity,

        /// <summary>반복적인 실패 시도</summary>
        RepeatedFailureAttempts
    }

    /// <summary>
    /// 보안 알림 유형
    /// </summary>
    public enum SecurityAlertType
    {
        /// <summary>높은 위험 이벤트</summary>
        HighRiskEvent,

        /// <summary>정책 위반</summary>
        PolicyViolation,

        /// <summary>이상 행위 감지</summary>
        AnomalyDetected,

        /// <summary>리소스 임계값 초과</summary>
        ResourceThresholdExceeded,

        /// <summary>보안 구성 변경</summary>
        SecurityConfigurationChanged
    }

    /// <summary>
    /// 보안 상태
    /// </summary>
    public enum SecurityStatus
    {
        /// <summary>안전</summary>
        Secure,

        /// <summary>주의</summary>
        Warning,

        /// <summary>위험</summary>
        Risk,

        /// <summary>매우 위험</summary>
        Critical
    }
}
