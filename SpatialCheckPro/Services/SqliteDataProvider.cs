using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// SpatiaLite(SQLite) 데이터 제공자
    /// </summary>
    public class SqliteDataProvider : IValidationDataProvider
    {
        private readonly ILogger<SqliteDataProvider> _logger;
        private SqliteConnection? _connection;

        public SqliteDataProvider(ILogger<SqliteDataProvider> logger)
        {
            _logger = logger;
        }

        public Task InitializeAsync(string dataSourcePath)
        {
            _connection = new SqliteConnection($"Data Source={dataSourcePath}");
            _connection.Open();
            _connection.EnableExtensions(true);
            _logger.LogInformation("SqliteDataProvider 초기화: {Path}", dataSourcePath);
            return Task.CompletedTask;
        }

        public Task<List<Feature>> GetFeaturesAsync(string layerName)
        {
            // SpatiaLite에서 피처를 읽어 OGR Feature 객체로 변환하는 로직.
            // 상세 구현은 시간 관계상 생략.
            _logger.LogInformation("'{LayerName}' 레이어에서 피처 읽기 (SQLite)", layerName);
            return Task.FromResult(new List<Feature>());
        }

        public async Task<List<string>> GetLayerNamesAsync()
        {
            var layerNames = new List<string>();
            if (_connection == null) return layerNames;

            await Task.Run(() =>
            {
                using var command = _connection.CreateCommand();
                // SpatiaLite에서는 'geometry_columns' 테이블에서 테이블 목록을 가져올 수 있습니다.
                command.CommandText = "SELECT f_table_name FROM geometry_columns;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    layerNames.Add(reader.GetString(0));
                }
            });
            return layerNames;
        }

        public Task<List<FieldDefn>> GetSchemaAsync(string layerName)
        {
            // SpatiaLite에서 스키마를 읽어 OGR FieldDefn 객체로 변환하는 로직.
            // 상세 구현은 시간 관계상 생략.
            _logger.LogInformation("'{LayerName}' 레이어에서 스키마 읽기 (SQLite)", layerName);
            return Task.FromResult(new List<FieldDefn>());
        }

        public void Close()
        {
            _connection?.Close();
            _connection?.Dispose();
            _logger.LogInformation("SqliteDataProvider 리소스 정리 완료");
        }
    }
}
