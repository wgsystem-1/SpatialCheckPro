using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// Microsoft.Extensions.Caching.Memory를 사용한 인메모리 캐시 서비스
    /// </summary>
    public class DataCacheService : IDataCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<DataCacheService> _logger;
        private readonly MemoryCacheEntryOptions _defaultOptions;

        public DataCacheService(ILogger<DataCacheService> logger)
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
            _logger = logger;

            // 기본 캐시 옵션: 10분 후 만료
            _defaultOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (TryGetValue(key, out T? value) && value != null)
            {
                _logger.LogDebug("Cache HIT: {Key}", key);
                return value;
            }

            _logger.LogDebug("Cache MISS: {Key}", key);
            var result = await factory();

            if (result != null)
            {
                Set(key, result, absoluteExpirationRelativeToNow);
            }

            return result;
        }

        public bool TryGetValue<T>(string key, out T? value)
        {
            return _cache.TryGetValue(key, out value);
        }

        public void Set<T>(string key, T value, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            var options = absoluteExpirationRelativeToNow.HasValue
                ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow }
                : _defaultOptions;

            _cache.Set(key, value, options);
            _logger.LogDebug("Cache SET: {Key}", key);
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
            _logger.LogDebug("Cache REMOVE: {Key}", key);
        }
    }
}
