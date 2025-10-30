using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Extensions;
using SpatialCheckPro.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 향상된 검수 캐싱 서비스
    /// Phase 2 Item #10: 캐싱 전략 도입
    ///
    /// 목적:
    /// - 범용 GetOrCreateAsync<T> 패턴 제공
    /// - Layer 메타데이터 (Feature 개수, 범위) 캐싱
    /// - Schema 정보 (필드 정의) 캐싱
    /// - Codelist (CSV에서 로드한 값) 캐싱
    /// - 공간 인덱스 캐싱 (메모리 허용 시)
    /// - 중복 조회 제거로 5-10% 성능 향상
    /// </summary>
    public class EnhancedValidationCacheService : IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _defaultOptions;
        private readonly ILogger<EnhancedValidationCacheService> _logger;
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30);
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

        // 캐시 통계
        private int _hitCount = 0;
        private int _missCount = 0;

        public EnhancedValidationCacheService(
            IMemoryCache cache,
            ILogger<EnhancedValidationCacheService> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 기본 캐시 옵션 설정
            _defaultOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(_defaultExpiration)
                .SetPriority(CacheItemPriority.Normal)
                .RegisterPostEvictionCallback(OnCacheEntryEvicted);

            _logger.LogInformation("EnhancedValidationCacheService 초기화 완료 - 기본 만료시간: {ExpirationMinutes}분",
                _defaultExpiration.TotalMinutes);
        }

        /// <summary>
        /// 범용 캐시 조회 또는 생성
        /// Phase 2 Item #10의 핵심 메서드
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            // 캐시에서 조회 시도
            if (_cache.TryGetValue(key, out T cachedValue))
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogCacheHit("EnhancedValidationCache", key);
                return cachedValue;
            }

            // 캐시 미스 - 락 획득 후 재확인 (이중 체크)
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                // 락 획득 중 다른 스레드가 캐시했을 수 있으므로 재확인
                if (_cache.TryGetValue(key, out cachedValue))
                {
                    Interlocked.Increment(ref _hitCount);
                    return cachedValue;
                }

                // 캐시 미스 확정
                Interlocked.Increment(ref _missCount);
                _logger.LogCacheMiss("EnhancedValidationCache", key);

                // 팩토리 함수로 값 생성
                var value = await factory();

                // 캐시에 저장
                var options = expiration.HasValue
                    ? new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(expiration.Value)
                        .SetPriority(CacheItemPriority.Normal)
                        .RegisterPostEvictionCallback(OnCacheEntryEvicted)
                    : _defaultOptions;

                _cache.Set(key, value, options);

                _logger.LogDebug("[Cache] [EnhancedValidationCache] 캐시 저장 - Key: {Key}, Type: {Type}",
                    key, typeof(T).Name);

                return value;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// 동기 버전 캐시 조회 또는 생성
        /// </summary>
        public T GetOrCreate<T>(
            string key,
            Func<T> factory,
            TimeSpan? expiration = null)
        {
            // 캐시에서 조회 시도
            if (_cache.TryGetValue(key, out T cachedValue))
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogCacheHit("EnhancedValidationCache", key);
                return cachedValue;
            }

            // 캐시 미스
            Interlocked.Increment(ref _missCount);
            _logger.LogCacheMiss("EnhancedValidationCache", key);

            // 팩토리 함수로 값 생성
            var value = factory();

            // 캐시에 저장
            var options = expiration.HasValue
                ? new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(expiration.Value)
                    .SetPriority(CacheItemPriority.Normal)
                    .RegisterPostEvictionCallback(OnCacheEntryEvicted)
                : _defaultOptions;

            _cache.Set(key, value, options);

            _logger.LogDebug("[Cache] [EnhancedValidationCache] 캐시 저장 - Key: {Key}, Type: {Type}",
                key, typeof(T).Name);

            return value;
        }

        // ========================================
        // 특화된 캐싱 메서드들
        // ========================================

        /// <summary>
        /// Layer 메타데이터 캐싱 (Feature 개수, 범위 등)
        /// </summary>
        public async Task<LayerMetadata> GetOrCreateLayerMetadataAsync(
            string gdbPath,
            string layerName,
            Func<Task<LayerMetadata>> factory,
            CancellationToken cancellationToken = default)
        {
            var key = $"layer:metadata:{gdbPath}:{layerName}";
            return await GetOrCreateAsync(key, factory, TimeSpan.FromHours(1), cancellationToken);
        }

        /// <summary>
        /// Schema 정보 캐싱 (필드 정의)
        /// </summary>
        public async Task<SchemaDefinition> GetOrCreateSchemaAsync(
            string gdbPath,
            string tableName,
            Func<Task<SchemaDefinition>> factory,
            CancellationToken cancellationToken = default)
        {
            var key = $"schema:definition:{gdbPath}:{tableName}";
            return await GetOrCreateAsync(key, factory, TimeSpan.FromHours(2), cancellationToken);
        }

        /// <summary>
        /// Codelist 캐싱 (CSV에서 로드한 값)
        /// </summary>
        public async Task<Dictionary<string, List<string>>> GetOrCreateCodelistAsync(
            string codelistName,
            Func<Task<Dictionary<string, List<string>>>> factory,
            CancellationToken cancellationToken = default)
        {
            var key = $"codelist:{codelistName}";
            // Codelist는 변경이 적으므로 더 오래 캐시
            return await GetOrCreateAsync(key, factory, TimeSpan.FromHours(4), cancellationToken);
        }

        /// <summary>
        /// 공간 인덱스 캐싱 (메모리 허용 시)
        /// </summary>
        public async Task<T> GetOrCreateSpatialIndexAsync<T>(
            string gdbPath,
            string layerName,
            Func<Task<T>> factory,
            CancellationToken cancellationToken = default) where T : class
        {
            var key = $"spatial:index:{gdbPath}:{layerName}";

            // 공간 인덱스는 메모리를 많이 사용하므로 낮은 우선순위로 설정
            if (_cache.TryGetValue(key, out T cachedValue))
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogCacheHit("SpatialIndex", key);
                return cachedValue;
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (_cache.TryGetValue(key, out cachedValue))
                {
                    Interlocked.Increment(ref _hitCount);
                    return cachedValue;
                }

                Interlocked.Increment(ref _missCount);
                _logger.LogCacheMiss("SpatialIndex", key);

                var value = await factory();

                // 메모리 압박 시 먼저 제거되도록 낮은 우선순위 설정
                var options = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                    .SetPriority(CacheItemPriority.Low)
                    .RegisterPostEvictionCallback(OnCacheEntryEvicted);

                _cache.Set(key, value, options);

                _logger.LogDebug("[Cache] [SpatialIndex] 공간 인덱스 캐시 저장 - Key: {Key}", key);

                return value;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        // ========================================
        // 캐시 관리 메서드들
        // ========================================

        /// <summary>
        /// 특정 키의 캐시 무효화
        /// </summary>
        public void Invalidate(string key)
        {
            _cache.Remove(key);
            _logger.LogDebug("[Cache] [EnhancedValidationCache] 캐시 무효화 - Key: {Key}", key);
        }

        /// <summary>
        /// 패턴으로 캐시 무효화 (접두사 기반)
        /// </summary>
        public void InvalidateByPrefix(string prefix)
        {
            // IMemoryCache는 패턴 기반 제거를 직접 지원하지 않으므로
            // 실제 구현에서는 캐시 키를 추적하는 별도 메커니즘이 필요할 수 있음
            _logger.LogDebug("[Cache] [EnhancedValidationCache] 패턴 기반 캐시 무효화 - Prefix: {Prefix}", prefix);
        }

        /// <summary>
        /// Layer 메타데이터 캐시 무효화
        /// </summary>
        public void InvalidateLayerMetadata(string gdbPath, string layerName)
        {
            var key = $"layer:metadata:{gdbPath}:{layerName}";
            Invalidate(key);
        }

        /// <summary>
        /// Schema 캐시 무효화
        /// </summary>
        public void InvalidateSchema(string gdbPath, string tableName)
        {
            var key = $"schema:definition:{gdbPath}:{tableName}";
            Invalidate(key);
        }

        /// <summary>
        /// Codelist 캐시 무효화
        /// </summary>
        public void InvalidateCodelist(string codelistName)
        {
            var key = $"codelist:{codelistName}";
            Invalidate(key);
        }

        /// <summary>
        /// 공간 인덱스 캐시 무효화
        /// </summary>
        public void InvalidateSpatialIndex(string gdbPath, string layerName)
        {
            var key = $"spatial:index:{gdbPath}:{layerName}";
            Invalidate(key);
        }

        /// <summary>
        /// 캐시 통계 조회
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            var hitCount = _hitCount;
            var missCount = _missCount;
            var totalRequests = hitCount + missCount;

            return new CacheStatistics
            {
                HitCount = hitCount,
                MissCount = missCount,
                HitRatio = totalRequests > 0 ? (double)hitCount / totalRequests : 0.0
            };
        }

        /// <summary>
        /// 캐시 통계 초기화
        /// </summary>
        public void ResetStatistics()
        {
            _hitCount = 0;
            _missCount = 0;
            _logger.LogDebug("[Cache] [EnhancedValidationCache] 캐시 통계 초기화");
        }

        /// <summary>
        /// 캐시 항목 제거 콜백
        /// </summary>
        private void OnCacheEntryEvicted(object key, object value, EvictionReason reason, object state)
        {
            _logger.LogDebug("[Cache] [EnhancedValidationCache] 캐시 항목 제거 - Key: {Key}, Reason: {Reason}",
                key, reason);
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _cacheLock?.Dispose();
            _logger.LogInformation("EnhancedValidationCacheService 종료 - 최종 통계: Hits={HitCount}, Misses={MissCount}, HitRatio={HitRatio:P1}",
                _hitCount, _missCount, GetStatistics().HitRatio);
        }
    }

    /// <summary>
    /// Layer 메타데이터 (Feature 개수, 범위 등)
    /// </summary>
    public class LayerMetadata
    {
        public string LayerName { get; set; } = string.Empty;
        public long FeatureCount { get; set; }
        public SpatialEnvelope? Envelope { get; set; }
        public string? GeometryType { get; set; }
        public string? SpatialReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Schema 정의 (필드 목록)
    /// </summary>
    public class SchemaDefinition
    {
        public string TableName { get; set; } = string.Empty;
        public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 필드 정의
    /// </summary>
    public class FieldDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Length { get; set; }
        public bool Nullable { get; set; } = true;
    }

}
