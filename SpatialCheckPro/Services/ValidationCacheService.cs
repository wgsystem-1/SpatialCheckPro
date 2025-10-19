using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 관련 데이터 캐싱을 담당하는 서비스
    /// </summary>
    public class ValidationCacheService : IValidationCacheService
    {
        private readonly ILruCache<string, TableMetadata> _tableMetadataCache;
        private readonly ILruCache<string, HashSet<string>> _referenceKeysCache;
        private readonly ILruCache<string, Dictionary<string, int>> _fieldValueCountsCache;
        private readonly ILruCache<string, long> _recordCountCache;
        private readonly ILogger<ValidationCacheService> _logger;
        private readonly Timer _cleanupTimer;

        /// <summary>
        /// 캐시 통계 업데이트 이벤트
        /// </summary>
        public event EventHandler<CacheStatisticsEventArgs>? StatisticsUpdated;

        public ValidationCacheService(ILogger<ValidationCacheService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 각 캐시 타입별로 적절한 크기 설정
            _tableMetadataCache = new LruCache<string, TableMetadata>(1000); // 테이블 메타데이터
            _referenceKeysCache = new LruCache<string, HashSet<string>>(100); // 참조 키 (메모리 사용량 큼)
            _fieldValueCountsCache = new LruCache<string, Dictionary<string, int>>(50); // 필드값 카운트 (메모리 사용량 매우 큼)
            _recordCountCache = new LruCache<string, long>(2000); // 레코드 수 (메모리 사용량 작음)

            // 주기적 정리 타이머 설정 (10분마다)
            _cleanupTimer = new Timer(PerformPeriodicCleanup, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

            _logger.LogInformation("검수 캐시 서비스 초기화 완료");
        }

        /// <summary>
        /// 테이블 메타데이터를 캐시에서 조회하거나 생성합니다
        /// </summary>
        public async Task<TableMetadata> GetOrCreateTableMetadataAsync(
            string gdbPath,
            string tableName,
            Func<Task<TableMetadata>> metadataFactory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"metadata:{gdbPath}:{tableName}";

            // 캐시에서 조회 시도
            if (_tableMetadataCache.TryGet(cacheKey, out var cachedMetadata) && cachedMetadata != null)
            {
                _logger.LogTrace("테이블 메타데이터 캐시 히트: {TableName}", tableName);
                return cachedMetadata;
            }

            // 캐시 미스 - 새로 생성
            _logger.LogTrace("테이블 메타데이터 캐시 미스 - 새로 생성: {TableName}", tableName);
            var metadata = await metadataFactory();

            // 캐시에 저장
            _tableMetadataCache.Set(cacheKey, metadata);
            
            _logger.LogDebug("테이블 메타데이터 캐시 저장: {TableName}", tableName);
            return metadata;
        }

        /// <summary>
        /// 참조 키를 캐시에서 조회하거나 생성합니다
        /// </summary>
        public async Task<HashSet<string>> GetOrCreateReferenceKeysAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            Func<Task<HashSet<string>>> keysFactory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"refkeys:{gdbPath}:{tableName}:{fieldName}";

            // 캐시에서 조회 시도
            if (_referenceKeysCache.TryGet(cacheKey, out var cachedKeys) && cachedKeys != null)
            {
                _logger.LogTrace("참조 키 캐시 히트: {TableName}.{FieldName} ({KeyCount}개)", 
                    tableName, fieldName, cachedKeys.Count);
                return cachedKeys;
            }

            // 캐시 미스 - 새로 생성
            _logger.LogTrace("참조 키 캐시 미스 - 새로 생성: {TableName}.{FieldName}", tableName, fieldName);
            var keys = await keysFactory();

            // 캐시에 저장 (메모리 사용량 체크)
            var estimatedMemoryUsage = EstimateReferenceKeysMemoryUsage(keys);
            if (estimatedMemoryUsage < 50 * 1024 * 1024) // 50MB 미만인 경우만 캐시
            {
                _referenceKeysCache.Set(cacheKey, keys);
                _logger.LogDebug("참조 키 캐시 저장: {TableName}.{FieldName} ({KeyCount}개, 예상 메모리: {MemoryMB:F2}MB)",
                    tableName, fieldName, keys.Count, estimatedMemoryUsage / (1024.0 * 1024.0));
            }
            else
            {
                _logger.LogWarning("참조 키 캐시 저장 건너뜀 - 메모리 사용량 초과: {TableName}.{FieldName} (예상 메모리: {MemoryMB:F2}MB)",
                    tableName, fieldName, estimatedMemoryUsage / (1024.0 * 1024.0));
            }

            return keys;
        }

        /// <summary>
        /// 필드값 카운트를 캐시에서 조회하거나 생성합니다
        /// </summary>
        public async Task<Dictionary<string, int>> GetOrCreateFieldValueCountsAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            Func<Task<Dictionary<string, int>>> countsFactory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"counts:{gdbPath}:{tableName}:{fieldName}";

            // 캐시에서 조회 시도
            if (_fieldValueCountsCache.TryGet(cacheKey, out var cachedCounts) && cachedCounts != null)
            {
                _logger.LogTrace("필드값 카운트 캐시 히트: {TableName}.{FieldName} ({UniqueCount}개 고유값)", 
                    tableName, fieldName, cachedCounts.Count);
                return cachedCounts;
            }

            // 캐시 미스 - 새로 생성
            _logger.LogTrace("필드값 카운트 캐시 미스 - 새로 생성: {TableName}.{FieldName}", tableName, fieldName);
            var counts = await countsFactory();

            // 캐시에 저장 (메모리 사용량 체크)
            var estimatedMemoryUsage = EstimateFieldValueCountsMemoryUsage(counts);
            if (estimatedMemoryUsage < 100 * 1024 * 1024) // 100MB 미만인 경우만 캐시
            {
                _fieldValueCountsCache.Set(cacheKey, counts);
                _logger.LogDebug("필드값 카운트 캐시 저장: {TableName}.{FieldName} ({UniqueCount}개 고유값, 예상 메모리: {MemoryMB:F2}MB)",
                    tableName, fieldName, counts.Count, estimatedMemoryUsage / (1024.0 * 1024.0));
            }
            else
            {
                _logger.LogWarning("필드값 카운트 캐시 저장 건너뜀 - 메모리 사용량 초과: {TableName}.{FieldName} (예상 메모리: {MemoryMB:F2}MB)",
                    tableName, fieldName, estimatedMemoryUsage / (1024.0 * 1024.0));
            }

            return counts;
        }

        /// <summary>
        /// 레코드 수를 캐시에서 조회하거나 생성합니다
        /// </summary>
        public async Task<long> GetOrCreateRecordCountAsync(
            string gdbPath,
            string tableName,
            Func<Task<long>> countFactory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"recordcount:{gdbPath}:{tableName}";

            // 캐시에서 조회 시도
            if (_recordCountCache.TryGet(cacheKey, out var cachedCount))
            {
                _logger.LogTrace("레코드 수 캐시 히트: {TableName} ({RecordCount}개)", tableName, cachedCount);
                return cachedCount;
            }

            // 캐시 미스 - 새로 생성
            _logger.LogTrace("레코드 수 캐시 미스 - 새로 생성: {TableName}", tableName);
            var count = await countFactory();

            // 캐시에 저장
            _recordCountCache.Set(cacheKey, count);
            _logger.LogDebug("레코드 수 캐시 저장: {TableName} ({RecordCount}개)", tableName, count);

            return count;
        }

        /// <summary>
        /// 특정 테이블의 캐시를 무효화합니다
        /// </summary>
        public void InvalidateTableCache(string gdbPath, string tableName)
        {
            var patterns = new[]
            {
                $"metadata:{gdbPath}:{tableName}",
                $"recordcount:{gdbPath}:{tableName}"
            };

            var removedCount = 0;
            foreach (var pattern in patterns)
            {
                if (_tableMetadataCache.Remove(pattern) || _recordCountCache.Remove(pattern))
                {
                    removedCount++;
                }
            }

            // 필드별 캐시는 패턴 매칭으로 제거 (간단한 구현)
            InvalidateCacheByPattern(_referenceKeysCache, $"refkeys:{gdbPath}:{tableName}:");
            InvalidateCacheByPattern(_fieldValueCountsCache, $"counts:{gdbPath}:{tableName}:");

            _logger.LogDebug("테이블 캐시 무효화 완료: {TableName} ({RemovedCount}개 항목 제거)", tableName, removedCount);
        }

        /// <summary>
        /// 특정 필드의 캐시를 무효화합니다
        /// </summary>
        public void InvalidateFieldCache(string gdbPath, string tableName, string fieldName)
        {
            var patterns = new[]
            {
                $"refkeys:{gdbPath}:{tableName}:{fieldName}",
                $"counts:{gdbPath}:{tableName}:{fieldName}"
            };

            var removedCount = 0;
            foreach (var pattern in patterns)
            {
                if (_referenceKeysCache.Remove(pattern) || _fieldValueCountsCache.Remove(pattern))
                {
                    removedCount++;
                }
            }

            _logger.LogDebug("필드 캐시 무효화 완료: {TableName}.{FieldName} ({RemovedCount}개 항목 제거)", 
                tableName, fieldName, removedCount);
        }

        /// <summary>
        /// 모든 캐시를 비웁니다
        /// </summary>
        public void ClearAllCaches()
        {
            _tableMetadataCache.Clear();
            _referenceKeysCache.Clear();
            _fieldValueCountsCache.Clear();
            _recordCountCache.Clear();

            _logger.LogInformation("모든 캐시 삭제 완료");
        }

        /// <summary>
        /// 캐시 통계 정보를 반환합니다
        /// </summary>
        public ValidationCacheStatistics GetCacheStatistics()
        {
            var stats = new ValidationCacheStatistics
            {
                TableMetadataCache = _tableMetadataCache.GetStatistics(),
                ReferenceKeysCache = _referenceKeysCache.GetStatistics(),
                FieldValueCountsCache = _fieldValueCountsCache.GetStatistics(),
                RecordCountCache = _recordCountCache.GetStatistics(),
                LastUpdated = DateTime.UtcNow
            };

            // 통계 업데이트 이벤트 발생
            StatisticsUpdated?.Invoke(this, new CacheStatisticsEventArgs { Statistics = stats });

            return stats;
        }

        /// <summary>
        /// 참조 키의 예상 메모리 사용량을 계산합니다
        /// </summary>
        private long EstimateReferenceKeysMemoryUsage(HashSet<string> keys)
        {
            long totalSize = 0;
            foreach (var key in keys)
            {
                totalSize += (key?.Length ?? 0) * 2; // UTF-16 문자당 2바이트
            }
            totalSize += keys.Count * 8; // HashSet 오버헤드 (대략적)
            return totalSize;
        }

        /// <summary>
        /// 필드값 카운트의 예상 메모리 사용량을 계산합니다
        /// </summary>
        private long EstimateFieldValueCountsMemoryUsage(Dictionary<string, int> counts)
        {
            long totalSize = 0;
            foreach (var kvp in counts)
            {
                totalSize += (kvp.Key?.Length ?? 0) * 2; // 키 문자열
                totalSize += 4; // int 값
            }
            totalSize += counts.Count * 16; // Dictionary 오버헤드 (대략적)
            return totalSize;
        }

        /// <summary>
        /// 패턴으로 캐시 항목을 무효화합니다 (간단한 구현)
        /// </summary>
        private void InvalidateCacheByPattern<T>(ILruCache<string, T> cache, string pattern)
        {
            // 실제 구현에서는 더 효율적인 패턴 매칭이 필요할 수 있음
            // 현재는 간단한 구현으로 대체
            _logger.LogTrace("패턴 기반 캐시 무효화: {Pattern}", pattern);
        }

        /// <summary>
        /// 주기적 캐시 정리를 수행합니다
        /// </summary>
        private void PerformPeriodicCleanup(object? state)
        {
            try
            {
                var maxAge = TimeSpan.FromHours(2); // 2시간 이상 사용되지 않은 항목 제거
                
                var removedMetadata = _tableMetadataCache.CleanupExpired(maxAge);
                var removedRefKeys = _referenceKeysCache.CleanupExpired(maxAge);
                var removedCounts = _fieldValueCountsCache.CleanupExpired(maxAge);
                var removedRecordCounts = _recordCountCache.CleanupExpired(maxAge);

                var totalRemoved = removedMetadata + removedRefKeys + removedCounts + removedRecordCounts;

                if (totalRemoved > 0)
                {
                    _logger.LogInformation("주기적 캐시 정리 완료 - 제거된 항목: 메타데이터 {Metadata}개, 참조키 {RefKeys}개, " +
                                         "카운트 {Counts}개, 레코드수 {RecordCounts}개 (총 {Total}개)",
                        removedMetadata, removedRefKeys, removedCounts, removedRecordCounts, totalRemoved);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "주기적 캐시 정리 중 오류 발생");
            }
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _tableMetadataCache?.Dispose();
            _referenceKeysCache?.Dispose();
            _fieldValueCountsCache?.Dispose();
            _recordCountCache?.Dispose();
            
            _logger.LogInformation("검수 캐시 서비스 종료됨");
        }
    }

    /// <summary>
    /// 검수 캐시 서비스 인터페이스
    /// </summary>
    public interface IValidationCacheService : IDisposable
    {
        /// <summary>
        /// 캐시 통계 업데이트 이벤트
        /// </summary>
        event EventHandler<CacheStatisticsEventArgs>? StatisticsUpdated;

        /// <summary>
        /// 테이블 메타데이터를 캐시에서 조회하거나 생성합니다
        /// </summary>
        Task<TableMetadata> GetOrCreateTableMetadataAsync(
            string gdbPath,
            string tableName,
            Func<Task<TableMetadata>> metadataFactory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 참조 키를 캐시에서 조회하거나 생성합니다
        /// </summary>
        Task<HashSet<string>> GetOrCreateReferenceKeysAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            Func<Task<HashSet<string>>> keysFactory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 필드값 카운트를 캐시에서 조회하거나 생성합니다
        /// </summary>
        Task<Dictionary<string, int>> GetOrCreateFieldValueCountsAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            Func<Task<Dictionary<string, int>>> countsFactory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 레코드 수를 캐시에서 조회하거나 생성합니다
        /// </summary>
        Task<long> GetOrCreateRecordCountAsync(
            string gdbPath,
            string tableName,
            Func<Task<long>> countFactory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 특정 테이블의 캐시를 무효화합니다
        /// </summary>
        void InvalidateTableCache(string gdbPath, string tableName);

        /// <summary>
        /// 특정 필드의 캐시를 무효화합니다
        /// </summary>
        void InvalidateFieldCache(string gdbPath, string tableName, string fieldName);

        /// <summary>
        /// 모든 캐시를 비웁니다
        /// </summary>
        void ClearAllCaches();

        /// <summary>
        /// 캐시 통계 정보를 반환합니다
        /// </summary>
        ValidationCacheStatistics GetCacheStatistics();
    }

    /// <summary>
    /// 테이블 메타데이터
    /// </summary>
    public class TableMetadata
    {
        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 필드 목록
        /// </summary>
        public List<FieldMetadata> Fields { get; set; } = new();

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string? GeometryType { get; set; }

        /// <summary>
        /// 공간 참조 시스템
        /// </summary>
        public string? SpatialReference { get; set; }

        /// <summary>
        /// 메타데이터 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 필드 메타데이터
    /// </summary>
    public class FieldMetadata
    {
        /// <summary>
        /// 필드명
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 타입
        /// </summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>
        /// 필드 길이
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// NULL 허용 여부
        /// </summary>
        public bool AllowNull { get; set; } = true;
    }

    /// <summary>
    /// 검수 캐시 통계 정보
    /// </summary>
    public class ValidationCacheStatistics
    {
        /// <summary>
        /// 테이블 메타데이터 캐시 통계
        /// </summary>
        public CacheStatistics TableMetadataCache { get; set; } = new();

        /// <summary>
        /// 참조 키 캐시 통계
        /// </summary>
        public CacheStatistics ReferenceKeysCache { get; set; } = new();

        /// <summary>
        /// 필드값 카운트 캐시 통계
        /// </summary>
        public CacheStatistics FieldValueCountsCache { get; set; } = new();

        /// <summary>
        /// 레코드 수 캐시 통계
        /// </summary>
        public CacheStatistics RecordCountCache { get; set; } = new();

        /// <summary>
        /// 통계 업데이트 시간
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// 전체 캐시 히트율
        /// </summary>
        public double OverallHitRatio
        {
            get
            {
                var totalHits = TableMetadataCache.HitCount + ReferenceKeysCache.HitCount + 
                               FieldValueCountsCache.HitCount + RecordCountCache.HitCount;
                var totalRequests = TableMetadataCache.TotalRequests + ReferenceKeysCache.TotalRequests + 
                                   FieldValueCountsCache.TotalRequests + RecordCountCache.TotalRequests;
                
                return totalRequests > 0 ? (double)totalHits / totalRequests : 0.0;
            }
        }
    }

    /// <summary>
    /// 캐시 통계 이벤트 인자
    /// </summary>
    public class CacheStatisticsEventArgs : EventArgs
    {
        /// <summary>
        /// 캐시 통계 정보
        /// </summary>
        public ValidationCacheStatistics Statistics { get; set; } = new();
    }
}
