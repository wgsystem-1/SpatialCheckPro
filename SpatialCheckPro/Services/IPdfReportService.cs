using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// PDF 보고서 생성 서비스 인터페이스
    /// </summary>
    public interface IPdfReportService
    {
        /// <summary>
        /// PDF 보고서를 생성합니다
        /// </summary>
        /// <param name="results">검수 결과 목록</param>
        /// <param name="outputPath">출력 파일 경로</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>생성 성공 여부</returns>
        Task<bool> GeneratePdfReportAsync(IEnumerable<ValidationResult> results, string outputPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// PDF 보고서 설정을 구성합니다
        /// </summary>
        /// <param name="settings">PDF 설정</param>
        void ConfigurePdfSettings(PdfReportSettings settings);
    }

    /// <summary>
    /// PDF 보고서 설정
    /// </summary>
    public class PdfReportSettings
    {
        /// <summary>페이지 크기</summary>
        public string PageSize { get; set; } = "A4";

        /// <summary>페이지 방향</summary>
        public string Orientation { get; set; } = "Portrait";

        /// <summary>여백 설정</summary>
        public PdfMargins Margins { get; set; } = new();

        /// <summary>폰트 설정</summary>
        public string FontFamily { get; set; } = "Arial";

        /// <summary>폰트 크기</summary>
        public int FontSize { get; set; } = 10;
    }

    /// <summary>
    /// PDF 여백 설정
    /// </summary>
    public class PdfMargins
    {
        /// <summary>상단 여백</summary>
        public double Top { get; set; } = 20;

        /// <summary>하단 여백</summary>
        public double Bottom { get; set; } = 20;

        /// <summary>좌측 여백</summary>
        public double Left { get; set; } = 20;

        /// <summary>우측 여백</summary>
        public double Right { get; set; } = 20;
    }
}
