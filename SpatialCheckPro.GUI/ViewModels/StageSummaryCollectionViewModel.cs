#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.Models.Enums;
using SpatialCheckPro.Services.RemainingTime;
using SpatialCheckPro.Services.RemainingTime.Models;
using System.Windows;

namespace SpatialCheckPro.GUI.ViewModels
{
    /// <summary>
    /// 전체 검수 단계 요약을 관리하는 뷰모델
    /// </summary>
    public class StageSummaryCollectionViewModel : INotifyPropertyChanged
    {
        private readonly Dictionary<string, StageSummaryViewModel> _stageLookup;
        private readonly ObservableCollection<StageSummaryViewModel> _stages;
        private readonly ObservableCollection<AlertViewModel> _alerts = new();
        private readonly AlertAggregationService _alertAggregationService;
        private readonly IRemainingTimeEstimator _etaEstimator;
        private ValidationRunContext _currentContext = new();
        private OverallEtaResult _cachedOverallEta = new(null, 0, Array.Empty<StageEtaResult>());

        /// <summary>
        /// 단계 요약 목록
        /// </summary>
        public ReadOnlyObservableCollection<StageSummaryViewModel> Stages { get; }
        public ReadOnlyObservableCollection<StageSummaryViewModel> StageSummaries => Stages;

        /// <summary>
        /// 전체 알림 목록
        /// </summary>
        public ReadOnlyObservableCollection<AlertViewModel> Alerts { get; }

        private int _completedStageCount = 0;

        /// <summary>
        /// 완료된 단계 수
        /// </summary>
        public int CompletedStageCount
        {
            get => _completedStageCount;
            private set
            {
                if (_completedStageCount != value)
                {
                    _completedStageCount = value;
                    OnPropertyChanged(nameof(CompletedStageCount));
                }
            }
        }

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
                return _cachedOverallEta.EstimatedRemaining;
            }
        }

        public double RemainingEtaConfidence => _cachedOverallEta.Confidence;

        /// <summary>
        /// 수집 뷰모델 생성자
        /// </summary>
        public StageSummaryCollectionViewModel(IRemainingTimeEstimator etaEstimator, AlertAggregationService? alertAggregationService = null)
        {
            _etaEstimator = etaEstimator ?? throw new ArgumentNullException(nameof(etaEstimator));
            _stageLookup = StageDefinitions.All.ToDictionary(def => def.StageId, def => new StageSummaryViewModel(def));
            _stages = new ObservableCollection<StageSummaryViewModel>(_stageLookup.Values.OrderBy(stage => stage.StageNumber));
            Stages = new ReadOnlyObservableCollection<StageSummaryViewModel>(_stages);
            Alerts = new ReadOnlyObservableCollection<AlertViewModel>(_alerts);
            _alertAggregationService = alertAggregationService ?? new AlertAggregationService(NullLogger<AlertAggregationService>.Instance);
            _alertAggregationService.AlertsAggregated += OnAlertsAggregated;
            _cachedOverallEta = new OverallEtaResult(null, 0, Array.Empty<StageEtaResult>());
        }

        /// <summary>
        /// 수집 상태를 초기화합니다
        /// </summary>
        public void Reset()
        {
            _etaEstimator.SeedPredictions(new Dictionary<int, double>(), _currentContext);
            foreach (var stage in _stages)
            {
                stage.Reset();
            }
            _alerts.Clear();
            _cachedOverallEta = new OverallEtaResult(null, 0, Array.Empty<StageEtaResult>());
            UpdateCompletedStageCount();
            OnPropertyChanged(nameof(RemainingTotalEta));
            OnPropertyChanged(nameof(RemainingEtaConfidence));
        }

        /// <summary>
        /// ETA 초기값을 설정합니다
        /// </summary>
        /// <param name="predictedTimes">단계별 예측 시간(초)</param>
        /// <param name="context">현재 검수 컨텍스트</param>
        public void InitializeEta(IDictionary<int, double> predictedTimes, ValidationRunContext context)
        {
            _currentContext = context;
            _etaEstimator.SeedPredictions(predictedTimes, context);

            foreach (var definition in StageDefinitions.All)
            {
                var stage = GetOrCreateStage(definition.StageNumber);
                if (predictedTimes.TryGetValue(definition.StageNumber, out var seconds))
                {
                    stage.SetPredictedDuration(TimeSpan.FromSeconds(seconds));
                }
                else
                {
                    var eta = _etaEstimator.GetStageEta(stage.StageId);
                    stage.SetPredictedDuration(eta?.EstimatedRemaining ?? TimeSpan.Zero);
                    if (eta != null)
                    {
                        stage.ApplyEta(eta);
                    }
                }
            }

            _cachedOverallEta = _etaEstimator.GetOverallEta();
            OnPropertyChanged(nameof(RemainingTotalEta));
            OnPropertyChanged(nameof(RemainingEtaConfidence));
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

            var oldStatus = stage.Status;
            stage.ApplyProgress(args.StageProgress, args.StatusMessage, args.IsStageCompleted, args.IsStageSuccessful, args.IsStageSkipped, args.ProcessedUnits, args.TotalUnits);

            // [해결 방향 2] 상태 변경 감지 개선: isCompleted 플래그로 직접 판단
            // 단계가 완료되었거나 상태가 변경된 경우 무조건 업데이트
            if (args.IsStageCompleted || oldStatus != stage.Status)
            {
                UpdateCompletedStageCount();
                System.Console.WriteLine($"[ApplyProgress] 완료 단계 수 업데이트: {CompletedStageCount}/6 (Stage={args.CurrentStage}, IsCompleted={args.IsStageCompleted}, OldStatus={oldStatus}, NewStatus={stage.Status})");
            }

            var etaResult = _etaEstimator.UpdateProgress(new StageProgressSample
            {
                StageId = stage.StageId,
                StageNumber = stage.StageNumber,
                StageName = stage.StageName,
                ObservedAt = stage.LastUpdatedAt,
                ProgressPercent = args.StageProgress,
                ProcessedUnits = args.ProcessedUnits,
                TotalUnits = args.TotalUnits,
                StartedAt = stage.StartedAt,
                IsCompleted = args.IsStageCompleted,
                IsSuccessful = args.IsStageSuccessful,
                IsSkipped = args.IsStageSkipped
            });

            stage.ApplyEta(etaResult);
            _cachedOverallEta = _etaEstimator.GetOverallEta();
            OnPropertyChanged(nameof(RemainingTotalEta));
            OnPropertyChanged(nameof(RemainingEtaConfidence));

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
            var oldStatus = stage.Status;
            stage.ForceStatus(status);
            if (!string.IsNullOrWhiteSpace(message))
            {
                var isCompleted = status is StageStatus.Completed or StageStatus.CompletedWithWarnings;
                stage.ApplyProgress(stage.Progress, message, isCompleted, status == StageStatus.Completed, status == StageStatus.Skipped, stage.ProcessedUnits, stage.TotalUnits);
                var etaResult = _etaEstimator.UpdateProgress(new StageProgressSample
                {
                    StageId = stage.StageId,
                    StageNumber = stage.StageNumber,
                    StageName = stage.StageName,
                    ObservedAt = stage.LastUpdatedAt,
                    ProgressPercent = stage.Progress,
                    ProcessedUnits = stage.ProcessedUnits,
                    TotalUnits = stage.TotalUnits,
                    StartedAt = stage.StartedAt,
                    IsCompleted = isCompleted,
                    IsSuccessful = status == StageStatus.Completed,
                    IsSkipped = status == StageStatus.Skipped
                });
                stage.ApplyEta(etaResult);
                _cachedOverallEta = _etaEstimator.GetOverallEta();
                OnPropertyChanged(nameof(RemainingTotalEta));
                OnPropertyChanged(nameof(RemainingEtaConfidence));
            }
            
            // [해결 방향 2] 완료 상태로 변경된 경우 무조건 업데이트
            var isCompletedStatus = status is StageStatus.Completed or StageStatus.CompletedWithWarnings or StageStatus.Skipped;
            var wasCompletedStatus = oldStatus is StageStatus.Completed or StageStatus.CompletedWithWarnings or StageStatus.Skipped;
            
            // 완료 상태가 변경되었거나 상태가 변경된 경우 업데이트
            if (isCompletedStatus != wasCompletedStatus || oldStatus != stage.Status)
            {
                UpdateCompletedStageCount();
                System.Console.WriteLine($"[ForceStageStatus] 완료 단계 수 업데이트: {CompletedStageCount}/6 (Stage={stageNumber}, Status={status}, OldStatus={oldStatus})");
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
            void UpdateAlerts()
            {
                _alerts.Clear();
                foreach (var alert in aggregatedAlerts)
                {
                    _alerts.Add(alert);
                }
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                UpdateAlerts();
            }
            else if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(UpdateAlerts);
            }
            else
            {
                UpdateAlerts();
            }
        }

        /// <summary>
        /// 완료 단계 수를 업데이트합니다
        /// Failed 상태도 실행이 완료된 것으로 간주합니다
        /// </summary>
        private void UpdateCompletedStageCount()
        {
            var newCount = _stages.Count(stage => stage.Status is StageStatus.Completed or StageStatus.CompletedWithWarnings or StageStatus.Skipped or StageStatus.Failed);
            CompletedStageCount = newCount;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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


