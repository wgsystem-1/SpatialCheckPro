using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 조건부 규칙 엔진 인터페이스
    /// ConditionalRule 기반 복합 조건 처리 및 동적 규칙 적용을 담당
    /// </summary>
    public interface IConditionalRuleEngine
    {
        /// <summary>
        /// 조건부 규칙을 검증합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="tableName">대상 테이블명</param>
        /// <param name="rule">적용할 조건부 규칙</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>속성 관계 오류 목록</returns>
        Task<List<AttributeRelationError>> ValidateConditionalRuleAsync(
            string gdbPath,
            string tableName,
            ConditionalRule rule,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 여러 조건부 규칙을 병렬로 검증합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="tableName">대상 테이블명</param>
        /// <param name="rules">적용할 조건부 규칙 목록</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>속성 관계 오류 목록</returns>
        Task<List<AttributeRelationError>> ValidateMultipleRulesAsync(
            string gdbPath,
            string tableName,
            List<ConditionalRule> rules,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 런타임에 규칙을 동적으로 추가합니다
        /// </summary>
        /// <param name="rule">추가할 조건부 규칙</param>
        /// <returns>추가 성공 여부</returns>
        Task<bool> AddRuleAsync(ConditionalRule rule);

        /// <summary>
        /// 런타임에 규칙을 동적으로 제거합니다
        /// </summary>
        /// <param name="ruleId">제거할 규칙 ID</param>
        /// <returns>제거 성공 여부</returns>
        Task<bool> RemoveRuleAsync(string ruleId);

        /// <summary>
        /// 런타임에 규칙을 동적으로 업데이트합니다
        /// </summary>
        /// <param name="rule">업데이트할 조건부 규칙</param>
        /// <returns>업데이트 성공 여부</returns>
        Task<bool> UpdateRuleAsync(ConditionalRule rule);

        /// <summary>
        /// 규칙 실행 통계를 조회합니다
        /// </summary>
        /// <returns>규칙별 실행 통계</returns>
        Dictionary<string, RuleExecutionStatistics> GetExecutionStatistics();

        /// <summary>
        /// 캐시를 정리합니다
        /// </summary>
        void ClearCache();
    }
}
