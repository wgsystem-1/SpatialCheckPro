# 검수 대시보드 ETA 계산 로직

검수 대시보드 ETA 계산 로직을 찾기 위해 관련 키워드(예: ETA/Remaining/예상/남은/Progress)를 전역에서 동시에 검색하겠습니다. 이후 핵심 파일을 열어 코드 인용과 함께 분석하겠습니다.

[19 tools called]

방금 대시보드 ETA(예상 남은 시간) 계산 경로를 전역에서 확인했고, GUI ViewModel + 런타임 추정기 + 초기 예측기의 3단 구성으로 동작함을 검증했습니다. 핵심 로직과 산출식은 아래와 같습니다.

## 전체 구조

- 초기 예측(시작 직후): `ValidationTimePredictor`가 과거 이력 기반으로 단계별 예상 시간을 산출해 시드로 주입
  - ```448:456:SpatialCheckPro.GUI/MainWindow.xaml.cs
  var predictor = new Models.ValidationTimePredictor(...);
  var predictedTimes = predictor.PredictStageTimes(...);
  _logger?.LogInformation("예측 시간 계산 완료:");
  foreach (var kvp in predictedTimes) { ... }
```
  - ```186:206:SpatialCheckPro.GUI/ViewModels/StageSummaryCollectionViewModel.cs
_etaEstimator.SeedPredictions(predictedTimes, context);
foreach (var definition in StageDefinitions.All)
{
    var stage = GetOrCreateStage(definition.StageNumber);
    if (predictedTimes.TryGetValue(...))
        stage.SetPredictedDuration(TimeSpan.FromSeconds(seconds));
    else
        stage.SetPredictedDuration(eta?.EstimatedRemaining ?? TimeSpan.Zero);
}
```
- 실시간 추정(진행 중): `AdaptiveRemainingTimeEstimator`가 매 진행 이벤트마다 ETA를 갱신(EWMA 스무딩, 처리율/진행률 기반)
  - ```228:249:SpatialCheckPro.GUI/ViewModels/StageSummaryCollectionViewModel.cs
  var etaResult = _etaEstimator.UpdateProgress(new StageProgressSample { ... });
  stage.ApplyEta(etaResult);
  _cachedOverallEta = _etaEstimator.GetOverallEta();
```
- 표시: 전체 ETA는 남은 단계들의 ETA 합, 신뢰도는 평균값
  - ```113:148:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
foreach (var state in _stageStates.Values.OrderBy(s => s.StageNumber))
{
    var eta = EstimateRemaining(state, ...);
    var confidence = CalculateConfidence(state);
    remainingStages.Add(ToResult(state, eta, confidence, hint));
    if (eta.HasValue) totalSeconds += Math.Max(0, eta.Value.TotalSeconds);
}
var overallEta = totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : (TimeSpan?)null;
var overallConfidence = confidences.Count > 0 ? confidences.Average() : MinimumConfidence;
```
  - UI 바인딩
    - ```126:131:SpatialCheckPro.GUI/Views/ValidationProgressView.xaml
    <TextBlock Text="예상 남은 시간" .../>
    <TextBlock x:Name="EstimatedTimeText" Text="계산 중..." .../>
```
    - ```126:131:SpatialCheckPro.GUI/Views/ValidationProgressView.xaml.cs
EstimatedTimeText.Text = _stageSummaries.RemainingTotalEta.HasValue
    ? FormatRemainingLabel(_stageSummaries.RemainingTotalEta.Value.TotalSeconds)
    : "계산 중...";
```
```
- 진행률 배지의 신뢰도(%) 표시
  - ```78:85:SpatialCheckPro.GUI/Views/ValidationProgressView.xaml
```
<TextBlock Text="예상 남은 시간" .../>
<TextBlock Text="{Binding RemainingEtaConfidence, StringFormat=({0:P0})}" .../>
```

### 단계별 ETA 산출식(Estimator)
- 단위 처리율 기반(우선 적용, 단위 수가 알려진 경우):
  - 남은초 = (전체단위 − 처리단위) ÷ 스무딩처리율
  - ```197:205:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
if (processedUnits > 0 && totalUnits > 0 && ... state.SmoothedUnitRate > 0)
{
    var remainingSeconds = (totalUnits - processedUnits) / state.SmoothedUnitRate;
    return TimeSpan.FromSeconds(remainingSeconds);
}
```
- 진행률 기반(초기 5% 이후, 처리단위 불명·혹은 보조 경로):
  - 남은초 = (경과초 ÷ 진행비율) − 경과초
  - ```207:215:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
  if (state.LastProgressPercent > ProgressSaturationThreshold*100 && ... state.SmoothedProgressRate > 0)
  {
    var estimatedTotal = elapsedSeconds / (state.LastProgressPercent/100.0);
    var remainingSeconds = estimatedTotal - elapsedSeconds;
    return TimeSpan.FromSeconds(remainingSeconds);
  }
```
- 데이터 부족 시 초기 예측값으로 폴백
  - ```217:219:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
return state.PredictedDuration;
```

## 스무딩과 샘플링
- 적응형 EWMA(진행 구간별 알파):
  - 초기(0–20% 또는 샘플<5): α=0.6, 중기(20–70%): 0.35, 후기(70–100%): 0.15
  - ```242:258:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
  if (progressPercent < 20 || state.RecentUnitRates.Count < 5) return 0.6;
  if (progressPercent < 70) return 0.35;
  return 0.15;
```
- 처리율/진행률 갱신식:
  - s = α·현재측정 + (1−α)·이전스무딩
  - ```276:314:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
state.SmoothedUnitRate = state.SmoothedUnitRate <= 0 ? rate : alpha * rate + (1 - alpha) * state.SmoothedUnitRate;
state.SmoothedProgressRate = state.SmoothedProgressRate <= 0 ? rate : alpha * rate + (1 - alpha) * state.SmoothedProgressRate;
```
- 진행 이벤트는 단계 서비스에서 발생하며 1초 주기로 샘플링되는 구성도 존재
  - ```23:25:SpatialCheckPro/Services/ParallelProgressTracker.cs
  // 진행률 업데이트 타이머 (1초마다)
  _progressUpdateTimer = new Timer(UpdateProgress, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
```

### 신뢰도(Confidence) 계산
- 최솟값 0.1, 최대 0.95(완료/스킵 시 1.0)
- 스무딩된 처리율/진행률이 생기면 0.5로 상향
- 최근 처리율 표준편차/평균(CV) < 0.35 이고 샘플≥5면 +0.2 (≤0.95)
- 진행률 90% 이상이면 +0.1 (≤1.0)
- ```341:367:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
double confidence = MinimumConfidence; // 0.1
if (state.SmoothedUnitRate > 0 || state.SmoothedProgressRate > 0) confidence = 0.5;
if (sampleCount >= 5) { confidence = Math.Max(confidence, 0.7); if (deviation < 0.35) confidence = Math.Min(MaximumConfidence, confidence + 0.2); }
if (state.LastProgressPercent >= 90) confidence = Math.Min(1.0, confidence + 0.1);
```

## ViewModel 보조 계산(결핍 시)
- 각 단계 뷰모델은 자체적으로도 단순 ETA를 산출해 표시 폴백
  - 단위 처리율 기반 또는 진행률 기반, 없으면 초기 예측
  - ```412:436:SpatialCheckPro.GUI/ViewModels/StageSummaryViewModel.cs
  if (processedUnits > 0 && totalUnits > 0) { rate = processedUnits / elapsed; remaining = (total - processed)/rate; }
  else if (stageProgress > 0 && stageProgress < 100) { estimatedTotal = elapsed/(stageProgress/100); remaining = estimatedTotal - elapsed; }
  return _predictedDuration;
```

### 전체 ETA 집계와 표시
- 남은 단계들의 ETA를 합산하여 RemainingTotalEta로 노출, 신뢰도는 평균
  - ```143:148:SpatialCheckPro/Services/RemainingTime/AdaptiveRemainingTimeEstimator.cs
var overallEta = totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : (TimeSpan?)null;
var overallConfidence = confidences.Count > 0 ? confidences.Average() : MinimumConfidence;
```
- UI 배지에 “예상 남은 시간 (X%)”로 신뢰도 표기, 우측 상세에는 포맷된 잔여 시간 표시
  - ```128:140:SpatialCheckPro.GUI/Views/ValidationProgressView.xaml.cs
    EstimatedTimeText.Text = _stageSummaries.RemainingTotalEta.HasValue
    ? FormatRemainingLabel(...)
    : "계산 중...";
```

### 요약
- 초기엔 이력 기반 단계별 예측(시드), 실행 중에는 EWMA로 스무딩한 처리율/진행률로 동적 추정.
- 단위 수가 알려지면 “남은단위/스무딩처리율”이 1순위, 아니면 진행률 역산, 둘 다 없으면 초기 예측으로 폴백.
- 전체 ETA는 “미완료 단계 ETA의 합”, 신뢰도는 “단계 신뢰도 평균”으로 산출되어 UI에 “예상 남은 시간 (신뢰도%)”로 표기됩니다.
```