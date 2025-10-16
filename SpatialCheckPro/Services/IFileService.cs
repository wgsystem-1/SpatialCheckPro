using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간정보 파일 처리를 담당하는 서비스 인터페이스
    /// </summary>
    public interface IFileService
    {
        /// <summary>
        /// 폴더에서 지원되는 공간정보 파일을 자동 인식
        /// </summary>
        /// <param name="folderPath">검색할 폴더 경로</param>
        /// <param name="includeSubfolders">하위 폴더 포함 여부</param>
        /// <returns>인식된 파일 정보 목록</returns>
        Task<IEnumerable<SpatialFileInfo>> DetectSpatialFilesAsync(string folderPath, bool includeSubfolders = true);

        /// <summary>
        /// 대용량 파일을 청크 단위로 분할 처리
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="progress">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>처리 결과</returns>
        Task<FileProcessResult> ProcessLargeFileAsync(string filePath, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 파일 형식 검증
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>지원 여부</returns>
        bool IsSupportedFormat(string filePath);

        /// <summary>
        /// 단일 파일의 메타데이터 추출
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>파일 정보</returns>
        Task<SpatialFileInfo?> ExtractFileMetadataAsync(string filePath);

        /// <summary>
        /// 파일이 대용량인지 확인 (2GB 초과)
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <returns>대용량 파일 여부</returns>
        bool IsLargeFile(string filePath);
    }
}
