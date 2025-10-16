using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 데이터 관리를 위한 서비스 인터페이스
    /// </summary>
    public interface IValidationDataService
    {
        /// <summary>
        /// 검수 결과 저장
        /// </summary>
        /// <param name="validationResult">저장할 검수 결과</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveValidationResultAsync(ValidationResult validationResult);

        /// <summary>
        /// 검수 결과 조회 (ID로)
        /// </summary>
        /// <param name="validationId">검수 ID</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult?> GetValidationResultAsync(string validationId);

        /// <summary>
        /// 검수 결과 목록 조회 (페이징)
        /// </summary>
        /// <param name="pageNumber">페이지 번호 (1부터 시작)</param>
        /// <param name="pageSize">페이지 크기</param>
        /// <returns>검수 결과 목록</returns>
        Task<(List<ValidationResult> Results, int TotalCount)> GetValidationResultsAsync(int pageNumber = 1, int pageSize = 20);

        /// <summary>
        /// 파일별 검수 결과 조회
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>해당 파일의 검수 결과 목록</returns>
        Task<List<ValidationResult>> GetValidationResultsByFileAsync(string filePath);

        /// <summary>
        /// 검수 결과 삭제
        /// </summary>
        /// <param name="validationId">삭제할 검수 ID</param>
        /// <returns>삭제 성공 여부</returns>
        Task<bool> DeleteValidationResultAsync(string validationId);

        /// <summary>
        /// 공간정보 파일 정보 저장
        /// </summary>
        /// <param name="spatialFileInfo">저장할 파일 정보</param>
        /// <returns>저장된 파일 정보 (ID 포함)</returns>
        Task<SpatialFileInfo?> SaveSpatialFileInfoAsync(SpatialFileInfo spatialFileInfo);

        /// <summary>
        /// 공간정보 파일 정보 조회
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>파일 정보</returns>
        Task<SpatialFileInfo?> GetSpatialFileInfoAsync(string filePath);

        /// <summary>
        /// 검수 통계 조회
        /// </summary>
        /// <returns>검수 통계 정보</returns>
        Task<Models.ValidationStatistics> GetValidationStatisticsAsync();

        /// <summary>
        /// 오류 통계 조회 (오류 유형별)
        /// </summary>
        /// <returns>오류 유형별 통계</returns>
        Task<Dictionary<ErrorType, int>> GetErrorStatisticsByTypeAsync();

        /// <summary>
        /// 최근 검수 결과 조회
        /// </summary>
        /// <param name="count">조회할 개수</param>
        /// <returns>최근 검수 결과 목록</returns>
        Task<List<ValidationResult>> GetRecentValidationResultsAsync(int count = 10);

        /// <summary>
        /// 검수 결과 검색
        /// </summary>
        /// <param name="searchCriteria">검색 조건</param>
        /// <returns>검색된 검수 결과 목록</returns>
        Task<List<ValidationResult>> SearchValidationResultsAsync(ValidationSearchCriteria searchCriteria);
    }



    /// <summary>
    /// 검수 결과 검색 조건 클래스
    /// </summary>
    public class ValidationSearchCriteria
    {
        /// <summary>
        /// 파일명 (부분 일치)
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// 검수 상태
        /// </summary>
        public ValidationStatus? Status { get; set; }

        /// <summary>
        /// 검수 시작 일시 (이후)
        /// </summary>
        public DateTime? StartDateFrom { get; set; }

        /// <summary>
        /// 검수 시작 일시 (이전)
        /// </summary>
        public DateTime? StartDateTo { get; set; }

        /// <summary>
        /// 오류 개수 (이상)
        /// </summary>
        public int? MinErrors { get; set; }

        /// <summary>
        /// 오류 개수 (이하)
        /// </summary>
        public int? MaxErrors { get; set; }

        /// <summary>
        /// 파일 형식
        /// </summary>
        public SpatialFileFormat? FileFormat { get; set; }
    }
}
