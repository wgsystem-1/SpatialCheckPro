using SpatialCheckPro.Models;
using System.ComponentModel;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 대용량 파일 처리 서비스 인터페이스
    /// </summary>
    public interface ILargeFileProcessor
    {
        /// <summary>
        /// 대용량 파일을 청크 단위로 처리합니다
        /// </summary>
        /// <param name="filePath">처리할 파일 경로</param>
        /// <param name="chunkSize">청크 크기 (바이트)</param>
        /// <param name="processor">청크 처리 함수</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>처리 결과</returns>
        Task<ProcessingResult> ProcessInChunksAsync(string filePath, int chunkSize, Func<byte[], int, Task<bool>> processor, CancellationToken cancellationToken = default);

        /// <summary>
        /// 파일 크기를 확인합니다
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>파일 크기 (바이트)</returns>
        long GetFileSize(string filePath);

        /// <summary>
        /// 파일이 대용량인지 확인합니다
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="threshold">임계값 (바이트)</param>
        /// <returns>대용량 파일 여부</returns>
        bool IsLargeFile(string filePath, long threshold = 2_147_483_648L); // 2GB

        /// <summary>
        /// 메모리 사용량을 모니터링합니다
        /// </summary>
        /// <returns>현재 메모리 사용량 (바이트)</returns>
        long GetMemoryUsage();

        /// <summary>
        /// 파일 스트림을 처리합니다
        /// </summary>
        /// <param name="stream">처리할 스트림</param>
        /// <param name="processor">스트림 처리 함수</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>처리 결과</returns>
        Task<ProcessingResult> ProcessFileStreamAsync(System.IO.Stream stream, Func<System.IO.Stream, Task<bool>> processor, CancellationToken cancellationToken = default);

        /// <summary>
        /// 파일의 메타데이터를 분석하여 처리 모드를 결정합니다
        /// </summary>
        /// <param name="filePath">분석할 파일 경로</param>
        /// <returns>파일 분석 결과</returns>
        object AnalyzeFileForProcessing(string filePath);


    }

    /// <summary>
    /// 파일 처리 결과
    /// </summary>
    public class ProcessingResult
    {
        /// <summary>처리 성공 여부</summary>
        public bool Success { get; set; }

        /// <summary>처리된 바이트 수</summary>
        public long ProcessedBytes { get; set; }

        /// <summary>처리 시간</summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>오류 메시지</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>추가 정보</summary>
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }
}
