namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 규칙 의존성을 나타내는 클래스
    /// </summary>
    public class RuleDependency
    {
        /// <summary>
        /// 의존하는 규칙 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 의존 대상 규칙 ID 목록
        /// </summary>
        public List<string> DependsOn { get; set; } = new List<string>();

        /// <summary>
        /// 의존성 타입
        /// </summary>
        public DependencyType DependencyType { get; set; } = DependencyType.Sequential;

        /// <summary>
        /// 의존성 실패 시 동작
        /// </summary>
        public DependencyFailureAction FailureAction { get; set; } = DependencyFailureAction.Skip;

        /// <summary>
        /// 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 의존성 타입
    /// </summary>
    public enum DependencyType
    {
        /// <summary>
        /// 순차적 실행 (의존 규칙이 성공해야 실행)
        /// </summary>
        Sequential,

        /// <summary>
        /// 조건부 실행 (의존 규칙 결과에 따라 실행)
        /// </summary>
        Conditional,

        /// <summary>
        /// 데이터 의존성 (의존 규칙의 결과 데이터 필요)
        /// </summary>
        DataDependency
    }

    /// <summary>
    /// 의존성 실패 시 동작
    /// </summary>
    public enum DependencyFailureAction
    {
        /// <summary>
        /// 건너뛰기
        /// </summary>
        Skip,

        /// <summary>
        /// 경고 후 계속
        /// </summary>
        WarnAndContinue,

        /// <summary>
        /// 전체 검수 중단
        /// </summary>
        Abort,

        /// <summary>
        /// 재시도
        /// </summary>
        Retry
    }
}
