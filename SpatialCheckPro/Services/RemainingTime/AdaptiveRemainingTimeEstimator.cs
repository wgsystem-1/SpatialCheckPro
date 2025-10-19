#nullable enable
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Data;
using SpatialCheckPro.Data.Entities;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services.RemainingTime.Models;

namespace SpatialCheckPro.Services.RemainingTime
{
    /// <summary>
    /// 단계별 진행 샘플을 기반으로 ETA를 추정하는 구현체
    /// </summary>
    public class AdaptiveRemainingTimeEstimator : IRemainingTimeEstimator
    {
        private readonly ILogger<AdaptiveRemainingTimeEstimator> _logger;
        private readonly IDbContextFactory<ValidationDbContext> _dbContextFactory;
        private readonly ConcurrentDictionary<string, StageEtaInternalState> _stageStates = new();
        private readonly object _historyLock = new();
        private ValidationRunContext _context = new();

        private const double MinimumConfidence = 0.1;
        private const double MaximumConfidence = 0.95;
        private const double EwmaAlpha = 0.3;
        private const int RequiredSamplesForHighConfidence = 5;
        private const double ProgressSaturationThreshold = 0.05;

        public AdaptiveRemainingTimeEstimator(ILogger<AdaptiveRemainingTimeEstimator> logger, IDbContextFactory<ValidationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public void SeedPredictions(IDictionary<int, double> stagePredictions, ValidationRunContext context)
        {
            _context = context ?? new ValidationRunContext();
            foreach (var (stageNumber, seconds) in stagePredictions)
            {
                var stageId = GetStageId(stageNumber);
                var state = _stageStates.GetOrAdd(stageId, _ => new StageEtaInternalState
                {
                    StageId = stageId,
                    StageNumber = stageNumber
                });

                state.StageNumber = stageNumber;
                state.StageName = GetStageName(stageNumber, state.StageName);
                state.PredictedDuration = TimeSpan.FromSeconds(Math.Max(1, seconds));
            }
        }

        public StageEtaResult UpdateProgress(StageProgressSample sample)
        {
            var state = CreateOrUpdateState(sample);

            state.LastObservedAt = sample.ObservedAt;
            state.LastProgressPercent = sample.ProgressPercent;
            state.TotalUnits = sample.TotalUnits >= 0 ? sample.TotalUnits : state.TotalUnits;

            if (!state.StartedAt.HasValue && sample.StartedAt.HasValue)
            {
                state.StartedAt = sample.StartedAt;
            }

            if (sample.IsSkipped)
            {
                state.IsCompleted = true;
                state.Confidence = MaximumConfidence;
                return new StageEtaResult(sample.StageId, sample.StageNumber, sample.StageName, TimeSpan.Zero, 1.0, "스킵됨");
            }

            var elapsed = state.StartedAt.HasValue
                ? sample.ObservedAt - state.StartedAt.Value
                : TimeSpan.FromSeconds(sample.ObservedAt.ToUnixTimeSeconds());

            if (elapsed.TotalSeconds <= 0)
            {
                return ToResult(state, state.PredictedDuration, state.Confidence, "측정 대기 중");
            }

            if (sample.ProcessedUnits >= 0)
            {
                UpdateUnitRate(state, sample.ProcessedUnits, elapsed.TotalSeconds);
            }

            UpdateProgressRate(state, sample.ProgressPercent, elapsed.TotalSeconds);

            if (sample.IsCompleted)
            {
                state.IsCompleted = true;
                state.Confidence = MaximumConfidence;
                return new StageEtaResult(sample.StageId, sample.StageNumber, sample.StageName, TimeSpan.Zero, 1.0, "완료됨");
            }

            var eta = EstimateRemaining(state, elapsed.TotalSeconds, sample.ProcessedUnits, sample.TotalUnits);
            var hint = BuildDisplayHint(state, eta);
            var confidence = CalculateConfidence(state);

            state.DisplayHint = hint;
            state.Confidence = confidence;

            return ToResult(state, eta, confidence, hint);
        }

        public StageEtaResult? GetStageEta(string stageId)
        {
            return _stageStates.TryGetValue(stageId, out var state)
                ? ToResult(state, state.PredictedDuration, state.Confidence, state.DisplayHint)
                : null;
        }

        public OverallEtaResult GetOverallEta()
        {
            var remainingStages = new List<StageEtaResult>();
            double totalSeconds = 0;
            var confidences = new List<double>();

            foreach (var state in _stageStates.Values.OrderBy(s => s.StageNumber))
            {
                if (state.IsCompleted)
                {
                    continue;
                }

                var eta = EstimateRemaining(state, GetElapsedSeconds(state), state.LastProcessedUnits, state.TotalUnits);
                var confidence = CalculateConfidence(state);
                var hint = BuildDisplayHint(state, eta);
                var result = ToResult(state, eta, confidence, hint);
                remainingStages.Add(result);

                if (eta.HasValue)
                {
                    totalSeconds += Math.Max(0, eta.Value.TotalSeconds);
                    confidences.Add(confidence);
                }
            }

            if (remainingStages.Count == 0)
            {
                return new OverallEtaResult(TimeSpan.Zero, 1.0, Array.Empty<StageEtaResult>());
            }

            var overallEta = totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : (TimeSpan?)null;
            var overallConfidence = confidences.Count > 0 ? confidences.Average() : MinimumConfidence;

            return new OverallEtaResult(overallEta, overallConfidence, remainingStages);
        }

        public async Task RecordStageHistoryAsync(IEnumerable<StageDurationSample> history)
        {
            var entities = new List<StageDurationHistoryEntity>();
            foreach (var sample in history)
            {
                if (sample.Duration <= TimeSpan.Zero)
                {
                    continue;
                }

                entities.Add(new StageDurationHistoryEntity
                {
                    StageId = sample.StageId,
                    StageNumber = sample.StageNumber,
                    StageName = string.IsNullOrWhiteSpace(sample.StageName) ? $"단계 {sample.StageNumber}" : sample.StageName,
                    Status = sample.Status,
                    DurationSeconds = sample.Duration.TotalSeconds,
                    TotalUnits = sample.TotalUnits,
                    FeatureCount = sample.FeatureCount,
                    FileSizeBytes = sample.FileSizeBytes,
                    CoordinateSystem = sample.Metadata.TryGetValue("CoordinateSystem", out var epsg) ? epsg : null,
                    CollectedAtUtc = sample.CollectedAt.ToUniversalTime(),
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(sample.Metadata)
                });
            }

            if (entities.Count == 0)
            {
                return;
            }

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            lock (_historyLock)
            {
                dbContext.StageDurationHistory.AddRange(entities);
            }

            await dbContext.SaveChangesAsync();
        }

        private TimeSpan? EstimateRemaining(StageEtaInternalState state, double elapsedSeconds, long processedUnits, long totalUnits)
        {
            if (state.IsCompleted)
            {
                return TimeSpan.Zero;
            }

            if (processedUnits > 0 && totalUnits > 0 && processedUnits < totalUnits && state.SmoothedUnitRate > 0.0001)
            {
                var remainingUnits = totalUnits - processedUnits;
                var remainingSeconds = remainingUnits / state.SmoothedUnitRate;
                if (double.IsFinite(remainingSeconds) && remainingSeconds >= 0)
                {
                    return TimeSpan.FromSeconds(remainingSeconds);
                }
            }

            if (state.LastProgressPercent > ProgressSaturationThreshold * 100 && state.LastProgressPercent < 100 && state.SmoothedProgressRate > 0.0001)
            {
                var progressRatio = state.LastProgressPercent / 100.0;
                var estimatedTotal = elapsedSeconds / progressRatio;
                var remainingSeconds = estimatedTotal - elapsedSeconds;
                if (double.IsFinite(remainingSeconds) && remainingSeconds >= 0)
                {
                    return TimeSpan.FromSeconds(remainingSeconds);
                }
            }

            return state.PredictedDuration;
        }

        private static double GetElapsedSeconds(StageEtaInternalState state)
        {
            if (!state.StartedAt.HasValue || !state.LastObservedAt.HasValue)
            {
                return 0;
            }

            return Math.Max(0, (state.LastObservedAt.Value - state.StartedAt.Value).TotalSeconds);
        }

        private static StageEtaResult ToResult(StageEtaInternalState state, TimeSpan? eta, double confidence, string? hint)
        {
            return new StageEtaResult(state.StageId, state.StageNumber, state.StageName, eta, confidence, hint);
        }

        private void UpdateUnitRate(StageEtaInternalState state, long processedUnits, double elapsedSeconds)
        {
            if (processedUnits <= 0 || elapsedSeconds <= 0)
            {
                return;
            }

            var rate = processedUnits / Math.Max(1.0, elapsedSeconds);
            if (!double.IsFinite(rate) || rate <= 0)
            {
                return;
            }

            state.LastProcessedUnits = processedUnits;
            state.SmoothedUnitRate = state.SmoothedUnitRate <= 0
                ? rate
                : EwmaAlpha * rate + (1 - EwmaAlpha) * state.SmoothedUnitRate;

            state.RecentUnitRates.Add(rate);
            if (state.RecentUnitRates.Count > 30)
            {
                state.RecentUnitRates.RemoveAt(0);
            }
        }

        private void UpdateProgressRate(StageEtaInternalState state, double progressPercent, double elapsedSeconds)
        {
            if (elapsedSeconds <= 1)
            {
                return;
            }

            if (progressPercent <= 0)
            {
                return;
            }

            var progressRatio = progressPercent / 100.0;
            var rate = progressRatio / elapsedSeconds;
            if (!double.IsFinite(rate) || rate <= 0)
            {
                return;
            }

            state.SmoothedProgressRate = state.SmoothedProgressRate <= 0
                ? rate
                : EwmaAlpha * rate + (1 - EwmaAlpha) * state.SmoothedProgressRate;
        }

        private string? BuildDisplayHint(StageEtaInternalState state, TimeSpan? eta)
        {
            if (!eta.HasValue)
            {
                return null;
            }

            var seconds = Math.Max(0, eta.Value.TotalSeconds);
            if (seconds < 60)
            {
                return $"약 {Math.Ceiling(seconds)}초";
            }

            if (seconds < 3600)
            {
                var minutes = Math.Floor(seconds / 60);
                var remainingSeconds = seconds % 60;
                return $"약 {minutes}분 {Math.Ceiling(remainingSeconds)}초";
            }

            var hours = Math.Floor(seconds / 3600);
            var minutesPart = Math.Floor((seconds % 3600) / 60);
            return $"약 {hours}시간 {minutesPart}분";
        }

        private double CalculateConfidence(StageEtaInternalState state)
        {
            double confidence = MinimumConfidence;

            if (state.SmoothedUnitRate > 0 || state.SmoothedProgressRate > 0)
            {
                confidence = 0.5;
            }

            var sampleCount = state.RecentUnitRates.Count;
            if (sampleCount >= RequiredSamplesForHighConfidence)
            {
                confidence = Math.Max(confidence, 0.7);
                var deviation = CalculateDeviation(state.RecentUnitRates);
                if (deviation < 0.35)
                {
                    confidence = Math.Min(MaximumConfidence, confidence + 0.2);
                }
            }

            if (state.LastProgressPercent >= 90)
            {
                confidence = Math.Min(1.0, confidence + 0.1);
            }

            return Math.Clamp(confidence, MinimumConfidence, 1.0);
        }

        private static double CalculateDeviation(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 1.0;
            }

            var mean = values.Average();
            if (mean <= 0)
            {
                return 1.0;
            }

            var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
            var stdDev = Math.Sqrt(variance);
            return stdDev / mean;
        }

        private StageEtaInternalState CreateOrUpdateState(StageProgressSample sample)
        {
            var stageId = string.IsNullOrWhiteSpace(sample.StageId)
                ? $"stage_{sample.StageNumber}"
                : sample.StageId;

            var state = _stageStates.GetOrAdd(stageId, _ => new StageEtaInternalState
            {
                StageId = stageId,
                StageNumber = sample.StageNumber,
                StageName = string.IsNullOrWhiteSpace(sample.StageName) ? $"단계 {sample.StageNumber}" : sample.StageName
            });

            state.StageNumber = sample.StageNumber;
            if (!string.IsNullOrWhiteSpace(sample.StageName))
            {
                state.StageName = sample.StageName;
            }

            if (string.IsNullOrWhiteSpace(state.StageName))
            {
                if (_context.Metadata.TryGetValue($"StageName_{sample.StageNumber}", out var stageName) && !string.IsNullOrWhiteSpace(stageName))
                {
                    state.StageName = stageName;
                }
                else
                {
                    state.StageName = $"단계 {sample.StageNumber}";
                }
            }

            if (!state.StartedAt.HasValue)
            {
                state.StartedAt = sample.StartedAt;
            }

            return state;
        }

        private string GetStageId(int stageNumber)
        {
            if (_context.Metadata.TryGetValue($"StageId_{stageNumber}", out var stageId) && !string.IsNullOrWhiteSpace(stageId))
            {
                return stageId;
            }

            return $"stage_{stageNumber}";
        }

        private string GetStageName(int stageNumber, string currentName)
        {
            if (!string.IsNullOrWhiteSpace(currentName) && currentName != $"단계 {stageNumber}")
            {
                return currentName;
            }

            if (_context.Metadata.TryGetValue($"StageName_{stageNumber}", out var stageName) && !string.IsNullOrWhiteSpace(stageName))
            {
                return stageName;
            }

            return $"단계 {stageNumber}";
        }
    }
}


