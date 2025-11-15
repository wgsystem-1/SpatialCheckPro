using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 기본 동적 배치 크기 관리자 구현
    /// 메모리 상태, CPU 사용률, 처리 성능을 고려하여 최적 배치 크기를 동적으로 조정
    /// </summary>
    public class DefaultDynamicBatchSizeManager : IDynamicBatchSizeManager
    {
        private readonly ILogger<DefaultDynamicBatchSizeManager>? _logger;
        private readonly IMemoryManager? _memoryManager;

        // 학습 데이터 저장 (최근 50개 배치 결과)
        private readonly Queue<BatchProcessingMetrics> _recentMetrics = new Queue<BatchProcessingMetrics>();
        private const int MAX_METRICS_HISTORY = 50;

        // 최적화 파라미터
        private const double MEMORY_PRESSURE_THRESHOLD = 0.8; // 80% 메모리 사용 시 압박으로 간주
        private const double CPU_PRESSURE_THRESHOLD = 0.9; // 90% CPU 사용 시 압박으로 간주
        private const int MIN_BATCH_SIZE = 100;
        private const int MAX_BATCH_SIZE = 10000;

        // EWMA (지수 가중 이동 평균) 가중치
        private const double EWMA_ALPHA = 0.3;

        // 최적 배치 크기 계산을 위한 내부 상태
        private double _optimalBatchSize = 1000;
        private double _averageThroughput = 0;
        private double _averageMemoryEfficiency = 0;

        public DefaultDynamicBatchSizeManager(
            IMemoryManager? memoryManager = null,
            ILogger<DefaultDynamicBatchSizeManager>? logger = null)
        {
            _memoryManager = memoryManager;
            _logger = logger;
        }

        /// <summary>
        /// 현재 시스템 상태를 고려한 최적 배치 크기 계산
        /// </summary>
        public int CalculateOptimalBatchSize(int baseBatchSize, long totalItems, int currentMemoryUsage = 0, double cpuUsage = 0)
        {
            try
            {
                // 기본 범위 제한
                var optimalSize = Math.Max(MIN_BATCH_SIZE, Math.Min(MAX_BATCH_SIZE, baseBatchSize));

                // 메모리 상태 고려
                if (_memoryManager != null)
                {
                    var memoryPressureLevel = CalculateMemoryPressureLevel(null);

                    if (memoryPressureLevel > 0.7) // 메모리 압박이 심한 경우
                    {
                        optimalSize = (int)(optimalSize * (1 - memoryPressureLevel));
                        _logger?.LogDebug("메모리 압박으로 배치 크기 감소: {Original} -> {New}", baseBatchSize, optimalSize);
                    }
                }

                // CPU 사용률 고려
                if (cpuUsage > CPU_PRESSURE_THRESHOLD)
                {
                    optimalSize = (int)(optimalSize * 0.8); // CPU 부하가 높으면 20% 감소
                    _logger?.LogDebug("CPU 부하로 배치 크기 감소: {Original} -> {New}", baseBatchSize, optimalSize);
                }

                // 학습 데이터 기반 최적화
                if (_recentMetrics.Count >= 5) // 최소 5개 이상의 데이터가 있어야 신뢰할 수 있음
                {
                    optimalSize = CalculateLearningBasedOptimalSize(optimalSize);
                }

                // 최종 범위 제한
                optimalSize = Math.Max(MIN_BATCH_SIZE, Math.Min(MAX_BATCH_SIZE, optimalSize));

                // 전체 항목 수 고려 (너무 큰 배치 크기는 비효율적)
                if (totalItems > 0 && optimalSize > totalItems / 10)
                {
                    optimalSize = Math.Max(MIN_BATCH_SIZE, (int)(totalItems / 10));
                }

                _optimalBatchSize = optimalSize;
                return optimalSize;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "최적 배치 크기 계산 실패, 기본값 사용: {BaseBatchSize}", baseBatchSize);
                return Math.Max(MIN_BATCH_SIZE, Math.Min(MAX_BATCH_SIZE, baseBatchSize));
            }
        }

        /// <summary>
        /// 배치 처리 성능을 학습하여 최적화
        /// </summary>
        public void LearnFromBatchResult(int batchSize, long processingTime, int memoryUsage, bool success)
        {
            try
            {
                var metrics = new BatchProcessingMetrics
                {
                    BatchSize = batchSize,
                    ProcessingTimeMs = processingTime,
                    MemoryUsageMB = memoryUsage,
                    Success = success,
                    ProcessedItems = batchSize
                };

                // 큐에 추가
                _recentMetrics.Enqueue(metrics);

                // 오래된 데이터 제거
                while (_recentMetrics.Count > MAX_METRICS_HISTORY)
                {
                    _recentMetrics.Dequeue();
                }

                // 통계 업데이트
                UpdateStatistics();

                _logger?.LogDebug("배치 처리 학습 데이터 추가: 크기={BatchSize}, 시간={Time}ms, 메모리={Memory}MB, 성공={Success}",
                    batchSize, processingTime, memoryUsage, success);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "배치 처리 학습 실패");
            }
        }

        /// <summary>
        /// 메모리 압박 감지 시 배치 크기 조정 권장사항
        /// </summary>
        public int RecommendBatchSizeUnderMemoryPressure(int currentBatchSize, int memoryPressureLevel)
        {
            // 메모리 압박 레벨에 따라 배치 크기 감소 (레벨 1-10)
            var reductionFactor = Math.Max(0.1, 1.0 - (memoryPressureLevel / 10.0));
            var recommendedSize = (int)(currentBatchSize * reductionFactor);

            recommendedSize = Math.Max(MIN_BATCH_SIZE, recommendedSize);

            _logger?.LogWarning("메모리 압박으로 배치 크기 조정 권장: {Current} -> {Recommended} (압박 레벨: {Level})",
                currentBatchSize, recommendedSize, memoryPressureLevel);

            return recommendedSize;
        }

        /// <summary>
        /// 시스템 상태에 따른 배치 크기 조정 계수 계산
        /// </summary>
        public double CalculateAdjustmentFactor()
        {
            double factor = 1.0;

            try
            {
                // 메모리 상태 고려
                if (_memoryManager != null)
                {
                    var memoryPressure = CalculateMemoryPressureLevel(null);

                    if (memoryPressure > 0.5)
                    {
                        factor *= (1 - memoryPressure * 0.5); // 최대 50% 감소
                    }
                }

                // 처리 성능 고려
                if (_averageThroughput > 0 && _recentMetrics.Count > 0)
                {
                    var recentThroughput = _recentMetrics.Average(m => m.Throughput);
                    var throughputRatio = recentThroughput / _averageThroughput;

                    // 처리율이 평균보다 20% 이상 떨어지면 배치 크기 감소
                    if (throughputRatio < 0.8)
                    {
                        factor *= 0.9;
                    }
                    // 처리율이 평균보다 20% 이상 높으면 배치 크기 증가
                    else if (throughputRatio > 1.2)
                    {
                        factor *= 1.1;
                    }
                }

                // 범위 제한
                factor = Math.Max(0.1, Math.Min(2.0, factor));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "조정 계수 계산 실패, 기본값 1.0 사용");
                factor = 1.0;
            }

            return factor;
        }

        /// <summary>
        /// 메모리 압박 레벨 계산 (0.0 - 1.0)
        /// </summary>
        private double CalculateMemoryPressureLevel(object memoryStats)
        {
            // 현재는 간단한 메모리 사용량으로 계산
            var currentUsage = _memoryManager?.GetCurrentMemoryUsage() ?? GC.GetTotalMemory(false);
            var maxMemory = _memoryManager != null ? 1024L * 1024 * 1024 * 2 : 1024L * 1024 * 1024 * 4; // 기본 4GB

            if (maxMemory == 0) return 0;

            var usageRatio = (double)currentUsage / maxMemory;
            return Math.Min(1.0, usageRatio / MEMORY_PRESSURE_THRESHOLD);
        }

        /// <summary>
        /// 학습 데이터 기반 최적 배치 크기 계산
        /// </summary>
        private int CalculateLearningBasedOptimalSize(int baseSize)
        {
            if (_recentMetrics.Count < 5) return baseSize;

            try
            {
                // 메모리 효율성이 가장 높은 배치 크기 찾기
                var bestBatchSize = _recentMetrics
                    .GroupBy(m => m.BatchSize)
                    .Select(g => new
                    {
                        BatchSize = g.Key,
                        AvgMemoryEfficiency = g.Average(m => m.MemoryEfficiency),
                        AvgThroughput = g.Average(m => m.Throughput),
                        SampleCount = g.Count()
                    })
                    .Where(x => x.SampleCount >= 2) // 최소 2개 이상의 샘플 필요
                    .OrderByDescending(x => x.AvgMemoryEfficiency)
                    .FirstOrDefault();

                if (bestBatchSize != null)
                {
                    // 현재 baseSize와 학습된 최적 크기의 가중 평균
                    var learningWeight = Math.Min(0.7, _recentMetrics.Count / 20.0); // 최대 70% 가중치
                    var optimalSize = (int)((1 - learningWeight) * baseSize + learningWeight * bestBatchSize.BatchSize);

                    _logger?.LogDebug("학습 기반 배치 크기 조정: {Base} -> {Optimal} (학습 가중치: {Weight:P1})",
                        baseSize, optimalSize, learningWeight);

                    return optimalSize;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "학습 기반 최적화 실패");
            }

            return baseSize;
        }

        /// <summary>
        /// 내부 통계 업데이트
        /// </summary>
        private void UpdateStatistics()
        {
            if (_recentMetrics.Count == 0) return;

            // EWMA를 사용한 통계 업데이트
            var latest = _recentMetrics.Last();

            _averageThroughput = _averageThroughput == 0
                ? latest.Throughput
                : EWMA_ALPHA * latest.Throughput + (1 - EWMA_ALPHA) * _averageThroughput;

            _averageMemoryEfficiency = _averageMemoryEfficiency == 0
                ? latest.MemoryEfficiency
                : EWMA_ALPHA * latest.MemoryEfficiency + (1 - EWMA_ALPHA) * _averageMemoryEfficiency;
        }
    }
}
