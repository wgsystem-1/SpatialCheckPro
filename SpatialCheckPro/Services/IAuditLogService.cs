using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 감사 로깅을 담당하는 서비스 인터페이스
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// 파일 접근 이벤트를 기록합니다
        /// </summary>
        /// <param name="filePath">접근한 파일 경로</param>
        /// <param name="action">수행한 작업</param>
        /// <param name="result">작업 결과</param>
        /// <param name="additionalInfo">추가 정보</param>
        Task LogFileAccessAsync(string filePath, FileAccessAction action, AccessResult result, Dictionary<string, object>? additionalInfo = null);

        /// <summary>
        /// 검수 작업 이벤트를 기록합니다
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <param name="action">검수 작업</param>
        /// <param name="stage">검수 단계</param>
        /// <param name="result">작업 결과</param>
        /// <param name="additionalInfo">추가 정보</param>
        Task LogValidationEventAsync(string validationId, ValidationAction action, int? stage, ValidationEventResult result, Dictionary<string, object>? additionalInfo = null);

        /// <summary>
        /// 보안 이벤트를 기록합니다
        /// </summary>
        /// <param name="eventType">보안 이벤트 유형</param>
        /// <param name="severity">심각도</param>
        /// <param name="description">설명</param>
        /// <param name="additionalInfo">추가 정보</param>
        Task LogSecurityEventAsync(SecurityEventType eventType, SecuritySeverity severity, string description, Dictionary<string, object>? additionalInfo = null);

        /// <summary>
        /// 데이터 편집 이벤트를 기록합니다
        /// </summary>
        /// <param name="filePath">편집한 파일 경로</param>
        /// <param name="featureId">편집한 피처 ID</param>
        /// <param name="action">편집 작업</param>
        /// <param name="changes">변경 내용</param>
        Task LogDataEditEventAsync(string filePath, string featureId, EditAction action, Dictionary<string, object> changes);

        /// <summary>
        /// 시스템 이벤트를 기록합니다
        /// </summary>
        /// <param name="eventType">시스템 이벤트 유형</param>
        /// <param name="description">설명</param>
        /// <param name="additionalInfo">추가 정보</param>
        Task LogSystemEventAsync(SystemEventType eventType, string description, Dictionary<string, object>? additionalInfo = null);

        /// <summary>
        /// 감사 로그를 조회합니다
        /// </summary>
        /// <param name="startDate">시작 날짜</param>
        /// <param name="endDate">종료 날짜</param>
        /// <param name="eventTypes">이벤트 유형 필터</param>
        /// <returns>감사 로그 목록</returns>
        Task<IEnumerable<AuditLogEntry>> GetAuditLogsAsync(DateTime? startDate = null, DateTime? endDate = null, AuditEventType[]? eventTypes = null);

        /// <summary>
        /// 보안 이벤트 알림을 설정합니다
        /// </summary>
        /// <param name="eventTypes">알림받을 이벤트 유형</param>
        /// <param name="notificationCallback">알림 콜백</param>
        void SetSecurityEventNotification(SecurityEventType[] eventTypes, Action<SecurityEventNotification> notificationCallback);
    }

    /// <summary>
    /// 감사 로그 항목
    /// </summary>
    public class AuditLogEntry
    {
        /// <summary>로그 ID</summary>
        public string LogId { get; set; } = string.Empty;

        /// <summary>이벤트 유형</summary>
        public AuditEventType EventType { get; set; }

        /// <summary>발생 시간</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>사용자 정보</summary>
        public string UserInfo { get; set; } = string.Empty;

        /// <summary>세션 ID</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>이벤트 설명</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>대상 리소스</summary>
        public string TargetResource { get; set; } = string.Empty;

        /// <summary>작업 결과</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>심각도</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>추가 메타데이터</summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>IP 주소</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>사용자 에이전트</summary>
        public string UserAgent { get; set; } = string.Empty;
    }

    /// <summary>
    /// 보안 이벤트 알림
    /// </summary>
    public class SecurityEventNotification
    {
        /// <summary>이벤트 유형</summary>
        public SecurityEventType EventType { get; set; }

        /// <summary>심각도</summary>
        public SecuritySeverity Severity { get; set; }

        /// <summary>발생 시간</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>설명</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>추가 정보</summary>
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    /// <summary>
    /// 감사 이벤트 유형
    /// </summary>
    public enum AuditEventType
    {
        /// <summary>파일 접근</summary>
        FileAccess,

        /// <summary>검수 작업</summary>
        ValidationEvent,

        /// <summary>보안 이벤트</summary>
        SecurityEvent,

        /// <summary>데이터 편집</summary>
        DataEdit,

        /// <summary>시스템 이벤트</summary>
        SystemEvent
    }

    /// <summary>
    /// 파일 접근 작업
    /// </summary>
    public enum FileAccessAction
    {
        /// <summary>파일 열기</summary>
        Open,

        /// <summary>파일 읽기</summary>
        Read,

        /// <summary>파일 쓰기</summary>
        Write,

        /// <summary>파일 삭제</summary>
        Delete,

        /// <summary>파일 이동</summary>
        Move,

        /// <summary>파일 복사</summary>
        Copy,

        /// <summary>폴더 검색</summary>
        FolderScan
    }

    /// <summary>
    /// 접근 결과
    /// </summary>
    public enum AccessResult
    {
        /// <summary>성공</summary>
        Success,

        /// <summary>실패</summary>
        Failed,

        /// <summary>거부됨</summary>
        Denied,

        /// <summary>부분 성공</summary>
        PartialSuccess
    }

    /// <summary>
    /// 검수 작업
    /// </summary>
    public enum ValidationAction
    {
        /// <summary>검수 시작</summary>
        Started,

        /// <summary>검수 완료</summary>
        Completed,

        /// <summary>검수 실패</summary>
        Failed,

        /// <summary>검수 취소</summary>
        Cancelled,

        /// <summary>단계 시작</summary>
        StageStarted,

        /// <summary>단계 완료</summary>
        StageCompleted,

        /// <summary>단계 실패</summary>
        StageFailed
    }

    /// <summary>
    /// 검수 이벤트 결과
    /// </summary>
    public enum ValidationEventResult
    {
        /// <summary>성공</summary>
        Success,

        /// <summary>실패</summary>
        Failed,

        /// <summary>경고</summary>
        Warning,

        /// <summary>진행 중</summary>
        InProgress
    }

    /// <summary>
    /// 보안 이벤트 유형
    /// </summary>
    public enum SecurityEventType
    {
        /// <summary>인증 실패</summary>
        AuthenticationFailure,

        /// <summary>권한 없는 접근</summary>
        UnauthorizedAccess,

        /// <summary>의심스러운 파일</summary>
        SuspiciousFile,

        /// <summary>보안 정책 위반</summary>
        PolicyViolation,

        /// <summary>데이터 무결성 오류</summary>
        DataIntegrityError,

        /// <summary>암호화 오류</summary>
        EncryptionError,

        /// <summary>시스템 침입 시도</summary>
        IntrusionAttempt,

        /// <summary>비정상적인 활동</summary>
        AnomalousActivity
    }

    /// <summary>
    /// 보안 심각도
    /// </summary>
    public enum SecuritySeverity
    {
        /// <summary>정보</summary>
        Info = 0,

        /// <summary>경고</summary>
        Warning = 1,

        /// <summary>낮음</summary>
        Low = 2,

        /// <summary>중간</summary>
        Medium = 3,

        /// <summary>높음</summary>
        High = 4,

        /// <summary>매우 높음</summary>
        Critical = 5
    }

    /// <summary>
    /// 편집 작업
    /// </summary>
    public enum EditAction
    {
        /// <summary>편집 시작</summary>
        EditStarted,

        /// <summary>속성 수정</summary>
        AttributeModified,

        /// <summary>지오메트리 수정</summary>
        GeometryModified,

        /// <summary>편집 저장</summary>
        EditSaved,

        /// <summary>편집 취소</summary>
        EditCancelled
    }

    /// <summary>
    /// 시스템 이벤트 유형
    /// </summary>
    public enum SystemEventType
    {
        /// <summary>애플리케이션 시작</summary>
        ApplicationStarted,

        /// <summary>애플리케이션 종료</summary>
        ApplicationStopped,

        /// <summary>설정 변경</summary>
        ConfigurationChanged,

        /// <summary>데이터베이스 연결</summary>
        DatabaseConnected,

        /// <summary>데이터베이스 연결 해제</summary>
        DatabaseDisconnected,

        /// <summary>오류 발생</summary>
        ErrorOccurred,

        /// <summary>성능 경고</summary>
        PerformanceWarning
    }
}
