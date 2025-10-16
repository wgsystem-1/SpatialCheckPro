#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.GUI.ViewModels
{
    /// <summary>
    /// 전체 검수 단계 요약을 관리하는 뷰모델
    /// </summary>
    public class StageSummaryCollectionViewModel
    {
        private readonly Dictionary<string, StageSummaryViewModel> _stageLookup;
        private readonly ObservableCollection<StageSummaryViewModel> _stages;
        private readonly ObservableCollection<AlertViewModel> _alerts = new();
        private readonly AlertAggregationService _alertAggregationService;

        /// <summary>
        /// 단계 요약 목록
        /// </summary>
        public ReadOnlyObservableCollection<StageSummaryViewModel> Stages { get; }
        public ReadOnlyObservableCollection<StageSummaryViewModel> StageSummaries => Stages;

        /// <summary>
        /// 전체 알림 목록
        /// </summary>
        public ReadOnlyObservableCollection<AlertViewModel> Alerts { get; }

        /// <summary>
        /// 완료된 단계 수
        /// </summary>
        public int CompletedStageCount => _stages.Count(stage => stage.Status is StageStatus.Completed or StageStatus.CompletedWithWarnings or StageStatus.Skipped);

        /// <summary>
        /// 현재 실행 중인 단계
        /// </summary>
        public StageSummaryViewModel? ActiveStage => _stages.FirstOrDefault(stage => stage.IsActive);

        /// <summary>
        /// 단계별 ETA 합계
        /// </summary>
        public TimeSpan? RemainingTotalEta
        {
            get
            {
                var remaining = _stages
                    .Select(stage => stage.EstimatedRemaining)
                    .Where(span => span.HasValue)
                    .Select(span => span!.Value)
                    .Where(span => span > TimeSpan.Zero)
                    .ToList();

                if (remaining.Count == 0)
                {
                    return null;
                }

                return TimeSpan.FromSeconds(remaining.Sum(span => span.TotalSeconds));
            }
        }

        /// <summary>
        /// 수집 뷰모델 생성자
        /// </summary>
        public StageSummaryCollectionViewModel(AlertAggregationService? alertAggregationService = null)
        {
            _stageLookup = StageDefinitions.All.ToDictionary(def => def.StageId, def => new StageSummaryViewModel(def));
            _stages = new ObservableCollection<StageSummaryViewModel>(_stageLookup.Values.OrderBy(stage => stage.StageNumber));
            Stages = new ReadOnlyObservableCollection<StageSummaryViewModel>(_stages);
            Alerts = new ReadOnlyObservableCollection<AlertViewModel>(_alerts);
            _alertAggregationService = alertAggregationService ?? new AlertAggregationService(NullLogger<AlertAggregationService>.Instance);
            _alertAggregationService.AlertsAggregated += OnAlertsAggregated;
        }

        /// <summary>
        /// 수집 상태를 초기화합니다
        /// </summary>
        public void Reset()
        {
            foreach (var stage in _stages)
            {
                stage.Reset();
            }
            _alerts.Clear();
        }

        /// <summary>
        /// 예측된 단계 소요 시간을 적용합니다
        /// </summary>
        /// <param name="predictedTimes">단계별 예측 시간(초)</param>
        public void ApplyPredictedTimes(IDictionary<int, double> predictedTimes)
        {
            foreach (var kvp in predictedTimes)
            {
                var stage = GetOrCreateStage(kvp.Key);
                stage.SetPredictedDuration(TimeSpan.FromSeconds(kvp.Value));
            }
        }

        /// <summary>
        /// 진행률 이벤트를 반영합니다
        /// </summary>
        /// <param name="args">진행률 이벤트 인자</param>
        public void ApplyProgress(ValidationProgressEventArgs args)
        {
            var stageId = StageDefinitions.GetStageId(args.CurrentStage);
            if (!_stageLookup.TryGetValue(stageId, out var stage))
            {
                stage = new StageSummaryViewModel(StageDefinitions.GetByNumber(args.CurrentStage));
                _stageLookup[stageId] = stage;
                _stages.Add(stage);
            }

            stage.ApplyProgress(args.StageProgress, args.StatusMessage, args.IsStageCompleted, args.IsStageSuccessful, args.IsStageSkipped, args.ProcessedUnits, args.TotalUnits);

            if (!string.IsNullOrWhiteSpace(args.StageName) && stage.StageName != args.StageName)
            {
                stage.StageName = args.StageName;
            }

            if (args.IsStageCompleted && !args.IsStageSuccessful)
            {
                AddOrUpdateAlert(stage, ErrorSeverity.Error, args.StatusMessage, null, StageStatus.Failed);
            }
            else if (!args.IsStageCompleted && args.StageProgress > 0 && stage.HasAlerts)
            {
                // 진행 중에 오류가 해결된 경우 알림 해제
                RemoveAlertsForStage(stage.StageId, AlertClearReason.ProgressRecovered);
            }
        }

        /// <summary>
        /// 단계별 외부 알림을 등록합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        /// <param name="severity">심각도</param>
        /// <param name="message">메시지</param>
        /// <param name="detail">세부 정보</param>
        /// <param name="stageStatus">관련 단계 상태</param>
        public void RegisterAlert(int stageNumber, ErrorSeverity severity, string message, string? detail, StageStatus stageStatus)
        {
            var stage = GetOrCreateStage(stageNumber);
            AddOrUpdateAlert(stage, severity, message, detail, stageStatus);
        }

        /// <summary>
        /// 단계 상태를 강제로 설정합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        /// <param name="status">강제 상태</param>
        /// <param name="message">상태 메시지</param>
        public void ForceStageStatus(int stageNumber, StageStatus status, string message)
        {
            var stage = GetOrCreateStage(stageNumber);
            stage.ForceStatus(status);
            if (!string.IsNullOrWhiteSpace(message))
            {
                stage.ApplyProgress(stage.Progress, message, status is StageStatus.Completed or StageStatus.CompletedWithWarnings, status == StageStatus.Completed, status == StageStatus.Skipped, stage.ProcessedUnits, stage.TotalUnits);
            }
        }

        private StageSummaryViewModel GetOrCreateStage(int stageNumber)
        {
            var stageId = StageDefinitions.GetStageId(stageNumber);
            if (!_stageLookup.TryGetValue(stageId, out var stage))
            {
                stage = new StageSummaryViewModel(StageDefinitions.GetByNumber(stageNumber));
                _stageLookup[stageId] = stage;
                _stages.Add(stage);
            }
            return stage;
        }

        /// <summary>
        /// 단계 번호로 요약 뷰모델을 반환합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        /// <returns>단계 요약 뷰모델 또는 null</returns>
        public StageSummaryViewModel? GetStage(int stageNumber)
        {
            var stageId = StageDefinitions.GetStageId(stageNumber);
            return _stageLookup.TryGetValue(stageId, out var stage) ? stage : null;
        }

        private void AddOrUpdateAlert(StageSummaryViewModel stage, ErrorSeverity severity, string message, string? detail, StageStatus stageStatus)
        {
            var existing = _alerts.FirstOrDefault(alert => alert.AlertKey == stage.StageId);
            if (existing != null)
            {
                existing.Update(severity, message, detail, stageStatus);
                _alertAggregationService.EnqueueAlert(existing);
            }
            else
            {
                var alert = new AlertViewModel(stage.StageId, stage.StageNumber, stage.StageName);
                alert.Update(severity, message, detail, stageStatus);
                stage.Alerts.Add(alert);
                _alerts.Add(alert);
                _alertAggregationService.EnqueueAlert(alert);
            }
        }

        /// <summary>
        /// 단계 알림을 제거합니다
        /// </summary>
        /// <param name="stageId">단계 식별자</param>
        /// <param name="reason">제거 이유</param>
        public void RemoveAlertsForStage(string stageId, AlertClearReason reason)
        {
            var target = _alerts.FirstOrDefault(alert => alert.AlertKey == stageId);
            if (target == null)
            {
                return;
            }

            switch (reason)
            {
                case AlertClearReason.Resolved:
                case AlertClearReason.ProgressRecovered:
                    _alerts.Remove(target);
                    if (_stageLookup.TryGetValue(stageId, out var stage))
                    {
                        stage.Alerts.Remove(target);
                    }
                    _alertAggregationService.RemoveAlert(stageId);
                    break;
                case AlertClearReason.Manual:
                    target.Update(ErrorSeverity.Info, "알림이 수동으로 해제되었습니다.", null, StageStatus.CompletedWithWarnings);
                    _alertAggregationService.EnqueueAlert(target);
                    break;
            }
        }

        private void OnAlertsAggregated(object? sender, IReadOnlyCollection<AlertViewModel> aggregatedAlerts)
        {
            _alerts.Clear();
            foreach (var alert in aggregatedAlerts)
            {
                _alerts.Add(alert);
            }
        }
    }

    /// <summary>
    /// 알림 해제 사유
    /// </summary>
    public enum AlertClearReason
    {
        /// <summary>
        /// 오류가 해결됨
        /// </summary>
        Resolved,

        /// <summary>
        /// 진행률이 회복되어 최근 오류 표시 제거
        /// </summary>
        ProgressRecovered,

        /// <summary>
        /// 사용자가 수동으로 알림을 해제함
        /// </summary>
        Manual
    }
}


