using System;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 동적 배치 크기 관리자 인터페이스
    /// 메모리 상태, CPU 사용률, I/O 성능 등을 고려하여 최적 배치 크기를 동적으로 조정
    /// </summary>
    public interface IDynamicBatchSizeManager
    {
        /// <summary>
        /// 현재 시스템 상태를 고려한 최적 배치 크기 계산
        /// </summary>
        /// <param name="baseBatchSize">기본 배치 크기</param>
        /// <param name="totalItems">전체 항목 수</param>
        /// <param name="currentMemoryUsage">현재 메모리 사용량 (MB)</param>
        /// <param name="cpuUsage">현재 CPU 사용률 (0-100)</param>
        /// <returns>최적 배치 크기</returns>
        int CalculateOptimalBatchSize(int baseBatchSize, long totalItems, int currentMemoryUsage = 0, double cpuUsage = 0);

        /// <summary>
        /// 배치 처리 성능을 학습하여 최적화
        /// </summary>
        /// <param name="batchSize">사용한 배치 크기</param>
        /// <param name="processingTime">처리 시간 (ms)</param>
        /// <param name="memoryUsage">메모리 사용량 (MB)</param>
        /// <param name="success">처리 성공 여부</param>
        void LearnFromBatchResult(int batchSize, long processingTime, int memoryUsage, bool success);

        /// <summary>
        /// 메모리 압박 감지 시 배치 크기 조정 권장사항
        /// </summary>
        /// <param name="currentBatchSize">현재 배치 크기</param>
        /// <param name="memoryPressureLevel">메모리 압박 레벨 (0-10)</param>
        /// <returns>권장 배치 크기</returns>
        int RecommendBatchSizeUnderMemoryPressure(int currentBatchSize, int memoryPressureLevel);

        /// <summary>
        /// 시스템 상태에 따른 배치 크기 조정 계수 계산
        /// </summary>
        /// <returns>조정 계수 (0.1 - 2.0)</returns>
        double CalculateAdjustmentFactor();
    }

    /// <summary>
    /// 배치 처리 메트릭 정보
    /// </summary>
    public class BatchProcessingMetrics
    {
        /// <summary>배치 크기</summary>
        public int BatchSize { get; set; }

        /// <summary>처리 시간 (ms)</summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>메모리 사용량 (MB)</summary>
        public int MemoryUsageMB { get; set; }

        /// <summary>성공 여부</summary>
        public bool Success { get; set; }

        /// <summary>처리된 항목 수</summary>
        public int ProcessedItems { get; set; }

        /// <summary>처리율 (항목/초)</summary>
        public double Throughput => ProcessedItems > 0 && ProcessingTimeMs > 0
            ? (ProcessedItems / (ProcessingTimeMs / 1000.0))
            : 0;

        /// <summary>메모리 효율성 (처리율/메모리 사용량)</summary>
        public double MemoryEfficiency => MemoryUsageMB > 0 ? Throughput / MemoryUsageMB : 0;
    }
}
