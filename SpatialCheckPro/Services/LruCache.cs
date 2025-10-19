using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// LRU(Least Recently Used) 캐시 구현
    /// </summary>
    public class LruCache<TKey, TValue> : ILruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxCapacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _lruList;
        private readonly ReaderWriterLockSlim _lock;
        private readonly ILogger<LruCache<TKey, TValue>>? _logger;

        /// <summary>
        /// 캐시 히트 수
        /// </summary>
        private long _hitCount;
        public long HitCount => _hitCount;

        /// <summary>
        /// 캐시 미스 수
        /// </summary>
        private long _missCount;
        public long MissCount => _missCount;

        /// <summary>
        /// 현재 캐시 항목 수
        /// </summary>
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _cache.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// 캐시 히트율 (0.0 ~ 1.0)
        /// </summary>
        public double HitRatio
        {
            get
            {
                var total = HitCount + MissCount;
                return total > 0 ? (double)HitCount / total : 0.0;
            }
        }

        public LruCache(int maxCapacity, ILogger<LruCache<TKey, TValue>>? logger = null)
        {
            if (maxCapacity <= 0)
                throw new ArgumentException("최대 용량은 1 이상이어야 합니다.", nameof(maxCapacity));

            _maxCapacity = maxCapacity;
            _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(maxCapacity);
            _lruList = new LinkedList<CacheItem>();
            _lock = new ReaderWriterLockSlim();
            _logger = logger;

            _logger?.LogDebug("LRU 캐시 초기화 완료 - 최대 용량: {MaxCapacity}", maxCapacity);
        }

        /// <summary>
        /// 캐시에서 값을 조회합니다
        /// </summary>
        public bool TryGet(TKey key, out TValue? value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    // 캐시 히트 - 노드를 맨 앞으로 이동
                    _lock.EnterWriteLock();
                    try
                    {
                        _lruList.Remove(node);
                        _lruList.AddFirst(node);
                        node.Value.LastAccessTime = DateTime.UtcNow;
                        
                        Interlocked.Increment(ref _hitCount);
                        value = node.Value.Value;
                        
                        _logger?.LogTrace("캐시 히트: {Key}", key);
                        return true;
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
                else
                {
                    // 캐시 미스
                    Interlocked.Increment(ref _missCount);
                    value = default;
                    
                    _logger?.LogTrace("캐시 미스: {Key}", key);
                    return false;
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        /// 캐시에 값을 저장합니다
        /// </summary>
        public void Set(TKey key, TValue value)
        {
            _lock.EnterWriteLock();
            try
            {
                var now = DateTime.UtcNow;

                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // 기존 항목 업데이트
                    existingNode.Value.Value = value;
                    existingNode.Value.LastAccessTime = now;
                    existingNode.Value.CreatedTime = now;
                    
                    // 맨 앞으로 이동
                    _lruList.Remove(existingNode);
                    _lruList.AddFirst(existingNode);
                    
                    _logger?.LogTrace("캐시 업데이트: {Key}", key);
                }
                else
                {
                    // 새 항목 추가
                    var newItem = new CacheItem
                    {
                        Key = key,
                        Value = value,
                        CreatedTime = now,
                        LastAccessTime = now
                    };

                    var newNode = new LinkedListNode<CacheItem>(newItem);
                    
                    // 용량 초과 시 가장 오래된 항목 제거
                    if (_cache.Count >= _maxCapacity)
                    {
                        RemoveLeastRecentlyUsed();
                    }

                    _cache[key] = newNode;
                    _lruList.AddFirst(newNode);
                    
                    _logger?.LogTrace("캐시 추가: {Key} (현재 크기: {CurrentSize}/{MaxCapacity})", 
                        key, _cache.Count, _maxCapacity);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 캐시에서 특정 키를 제거합니다
        /// </summary>
        public bool Remove(TKey key)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _cache.Remove(key);
                    _lruList.Remove(node);
                    
                    _logger?.LogTrace("캐시 제거: {Key}", key);
                    return true;
                }
                
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 캐시를 모두 비웁니다
        /// </summary>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                var count = _cache.Count;
                _cache.Clear();
                _lruList.Clear();
                
                _logger?.LogDebug("캐시 전체 삭제 완료 - 삭제된 항목 수: {Count}", count);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 만료된 캐시 항목들을 정리합니다
        /// </summary>
        public int CleanupExpired(TimeSpan maxAge)
        {
            _lock.EnterWriteLock();
            try
            {
                var cutoffTime = DateTime.UtcNow - maxAge;
                var expiredKeys = new List<TKey>();

                // 만료된 항목 찾기
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.Value.LastAccessTime < cutoffTime)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // 만료된 항목 제거
                foreach (var key in expiredKeys)
                {
                    if (_cache.TryGetValue(key, out var node))
                    {
                        _cache.Remove(key);
                        _lruList.Remove(node);
                    }
                }

                if (expiredKeys.Count > 0)
                {
                    _logger?.LogDebug("만료된 캐시 항목 정리 완료 - 제거된 항목 수: {ExpiredCount}", expiredKeys.Count);
                }

                return expiredKeys.Count;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 캐시 통계 정보를 반환합니다
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            _lock.EnterReadLock();
            try
            {
                return new CacheStatistics
                {
                    MaxCapacity = _maxCapacity,
                    CurrentSize = _cache.Count,
                    HitCount = HitCount,
                    MissCount = MissCount,
                    HitRatio = HitRatio,
                    UtilizationRatio = (double)_cache.Count / _maxCapacity
                };
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// 가장 오래된 항목을 제거합니다
        /// </summary>
        private void RemoveLeastRecentlyUsed()
        {
            if (_lruList.Last != null)
            {
                var lruItem = _lruList.Last.Value;
                _cache.Remove(lruItem.Key);
                _lruList.RemoveLast();
                
                _logger?.LogTrace("LRU 항목 제거: {Key}", lruItem.Key);
            }
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _lock?.Dispose();
            _logger?.LogDebug("LRU 캐시 종료됨");
        }

        /// <summary>
        /// 캐시 항목 클래스
        /// </summary>
        private class CacheItem
        {
            public TKey Key { get; set; } = default!;
            public TValue Value { get; set; } = default!;
            public DateTime CreatedTime { get; set; }
            public DateTime LastAccessTime { get; set; }
        }
    }

    /// <summary>
    /// LRU 캐시 인터페이스
    /// </summary>
    public interface ILruCache<TKey, TValue> : IDisposable where TKey : notnull
    {
        /// <summary>
        /// 캐시 히트 수
        /// </summary>
        long HitCount { get; }

        /// <summary>
        /// 캐시 미스 수
        /// </summary>
        long MissCount { get; }

        /// <summary>
        /// 현재 캐시 항목 수
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 캐시 히트율 (0.0 ~ 1.0)
        /// </summary>
        double HitRatio { get; }

        /// <summary>
        /// 캐시에서 값을 조회합니다
        /// </summary>
        bool TryGet(TKey key, out TValue? value);

        /// <summary>
        /// 캐시에 값을 저장합니다
        /// </summary>
        void Set(TKey key, TValue value);

        /// <summary>
        /// 캐시에서 특정 키를 제거합니다
        /// </summary>
        bool Remove(TKey key);

        /// <summary>
        /// 캐시를 모두 비웁니다
        /// </summary>
        void Clear();

        /// <summary>
        /// 만료된 캐시 항목들을 정리합니다
        /// </summary>
        int CleanupExpired(TimeSpan maxAge);

        /// <summary>
        /// 캐시 통계 정보를 반환합니다
        /// </summary>
        CacheStatistics GetStatistics();
    }

    /// <summary>
    /// 캐시 통계 정보
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// 최대 용량
        /// </summary>
        public int MaxCapacity { get; set; }

        /// <summary>
        /// 현재 크기
        /// </summary>
        public int CurrentSize { get; set; }

        /// <summary>
        /// 캐시 히트 수
        /// </summary>
        public long HitCount { get; set; }

        /// <summary>
        /// 캐시 미스 수
        /// </summary>
        public long MissCount { get; set; }

        /// <summary>
        /// 캐시 히트율 (0.0 ~ 1.0)
        /// </summary>
        public double HitRatio { get; set; }

        /// <summary>
        /// 캐시 사용률 (0.0 ~ 1.0)
        /// </summary>
        public double UtilizationRatio { get; set; }

        /// <summary>
        /// 총 요청 수
        /// </summary>
        public long TotalRequests => HitCount + MissCount;
    }
}
