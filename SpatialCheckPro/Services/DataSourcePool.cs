using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Threading.Tasks;
using System.Threading;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// DataSource 풀링 서비스 구현
    /// 동일한 GDB 파일에 대한 중복 오픈을 방지하여 I/O 성능을 최적화합니다.
    /// 스레드 안전성을 보장합니다.
    /// </summary>
    public class DataSourcePool : IDataSourcePool
    {
        private readonly ILogger<DataSourcePool> _logger;
        private readonly ConcurrentDictionary<string, PooledDataSource> _pool = new();
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly Timer _cleanupTimer;
        private readonly int _maxPoolSize;
        private readonly TimeSpan _idleTimeout;

        public DataSourcePool(ILogger<DataSourcePool> logger, int maxPoolSize = 50, TimeSpan? idleTimeout = null)
        {
            _logger = logger;
            _maxPoolSize = maxPoolSize;
            _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30);
            
            // 주기적으로 사용하지 않는 DataSource 정리
            _cleanupTimer = new Timer(CleanupIdleDataSources, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// 풀링된 DataSource 정보
        /// </summary>
        private class PooledDataSource
        {
            public DataSource DataSource { get; set; }
            public int ReferenceCount { get; set; }
            public DateTime LastAccessed { get; set; }
            public string FilePath { get; set; }

            public PooledDataSource(DataSource dataSource, string filePath)
            {
                DataSource = dataSource;
                ReferenceCount = 0;
                LastAccessed = DateTime.Now;
                FilePath = filePath;
            }
        }

        public int PoolSize => _pool.Count;

        public DataSource? GetDataSource(string gdbPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DataSourcePool));

            if (string.IsNullOrEmpty(gdbPath))
                return null;

            // 경로 정규화
            var normalizedPath = System.IO.Path.GetFullPath(gdbPath);

            lock (_lockObject)
            {
                // 기존 DataSource가 있으면 재사용 (단, Dispose되지 않은 경우만)
                if (_pool.TryGetValue(normalizedPath, out var pooled))
                {
                    // ⚠️ Dispose 여부 검사 (네이티브 핸들 확인)
                    try
                    {
                        // DataSource가 유효한지 확인 (LayerCount 접근으로 테스트)
                        var layerCount = pooled.DataSource.GetLayerCount();

                        // 유효하면 재사용
                        pooled.ReferenceCount++;
                        pooled.LastAccessed = DateTime.Now;

                        _logger.LogDebug("DataSource 풀 히트: {Path}, 참조 카운트: {RefCount}",
                            normalizedPath, pooled.ReferenceCount);

                        return pooled.DataSource;
                    }
                    catch (Exception ex)
                    {
                        // Dispose되었거나 손상된 DataSource - 풀에서 제거
                        _logger.LogWarning(ex, "풀에 있는 DataSource가 유효하지 않음: {Path}, 재생성 중...", normalizedPath);
                        _pool.TryRemove(normalizedPath, out _);
                        // 아래에서 새로 생성됨
                    }
                }

                // 풀 크기 제한 확인
                if (_pool.Count >= _maxPoolSize)
                {
                    _logger.LogWarning("DataSource 풀 크기 제한 도달: {MaxSize}, 오래된 항목 정리 중...", _maxPoolSize);
                    CleanupOldestDataSources();
                }

                // 새로운 DataSource 생성
                _logger.LogInformation("새 DataSource 생성: {Path}", normalizedPath);
                
                var dataSource = Ogr.Open(normalizedPath, 0);
                if (dataSource == null)
                {
                    _logger.LogError("DataSource 오픈 실패: {Path}", normalizedPath);
                    return null;
                }

                var newPooled = new PooledDataSource(dataSource, normalizedPath);
                newPooled.ReferenceCount = 1;
                
                _pool[normalizedPath] = newPooled;
                
                _logger.LogInformation("DataSource 풀에 추가: {Path}, 풀 크기: {PoolSize}", 
                    normalizedPath, _pool.Count);
                
                return dataSource;
            }
        }

        public void ReturnDataSource(string gdbPath, DataSource dataSource)
        {
            if (_disposed || string.IsNullOrEmpty(gdbPath) || dataSource == null)
                return;

            var normalizedPath = System.IO.Path.GetFullPath(gdbPath);

            lock (_lockObject)
            {
                if (_pool.TryGetValue(normalizedPath, out var pooled))
                {
                    pooled.ReferenceCount--;
                    pooled.LastAccessed = DateTime.Now;
                    
                    _logger.LogDebug("DataSource 반환: {Path}, 참조 카운트: {RefCount}", 
                        normalizedPath, pooled.ReferenceCount);

                    // 참조 카운트가 0이 되어도 즉시 해제하지 않고 풀에 유지
                    // 곧 다시 사용될 가능성이 높기 때문
                    if (pooled.ReferenceCount < 0)
                    {
                        _logger.LogWarning("DataSource 참조 카운트 음수: {Path}, {RefCount}", 
                            normalizedPath, pooled.ReferenceCount);
                        pooled.ReferenceCount = 0;
                    }
                }
                else
                {
                    _logger.LogWarning("풀에 없는 DataSource 반환 시도: {Path}", normalizedPath);
                }
            }
        }

        public void RemoveDataSource(string gdbPath)
        {
            if (_disposed || string.IsNullOrEmpty(gdbPath))
                return;

            var normalizedPath = System.IO.Path.GetFullPath(gdbPath);

            lock (_lockObject)
            {
                if (_pool.TryRemove(normalizedPath, out var pooled))
                {
                    if (pooled.ReferenceCount > 0)
                    {
                        _logger.LogWarning("사용 중인 DataSource 강제 제거: {Path}, 참조 카운트: {RefCount}", 
                            normalizedPath, pooled.ReferenceCount);
                    }

                    pooled.DataSource?.Dispose();
                    _logger.LogInformation("DataSource 풀에서 제거: {Path}", normalizedPath);
                }
            }
        }

        public void ClearPool()
        {
            lock (_lockObject)
            {
                var count = _pool.Count;
                
                foreach (var kvp in _pool)
                {
                    var pooled = kvp.Value;
                    if (pooled.ReferenceCount > 0)
                    {
                        _logger.LogWarning("사용 중인 DataSource 강제 정리: {Path}, 참조 카운트: {RefCount}", 
                            kvp.Key, pooled.ReferenceCount);
                    }
                    
                    pooled.DataSource?.Dispose();
                }
                
                _pool.Clear();
                
                if (count > 0)
                {
                    _logger.LogInformation("DataSource 풀 전체 정리 완료: {Count}개 항목", count);
                }
            }
        }

        /// <summary>
        /// 오래된 DataSource 정리 (30분 이상 사용되지 않은 것)
        /// </summary>
        public void CleanupExpiredDataSources(TimeSpan? maxAge = null)
        {
            if (_disposed)
                return;

            var cutoffTime = DateTime.Now - (maxAge ?? TimeSpan.FromMinutes(30));
            var expiredPaths = new List<string>();

            lock (_lockObject)
            {
                foreach (var kvp in _pool)
                {
                    var pooled = kvp.Value;
                    if (pooled.ReferenceCount == 0 && pooled.LastAccessed < cutoffTime)
                    {
                        expiredPaths.Add(kvp.Key);
                    }
                }

                foreach (var path in expiredPaths)
                {
                    if (_pool.TryRemove(path, out var pooled))
                    {
                        pooled.DataSource?.Dispose();
                        _logger.LogInformation("만료된 DataSource 정리: {Path}", path);
                    }
                }
            }

            if (expiredPaths.Count > 0)
            {
                _logger.LogInformation("만료된 DataSource 정리 완료: {Count}개 항목", expiredPaths.Count);
            }
        }

        /// <summary>
        /// 사용하지 않는 DataSource를 주기적으로 정리
        /// </summary>
        private void CleanupIdleDataSources(object? state)
        {
            try
            {
                CleanupExpiredDataSources(_idleTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataSource 정리 중 오류 발생");
            }
        }

        /// <summary>
        /// 가장 오래된 DataSource들을 정리하여 풀 크기 제한 유지
        /// </summary>
        private void CleanupOldestDataSources()
        {
            var oldestEntries = new List<KeyValuePair<string, PooledDataSource>>();

            lock (_lockObject)
            {
                foreach (var kvp in _pool)
                {
                    if (kvp.Value.ReferenceCount == 0)
                    {
                        oldestEntries.Add(kvp);
                    }
                }

                // 가장 오래된 순으로 정렬
                oldestEntries.Sort((a, b) => a.Value.LastAccessed.CompareTo(b.Value.LastAccessed));

                // 풀 크기의 20%만큼 정리
                var cleanupCount = Math.Max(1, _pool.Count / 5);
                var toRemove = oldestEntries.Take(cleanupCount).ToList();

                foreach (var entry in toRemove)
                {
                    if (_pool.TryRemove(entry.Key, out var pooled))
                    {
                        pooled.DataSource?.Dispose();
                        _logger.LogInformation("오래된 DataSource 정리: {Path}", entry.Key);
                    }
                }
            }

            if (oldestEntries.Count > 0)
            {
                _logger.LogInformation("오래된 DataSource 정리 완료: {Count}개 항목", oldestEntries.Count);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                ClearPool();
                _disposed = true;
                _logger.LogInformation("DataSourcePool 리소스 정리 완료");
            }
        }
    }
}
