using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// HTML 보고서 생성 서비스 인터페이스
    /// </summary>
    public interface IHtmlReportService
    {
        /// <summary>
        /// HTML 보고서를 생성합니다
        /// </summary>
        /// <param name="results">검수 결과 목록</param>
        /// <param name="outputPath">출력 파일 경로</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>생성 성공 여부</returns>
        Task<bool> GenerateHtmlReportAsync(IEnumerable<ValidationResult> results, string outputPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// HTML 템플릿을 사용자 정의합니다
        /// </summary>
        /// <param name="templatePath">템플릿 파일 경로</param>
        void SetCustomTemplate(string templatePath);
    }
}
