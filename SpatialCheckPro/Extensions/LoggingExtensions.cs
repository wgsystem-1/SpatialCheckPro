using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Extensions
{
    /// <summary>
    /// 구조화 로깅 확장 메서드
    /// Phase 2 Item #9: 로깅 표준화 및 구조화 로깅
    ///
    /// 목적:
    /// - 일관된 로그 메시지 형식 제공
    /// - 중요 컨텍스트 정보 포함 (ValidationId, TableId 등)
    /// - 성능 메트릭 로깅 표준화
    /// - 로그 분석 효율 향상
    /// </summary>
    public static class LoggingExtensions
    {
        // ========================================
        // 검수 전체 프로세스 로깅
        // ========================================

        /// <summary>
        /// 검수 시작 로깅
        /// </summary>
        public static void LogValidationStarted(
            this ILogger logger,
            string validationId,
            string filePath,
            long fileSize)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] 검수 시작 - 파일: {FilePath} ({FileSizeMB:F2}MB)",
                validationId,
                filePath,
                fileSize / (1024.0 * 1024.0));
        }

        /// <summary>
        /// 검수 완료 로깅
        /// </summary>
        public static void LogValidationCompleted(
            this ILogger logger,
            string validationId,
            int totalErrors,
            int totalWarnings,
            double elapsedSeconds)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] 검수 완료 - 오류: {ErrorCount}개, 경고: {WarningCount}개, 소요시간: {ElapsedSeconds:F2}초",
                validationId,
                totalErrors,
                totalWarnings,
                elapsedSeconds);
        }

        /// <summary>
        /// 검수 실패 로깅
        /// </summary>
        public static void LogValidationFailed(
            this ILogger logger,
            string validationId,
            Exception exception,
            string reason)
        {
            logger.LogError(exception,
                "[Validation:{ValidationId}] 검수 실패 - 사유: {Reason}",
                validationId,
                reason);
        }

        /// <summary>
        /// 검수 취소 로깅
        /// </summary>
        public static void LogValidationCancelled(
            this ILogger logger,
            string validationId,
            double elapsedSeconds)
        {
            logger.LogWarning(
                "[Validation:{ValidationId}] 검수 취소됨 - 소요시간: {ElapsedSeconds:F2}초",
                validationId,
                elapsedSeconds);
        }

        // ========================================
        // 단계별 검수 로깅
        // ========================================

        /// <summary>
        /// 검수 단계 시작 로깅
        /// </summary>
        public static void LogStageStarted(
            this ILogger logger,
            string validationId,
            int stage,
            string stageName)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] Stage {Stage} ({StageName}) 시작",
                validationId,
                stage,
                stageName);
        }

        /// <summary>
        /// 검수 단계 완료 로깅
        /// </summary>
        public static void LogStageCompleted(
            this ILogger logger,
            string validationId,
            int stage,
            string stageName,
            int errorCount,
            double elapsedSeconds)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] Stage {Stage} ({StageName}) 완료 - 오류: {ErrorCount}개, 소요시간: {ElapsedSeconds:F2}초",
                validationId,
                stage,
                stageName,
                errorCount,
                elapsedSeconds);
        }

        /// <summary>
        /// 검수 단계 실패 로깅
        /// </summary>
        public static void LogStageFailed(
            this ILogger logger,
            string validationId,
            int stage,
            string stageName,
            Exception exception)
        {
            logger.LogError(exception,
                "[Validation:{ValidationId}] Stage {Stage} ({StageName}) 실패",
                validationId,
                stage,
                stageName);
        }

        // ========================================
        // 테이블별 검수 로깅
        // ========================================

        /// <summary>
        /// 테이블 검수 시작 로깅
        /// </summary>
        public static void LogTableCheckStarted(
            this ILogger logger,
            string validationId,
            string tableId,
            string tableName,
            long featureCount)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] [Table:{TableId}] 테이블 검수 시작 - {TableName} ({FeatureCount}개 피처)",
                validationId,
                tableId,
                tableName,
                featureCount);
        }

        /// <summary>
        /// 테이블 검수 완료 로깅
        /// </summary>
        public static void LogTableCheckCompleted(
            this ILogger logger,
            string validationId,
            string tableId,
            string tableName,
            int errorCount,
            double elapsedSeconds)
        {
            logger.LogInformation(
                "[Validation:{ValidationId}] [Table:{TableId}] 테이블 검수 완료 - {TableName}, 오류: {ErrorCount}개, 소요시간: {ElapsedSeconds:F2}초",
                validationId,
                tableId,
                tableName,
                errorCount,
                elapsedSeconds);
        }

        // ========================================
        // 성능 메트릭 로깅
        // ========================================

        /// <summary>
        /// 메모리 사용량 로깅
        /// </summary>
        public static void LogMemoryUsage(
            this ILogger logger,
            string context,
            long usedMemoryMB,
            long totalMemoryMB,
            double pressureRatio)
        {
            logger.LogDebug(
                "[Performance] [{Context}] 메모리 사용량 - 사용: {UsedMemoryMB}MB / {TotalMemoryMB}MB, 압박률: {PressureRatio:P1}",
                context,
                usedMemoryMB,
                totalMemoryMB,
                pressureRatio);
        }

        /// <summary>
        /// 배치 처리 성능 로깅
        /// </summary>
        public static void LogBatchProcessing(
            this ILogger logger,
            string context,
            int batchSize,
            int processedCount,
            int totalCount,
            double elapsedSeconds)
        {
            var throughput = processedCount / elapsedSeconds;
            var progressPercent = (double)processedCount / totalCount * 100;

            logger.LogDebug(
                "[Performance] [{Context}] 배치 처리 - 크기: {BatchSize}, 진행: {ProcessedCount}/{TotalCount} ({ProgressPercent:F1}%), " +
                "처리량: {Throughput:F0}개/초, 소요시간: {ElapsedSeconds:F2}초",
                context,
                batchSize,
                processedCount,
                totalCount,
                progressPercent,
                throughput,
                elapsedSeconds);
        }

        /// <summary>
        /// GC 컬렉션 발생 로깅
        /// </summary>
        public static void LogGCCollection(
            this ILogger logger,
            string context,
            int generation,
            long beforeMemoryMB,
            long afterMemoryMB)
        {
            var freedMemoryMB = beforeMemoryMB - afterMemoryMB;

            logger.LogDebug(
                "[Performance] [{Context}] GC Gen{Generation} 수행 - 해제: {FreedMemoryMB}MB (이전: {BeforeMemoryMB}MB → 이후: {AfterMemoryMB}MB)",
                context,
                generation,
                freedMemoryMB,
                beforeMemoryMB,
                afterMemoryMB);
        }

        /// <summary>
        /// 쿼리 성능 로깅
        /// </summary>
        public static void LogQueryPerformance(
            this ILogger logger,
            string queryName,
            int resultCount,
            double elapsedMilliseconds)
        {
            logger.LogDebug(
                "[Performance] [Query:{QueryName}] 실행 완료 - 결과: {ResultCount}개, 소요시간: {ElapsedMs:F2}ms",
                queryName,
                resultCount,
                elapsedMilliseconds);
        }

        // ========================================
        // 캐시 관련 로깅
        // ========================================

        /// <summary>
        /// 캐시 적중 로깅
        /// </summary>
        public static void LogCacheHit(
            this ILogger logger,
            string cacheType,
            string key)
        {
            logger.LogDebug(
                "[Cache] [{CacheType}] 캐시 적중 - Key: {Key}",
                cacheType,
                key);
        }

        /// <summary>
        /// 캐시 미스 로깅
        /// </summary>
        public static void LogCacheMiss(
            this ILogger logger,
            string cacheType,
            string key)
        {
            logger.LogDebug(
                "[Cache] [{CacheType}] 캐시 미스 - Key: {Key}",
                cacheType,
                key);
        }

        /// <summary>
        /// 캐시 갱신 로깅
        /// </summary>
        public static void LogCacheRefresh(
            this ILogger logger,
            string cacheType,
            string key,
            int itemCount)
        {
            logger.LogDebug(
                "[Cache] [{CacheType}] 캐시 갱신 - Key: {Key}, 항목: {ItemCount}개",
                cacheType,
                key,
                itemCount);
        }

        // ========================================
        // 데이터 스트리밍 로깅
        // ========================================

        /// <summary>
        /// 스트리밍 시작 로깅
        /// </summary>
        public static void LogStreamingStarted(
            this ILogger logger,
            string context,
            string outputPath)
        {
            logger.LogInformation(
                "[Streaming] [{Context}] 스트리밍 시작 - 출력: {OutputPath}",
                context,
                outputPath);
        }

        /// <summary>
        /// 스트리밍 완료 로깅
        /// </summary>
        public static void LogStreamingCompleted(
            this ILogger logger,
            string context,
            string outputPath,
            int itemCount,
            double elapsedSeconds)
        {
            logger.LogInformation(
                "[Streaming] [{Context}] 스트리밍 완료 - 출력: {OutputPath}, 항목: {ItemCount}개, 소요시간: {ElapsedSeconds:F2}초",
                context,
                outputPath,
                itemCount,
                elapsedSeconds);
        }

        /// <summary>
        /// 스트리밍 플러시 로깅
        /// </summary>
        public static void LogStreamingFlush(
            this ILogger logger,
            string context,
            int batchSize,
            int totalCount)
        {
            logger.LogDebug(
                "[Streaming] [{Context}] 배치 플러시 - 크기: {BatchSize}개, 누적: {TotalCount}개",
                context,
                batchSize,
                totalCount);
        }

        // ========================================
        // 오류 및 경고 로깅
        // ========================================

        /// <summary>
        /// 검수 오류 발견 로깅
        /// </summary>
        public static void LogValidationErrorFound(
            this ILogger logger,
            string validationId,
            string tableId,
            string errorCode,
            string featureId)
        {
            logger.LogDebug(
                "[Validation:{ValidationId}] [Table:{TableId}] 오류 발견 - Code: {ErrorCode}, Feature: {FeatureId}",
                validationId,
                tableId,
                errorCode,
                featureId);
        }

        /// <summary>
        /// 재시도 시도 로깅
        /// </summary>
        public static void LogRetryAttempt(
            this ILogger logger,
            string operation,
            int attemptNumber,
            int maxAttempts,
            Exception exception)
        {
            logger.LogWarning(exception,
                "[Retry] {Operation} 재시도 중 - {AttemptNumber}/{MaxAttempts}",
                operation,
                attemptNumber,
                maxAttempts);
        }

        /// <summary>
        /// 임계값 초과 경고 로깅
        /// </summary>
        public static void LogThresholdExceeded(
            this ILogger logger,
            string metric,
            double currentValue,
            double thresholdValue)
        {
            logger.LogWarning(
                "[Threshold] {Metric} 임계값 초과 - 현재: {CurrentValue:F2}, 임계값: {ThresholdValue:F2}",
                metric,
                currentValue,
                thresholdValue);
        }
    }
}
