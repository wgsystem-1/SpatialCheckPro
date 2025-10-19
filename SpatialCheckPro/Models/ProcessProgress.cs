namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 처리 진행률 정보를 나타내는 모델 클래스
    /// </summary>
    public class ProcessProgress
    {
        /// <summary>
        /// 진행률 (0-100)
        /// </summary>
        public int Percentage { get; set; }

        /// <summary>
        /// 현재 처리 중인 청크 번호
        /// </summary>
        public int CurrentChunk { get; set; }

        /// <summary>
        /// 전체 청크 수
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// 처리된 바이트 수
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// 전체 바이트 수
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// 현재 상태 메시지
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 예상 남은 시간 (초)
        /// </summary>
        public int? EstimatedRemainingSeconds { get; set; }

        /// <summary>
        /// 처리 속도 (바이트/초)
        /// </summary>
        public double ProcessingSpeed { get; set; }
    }
}
