using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 검수 시간 예측 모델
    /// </summary>
    public class ValidationTimePredictor
    {
        private readonly ILogger<ValidationTimePredictor> _logger;
        private readonly string _historyFilePath;
        private ValidationHistoryData _historyData;

        /// <summary>
        /// 검수 이력 데이터
        /// </summary>
        public class ValidationHistoryData
        {
            public List<ValidationRunData> Runs { get; set; } = new();
            public DateTime LastUpdated { get; set; }
        }

        /// <summary>
        /// 검수 실행 데이터
        /// </summary>
        public class ValidationRunData
        {
            public DateTime Timestamp { get; set; }
            public string FilePath { get; set; } = string.Empty;
            
            // 입력 메트릭
            public int TableCount { get; set; }
            public int TotalFeatureCount { get; set; }
            public int SchemaFieldCount { get; set; }
            public int GeometryCheckItemCount { get; set; }
            public int RelationRuleCount { get; set; }
            public int AttributeColumnCount { get; set; }
            
            // 실행 시간 (초)
            public double Stage0Time { get; set; } // FileGDB 완전성
            public double Stage1Time { get; set; } // 테이블 검수
            public double Stage2Time { get; set; } // 스키마 검수
            public double Stage3Time { get; set; } // 지오메트리 검수
            public double Stage4Time { get; set; } // 관계 검수
            public double Stage5Time { get; set; } // 속성 검수
            public double TotalTime { get; set; }
        }

        public ValidationTimePredictor(ILogger<ValidationTimePredictor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _historyFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeoSpatialValidator",
                "validation_history.json"
            );
            
            LoadHistory();
        }

        /// <summary>
        /// 이력 데이터 로드
        /// </summary>
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    _historyData = JsonSerializer.Deserialize<ValidationHistoryData>(json) ?? new ValidationHistoryData();
                    _logger.LogInformation("검수 이력 로드 완료: {Count}개 실행 기록", _historyData.Runs.Count);
                }
                else
                {
                    _historyData = new ValidationHistoryData();
                    InitializeWithDefaultData();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "검수 이력 로드 실패, 기본값 사용");
                _historyData = new ValidationHistoryData();
                InitializeWithDefaultData();
            }
        }

        /// <summary>
        /// 기본 데이터로 초기화 (로그 분석 결과 기반)
        /// </summary>
        private void InitializeWithDefaultData()
        {
            // 로그 분석 결과를 바탕으로 기본 데이터 추가
            _historyData.Runs.Add(new ValidationRunData
            {
                Timestamp = DateTime.Parse("2025-10-08 03:05:32"),
                TableCount = 52,
                TotalFeatureCount = 11451, // 로그에서 계산된 총 피처 수
                SchemaFieldCount = 405,
                GeometryCheckItemCount = 232,
                RelationRuleCount = 39,
                AttributeColumnCount = 0,
                Stage0Time = 0.154, // 32.071 -> 32.225
                Stage1Time = 0.163, // 32.225 -> 32.388
                Stage2Time = 6.134, // 32.388 -> 38.522
                Stage3Time = 8.060, // 38.522 -> 46.582
                Stage4Time = 87.472, // 46.582 -> 134.054 (1:27)
                Stage5Time = 0,
                TotalTime = 101.983
            });

            _historyData.Runs.Add(new ValidationRunData
            {
                Timestamp = DateTime.Parse("2025-10-08 03:07:16"),
                TableCount = 52,
                TotalFeatureCount = 6838, // 두 번째 실행의 피처 수
                SchemaFieldCount = 425,
                GeometryCheckItemCount = 232,
                RelationRuleCount = 664,
                AttributeColumnCount = 0,
                Stage0Time = 0.149,
                Stage1Time = 0.162,
                Stage2Time = 5.781, // 16.846 -> 22.627
                Stage3Time = 2.556, // 22.627 -> 25.183
                Stage4Time = 14.931, // 25.183 -> 40.114
                Stage5Time = 0,
                TotalTime = 23.579
            });
        }

        /// <summary>
        /// 검수 시간 예측
        /// </summary>
        public Dictionary<int, double> PredictStageTimes(
            int tableCount,
            int totalFeatureCount,
            int schemaFieldCount,
            int geometryCheckItemCount,
            int relationRuleCount,
            int attributeColumnCount)
        {
            var predictions = new Dictionary<int, double>();
            
            // 유사한 실행 기록 찾기 (가중 거리 기반)
            var similarRuns = FindSimilarRuns(
                tableCount, totalFeatureCount, schemaFieldCount,
                geometryCheckItemCount, relationRuleCount, attributeColumnCount
            );

            if (!similarRuns.Any())
            {
                // 기본 예측값 사용
                return GetDefaultPredictions(
                    tableCount, totalFeatureCount, schemaFieldCount,
                    geometryCheckItemCount, relationRuleCount, attributeColumnCount
                );
            }

            // 가중 평균으로 예측
            for (int stage = 0; stage <= 5; stage++)
            {
                double totalWeight = 0;
                double weightedTime = 0;

                foreach (var (run, weight) in similarRuns)
                {
                    double stageTime = stage switch
                    {
                        0 => run.Stage0Time,
                        1 => run.Stage1Time,
                        2 => run.Stage2Time,
                        3 => run.Stage3Time,
                        4 => run.Stage4Time,
                        5 => run.Stage5Time,
                        _ => 0
                    };

                    // 메트릭 기반 스케일링 적용
                    double scaleFactor = GetScaleFactor(stage, run, 
                        tableCount, totalFeatureCount, schemaFieldCount,
                        geometryCheckItemCount, relationRuleCount, attributeColumnCount);

                    weightedTime += stageTime * scaleFactor * weight;
                    totalWeight += weight;
                }

                predictions[stage] = totalWeight > 0 ? weightedTime / totalWeight : 0;
            }

            // 최소 시간 보장
            predictions[0] = Math.Max(predictions[0], 0.1); // FileGDB 검수
            predictions[1] = Math.Max(predictions[1], 0.1); // 테이블 검수
            predictions[2] = Math.Max(predictions[2], 1.0); // 스키마 검수
            predictions[3] = Math.Max(predictions[3], 1.0); // 지오메트리 검수
            predictions[4] = Math.Max(predictions[4], 1.0); // 관계 검수
            predictions[5] = Math.Max(predictions[5], 0.5); // 속성 검수

            _logger.LogInformation("예측 시간 - 0단계: {S0:F1}초, 1단계: {S1:F1}초, 2단계: {S2:F1}초, 3단계: {S3:F1}초, 4단계: {S4:F1}초, 5단계: {S5:F1}초",
                predictions[0], predictions[1], predictions[2], predictions[3], predictions[4], predictions[5]);

            return predictions;
        }

        /// <summary>
        /// 유사한 실행 기록 찾기
        /// </summary>
        private List<(ValidationRunData run, double weight)> FindSimilarRuns(
            int tableCount, int totalFeatureCount, int schemaFieldCount,
            int geometryCheckItemCount, int relationRuleCount, int attributeColumnCount)
        {
            var results = new List<(ValidationRunData, double)>();

            foreach (var run in _historyData.Runs)
            {
                // 거리 계산 (정규화된 유클리드 거리)
                double distance = 0;
                distance += Math.Pow((run.TableCount - tableCount) / 100.0, 2);
                distance += Math.Pow((run.TotalFeatureCount - totalFeatureCount) / 10000.0, 2);
                distance += Math.Pow((run.SchemaFieldCount - schemaFieldCount) / 500.0, 2);
                distance += Math.Pow((run.GeometryCheckItemCount - geometryCheckItemCount) / 300.0, 2);
                distance += Math.Pow((run.RelationRuleCount - relationRuleCount) / 1000.0, 2);
                distance += Math.Pow((run.AttributeColumnCount - attributeColumnCount) / 100.0, 2);
                distance = Math.Sqrt(distance);

                // 가중치 계산 (거리가 가까울수록 높은 가중치)
                double weight = 1.0 / (1.0 + distance);
                
                // 최근 실행일수록 가중치 증가
                var age = (DateTime.Now - run.Timestamp).TotalDays;
                weight *= Math.Exp(-age / 30.0); // 30일 반감기

                if (weight > 0.1) // 임계값 이상만 사용
                {
                    results.Add((run, weight));
                }
            }

            return results.OrderByDescending(r => r.Item2).Take(5).ToList();
        }

        /// <summary>
        /// 스케일 팩터 계산
        /// </summary>
        private double GetScaleFactor(int stage, ValidationRunData baseRun,
            int tableCount, int totalFeatureCount, int schemaFieldCount,
            int geometryCheckItemCount, int relationRuleCount, int attributeColumnCount)
        {
            return stage switch
            {
                0 => 1.0, // FileGDB 검수는 크기와 무관
                1 => (double)tableCount / Math.Max(1, baseRun.TableCount),
                2 => (double)schemaFieldCount / Math.Max(1, baseRun.SchemaFieldCount),
                3 => Math.Pow((double)totalFeatureCount / Math.Max(1, baseRun.TotalFeatureCount), 0.8), // 서브리니어 스케일링
                4 => Math.Pow((double)relationRuleCount / Math.Max(1, baseRun.RelationRuleCount), 0.9),
                5 => (double)attributeColumnCount / Math.Max(1, baseRun.AttributeColumnCount),
                _ => 1.0
            };
        }

        /// <summary>
        /// 기본 예측값
        /// </summary>
        private Dictionary<int, double> GetDefaultPredictions(
            int tableCount, int totalFeatureCount, int schemaFieldCount,
            int geometryCheckItemCount, int relationRuleCount, int attributeColumnCount)
        {
            return new Dictionary<int, double>
            {
                [0] = 0.15, // FileGDB 검수 (고정)
                [1] = 0.003 * tableCount, // 테이블당 3ms
                [2] = 0.015 * schemaFieldCount, // 필드당 15ms
                [3] = 0.001 * totalFeatureCount + 0.5, // 피처당 1ms + 오버헤드
                [4] = 0.05 * relationRuleCount + 1.0, // 규칙당 50ms + 오버헤드
                [5] = 0.01 * attributeColumnCount // 컬럼당 10ms
            };
        }

        /// <summary>
        /// 실행 결과 저장
        /// </summary>
        public void SaveRunData(ValidationRunData runData)
        {
            try
            {
                _historyData.Runs.Add(runData);
                _historyData.LastUpdated = DateTime.Now;

                // 최대 100개 실행 기록만 유지
                if (_historyData.Runs.Count > 100)
                {
                    _historyData.Runs = _historyData.Runs
                        .OrderByDescending(r => r.Timestamp)
                        .Take(100)
                        .ToList();
                }

                // 디렉토리 생성
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // JSON 저장
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(_historyData, options);
                File.WriteAllText(_historyFilePath, json);

                _logger.LogInformation("검수 실행 데이터 저장 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 실행 데이터 저장 실패");
            }
        }
    }
}
