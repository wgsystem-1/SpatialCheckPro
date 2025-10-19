using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using System.ComponentModel;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 스키마 검수 프로세서 인터페이스
    /// </summary>
    public interface ISchemaCheckProcessor
    {
        /// <summary>
        /// 스키마 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ProcessAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 컬럼 구조 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateColumnStructureAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 데이터 타입 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateDataTypesAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 기본키/외래키 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidatePrimaryForeignKeysAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 외래키 관계 검수를 수행합니다
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">스키마 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        Task<ValidationResult> ValidateForeignKeyRelationsAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default);


    }
}
