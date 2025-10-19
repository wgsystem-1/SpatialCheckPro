using System;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 인메모리 캐시 서비스 인터페이스
    /// </summary>
    public interface IDataCacheService
    {
        /// <summary>
        /// 캐시에서 항목을 가져옵니다. 없으면 생성 함수를 통해 생성하고 캐시에 추가합니다.
        /// </summary>
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpirationRelativeToNow = null);

        /// <summary>
        /// 캐시에서 항목을 가져옵니다.
        /// </summary>
        bool TryGetValue<T>(string key, out T? value);

        /// <summary>
        /// 캐시에 항목을 설정합니다.
        /// </summary>
        void Set<T>(string key, T value, TimeSpan? absoluteExpirationRelativeToNow = null);

        /// <summary>
        /// 캐시에서 항목을 제거합니다.
        /// </summary>
        void Remove(string key);
    }
}
