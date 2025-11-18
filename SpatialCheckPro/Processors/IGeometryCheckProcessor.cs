using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using System.ComponentModel;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 지오메트리 검수 프로세서 인터페이스
    /// </summary>
    public interface IGeometryCheckProcessor
    {
        /// <summary>
        /// 지오메트리 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <param name="streamingOutputPath">스트리밍 출력 경로 (null이면 메모리에 누적)</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ProcessAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default, string? streamingOutputPath = null);

        /// <summary>
        /// 중복 지오메트리 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> CheckDuplicateGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 겹치는 지오메트리 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> CheckOverlappingGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 뒤틀린 지오메트리 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> CheckTwistedGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 슬리버 폴리곤 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">지오메트리 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> CheckSliverPolygonsAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 공간 인덱스 캐시를 정리합니다 (배치 검수 성능 최적화)
        /// </summary>
        void ClearSpatialIndexCache();

        /// <summary>
        /// 특정 파일의 공간 인덱스 캐시를 정리합니다 (배치 검수 성능 최적화)
        /// </summary>
        void ClearSpatialIndexCacheForFile(string filePath);
    }
}
