using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 결과 집계 및 저장을 담당하는 서비스 인터페이스
    /// </summary>
    public interface IValidationResultService
    {
        /// <summary>
        /// 검수 결과를 SQLite 데이터베이스에 저장
        /// </summary>
        /// <param name="validationResult">저장할 검수 결과</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveValidationResultAsync(ValidationResult validationResult);

        /// <summary>
        /// 검수 결과 조회
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult?> GetValidationResultAsync(string validationId);

        /// <summary>
        /// 파일별 검수 이력 조회
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="limit">조회할 최대 개수</param>
        /// <returns>검수 이력 목록</returns>
        Task<IEnumerable<ValidationResult>> GetValidationHistoryAsync(string filePath, int limit = 10);

        /// <summary>
        /// 전체 검수 이력 조회
        /// </summary>
        /// <param name="startDate">시작 날짜</param>
        /// <param name="endDate">종료 날짜</param>
        /// <param name="status">검수 상태 필터</param>
        /// <param name="limit">조회할 최대 개수</param>
        /// <returns>검수 이력 목록</returns>
        Task<IEnumerable<ValidationResult>> GetAllValidationHistoryAsync(
            DateTime? startDate = null, 
            DateTime? endDate = null, 
            ValidationStatus? status = null, 
            int limit = 100);

        /// <summary>
        /// 중복 검수 방지를 위한 최근 검수 결과 확인
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="fileModifiedTime">파일 수정 시간</param>
        /// <param name="configHash">설정 파일 해시</param>
        /// <returns>최근 검수 결과 (없으면 null)</returns>
        Task<ValidationResult?> GetRecentValidationResultAsync(string filePath, DateTime fileModifiedTime, string configHash);

        /// <summary>
        /// 검수 결과 삭제
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <returns>삭제 성공 여부</returns>
        Task<bool> DeleteValidationResultAsync(string validationId);

        /// <summary>
        /// 오래된 검수 결과 정리
        /// </summary>
        /// <param name="retentionDays">보관 기간 (일)</param>
        /// <returns>삭제된 검수 결과 수</returns>
        Task<int> CleanupOldValidationResultsAsync(int retentionDays = 30);

        /// <summary>
        /// 검수 통계 조회
        /// </summary>
        /// <param name="startDate">시작 날짜</param>
        /// <param name="endDate">종료 날짜</param>
        /// <returns>검수 통계</returns>
        Task<Models.ValidationStatistics> GetValidationStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 검수 결과 요약 정보 생성
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <returns>요약 정보</returns>
        ValidationSummary CreateValidationSummary(ValidationResult validationResult);

        /// <summary>
        /// 단계별 검수 결과 집계
        /// </summary>
        /// <param name="stageResults">단계별 결과 목록</param>
        /// <returns>집계된 결과</returns>
        ValidationAggregate AggregateStageResults(IEnumerable<StageResult> stageResults);

        /// <summary>
        /// 검수 결과 통계 계산
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <returns>계산된 통계</returns>
        Models.ValidationStatistics CalculateStatistics(ValidationResult validationResult);
    }


}
