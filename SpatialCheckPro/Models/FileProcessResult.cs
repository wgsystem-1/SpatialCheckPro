namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 파일 처리 결과를 나타내는 모델 클래스
    /// </summary>
    public class FileProcessResult
    {
        /// <summary>
        /// 처리 성공 여부
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 처리 성공 여부 (IsSuccess와 동일)
        /// </summary>
        public bool Success => IsSuccess;

        /// <summary>
        /// 처리된 바이트 수
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// 처리된 파일 정보
        /// </summary>
        public SpatialFileInfo? FileInfo { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 처리 시작 시간
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 처리 완료 시간
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 처리된 청크 수 (대용량 파일인 경우)
        /// </summary>
        public int ProcessedChunks { get; set; }

        /// <summary>
        /// 전체 청크 수 (대용량 파일인 경우)
        /// </summary>
        public int TotalChunks { get; set; }
    }
}
