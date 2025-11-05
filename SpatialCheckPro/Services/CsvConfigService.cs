using System.Globalization;
using System.Text;
using CsvHelper;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// CSV 설정 파일 관리 서비스 구현체
    /// </summary>
    public class CsvConfigService : ICsvConfigService
    {
        private readonly ILogger<CsvConfigService> _logger;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        public CsvConfigService(ILogger<CsvConfigService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 전체 검수 설정 로드
        /// </summary>
        public async Task<ValidationConfig> LoadValidationConfigAsync(string configDirectory)
        {
            try
            {
                _logger.LogInformation("검수 설정을 로드합니다. 디렉토리: {Directory}", configDirectory);

                var config = new ValidationConfig();
                var filePaths = new ConfigFilePaths
                {
                    TableCheckFile = System.IO.Path.Combine(configDirectory, "1_table_check.csv"),
                    SchemaCheckFile = System.IO.Path.Combine(configDirectory, "2_schema_check.csv"),
                    GeometryCheckFile = System.IO.Path.Combine(configDirectory, "3_geometry_check.csv"),
                    RelationCheckFile = System.IO.Path.Combine(configDirectory, "5_relation_check.csv")
                };

                config.FilePaths = filePaths;

                // 각 설정 파일 로드
                if (System.IO.File.Exists(filePaths.TableCheckFile))
                {
                    config.TableChecks = await LoadTableCheckConfigAsync(filePaths.TableCheckFile);
                    _logger.LogInformation("테이블 검수 설정 로드 완료: {Count}개", config.TableChecks.Count);
                }

                if (System.IO.File.Exists(filePaths.SchemaCheckFile))
                {
                    config.SchemaChecks = await LoadSchemaCheckConfigAsync(filePaths.SchemaCheckFile);
                    _logger.LogInformation("스키마 검수 설정 로드 완료: {Count}개", config.SchemaChecks.Count);
                }

                if (System.IO.File.Exists(filePaths.GeometryCheckFile))
                {
                    config.GeometryChecks = await LoadGeometryCheckConfigAsync(filePaths.GeometryCheckFile);
                    _logger.LogInformation("지오메트리 검수 설정 로드 완료: {Count}개", config.GeometryChecks.Count);
                }

                if (System.IO.File.Exists(filePaths.RelationCheckFile))
                {
                    config.RelationChecks = await LoadRelationCheckConfigAsync(filePaths.RelationCheckFile);
                    _logger.LogInformation("관계 검수 설정 로드 완료: {Count}개", config.RelationChecks.Count);
                }

                config.LoadedAt = DateTime.Now;

                _logger.LogInformation("전체 검수 설정 로드가 완료되었습니다.");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 설정 로드 중 오류가 발생했습니다. 디렉토리: {Directory}", configDirectory);
                throw;
            }
        }

        /// <summary>
        /// 테이블 검수 설정 로드 (파일 잠금 문제 해결)
        /// </summary>
        public async Task<List<TableCheckConfig>> LoadTableCheckConfigAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("테이블 검수 설정을 로드합니다. 파일: {FilePath}", filePath);

                // 파일 잠금 문제 해결을 위한 안전한 파일 읽기
                string content;
                using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var streamReader = new System.IO.StreamReader(fileStream, Encoding.UTF8))
                {
                    content = await streamReader.ReadToEndAsync();
                }
                using var reader = new System.IO.StringReader(content);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                var records = csv.GetRecords<TableCheckConfig>().ToList();

                _logger.LogInformation("테이블 검수 설정 로드 완료: {Count}개", records.Count);
                return records;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 검수 설정 로드 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 스키마 검수 설정 로드 (파일 잠금 문제 해결)
        /// </summary>
        public async Task<List<SchemaCheckConfig>> LoadSchemaCheckConfigAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("스키마 검수 설정을 로드합니다. 파일: {FilePath}", filePath);

                // 파일 잠금 문제 해결을 위한 안전한 파일 읽기
                string content;
                using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var streamReader = new System.IO.StreamReader(fileStream, Encoding.UTF8))
                {
                    content = await streamReader.ReadToEndAsync();
                }
                using var reader = new System.IO.StringReader(content);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                var records = csv.GetRecords<SchemaCheckConfig>().ToList();

                _logger.LogInformation("스키마 검수 설정 로드 완료: {Count}개", records.Count);
                return records;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 검수 설정 로드 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 지오메트리 검수 설정 로드
        /// </summary>
        public async Task<List<GeometryCheckConfig>> LoadGeometryCheckConfigAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("지오메트리 검수 설정을 로드합니다. 파일: {FilePath}", filePath);

                using var reader = new System.IO.StringReader(await System.IO.File.ReadAllTextAsync(filePath, Encoding.UTF8));
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                
                var records = csv.GetRecords<GeometryCheckConfig>().ToList();

                _logger.LogInformation("지오메트리 검수 설정 로드 완료: {Count}개", records.Count);
                return records;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 설정 로드 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 관계 검수 설정 로드
        /// </summary>
        public async Task<List<RelationCheckConfig>> LoadRelationCheckConfigAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("관계 검수 설정을 로드합니다. 파일: {FilePath}", filePath);

                using var reader = new System.IO.StringReader(await System.IO.File.ReadAllTextAsync(filePath, Encoding.UTF8));
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                // 헤더 유연성: 일부 헤더 누락 허용 (구버전/신버전 혼용 대응)
                csv.Context.Configuration.HeaderValidated = null;
                csv.Context.Configuration.MissingFieldFound = null;

                var records = csv.GetRecords<RelationCheckConfig>().ToList();

                _logger.LogInformation("관계 검수 설정 로드 완료: {Count}개", records.Count);
                return records;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "관계 검수 설정 로드 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 테이블 검수 설정 저장
        /// </summary>
        public async Task<bool> SaveTableCheckConfigAsync(string filePath, List<TableCheckConfig> configs)
        {
            try
            {
                _logger.LogInformation("테이블 검수 설정을 저장합니다. 파일: {FilePath}, 개수: {Count}", filePath, configs.Count);

                using var writer = new System.IO.StringWriter();
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                await csv.WriteRecordsAsync(configs);
                await System.IO.File.WriteAllTextAsync(filePath, writer.ToString(), Encoding.UTF8);

                _logger.LogInformation("테이블 검수 설정 저장이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 검수 설정 저장 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// 스키마 검수 설정 저장
        /// </summary>
        public async Task<bool> SaveSchemaCheckConfigAsync(string filePath, List<SchemaCheckConfig> configs)
        {
            try
            {
                _logger.LogInformation("스키마 검수 설정을 저장합니다. 파일: {FilePath}, 개수: {Count}", filePath, configs.Count);

                using var writer = new System.IO.StringWriter();
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                await csv.WriteRecordsAsync(configs);
                await System.IO.File.WriteAllTextAsync(filePath, writer.ToString(), Encoding.UTF8);

                _logger.LogInformation("스키마 검수 설정 저장이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 검수 설정 저장 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// 지오메트리 검수 설정 저장
        /// </summary>
        public async Task<bool> SaveGeometryCheckConfigAsync(string filePath, List<GeometryCheckConfig> configs)
        {
            try
            {
                _logger.LogInformation("지오메트리 검수 설정을 저장합니다. 파일: {FilePath}, 개수: {Count}", filePath, configs.Count);

                using var writer = new System.IO.StringWriter();
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                await csv.WriteRecordsAsync(configs);
                await System.IO.File.WriteAllTextAsync(filePath, writer.ToString(), Encoding.UTF8);

                _logger.LogInformation("지오메트리 검수 설정 저장이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 설정 저장 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// 관계 검수 설정 저장
        /// </summary>
        public async Task<bool> SaveRelationCheckConfigAsync(string filePath, List<RelationCheckConfig> configs)
        {
            try
            {
                _logger.LogInformation("관계 검수 설정을 저장합니다. 파일: {FilePath}, 개수: {Count}", filePath, configs.Count);

                using var writer = new System.IO.StringWriter();
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                await csv.WriteRecordsAsync(configs);
                await System.IO.File.WriteAllTextAsync(filePath, writer.ToString(), Encoding.UTF8);

                _logger.LogInformation("관계 검수 설정 저장이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "관계 검수 설정 저장 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// CSV 파일 유효성 검증
        /// </summary>
        public async Task<CsvValidationResult> ValidateCsvFileAsync(string filePath, ConfigType configType)
        {
            var result = new CsvValidationResult
            {
                FilePath = filePath,
                ConfigType = configType
            };

            try
            {
                _logger.LogInformation("CSV 파일 유효성을 검증합니다. 파일: {FilePath}, 타입: {ConfigType}", filePath, configType);

                if (!System.IO.File.Exists(filePath))
                {
                    result.Errors.Add($"파일이 존재하지 않습니다: {filePath}");
                    return result;
                }

                // 파일 크기 검사
                var fileInfo = new System.IO.FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    result.Errors.Add("파일이 비어있습니다.");
                    return result;
                }

                // 설정 타입별 검증
                switch (configType)
                {
                    case ConfigType.TableCheck:
                        await ValidateTableCheckCsv(filePath, result);
                        break;
                    case ConfigType.SchemaCheck:
                        await ValidateSchemaCheckCsv(filePath, result);
                        break;
                    case ConfigType.GeometryCheck:
                        await ValidateGeometryCheckCsv(filePath, result);
                        break;
                    case ConfigType.RelationCheck:
                        await ValidateRelationCheckCsv(filePath, result);
                        break;
                }

                result.IsValid = result.Errors.Count == 0;

                _logger.LogInformation("CSV 파일 검증 완료. 파일: {FilePath}, 유효: {IsValid}, 오류: {ErrorCount}, 경고: {WarningCount}",
                    filePath, result.IsValid, result.Errors.Count, result.Warnings.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV 파일 검증 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                result.Errors.Add($"검증 중 오류 발생: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 기본 설정 파일 생성
        /// </summary>
        public async Task<bool> CreateDefaultConfigFilesAsync(string configDirectory)
        {
            try
            {
                _logger.LogInformation("기본 설정 파일을 생성합니다. 디렉토리: {Directory}", configDirectory);

                // 디렉토리 생성
                if (!System.IO.Directory.Exists(configDirectory))
                {
                    System.IO.Directory.CreateDirectory(configDirectory);
                }

                // 기본 설정 파일들 생성
                await CreateDefaultTableCheckFile(System.IO.Path.Combine(configDirectory, "table_check.csv"));
                await CreateDefaultSchemaCheckFile(System.IO.Path.Combine(configDirectory, "schema_check.csv"));
                await CreateDefaultGeometryCheckFile(System.IO.Path.Combine(configDirectory, "geometry_check.csv"));
                await CreateDefaultRelationCheckFile(System.IO.Path.Combine(configDirectory, "relation_check.csv"));

                _logger.LogInformation("기본 설정 파일 생성이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본 설정 파일 생성 중 오류가 발생했습니다. 디렉토리: {Directory}", configDirectory);
                return false;
            }
        }

        /// <summary>
        /// 테이블 검수 CSV 검증
        /// </summary>
        private async Task ValidateTableCheckCsv(string filePath, CsvValidationResult result)
        {
            try
            {
                var configs = await LoadTableCheckConfigAsync(filePath);
                result.RecordCount = configs.Count;

                foreach (var config in configs)
                {
                    var validationErrors = config.Validate();
                    foreach (var error in validationErrors)
                    {
                        result.Errors.Add($"테이블 '{config.TableName}': {error.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"테이블 검수 설정 파싱 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 스키마 검수 CSV 검증
        /// </summary>
        private async Task ValidateSchemaCheckCsv(string filePath, CsvValidationResult result)
        {
            try
            {
                var configs = await LoadSchemaCheckConfigAsync(filePath);
                result.RecordCount = configs.Count;

                foreach (var config in configs)
                {
                    var validationErrors = config.Validate();
                    foreach (var error in validationErrors)
                    {
                        result.Errors.Add($"스키마 '{config.TableId}.{config.ColumnName}': {error.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"스키마 검수 설정 파싱 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 지오메트리 검수 CSV 검증
        /// </summary>
        private async Task ValidateGeometryCheckCsv(string filePath, CsvValidationResult result)
        {
            try
            {
                var configs = await LoadGeometryCheckConfigAsync(filePath);
                result.RecordCount = configs.Count;

                foreach (var config in configs)
                {
                    var isValid = config.Validate();
                    if (!isValid)
                    {
                        result.Errors.Add($"지오메트리 '{config.TableName}': 필수 필드가 누락되었습니다.");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"지오메트리 검수 설정 파싱 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 관계 검수 CSV 검증
        /// </summary>
        private async Task ValidateRelationCheckCsv(string filePath, CsvValidationResult result)
        {
            try
            {
                var configs = await LoadRelationCheckConfigAsync(filePath);
                result.RecordCount = configs.Count;

                foreach (var config in configs)
                {
                    var validationErrors = config.Validate();
                    foreach (var error in validationErrors)
                    {
                        result.Errors.Add($"관계 검수 '{config.MainTableId}': {error.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"관계 검수 설정 파싱 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 기본 테이블 검수 파일 생성
        /// </summary>
        private async Task CreateDefaultTableCheckFile(string filePath)
        {
            var defaultConfigs = new List<TableCheckConfig>
            {
                new TableCheckConfig
                {
                    TableId = "TBL001",
                    TableName = "건물",
                    GeometryType = "POLYGON",
                    CoordinateSystem = "EPSG:5179"
                }
            };

            await SaveTableCheckConfigAsync(filePath, defaultConfigs);
        }

        /// <summary>
        /// 기본 스키마 검수 파일 생성
        /// </summary>
        private async Task CreateDefaultSchemaCheckFile(string filePath)
        {
            var defaultConfigs = new List<SchemaCheckConfig>
            {
                new SchemaCheckConfig
                {
                    TableId = "tn_buld",
                    ColumnName = "objectid",
                    ColumnKoreanName = "시스템고유아이디",
                    DataType = "INTEGER",
                    Length = "",
                    IsNotNull = "Y",
                    ReferenceTable = "",
                    ReferenceColumn = ""
                }
            };

            await SaveSchemaCheckConfigAsync(filePath, defaultConfigs);
        }

        /// <summary>
        /// 기본 지오메트리 검수 파일 생성
        /// </summary>
        private async Task CreateDefaultGeometryCheckFile(string filePath)
        {
            var defaultConfigs = new List<GeometryCheckConfig>
            {
                new GeometryCheckConfig
                {
                    TableId = "TBL001",
                    TableName = "샘플포인트",
                    CheckDuplicate = "Y",
                    CheckOverlap = "N",
                    CheckSelfIntersection = "N",
                    CheckSliver = "N"
                }
            };

            await SaveGeometryCheckConfigAsync(filePath, defaultConfigs);
        }

        /// <summary>
        /// 기본 관계 검수 파일 생성
        /// </summary>
        private async Task CreateDefaultRelationCheckFile(string filePath)
        {
            var defaultConfigs = new List<RelationCheckConfig>
            {
                new RelationCheckConfig
                {
                    MainTableId = "TBL001",
                    MainTableName = "샘플포인트",
                    RelatedTableId = "TBL002",
                    RelatedTableName = "샘플폴리곤",
                    CheckLineInPolygon = "N",
                    CheckPointInPolygon = "Y",
                    CheckPolygonInPolygon = "N"
                }
            };

            await SaveRelationCheckConfigAsync(filePath, defaultConfigs);
        }
    }
}
