using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.OSR;
using System.ComponentModel;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 1단계 테이블 검수를 수행하는 프로세서 구현체
    /// </summary>
    public class TableCheckProcessor : ITableCheckProcessor
    {
        private readonly ILogger<TableCheckProcessor> _logger;

        public TableCheckProcessor(ILogger<TableCheckProcessor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 테이블 리스트 검증을 수행합니다
        /// </summary>
        public Task<CheckResult> ValidateTableListAsync(SpatialFileInfo spatialFile, IEnumerable<TableCheckConfig> config)
        {
            var result = new CheckResult
            {
                CheckId = "TABLE_LIST_CHECK",
                CheckName = "테이블 리스트 검증",
                Status = CheckStatus.Running
            };

            try
            {
                _logger.LogInformation("테이블 리스트 검증 시작: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                result.TotalCount = configList.Count;

                // 설정에 정의된 테이블들이 실제 파일에 존재하는지 확인
                foreach (var tableConfig in configList)
                {
                    var tableExists = spatialFile.Tables.Any(t => 
                        string.Equals(t.TableName, tableConfig.TableName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.TableId, tableConfig.TableId, StringComparison.OrdinalIgnoreCase));

                    if (!tableExists)
                    {
                        var error = new ValidationError
                        {
                            ErrorId = Guid.NewGuid().ToString(),
                            TableName = tableConfig.TableName,
                            Message = $"설정에 정의된 테이블 '{tableConfig.TableName}' (ID: {tableConfig.TableId})이 파일에 존재하지 않습니다.",
                            Severity = ErrorSeverity.Error,
                            Metadata = new Dictionary<string, object>
                            {
                                ["ExpectedTableId"] = tableConfig.TableId,
                                ["ExpectedTableName"] = tableConfig.TableName
                            }
                        };
                        result.Errors.Add(error);
                        result.ErrorCount++;
                    }
                }

                // 파일에 존재하지만 설정에 정의되지 않은 테이블 확인 (경고)
                // ORG_ 접두사 테이블은 ArcGIS Pro 백업 파일이므로 예외 처리
                foreach (var actualTable in spatialFile.Tables)
                {
                    // ORG_ 접두사 테이블은 검수 대상에서 제외
                    if (actualTable.TableName.StartsWith("ORG_", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("ORG_ 접두사 테이블은 검수 대상에서 제외: {TableName}", actualTable.TableName);
                        continue;
                    }

                    var isConfigured = configList.Any(c => 
                        string.Equals(c.TableName, actualTable.TableName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.TableId, actualTable.TableId, StringComparison.OrdinalIgnoreCase));

                    if (!isConfigured)
                    {
                        var warning = new ValidationError
                        {
                            ErrorId = Guid.NewGuid().ToString(),
                            TableName = actualTable.TableName,
                            Message = $"파일에 존재하는 테이블 '{actualTable.TableName}'이 검수 설정에 정의되지 않았습니다.",
                            Severity = ErrorSeverity.Warning,
                            Metadata = new Dictionary<string, object>
                            {
                                ["ActualTableId"] = actualTable.TableId,
                                ["ActualTableName"] = actualTable.TableName
                            }
                        };
                        result.Warnings.Add(warning);
                        result.WarningCount++;
                    }
                }

                result.Status = result.ErrorCount > 0 ? CheckStatus.Failed : CheckStatus.Passed;
                _logger.LogInformation("테이블 리스트 검증 완료: 오류 {ErrorCount}개, 경고 {WarningCount}개", 
                    result.ErrorCount, result.WarningCount);

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 리스트 검증 중 오류 발생");
                result.Status = CheckStatus.Failed;
                result.Errors.Add(new ValidationError
                {
                    ErrorId = Guid.NewGuid().ToString(),
                    Message = $"테이블 리스트 검증 중 오류 발생: {ex.Message}",
                    Severity = ErrorSeverity.Critical
                });
                result.ErrorCount++;
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 좌표계 검증을 수행합니다
        /// </summary>
        public Task<CheckResult> ValidateCoordinateSystemAsync(SpatialFileInfo spatialFile, IEnumerable<TableCheckConfig> config)
        {
            var result = new CheckResult
            {
                CheckId = "COORDINATE_SYSTEM_CHECK",
                CheckName = "좌표계 검증",
                Status = CheckStatus.Running
            };

            try
            {
                _logger.LogInformation("좌표계 검증 시작: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                result.TotalCount = configList.Count;

                // GDAL을 사용하여 실제 파일의 좌표계 정보 확인
                using var dataSource = Ogr.Open(spatialFile.FilePath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"파일을 열 수 없습니다: {spatialFile.FilePath}");
                }

                foreach (var tableConfig in configList)
                {
                    // 해당 테이블(레이어) 찾기
                    Layer layer = null;
                    for (int i = 0; i < dataSource.GetLayerCount(); i++)
                    {
                        var currentLayer = dataSource.GetLayerByIndex(i);
                        if (string.Equals(currentLayer.GetName(), tableConfig.TableName, StringComparison.OrdinalIgnoreCase))
                        {
                            layer = currentLayer;
                            break;
                        }
                    }

                    if (layer == null)
                    {
                        // 테이블이 존재하지 않는 경우는 테이블 리스트 검증에서 처리되므로 건너뜀
                        continue;
                    }

                    // 좌표계 정보 확인
                    var spatialRef = layer.GetSpatialRef();
                    string actualCoordSystem = "UNKNOWN";
                    
                    if (spatialRef != null)
                    {
                        spatialRef.AutoIdentifyEPSG();
                        var epsgCode = spatialRef.GetAuthorityCode(null);
                        if (!string.IsNullOrEmpty(epsgCode))
                        {
                            actualCoordSystem = $"EPSG:{epsgCode}";
                        }
                        else
                        {
                            // EPSG 코드가 없는 경우 WKT 문자열 사용
                            spatialRef.ExportToWkt(out string wkt, null);
                            actualCoordSystem = wkt?.Substring(0, Math.Min(100, wkt.Length)) ?? "UNKNOWN";
                        }
                    }

                    // 설정된 좌표계와 비교
                    if (!string.Equals(actualCoordSystem, tableConfig.CoordinateSystem, StringComparison.OrdinalIgnoreCase))
                    {
                        var error = new ValidationError
                        {
                            ErrorId = Guid.NewGuid().ToString(),
                            TableName = tableConfig.TableName,
                            Message = $"테이블 '{tableConfig.TableName}'의 좌표계가 설정과 다릅니다. 예상: {tableConfig.CoordinateSystem}, 실제: {actualCoordSystem}",
                            Severity = ErrorSeverity.Error,
                            Metadata = new Dictionary<string, object>
                            {
                                ["ExpectedCoordinateSystem"] = tableConfig.CoordinateSystem,
                                ["ActualCoordinateSystem"] = actualCoordSystem,
                                ["TableId"] = tableConfig.TableId
                            }
                        };
                        result.Errors.Add(error);
                        result.ErrorCount++;
                    }
                }

                result.Status = result.ErrorCount > 0 ? CheckStatus.Failed : CheckStatus.Passed;
                _logger.LogInformation("좌표계 검증 완료: 오류 {ErrorCount}개", result.ErrorCount);

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "좌표계 검증 중 오류 발생");
                result.Status = CheckStatus.Failed;
                result.Errors.Add(new ValidationError
                {
                    ErrorId = Guid.NewGuid().ToString(),
                    Message = $"좌표계 검증 중 오류 발생: {ex.Message}",
                    Severity = ErrorSeverity.Critical
                });
                result.ErrorCount++;
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 지오메트리 타입 검증을 수행합니다
        /// </summary>
        public Task<CheckResult> ValidateGeometryTypeAsync(SpatialFileInfo spatialFile, IEnumerable<TableCheckConfig> config)
        {
            var result = new CheckResult
            {
                CheckId = "GEOMETRY_TYPE_CHECK",
                CheckName = "지오메트리 타입 검증",
                Status = CheckStatus.Running
            };

            try
            {
                _logger.LogInformation("지오메트리 타입 검증 시작: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                result.TotalCount = configList.Count;

                // GDAL을 사용하여 실제 파일의 지오메트리 타입 확인
                using var dataSource = Ogr.Open(spatialFile.FilePath, 0);
                if (dataSource == null)
                {
                    throw new InvalidOperationException($"파일을 열 수 없습니다: {spatialFile.FilePath}");
                }

                foreach (var tableConfig in configList)
                {
                    // 해당 테이블(레이어) 찾기
                    Layer layer = null;
                    for (int i = 0; i < dataSource.GetLayerCount(); i++)
                    {
                        var currentLayer = dataSource.GetLayerByIndex(i);
                        if (string.Equals(currentLayer.GetName(), tableConfig.TableName, StringComparison.OrdinalIgnoreCase))
                        {
                            layer = currentLayer;
                            break;
                        }
                    }

                    if (layer == null)
                    {
                        // 테이블이 존재하지 않는 경우는 테이블 리스트 검증에서 처리되므로 건너뜀
                        continue;
                    }

                    // 지오메트리 타입 확인
                    var geometryType = layer.GetGeomType();
                    string actualGeomType = ConvertOgrGeometryTypeToString(geometryType);

                    // 설정된 지오메트리 타입과 비교
                    if (!string.Equals(actualGeomType, tableConfig.GeometryType, StringComparison.OrdinalIgnoreCase))
                    {
                        var error = new ValidationError
                        {
                            ErrorId = Guid.NewGuid().ToString(),
                            TableName = tableConfig.TableName,
                            Message = $"테이블 '{tableConfig.TableName}'의 지오메트리 타입이 설정과 다릅니다. 예상: {tableConfig.GeometryType}, 실제: {actualGeomType}",
                            Severity = ErrorSeverity.Error,
                            Metadata = new Dictionary<string, object>
                            {
                                ["ExpectedGeometryType"] = tableConfig.GeometryType,
                                ["ActualGeometryType"] = actualGeomType,
                                ["TableId"] = tableConfig.TableId
                            }
                        };
                        result.Errors.Add(error);
                        result.ErrorCount++;
                    }
                }

                result.Status = result.ErrorCount > 0 ? CheckStatus.Failed : CheckStatus.Passed;
                _logger.LogInformation("지오메트리 타입 검증 완료: 오류 {ErrorCount}개", result.ErrorCount);

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 타입 검증 중 오류 발생");
                result.Status = CheckStatus.Failed;
                result.Errors.Add(new ValidationError
                {
                    ErrorId = Guid.NewGuid().ToString(),
                    Message = $"지오메트리 타입 검증 중 오류 발생: {ex.Message}",
                    Severity = ErrorSeverity.Critical
                });
                result.ErrorCount++;
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 전체 테이블 검수를 수행합니다
        /// </summary>
        public async Task<StageResult> ExecuteTableCheckAsync(SpatialFileInfo spatialFile, IEnumerable<TableCheckConfig> config)
        {
            var stageResult = new StageResult
            {
                StageNumber = 1,
                StageName = "테이블 검수",
                Status = StageStatus.Running,
                StartedAt = DateTime.Now
            };

            try
            {
                _logger.LogInformation("1단계 테이블 검수 시작: {FilePath}", spatialFile.FilePath);

                var configList = config.ToList();
                if (!configList.Any())
                {
                    throw new InvalidOperationException("테이블 검수 설정이 없습니다.");
                }

                // 1. 테이블 리스트 검증
                var tableListResult = await ValidateTableListAsync(spatialFile, configList);
                stageResult.CheckResults.Add(tableListResult);

                // 테이블 리스트 검증에 실패하면 즉시 중단
                if (tableListResult.Status == CheckStatus.Failed)
                {
                    stageResult.Status = StageStatus.Failed;
                    stageResult.ErrorMessage = "테이블 리스트 검증에 실패하여 검수를 중단합니다.";
                    stageResult.CompletedAt = DateTime.Now;
                    _logger.LogError("테이블 리스트 검증 실패로 1단계 검수 중단");
                    return stageResult;
                }

                // 2. 좌표계 검증
                var coordSystemResult = await ValidateCoordinateSystemAsync(spatialFile, configList);
                stageResult.CheckResults.Add(coordSystemResult);

                // 좌표계 검증에 실패하면 즉시 중단
                if (coordSystemResult.Status == CheckStatus.Failed)
                {
                    stageResult.Status = StageStatus.Failed;
                    stageResult.ErrorMessage = "좌표계 검증에 실패하여 검수를 중단합니다.";
                    stageResult.CompletedAt = DateTime.Now;
                    _logger.LogError("좌표계 검증 실패로 1단계 검수 중단");
                    return stageResult;
                }

                // 3. 지오메트리 타입 검증
                var geomTypeResult = await ValidateGeometryTypeAsync(spatialFile, configList);
                stageResult.CheckResults.Add(geomTypeResult);

                // 지오메트리 타입 검증에 실패하면 즉시 중단
                if (geomTypeResult.Status == CheckStatus.Failed)
                {
                    stageResult.Status = StageStatus.Failed;
                    stageResult.ErrorMessage = "지오메트리 타입 검증에 실패하여 검수를 중단합니다.";
                    stageResult.CompletedAt = DateTime.Now;
                    _logger.LogError("지오메트리 타입 검증 실패로 1단계 검수 중단");
                    return stageResult;
                }

                // 모든 검수 통과
                stageResult.Status = StageStatus.Completed;
                stageResult.CompletedAt = DateTime.Now;
                _logger.LogInformation("1단계 테이블 검수 완료");

                return stageResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1단계 테이블 검수 중 오류 발생");
                stageResult.Status = StageStatus.Failed;
                stageResult.ErrorMessage = $"테이블 검수 중 오류 발생: {ex.Message}";
                stageResult.CompletedAt = DateTime.Now;
                return stageResult;
            }
        }



        /// <summary>
        /// 테이블 목록 검수를 수행합니다 (인터페이스 구현)
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        public async Task<ValidationResult> ValidateTableListAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                var spatialFile = new SpatialFileInfo { FilePath = filePath };
                var checkResult = await ValidateTableListAsync(spatialFile, new[] { config });
                return ConvertCheckResultToValidationResult(checkResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 목록 검수 중 오류 발생: {FilePath}", filePath);
                return CreateErrorValidationResult(ex.Message);
            }
        }

        /// <summary>
        /// 좌표계 검수를 수행합니다 (인터페이스 구현)
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        public async Task<ValidationResult> ValidateCoordinateSystemAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                var spatialFile = new SpatialFileInfo { FilePath = filePath };
                var checkResult = await ValidateCoordinateSystemAsync(spatialFile, new[] { config });
                return ConvertCheckResultToValidationResult(checkResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "좌표계 검수 중 오류 발생: {FilePath}", filePath);
                return CreateErrorValidationResult(ex.Message);
            }
        }

        /// <summary>
        /// 지오메트리 타입 검수를 수행합니다 (인터페이스 구현)
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        public async Task<ValidationResult> ValidateGeometryTypeAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                var spatialFile = new SpatialFileInfo { FilePath = filePath };
                var checkResult = await ValidateGeometryTypeAsync(spatialFile, new[] { config });
                return ConvertCheckResultToValidationResult(checkResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 타입 검수 중 오류 발생: {FilePath}", filePath);
                return CreateErrorValidationResult(ex.Message);
            }
        }

        /// <summary>
        /// 테이블 검수를 수행합니다 (인터페이스 구현)
        /// </summary>
        /// <param name="filePath">검수할 파일 경로</param>
        /// <param name="config">테이블 검수 설정</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>검수 결과</returns>
        public async Task<ValidationResult> ProcessAsync(string filePath, TableCheckConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                // 파일 정보 생성
                var spatialFile = new SpatialFileInfo { FilePath = filePath };
                
                // 기존 메서드를 활용하여 검수 수행
                var stageResult = await ExecuteTableCheckAsync(spatialFile, new[] { config });
                
                // StageResult를 ValidationResult로 변환
                var validationResult = new ValidationResult
                {
                    IsValid = stageResult.Status == StageStatus.Completed,
                    ErrorCount = stageResult.CheckResults.Sum(c => c.ErrorCount),
                    WarningCount = stageResult.CheckResults.Sum(c => c.WarningCount),
                    ProcessingTime = (stageResult.CompletedAt ?? DateTime.Now) - stageResult.StartedAt,
                    Message = stageResult.ErrorMessage ?? "테이블 검수 완료"
                };

                // 오류 및 경고 정보 추가
                foreach (var checkResult in stageResult.CheckResults)
                {
                    validationResult.Errors.AddRange(checkResult.Errors);
                    validationResult.Warnings.AddRange(checkResult.Warnings);
                }

                return validationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 검수 중 오류 발생: {FilePath}", filePath);
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorCount = 1,
                    Message = $"테이블 검수 중 오류 발생: {ex.Message}",
                    Errors = new List<ValidationError>
                    {
                        new ValidationError
                        {
                            ErrorId = Guid.NewGuid().ToString(),
                            Message = ex.Message,
                            Severity = ErrorSeverity.Critical
                        }
                    }
                };
            }
        }



        /// <summary>
        /// CheckResult를 ValidationResult로 변환합니다
        /// </summary>
        private static ValidationResult ConvertCheckResultToValidationResult(CheckResult checkResult)
        {
            return new ValidationResult
            {
                IsValid = checkResult.Status == CheckStatus.Passed,
                ErrorCount = checkResult.ErrorCount,
                WarningCount = checkResult.WarningCount,
                ProcessingTime = TimeSpan.Zero,
                Message = checkResult.Status == CheckStatus.Passed ? "검수 완료" : "검수 실패",
                Errors = checkResult.Errors.ToList(),
                Warnings = checkResult.Warnings.ToList()
            };
        }

        /// <summary>
        /// 오류 ValidationResult를 생성합니다
        /// </summary>
        private static ValidationResult CreateErrorValidationResult(string errorMessage)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorCount = 1,
                Message = $"검수 중 오류 발생: {errorMessage}",
                Errors = new List<ValidationError>
                {
                    new ValidationError
                    {
                        ErrorId = Guid.NewGuid().ToString(),
                        Message = errorMessage,
                        Severity = ErrorSeverity.Critical
                    }
                }
            };
        }

        /// <summary>
        /// OGR 지오메트리 타입을 문자열로 변환합니다
        /// </summary>
        private static string ConvertOgrGeometryTypeToString(wkbGeometryType geometryType)
        {
            return geometryType switch
            {
                wkbGeometryType.wkbPoint => "POINT",
                wkbGeometryType.wkbLineString => "LINESTRING",
                wkbGeometryType.wkbPolygon => "POLYGON",
                wkbGeometryType.wkbMultiPoint => "MULTIPOINT",
                wkbGeometryType.wkbMultiLineString => "MULTILINESTRING",
                wkbGeometryType.wkbMultiPolygon => "MULTIPOLYGON",
                wkbGeometryType.wkbGeometryCollection => "GEOMETRYCOLLECTION",
                wkbGeometryType.wkbPoint25D => "POINT Z",
                wkbGeometryType.wkbLineString25D => "LINESTRING Z",
                wkbGeometryType.wkbPolygon25D => "POLYGON Z",
                wkbGeometryType.wkbMultiPoint25D => "MULTIPOINT Z",
                wkbGeometryType.wkbMultiLineString25D => "MULTILINESTRING Z",
                wkbGeometryType.wkbMultiPolygon25D => "MULTIPOLYGON Z",
                _ => geometryType.ToString()
            };
        }
    }
}
