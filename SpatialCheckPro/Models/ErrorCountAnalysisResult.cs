using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 오류 개수 분석 결과
    /// </summary>
    public class ErrorCountAnalysisResult
    {
        /// <summary>
        /// 분석 대상 FileGDB 경로
        /// </summary>
        public string GdbPath { get; set; } = string.Empty;

        /// <summary>
        /// 분석 실행 시간
        /// </summary>
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 검수 결과에서 계산된 총 오류 개수
        /// </summary>
        public int ValidationResultErrorCount { get; set; }

        /// <summary>
        /// QC_ERRORS_POINT에 저장된 실제 오류 개수
        /// </summary>
        public int SavedPointErrorCount { get; set; }

        /// <summary>
        /// 저장되지 않은 오류 개수 (차이)
        /// </summary>
        public int MissingErrorCount => ValidationResultErrorCount - SavedPointErrorCount;

        /// <summary>
        /// 저장 성공률 (%)
        /// </summary>
        public double SaveSuccessRate => ValidationResultErrorCount > 0 
            ? (SavedPointErrorCount / (double)ValidationResultErrorCount) * 100 
            : 100.0;

        /// <summary>
        /// 단계별 오류 개수 분석
        /// </summary>
        public List<StageErrorAnalysis> StageAnalyses { get; set; } = new List<StageErrorAnalysis>();

        /// <summary>
        /// 저장되지 않은 오류 상세 정보
        /// </summary>
        public List<MissingErrorDetail> MissingErrorDetails { get; set; } = new List<MissingErrorDetail>();

        /// <summary>
        /// 분석 요약 메시지
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// 분석 상태
        /// </summary>
        public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// 단계별 오류 분석
    /// </summary>
    public class StageErrorAnalysis
    {
        /// <summary>
        /// 단계 번호 (0-5)
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 단계 이름
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과에서 계산된 오류 개수
        /// </summary>
        public int ValidationErrorCount { get; set; }

        /// <summary>
        /// 저장된 오류 개수
        /// </summary>
        public int SavedErrorCount { get; set; }

        /// <summary>
        /// 저장되지 않은 오류 개수
        /// </summary>
        public int MissingErrorCount => ValidationErrorCount - SavedErrorCount;

        /// <summary>
        /// 저장 성공률 (%)
        /// </summary>
        public double SaveSuccessRate => ValidationErrorCount > 0 
            ? (SavedErrorCount / (double)ValidationErrorCount) * 100 
            : 100.0;

        /// <summary>
        /// 오류 타입별 분석
        /// </summary>
        public List<ErrorTypeAnalysis> ErrorTypeAnalyses { get; set; } = new List<ErrorTypeAnalysis>();
    }

    /// <summary>
    /// 오류 타입별 분석
    /// </summary>
    public class ErrorTypeAnalysis
    {
        /// <summary>
        /// 오류 타입 코드
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// 오류 타입 이름
        /// </summary>
        public string ErrorTypeName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과에서 계산된 오류 개수
        /// </summary>
        public int ValidationErrorCount { get; set; }

        /// <summary>
        /// 저장된 오류 개수
        /// </summary>
        public int SavedErrorCount { get; set; }

        /// <summary>
        /// 저장되지 않은 오류 개수
        /// </summary>
        public int MissingErrorCount => ValidationErrorCount - SavedErrorCount;

        /// <summary>
        /// 저장 성공률 (%)
        /// </summary>
        public double SaveSuccessRate => ValidationErrorCount > 0 
            ? (SavedErrorCount / (double)ValidationErrorCount) * 100 
            : 100.0;
    }

    /// <summary>
    /// 저장되지 않은 오류 상세 정보
    /// </summary>
    public class MissingErrorDetail
    {
        /// <summary>
        /// 단계 번호
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 오류 타입
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// 소스 테이블/클래스
        /// </summary>
        public string SourceClass { get; set; } = string.Empty;

        /// <summary>
        /// 소스 객체 ID
        /// </summary>
        public long SourceOID { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 저장 실패 원인
        /// </summary>
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// 분석 상태
    /// </summary>
    public enum AnalysisStatus
    {
        /// <summary>
        /// 대기 중
        /// </summary>
        Pending,

        /// <summary>
        /// 분석 중
        /// </summary>
        Analyzing,

        /// <summary>
        /// 완료
        /// </summary>
        Completed,

        /// <summary>
        /// 실패
        /// </summary>
        Failed
    }
}
