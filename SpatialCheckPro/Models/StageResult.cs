using SpatialCheckPro.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 단계별 검수 결과를 나타내는 모델 클래스
    /// </summary>
    public class StageResult
    {
        /// <summary>
        /// 단계 ID
        /// </summary>
        public string StageId { get; set; } = string.Empty;

        /// <summary>
        /// 단계 번호 (1-4)
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 단계 상태
        /// </summary>
        public StageStatus Status { get; set; }

        /// <summary>
        /// 검수 항목 결과 목록
        /// </summary>
        public List<CheckResult> CheckResults { get; set; } = new List<CheckResult>();

        /// <summary>
        /// 단계 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 단계 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 오류 메시지 (실패 시)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 단계 소요 시간 (밀리초)
        /// </summary>
        public long ElapsedMilliseconds => CompletedAt.HasValue 
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds 
            : 0;

        /// <summary>
        /// 단계별 총 오류 개수
        /// </summary>
        public int TotalErrors => CheckResults.Sum(c => c.ErrorCount);

        /// <summary>
        /// 단계별 총 경고 개수
        /// </summary>
        public int TotalWarnings => CheckResults.Sum(c => c.WarningCount);

        /// <summary>
        /// 오류 개수 (TotalErrors와 동일)
        /// </summary>
        public int ErrorCount => TotalErrors;

        /// <summary>
        /// 경고 개수 (TotalWarnings와 동일)
        /// </summary>
        public int WarningCount => TotalWarnings;

        /// <summary>
        /// 오류 목록
        /// </summary>
        public List<ValidationError> Errors { get; set; } = new();

        /// <summary>
        /// 경고 목록
        /// </summary>
        public List<ValidationError> Warnings { get; set; } = new();

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsSuccess => Status == StageStatus.Completed && TotalErrors == 0;
    }
}
