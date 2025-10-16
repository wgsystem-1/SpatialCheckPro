using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Services; // 네임스페이스 확인
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// FileGDB 데이터 제공자
    /// </summary>
    public class GdbDataProvider : IValidationDataProvider
    {
        private readonly ILogger<GdbDataProvider> _logger;
        private readonly GdalDataAnalysisService _gdalService;
        private readonly IDataSourcePool _dataSourcePool;
        private string _gdbPath = string.Empty;

        public GdbDataProvider(ILogger<GdbDataProvider> logger, GdalDataAnalysisService gdalService, IDataSourcePool dataSourcePool)
        {
            _logger = logger;
            _gdalService = gdalService;
            _dataSourcePool = dataSourcePool;
        }

        public Task InitializeAsync(string dataSourcePath)
        {
            _gdbPath = dataSourcePath;
            _logger.LogInformation("GdbDataProvider 초기화: {Path}", _gdbPath);
            return Task.CompletedTask;
        }

        public async Task<List<Feature>> GetFeaturesAsync(string layerName)
        {
            return await _gdalService.GetAllFeaturesAsync(_gdbPath, layerName);
        }

        public async Task<List<string>> GetLayerNamesAsync()
        {
            return await _gdalService.GetLayerNamesAsync(_gdbPath);
        }

        public async Task<List<FieldDefn>> GetSchemaAsync(string layerName)
        {
            // 빠른 경로: 레이어 정의에서 필드 정의를 직접 수집
            var result = new List<FieldDefn>();
            var ds = _dataSourcePool.GetDataSource(_gdbPath);
            if (ds == null) return result;
            try
            {
                var layer = ds.GetLayerByName(layerName) ?? FindLayerCaseInsensitive(ds, layerName);
                if (layer == null) return result;
                try
                {
                    var defn = layer.GetLayerDefn();
                    if (defn != null)
                    {
                        for (int i = 0; i < defn.GetFieldCount(); i++)
                        {
                            var fd = defn.GetFieldDefn(i);
                            if (fd != null) result.Add(fd);
                        }
                    }
                }
                finally { layer.Dispose(); }
            }
            finally
            {
                _dataSourcePool.ReturnDataSource(_gdbPath, ds);
            }
            return result;
        }

        private static Layer? FindLayerCaseInsensitive(DataSource dataSource, string layerName)
        {
            for (int i = 0; i < dataSource.GetLayerCount(); i++)
            {
                var lyr = dataSource.GetLayerByIndex(i);
                if (lyr == null) continue;
                try
                {
                    var name = lyr.GetName() ?? string.Empty;
                    if (string.Equals(name, layerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return lyr; // Dispose는 호출부에서
                    }
                }
                catch
                {
                    lyr.Dispose();
                }
            }
            return null;
        }

        public void Close()
        {
            // DataSourcePool에서 관리하므로 별도의 Close 작업 불필요
            _logger.LogInformation("GdbDataProvider 리소스 정리 (DataSourcePool에서 관리)");
        }
    }
}
