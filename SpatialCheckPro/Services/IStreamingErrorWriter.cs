using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 오류 스트리밍 기록 인터페이스
    /// Phase 2 Item #7: 메모리 누적 없이 오류를 디스크에 즉시 기록
    /// </summary>
    public interface IStreamingErrorWriter : IDisposable
    {
        /// <summary>
        /// 단일 오류를 디스크에 즉시 기록
        /// </summary>
        Task WriteErrorAsync(ValidationError error);

        /// <summary>
        /// 여러 오류를 배치로 기록
        /// </summary>
        Task WriteErrorsAsync(IEnumerable<ValidationError> errors);

        /// <summary>
        /// 현재까지 기록된 오류 통계 가져오기
        /// </summary>
        ErrorStatistics GetStatistics();

        /// <summary>
        /// 기록 완료 및 최종 통계 반환
        /// </summary>
        Task<ErrorStatistics> FinalizeAsync();

        /// <summary>
        /// 출력 파일 경로
        /// </summary>
        string OutputPath { get; }
    }

    /// <summary>
    /// 오류 통계 정보 (메모리 효율적)
    /// </summary>
    public class ErrorStatistics
    {
        /// <summary>
        /// 총 오류 개수
        /// </summary>
        public int TotalErrorCount { get; set; }

        /// <summary>
        /// 총 경고 개수
        /// </summary>
        public int TotalWarningCount { get; set; }

        /// <summary>
        /// 오류 코드별 개수
        /// </summary>
        public Dictionary<string, int> ErrorCountByCode { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 심각도별 개수
        /// </summary>
        public Dictionary<string, int> ErrorCountBySeverity { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 테이블별 오류 개수
        /// </summary>
        public Dictionary<string, int> ErrorCountByTable { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 출력 파일 경로
        /// </summary>
        public string? OutputFilePath { get; set; }

        /// <summary>
        /// 기록 시작 시간
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 기록 완료 시간
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 기록 소요 시간
        /// </summary>
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    }
}
