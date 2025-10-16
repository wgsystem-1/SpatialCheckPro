using SpatialCheckPro.Models;
using System.ComponentModel;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 보고서 생성 서비스 인터페이스
    /// </summary>
    public interface IReportService
    {
        /// <summary>
        /// 검수 결과 보고서를 생성합니다
        /// </summary>
        /// <param name="results">검수 결과 목록</param>
        /// <param name="outputPath">출력 파일 경로</param>
        /// <param name="format">보고서 형식</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>생성 성공 여부</returns>
        Task<bool> GenerateReportAsync(IEnumerable<ValidationResult> results, string outputPath, ReportFormat format, CancellationToken cancellationToken = default);

        /// <summary>
        /// 지원되는 보고서 형식 목록을 가져옵니다
        /// </summary>
        /// <returns>지원되는 형식 목록</returns>
        IEnumerable<ReportFormat> GetSupportedFormats();


    }

    /// <summary>
    /// 보고서 형식 열거형
    /// </summary>
    public enum ReportFormat
    {
        /// <summary>HTML 형식</summary>
        Html,
        /// <summary>Excel 형식</summary>
        Excel,
        /// <summary>PDF 형식</summary>
        Pdf,
        /// <summary>CSV 형식</summary>
        Csv
    }
}
