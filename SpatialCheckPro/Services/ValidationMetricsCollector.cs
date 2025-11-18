using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 실행 메트릭을 수집하고 관리하는 서비스
    /// </summary>
    public class ValidationMetricsCollector
    {
        private readonly ILogger<ValidationMetricsCollector> _logger;
        private readonly string _metricsFilePath;
        private readonly Dictionary<int, StageTimingData> _currentRunStages = new();
        private ValidationMetrics _metrics = new();
        private string? _currentRunId;
        
        public ValidationMetricsCollector(ILogger<ValidationMetricsCollector> logger)
        {
            _logger = logger;
            _metricsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpatialCheckPro",
                "validation_metrics.json"
            );
            
            LoadMetrics();
        }
        
        /// <summary>
        /// 검수 메트릭 데이터
        /// </summary>
        public class ValidationMetrics
        {
            public List<ValidationRunMetric> Runs { get; set; } = new();
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, AverageStageMetric> StageAverages { get; set; } = new();
        }
        
        /// <summary>
        /// 검수 실행 메트릭
        /// </summary>
        public class ValidationRunMetric
        {
            public string RunId { get; set; } = Guid.NewGuid().ToString();
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public long FileSizeBytes { get; set; }
            public int TableCount { get; set; }
            public int TotalFeatureCount { get; set; }
            public Dictionary<int, StageMetric> StageMetrics { get; set; } = new();
            public double TotalSeconds { get; set; }
            public bool IsSuccessful { get; set; }
        }
        
        /// <summary>
        /// 단계별 메트릭
        /// </summary>
        public class StageMetric
        {
            public int StageNumber { get; set; }
            public string StageName { get; set; } = string.Empty;
            public double ElapsedSeconds { get; set; }
            public long ProcessedItems { get; set; }
            public long TotalItems { get; set; }
            public int ErrorCount { get; set; }
            public bool IsSuccessful { get; set; }
            public double ItemsPerSecond => ElapsedSeconds > 0 ? ProcessedItems / ElapsedSeconds : 0;
        }
        
        /// <summary>
        /// 평균 단계 메트릭
        /// </summary>
        public class AverageStageMetric
        {
            public double AverageSeconds { get; set; }
            public double AverageItemsPerSecond { get; set; }
            public int SampleCount { get; set; }
            public double StdDeviation { get; set; }
        }
        
        /// <summary>
        /// 새로운 검수 실행을 시작합니다
        /// </summary>
        public void StartNewRun(string filePath, long fileSizeBytes, int tableCount, int totalFeatureCount)
        {
            _currentRunId = Guid.NewGuid().ToString();
            _currentRunStages.Clear();
            
            _logger.LogInformation("검수 메트릭 수집 시작: RunId={RunId}, File={File}, Size={Size:N0}, Tables={Tables}, Features={Features}",
                _currentRunId, Path.GetFileName(filePath), fileSizeBytes, tableCount, totalFeatureCount);
        }
        
        /// <summary>
        /// 단계 시작을 기록합니다
        /// </summary>
        public void RecordStageStart(int stageNumber, string stageName, long totalItems)
        {
            if (_currentRunId == null) return;
            
            _currentRunStages[stageNumber] = new StageTimingData
            {
                StageNumber = stageNumber,
                StageName = stageName,
                StartTime = DateTime.Now,
                TotalItems = totalItems
            };
            
            _logger.LogDebug("단계 시작 기록: Stage={Stage} {Name}, TotalItems={Items}",
                stageNumber, stageName, totalItems);
        }
        
        /// <summary>
        /// 단계 진행을 업데이트합니다
        /// </summary>
        public void UpdateStageProgress(int stageNumber, long processedItems)
        {
            if (_currentRunStages.TryGetValue(stageNumber, out var stage))
            {
                stage.ProcessedItems = processedItems;
            }
        }
        
        /// <summary>
        /// 단계 완료를 기록합니다
        /// </summary>
        public void RecordStageEnd(int stageNumber, bool isSuccessful, int errorCount, int warningCount, int skippedCount)
        {
            if (!_currentRunStages.TryGetValue(stageNumber, out var stage))
                return;
                
            stage.EndTime = DateTime.Now;
            stage.IsSuccessful = isSuccessful;
            stage.ErrorCount = errorCount;
            stage.WarningCount = warningCount;
            stage.SkippedCount = skippedCount;
            
            _logger.LogInformation("단계 완료 기록: Stage={Stage} {Name}, Time={Time:F2}초, Items={Items}/{Total}, Errors={Errors}",
                stageNumber, stage.StageName, stage.ElapsedSeconds, stage.ProcessedItems, stage.TotalItems, errorCount);
        }
        
        /// <summary>
        /// 검수 실행을 완료하고 메트릭을 저장합니다
        /// </summary>
        public async Task CompleteRunAndSaveAsync(string filePath, long fileSizeBytes, int tableCount, int totalFeatureCount, bool isSuccessful)
        {
            if (_currentRunId == null) return;
            
            // 빈 시퀀스 처리: 검수가 실패하여 단계가 시작되지 않은 경우
            DateTime startTime = DateTime.Now;
            if (_currentRunStages != null && _currentRunStages.Count > 0)
            {
                var stagesWithStartTime = _currentRunStages.Values.Where(s => s != null).ToList();
                if (stagesWithStartTime.Count > 0)
                {
                    startTime = stagesWithStartTime.Min(s => s.StartTime);
                }
            }
            
            var runMetric = new ValidationRunMetric
            {
                RunId = _currentRunId,
                StartTime = startTime,
                EndTime = DateTime.Now,
                FilePath = filePath,
                FileSizeBytes = fileSizeBytes,
                TableCount = tableCount,
                TotalFeatureCount = totalFeatureCount,
                IsSuccessful = isSuccessful
            };
            
            // 단계별 메트릭 변환 (null 체크 추가)
            if (_currentRunStages != null)
            {
                foreach (var stage in _currentRunStages.Values)
                {
                    if (stage != null)
                    {
                        runMetric.StageMetrics[stage.StageNumber] = new StageMetric
                        {
                            StageNumber = stage.StageNumber,
                            StageName = stage.StageName,
                            ElapsedSeconds = stage.ElapsedSeconds,
                            ProcessedItems = stage.ProcessedItems,
                            TotalItems = stage.TotalItems,
                            ErrorCount = stage.ErrorCount,
                            IsSuccessful = stage.IsSuccessful
                        };
                    }
                }
            }
            
            runMetric.TotalSeconds = (runMetric.EndTime - runMetric.StartTime).TotalSeconds;
            
            // 메트릭에 추가
            _metrics.Runs.Add(runMetric);
            _metrics.LastUpdated = DateTime.Now;
            
            // 평균 계산 업데이트
            UpdateStageAverages();
            
            // 파일로 저장
            await SaveMetricsAsync();
            
            _logger.LogInformation("검수 메트릭 저장 완료: RunId={RunId}, TotalTime={Time:F2}초",
                _currentRunId, runMetric.TotalSeconds);
        }
        
        /// <summary>
        /// 예측을 위한 단계별 평균 시간을 가져옵니다
        /// </summary>
        public Dictionary<int, double> GetStagePredictions(int tableCount, int featureCount)
        {
            var predictions = new Dictionary<int, double>();
            
            // 최근 실행 데이터 기반 예측
            var recentRuns = _metrics.Runs
                .Where(r => r.IsSuccessful)
                .OrderByDescending(r => r.StartTime)
                .Take(10)
                .ToList();
                
            if (!recentRuns.Any())
            {
                // 기본값 반환
                return GetDefaultPredictions(tableCount, featureCount);
            }
            
            // 유사한 규모의 실행 찾기
            var similarRuns = recentRuns
                .Where(r => Math.Abs(r.TotalFeatureCount - featureCount) / (double)Math.Max(r.TotalFeatureCount, featureCount) < 0.5)
                .ToList();
                
            if (!similarRuns.Any())
                similarRuns = recentRuns;
            
            // 단계별 평균 계산
            for (int stage = 0; stage <= 5; stage++)
            {
                var stageTimes = similarRuns
                    .Where(r => r.StageMetrics.ContainsKey(stage))
                    .Select(r => r.StageMetrics[stage])
                    .ToList();
                    
                if (stageTimes.Any())
                {
                    // 피처 수 비율로 조정
                    var avgItemsPerSecond = stageTimes.Average(s => s.ItemsPerSecond);
                    if (avgItemsPerSecond > 0)
                    {
                        // 예상 항목 수 추정
                        var estimatedItems = stage switch
                        {
                            0 => 1, // FileGDB 검수
                            1 => tableCount,
                            2 => tableCount * 10, // 평균 필드 수
                            3 => featureCount,
                            4 => tableCount * 5, // 평균 관계 규칙
                            5 => tableCount * 10, // 평균 속성 규칙
                            _ => 1
                        };
                        
                        predictions[stage] = estimatedItems / avgItemsPerSecond;
                    }
                    else
                    {
                        predictions[stage] = stageTimes.Average(s => s.ElapsedSeconds);
                    }
                }
            }
            
            return predictions;
        }
        
        private void LoadMetrics()
        {
            try
            {
                var directory = Path.GetDirectoryName(_metricsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                    
                if (File.Exists(_metricsFilePath))
                {
                    var json = File.ReadAllText(_metricsFilePath);
                    _metrics = JsonSerializer.Deserialize<ValidationMetrics>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new ValidationMetrics();
                    
                    _logger.LogInformation("검수 메트릭 로드 완료: {Count}개 실행 기록", _metrics.Runs.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "검수 메트릭 로드 실패");
                _metrics = new ValidationMetrics();
            }
        }
        
        private async Task SaveMetricsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_metricsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                    
                // 최대 100개 실행만 유지
                if (_metrics.Runs.Count > 100)
                {
                    _metrics.Runs = _metrics.Runs
                        .OrderByDescending(r => r.StartTime)
                        .Take(100)
                        .ToList();
                }
                
                var json = JsonSerializer.Serialize(_metrics, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_metricsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 메트릭 저장 실패");
            }
        }
        
        private void UpdateStageAverages()
        {
            var stageGroups = _metrics.Runs
                .Where(r => r.IsSuccessful)
                .SelectMany(r => r.StageMetrics.Values)
                .GroupBy(s => s.StageNumber);
                
            foreach (var group in stageGroups)
            {
                var times = group.Select(s => s.ElapsedSeconds).ToList();
                var itemsPerSecond = group.Select(s => s.ItemsPerSecond).ToList();
                
                var key = $"Stage_{group.Key}";
                _metrics.StageAverages[key] = new AverageStageMetric
                {
                    AverageSeconds = times.Average(),
                    AverageItemsPerSecond = itemsPerSecond.Average(),
                    SampleCount = times.Count,
                    StdDeviation = CalculateStdDev(times)
                };
            }
        }
        
        private double CalculateStdDev(List<double> values)
        {
            if (values.Count <= 1) return 0;
            
            var avg = values.Average();
            var sum = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sum / (values.Count - 1));
        }
        
        private Dictionary<int, double> GetDefaultPredictions(int tableCount, int featureCount)
        {
            return new Dictionary<int, double>
            {
                { 0, 0.2 }, // FileGDB 검수
                { 1, 0.2 + tableCount * 0.01 }, // 테이블 검수
                { 2, 5.0 + tableCount * 0.1 }, // 스키마 검수
                { 3, 5.0 + featureCount * 0.001 }, // 지오메트리 검수
                { 4, 10.0 + tableCount * 0.5 }, // 관계 검수
                { 5, 5.0 + tableCount * 0.2 } // 속성 검수
            };
        }
    }
}
