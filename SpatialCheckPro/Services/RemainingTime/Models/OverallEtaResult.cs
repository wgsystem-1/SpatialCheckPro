#nullable enable
using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Services.RemainingTime.Models
{
    /// <summary>
    /// 전체 검수 ETA 결과
    /// </summary>
    public record OverallEtaResult(
        TimeSpan? EstimatedRemaining,
        double Confidence,
        IReadOnlyList<StageEtaResult> StageResults);
}

 