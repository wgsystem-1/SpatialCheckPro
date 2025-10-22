using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// SpatiaLite(SQLite) 데이터 제공자
    /// OGR(Open) 기반으로 SpatiaLite를 직접 열어 호환성을 높이며
    /// 상위 프로세서들이 OGR Feature/Schema를 동일 패턴으로 사용할 수 있도록 합니다.
    /// </summary>
    public class SqliteDataProvider : IValidationDataProvider
    {
        private readonly ILogger<SqliteDataProvider> _logger;
        private string? _sqlitePath;
        private DataSource? _ogrDataSource;

        public SqliteDataProvider(ILogger<SqliteDataProvider> logger)
        {
            _logger = logger;
        }

        public Task InitializeAsync(string dataSourcePath)
        {
            // OGR로 SpatiaLite를 직접 연다. (읽기 전용)
            _sqlitePath = dataSourcePath;
            _ogrDataSource = Ogr.Open(dataSourcePath, 0);
            if (_ogrDataSource == null)
            {
                throw new InvalidOperationException($"SpatiaLite DB를 열 수 없습니다: {dataSourcePath}");
            }
            _logger.LogInformation("SqliteDataProvider(OGR) 초기화: {Path}", dataSourcePath);
            return Task.CompletedTask;
        }

        public Task<List<Feature>> GetFeaturesAsync(string layerName)
        {
            var features = new List<Feature>();
            if (_ogrDataSource == null) return Task.FromResult(features);

            try
            {
                using var layer = _ogrDataSource.GetLayerByName(layerName);
                if (layer == null) return Task.FromResult(features);

                layer.ResetReading();
                Feature? f;
                while ((f = layer.GetNextFeature()) != null)
                {
                    // Feature는 호출자에서 using 처리할 수 있도록 그대로 반환 리스트에 담는다.
                    features.Add(f);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQLite 레이어 피처 읽기 실패: {Layer}", layerName);
            }

            return Task.FromResult(features);
        }

        public async Task<List<string>> GetLayerNamesAsync()
        {
            var layerNames = new List<string>();
            if (_ogrDataSource == null) return layerNames;

            await Task.Run(() =>
            {
                for (int i = 0; i < _ogrDataSource.GetLayerCount(); i++)
                {
                    using var layer = _ogrDataSource.GetLayerByIndex(i);
                    if (layer != null)
                    {
                        layerNames.Add(layer.GetName());
                    }
                }
            });
            return layerNames;
        }

        public Task<List<FieldDefn>> GetSchemaAsync(string layerName)
        {
            var schema = new List<FieldDefn>();
            if (_ogrDataSource == null) return Task.FromResult(schema);

            try
            {
                using var layer = _ogrDataSource.GetLayerByName(layerName);
                if (layer == null) return Task.FromResult(schema);
                using var defn = layer.GetLayerDefn();
                for (int i = 0; i < defn.GetFieldCount(); i++)
                {
                    // FieldDefn은 네이티브 리소스이므로 복사본을 만들어 반환하는 것이 안전하나,
                    // 여기서는 호출 측에서 읽기 용도로만 사용할 것을 전제로 원본을 반환한다.
                    using var fd = defn.GetFieldDefn(i);
                    schema.Add(fd);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQLite 레이어 스키마 읽기 실패: {Layer}", layerName);
            }

            return Task.FromResult(schema);
        }

        public void Close()
        {
            try
            {
                _ogrDataSource?.Dispose();
            }
            catch { }
            finally
            {
                _ogrDataSource = null;
            }
            _logger.LogInformation("SqliteDataProvider(OGR) 리소스 정리 완료");
        }
    }
}
