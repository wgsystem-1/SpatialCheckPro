using SpatialCheckPro.Models.Enums;
using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 검수 통계 정보
    /// </summary>
    public class ValidationStatistics
    {
        /// <summary>총 검수 횟수</summary>
        public int TotalValidations { get; set; }

        /// <summary>성공한 검수 횟수</summary>
        public int SuccessfulValidations { get; set; }

        /// <summary>실패한 검수 횟수</summary>
        public int FailedValidations { get; set; }

        /// <summary>취소된 검수 횟수</summary>
        public int CancelledValidations { get; set; }

        /// <summary>평균 검수 소요 시간 (분)</summary>
        public double AverageValidationTimeMinutes { get; set; }

        /// <summary>총 발견된 오류 수</summary>
        public int TotalErrors { get; set; }

        /// <summary>총 발견된 경고 수</summary>
        public int TotalWarnings { get; set; }

        /// <summary>가장 많이 검수된 파일 형식</summary>
        public SpatialFileFormat MostValidatedFormat { get; set; }

        /// <summary>단계별 실패 통계</summary>
        public Dictionary<int, int> StageFailureCount { get; set; } = new();

        /// <summary>검수 소요 시간</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>통계 생성 시간</summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// 검수 결과 요약 정보
    /// </summary>
    public class ValidationSummary
    {
        /// <summary>검수 ID</summary>
        public string ValidationId { get; set; } = string.Empty;

        /// <summary>파일명</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>파일 형식</summary>
        public SpatialFileFormat FileFormat { get; set; }

        /// <summary>검수 상태</summary>
        public ValidationStatus Status { get; set; }

        /// <summary>검수 시작 시간</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>검수 완료 시간</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>검수 소요 시간</summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>총 오류 수</summary>
        public int TotalErrors { get; set; }

        /// <summary>총 경고 수</summary>
        public int TotalWarnings { get; set; }

        /// <summary>완료된 단계 수</summary>
        public int CompletedStages { get; set; }

        /// <summary>전체 단계 수</summary>
        public int TotalStages { get; set; } = 4;

        /// <summary>성공률 (%)</summary>
        public double SuccessRate { get; set; }

        /// <summary>단계별 상태</summary>
        public Dictionary<int, StageStatus> StageStatuses { get; set; } = new();
    }

    /// <summary>
    /// 검수 결과 집계 정보
    /// </summary>
    public class ValidationAggregate
    {
        /// <summary>총 검수 항목 수</summary>
        public int TotalChecks { get; set; }

        /// <summary>통과한 검수 항목 수</summary>
        public int PassedChecks { get; set; }

        /// <summary>실패한 검수 항목 수</summary>
        public int FailedChecks { get; set; }

        /// <summary>경고가 발생한 검수 항목 수</summary>
        public int WarningChecks { get; set; }

        /// <summary>총 오류 수</summary>
        public int TotalErrors { get; set; }

        /// <summary>총 경고 수</summary>
        public int TotalWarnings { get; set; }

        /// <summary>단계별 집계</summary>
        public Dictionary<int, StageAggregate> StageAggregates { get; set; } = new();

        /// <summary>검수 항목별 집계</summary>
        public Dictionary<string, CheckAggregate> CheckAggregates { get; set; } = new();
    }

    /// <summary>
    /// 단계별 집계 정보
    /// </summary>
    public class StageAggregate
    {
        /// <summary>단계 번호</summary>
        public int StageNumber { get; set; }

        /// <summary>단계명</summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>단계 상태</summary>
        public StageStatus Status { get; set; }

        /// <summary>검수 항목 수</summary>
        public int CheckCount { get; set; }

        /// <summary>오류 수</summary>
        public int ErrorCount { get; set; }

        /// <summary>경고 수</summary>
        public int WarningCount { get; set; }

        /// <summary>소요 시간</summary>
        public TimeSpan? Duration { get; set; }
    }

    /// <summary>
    /// 검수 항목별 집계 정보
    /// </summary>
    public class CheckAggregate
    {
        /// <summary>검수 항목 ID</summary>
        public string CheckId { get; set; } = string.Empty;

        /// <summary>검수 항목명</summary>
        public string CheckName { get; set; } = string.Empty;

        /// <summary>검수 상태</summary>
        public CheckStatus Status { get; set; }

        /// <summary>대상 항목 수</summary>
        public int TotalCount { get; set; }

        /// <summary>오류 수</summary>
        public int ErrorCount { get; set; }

        /// <summary>경고 수</summary>
        public int WarningCount { get; set; }

        /// <summary>성공률 (%)</summary>
        public double SuccessRate { get; set; }
    }
}
