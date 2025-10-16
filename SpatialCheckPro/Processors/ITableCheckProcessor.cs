using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using System.ComponentModel;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 테이블 검수 프로세서 인터페이스
    /// </summary>
    public interface ITableCheckProcessor
    {
        /// <summary>
        /// 테이블 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ProcessAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 테이블 목록 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateTableListAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 좌표계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateCoordinateSystemAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 지오메트리 타입 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateGeometryTypeAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default);


    }
}
