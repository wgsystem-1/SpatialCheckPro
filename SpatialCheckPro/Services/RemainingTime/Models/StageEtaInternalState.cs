#nullable enable
using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Services.RemainingTime.Models
{
    /// <summary>
    /// 단계 ETA 계산에 사용되는 내부 상태
    /// </summary>
    internal class StageEtaInternalState
    {
        public string StageId { get; init; } = string.Empty;

        public int StageNumber { get; set; }

        public string StageName { get; set; } = string.Empty;

        public DateTimeOffset? StartedAt { get; set; }

        public double SmoothedUnitRate { get; set; }

        public double SmoothedProgressRate { get; set; }

        public long LastProcessedUnits { get; set; } = -1;

        public long TotalUnits { get; set; } = -1;

        public DateTimeOffset? LastObservedAt { get; set; }

        public double LastProgressPercent { get; set; }

        public double Confidence { get; set; }

        public bool IsCompleted { get; set; }

        public List<double> RecentDurationsSeconds { get; } = new();

        public List<double> RecentUnitRates { get; } = new();

        public List<double> HistoricalDurationsSeconds { get; } = new();

        public TimeSpan? PredictedDuration { get; set; }

        public string? DisplayHint { get; set; }
    }
}


