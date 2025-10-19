namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 청크 처리 정보를 나타내는 모델 클래스
    /// </summary>
    public class ChunkProcessInfo
    {
        /// <summary>
        /// 청크 번호
        /// </summary>
        public int ChunkNumber { get; set; }

        /// <summary>
        /// 청크 시작 위치 (바이트)
        /// </summary>
        public long StartPosition { get; set; }

        /// <summary>
        /// 청크 크기 (바이트)
        /// </summary>
        public long ChunkSize { get; set; }

        /// <summary>
        /// 청크 처리 상태
        /// </summary>
        public ChunkStatus Status { get; set; }

        /// <summary>
        /// 처리 시작 시간
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 처리 완료 시간
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 처리된 레코드 수
        /// </summary>
        public int ProcessedRecords { get; set; }
    }

    /// <summary>
    /// 청크 처리 상태를 나타내는 열거형
    /// </summary>
    public enum ChunkStatus
    {
        /// <summary>
        /// 대기 중
        /// </summary>
        Pending,

        /// <summary>
        /// 처리 중
        /// </summary>
        Processing,

        /// <summary>
        /// 완료
        /// </summary>
        Completed,

        /// <summary>
        /// 실패
        /// </summary>
        Failed,

        /// <summary>
        /// 취소됨
        /// </summary>
        Cancelled
    }
}
