namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 파일 처리 통계 정보
    /// </summary>
    public class ProcessingStatistics
    {
        /// <summary>전체 청크 수</summary>
        public int TotalChunks { get; set; }

        /// <summary>완료된 청크 수</summary>
        public int CompletedChunks { get; set; }

        /// <summary>실패한 청크 수</summary>
        public int FailedChunks { get; set; }

        /// <summary>처리된 총 바이트 수</summary>
        public long TotalProcessedBytes { get; set; }

        /// <summary>평균 처리 시간</summary>
        public TimeSpan AverageProcessingTime { get; set; }

        /// <summary>총 처리 시간</summary>
        public TimeSpan TotalProcessingTime { get; set; }

        /// <summary>처리 속도 (바이트/초)</summary>
        public double ProcessingSpeed { get; set; }

        /// <summary>성공률 (%)</summary>
        public double SuccessRate => TotalChunks > 0 ? (double)CompletedChunks / TotalChunks * 100 : 0;

        /// <summary>실패율 (%)</summary>
        public double FailureRate => TotalChunks > 0 ? (double)FailedChunks / TotalChunks * 100 : 0;
    }
}