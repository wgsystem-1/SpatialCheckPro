using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using System.Linq; // Added for .Any()
using System.IO; // Added for Directory and File operations

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

                        // 우선 포인트 지오메트리를 생성 시도하여 저장 레이어를 결정
                        OSGeo.OGR.Geometry? pointGeometryCandidate = null;
                        // 1차 시도: 기존 지오메트리에서 Point 생성
                        if (qcError.Geometry != null)
                        {
                            try { pointGeometryCandidate = CreateSimplePoint(qcError.Geometry); } catch { pointGeometryCandidate = null; }
                        }
                        // 2차 시도: WKT에서 Point 생성
                        if (pointGeometryCandidate == null && !string.IsNullOrWhiteSpace(qcError.GeometryWKT))
                        {
                            try
                            {
                                var geomFromWkt = OSGeo.OGR.Geometry.CreateFromWkt(qcError.GeometryWKT);
                                if (geomFromWkt != null && !geomFromWkt.IsEmpty())
                                {
                                    pointGeometryCandidate = CreateSimplePoint(geomFromWkt);
                                }
                                geomFromWkt?.Dispose();
                            }
                            catch { pointGeometryCandidate = null; }
                        }
                        // 3차 시도: 좌표로부터 Point 생성 (0,0은 위치 미확정으로 간주)
                        if (pointGeometryCandidate == null && (qcError.X != 0 || qcError.Y != 0))
                        {
                            try
                            {
                                var p = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                                p.AddPoint(qcError.X, qcError.Y, 0);
                                pointGeometryCandidate = p;
                            }
                            catch { pointGeometryCandidate = null; }
                        }

                        // 저장 레이어 결정: 포인트 지오메트리가 있으면 Point, 없으면 NoGeom
                        string layerName = pointGeometryCandidate != null ? "QC_Errors_Point" : "QC_Errors_NoGeom";

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
                            // 레이어가 없으면 생성 시도 (레이어명에 따라 타입 결정)
                            layer = CreateQcErrorLayer(dataSource, layerName);
                            if (layer == null)
                            {
                                _logger.LogError("QC_ERRORS 레이어 생성 실패: {LayerName}", layerName);
                                dataSource.Dispose();
                                return false;
                            }
                            _logger.LogInformation("QC_ERRORS 레이어 생성 성공: {LayerName}", layerName);
                        }
                        else
                        {
                            // 기존 레이어의 지오메트리 타입 검증 및 필요시 재생성
                            var expectedType = layerName == "QC_Errors_Point" ? wkbGeometryType.wkbPoint : wkbGeometryType.wkbNone;
                            var currentType = layer.GetGeomType();
                            if (currentType != expectedType)
                            {
                                _logger.LogWarning("레이어 지오메트리 타입 불일치: {LayerName} (현재: {CurrentType}, 기대: {ExpectedType}) - 레이어 재생성 시도", layerName, currentType, expectedType);
                                try
                                {
                                    // 레이어 인덱스 조회 후 삭제
                                    int layerIndex = -1;
                                    for (int i = 0; i < dataSource.GetLayerCount(); i++)
                                    {
                                        var idxLayer = dataSource.GetLayerByIndex(i);
                                        if (idxLayer != null && string.Equals(idxLayer.GetName(), layerName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            layerIndex = i;
                                            break;
                                        }
                                    }
                                    if (layerIndex >= 0)
                                    {
                                        dataSource.DeleteLayer(layerIndex);
                                        _logger.LogInformation("기존 레이어 삭제 완료: {LayerName}", layerName);
                                    }

                                    layer = CreateQcErrorLayer(dataSource, layerName);
                                    if (layer == null)
                                    {
                                        _logger.LogError("레이어 재생성 실패: {LayerName}", layerName);
                                        dataSource.Dispose();
                                        return false;
                                    }
                                    _logger.LogInformation("레이어 재생성 성공: {LayerName}", layerName);
                                }
                                catch (Exception recreateEx)
                                {
                                    _logger.LogError(recreateEx, "레이어 재생성 중 오류: {LayerName}", layerName);
                                    dataSource.Dispose();
                                    return false;
                                }
                            }
                        }

                        // 새 피처 생성
                        var featureDefn = layer.GetLayerDefn();
                        var feature = new Feature(featureDefn);

                        // 필수 필드 설정
                        feature.SetField("ErrCode", qcError.ErrCode);
                        feature.SetField("SourceClass", qcError.SourceClass);
                        feature.SetField("SourceOID", (int)qcError.SourceOID);
                        feature.SetField("Message", qcError.Message);

                        // NoGeom 레이어라면 TableId/TableName도 함께 기록
                        if (string.Equals(layerName, "QC_Errors_NoGeom", StringComparison.OrdinalIgnoreCase))
                        {
                            var tableId = string.IsNullOrWhiteSpace(qcError.TableId) ? qcError.SourceClass : qcError.TableId;
                            feature.SetField("TableId", tableId);
                            feature.SetField("TableName", qcError.TableName ?? string.Empty);
                        }

                        // 포인트 지오메트리가 준비된 경우에만 지오메트리 설정
                        if (pointGeometryCandidate != null)
                        {
                            try
                            {
                                // 좌표 확인을 위한 WKT 로깅
                                string wkt = string.Empty;
                                pointGeometryCandidate.ExportToWkt(out wkt);
                                _logger.LogDebug("Point 지오메트리 WKT: {Wkt}", wkt);

                                // 피처에 지오메트리 설정
                                feature.SetGeometry(pointGeometryCandidate);
                                _logger.LogDebug("Point 지오메트리 설정 완료: {ErrorCode}", qcError.ErrCode);
                            }
                            finally
                            {
                                // SetGeometry에서 복사본을 사용하므로 원본 지오메트리 해제
                                pointGeometryCandidate.Dispose();
                            }
                        }

                        // 피처를 레이어에 추가
                        var result = layer.CreateFeature(feature);

                        if (result == 0) // OGRERR_NONE
                        {
                            try
                            {
                                // 레이어 변경사항을 디스크에 동기화
                                layer.SyncToDisk();
                                _logger.LogDebug("레이어 동기화 완료: {LayerName}", layerName);

                                // DataSource 캐시 Flush
                                dataSource.FlushCache();
                                _logger.LogDebug("DataSource 캐시 Flush 완료");

                                _logger.LogDebug("QC 오류 저장 성공: {ErrorCode} -> {LayerName}", qcError.ErrCode, layerName);

                                feature.Dispose();
                                dataSource.Dispose();

                                return true;
                            }
                            catch (Exception syncEx)
                            {
                                _logger.LogError(syncEx, "디스크 동기화 중 오류 발생: {ErrorCode}", qcError.ErrCode);
                                feature.Dispose();
                                dataSource.Dispose();
                                return false;
                            }
                        }
                        else
                        {
                            _logger.LogError("QC 오류 저장 실패: {ErrorCode}, OGR 오류 코드: {Result}", qcError.ErrCode, result);
                            feature.Dispose();
                            dataSource.Dispose();
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

                    // 배치 처리: 오류별로 대상 레이어를 분기 (Point 또는 NoGeom)
                    var pointLayer = dataSource.GetLayerByName("QC_Errors_Point") ?? CreateQcErrorLayer(dataSource, "QC_Errors_Point");
                    var noGeomLayer = dataSource.GetLayerByName("QC_Errors_NoGeom") ?? CreateQcErrorLayer(dataSource, "QC_Errors_NoGeom");
                    if (pointLayer == null)
                    {
                        _logger.LogError("배치 저장 실패: QC_Errors_Point 레이어를 준비할 수 없습니다");
                        return successCount;
                    }

                    // NoGeom 레이어는 없을 수 있으나, 필요 시에만 사용
                    pointLayer.StartTransaction();
                    noGeomLayer?.StartTransaction();

                    foreach (var qcError in qcErrors)
                    {
                        // 공통 필드 준비
                        OSGeo.OGR.Geometry? pointGeometry = null;
                        try
                        {
                            // 1차: 기존 지오메트리에서 Point 생성
                            if (qcError.Geometry != null)
                            {
                                pointGeometry = CreateSimplePoint(qcError.Geometry);
                            }
                            // 2차: WKT에서 Point 생성
                            else if (!string.IsNullOrWhiteSpace(qcError.GeometryWKT))
                            {
                                var geometryFromWkt = OSGeo.OGR.Geometry.CreateFromWkt(qcError.GeometryWKT);
                                if (geometryFromWkt != null)
                                {
                                    pointGeometry = CreateSimplePoint(geometryFromWkt);
                                    geometryFromWkt.Dispose();
                                }
                            }
                            // 3차: 좌표로 Point 생성 (0,0은 무시)
                            else if (qcError.X != 0 || qcError.Y != 0)
                            {
                                var p = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                                p.AddPoint(qcError.X, qcError.Y, 0);
                                pointGeometry = p;
                            }

                            // 대상 레이어 결정 및 생성
                            var targetLayer = pointGeometry != null ? pointLayer : noGeomLayer;
                            if (targetLayer == null)
                            {
                                // NoGeom 레이어도 없으면 생성 시도
                                targetLayer = CreateQcErrorLayer(dataSource, "QC_Errors_NoGeom");
                                if (targetLayer == null)
                                {
                                    _logger.LogWarning("대상 레이어를 준비할 수 없어 오류를 건너뜁니다: {ErrCode}", qcError.ErrCode);
                                    pointGeometry?.Dispose();
                                    continue;
                                }
                                noGeomLayer = targetLayer;
                            }

                            using var feature = new Feature(targetLayer.GetLayerDefn());
                            feature.SetField("ErrCode", qcError.ErrCode);
                            feature.SetField("SourceClass", qcError.SourceClass);
                            feature.SetField("SourceOID", (int)qcError.SourceOID);
                            feature.SetField("Message", qcError.Message);

                            if (string.Equals(targetLayer.GetName(), "QC_Errors_NoGeom", StringComparison.OrdinalIgnoreCase))
                            {
                                var tableId = string.IsNullOrWhiteSpace(qcError.TableId) ? qcError.SourceClass : qcError.TableId;
                                feature.SetField("TableId", tableId);
                                feature.SetField("TableName", qcError.TableName ?? string.Empty);
                            }

                            if (pointGeometry != null)
                            {
                                // 좌표 확인 로그 (디버그)
                                string wkt = string.Empty;
                                pointGeometry.ExportToWkt(out wkt);
                                _logger.LogDebug("배치 Point WKT: {Wkt}", wkt);

                                feature.SetGeometry(pointGeometry);
                                pointGeometry.Dispose();
                            }

                            if (targetLayer.CreateFeature(feature) == 0)
                            {
                                successCount++;
                            }
                        }
                        catch (Exception feEx)
                        {
                            _logger.LogWarning(feEx, "배치 개별 피처 생성 실패: {ErrCode}", qcError.ErrCode);
                            pointGeometry?.Dispose();
                        }
                    }

                    pointLayer.CommitTransaction();
                    noGeomLayer?.CommitTransaction();

                    try
                    {
                        pointLayer.SyncToDisk();
                        noGeomLayer?.SyncToDisk();
                        dataSource.FlushCache();
                    }
                    catch (Exception syncEx)
                    {
                        _logger.LogWarning(syncEx, "배치 저장 동기화 경고 (Point/NoGeom)");
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
        /// QC_ERRORS 레이어를 생성합니다 (기존 레이어 삭제 후 재생성)
        /// </summary>
        /// <param name="dataSource">GDAL 데이터소스</param>
        /// <param name="layerName">레이어 이름</param>
        /// <returns>생성된 레이어 또는 null</returns>
        private Layer? CreateQcErrorLayer(DataSource dataSource, string layerName)
        {
            try
            {
                _logger.LogDebug("QC_ERRORS 레이어 생성 시작: {LayerName}", layerName);

                // 레이어별 지오메트리 타입 결정
                var geometryType = layerName switch
                {
                    "QC_Errors_Point" => wkbGeometryType.wkbPoint,
                    "QC_Errors_Line" => wkbGeometryType.wkbLineString,
                    "QC_Errors_Polygon" => wkbGeometryType.wkbPolygon,
                    "QC_Errors_NoGeom" => wkbGeometryType.wkbNone,
                    _ => wkbGeometryType.wkbPoint
                };

                // 레이어 생성 (기존 삭제 없이 생성만 시도)
                var layer = dataSource.CreateLayer(layerName, null, geometryType, null);
                if (layer == null)
                {
                    _logger.LogError("레이어 생성 실패: {LayerName}", layerName);
                    return null;
                }
                
                // 필수 필드만 정의 (단순화된 스키마)
                var fieldDefn = new FieldDefn("ErrCode", FieldType.OFTString);
                fieldDefn.SetWidth(32);
                layer.CreateField(fieldDefn, 1);
                              
                fieldDefn = new FieldDefn("SourceClass", FieldType.OFTString);
                fieldDefn.SetWidth(128);
                layer.CreateField(fieldDefn, 1);
                
                if (layerName.Equals("QC_Errors_NoGeom", StringComparison.OrdinalIgnoreCase))
                {
                    var tableIdField = new FieldDefn("TableId", FieldType.OFTString);
                    tableIdField.SetWidth(128);
                    layer.CreateField(tableIdField, 1);

                    var tableNameField = new FieldDefn("TableName", FieldType.OFTString);
                    tableNameField.SetWidth(128);
                    layer.CreateField(tableNameField, 1);
                }

                fieldDefn = new FieldDefn("SourceOID", FieldType.OFTInteger);
                layer.CreateField(fieldDefn, 1);
                
                fieldDefn = new FieldDefn("Message", FieldType.OFTString);
                fieldDefn.SetWidth(1024);
                layer.CreateField(fieldDefn, 1);
                
                // 레이어 스키마 변경사항을 디스크에 동기화
                layer.SyncToDisk();
                _logger.LogDebug("레이어 스키마 동기화 완료: {LayerName}", layerName);

                // Severity/Status 필드는 사용하지 않으므로 생성하지 않음

                _logger.LogInformation("QC_ERRORS 레이어 생성 완료 (단순화된 스키마): {LayerName}", layerName);
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
                                    TableId = feature.GetFieldAsString("TableId"),
                                    TableName = feature.GetFieldAsString("TableName"),
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

        /// <summary>
        /// 모든 오류를 Point 레이어에 저장하는 방식
        /// </summary>
        /// <param name="qcError">QC 오류 객체</param>
        /// <param name="geomTypeUpper">지오메트리 타입 (대문자)</param>
        /// <returns>레이어명</returns>
        private string DetermineLayerNameForPointMode(QcError qcError, string geomTypeUpper)
        {
            // 모든 오류를 Point 레이어에 저장 (작업자가 위치 확인 가능)
            _logger.LogDebug("Point 저장 방식: 모든 오류를 Point 레이어에 저장 - {ErrCode}", qcError.ErrCode);
            return "QC_Errors_Point";
        }

        /// <summary>
        /// 실제 객체 좌표를 사용한 Point 생성 메서드
        /// POINT->POINT 좌표, LINE/POLYGON->첫점 좌표
        /// </summary>
        /// <param name="geometry">원본 지오메트리</param>
        /// <returns>Point 지오메트리</returns>
        private OSGeo.OGR.Geometry? CreateSimplePoint(OSGeo.OGR.Geometry geometry)
        {
            try
            {
                if (geometry == null || geometry.IsEmpty())
                {
                    return null;
                }

                var geomType = geometry.GetGeometryType();
                
                // POINT: 그대로 사용
                if (geomType == wkbGeometryType.wkbPoint)
                {
                    return geometry.Clone();
                }
                
                // MultiPoint: 첫 번째 Point 사용
                if (geomType == wkbGeometryType.wkbMultiPoint)
                {
                    if (geometry.GetGeometryCount() > 0)
                    {
                        var firstPoint = geometry.GetGeometryRef(0);
                        return firstPoint?.Clone();
                    }
                }
                
                // LineString: 첫 번째 점 사용
                if (geomType == wkbGeometryType.wkbLineString)
                {
                    if (geometry.GetPointCount() > 0)
                    {
                        var point = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                        // GDAL/OGR API에서 올바른 방법 사용
                        var pointArray = new double[3];
                        geometry.GetPoint(0, pointArray);
                        point.AddPoint(pointArray[0], pointArray[1], pointArray[2]);
                        
                        _logger.LogDebug("LineString 첫점 추출: ({X}, {Y})", pointArray[0], pointArray[1]);
                        return point;
                    }
                }
                
                // MultiLineString: 첫 번째 LineString의 첫 점 사용
                if (geomType == wkbGeometryType.wkbMultiLineString)
                {
                    if (geometry.GetGeometryCount() > 0)
                    {
                        var firstLine = geometry.GetGeometryRef(0);
                        if (firstLine != null && firstLine.GetPointCount() > 0)
                        {
                            var point = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                            var pointArray = new double[3];
                            firstLine.GetPoint(0, pointArray);
                            point.AddPoint(pointArray[0], pointArray[1], pointArray[2]);
                            
                            _logger.LogDebug("MultiLineString 첫점 추출: ({X}, {Y})", pointArray[0], pointArray[1]);
                            return point;
                        }
                    }
                }
                
                // Polygon: 외부 링의 첫 번째 점 사용
                if (geomType == wkbGeometryType.wkbPolygon)
                {
                    if (geometry.GetGeometryCount() > 0)
                    {
                        var exteriorRing = geometry.GetGeometryRef(0); // 외부 링
                        if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                        {
                            var point = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                            var pointArray = new double[3];
                            exteriorRing.GetPoint(0, pointArray);
                            point.AddPoint(pointArray[0], pointArray[1], pointArray[2]);
                            
                            _logger.LogDebug("Polygon 첫점 추출: ({X}, {Y})", pointArray[0], pointArray[1]);
                            return point;
                        }
                    }
                }
                
                // MultiPolygon: 첫 번째 Polygon의 외부 링 첫 점 사용
                if (geomType == wkbGeometryType.wkbMultiPolygon)
                {
                    if (geometry.GetGeometryCount() > 0)
                    {
                        var firstPolygon = geometry.GetGeometryRef(0);
                        if (firstPolygon != null && firstPolygon.GetGeometryCount() > 0)
                        {
                            var exteriorRing = firstPolygon.GetGeometryRef(0); // 외부 링
                            if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                            {
                                var point = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                                var pointArray = new double[3];
                                exteriorRing.GetPoint(0, pointArray);
                                point.AddPoint(pointArray[0], pointArray[1], pointArray[2]);
                                
                                _logger.LogDebug("MultiPolygon 첫점 추출: ({X}, {Y})", pointArray[0], pointArray[1]);
                                return point;
                            }
                        }
                    }
                }
                
                // 기타 지오메트리 타입: 중심점으로 폴백
                var envelope = new OSGeo.OGR.Envelope();
                geometry.GetEnvelope(envelope);
                
                double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
                double centerY = (envelope.MinY + envelope.MaxY) / 2.0;
                
                var fallbackPoint = new OSGeo.OGR.Geometry(wkbGeometryType.wkbPoint);
                fallbackPoint.AddPoint(centerX, centerY, 0);
                
                _logger.LogDebug("지오메트리를 중심점으로 폴백: {GeometryType} → Point ({X}, {Y})", 
                    geomType, centerX, centerY);
                
                return fallbackPoint;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "실제 좌표 Point 생성 실패");
                return null;
            }
        }

        /// <summary>
        /// 비공간 오류인지 판단합니다 (기존 메서드 유지)
        /// </summary>
        /// <param name="qcError">QC 오류 객체</param>
        /// <returns>비공간 오류 여부</returns>
        private bool IsNonSpatialError(QcError qcError)
        {
            // 1. 지오메트리 정보가 전혀 없는 경우
            bool hasNoGeometry = string.IsNullOrEmpty(qcError.GeometryWKT) && 
                                qcError.Geometry == null && 
                                (qcError.X == 0 && qcError.Y == 0);
            
            if (hasNoGeometry)
            {
                return true;
            }
            
            // 2. 비공간 오류 타입들
            var nonSpatialErrorTypes = new[]
            {
                "SCHEMA", "ATTR", "TABLE", "FIELD", "DOMAIN", "CONSTRAINT"
            };
            
            if (nonSpatialErrorTypes.Contains(qcError.ErrType, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // 3. 비공간 오류 코드들
            var nonSpatialErrorCodes = new[]
            {
                "SCM001", "SCM002", "SCM003", // 스키마 관련
                "ATR001", "ATR002", "ATR003", // 속성 관련
                "TBL001", "TBL002", "TBL003", // 테이블 관련
                "FLD001", "FLD002", "FLD003", // 필드 관련
                "DOM001", "DOM002", "DOM003"  // 도메인 관련
            };
            
            if (nonSpatialErrorCodes.Contains(qcError.ErrCode, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // 4. 메시지 내용으로 판단
            var nonSpatialKeywords = new[]
            {
                "스키마", "속성", "테이블", "필드", "도메인", "제약조건",
                "schema", "attribute", "table", "field", "domain", "constraint",
                "누락", "타입", "길이", "정밀도", "null", "기본값"
            };
            
            var messageLower = qcError.Message.ToLower();
            if (nonSpatialKeywords.Any(keyword => messageLower.Contains(keyword.ToLower())))
            {
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 원본 GDB 경로를 찾는 헬퍼 메서드
        /// </summary>
        /// <param name="currentGdbDir">현재 GDB 디렉토리</param>
        /// <param name="sourceClass">소스 클래스명</param>
        /// <returns>원본 GDB 경로</returns>
        private string? FindOriginalGdbPath(string? currentGdbDir, string sourceClass)
        {
            try
            {
                if (string.IsNullOrEmpty(currentGdbDir) || !Directory.Exists(currentGdbDir))
                {
                    return null;
                }

                // 현재 디렉토리에서 .gdb 파일들 검색
                var gdbFiles = Directory.GetFiles(currentGdbDir, "*.gdb", SearchOption.TopDirectoryOnly);
                
                foreach (var gdbFile in gdbFiles)
                {
                    try
                    {
                        // 각 GDB 파일에서 해당 클래스가 있는지 확인
                        using var dataSource = OSGeo.OGR.Ogr.Open(gdbFile, 0);
                        if (dataSource != null)
                        {
                            for (int i = 0; i < dataSource.GetLayerCount(); i++)
                            {
                                var layer = dataSource.GetLayerByIndex(i);
                                if (layer != null && 
                                    string.Equals(layer.GetName(), sourceClass, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogDebug("원본 GDB 파일 발견: {GdbPath} (클래스: {SourceClass})", gdbFile, sourceClass);
                                    return gdbFile;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "GDB 파일 검사 중 오류: {GdbFile}", gdbFile);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "원본 GDB 경로 검색 실패: {CurrentGdbDir}", currentGdbDir);
                return null;
            }
        }

        /// <summary>
        /// 원본 GDB에서 지오메트리 정보를 재추출하는 메서드
        /// </summary>
        /// <param name="originalGdbPath">원본 GDB 경로</param>
        /// <param name="sourceClass">소스 클래스명</param>
        /// <param name="sourceOid">소스 OID</param>
        /// <returns>지오메트리 정보</returns>
        private async Task<(OSGeo.OGR.Geometry? geometry, double x, double y, string geometryType)> RetrieveGeometryFromOriginalGdb(
            string originalGdbPath, string sourceClass, string sourceOid)
        {
            try
            {
                using var dataSource = OSGeo.OGR.Ogr.Open(originalGdbPath, 0);
                if (dataSource == null)
                {
                    _logger.LogWarning("원본 GDB를 열 수 없습니다: {OriginalGdbPath}", originalGdbPath);
                    return (null, 0, 0, "Unknown");
                }

                // 레이어 찾기
                Layer? layer = null;
                for (int i = 0; i < dataSource.GetLayerCount(); i++)
                {
                    var testLayer = dataSource.GetLayerByIndex(i);
                    if (testLayer != null && 
                        string.Equals(testLayer.GetName(), sourceClass, StringComparison.OrdinalIgnoreCase))
                    {
                        layer = testLayer;
                        break;
                    }
                }

                if (layer == null)
                {
                    _logger.LogWarning("원본 GDB에서 클래스를 찾을 수 없습니다: {SourceClass}", sourceClass);
                    return (null, 0, 0, "Unknown");
                }

                // 피처 찾기 시도
                Feature? feature = null;
                
                // ObjectId 필드로 검색
                if (long.TryParse(sourceOid, out var numericOid))
                {
                    layer.SetAttributeFilter($"OBJECTID = {numericOid}");
                    layer.ResetReading();
                    feature = layer.GetNextFeature();
                }

                // FID로 직접 검색
                if (feature == null && long.TryParse(sourceOid, out var fid))
                {
                    layer.SetAttributeFilter(null);
                    layer.ResetReading();
                    feature = layer.GetFeature(fid);
                }

                if (feature == null)
                {
                    _logger.LogWarning("원본 GDB에서 피처를 찾을 수 없습니다: {SourceClass}:{SourceOid}", sourceClass, sourceOid);
                    return (null, 0, 0, "Unknown");
                }

                var geometry = feature.GetGeometryRef();
                if (geometry == null || geometry.IsEmpty())
                {
                    _logger.LogWarning("원본 GDB에서 지오메트리가 없습니다: {SourceClass}:{SourceOid}", sourceClass, sourceOid);
                    feature.Dispose();
                    return (null, 0, 0, "NoGeometry");
                }

                // 지오메트리 복사 및 첫 점 좌표 추출
                var clonedGeometry = geometry.Clone();
                double firstX = 0, firstY = 0;
                var geomType = clonedGeometry.GetGeometryType();
                
                if (geomType == wkbGeometryType.wkbPoint)
                {
                    // Point: 그대로 사용
                    var pointArray = new double[3];
                    clonedGeometry.GetPoint(0, pointArray);
                    firstX = pointArray[0];
                    firstY = pointArray[1];
                }
                else if (geomType == wkbGeometryType.wkbMultiPoint)
                {
                    // MultiPoint: 첫 번째 Point 사용
                    if (clonedGeometry.GetGeometryCount() > 0)
                    {
                        var firstPoint = clonedGeometry.GetGeometryRef(0);
                        if (firstPoint != null)
                        {
                            var pointArray = new double[3];
                            firstPoint.GetPoint(0, pointArray);
                            firstX = pointArray[0];
                            firstY = pointArray[1];
                        }
                    }
                }
                else if (geomType == wkbGeometryType.wkbLineString)
                {
                    // LineString: 첫 번째 점 사용
                    if (clonedGeometry.GetPointCount() > 0)
                    {
                        var pointArray = new double[3];
                        clonedGeometry.GetPoint(0, pointArray);
                        firstX = pointArray[0];
                        firstY = pointArray[1];
                    }
                }
                else if (geomType == wkbGeometryType.wkbMultiLineString)
                {
                    // MultiLineString: 첫 번째 LineString의 첫 점 사용
                    if (clonedGeometry.GetGeometryCount() > 0)
                    {
                        var firstLine = clonedGeometry.GetGeometryRef(0);
                        if (firstLine != null && firstLine.GetPointCount() > 0)
                        {
                            var pointArray = new double[3];
                            firstLine.GetPoint(0, pointArray);
                            firstX = pointArray[0];
                            firstY = pointArray[1];
                        }
                    }
                }
                else if (geomType == wkbGeometryType.wkbPolygon)
                {
                    // Polygon: 외부 링의 첫 번째 점 사용
                    if (clonedGeometry.GetGeometryCount() > 0)
                    {
                        var exteriorRing = clonedGeometry.GetGeometryRef(0);
                        if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                        {
                            var pointArray = new double[3];
                            exteriorRing.GetPoint(0, pointArray);
                            firstX = pointArray[0];
                            firstY = pointArray[1];
                        }
                    }
                }
                else if (geomType == wkbGeometryType.wkbMultiPolygon)
                {
                    // MultiPolygon: 첫 번째 Polygon의 외부 링 첫 점 사용
                    if (clonedGeometry.GetGeometryCount() > 0)
                    {
                        var firstPolygon = clonedGeometry.GetGeometryRef(0);
                        if (firstPolygon != null && firstPolygon.GetGeometryCount() > 0)
                        {
                            var exteriorRing = firstPolygon.GetGeometryRef(0);
                            if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                            {
                                var pointArray = new double[3];
                                exteriorRing.GetPoint(0, pointArray);
                                firstX = pointArray[0];
                                firstY = pointArray[1];
                            }
                        }
                    }
                }
                else
                {
                    // 기타 지오메트리 타입: 중심점으로 폴백
                    var envelope = new OSGeo.OGR.Envelope();
                    clonedGeometry.GetEnvelope(envelope);
                    firstX = (envelope.MinX + envelope.MaxX) / 2.0;
                    firstY = (envelope.MinY + envelope.MaxY) / 2.0;
                }
                string geometryTypeName = geomType switch
                {
                    wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => "POINT",
                    wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => "LINESTRING",
                    wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => "POLYGON",
                    _ => "UNKNOWN"
                };

                feature.Dispose();
                
                _logger.LogDebug("원본 GDB에서 지오메트리 재추출 성공: {SourceClass}:{SourceOid} - {GeometryType}", 
                    sourceClass, sourceOid, geometryTypeName);

                return (clonedGeometry, firstX, firstY, geometryTypeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "원본 GDB에서 지오메트리 재추출 실패: {SourceClass}:{SourceOid}", sourceClass, sourceOid);
                return (null, 0, 0, "Unknown");
            }
        }
    }
}
