using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services.Interfaces
{
    /// <summary>
    /// 오류 개수 분석 서비스 인터페이스
    /// </summary>
    public interface IErrorCountAnalysisService
    {
        /// <summary>
        /// 오류 개수 분석을 수행합니다
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>분석 결과</returns>
        Task<ErrorCountAnalysisResult> AnalyzeErrorCountsAsync(ValidationResult validationResult, string gdbPath);
    }
}
