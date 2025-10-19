#nullable enable
using System;

namespace SpatialCheckPro.Services.RemainingTime.Models
{
    /// <summary>
    /// ETA 계산을 위한 단계별 진행 샘플
    /// </summary>
    public class StageProgressSample
    {
        /// <summary>
        /// 단계 식별자
        /// </summary>
        public string StageId { get; init; } = string.Empty;

        /// <summary>
        /// 단계 번호
        /// </summary>
        public int StageNumber { get; init; }

        /// <summary>
        /// 단계 표시명
        /// </summary>
        public string StageName { get; init; } = string.Empty;

        /// <summary>
        /// 샘플 측정 시각
        /// </summary>
        public DateTimeOffset ObservedAt { get; init; }

        /// <summary>
        /// 진행률 (0-100)
        /// </summary>
        public double ProgressPercent { get; init; }

        /// <summary>
        /// 처리된 단위 수 (-1이면 알 수 없음)
        /// </summary>
        public long ProcessedUnits { get; init; } = -1;

        /// <summary>
        /// 전체 단위 수 (-1이면 알 수 없음)
        /// </summary>
        public long TotalUnits { get; init; } = -1;

        /// <summary>
        /// 단계 시작 시각
        /// </summary>
        public DateTimeOffset? StartedAt { get; init; }

        /// <summary>
        /// 단계 완료 여부
        /// </summary>
        public bool IsCompleted { get; init; }

        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsSuccessful { get; init; }

        /// <summary>
        /// 단계 스킵 여부
        /// </summary>
        public bool IsSkipped { get; init; }
    }
}


