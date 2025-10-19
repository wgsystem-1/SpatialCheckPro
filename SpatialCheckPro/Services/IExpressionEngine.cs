using OSGeo.OGR;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 표현식 엔진 인터페이스
    /// SQL 유사 표현식의 파싱 및 실행을 담당
    /// </summary>
    public interface IExpressionEngine
    {
        /// <summary>
        /// 표현식을 파싱하고 유효성을 검증합니다
        /// </summary>
        /// <param name="expression">파싱할 표현식</param>
        /// <param name="tableSchema">테이블 스키마 정보</param>
        /// <returns>파싱 결과</returns>
        Task<ExpressionParseResult> ParseExpressionAsync(string expression, Dictionary<string, Type> tableSchema);

        /// <summary>
        /// 파싱된 표현식을 피처에 대해 실행합니다
        /// </summary>
        /// <param name="parseResult">파싱 결과</param>
        /// <param name="feature">대상 피처</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <returns>실행 결과</returns>
        Task<ExpressionExecutionResult> ExecuteExpressionAsync(
            ExpressionParseResult parseResult, 
            Feature feature, 
            ExpressionExecutionContext context);

        /// <summary>
        /// 표현식을 직접 실행합니다 (파싱과 실행을 한번에)
        /// </summary>
        /// <param name="expression">실행할 표현식</param>
        /// <param name="feature">대상 피처</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <returns>실행 결과</returns>
        Task<ExpressionExecutionResult> EvaluateExpressionAsync(
            string expression, 
            Feature feature, 
            ExpressionExecutionContext context);

        /// <summary>
        /// 복합 표현식을 실행합니다 (여러 테이블 참조)
        /// </summary>
        /// <param name="expression">실행할 표현식</param>
        /// <param name="features">테이블별 피처 목록</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <returns>실행 결과</returns>
        Task<ExpressionExecutionResult> EvaluateComplexExpressionAsync(
            string expression, 
            Dictionary<string, Feature> features, 
            ExpressionExecutionContext context);

        /// <summary>
        /// 표현식에서 사용된 필드 목록을 추출합니다
        /// </summary>
        /// <param name="expression">분석할 표현식</param>
        /// <returns>필드 참조 목록</returns>
        List<FieldReference> ExtractFieldReferences(string expression);

        /// <summary>
        /// 표현식을 OGR 필터 문자열로 변환합니다
        /// </summary>
        /// <param name="expression">변환할 표현식</param>
        /// <param name="tableAlias">테이블 별칭</param>
        /// <returns>OGR 필터 문자열</returns>
        string ConvertToOgrFilter(string expression, string? tableAlias = null);
    }

    /// <summary>
    /// 표현식 파싱 결과
    /// </summary>
    public class ExpressionParseResult
    {
        /// <summary>
        /// 파싱 성공 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 파싱된 표현식 트리
        /// </summary>
        public ExpressionNode? RootNode { get; set; }

        /// <summary>
        /// 사용된 필드 참조 목록
        /// </summary>
        public List<FieldReference> FieldReferences { get; set; } = new List<FieldReference>();

        /// <summary>
        /// 파싱 오류 목록
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 표현식 타입 (Boolean, Numeric, String 등)
        /// </summary>
        public ExpressionType ExpressionType { get; set; } = ExpressionType.Boolean;

        /// <summary>
        /// 최적화된 표현식 (성능 향상을 위해)
        /// </summary>
        public string? OptimizedExpression { get; set; }
    }

    /// <summary>
    /// 표현식 실행 결과
    /// </summary>
    public class ExpressionExecutionResult
    {
        /// <summary>
        /// 실행 성공 여부
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 결과값 (Boolean, Numeric, String 등)
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Boolean 결과값 (조건 평가용)
        /// </summary>
        public bool BooleanValue => Value is bool b ? b : false;

        /// <summary>
        /// 숫자 결과값
        /// </summary>
        public double? NumericValue => Value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => null
        };

        /// <summary>
        /// 문자열 결과값
        /// </summary>
        public string? StringValue => Value?.ToString();

        /// <summary>
        /// 실행 오류 메시지
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 실행 시간 (성능 모니터링용)
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 표현식 실행 컨텍스트
    /// </summary>
    public class ExpressionExecutionContext
    {
        /// <summary>
        /// 테이블 스키마 정보
        /// </summary>
        public Dictionary<string, Dictionary<string, Type>> TableSchemas { get; set; } = 
            new Dictionary<string, Dictionary<string, Type>>();

        /// <summary>
        /// 변수 값 (사용자 정의 변수)
        /// </summary>
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 함수 정의 (사용자 정의 함수)
        /// </summary>
        public Dictionary<string, Func<object[], object>> Functions { get; set; } = 
            new Dictionary<string, Func<object[], object>>();

        /// <summary>
        /// 실행 옵션
        /// </summary>
        public ExpressionExecutionOptions Options { get; set; } = new ExpressionExecutionOptions();

        /// <summary>
        /// 현재 테이블 별칭
        /// </summary>
        public string? CurrentTableAlias { get; set; }

        /// <summary>
        /// 로깅 활성화 여부
        /// </summary>
        public bool EnableLogging { get; set; } = false;
    }

    /// <summary>
    /// 표현식 실행 옵션
    /// </summary>
    public class ExpressionExecutionOptions
    {
        /// <summary>
        /// NULL 값 처리 방식
        /// </summary>
        public NullHandling NullHandling { get; set; } = NullHandling.ReturnNull;

        /// <summary>
        /// 타입 변환 허용 여부
        /// </summary>
        public bool AllowTypeConversion { get; set; } = true;

        /// <summary>
        /// 대소문자 구분 여부
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// 최대 실행 시간 (밀리초)
        /// </summary>
        public int MaxExecutionTimeMs { get; set; } = 5000;

        /// <summary>
        /// 캐시 사용 여부
        /// </summary>
        public bool EnableCaching { get; set; } = true;
    }

    /// <summary>
    /// 필드 참조 정보
    /// </summary>
    public class FieldReference
    {
        /// <summary>
        /// 테이블 별칭 (예: "source", "target")
        /// </summary>
        public string? TableAlias { get; set; }

        /// <summary>
        /// 필드명
        /// </summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// 전체 참조 문자열 (예: "source.FIELD1")
        /// </summary>
        public string FullReference { get; set; } = string.Empty;

        /// <summary>
        /// 필드 타입
        /// </summary>
        public Type? FieldType { get; set; }

        /// <summary>
        /// 표현식 내 위치
        /// </summary>
        public int Position { get; set; }
    }

    /// <summary>
    /// 표현식 노드 (추상 구문 트리)
    /// </summary>
    public abstract class ExpressionNode
    {
        /// <summary>
        /// 노드 타입
        /// </summary>
        public abstract ExpressionNodeType NodeType { get; }

        /// <summary>
        /// 노드를 평가합니다
        /// </summary>
        /// <param name="context">실행 컨텍스트</param>
        /// <returns>평가 결과</returns>
        public abstract object? Evaluate(ExpressionExecutionContext context);

        /// <summary>
        /// 노드를 문자열로 변환합니다
        /// </summary>
        /// <returns>문자열 표현</returns>
        public abstract override string ToString();
    }

    /// <summary>
    /// 표현식 타입
    /// </summary>
    public enum ExpressionType
    {
        Boolean,    // 불린 표현식 (조건문)
        Numeric,    // 숫자 표현식
        String,     // 문자열 표현식
        Date,       // 날짜 표현식
        Mixed       // 혼합 타입
    }

    /// <summary>
    /// 표현식 노드 타입
    /// </summary>
    public enum ExpressionNodeType
    {
        Literal,        // 리터럴 값
        FieldReference, // 필드 참조
        BinaryOperator, // 이항 연산자
        UnaryOperator,  // 단항 연산자
        FunctionCall,   // 함수 호출
        Conditional     // 조건문
    }

    /// <summary>
    /// NULL 값 처리 방식
    /// </summary>
    public enum NullHandling
    {
        ReturnNull,     // NULL 반환
        ReturnFalse,    // False 반환
        ThrowException, // 예외 발생
        IgnoreNull      // NULL 무시
    }
}
