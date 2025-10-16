using SpatialCheckPro.Models;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간정보 피처 편집을 담당하는 서비스 인터페이스
    /// </summary>
    public interface ISpatialEditService
    {
        /// <summary>
        /// 피처 편집 모드 시작
        /// </summary>
        /// <param name="spatialFile">편집할 공간정보 파일</param>
        /// <param name="featureId">편집할 피처 ID</param>
        /// <returns>편집 세션 정보</returns>
        Task<EditSession> StartEditSessionAsync(SpatialFileInfo spatialFile, string featureId);
        
        /// <summary>
        /// 피처 속성 값 수정
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <param name="attributeName">속성명</param>
        /// <param name="newValue">새로운 값</param>
        /// <returns>수정 결과</returns>
        Task<EditResult> UpdateAttributeAsync(EditSession editSession, string attributeName, object newValue);
        
        /// <summary>
        /// 지오메트리 수정
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <param name="newGeometry">새로운 지오메트리</param>
        /// <returns>수정 결과</returns>
        Task<EditResult> UpdateGeometryAsync(EditSession editSession, Geometry newGeometry);
        
        /// <summary>
        /// 편집 내용 저장 및 세션 종료
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>저장 결과</returns>
        Task<SaveResult> SaveAndCloseEditSessionAsync(EditSession editSession);
        
        /// <summary>
        /// 편집 내용 취소 및 세션 종료
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>취소 결과</returns>
        Task<bool> CancelEditSessionAsync(EditSession editSession);
        
        /// <summary>
        /// 편집 후 자동 재검수 실행
        /// </summary>
        /// <param name="spatialFile">편집된 공간정보 파일</param>
        /// <param name="editedFeatureIds">편집된 피처 ID 목록</param>
        /// <returns>재검수 결과</returns>
        Task<ValidationResult> RevalidateAfterEditAsync(SpatialFileInfo spatialFile, IEnumerable<string> editedFeatureIds);

        /// <summary>
        /// 편집 가능한 피처인지 확인
        /// </summary>
        /// <param name="spatialFile">공간정보 파일</param>
        /// <param name="featureId">피처 ID</param>
        /// <returns>편집 가능 여부</returns>
        Task<bool> CanEditFeatureAsync(SpatialFileInfo spatialFile, string featureId);

        /// <summary>
        /// 피처 정보 조회
        /// </summary>
        /// <param name="spatialFile">공간정보 파일</param>
        /// <param name="featureId">피처 ID</param>
        /// <returns>피처 정보</returns>
        Task<FeatureInfo> GetFeatureInfoAsync(SpatialFileInfo spatialFile, string featureId);

        /// <summary>
        /// 활성 편집 세션 목록 조회
        /// </summary>
        /// <returns>편집 세션 목록</returns>
        IEnumerable<EditSession> GetActiveEditSessions();
    }

    /// <summary>
    /// 편집 세션 정보
    /// </summary>
    public class EditSession
    {
        /// <summary>편집 세션 ID</summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>편집 대상 파일</summary>
        public SpatialFileInfo SpatialFile { get; set; }
        
        /// <summary>편집 대상 피처 ID</summary>
        public string FeatureId { get; set; }
        
        /// <summary>편집 시작 시간</summary>
        public DateTime StartTime { get; set; } = DateTime.Now;
        
        /// <summary>원본 피처 데이터 (롤백용)</summary>
        public Dictionary<string, object> OriginalAttributes { get; set; } = new();
        
        /// <summary>원본 지오메트리 (롤백용)</summary>
        public Geometry OriginalGeometry { get; set; }
        
        /// <summary>현재 편집 중인 속성 값들</summary>
        public Dictionary<string, object> CurrentAttributes { get; set; } = new();
        
        /// <summary>현재 편집 중인 지오메트리</summary>
        public Geometry CurrentGeometry { get; set; }
        
        /// <summary>편집 상태</summary>
        public EditSessionStatus Status { get; set; } = EditSessionStatus.Active;

        /// <summary>편집 이력</summary>
        public List<EditOperation> EditHistory { get; set; } = new();

        /// <summary>마지막 수정 시간</summary>
        public DateTime LastModified { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 편집 결과
    /// </summary>
    public class EditResult
    {
        /// <summary>편집 성공 여부</summary>
        public bool IsSuccess { get; set; }
        
        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }
        
        /// <summary>경고 메시지</summary>
        public List<string> Warnings { get; set; } = new();
        
        /// <summary>편집된 값</summary>
        public object EditedValue { get; set; }

        /// <summary>유효성 검사 결과</summary>
        public List<ValidationError> ValidationErrors { get; set; } = new();
    }

    /// <summary>
    /// 저장 결과
    /// </summary>
    public class SaveResult
    {
        /// <summary>저장 성공 여부</summary>
        public bool IsSuccess { get; set; }
        
        /// <summary>오류 메시지</summary>
        public string ErrorMessage { get; set; }
        
        /// <summary>저장된 피처 수</summary>
        public int SavedFeatureCount { get; set; }
        
        /// <summary>재검수 결과</summary>
        public ValidationResult RevalidationResult { get; set; }

        /// <summary>백업 파일 경로</summary>
        public string BackupFilePath { get; set; }
    }

    /// <summary>
    /// 피처 정보
    /// </summary>
    public class FeatureInfo
    {
        /// <summary>피처 ID</summary>
        public string FeatureId { get; set; }

        /// <summary>속성 정보</summary>
        public Dictionary<string, object> Attributes { get; set; } = new();

        /// <summary>지오메트리</summary>
        public Geometry Geometry { get; set; }

        /// <summary>지오메트리 타입</summary>
        public string GeometryType { get; set; }

        /// <summary>테이블명</summary>
        public string TableName { get; set; }

        /// <summary>편집 가능 여부</summary>
        public bool IsEditable { get; set; } = true;

        /// <summary>잠금 상태</summary>
        public bool IsLocked { get; set; }

        /// <summary>잠금 사용자</summary>
        public string LockedBy { get; set; }
    }

    /// <summary>
    /// 편집 작업 정보
    /// </summary>
    public class EditOperation
    {
        /// <summary>작업 ID</summary>
        public string OperationId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>작업 유형</summary>
        public EditOperationType Type { get; set; }

        /// <summary>작업 시간</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>대상 속성명 (속성 편집인 경우)</summary>
        public string AttributeName { get; set; }

        /// <summary>이전 값</summary>
        public object OldValue { get; set; }

        /// <summary>새로운 값</summary>
        public object NewValue { get; set; }

        /// <summary>작업 설명</summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// 편집 세션 상태
    /// </summary>
    public enum EditSessionStatus
    {
        /// <summary>활성</summary>
        Active,

        /// <summary>저장됨</summary>
        Saved,

        /// <summary>취소됨</summary>
        Cancelled,

        /// <summary>오류</summary>
        Error
    }

    /// <summary>
    /// 편집 작업 유형
    /// </summary>
    public enum EditOperationType
    {
        /// <summary>속성 수정</summary>
        AttributeUpdate,

        /// <summary>지오메트리 수정</summary>
        GeometryUpdate,

        /// <summary>피처 생성</summary>
        FeatureCreate,

        /// <summary>피처 삭제</summary>
        FeatureDelete
    }
}
