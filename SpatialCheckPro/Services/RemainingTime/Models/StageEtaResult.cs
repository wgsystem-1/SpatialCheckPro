#nullable enable
using System;

namespace SpatialCheckPro.Services.RemainingTime.Models
{
    /// <summary>
    /// 단일 단계 ETA 결과 정보
    /// </summary>
    public record StageEtaResult(
        string StageId,
        int StageNumber,
        string StageName,
        TimeSpan? EstimatedRemaining,
        double Confidence,
        string? DisplayHint);
}

 