using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 결과 포맷팅 인터페이스
    /// </summary>
    public interface IValidationResultFormatter
    {
        /// <summary>
        /// 검수 결과를 요약 정보로 포맷팅합니다
        /// </summary>
        /// <param name="ukResults">UK 검수 결과 목록</param>
        /// <param name="fkResults">FK 검수 결과 목록</param>
        /// <returns>검수 요약 정보</returns>
        IntegrityValidationSummary FormatResults(
            List<UniqueKeyValidationResult> ukResults,
            List<ForeignKeyValidationResult> fkResults);

        /// <summary>
        /// 상세 보고서를 생성합니다
        /// </summary>
        /// <param name="summary">검수 요약 정보</param>
        /// <returns>상세 보고서 텍스트</returns>
        string GenerateDetailedReport(IntegrityValidationSummary summary);

        /// <summary>
        /// CSV 형태로 내보냅니다
        /// </summary>
        /// <param name="summary">검수 요약 정보</param>
        /// <returns>CSV 바이트 배열</returns>
        Task<byte[]> ExportToCsvAsync(IntegrityValidationSummary summary);

        /// <summary>
        /// Excel 형태로 내보냅니다
        /// </summary>
        /// <param name="summary">검수 요약 정보</param>
        /// <returns>Excel 바이트 배열</returns>
        Task<byte[]> ExportToExcelAsync(IntegrityValidationSummary summary);

        /// <summary>
        /// PDF 형태로 내보냅니다
        /// </summary>
        /// <param name="summary">검수 요약 정보</param>
        /// <returns>PDF 바이트 배열</returns>
        Task<byte[]> ExportToPdfAsync(IntegrityValidationSummary summary);

        /// <summary>
        /// UK 검수 결과를 HTML 테이블로 포맷팅합니다
        /// </summary>
        /// <param name="results">UK 검수 결과 목록</param>
        /// <returns>HTML 테이블 문자열</returns>
        string FormatUniqueKeyResultsAsHtml(List<UniqueKeyValidationResult> results);

        /// <summary>
        /// FK 검수 결과를 HTML 테이블로 포맷팅합니다
        /// </summary>
        /// <param name="results">FK 검수 결과 목록</param>
        /// <returns>HTML 테이블 문자열</returns>
        string FormatForeignKeyResultsAsHtml(List<ForeignKeyValidationResult> results);
    }
}
