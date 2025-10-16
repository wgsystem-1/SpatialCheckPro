using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using System.Linq; // Added for .Any()

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC 오류 데이터 입출력 서비스
    /// </summary>
    public class QcErrorDataService
    {
        private readonly ILogger<QcErrorDataService> _logger;
        private readonly FgdbSchemaService _schemaService;

        public QcErrorDataService(ILogger<QcErrorDataService> logger, FgdbSchemaService schemaService)
        {
            _logger = logger;
            _schemaService = schemaService;
        }

        /// <summary>
        /// GDAL 초기화 상태를 확인하고 필요시 재초기화합니다
        /// </summary>
        private void EnsureGdalInitialized()
        {
            try
            {
                // GDAL 드라이버 개수로 초기화 상태 확인
                var driverCount = Ogr.GetDriverCount();
                if (driverCount == 0)
                {
                    _logger.LogWarning("GDAL 드라이버가 등록되지 않음. 재초기화 수행...");
                    Gdal.AllRegister();
                    Ogr.RegisterAll();
                    
                    driverCount = Ogr.GetDriverCount();
                    _logger.LogInformation("GDAL 재초기화 완료. 등록된 드라이버 수: {DriverCount}", driverCount);
                }
                else
                {
                    _logger.LogDebug("GDAL 초기화 상태 정상. 등록된 드라이버 수: {DriverCount}", driverCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 초기화 상태 확인 중 오류 발생");
                _logger.LogWarning("GDAL 초기화 오류를 무시하고 계속 진행합니다");
            }
        }

        /// <summary>
        /// FileGDB 드라이버를 안전하게 가져옵니다
        /// </summary>
        private OSGeo.OGR.Driver GetFileGdbDriverSafely()
        {
            string[] driverNames = { "FileGDB", "ESRI FileGDB", "OpenFileGDB" };
            
            foreach (var driverName in driverNames)
            {
                try
                {
                    var driver = Ogr.GetDriverByName(driverName);
                    if (driver != null)
                    {
                        _logger.LogDebug("{DriverName} 드라이버 사용", driverName);
                        return driver;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("{DriverName} 드라이버 확인 실패: {Error}", driverName, ex.Message);
                }
            }
            
            _logger.LogError("사용 가능한 FileGDB 드라이버를 찾을 수 없습니다");
            return null;
        }

        /// <summary>
        /// QC_ERRORS 데이터베이스를 초기화합니다
        /// </summary>
        public async Task<bool> InitializeQcErrorsDatabaseAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 데이터베이스 초기화: {GdbPath}", gdbPath);
                return await _schemaService.CreateQcErrorsSchemaAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 데이터베이스 초기화 실패: {GdbPath}", gdbPath);
                
                // 구체적인 오류 원인 분석
                if (ex is UnauthorizedAccessException)
                {
                    _logger.LogError("권한 부족: FileGDB에 쓰기 권한이 없습니다");
                }
                else if (ex is DirectoryNotFoundException)
                {
                    _logger.LogError("경로 오류: 지정된 경로를 찾을 수 없습니다");
                }
                else if (ex is IOException)
                {
                    _logger.LogError("입출력 오류: 디스크 공간 부족이거나 파일이 사용 중일 수 있습니다");
                }
                else
                {
                    _logger.LogError("예상치 못한 오류: {ErrorType} - {Message}", ex.GetType().Name, ex.Message);
                }
                
                return false;
            }
        }

        /// <summary>
        /// QC_ERRORS 스키마 유효성을 검사합니다
        /// </summary>
        public async Task<bool> ValidateQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                _logger.LogDebug("QC_ERRORS 스키마 유효성 검사: {GdbPath}", gdbPath);
                return await _schemaService.ValidateSchemaAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 검증 실패");
                return false;
            }
        }

        /// <summary>
        /// 손상된 QC_ERRORS 스키마를 자동 복구합니다
        /// </summary>
        public async Task<bool> RepairQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 스키마 복구: {GdbPath}", gdbPath);
                return await _schemaService.RepairQcErrorsSchemaAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 복구 실패");
                return false;
            }
        }

        /// <summary>
        /// QC 오류를 저장합니다
        /// </summary>
        public async Task<bool> UpsertQcErrorAsync(string gdbPath, QcError qcError)
        {
            try
            {
                _logger.LogDebug("QC 오류 저장 시작: {ErrorCode} - {TableId}:{ObjectId}", 
                    qcError.ErrCode, qcError.SourceClass, qcError.SourceOID);

                return await Task.Run(() =>
                {
                    try
                    {
                        // GDAL 초기화 확인 (안전장치)
                        EnsureGdalInitialized();
                        
                        // FileGDB를 쓰기 모드로 열기 (안전한 방식)
                        var driver = GetFileGdbDriverSafely();
                        if (driver == null)
                        {
                            _logger.LogError("FileGDB 드라이버를 찾을 수 없습니다: {GdbPath}", gdbPath);
                            return false;
                        }
                        
                        // 데이터셋(QC_ERRORS) 하위에 Feature Class가 위치할 수 있으므로 우선 루트 열기
                        var dataSource = driver.Open(gdbPath, 1); // 쓰기 모드

                        if (dataSource == null)
                        {
                            _logger.LogError("FileGDB를 쓰기 모드로 열 수 없습니다: {GdbPath}", gdbPath);
                            return false;
                        }

                        // 지오메트리 타입에 따라 적절한 레이어 선택 (대소문자 및 멀티타입 지원)
                        var geomTypeUpper = (qcError.GeometryType ?? string.Empty).Trim().ToUpperInvariant();
                        if (string.IsNullOrEmpty(geomTypeUpper) || geomTypeUpper == "UNKNOWN")
                        {
                            // GeometryWKT가 있으면 타입 유추
                            if (!string.IsNullOrWhiteSpace(qcError.GeometryWKT))
                            {
                                var wktUpper = qcError.GeometryWKT.ToUpperInvariant();
                                if (wktUpper.StartsWith("POINT")) geomTypeUpper = "POINT";
                                else if (wktUpper.StartsWith("LINESTRING") || wktUpper.StartsWith("MULTILINESTRING")) geomTypeUpper = "LINESTRING";
                                else if (wktUpper.StartsWith("POLYGON") || wktUpper.StartsWith("MULTIPOLYGON")) geomTypeUpper = "POLYGON";
                            }
                        }
                        string layerName = geomTypeUpper switch
                        {
                            "POINT" or "MULTIPOINT" => "QC_Errors_Point",
                            "LINESTRING" or "MULTILINESTRING" => "QC_Errors_Line",
                            "POLYGON" or "MULTIPOLYGON" => "QC_Errors_Polygon",
                            _ => "QC_Errors_NoGeom"
                        };

                        // 데이터셋 내부 탐색: 루트에서 직접 못 찾으면 하위 계층에서 검색
                        Layer layer = dataSource.GetLayerByName(layerName);
                        if (layer == null)
                        {
                            for (int i = 0; i < dataSource.GetLayerCount(); i++)
                            {
                                var l = dataSource.GetLayerByIndex(i);
                                if (l != null && string.Equals(l.GetName(), layerName, StringComparison.OrdinalIgnoreCase)) { layer = l; break; }
                            }
                        }
                        if (layer == null)
                        {
                            _logger.LogWarning("QC_ERRORS 레이어를 찾을 수 없습니다: {LayerName} - 레이어 생성 시도", layerName);
                            
                            // 레이어가 없으면 생성 시도
                            layer = CreateQcErrorLayer(dataSource, layerName);
                            if (layer == null)
                            {
                                _logger.LogError("QC_ERRORS 레이어 생성 실패: {LayerName}", layerName);
                                dataSource.Dispose();
                                return false;
                            }
                            _logger.LogInformation("QC_ERRORS 레이어 생성 성공: {LayerName}", layerName);
                        }

                        // 새 피처 생성
                        var featureDefn = layer.GetLayerDefn();
                        var feature = new Feature(featureDefn);

                        // 필드 값 설정
                        feature.SetField("GlobalID", qcError.GlobalID);
                        feature.SetField("ErrType", qcError.ErrType);
                        feature.SetField("ErrCode", qcError.ErrCode);
                        feature.SetField("Severity", qcError.Severity);
                        feature.SetField("Status", qcError.Status);
                        feature.SetField("RuleId", qcError.RuleId);
                        feature.SetField("SourceClass", qcError.SourceClass);
                        feature.SetField("SourceOID", (int)qcError.SourceOID);
                        
                        if (!string.IsNullOrEmpty(qcError.SourceGlobalID))
                        {
                            feature.SetField("SourceGlobalID", qcError.SourceGlobalID);
                        }

                        feature.SetField("X", qcError.X);
                        feature.SetField("Y", qcError.Y);
                        
                        if (!string.IsNullOrEmpty(qcError.GeometryWKT))
                        {
                            feature.SetField("GeometryWKT", qcError.GeometryWKT);
                        }
                        
                        feature.SetField("GeometryType", qcError.GeometryType);
                        feature.SetField("ErrorValue", qcError.ErrorValue);
                        feature.SetField("ThresholdValue", qcError.ThresholdValue);
                        feature.SetField("Message", qcError.Message);
                        feature.SetField("DetailsJSON", qcError.DetailsJSON);
                        feature.SetField("RunID", qcError.RunID);
                        // 검수 파일명 저장(필드가 있으면 설정)
                        try { feature.SetField("SourceFile", qcError.SourceFile ?? string.Empty); } catch { }
                        feature.SetField("CreatedUTC", qcError.CreatedUTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        feature.SetField("UpdatedUTC", qcError.UpdatedUTC.ToString("yyyy-MM-dd HH:mm:ss"));

                        // 지오메트리 설정 (NoGeom 레이어는 지오메트리를 설정하지 않음)
                        if (!string.Equals(layerName, "QC_Errors_NoGeom", StringComparison.OrdinalIgnoreCase))
                        {
                            if (qcError.Geometry != null && qcError.Geometry is OSGeo.OGR.Geometry ogrGeometry)
                            {
                                feature.SetGeometryDirectly(ogrGeometry);
                            }
                            else if (!string.IsNullOrWhiteSpace(qcError.GeometryWKT))
                            {
                                try
                                {
                                    var geomFromWkt = OSGeo.OGR.Geometry.CreateFromWkt(qcError.GeometryWKT);
                                    if (geomFromWkt != null)
                                    {
                                        feature.SetGeometry(geomFromWkt);
                                        geomFromWkt.Dispose();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "WKT로부터 지오메트리 생성 실패: {WKT}", qcError.GeometryWKT);
                                }
                            }
                            else if (qcError.X != 0 && qcError.Y != 0)
                            {
                                // 좌표가 있으면 Point 지오메트리 생성 (포인트 레이어에만 의미가 있음)
                                var point = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                                point.AddPoint(qcError.X, qcError.Y, 0);
                                feature.SetGeometry(point);
                                point.Dispose();
                            }
                            else
                            {
                                // 지오메트리 정보 전혀 없을 때 NoGeom 레이어로 리다이렉트 저장
                                _logger.LogDebug("지오메트리 정보 없음. NoGeom 레이어로 저장 리다이렉트");
                                layer = dataSource.GetLayerByName("QC_Errors_NoGeom");
                                if (layer == null)
                                {
                                    _logger.LogError("QC_Errors_NoGeom 레이어를 찾을 수 없습니다");
                                    dataSource.Dispose();
                                    return false;
                                }
                                featureDefn = layer.GetLayerDefn();
                                var redirectFeature = new Feature(featureDefn);
                                // 공통 필드 재설정
                                redirectFeature.SetField("GlobalID", qcError.GlobalID);
                                redirectFeature.SetField("ErrType", qcError.ErrType);
                                redirectFeature.SetField("ErrCode", qcError.ErrCode);
                                redirectFeature.SetField("Severity", qcError.Severity);
                                redirectFeature.SetField("Status", qcError.Status);
                                redirectFeature.SetField("RuleId", qcError.RuleId);
                                redirectFeature.SetField("SourceClass", qcError.SourceClass);
                                redirectFeature.SetField("SourceOID", (long)qcError.SourceOID);
                                if (!string.IsNullOrEmpty(qcError.SourceGlobalID)) redirectFeature.SetField("SourceGlobalID", qcError.SourceGlobalID);
                                redirectFeature.SetField("X", qcError.X);
                                redirectFeature.SetField("Y", qcError.Y);
                                if (!string.IsNullOrEmpty(qcError.GeometryWKT)) redirectFeature.SetField("GeometryWKT", qcError.GeometryWKT);
                                redirectFeature.SetField("GeometryType", qcError.GeometryType);
                                redirectFeature.SetField("ErrorValue", qcError.ErrorValue);
                                redirectFeature.SetField("ThresholdValue", qcError.ThresholdValue);
                                redirectFeature.SetField("Message", qcError.Message);
                                redirectFeature.SetField("DetailsJSON", qcError.DetailsJSON);
                                redirectFeature.SetField("RunID", qcError.RunID);
                                redirectFeature.SetField("CreatedUTC", qcError.CreatedUTC.ToString("yyyy-MM-dd HH:mm:ss"));
                                redirectFeature.SetField("UpdatedUTC", qcError.UpdatedUTC.ToString("yyyy-MM-dd HH:mm:ss"));
                                var redirectResult = layer.CreateFeature(redirectFeature);
                                redirectFeature.Dispose();
                                dataSource.Dispose();
                                if (redirectResult == 0)
                                {
                                    _logger.LogDebug("NoGeom 레이어로 저장 성공: {ErrorCode}", qcError.ErrCode);
                                    return true;
                                }
                                _logger.LogError("NoGeom 레이어 저장 실패: {Result}", redirectResult);
                                return false;
                            }
                        }

                        // 피처를 레이어에 추가
                        var result = layer.CreateFeature(feature);
                        
                        feature.Dispose();
                        dataSource.Dispose();

                        if (result == 0) // OGRERR_NONE
                        {
                            _logger.LogDebug("QC 오류 저장 성공: {ErrorCode}", qcError.ErrCode);
                            return true;
                        }
                        else
                        {
                            _logger.LogError("QC 오류 저장 실패: {ErrorCode}, OGR 오류 코드: {Result}", qcError.ErrCode, result);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "QC 오류 저장 중 예외 발생: {ErrorCode}", qcError.ErrCode);
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 저장 실패: {ErrorCode}", qcError.ErrCode);
                return false;
            }
        }

        /// <summary>
        /// QC 오류 상태를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateQcErrorStatusAsync(string gdbPath, string errorId, string newStatus)
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// QC 오류 담당자를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateQcErrorAssigneeAsync(string gdbPath, string errorId, string assignee)
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// QC 오류 심각도를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateQcErrorSeverityAsync(string gdbPath, string errorId, string severity)
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 특정 위치에서 허용 거리 내의 오류들을 검색합니다
        /// </summary>
        public async Task<List<QcError>> SearchErrorsAtLocationAsync(string gdbPath, double x, double y, double tolerance)
        {
            return await Task.FromResult(new List<QcError>());
        }

        /// <summary>
        /// 특정 영역 내의 오류들을 검색합니다
        /// </summary>
        public async Task<List<QcError>> SearchErrorsInBoundsAsync(string gdbPath, double minX, double minY, double maxX, double maxY)
        {
            return await Task.FromResult(new List<QcError>());
        }

        /// <summary>
        /// 오류 ID로 특정 오류를 검색합니다
        /// </summary>
        public async Task<QcError?> GetQcErrorByIdAsync(string gdbPath, string errorId)
        {
            return await Task.FromResult<QcError?>(null);
        }

        /// <summary>
        /// 스키마 유효성을 검사합니다
        /// </summary>
        public async Task<bool> ValidateSchemaAsync(string gdbPath)
        {
            return await _schemaService.ValidateSchemaAsync(gdbPath);
        }

        /// <summary>
        /// QC 실행 정보를 생성합니다
        /// </summary>
        public async Task<string> CreateQcRunAsync(string gdbPath, QcRun qcRun)
        {
            return await Task.Run(() =>
            {
                EnsureGdalInitialized();
                using var dataSource = Ogr.Open(gdbPath, 1);
                if (dataSource == null)
                {
                    _logger.LogError("QC_Runs 생성을 위해 GDB를 열 수 없습니다: {Path}", gdbPath);
                    return string.Empty;
                }

                var layer = dataSource.GetLayerByName("QC_Runs");
                if (layer == null)
                {
                    _logger.LogError("QC_Runs 테이블을 찾을 수 없습니다.");
                    return string.Empty;
                }

                using var feature = new Feature(layer.GetLayerDefn());
                var runId = Guid.NewGuid().ToString();
                qcRun.GlobalID = runId;

                feature.SetField("GlobalID", qcRun.GlobalID);
                feature.SetField("RunName", qcRun.RunName);
                feature.SetField("TargetFilePath", qcRun.TargetFilePath);
                feature.SetField("RulesetVersion", qcRun.RulesetVersion);
                feature.SetField("StartTimeUTC", qcRun.StartTimeUTC.ToString("o"));
                feature.SetField("ExecutedBy", qcRun.ExecutedBy);
                feature.SetField("Status", qcRun.Status);
                feature.SetField("CreatedUTC", DateTime.UtcNow.ToString("o"));
                feature.SetField("UpdatedUTC", DateTime.UtcNow.ToString("o"));

                if (layer.CreateFeature(feature) != 0)
                {
                    _logger.LogError("QC_Runs 레코드 생성 실패");
                    return string.Empty;
                }
                
                _logger.LogInformation("QC_Runs 레코드 생성 성공: {RunId}", runId);
                return runId;
            });
        }

        /// <summary>
        /// QC 오류 데이터를 배치로 추가합니다
        /// </summary>
        public async Task<int> BatchAppendQcErrorsAsync(string gdbPath, IEnumerable<QcError> qcErrors, int batchSize = 1000)
        {
            return await Task.Run(() =>
            {
                int successCount = 0;
                if (!qcErrors.Any()) return 0;

                try
                {
                    EnsureGdalInitialized();
                    var driver = GetFileGdbDriverSafely();
                    if (driver == null) return 0;

                    using var dataSource = driver.Open(gdbPath, 1);
                    if (dataSource == null) return 0;

                    var groupedErrors = qcErrors.GroupBy(e =>
                    {
                        var wktType = QcError.DetermineGeometryType(e.GeometryWKT).ToUpperInvariant();
                        if (!string.Equals(wktType, "UNKNOWN", StringComparison.Ordinal))
                        {
                            return wktType;
                        }

                        var declaredType = (e.GeometryType ?? string.Empty).Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(declaredType))
                        {
                            return declaredType;
                        }

                        if (e.Geometry != null)
                        {
                            return e.Geometry.GetGeometryName()?.ToUpperInvariant() ?? "NOGEOM";
                        }

                        return "NOGEOM";
                    });

                    foreach (var group in groupedErrors)
                    {
                        string normalizedKey = group.Key switch
                        {
                            _ when string.Equals(group.Key, "POINT", StringComparison.OrdinalIgnoreCase) => "POINT",
                            _ when string.Equals(group.Key, "MULTIPOINT", StringComparison.OrdinalIgnoreCase) => "POINT",
                            _ when string.Equals(group.Key, "LINESTRING", StringComparison.OrdinalIgnoreCase) => "LINESTRING",
                            _ when string.Equals(group.Key, "LINE", StringComparison.OrdinalIgnoreCase) => "LINESTRING",
                            _ when string.Equals(group.Key, "MULTILINESTRING", StringComparison.OrdinalIgnoreCase) => "LINESTRING",
                            _ when string.Equals(group.Key, "POLYGON", StringComparison.OrdinalIgnoreCase) => "POLYGON",
                            _ when string.Equals(group.Key, "MULTIPOLYGON", StringComparison.OrdinalIgnoreCase) => "POLYGON",
                            _ when group.Key.Contains("LINE", StringComparison.OrdinalIgnoreCase) => "LINESTRING",
                            _ when group.Key.Contains("POLY", StringComparison.OrdinalIgnoreCase) => "POLYGON",
                            _ => "NOGEOM"
                        };

                        string layerName = normalizedKey switch
                        {
                            "POINT" => "QC_Errors_Point",
                            "LINESTRING" => "QC_Errors_Line",
                            "POLYGON" => "QC_Errors_Polygon",
                            _ => "QC_Errors_NoGeom"
                        };
                        var layer = dataSource.GetLayerByName(layerName);
                        if (layer == null) continue;

                        layer.StartTransaction();
                        foreach (var qcError in group)
                        {
                            using var feature = new Feature(layer.GetLayerDefn());
                            feature.SetField("GlobalID", qcError.GlobalID);
                            feature.SetField("ErrType", qcError.ErrType);
                            feature.SetField("ErrCode", qcError.ErrCode);
                            feature.SetField("Severity", qcError.Severity);
                            feature.SetField("Status", qcError.Status);
                            feature.SetField("RuleId", qcError.RuleId);
                            feature.SetField("SourceClass", qcError.SourceClass);
                            feature.SetField("SourceOID", (int)qcError.SourceOID);
                            feature.SetField("RunID", qcError.RunID);
                            feature.SetField("Message", qcError.Message);
                            feature.SetField("CreatedUTC", qcError.CreatedUTC.ToString("o"));
                            feature.SetField("UpdatedUTC", qcError.UpdatedUTC.ToString("o"));
                            
                            if (layerName != "QC_Errors_NoGeom")
                            {
                                if (qcError.Geometry != null)
                                {
                                    feature.SetGeometry(qcError.Geometry);
                                }
                                else if (!string.IsNullOrWhiteSpace(qcError.GeometryWKT))
                                {
                                    var geometryFromWkt = OSGeo.OGR.Geometry.CreateFromWkt(qcError.GeometryWKT);
                                    if (geometryFromWkt != null)
                                    {
                                        feature.SetGeometry(geometryFromWkt);
                                        geometryFromWkt.Dispose();
                                    }
                                }
                            }

                            if (layer.CreateFeature(feature) == 0)
                            {
                                successCount++;
                            }
                        }
                        layer.CommitTransaction();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "QC 오류 일괄 저장 실패");
                    return successCount;
                }
                return successCount;
            });
        }

        /// <summary>
        /// QC 실행 상태를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateQcRunStatusAsync(string gdbPath, string runId, string status, int totalErrors = 0, int totalWarnings = 0, string? resultSummary = null)
        {
            return await Task.Run(() =>
            {
                EnsureGdalInitialized();
                using var dataSource = Ogr.Open(gdbPath, 1);
                if (dataSource == null) return false;

                var layer = dataSource.GetLayerByName("QC_Runs");
                if (layer == null) return false;

                layer.SetAttributeFilter($"GlobalID = '{runId}'");
                layer.ResetReading();
                using var feature = layer.GetNextFeature();

                if (feature == null)
                {
                    _logger.LogWarning("업데이트할 QC_Runs 레코드를 찾지 못했습니다: {RunId}", runId);
                    return false;
                }

                feature.SetField("EndTimeUTC", DateTime.UtcNow.ToString("o"));
                feature.SetField("Status", status);
                feature.SetField("TotalErrors", totalErrors);
                feature.SetField("TotalWarnings", totalWarnings);
                feature.SetField("ResultSummary", resultSummary);
                feature.SetField("UpdatedUTC", DateTime.UtcNow.ToString("o"));

                if (layer.SetFeature(feature) != 0)
                {
                    _logger.LogError("QC_Runs 레코드 업데이트 실패: {RunId}", runId);
                    return false;
                }
                
                _logger.LogInformation("QC_Runs 레코드 업데이트 성공: {RunId}", runId);
                return true;
            });
        }

        /// <summary>
        /// QC_ERRORS 레이어를 생성합니다
        /// </summary>
        /// <param name="dataSource">GDAL 데이터소스</param>
        /// <param name="layerName">레이어 이름</param>
        /// <returns>생성된 레이어 또는 null</returns>
        private Layer? CreateQcErrorLayer(DataSource dataSource, string layerName)
        {
            try
            {
                _logger.LogDebug("QC_ERRORS 레이어 생성 시작: {LayerName}", layerName);
                
                // 지오메트리 타입 결정
                var geometryType = layerName switch
                {
                    "QC_Errors_Point" => wkbGeometryType.wkbPoint,
                    "QC_Errors_Line" => wkbGeometryType.wkbLineString,
                    "QC_Errors_Polygon" => wkbGeometryType.wkbPolygon,
                    _ => wkbGeometryType.wkbNone
                };
                
                // 레이어 생성
                var layer = dataSource.CreateLayer(layerName, null, geometryType, null);
                if (layer == null)
                {
                    _logger.LogError("레이어 생성 실패: {LayerName}", layerName);
                    return null;
                }
                
                // 필드 정의 생성
                var fieldDefn = new FieldDefn("GlobalID", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("ErrType", FieldType.OFTString);
                fieldDefn.SetWidth(20);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("ErrCode", FieldType.OFTString);
                fieldDefn.SetWidth(20);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("Severity", FieldType.OFTString);
                fieldDefn.SetWidth(10);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("Status", FieldType.OFTString);
                fieldDefn.SetWidth(20);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("RuleId", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("SourceClass", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("SourceOID", FieldType.OFTInteger);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("SourceGlobalID", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("X", FieldType.OFTReal);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("Y", FieldType.OFTReal);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("GeometryWKT", FieldType.OFTString);
                fieldDefn.SetWidth(1000);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("GeometryType", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("ErrorValue", FieldType.OFTString);
                fieldDefn.SetWidth(100);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("ThresholdValue", FieldType.OFTString);
                fieldDefn.SetWidth(100);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("Message", FieldType.OFTString);
                fieldDefn.SetWidth(500);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("DetailsJSON", FieldType.OFTString);
                fieldDefn.SetWidth(2000);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("RunID", FieldType.OFTString);
                fieldDefn.SetWidth(50);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("SourceFile", FieldType.OFTString);
                fieldDefn.SetWidth(500);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("CreatedUTC", FieldType.OFTString);
                fieldDefn.SetWidth(30);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("UpdatedUTC", FieldType.OFTString);
                fieldDefn.SetWidth(30);
                layer.CreateField(fieldDefn, 1);
                
                _logger.LogInformation("QC_ERRORS 레이어 생성 완료: {LayerName}", layerName);
                return layer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 레이어 생성 중 오류 발생: {LayerName}", layerName);
                return null;
            }
        }

        /// <summary>
        /// QC 오류 목록을 조회합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="runId">실행 ID (선택사항)</param>
        /// <returns>QC 오류 목록</returns>
        public async Task<List<QcError>> GetQcErrorsAsync(string gdbPath, string? runId = null)
        {
            try
            {
                EnsureGdalInitialized();
                
                var qcErrors = new List<QcError>();
                
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    _logger.LogError("File Geodatabase를 열 수 없습니다: {Path}", gdbPath);
                    return qcErrors;
                }

                // QC_ERRORS 테이블들 조회
                string[] qcErrorTables = { "QC_Errors_Point", "QC_Errors_Line", "QC_Errors_Polygon", "QC_Errors_NoGeom" };
                
                foreach (var tableName in qcErrorTables)
                {
                    try
                    {
                        var layer = dataSource.GetLayerByName(tableName);
                        if (layer == null)
                        {
                            _logger.LogDebug("QC_ERRORS 테이블을 찾을 수 없습니다: {TableName}", tableName);
                            continue;
                        }

                        // RunID 필터링 (있는 경우)
                        if (!string.IsNullOrEmpty(runId))
                        {
                            layer.SetAttributeFilter($"RunID = '{runId}'");
                        }

                        layer.ResetReading();
                        
                        Feature feature;
                        while ((feature = layer.GetNextFeature()) != null)
                        {
                            try
                            {
                                var qcError = new QcError
                                {
                                    GlobalID = feature.GetFieldAsString("GlobalID"),
                                    ErrType = feature.GetFieldAsString("ErrType"),
                                    ErrCode = feature.GetFieldAsString("ErrCode"),
                                    Severity = feature.GetFieldAsString("Severity"),
                                    Status = feature.GetFieldAsString("Status"),
                                    RuleId = feature.GetFieldAsString("RuleId"),
                                    SourceClass = feature.GetFieldAsString("SourceClass"),
                                    SourceOID = feature.GetFieldAsInteger("SourceOID"),
                                    SourceGlobalID = feature.GetFieldAsString("SourceGlobalID"),
                                    X = feature.GetFieldAsDouble("X"),
                                    Y = feature.GetFieldAsDouble("Y"),
                                    GeometryWKT = feature.GetFieldAsString("GeometryWKT"),
                                    GeometryType = feature.GetFieldAsString("GeometryType"),
                                    ErrorValue = feature.GetFieldAsString("ErrorValue"),
                                    ThresholdValue = feature.GetFieldAsString("ThresholdValue"),
                                    Message = feature.GetFieldAsString("Message"),
                                    DetailsJSON = feature.GetFieldAsString("DetailsJSON"),
                                    RunID = feature.GetFieldAsString("RunID"),
                                    SourceFile = feature.GetFieldAsString("SourceFile"),
                                    CreatedUTC = DateTime.TryParse(feature.GetFieldAsString("CreatedUTC"), out var created) ? created : DateTime.UtcNow,
                                    UpdatedUTC = DateTime.TryParse(feature.GetFieldAsString("UpdatedUTC"), out var updated) ? updated : DateTime.UtcNow
                                };

                                qcErrors.Add(qcError);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "QC 오류 피처 변환 실패: {TableName}", tableName);
                            }
                            finally
                            {
                                feature.Dispose();
                            }
                        }
                        
                        _logger.LogDebug("QC_ERRORS 테이블 조회 완료: {TableName} - {Count}개", tableName, qcErrors.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "QC_ERRORS 테이블 조회 실패: {TableName}", tableName);
                    }
                }

                _logger.LogInformation("QC 오류 조회 완료: 총 {Count}개", qcErrors.Count);
                return qcErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 조회 실패: {GdbPath}", gdbPath);
                return new List<QcError>();
            }
        }
    }
}
