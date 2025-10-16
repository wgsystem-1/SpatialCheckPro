using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 단계별 병렬 처리 관리자
    /// 독립적인 단계들을 병렬로 실행하여 전체 검수 시간을 단축
    /// </summary>
    public class StageParallelProcessingManager
    {
        private readonly ILogger<StageParallelProcessingManager> _logger;
        private readonly AdvancedParallelProcessingManager _parallelProcessingManager;
        private readonly PerformanceSettings _performanceSettings;

        public StageParallelProcessingManager(
            ILogger<StageParallelProcessingManager> logger,
            AdvancedParallelProcessingManager parallelProcessingManager,
            PerformanceSettings performanceSettings)
        {
            _logger = logger;
            _parallelProcessingManager = parallelProcessingManager;
            _performanceSettings = performanceSettings;
        }

        /// <summary>
        /// 검수 단계들을 독립성에 따라 그룹화하여 병렬 실행
        /// </summary>
        public async Task<StageParallelResult> ExecuteStagesInParallelAsync(
            Func<Task<object>> stage0Func,
            Func<Task<object>> stage1Func,
            Func<object, Task<object>> stage2Func,
            Func<object, Task<object>> stage3Func,
            Func<Task<object>> stage4Func,
            Func<Task<object>> stage5Func,
            bool[] enabledStages)
        {
            var result = new StageParallelResult();
            var startTime = DateTime.Now;

            _logger.LogInformation("=== 검수 단계별 병렬 처리 시작 ===");
            _logger.LogInformation("활성화된 단계: {Stages}", string.Join(", ", 
                enabledStages.Select((enabled, index) => enabled ? $"{index}단계" : "").Where(s => !string.IsNullOrEmpty(s))));

            try
            {
                // 그룹 A: 독립적인 단계들 (0, 1, 4, 5단계)
                var independentStages = new List<(int StageNumber, Func<Task<object>> Func)>();
                
                if (enabledStages[0]) independentStages.Add((0, stage0Func));
                if (enabledStages[1]) independentStages.Add((1, stage1Func));
                if (enabledStages[4]) independentStages.Add((4, stage4Func));
                if (enabledStages[5]) independentStages.Add((5, stage5Func));

                // 독립적인 단계들을 병렬로 실행
                if (independentStages.Any())
                {
                    _logger.LogInformation("독립 단계 병렬 실행 시작: {Count}개 단계", independentStages.Count);
                    
                    var stageItems = independentStages.Select(s => new { 
                        StageNumber = s.StageNumber, 
                        Func = s.Func 
                    }).Cast<object>().ToList();

                    var independentResults = await _parallelProcessingManager.ExecuteRuleParallelProcessingAsync(
                        stageItems,
                        async (item) =>
                        {
                            var stageItem = (dynamic)item;
                            var stageResult = await stageItem.Func();
                            return new { StageNumber = stageItem.StageNumber, Result = stageResult };
                        },
                        null,
                        "독립 검수 단계"
                    );

                    foreach (var stageResult in independentResults)
                    {
                        if (stageResult == null) continue;
                        var stageData = (dynamic)stageResult;
                        switch ((int)stageData.StageNumber)
                        {
                            case 0:
                                result.Stage0Result = stageData.Result;
                                break;
                            case 1:
                                result.Stage1Result = stageData.Result;
                                break;
                            case 4:
                                result.Stage4Result = stageData.Result;
                                break;
                            case 5:
                                result.Stage5Result = stageData.Result;
                                break;
                        }
                    }

                    _logger.LogInformation("독립 단계 병렬 실행 완료");
                }

                // === 의존 단계: 2단계 후 3단계 순차 실행 ===
                if (enabledStages[2])
                {
                    if (result.Stage1Result == null)
                    {
                        _logger.LogWarning("2단계를 실행할 수 없습니다. 1단계 결과가 존재하지 않습니다.");
                    }
                    else
                    {
                        _logger.LogInformation("2단계(스키마 검수) 순차 실행 시작");
                        try
                        {
                            var stage2Result = await stage2Func(result.Stage1Result);
                            result.Stage2Result = stage2Result;
                            _logger.LogInformation("2단계(스키마 검수) 순차 실행 완료");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "2단계(스키마 검수) 실행 중 오류 발생");
                            throw;
                        }
                    }
                }

                if (enabledStages[3])
                {
                    if (!enabledStages[2] && result.Stage2Result == null)
                    {
                        _logger.LogWarning("3단계를 실행할 수 없습니다. 2단계가 비활성화되어 있거나 결과가 없습니다.");
                    }
                    else if (result.Stage2Result == null)
                    {
                        _logger.LogWarning("3단계를 실행할 수 없습니다. 2단계 결과가 존재하지 않습니다.");
                    }
                    else
                    {
                        _logger.LogInformation("3단계(지오메트리 검수) 순차 실행 시작");
                        try
                        {
                            var stage3Result = await stage3Func(result.Stage2Result);
                            result.Stage3Result = stage3Result;
                            _logger.LogInformation("3단계(지오메트리 검수) 순차 실행 완료");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "3단계(지오메트리 검수) 실행 중 오류 발생");
                            throw;
                        }
                    }
                }

                result.TotalExecutionTime = DateTime.Now - startTime;
                _logger.LogInformation("=== 검수 단계별 병렬 처리 완료 - 총 소요시간: {Duration} ===", result.TotalExecutionTime);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 단계별 병렬 처리 중 오류 발생");
                result.HasError = true;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 단계별 병렬 처리 결과
        /// </summary>
        public class StageParallelResult
        {
            public object? Stage0Result { get; set; }
            public object? Stage1Result { get; set; }
            public object? Stage2Result { get; set; }
            public object? Stage3Result { get; set; }
            public object? Stage4Result { get; set; }
            public object? Stage5Result { get; set; }
            public TimeSpan TotalExecutionTime { get; set; }
            public bool HasError { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
