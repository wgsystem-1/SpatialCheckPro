using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 검수 단계를 나타내는 클래스
    /// </summary>
    public class ValidationStage
    {
        /// <summary>
        /// 단계 ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 단계명
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 단계 타입
        /// </summary>
        public ValidationStageType StageType { get; set; }

        /// <summary>
        /// 우선순위
        /// </summary>
        public RulePriority Priority { get; set; } = RulePriority.Normal;

        /// <summary>
        /// 의존성 목록
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// 실패 시 동작
        /// </summary>
        public DependencyFailureAction FailureAction { get; set; } = DependencyFailureAction.WarnAndContinue;

        /// <summary>
        /// 공간 관계 규칙 목록
        /// </summary>
        public List<SpatialRelationRule> SpatialRules { get; set; } = new List<SpatialRelationRule>();

        /// <summary>
        /// 논리적 관계 규칙 목록
        /// </summary>
        public List<LogicalRelationRule> LogicalRules { get; set; } = new List<LogicalRelationRule>();

        /// <summary>
        /// 조건부 규칙 목록
        /// </summary>
        public List<ConditionalRule> ConditionalRules { get; set; } = new List<ConditionalRule>();

        /// <summary>
        /// 교차 테이블 관계 규칙 목록
        /// </summary>
        public List<CrossTableRelationRule> CrossTableRules { get; set; } = new List<CrossTableRelationRule>();

        /// <summary>
        /// 병렬 처리 가능 여부
        /// </summary>
        public bool CanRunInParallel { get; set; } = true;

        /// <summary>
        /// 최대 재시도 횟수
        /// </summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>
        /// 재시도 지연 시간 (밀리초)
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 검수 단계 타입
    /// </summary>
    public enum ValidationStageType
    {
        /// <summary>
        /// 공간 관계 검수
        /// </summary>
        SpatialRelation,

        /// <summary>
        /// 속성 관계 검수
        /// </summary>
        AttributeRelation,

        /// <summary>
        /// 교차 테이블 관계 검수
        /// </summary>
        CrossTableRelation,

        /// <summary>
        /// 혼합 검수
        /// </summary>
        Mixed
    }
}
