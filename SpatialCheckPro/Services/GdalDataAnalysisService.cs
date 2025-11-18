using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.GDAL;
using SpatialCheckPro.Services;
using System.Collections.Concurrent;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    public class GdalDataAnalysisService
    {
        private readonly ILogger<GdalDataAnalysisService> _logger;
        private readonly IDataSourcePool _dataSourcePool;
        private readonly IDataCacheService _cacheService;

        public GdalDataAnalysisService(ILogger<GdalDataAnalysisService> logger, IDataSourcePool dataSourcePool, IDataCacheService cacheService)
        {
            _logger = logger;
            _dataSourcePool = dataSourcePool;
            _cacheService = cacheService;
        }

        public async Task<bool> IsGdalAvailableAsync()
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 테이블 ID로 FeatureClass 정보를 찾습니다 (대소문자 무시)
        /// </summary>
        public async Task<FeatureClassInfo?> FindFeatureClassByTableIdAsync(string gdbPath, string tableId)
        {
            try
            {
                var dataSource = _dataSourcePool.GetDataSource(gdbPath);
                using var layer = FindLayerCaseInsensitive(dataSource, tableId);

                if (layer == null)
                {
                    _logger.LogWarning("{TableId}에 해당하는 레이어를 찾을 수 없습니다.", tableId);
                    return null;
                }
                
                var layerName = layer.GetName();
                var (geomType, featureCount) = await GetLayerInfoAsync(gdbPath, layerName);

                return new FeatureClassInfo
                {
                    Name = layerName,
                    Exists = true,
                    GeometryType = ConvertGeometryType(geomType),
                    FeatureCount = featureCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{TableId}에 대한 FeatureClass 정보 조회 실패", tableId);
                return null;
            }
        }

        private Layer? FindLayerCaseInsensitive(DataSource dataSource, string layerName)
        {
            for (int i = 0; i < dataSource.GetLayerCount(); i++)
            {
                var layer = dataSource.GetLayerByIndex(i);
                if (layer != null && layer.GetName().Equals(layerName, StringComparison.OrdinalIgnoreCase))
                {
                    return layer;
                }
            }
            return null;
        }

        private FeatureClassInfo? AnalyzeGdbDirectly(string gdbPath, string tableId)
        {
            // ... (메서드 내용)
            return null;
        }

        private int? GetFeatureCountEstimate(string gdbPath, string featureClassName)
        {
            // ... (메서드 내용)
            return null;
        }

        private string GetGeometryTypeEstimate(string featureClassName)
        {
            // ... (메서드 내용)
            return "";
        }

        private int GetMatchPriority(string layerName, string tableId)
        {
            // ... (메서드 내용)
            return 0;
        }

        private FeatureClassInfo CreateFeatureClassInfo(OSGeo.OGR.Layer layer, string layerName)
        {
            // ... (메서드 내용)
            return new FeatureClassInfo();
        }

        /// <summary>
        /// OGR wkbGeometryType을 문자열로 변환합니다
        /// </summary>
        private string ConvertGeometryType(wkbGeometryType geomType)
        {
            return geomType switch
            {
                wkbGeometryType.wkbPoint => "Point",
                wkbGeometryType.wkbLineString => "LineString",
                wkbGeometryType.wkbPolygon => "Polygon",
                wkbGeometryType.wkbMultiPoint => "MultiPoint",
                wkbGeometryType.wkbMultiLineString => "MultiLineString",
                wkbGeometryType.wkbMultiPolygon => "MultiPolygon",
                wkbGeometryType.wkbGeometryCollection => "GeometryCollection",
                wkbGeometryType.wkbPoint25D => "Point25D",
                wkbGeometryType.wkbLineString25D => "LineString25D",
                wkbGeometryType.wkbPolygon25D => "Polygon25D",
                wkbGeometryType.wkbMultiPoint25D => "MultiPoint25D",
                wkbGeometryType.wkbMultiLineString25D => "MultiLineString25D",
                wkbGeometryType.wkbMultiPolygon25D => "MultiPolygon25D",
                wkbGeometryType.wkbGeometryCollection25D => "GeometryCollection25D",
                wkbGeometryType.wkbUnknown => "Unknown",
                wkbGeometryType.wkbNone => "None",
                _ => geomType.ToString()
            };
        }
        
        public async Task<Dictionary<string, List<FieldDefn>>> GetDetailedSchemaAsync(string gdbPath)
        {
            // ... (메서드 내용)
            return new Dictionary<string, List<FieldDefn>>();
        }

        /// <summary>
        /// 지정된 FeatureClass의 상세 스키마 정보를 조회합니다
        /// </summary>
        public async Task<DetailedSchemaInfo> GetDetailedSchemaAsync(string gdbPath, string featureClassName)
        {
            var schemaInfo = new DetailedSchemaInfo
            {
                FeatureClassName = featureClassName,
                Fields = new List<DetailedFieldInfo>(),
                GeometryFields = new List<GeometryFieldInfo>(),
                Domains = new Dictionary<string, DomainInfo>()
            };

            DataSource? dataSource = null;
            try
            {
                dataSource = _dataSourcePool.GetDataSource(gdbPath);
                if (dataSource == null)
                {
                    _logger.LogError("GetDetailedSchemaAsync: DataSource를 열 수 없습니다: {GdbPath}", gdbPath);
                    return schemaInfo;
                }

                await Task.Run(() =>
                {
                    // 레이어 찾기 (대소문자 무시)
                    var layer = FindLayerCaseInsensitive(dataSource, featureClassName);
                    if (layer == null)
                    {
                        _logger.LogWarning("GetDetailedSchemaAsync: 레이어를 찾을 수 없습니다: {LayerName}", featureClassName);
                        return;
                    }

                    try
                    {
                        // 피처 수 조회
                        schemaInfo.FeatureCount = layer.GetFeatureCount(1);

                        // 레이어 정의 가져오기
                        var layerDefn = layer.GetLayerDefn();
                        if (layerDefn == null)
                        {
                            _logger.LogWarning("GetDetailedSchemaAsync: LayerDefn을 가져올 수 없습니다: {LayerName}", featureClassName);
                            return;
                        }

                        // FID 필드 (ObjectID) 추가 - GDAL은 FID를 일반 필드로 취급하지 않음
                        string fidColumnName = layer.GetFIDColumn();
                        if (!string.IsNullOrEmpty(fidColumnName))
                        {
                            var fidFieldInfo = new DetailedFieldInfo
                            {
                                Name = fidColumnName,
                                DataType = "INTEGER",
                                SubType = "None",
                                Length = 0,
                                Precision = 0,
                                IsNullable = false, // FID는 항상 NOT NULL
                                DefaultValue = null,
                                IsFidField = true
                            };
                            schemaInfo.Fields.Add(fidFieldInfo);
                            _logger.LogDebug("FID 필드 추가: {FidColumn} (타입: INTEGER, NOT NULL)", fidColumnName);
                        }
                        else
                        {
                            // FID 컬럼명이 없으면 기본값 사용
                            _logger.LogDebug("FID 컬럼명이 비어있음, 기본 'OBJECTID' 사용");
                            var fidFieldInfo = new DetailedFieldInfo
                            {
                                Name = "OBJECTID",
                                DataType = "INTEGER",
                                SubType = "None",
                                Length = 0,
                                Precision = 0,
                                IsNullable = false,
                                DefaultValue = null,
                                IsFidField = true
                            };
                            schemaInfo.Fields.Add(fidFieldInfo);
                            _logger.LogDebug("기본 FID 필드 추가: OBJECTID (타입: INTEGER, NOT NULL)");
                        }

                        // 일반 필드 정보 추출
                        int fieldCount = layerDefn.GetFieldCount();
                        _logger.LogDebug("GetDetailedSchemaAsync: {LayerName} - 일반 필드 {FieldCount}개 발견 (FID 제외)", featureClassName, fieldCount);

                        for (int i = 0; i < fieldCount; i++)
                        {
                            var fieldDefn = layerDefn.GetFieldDefn(i);
                            if (fieldDefn == null) continue;

                            var fieldInfo = new DetailedFieldInfo
                            {
                                Name = fieldDefn.GetName(),
                                DataType = ConvertOgrTypeToString(fieldDefn.GetFieldType()),
                                SubType = fieldDefn.GetSubType().ToString(),
                                Length = fieldDefn.GetWidth(),
                                Precision = fieldDefn.GetPrecision(),
                                IsNullable = fieldDefn.IsNullable() == 1,
                                DefaultValue = fieldDefn.GetDefault(),
                                IsFidField = false // FID는 별도 처리
                            };

                            schemaInfo.Fields.Add(fieldInfo);
                            _logger.LogTrace("필드 추가: {FieldName} ({DataType}, 길이: {Length}, Nullable: {IsNullable})", 
                                fieldInfo.Name, fieldInfo.DataType, fieldInfo.Length, fieldInfo.IsNullable);
                        }

                        // 지오메트리 필드 정보 추출
                        int geomFieldCount = layerDefn.GetGeomFieldCount();
                        for (int i = 0; i < geomFieldCount; i++)
                        {
                            var geomFieldDefn = layerDefn.GetGeomFieldDefn(i);
                            if (geomFieldDefn == null) continue;

                            // SpatialReference WKT 변환
                            string wkt = string.Empty;
                            var spatialRef = geomFieldDefn.GetSpatialRef();
                            if (spatialRef != null)
                            {
                                spatialRef.ExportToWkt(out wkt, null);
                            }

                            var geomFieldInfo = new GeometryFieldInfo
                            {
                                Name = geomFieldDefn.GetName(),
                                GeometryType = ConvertGeometryType(geomFieldDefn.GetFieldType()),
                                SpatialReference = wkt
                            };

                            schemaInfo.GeometryFields.Add(geomFieldInfo);
                            _logger.LogDebug("지오메트리 필드 추가: {GeomFieldName} ({GeomType})", 
                                geomFieldInfo.Name, geomFieldInfo.GeometryType);
                        }

                        _logger.LogInformation("GetDetailedSchemaAsync 완료: {LayerName} - 전체필드 {FieldCount}개 (FID 포함), 지오메트리필드 {GeomFieldCount}개", 
                            featureClassName, schemaInfo.Fields.Count, schemaInfo.GeometryFields.Count);
                    }
                    finally
                    {
                        layer.Dispose();
                    }
                });

                return schemaInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDetailedSchemaAsync: 상세 스키마 조회 중 오류 발생: {LayerName}", featureClassName);
                return schemaInfo;
            }
            finally
            {
                if (dataSource != null)
                {
                    _dataSourcePool.ReturnDataSource(gdbPath, dataSource);
                }
            }
        }

        private bool CheckGdalAvailable()
        {
            return true;
        }

        private async Task<bool> CheckForActualNullValuesAsync(Layer layer, int fieldIndex)
        {
            return await Task.FromResult(false);
        }

        public async Task<List<FieldInfo>> GetFeatureClassSchemaAsync(string gdbPath, string featureClassName)
        {
            return new List<FieldInfo>();
        }

        public async Task<ActualFieldInfo?> GetActualFieldInfoAsync(string gdbPath, string featureClassName, string fieldName)
        {
            return null;
        }

        public async Task<int> CheckDuplicateValuesAsync(string gdbPath, string featureClassName, string fieldName)
        {
            return 0;
        }

        private int CheckFidDuplicates(OSGeo.OGR.Layer layer)
        {
            return 0;
        }

        /// <summary>
        /// OGR FieldType을 문자열 타입으로 변환합니다
        /// </summary>
        private string ConvertOgrTypeToString(FieldType ogrType)
        {
            return ogrType switch
            {
                FieldType.OFTInteger => "INTEGER",
                FieldType.OFTInteger64 => "INTEGER",
                FieldType.OFTReal => "REAL",
                FieldType.OFTString => "TEXT",
                FieldType.OFTDate => "DATE",
                FieldType.OFTDateTime => "DATE",
                FieldType.OFTTime => "DATE",
                FieldType.OFTBinary => "BINARY",
                FieldType.OFTIntegerList => "INTEGER_LIST",
                FieldType.OFTInteger64List => "INTEGER_LIST",
                FieldType.OFTRealList => "REAL_LIST",
                FieldType.OFTStringList => "STRING_LIST",
                _ => ogrType.ToString()
            };
        }

        public async Task<List<Feature>> GetAllFeaturesAsync(string gdbPath, string layerName)
        {
            var features = new List<Feature>();
            DataSource? dataSource = null;
            try
            {
                dataSource = _dataSourcePool.GetDataSource(gdbPath);
                if (dataSource == null)
                {
                    _logger.LogError("{Path}에 대한 데이터 소스를 가져올 수 없습니다.", gdbPath);
                    return features;
                }

                await Task.Run(() =>
                {
                    var layer = FindLayerCaseInsensitive(dataSource, layerName);
                    if (layer == null)
                    {
                        _logger.LogWarning("레이어를 찾을 수 없습니다: {Layer}", layerName);
                        return;
                    }

                    try
                    {
                        layer.ResetReading();
                        Feature? f;
                        while ((f = layer.GetNextFeature()) != null)
                        {
                            // 호출자가 해제를 책임지므로 리스트에 추가만 수행
                            features.Add(f);
                        }
                    }
                    finally
                    {
                        layer.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "피처 나열 중 오류: {Layer}", layerName);
            }
            finally
            {
                if (dataSource != null)
                {
                    _dataSourcePool.ReturnDataSource(gdbPath, dataSource);
                }
            }

            return features;
        }

        public async Task<List<string>> GetLayerNamesAsync(string gdbPath)
        {
            var layerNames = new List<string>();
            var dataSource = _dataSourcePool.GetDataSource(gdbPath);
            if (dataSource == null)
            {
                _logger.LogError("GetLayerNamesAsync: DataSource를 열 수 없습니다: {GdbPath}", gdbPath);
                return layerNames;
            }

            try
            {
                await Task.Run(() =>
                {
                    var layerCount = dataSource.GetLayerCount();
                    _logger.LogDebug("GetLayerNamesAsync: 총 {LayerCount}개 레이어 발견", layerCount);
                    
                    for (var i = 0; i < layerCount; i++)
                    {
                        var layer = dataSource.GetLayerByIndex(i);
                        if (layer != null)
                        {
                            var name = layer.GetName();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                layerNames.Add(name);
                                _logger.LogDebug("GetLayerNamesAsync: 레이어 [{Index}] = {LayerName}", i, name);
                            }
                            // layer.Dispose() 제거: DataSource가 관리
                        }
                    }
                    _logger.LogInformation("GetLayerNamesAsync: 레이어 이름 목록 조회 완료: {Count}개 레이어", layerNames.Count);
                });
            }
            finally
            {
                _dataSourcePool.ReturnDataSource(gdbPath, dataSource);
            }
            return layerNames;
        }

        /// <summary>
        /// 레이어의 피처 수와 지오메트리 타입을 반환합니다
        /// 최적화: Task.Run 제거하여 스레드 풀 오버헤드 제거
        /// </summary>
        public async Task<(wkbGeometryType GeometryType, long FeatureCount)> GetLayerInfoAsync(string gdbPath, string layerName)
        {
            DataSource? dataSource = null;
            try
            {
                dataSource = _dataSourcePool.GetDataSource(gdbPath);
                if (dataSource == null)
                {
                    _logger.LogError("{Path}에 대한 데이터 소스를 가져올 수 없습니다.", gdbPath);
                    return (wkbGeometryType.wkbUnknown, 0);
                }

                // Task.Run 제거: 동기 작업을 비동기로 래핑하는 오버헤드 제거
                // 동기 작업을 직접 수행하고 Task.FromResult로 래핑
                var layer = FindLayerCaseInsensitive(dataSource, layerName);
                if (layer == null)
                {
                    _logger.LogWarning("레이어를 찾을 수 없습니다: {Layer}", layerName);
                    return await Task.FromResult((wkbGeometryType.wkbUnknown, 0L));
                }

                try
                {
                    // 피처 수: OGR 내부 인덱스 기반으로 빠르게 조회
                    long count = layer.GetFeatureCount(1);

                    // 지오메트리 타입은 정의(레이어 정의)에서 확보, 불명확하면 첫 피처 확인
                    var defn = layer.GetLayerDefn();
                    var gtype = defn != null ? defn.GetGeomType() : wkbGeometryType.wkbUnknown;
                    if (gtype == wkbGeometryType.wkbUnknown && count > 0)
                    {
                        layer.ResetReading();
                        using var f = layer.GetNextFeature();
                        if (f != null)
                        {
                            var g = f.GetGeometryRef();
                            if (g != null)
                            {
                                gtype = g.GetGeometryType();
                            }
                        }
                    }
                    return await Task.FromResult((gtype, count));
                }
                finally
                {
                    layer.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레이어 정보 조회 실패: {Layer}", layerName);
                return (wkbGeometryType.wkbUnknown, 0);
            }
            finally
            {
                if (dataSource != null)
                {
                    _dataSourcePool.ReturnDataSource(gdbPath, dataSource);
                }
            }
        }
    }
}
