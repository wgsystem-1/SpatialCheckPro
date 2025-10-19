using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 교차 테이블 관계 규칙을 나타내는 클래스
    /// </summary>
    public class CrossTableRelationRule
    {
        /// <summary>
        /// 규칙 고유 ID
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 규칙명
        /// </summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>
        /// 주 테이블명
        /// </summary>
        public string PrimaryTable { get; set; } = string.Empty;

        /// <summary>
        /// 관련 테이블 목록
        /// </summary>
        public List<string> RelatedTables { get; set; } = new List<string>();

        /// <summary>
        /// 테이블 간 관계 정의 목록
        /// Key: 테이블명, Value: JOIN 조건
        /// </summary>
        public Dictionary<string, string> TableRelations { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 검증 표현식
        /// 여러 테이블을 참조하는 복합 조건
        /// </summary>
        public string ValidationExpression { get; set; } = string.Empty;

        /// <summary>
        /// 참조 무결성 검사 여부
        /// </summary>
        public bool CheckReferentialIntegrity { get; set; } = true;

        /// <summary>
        /// 외래키 제약조건 목록
        /// Key: 소스테이블.필드, Value: 대상테이블.필드
        /// </summary>
        public Dictionary<string, string> ForeignKeyConstraints { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 위반 시 심각도
        /// </summary>
        public ErrorSeverity ViolationSeverity { get; set; } = ErrorSeverity.Error;

        /// <summary>
        /// 규칙 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 규칙 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 오류 메시지 템플릿
        /// </summary>
        public string ErrorMessageTemplate { get; set; } = "교차 테이블 관계 규칙 위반: {0}";

        /// <summary>
        /// 수정 제안 템플릿
        /// </summary>
        public string? SuggestedFixTemplate { get; set; }

        /// <summary>
        /// 규칙 카테고리
        /// </summary>
        public string Category { get; set; } = "CrossTable";

        /// <summary>
        /// 성능 최적화 옵션
        /// </summary>
        public CrossTableOptimizationOptions OptimizationOptions { get; set; } = new CrossTableOptimizationOptions();

        /// <summary>
        /// 규칙 유효성을 검증합니다
        /// </summary>
        /// <returns>검증 오류 목록</returns>
        public List<ValidationError> Validate()
        {
            var errors = new List<ValidationError>();

            if (string.IsNullOrWhiteSpace(RuleId))
            {
                errors.Add(new ValidationError { Message = "규칙 ID는 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(RuleName))
            {
                errors.Add(new ValidationError { Message = "규칙명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(PrimaryTable))
            {
                errors.Add(new ValidationError { Message = "주 테이블명은 필수입니다." });
            }

            if (RelatedTables == null || RelatedTables.Count == 0)
            {
                errors.Add(new ValidationError { Message = "관련 테이블이 최소 하나는 필요합니다." });
            }

            if (string.IsNullOrWhiteSpace(ValidationExpression))
            {
                errors.Add(new ValidationError { Message = "검증 표현식은 필수입니다." });
            }

            // 테이블 관계 정의 검증
            foreach (var relatedTable in RelatedTables)
            {
                if (!TableRelations.ContainsKey(relatedTable))
                {
                    errors.Add(new ValidationError 
                    { 
                        Message = $"관련 테이블 '{relatedTable}'에 대한 관계 정의가 없습니다." 
                    });
                }
            }

            return errors;
        }
    }

    /// <summary>
    /// 교차 테이블 최적화 옵션
    /// </summary>
    public class CrossTableOptimizationOptions
    {
        /// <summary>
        /// 배치 크기
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// 병렬 처리 여부
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;

        /// <summary>
        /// 인덱스 힌트 사용 여부
        /// </summary>
        public bool UseIndexHints { get; set; } = true;

        /// <summary>
        /// 캐시 사용 여부
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 캐시 만료 시간 (분)
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 30;
    }
}
