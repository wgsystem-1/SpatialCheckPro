using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using SchemaCheckConfig = SpatialCheckPro.Models.Config.SchemaCheckConfig;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 4단계 검수 프로세스를 관리하는 서비스 인터페이스
    /// </summary>
    public interface IValidationService
    {
        /// <summary>
        /// 전체 검수 프로세스 실행
        /// </summary>
        /// <param name="spatialFile">검수할 공간정보 파일</param>
        /// <param name="configDirectory">검수 설정 파일 디렉토리</param>
        /// <param name="progress">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ExecuteValidationAsync(
            SpatialFileInfo spatialFile, 
            string configDirectory,
            IProgress<ValidationProgress>? progress = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 1단계: 테이블 검수 실행
        /// </summary>
        /// <param name="spatialFile">검수할 공간정보 파일</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>단계 검수 결과</returns>
        Task<StageResult> ExecuteTableCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<TableCheckConfig> config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 2단계: 스키마 검수 실행
        /// </summary>
        /// <param name="spatialFile">검수할 공간정보 파일</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>단계 검수 결과</returns>
        Task<StageResult> ExecuteSchemaCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<SchemaCheckConfig> config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 3단계: 지오메트리 검수 실행
        /// </summary>
        /// <param name="spatialFile">검수할 공간정보 파일</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>단계 검수 결과</returns>
        Task<StageResult> ExecuteGeometryCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<GeometryCheckConfig> config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 4단계: 관계 검수 실행
        /// </summary>
        /// <param name="spatialFile">검수할 공간정보 파일</param>
        /// <param name="config">관계 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>단계 검수 결과</returns>
        Task<StageResult> ExecuteRelationCheckAsync(
            SpatialFileInfo spatialFile, 
            IEnumerable<RelationCheckConfig> config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 검수 취소
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <returns>취소 성공 여부</returns>
        Task<bool> CancelValidationAsync(string validationId);

        /// <summary>
        /// 검수 상태 조회
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <returns>검수 상태</returns>
        Task<ValidationStatus> GetValidationStatusAsync(string validationId);
    }

    /// <summary>
    /// 검수 진행률 정보
    /// </summary>
    public class ValidationProgress
    {
        /// <summary>현재 단계 번호 (1-4)</summary>
        public int CurrentStage { get; set; }

        /// <summary>현재 단계명</summary>
        public string CurrentStageName { get; set; } = string.Empty;

        /// <summary>전체 진행률 (0-100)</summary>
        public int OverallPercentage { get; set; }

        /// <summary>현재 단계 진행률 (0-100)</summary>
        public int StagePercentage { get; set; }

        /// <summary>현재 처리 중인 작업 설명</summary>
        public string CurrentTask { get; set; } = string.Empty;

        /// <summary>처리된 항목 수</summary>
        public int ProcessedItems { get; set; }

        /// <summary>전체 항목 수</summary>
        public int TotalItems { get; set; }

        /// <summary>검수 시작 시간</summary>
        public DateTime StartTime { get; set; }

        /// <summary>예상 완료 시간</summary>
        public DateTime? EstimatedCompletionTime { get; set; }

        /// <summary>현재까지 발견된 오류 수</summary>
        public int ErrorCount { get; set; }

        /// <summary>현재까지 발견된 경고 수</summary>
        public int WarningCount { get; set; }
    }
}
