#nullable enable
using System;
using System.Collections.Generic;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 단계별 소요 시간 이력을 표현하는 데이터 샘플
    /// </summary>
    public class StageDurationSample
    {
        /// <summary>
        /// 단계 식별자
        /// </summary>
        public string StageId { get; set; } = string.Empty;

        /// <summary>
        /// 단계 번호
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 샘플이 수집된 시각
        /// </summary>
        public DateTime CollectedAt { get; set; }

        /// <summary>
        /// 단계 수행 결과 상태
        /// </summary>
        public StageStatus Status { get; set; }

        /// <summary>
        /// 단계 소요 시간
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 단계 처리에 사용된 전체 단위 수 (알 수 없는 경우 -1)
        /// </summary>
        public long TotalUnits { get; set; } = -1;

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSizeBytes { get; set; } = -1;

        /// <summary>
        /// 피처 수 (알 수 없는 경우 -1)
        /// </summary>
        public long FeatureCount { get; set; } = -1;

        /// <summary>
        /// 추가 메타데이터 (JSON 직렬화 용도)
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}


