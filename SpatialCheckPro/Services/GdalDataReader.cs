using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// GDAL 기반 데이터 읽기 서비스 구현체 (메모리 최적화 및 캐싱 포함)
    /// </summary>
    public class GdalDataReader : IGdalDataReader
    {
        private readonly ILogger<GdalDataReader> _logger;
        private readonly IMemoryManager? _memoryManager;
        private readonly IValidationCacheService? _cacheService;
        private readonly int _maxRetryAttempts = 3;
        private readonly int _retryDelayMs = 1000;

        public GdalDataReader(
            ILogger<GdalDataReader> logger, 
            IMemoryManager? memoryManager = null,
            IValidationCacheService? cacheService = null)
        {
            _logger = logger;
            _memoryManager = memoryManager;
            _cacheService = cacheService;
            InitializeGdal();
        }

        /// <summary>
        /// GDAL 초기화
        /// </summary>
        private void InitializeGdal()
        {
            try
            {
                Gdal.AllRegister();
                Ogr.RegisterAll();
                _logger.LogDebug("GDAL 초기화 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 초기화 실패");
                throw;
            }
        }

        /// <summary>
        /// 테이블 존재 여부를 확인합니다
        /// </summary>
        public async Task<bool> IsTableExistsAsync(string gdbPath, string tableName)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    return false;
                }

                var layer = dataSource.GetLayerByName(tableName);
                var exists = layer != null;
                
                layer?.Dispose();
                
                _logger.LogDebug("테이블 존재 확인: {TableName} = {Exists}", tableName, exists);
                return exists;
            });
        }

        /// <summary>
        /// 필드 존재 여부를 확인합니다
        /// </summary>
        public async Task<bool> IsFieldExistsAsync(string gdbPath, string tableName, string fieldName)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    return false;
                }

                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                    return false;
                }

                var layerDefn = layer.GetLayerDefn();
                var fieldIndex = layerDefn.GetFieldIndex(fieldName);
                var exists = fieldIndex >= 0;

                _logger.LogDebug("필드 존재 확인: {TableName}.{FieldName} = {Exists}", 
                    tableName, fieldName, exists);
                
                return exists;
            });
        }

        /// <summary>
        /// 테이블의 레코드 수를 조회합니다 (캐싱 포함)
        /// </summary>
        public async Task<long> GetRecordCountAsync(string gdbPath, string tableName)
        {
            // 캐시 서비스가 있는 경우 캐시 활용
            if (_cacheService != null)
            {
                return await _cacheService.GetOrCreateRecordCountAsync(
                    gdbPath, 
                    tableName, 
                    async () => await GetRecordCountInternalAsync(gdbPath, tableName));
            }
            else
            {
                return await GetRecordCountInternalAsync(gdbPath, tableName);
            }
        }

        /// <summary>
        /// 테이블의 레코드 수를 직접 조회합니다 (내부 구현)
        /// </summary>
        private async Task<long> GetRecordCountInternalAsync(string gdbPath, string tableName)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    return 0L;
                }

                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                    return 0L;
                }

                // GetFeatureCount(1)은 정확한 개수를 반환하지만 느릴 수 있음
                var count = layer.GetFeatureCount(1);
                
                _logger.LogDebug("레코드 수 조회: {TableName} = {Count}", tableName, count);
                return count;
            });
        }

        /// <summary>
        /// 필드의 모든 값을 조회합니다 (메모리 최적화 포함)
        /// </summary>
        public async Task<List<string>> GetAllFieldValuesAsync(
            string gdbPath, 
            string tableName, 
            string fieldName,
            int batchSize = 10000,
            CancellationToken cancellationToken = default)
        {
            var values = new List<string>();
            var processedCount = 0;
            
            await foreach (var value in GetFieldValuesStreamAsync(gdbPath, tableName, fieldName, batchSize, cancellationToken))
            {
                values.Add(value);
                processedCount++;
                
                // 메모리 관리자가 있는 경우 동적 메모리 관리
                if (_memoryManager != null)
                {
                    // 메모리 압박 체크 및 자동 정리
                    if (processedCount % 25000 == 0)
                    {
                        if (_memoryManager.IsMemoryPressureHigh())
                        {
                            _logger.LogDebug("메모리 압박 감지 - 자동 정리 수행 중 (처리된 값: {ProcessedCount}개)", processedCount);
                            await _memoryManager.TryReduceMemoryPressureAsync();
                        }
                    }
                }
                else
                {
                    // 기본 메모리 정리 (메모리 관리자가 없는 경우)
                    if (processedCount % 50000 == 0)
                    {
                        GC.Collect();
                        _logger.LogDebug("메모리 정리 수행. 현재 값 개수: {Count}", values.Count);
                    }
                }
            }

            _logger.LogInformation("필드값 조회 완료: {TableName}.{FieldName} = {Count}개", 
                tableName, fieldName, values.Count);
            
            return values;
        }

        /// <summary>
        /// 필드값을 스트리밍 방식으로 조회합니다
        /// </summary>
        public async IAsyncEnumerable<string> GetFieldValuesStreamAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            int batchSize = 10000,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var batch in GetFieldValuesBatchAsync(gdbPath, tableName, fieldName, batchSize, cancellationToken))
            {
                foreach (var value in batch)
                {
                    yield return value;
                }
            }
        }

        /// <summary>
        /// 특정 필드값을 가진 레코드의 ObjectId 목록을 조회합니다
        /// </summary>
        public async Task<List<long>> GetObjectIdsForFieldValueAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            string fieldValue,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var objectIds = new List<long>();

                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    return objectIds;
                }

                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                    return objectIds;
                }

                var layerDefn = layer.GetLayerDefn();
                var fieldIndex = layerDefn.GetFieldIndex(fieldName);
                
                if (fieldIndex < 0)
                {
                    _logger.LogWarning("필드를 찾을 수 없습니다: {TableName}.{FieldName}", tableName, fieldName);
                    return objectIds;
                }

                layer.ResetReading();
                Feature feature;
                
                while ((feature = layer.GetNextFeature()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        var value = feature.GetFieldAsString(fieldIndex);
                        if (string.Equals(value, fieldValue, StringComparison.OrdinalIgnoreCase))
                        {
                            var objectId = GetObjectId(feature);
                            if (objectId.HasValue)
                            {
                                objectIds.Add(objectId.Value);
                            }
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                _logger.LogDebug("ObjectId 조회 완료: {TableName}.{FieldName}='{FieldValue}' = {Count}개",
                    tableName, fieldName, fieldValue, objectIds.Count);

                return objectIds;
            });
        }

        /// <summary>
        /// 필드값별 개수를 조회합니다 (메모리 최적화 및 캐싱 포함)
        /// </summary>
        public async Task<Dictionary<string, int>> GetFieldValueCountsAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            CancellationToken cancellationToken = default)
        {
            // 캐시 서비스가 있는 경우 캐시 활용
            if (_cacheService != null)
            {
                return await _cacheService.GetOrCreateFieldValueCountsAsync(
                    gdbPath, 
                    tableName, 
                    fieldName,
                    async () => await GetFieldValueCountsInternalAsync(gdbPath, tableName, fieldName, cancellationToken),
                    cancellationToken);
            }
            else
            {
                return await GetFieldValueCountsInternalAsync(gdbPath, tableName, fieldName, cancellationToken);
            }
        }

        /// <summary>
        /// 필드값별 개수를 직접 조회합니다 (내부 구현)
        /// </summary>
        private async Task<Dictionary<string, int>> GetFieldValueCountsInternalAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            CancellationToken cancellationToken = default)
        {
            var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var processedCount = 0;

            // 동적 배치 크기 결정
            var batchSize = _memoryManager?.GetOptimalBatchSize(10000, 1000) ?? 10000;

            await foreach (var value in GetFieldValuesStreamAsync(gdbPath, tableName, fieldName, batchSize, cancellationToken))
            {
                if (valueCounts.ContainsKey(value))
                {
                    valueCounts[value]++;
                }
                else
                {
                    valueCounts[value] = 1;
                }

                processedCount++;

                // 메모리 관리
                if (_memoryManager != null && processedCount % 50000 == 0)
                {
                    if (_memoryManager.IsMemoryPressureHigh())
                    {
                        _logger.LogDebug("메모리 압박 감지 - 자동 정리 수행 중 (집계된 값: {ProcessedCount}개, 고유값: {UniqueCount}개)", 
                            processedCount, valueCounts.Count);
                        await _memoryManager.TryReduceMemoryPressureAsync();
                        
                        // 배치 크기 재조정
                        batchSize = _memoryManager.GetOptimalBatchSize(10000, 1000);
                    }
                }
            }

            _logger.LogInformation("필드값 개수 집계 완료: {TableName}.{FieldName} = {UniqueCount}개 고유값 (총 {ProcessedCount}개 처리)",
                tableName, fieldName, valueCounts.Count, processedCount);

            return valueCounts;
        }

        /// <summary>
        /// 배치 단위로 필드값을 조회합니다
        /// </summary>
        private async IAsyncEnumerable<List<string>> GetFieldValuesBatchAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 재시도 로직을 단순화하여 직접 호출
            await foreach (var batch in GetFieldValuesBatchInternalAsync(gdbPath, tableName, fieldName, batchSize, cancellationToken))
            {
                yield return batch;
            }
        }

        /// <summary>
        /// 내부 배치 조회 구현 (메모리 최적화 포함)
        /// </summary>
        private async IAsyncEnumerable<List<string>> GetFieldValuesBatchInternalAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var dataSource = await OpenDataSourceAsync(gdbPath);
            if (dataSource == null)
            {
                yield break;
            }

            using var layer = dataSource.GetLayerByName(tableName);
            if (layer == null)
            {
                _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                yield break;
            }

            var layerDefn = layer.GetLayerDefn();
            var fieldIndex = layerDefn.GetFieldIndex(fieldName);
            
            if (fieldIndex < 0)
            {
                _logger.LogWarning("필드를 찾을 수 없습니다: {TableName}.{FieldName}", tableName, fieldName);
                yield break;
            }

            layer.ResetReading();
            var batch = new List<string>(batchSize);
            Feature feature;
            var processedCount = 0;
            var batchCount = 0;

            while ((feature = layer.GetNextFeature()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var value = feature.GetFieldAsString(fieldIndex) ?? string.Empty;
                    batch.Add(value);
                    processedCount++;

                    // 동적 배치 크기 조정
                    var currentBatchSize = _memoryManager?.GetOptimalBatchSize(batchSize, 1000) ?? batchSize;

                    if (batch.Count >= currentBatchSize)
                    {
                        yield return new List<string>(batch);
                        batch.Clear();
                        batchCount++;
                        
                        // 메모리 관리
                        if (_memoryManager != null)
                        {
                            // 주기적 메모리 체크
                            if (batchCount % 10 == 0 && _memoryManager.IsMemoryPressureHigh())
                            {
                                _logger.LogDebug("메모리 압박 감지 - 자동 정리 수행 중 (배치 {BatchCount}, 처리된 레코드: {ProcessedCount}개)", 
                                    batchCount, processedCount);
                                await _memoryManager.TryReduceMemoryPressureAsync();
                            }
                        }
                        else
                        {
                            // 기본 메모리 정리
                            if (processedCount % 50000 == 0)
                            {
                                GC.Collect();
                                _logger.LogDebug("배치 처리 진행: {ProcessedCount}개 처리됨", processedCount);
                            }
                        }
                    }
                }
                finally
                {
                    feature.Dispose();
                }
            }

            // 마지막 배치 반환
            if (batch.Count > 0)
            {
                yield return batch;
            }

            _logger.LogInformation("필드값 스트리밍 완료: {TableName}.{FieldName} = {ProcessedCount}개 처리됨 ({BatchCount}개 배치)",
                tableName, fieldName, processedCount, batchCount + 1);
        }

        /// <summary>
        /// DataSource를 안전하게 열기
        /// </summary>
        private async Task<DataSource?> OpenDataSourceAsync(string gdbPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var dataSource = Ogr.Open(gdbPath, 0); // 읽기 전용
                    if (dataSource == null)
                    {
                        _logger.LogError("FileGDB를 열 수 없습니다: {Path}", gdbPath);
                    }
                    return dataSource;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FileGDB 열기 실패: {Path}", gdbPath);
                    return null;
                }
            });
        }

        /// <summary>
        /// Feature에서 ObjectId 추출
        /// </summary>
        private long? GetObjectId(Feature feature)
        {
            try
            {
                // OBJECTID 필드 우선 시도
                var objectIdIndex = feature.GetFieldIndex("OBJECTID");
                if (objectIdIndex >= 0)
                {
                    return feature.GetFieldAsInteger64(objectIdIndex);
                }

                // FID 폴백
                var fid = feature.GetFID();
                return fid >= 0 ? fid : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ObjectId 추출 실패");
                return null;
            }
        }

        /// <summary>
        /// 재시도 로직이 포함된 실행
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "작업 실패 (시도 {Attempt}/{MaxAttempts})", attempt, _maxRetryAttempts);

                    if (attempt < _maxRetryAttempts)
                    {
                        await Task.Delay(_retryDelayMs * attempt);
                    }
                }
            }

            _logger.LogError(lastException, "모든 재시도 실패");
            throw lastException ?? new InvalidOperationException("알 수 없는 오류로 작업 실패");
        }

        /// <summary>
        /// 피처를 스트리밍 방식으로 조회합니다
        /// </summary>
        public async IAsyncEnumerable<Feature> GetFeaturesStreamAsync(
            string gdbPath,
            string tableName,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var dataSource = await OpenDataSourceAsync(gdbPath);
            if (dataSource == null)
            {
                _logger.LogWarning("데이터소스를 열 수 없습니다: {GdbPath}", gdbPath);
                yield break;
            }

            using var layer = dataSource.GetLayerByName(tableName);
            if (layer == null)
            {
                _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                yield break;
            }

            layer.ResetReading();
            var processedCount = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var feature = layer.GetNextFeature();
                if (feature == null)
                    break;

                yield return feature;
                processedCount++;

                // 메모리 관리
                if (_memoryManager != null && processedCount % 1000 == 0)
                {
                    if (_memoryManager.IsMemoryPressureHigh())
                    {
                        _logger.LogDebug("메모리 압박 감지 - 자동 정리 수행 중 (처리된 피처: {ProcessedCount}개)", processedCount);
                        await _memoryManager.TryReduceMemoryPressureAsync();
                    }
                }
            }

            _logger.LogDebug("피처 스트리밍 완료: {TableName} = {ProcessedCount}개 처리", tableName, processedCount);
        }

        /// <summary>
        /// 테이블 스키마 정보를 조회합니다
        /// </summary>
        public async Task<Dictionary<string, Type>> GetTableSchemaAsync(string gdbPath, string tableName)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var schema = new Dictionary<string, Type>();

                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    _logger.LogWarning("데이터소스를 열 수 없습니다: {GdbPath}", gdbPath);
                    return schema;
                }

                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                    return schema;
                }

                var layerDefn = layer.GetLayerDefn();
                for (int i = 0; i < layerDefn.GetFieldCount(); i++)
                {
                    var fieldDefn = layerDefn.GetFieldDefn(i);
                    var fieldName = fieldDefn.GetName();
                    var fieldType = ConvertOgrTypeToClrType(fieldDefn.GetFieldType());
                    
                    schema[fieldName] = fieldType;
                }

                _logger.LogDebug("테이블 스키마 조회 완료: {TableName} = {FieldCount}개 필드", tableName, schema.Count);
                return schema;
            });
        }

        /// <summary>
        /// 특정 ObjectId의 피처를 조회합니다
        /// </summary>
        public async Task<Feature?> GetFeatureByIdAsync(string gdbPath, string tableName, long objectId)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var dataSource = await OpenDataSourceAsync(gdbPath);
                if (dataSource == null)
                {
                    _logger.LogWarning("데이터소스를 열 수 없습니다: {GdbPath}", gdbPath);
                    return null;
                }

                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableName}", tableName);
                    return null;
                }

                // OBJECTID 필드로 검색 시도
                var layerDefn = layer.GetLayerDefn();
                var objectIdFieldIndex = layerDefn.GetFieldIndex("OBJECTID");
                
                if (objectIdFieldIndex >= 0)
                {
                    layer.SetAttributeFilter($"OBJECTID = {objectId}");
                    layer.ResetReading();
                    
                    var feature = layer.GetNextFeature();
                    layer.SetAttributeFilter(null); // 필터 해제
                    
                    if (feature != null)
                    {
                        _logger.LogDebug("피처 조회 성공: {TableName}, ObjectId={ObjectId}", tableName, objectId);
                        return feature;
                    }
                }

                // FID로 검색 시도
                var featureByFid = layer.GetFeature(objectId);
                if (featureByFid != null)
                {
                    _logger.LogDebug("피처 조회 성공 (FID): {TableName}, FID={ObjectId}", tableName, objectId);
                    return featureByFid;
                }

                _logger.LogWarning("피처를 찾을 수 없습니다: {TableName}, ObjectId={ObjectId}", tableName, objectId);
                return null;
            });
        }

        #region Private Helper Methods

        /// <summary>
        /// OGR 타입을 CLR 타입으로 변환합니다
        /// </summary>
        private Type ConvertOgrTypeToClrType(FieldType ogrType)
        {
            return ogrType switch
            {
                FieldType.OFTInteger => typeof(int),
                FieldType.OFTInteger64 => typeof(long),
                FieldType.OFTReal => typeof(double),
                FieldType.OFTString => typeof(string),
                FieldType.OFTDate => typeof(DateTime),
                FieldType.OFTDateTime => typeof(DateTime),
                FieldType.OFTTime => typeof(TimeSpan),
                FieldType.OFTBinary => typeof(byte[]),
                _ => typeof(string)
            };
        }





        #endregion
    }
}
