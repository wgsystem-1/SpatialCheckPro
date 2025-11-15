using SpatialCheckPro.Models.Enums;
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 개별 검수 항목 결과를 나타내는 모델 클래스
    /// </summary>
    public class CheckResult
    {
        /// <summary>
        /// 검수 항목 ID
        /// </summary>
        public string CheckId { get; set; } = string.Empty;

        /// <summary>
        /// 검수 항목명
        /// </summary>
        public string CheckName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과 상태
        /// </summary>
        public CheckStatus Status { get; set; }

        /// <summary>
        /// 오류 목록
        /// </summary>
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();

        /// <summary>
        /// 경고 목록
        /// </summary>
        public List<ValidationError> Warnings { get; set; } = new List<ValidationError>();

        /// <summary>
        /// 검수 대상 개수
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 개수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 성공률 (백분율)
        /// </summary>
        public double SuccessRate => TotalCount > 0 
            ? ((double)(TotalCount - ErrorCount) / TotalCount) * 100 
            : 100;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsPassed => Status == CheckStatus.Passed;

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 검수 단계에서 제외된 객체 수
        /// </summary>
        public int SkippedCount { get; set; }
    }
}
