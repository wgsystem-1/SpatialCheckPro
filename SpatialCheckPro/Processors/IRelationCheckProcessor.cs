using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 관계 검수 프로세서 인터페이스
    /// </summary>
    public interface IRelationCheckProcessor
    {
        /// <summary>
        /// 전체 관계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">File Geodatabase 경로</param>
        /// <param name="config">관계 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ProcessAsync(
            string filePath,
            RelationCheckConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 공간 관계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">File Geodatabase 경로</param>
        /// <param name="config">관계 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateSpatialRelationsAsync(
            string filePath,
            RelationCheckConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 속성 관계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">File Geodatabase 경로</param>
        /// <param name="config">관계 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateAttributeRelationsAsync(
            string filePath,
            RelationCheckConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 교차 테이블 관계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">File Geodatabase 경로</param>
        /// <param name="config">관계 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateCrossTableRelationsAsync(
            string filePath,
            RelationCheckConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 진행률 업데이트 이벤트
        /// </summary>
        event EventHandler<RelationValidationProgressEventArgs>? ProgressUpdated;
    }
}
