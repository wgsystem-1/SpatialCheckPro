using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 속성 검수 프로세서 인터페이스
    /// </summary>
    public interface IAttributeCheckProcessor
    {
        /// <summary>
        /// 코드리스트 파일을 로드합니다.
        /// </summary>
        /// <param name="codelistPath">코드리스트 CSV 파일 경로</param>
        void LoadCodelist(string? codelistPath);

        /// <summary>
        /// 속성 검수를 수행합니다.
        /// </summary>
        /// <param name="dataSourcePath">데이터 소스 경로 (GDB 또는 SQLite)</param>
        /// <param name="dataProvider">데이터 제공자</param>
        /// <param name="rules">속성 검수 규칙 목록</param>
        /// <param name="token">취소 토큰</param>
        /// <returns>검수 오류 목록</returns>
        Task<List<ValidationError>> ValidateAsync(string dataSourcePath, IValidationDataProvider dataProvider, List<AttributeCheckConfig> rules, CancellationToken token = default);

        /// <summary>
        /// 단일 속성 검수 규칙을 처리합니다 (병렬 처리용)
        /// </summary>
        /// <param name="dataSourcePath">데이터 소스 경로</param>
        /// <param name="dataProvider">데이터 제공자</param>
        /// <param name="rule">속성 검수 규칙</param>
        /// <returns>검수 오류 목록</returns>
        Task<List<ValidationError>> ValidateSingleRuleAsync(string dataSourcePath, IValidationDataProvider dataProvider, AttributeCheckConfig rule);
    }
}
