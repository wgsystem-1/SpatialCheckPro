using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 테이블 검수 결과
    /// </summary>
    public class TableCheckResult : CheckResult
    {
        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 총 테이블 수
        /// </summary>
        public int TotalTableCount { get; set; }

        /// <summary>
        /// 처리된 테이블 수 (객체가 있는 테이블)
        /// </summary>
        public int ProcessedTableCount { get; set; }

        /// <summary>
        /// 스킵된 테이블 수 (객체가 0개인 테이블)
        /// </summary>
        public int SkippedTableCount { get; set; }

        /// <summary>
        /// 테이블별 검수 결과 목록
        /// </summary>
        public List<TableValidationItem> TableResults { get; set; } = new();

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

        /// <summary>StageResult로 변환</summary>
        public StageResult ToStageResult()
        {
            return new StageResult
            {
                StageId = CheckId,
                StageName = CheckName,
                Status = ConvertToStageStatus(Status),
                Errors = Errors,
                Warnings = Warnings,
                Metadata = Metadata
            };
        }

        private static StageStatus ConvertToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.CompletedWithWarnings,
                CheckStatus.Skipped => StageStatus.Skipped,
                _ => StageStatus.Pending
            };
        }
    }

    /// <summary>
    /// 스키마 검수 결과
    /// </summary>
    public class SchemaCheckResult : CheckResult
    {
        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 스키마 검수 결과 목록
        /// </summary>
        public List<SchemaValidationItem> SchemaResults { get; set; } = new();

        /// <summary>
        /// 총 컬럼 수
        /// </summary>
        public int TotalColumnCount { get; set; }

        /// <summary>
        /// 처리된 컬럼 수
        /// </summary>
        public int ProcessedColumnCount { get; set; }

        /// <summary>
        /// 스킵된 컬럼 수
        /// </summary>
        public int SkippedColumnCount { get; set; }

        /// <summary>
        /// 유효한 컬럼 수
        /// </summary>
        public int ValidColumnCount => SchemaResults.Count(s => s.IsValid);

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

        /// <summary>StageResult로 변환</summary>
        public StageResult ToStageResult()
        {
            return new StageResult
            {
                StageId = CheckId,
                StageName = CheckName,
                Status = ConvertToStageStatus(Status),
                Errors = Errors,
                Warnings = Warnings,
                Metadata = Metadata
            };
        }

        private static StageStatus ConvertToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.CompletedWithWarnings,
                CheckStatus.Skipped => StageStatus.Skipped,
                _ => StageStatus.Pending
            };
        }
    }

    /// <summary>
    /// 지오메트리 검수 결과
    /// </summary>
    public class GeometryCheckResult : CheckResult
    {
        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 총 테이블 수
        /// </summary>
        public int TotalTableCount { get; set; }

        /// <summary>
        /// 처리된 테이블 수
        /// </summary>
        public int ProcessedTableCount { get; set; }

        /// <summary>
        /// 스킵된 테이블 수
        /// </summary>
        public int SkippedTableCount { get; set; }

        /// <summary>
        /// 지오메트리 검수 결과 목록
        /// </summary>
        public List<GeometryValidationItem> GeometryResults { get; set; } = new List<GeometryValidationItem>();

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

        /// <summary>StageResult로 변환</summary>
        public StageResult ToStageResult()
        {
            return new StageResult
            {
                StageId = CheckId,
                StageName = CheckName,
                Status = ConvertToStageStatus(Status),
                Errors = Errors,
                Warnings = Warnings,
                Metadata = Metadata
            };
        }

        private static StageStatus ConvertToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.CompletedWithWarnings,
                CheckStatus.Skipped => StageStatus.Skipped,
                _ => StageStatus.Pending
            };
        }
    }

    /// <summary>
    /// 관계 검수 결과
    /// </summary>
    public class RelationCheckResult : CheckResult
    {
        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 검사한 규칙 수
        /// </summary>
        public int ProcessedRulesCount { get; set; }

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

        /// <summary>StageResult로 변환</summary>
        public StageResult ToStageResult()
        {
            return new StageResult
            {
                StageId = CheckId,
                StageName = CheckName,
                Status = ConvertToStageStatus(Status),
                Errors = Errors,
                Warnings = Warnings,
                Metadata = Metadata
            };
        }

        private static StageStatus ConvertToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.CompletedWithWarnings,
                CheckStatus.Skipped => StageStatus.Skipped,
                _ => StageStatus.Pending
            };
        }
    }

    /// <summary>
    /// 속성 관계 검수 결과 (신규 5단계)
    /// </summary>
    public class AttributeRelationCheckResult : CheckResult
    {
        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 검수 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 검사한 규칙 수
        /// </summary>
        public int ProcessedRulesCount { get; set; }

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

        /// <summary>StageResult로 변환</summary>
        public StageResult ToStageResult()
        {
            return new StageResult
            {
                StageId = CheckId,
                StageName = CheckName,
                Status = ConvertToStageStatus(Status),
                Errors = Errors,
                Warnings = Warnings,
                Metadata = Metadata
            };
        }

        private static StageStatus ConvertToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.CompletedWithWarnings,
                CheckStatus.Skipped => StageStatus.Skipped,
                _ => StageStatus.Pending
            };
        }
    }
}
