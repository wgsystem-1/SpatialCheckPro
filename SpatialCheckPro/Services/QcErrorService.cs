using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC 오류 관리 서비스
    /// </summary>
    public class QcErrorService
    {
        private readonly ILogger<QcErrorService> _logger;
        private readonly QcErrorDataService _dataService;
        private readonly FgdbSchemaService _schemaService;
        private readonly QcStoragePathService _pathService;

        private string? _currentQcGdbPath;
        private string? _currentRunId;
        private string? _currentSourceGdbPath;

        private static bool _isGdalInitialized = false;
        private static readonly object _gdalLock = new object();

        public QcErrorService(
            ILogger<QcErrorService> logger, 
            QcErrorDataService dataService, 
            FgdbSchemaService schemaService,
            QcStoragePathService pathService)
        {
            _logger = logger;
            _dataService = dataService;
            _schemaService = schemaService;
            _pathService = pathService;
        }

        /// <summary>
        /// 새로운 검수 실행을 시작하고 QC 저장소를 초기화합니다.
        /// </summary>
        /// <param name="run">QC_Runs에 기록될 메타데이터</param>
        /// <param name="targetGdbPath">검수 대상 원본 GDB 경로</param>
        /// <returns>생성된 QC GDB 경로와 RunID</returns>
        public async Task<(string qcGdbPath, string runId)> BeginRunAsync(QcRun run, string targetGdbPath)
        {
            _logger.LogInformation("새로운 검수 실행 시작: {RunName}", run.RunName);

            // 1. QC용 GDB 경로 생성
            _currentQcGdbPath = _pathService.BuildQcGdbPath(targetGdbPath);
            _currentSourceGdbPath = targetGdbPath;
            _logger.LogInformation("QC 결과 저장 경로: {QcGdbPath}", _currentQcGdbPath);

            // 2. 해당 GDB에 스키마 보장 (원본 좌표계 복제)
            var schemaResult = await _schemaService.CreateQcErrorsSchemaAsync(_currentQcGdbPath, _currentSourceGdbPath);
            if (!schemaResult)
            {
                throw new InvalidOperationException("QC GDB에 스키마를 생성하지 못했습니다.");
            }

            // 3. QC_Runs 테이블에 레코드 생성 및 RunID 반환
            run.Status = "Running";
            run.StartTimeUTC = DateTime.UtcNow;
            _currentRunId = await _dataService.CreateQcRunAsync(_currentQcGdbPath, run);
            if (string.IsNullOrEmpty(_currentRunId))
            {
                throw new InvalidOperationException("QC_Runs 테이블에 실행 정보를 기록하지 못했습니다.");
            }
            
            _logger.LogInformation("새로운 RunID 생성: {RunId}", _currentRunId);
            return (_currentQcGdbPath, _currentRunId);
        }

        /// <summary>
        /// 진행 중인 검수 실행을 최종 결과로 업데이트합니다.
        /// </summary>
        public async Task EndRunAsync(int totalErrors, int totalWarnings, string summary, bool success)
        {
            if (string.IsNullOrEmpty(_currentQcGdbPath) || string.IsNullOrEmpty(_currentRunId))
            {
                _logger.LogWarning("진행 중인 검수 정보가 없어 EndRun을 실행할 수 없습니다.");
                return;
            }

            _logger.LogInformation("검수 실행 종료 처리: {RunId}", _currentRunId);
            await _dataService.UpdateQcRunStatusAsync(_currentQcGdbPath, _currentRunId, 
                success ? "Completed" : "Failed", totalErrors, totalWarnings, summary);
        }

        /// <summary>
        /// GDAL 초기화 상태를 확인하고 필요시 재초기화합니다
        /// </summary>
        private void EnsureGdalInitialized()
        {
            // 이미 초기화되었으면 빠르게 반환
            if (_isGdalInitialized)
                return;

            lock (_gdalLock)
            {
                // 다시 확인 (다른 스레드가 초기화했을 수 있음)
                if (_isGdalInitialized)
                    return;

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
                    
                    _isGdalInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GDAL 초기화 상태 확인 중 오류 발생");
                    _logger.LogWarning("GDAL 초기화 오류를 무시하고 계속 진행합니다");
                    // 오류가 발생해도 다시 시도하지 않도록 표시
                    _isGdalInitialized = true;
                }
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
        /// QC 오류 목록을 조회합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="runId">실행 ID (선택사항)</param>
        /// <returns>QC 오류 목록</returns>
        public async Task<List<QcError>> GetQcErrorsAsync(string gdbPath, string? runId = null)
        {
            try
            {
                return await _dataService.GetQcErrorsAsync(gdbPath, runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 조회 실패: {GdbPath}", gdbPath);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// QC 오류 상태를 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="errorId">오류 ID</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateErrorStatusAsync(string gdbPath, string errorId, string newStatus)
        {
            try
            {
                return await _dataService.UpdateQcErrorStatusAsync(gdbPath, errorId, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 상태 업데이트 실패: {ErrorId}", errorId);
                return false;
            }
        }

        /// <summary>
        /// QC 오류 담당자를 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="errorId">오류 ID</param>
        /// <param name="assignee">담당자</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateErrorAssigneeAsync(string gdbPath, string errorId, string assignee)
        {
            try
            {
                return await _dataService.UpdateQcErrorAssigneeAsync(gdbPath, errorId, assignee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 담당자 업데이트 실패: {ErrorId}", errorId);
                return false;
            }
        }

        /// <summary>
        /// QC 오류 심각도를 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="errorId">오류 ID</param>
        /// <param name="severity">심각도</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateErrorSeverityAsync(string gdbPath, string errorId, string severity)
        {
            try
            {
                return await _dataService.UpdateQcErrorSeverityAsync(gdbPath, errorId, severity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 심각도 업데이트 실패: {ErrorId}", errorId);
                return false;
            }
        }

        /// <summary>
        /// QC_ERRORS 데이터베이스를 초기화합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> InitializeQcErrorsDatabaseAsync(string gdbPath)
        {
            try
            {
                return await _dataService.InitializeQcErrorsDatabaseAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 데이터베이스 초기화 실패: {GdbPath}", gdbPath);
                return false;
            }
        }

        /// <summary>
        /// QC_ERRORS 스키마 유효성을 검사합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <returns>스키마 유효성 여부</returns>
        public async Task<bool> ValidateQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                return await _dataService.ValidateQcErrorsSchemaAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 검증 실패: {GdbPath}", gdbPath);
                return false;
            }
        }

        /// <summary>
        /// 손상된 QC_ERRORS 스키마를 자동 복구합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <returns>복구 성공 여부</returns>
        public async Task<bool> RepairQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 스키마 복구 시작: {GdbPath}", gdbPath);
                return await _dataService.RepairQcErrorsSchemaAsync(gdbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 복구 실패: {GdbPath}", gdbPath);
                return false;
            }
        }

        /// <summary>
        /// 특정 위치에서 허용 거리 내의 오류들을 검색합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="tolerance">허용 거리 (미터)</param>
        /// <returns>검색된 오류 목록 (거리순 정렬)</returns>
        public async Task<List<QcError>> SearchErrorsAtLocationAsync(string gdbPath, double x, double y, double tolerance)
        {
            try
            {
                _logger.LogDebug("위치 기반 오류 검색: ({X}, {Y}), 허용거리={Tolerance}m", x, y, tolerance);
                return await _dataService.SearchErrorsAtLocationAsync(gdbPath, x, y, tolerance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "위치 기반 오류 검색 실패: ({X}, {Y})", x, y);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// 특정 영역 내의 오류들을 검색합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <returns>검색된 오류 목록</returns>
        public async Task<List<QcError>> SearchErrorsInBoundsAsync(string gdbPath, double minX, double minY, double maxX, double maxY)
        {
            try
            {
                _logger.LogDebug("영역 기반 오류 검색: ({MinX}, {MinY}) - ({MaxX}, {MaxY})", minX, minY, maxX, maxY);
                return await _dataService.SearchErrorsInBoundsAsync(gdbPath, minX, minY, maxX, maxY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "영역 기반 오류 검색 실패");
                return new List<QcError>();
            }
        }

        /// <summary>
        /// 오류 ID로 특정 오류를 검색합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="errorId">오류 ID</param>
        /// <returns>검색된 오류 (없으면 null)</returns>
        public async Task<QcError?> GetErrorByIdAsync(string gdbPath, string errorId)
        {
            try
            {
                return await _dataService.GetQcErrorByIdAsync(gdbPath, errorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 ID 검색 실패: {ErrorId}", errorId);
                return null;
            }
        }

        /// <summary>
        /// 다중 오류 상태를 일괄 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="errorIds">업데이트할 오류 ID 목록</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateMultipleErrorsAsync(string gdbPath, List<string> errorIds, string newStatus)
        {
            try
            {
                _logger.LogInformation("다중 오류 상태 업데이트 시작: {Count}개 -> {NewStatus}", errorIds.Count, newStatus);

                var successCount = 0;
                foreach (var errorId in errorIds)
                {
                    var success = await UpdateErrorStatusAsync(gdbPath, errorId, newStatus);
                    if (success)
                    {
                        successCount++;
                    }
                }

                var allSuccess = successCount == errorIds.Count;
                _logger.LogInformation("다중 오류 상태 업데이트 완료: {Success}/{Total}", successCount, errorIds.Count);
                
                return allSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "다중 오류 상태 업데이트 실패");
                return false;
            }
        }

        /// <summary>
        /// 다중 오류 상태를 일괄 업데이트합니다 (gdbPath 없는 버전)
        /// </summary>
        /// <param name="errorIds">업데이트할 오류 ID 목록</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateMultipleErrorsAsync(List<string> errorIds, string newStatus)
        {
            return await UpdateMultipleErrorsAsync("", errorIds, newStatus);
        }

        /// <summary>
        /// QC 오류 객체를 사용하여 상태를 업데이트합니다 (ErrorFeatureStatusService 호환용)
        /// </summary>
        /// <param name="gdbPath">FGDB 경로</param>
        /// <param name="qcError">업데이트할 QC 오류 객체</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> UpdateQcErrorStatusAsync(string gdbPath, QcError qcError)
        {
            try
            {
                _logger.LogDebug("QC 오류 상태 업데이트: {ErrorId} -> {Status}", qcError.GlobalID, qcError.Status);
                return await _dataService.UpsertQcErrorAsync(gdbPath, qcError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC 오류 상태 업데이트 실패: {ErrorId}", qcError.GlobalID);
                return false;
            }
        }

        /// <summary>
        /// ValidationError를 GeometryErrorDetail로 변환 (Metadata 기반 좌표/WKT 반영)
        /// </summary>
        private GeometryErrorDetail ConvertValidationErrorToGeometryErrorDetail(ValidationError error)
        {
            var detail = new GeometryErrorDetail
            {
                ObjectId = error.FeatureId,
                ErrorType = error.ErrorCode,
                ErrorValue = error.Message,
                DetailMessage = error.Message
            };

            if (error.Metadata != null)
            {
                if (error.Metadata.TryGetValue("X", out var xValue) && double.TryParse(xValue?.ToString(), out var x))
                {
                    detail.X = x;
                }

                if (error.Metadata.TryGetValue("Y", out var yValue) && double.TryParse(yValue?.ToString(), out var y))
                {
                    detail.Y = y;
                }

                if (error.Metadata.TryGetValue("GeometryWkt", out var wktObj))
                {
                    var wkt = wktObj?.ToString();
                    if (!string.IsNullOrWhiteSpace(wkt))
                    {
                        detail.GeometryWkt = wkt;
                    }
                }
            }

            return detail;
        }

        /// <summary>
        /// 지오메트리 검수 결과를 QC_ERRORS에 저장합니다
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="validationResults">지오메트리 검수 결과 목록</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <param name="sourceGdbPath">원본 FileGDB 경로</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> SaveGeometryValidationResultsAsync(string qcErrorsGdbPath, List<GeometryValidationItem> validationResults, string runId, string sourceGdbPath)
        {
            try
            {
                _logger.LogInformation("지오메트리 검수 결과 저장 시작: {Count}개 항목", validationResults.Count);
                
                // 검수 결과 상세 분석
                var totalErrorDetails = validationResults.Sum(v => v.ErrorDetails?.Count ?? 0);
                _logger.LogInformation("총 오류 상세 항목: {TotalErrors}개", totalErrorDetails);
                
                // 각 검수 결과별 상세 로깅
                foreach (var validationItem in validationResults)
                {
                    var errorCount = validationItem.ErrorDetails?.Count ?? 0;
                    if (errorCount > 0)
                    {
                        _logger.LogInformation("검수 결과 '{TableId}' - {CheckType}: {ErrorCount}개 오류", 
                            validationItem.TableId, validationItem.CheckType, errorCount);
                        
                        // 오류 타입별 개수 로깅
                        var errorTypeGroups = validationItem.ErrorDetails?.GroupBy(e => e.ErrorType).ToList() ?? new List<IGrouping<string, GeometryErrorDetail>>();
                        foreach (var group in errorTypeGroups)
                        {
                            _logger.LogInformation("  - {ErrorType}: {Count}개", group.Key, group.Count());
                        }
                    }
                }
                
                // 저장할 오류가 있는지 확인 (ErrorDetails가 비어있어도 다른 오류 정보가 있으면 저장)
                var hasErrors = totalErrorDetails > 0 || validationResults.Any(v => 
                    v.ErrorCount > 0 || v.WarningCount > 0 || 
                    (v.ErrorMessages != null && v.ErrorMessages.Any()));
                
                if (!hasErrors)
                {
                    _logger.LogInformation("저장할 오류가 없습니다 - 검수 통과");
                    return true;
                }
                
                _logger.LogInformation("오류 정보 확인: ErrorDetails {ErrorDetails}개, ErrorCount {ErrorCount}개, ErrorMessages {ErrorMessages}개", 
                    totalErrorDetails, 
                    validationResults.Sum(v => v.ErrorCount),
                    validationResults.Sum(v => v.ErrorMessages?.Count ?? 0));

                // QC_ERRORS 데이터베이스 초기화 (없으면 생성)
                _logger.LogDebug("QC_ERRORS 데이터베이스 초기화 중...");
                var initResult = await InitializeQcErrorsDatabaseAsync(qcErrorsGdbPath);
                if (!initResult)
                {
                    _logger.LogError("QC_ERRORS 데이터베이스 초기화 실패");
                    return false;
                }

                // ValidationResultConverter를 사용하여 GeometryValidationItem을 QcError로 변환
                _logger.LogDebug("GeometryValidationItem을 QcError로 변환 중...");
                var qcErrors = new List<QcError>();
                
                var processedItems = 0;
                var convertedErrors = 0;
                var emptyObjectIdCount = 0;
                var tablesWithEmptyObjectId = new HashSet<string>();
                
                foreach (var validationItem in validationResults)
                {
                    processedItems++;
                    _logger.LogDebug("처리 중: {Current}/{Total} - {TableId} ({ErrorCount}개 오류)", 
                        processedItems, validationResults.Count, validationItem.TableId, validationItem.ErrorDetails?.Count ?? 0);
                    
                    // ErrorDetails가 있는 경우 처리
                    if (validationItem.ErrorDetails != null && validationItem.ErrorDetails.Any())
                    {
                        foreach (var errorDetail in validationItem.ErrorDetails)
                        {
                            convertedErrors++;
                            
                            // ObjectId 유효성 사전 검사 및 기본값 설정
                            var objectId = errorDetail.ObjectId;
                            if (string.IsNullOrWhiteSpace(objectId))
                            {
                                emptyObjectIdCount++;
                                tablesWithEmptyObjectId.Add(validationItem.TableId);
                                _logger.LogDebug("빈 ObjectId 발견: {TableId} - {ErrorType}", validationItem.TableId, errorDetail.ErrorType);
                                
                                // 기본값 설정: 테이블 수준 오류인 경우 "TABLE", 그 외는 "UNKNOWN"
                                objectId = errorDetail.ErrorType == "기본검사" ? "TABLE" : "UNKNOWN";
                                _logger.LogDebug("ObjectId 기본값 설정: {ObjectId}", objectId);
                            }
                            
                            // 오류 상세 좌표(X/Y)가 제공되면 이를 우선 사용, 없을 때만 원본 FGDB에서 추출
                            double x, y;
                            string geometryType;
                            OSGeo.OGR.Geometry? geometry = null;

                            if ((errorDetail.X != 0 || errorDetail.Y != 0))
                            {
                                // 오류 지점 좌표를 그대로 사용하여 Point로 저장
                                x = errorDetail.X;
                                y = errorDetail.Y;
                                geometryType = "Point";
                                // 원본 지오메트리는 저장하지 않음(DetailsJSON에 포함 가능)
                            }
                            else
                            {
                                // 좌표가 없을 때만 원본에서 대표 좌표 추출
                                var extracted = await ExtractGeometryInfoAsync(
                                    sourceGdbPath, validationItem.TableId, objectId);
                                geometry = extracted.geometry;
                                x = extracted.x;
                                y = extracted.y;
                                geometryType = extracted.geometryType;
                            }

                            var qcError = new QcError
                            {
                                GlobalID = Guid.NewGuid().ToString(),
                                ErrType = "GEOM",
                                ErrCode = GetErrorCodeFromCheckType(errorDetail.ErrorType),
                                Severity = string.Empty,
                                Status = string.Empty,
                                RuleId = $"GEOM_{errorDetail.ErrorType}",
                                SourceClass = validationItem.TableId,
                                SourceOID = long.TryParse(objectId, out var oid) ? oid : 0,
                                SourceGlobalID = null, // 향후 구현
                                X = x,
                                Y = y,
                                // 오류 상세 좌표 우선: 좌표가 있으면 Point WKT 사용, 아니면 추출한 WKT 사용
                                GeometryWKT = (errorDetail.X != 0 || errorDetail.Y != 0)
                                    ? QcError.CreatePointWKT(x, y)
                                    : (geometry != null ? GetWktFromGeometry(geometry) : null),
                                GeometryType = (errorDetail.X != 0 || errorDetail.Y != 0) ? "Point" : geometryType,
                                Geometry = (errorDetail.X != 0 || errorDetail.Y != 0) ? null : geometry?.Clone(), // 좌표 우선 시 원본 지오메트리 저장 안 함
                                ErrorValue = errorDetail.ErrorValue,
                                ThresholdValue = errorDetail.ThresholdValue,
                                Message = errorDetail.DetailMessage,
                                DetailsJSON = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    CheckType = errorDetail.ErrorType,
                                    TableName = validationItem.TableName,
                                    Threshold = validationItem.Threshold,
                                    ObjectId = errorDetail.ObjectId,
                                    Coordinates = new { X = x, Y = y },
                                    GeometryType = (errorDetail.X != 0 || errorDetail.Y != 0) ? "Point" : geometryType,
                                    // 원본 지오메트리는 상세에만 선택적으로 보존
                                    OriginalGeometryWKT = errorDetail.GeometryWkt ?? (geometry != null ? GetWktFromGeometry(geometry) : null)
                                }),
                                RunID = runId,
                                CreatedUTC = DateTime.UtcNow,
                                UpdatedUTC = DateTime.UtcNow
                            };
                            
                            qcErrors.Add(qcError);
                            
                            // 지오메트리 리소스 해제
                            geometry?.Dispose();
                        }
                    }
                    // ErrorDetails가 없지만 ErrorCount나 ErrorMessages가 있는 경우 처리
                    else if (validationItem.ErrorCount > 0 || (validationItem.ErrorMessages != null && validationItem.ErrorMessages.Any()))
                    {
                        _logger.LogInformation("ErrorDetails 없이 오류 정보 처리: {TableId} - ErrorCount: {ErrorCount}, ErrorMessages: {ErrorMessageCount}", 
                            validationItem.TableId, validationItem.ErrorCount, validationItem.ErrorMessages?.Count ?? 0);
                        
                        // ErrorMessages에서 오류 정보 추출하여 QcError 생성
                        if (validationItem.ErrorMessages != null && validationItem.ErrorMessages.Any())
                        {
                            foreach (var errorMessage in validationItem.ErrorMessages)
                            {
                                convertedErrors++;
                                
                                // 원본 FileGDB에서 실제 지오메트리 정보 추출 (테이블 대표)
                                var (geometry, x, y, geometryType) = await ExtractGeometryInfoAsync(
                                    sourceGdbPath, validationItem.TableId, "TABLE");

                                var qcError = new QcError
                                {
                                    GlobalID = Guid.NewGuid().ToString(),
                                    ErrType = "GEOM",
                                    ErrCode = GetErrorCodeFromCheckType(validationItem.CheckType),
                                    Severity = string.Empty,
                                    Status = string.Empty,
                                    RuleId = $"GEOM_{validationItem.CheckType}",
                                    SourceClass = validationItem.TableId,
                                    SourceOID = 0, // 테이블 수준 오류
                                    SourceGlobalID = null,
                                    X = x,
                                    Y = y,
                                    GeometryWKT = geometry != null ? GetWktFromGeometry(geometry) : null,
                                    GeometryType = geometryType,
                                    Geometry = geometry?.Clone(),
                                    ErrorValue = validationItem.CheckType,
                                    ThresholdValue = validationItem.Threshold ?? "N/A",
                                    Message = errorMessage,
                                    DetailsJSON = System.Text.Json.JsonSerializer.Serialize(new
                                    {
                                        CheckType = validationItem.CheckType,
                                        TableName = validationItem.TableName,
                                        Threshold = validationItem.Threshold,
                                        ObjectId = "TABLE",
                                        Coordinates = new { X = x, Y = y },
                                        GeometryType = geometryType,
                                        ErrorCount = validationItem.ErrorCount
                                    }),
                                    RunID = runId,
                                    CreatedUTC = DateTime.UtcNow,
                                    UpdatedUTC = DateTime.UtcNow
                                };
                                
                                qcErrors.Add(qcError);
                                
                                // 지오메트리 리소스 해제
                                geometry?.Dispose();
                            }
                        }
                    }
                }
                
                // 빈 ObjectId 통계 로깅
                if (emptyObjectIdCount > 0)
                {
                    _logger.LogInformation("빈 ObjectId 통계: {EmptyCount}개 오류 (영향받은 테이블: {TableCount}개)", 
                        emptyObjectIdCount, tablesWithEmptyObjectId.Count);
                    _logger.LogDebug("빈 ObjectId가 있는 테이블: {Tables}", string.Join(", ", tablesWithEmptyObjectId));
                }

                _logger.LogInformation("QcError 변환 완료: {ConvertedErrors}개 오류를 {QcErrorCount}개 QcError로 변환", 
                    convertedErrors, qcErrors.Count);

                if (qcErrors.Count == 0)
                {
                    _logger.LogInformation("변환된 QcError가 없습니다 - 저장할 내용 없음");
                    return true;
                }

                // QcErrorDataService를 통해 실제 저장
                _logger.LogInformation("QcError 저장 시작: {Count}개 항목", qcErrors.Count);
                var successCount = 0;
                var failedCount = 0;
                
                for (int i = 0; i < qcErrors.Count; i++)
                {
                    var qcError = qcErrors[i];
                    _logger.LogDebug("저장 중: {Current}/{Total} - {ErrorCode} ({TableId}:{ObjectId})", 
                        i + 1, qcErrors.Count, qcError.ErrCode, qcError.SourceClass, qcError.SourceOID);
                    
                    var success = await _dataService.UpsertQcErrorAsync(qcErrorsGdbPath, qcError);
                    if (success) 
                    {
                        successCount++;
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogWarning("QcError 저장 실패: {ErrorCode} ({TableId}:{ObjectId})", 
                            qcError.ErrCode, qcError.SourceClass, qcError.SourceOID);
                    }
                }

                var allSuccess = successCount == qcErrors.Count;
                _logger.LogInformation("지오메트리 검수 결과 저장 완료: 성공 {Success}개, 실패 {Failed}개, 총 {Total}개", 
                    successCount, failedCount, qcErrors.Count);
                
                return allSuccess;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 저장 실패: 잘못된 매개변수 - {Message}", ex.Message);
                _logger.LogError("확인 사항: 1) FileGDB 경로가 올바른지 2) RunId가 유효한지 3) 검수 결과 데이터가 올바른지");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 저장 실패: 잘못된 작업 - {Message}", ex.Message);
                _logger.LogError("확인 사항: 1) QC_ERRORS 스키마가 올바르게 생성되었는지 2) 데이터베이스 연결 상태");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 저장 실패: 접근 권한 부족");
                _logger.LogError("해결 방안: 1) 관리자 권한으로 실행 2) FileGDB 파일 권한 확인");
                return false;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 저장 실패: 입출력 오류");
                _logger.LogError("해결 방안: 1) 디스크 공간 확인 2) 파일이 다른 프로그램에서 사용 중인지 확인");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 결과 저장 중 예상치 못한 오류 발생");
                _logger.LogError("오류 상세: {ErrorType} - {Message}", ex.GetType().Name, ex.Message);
                _logger.LogError("스택 트레이스: {StackTrace}", ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// 검수 타입에서 오류 코드를 생성합니다
        /// </summary>
        private string GetErrorCodeFromCheckType(string checkType)
        {
            return checkType switch
            {
                "기본검사" => "GEO001",
                "중복 지오메트리" => "DUP001",
                "겹침 지오메트리" => "OVL001",
                "자체 꼬임" => "SLF001",
                "슬리버폴리곤" => "SLV001",
                "짧은객체" => "SHT001",
                "작은면적객체" => "SML001",
                "폴리곤내폴리곤존재" => "PIP001",
                _ => "GEO999"
            };
        }

        /// <summary>
        /// 오류 타입에서 심각도를 결정합니다
        /// </summary>
        private string GetSeverityFromErrorType(string errorType)
        {
            return errorType switch
            {
                "기본검사" => "CRIT",
                "중복 지오메트리" => "MAJOR",
                "겹침 지오메트리" => "MAJOR",
                "자체 꼬임" => "CRIT",
                "슬리버폴리곤" => "MINOR",
                "짧은객체" => "MINOR",
                "작은면적객체" => "MINOR",
                "폴리곤내폴리곤존재" => "MAJOR",
                _ => "INFO"
            };
        }

        /// <summary>
        /// 지오메트리에서 WKT 문자열을 추출합니다
        /// </summary>
        private string? GetWktFromGeometry(OSGeo.OGR.Geometry geometry)
        {
            try
            {
                string wkt;
                var result = geometry.ExportToWkt(out wkt);
                return result == 0 ? wkt : null; // OGRERR_NONE = 0
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WKT 변환 실패");
                return null;
            }
        }

        /// <summary>
        /// 원본 FileGDB에서 실제 지오메트리 정보를 추출합니다
        /// </summary>
        /// <param name="sourceGdbPath">원본 FileGDB 경로</param>
        /// <param name="tableId">테이블 ID</param>
        /// <param name="objectId">객체 ID</param>
        /// <returns>지오메트리, X좌표, Y좌표, 지오메트리 타입</returns>
        /// <summary>
        /// 원본 FileGDB에서 실제 지오메트리 정보를 추출합니다
        /// </summary>
        /// <param name="sourceGdbPath">원본 FileGDB 경로</param>
        /// <param name="tableId">테이블 ID</param>
        /// <param name="objectId">객체 ID</param>
        /// <returns>지오메트리, X좌표, Y좌표, 지오메트리 타입</returns>
        public async Task<(OSGeo.OGR.Geometry? geometry, double x, double y, string geometryType)> ExtractGeometryInfoAsync(
            string sourceGdbPath, string tableId, string objectId)
        {
            try
            {
                // GDAL 초기화 확인 (안전장치)
                EnsureGdalInitialized();
                
                var driver = GetFileGdbDriverSafely();
                if (driver == null)
                {
                    _logger.LogError("ExtractGeometryInfoAsync: FileGDB 드라이버를 찾을 수 없습니다");
                    return (null, 0, 0, "Unknown");
                }
                
                var dataSource = driver.Open(sourceGdbPath, 0); // 읽기 모드

                if (dataSource == null)
                {
                    _logger.LogWarning("원본 FileGDB를 열 수 없습니다: {SourceGdbPath}", sourceGdbPath);
                    return (null, 0, 0, "Unknown");
                }

                // 테이블 찾기 (대소문자 무관)
                Layer? layer = null;
                for (int i = 0; i < dataSource.GetLayerCount(); i++)
                {
                    var testLayer = dataSource.GetLayerByIndex(i);
                    if (testLayer.GetName().Equals(tableId, StringComparison.OrdinalIgnoreCase))
                    {
                        layer = testLayer;
                        break;
                    }
                }

                if (layer == null)
                {
                    _logger.LogWarning("테이블을 찾을 수 없습니다: {TableId}", tableId);
                    dataSource.Dispose();
                    return (null, 0, 0, "Unknown");
                }

                // ObjectId 유효성 검사
                if (string.IsNullOrWhiteSpace(objectId))
                {
                    _logger.LogDebug("ObjectId가 비어있습니다 (테이블이 비어있거나 ObjectId 필드 없음): {TableId}", tableId);
                    dataSource.Dispose();
                    return (null, 0, 0, "Unknown");
                }
                
                // 특수 ObjectId 처리 (TABLE, UNKNOWN 등)
                if (objectId == "TABLE" || objectId == "UNKNOWN" || !long.TryParse(objectId, out _))
                {
                    _logger.LogDebug("특수 ObjectId 처리: {TableId}:{ObjectId} - 대표 지오메트리 사용", tableId, objectId);
                    // 테이블의 첫 번째 피처를 대표로 사용
                    layer.ResetReading();
                    var firstFeature = layer.GetNextFeature();
                    if (firstFeature != null)
                    {
                        var firstGeometry = firstFeature.GetGeometryRef();
                        if (firstGeometry != null)
                        {
                            var firstClonedGeometry = firstGeometry.Clone();
                            var firstEnvelope = new OSGeo.OGR.Envelope();
                            firstClonedGeometry.GetEnvelope(firstEnvelope);
                            double firstCenterX = (firstEnvelope.MinX + firstEnvelope.MaxX) / 2.0;
                            double firstCenterY = (firstEnvelope.MinY + firstEnvelope.MaxY) / 2.0;
                            
                            var firstGeomType = firstClonedGeometry.GetGeometryType();
                            string firstGeometryTypeName = firstGeomType switch
                            {
                                wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => "POINT",
                                wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => "LINESTRING",
                                wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => "POLYGON",
                                _ => "UNKNOWN"
                            };
                            
                            firstFeature.Dispose();
                            dataSource.Dispose();
                            return (firstClonedGeometry, firstCenterX, firstCenterY, firstGeometryTypeName);
                        }
                        firstFeature.Dispose();
                    }
                    dataSource.Dispose();
                    return (null, 0, 0, "Unknown");
                }

                // ObjectId로 피처 검색 (다양한 방법 시도)
                Feature? feature = null;
                
                // 방법 1: OBJECTID 필드로 직접 검색
                try
                {
                    layer.SetAttributeFilter($"OBJECTID = {objectId}");
                    layer.ResetReading();
                    feature = layer.GetNextFeature();
                    if (feature != null)
                    {
                        _logger.LogDebug("ObjectId 매칭 성공 (1단계): {TableId}:{ObjectId}", tableId, objectId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OBJECTID 필터 검색 실패, 다른 방법 시도: {TableId}:{ObjectId}", tableId, objectId);
                }
                
                // 방법 1-2: OBJ_ 접두어가 있는 경우 재시도
                if (feature == null && !objectId.StartsWith("OBJ_", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        layer.SetAttributeFilter($"OBJECTID = 'OBJ_{objectId}'");
                        layer.ResetReading();
                        feature = layer.GetNextFeature();
                        if (feature != null)
                        {
                            _logger.LogDebug("ObjectId 매칭 성공 (1-2단계): {TableId}:OBJ_{ObjectId}", tableId, objectId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "OBJ_ 접두어 검색 실패: {TableId}:{ObjectId}", tableId, objectId);
                    }
                }
                
                // 방법 2: FID로 직접 검색 (OBJECTID 필드가 없는 경우)
                if (feature == null && long.TryParse(objectId, out var fid))
                {
                    try
                    {
                        layer.SetAttributeFilter(null); // 필터 초기화
                        layer.ResetReading();
                        feature = layer.GetFeature(fid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "FID 직접 검색 실패: {TableId}:{ObjectId}", tableId, objectId);
                    }
                }
                
                // 방법 3: 모든 피처를 순회하며 ObjectId 비교
                if (feature == null)
                {
                    try
                    {
                        layer.SetAttributeFilter(null); // 필터 초기화
                        layer.ResetReading();
                        
                        Feature? currentFeature;
                        while ((currentFeature = layer.GetNextFeature()) != null)
                        {
                            var currentFid = currentFeature.GetFID();
                            if (currentFid.ToString() == objectId)
                            {
                                feature = currentFeature;
                                break;
                            }
                            currentFeature.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "순회 검색 실패: {TableId}:{ObjectId}", tableId, objectId);
                    }
                }
                
                // 방법 4: 모든 방법 실패 시 첫 번째 피처를 대표로 사용
                if (feature == null)
                {
                    _logger.LogWarning("모든 방법으로 객체를 찾을 수 없습니다: {TableId}:{ObjectId} - 첫 번째 피처를 대표로 사용", tableId, objectId);
                    layer.SetAttributeFilter(null);
                    layer.ResetReading();
                    feature = layer.GetNextFeature();
                    
                    if (feature == null)
                    {
                        _logger.LogError("테이블이 비어있습니다: {TableId}", tableId);
                        dataSource.Dispose();
                        return (null, 0, 0, "EmptyTable");
                    }
                }

                var geometry = feature.GetGeometryRef();
                if (geometry == null)
                {
                    _logger.LogWarning("지오메트리가 없습니다: {TableId}:{ObjectId}", tableId, objectId);
                    feature.Dispose();
                    dataSource.Dispose();
                    return (null, 0, 0, "NoGeometry");
                }

                // 지오메트리 복사 (원본 보호)
                var clonedGeometry = geometry.Clone();
                
                // 첫 점 좌표 추출 (요구사항에 따라)
                double firstX = 0, firstY = 0;
                var geomType = clonedGeometry.GetGeometryType();
                
                var flattened = (wkbGeometryType)((int)geomType & 0xFF);

                if (flattened == wkbGeometryType.wkbPoint)
                {
                    // Point: 그대로 사용
                    var pointArray = new double[3];
                    clonedGeometry.GetPoint(0, pointArray);
                    firstX = pointArray[0];
                    firstY = pointArray[1];
                }
                else if (flattened == wkbGeometryType.wkbMultiPoint)
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
                else if (flattened == wkbGeometryType.wkbLineString)
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
                else if (flattened == wkbGeometryType.wkbMultiLineString)
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
                else if (flattened == wkbGeometryType.wkbPolygon)
                {
                    // Polygon: 내부 보장 포인트(PointOnSurface) 우선
                    try
                    {
                        using var pos = clonedGeometry.PointOnSurface();
                        if (pos != null && !pos.IsEmpty())
                        {
                            var p = new double[3];
                            pos.GetPoint(0, p);
                            firstX = p[0];
                            firstY = p[1];
                        }
                        else if (clonedGeometry.GetGeometryCount() > 0)
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
                    catch
                    {
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
                }
                else if (flattened == wkbGeometryType.wkbMultiPolygon)
                {
                    // MultiPolygon: PointOnSurface 우선, 실패 시 첫 Polygon 외부 링 첫점
                    try
                    {
                        using var pos = clonedGeometry.PointOnSurface();
                        if (pos != null && !pos.IsEmpty())
                        {
                            var p = new double[3];
                            pos.GetPoint(0, p);
                            firstX = p[0];
                            firstY = p[1];
                        }
                        else if (clonedGeometry.GetGeometryCount() > 0)
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
                    catch
                    {
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
                }
                else
                {
                    // 기타 지오메트리 타입: 중심점으로 폴백
                    var envelope = new OSGeo.OGR.Envelope();
                    clonedGeometry.GetEnvelope(envelope);
                    firstX = (envelope.MinX + envelope.MaxX) / 2.0;
                    firstY = (envelope.MinY + envelope.MaxY) / 2.0;
                }

                // 지오메트리 타입 결정 (대문자로 통일)
                string geometryTypeName = geomType switch
                {
                    wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => "POINT",
                    wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => "LINESTRING",
                    wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => "POLYGON",
                    _ => "UNKNOWN"
                };

                feature.Dispose();
                dataSource.Dispose();

                _logger.LogInformation("지오메트리 정보 추출 완료: {TableId}:{ObjectId} - {GeometryType} ({X}, {Y})", 
                    tableId, objectId, geometryTypeName, firstX, firstY);

                return (clonedGeometry, firstX, firstY, geometryTypeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 정보 추출 실패: {TableId}:{ObjectId}", tableId, objectId);
                return (null, 0, 0, "Unknown");
            }
        }

        /// <summary>
        /// QcError 목록을 배치로 저장합니다
        /// </summary>
        /// <param name="gdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="qcErrors">저장할 QcError 목록</param>
        /// <param name="batchSize">배치 크기(기본 1000)</param>
        /// <returns>성공적으로 저장된 개수</returns>
        public async Task<int> BatchAppendQcErrorsAsync(string gdbPath, IEnumerable<QcError> qcErrors, int batchSize = 1000)
        {
            return await _dataService.BatchAppendQcErrorsAsync(gdbPath, qcErrors, batchSize);
        }

        /// <summary>
        /// QC_ERRORS 시스템 진단 및 문제 해결 방안 제시
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>진단 결과 및 해결 방안</returns>
        public async Task<string> DiagnoseQcErrorsIssuesAsync(string gdbPath)
        {
            var diagnostics = new List<string>();
            
            try
            {
                _logger.LogInformation("QC_ERRORS 시스템 진단 시작: {GdbPath}", gdbPath);
                
                // 1. 경로 존재 여부 확인
                if (!Directory.Exists(gdbPath) && !File.Exists(gdbPath))
                {
                    diagnostics.Add("❌ FileGDB 경로가 존재하지 않습니다");
                    diagnostics.Add("   해결방안: 올바른 FileGDB 경로를 지정하세요");
                    return string.Join(Environment.NewLine, diagnostics);
                }
                diagnostics.Add("✅ FileGDB 경로 존재 확인");
                
                // 2. 쓰기 권한 확인
                try
                {
                    var testFile = Path.Combine(gdbPath, "test_write_permission.tmp");
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                    diagnostics.Add("✅ FileGDB 쓰기 권한 확인");
                }
                catch (UnauthorizedAccessException)
                {
                    diagnostics.Add("❌ FileGDB 쓰기 권한 부족");
                    diagnostics.Add("   해결방안: 1) 관리자 권한으로 실행 2) 파일/폴더 권한 설정 확인");
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"⚠️ 쓰기 권한 확인 실패: {ex.Message}");
                }
                
                // 3. GDAL 드라이버 확인
                try
                {
                    // GDAL 초기화 확인
                    EnsureGdalInitialized();
                    
                    var driver = GetFileGdbDriverSafely();
                    if (driver != null)
                    {
                        diagnostics.Add("✅ GDAL FileGDB 드라이버 사용 가능");
                    }
                    else
                    {
                        diagnostics.Add("❌ GDAL FileGDB 드라이버를 찾을 수 없음");
                        diagnostics.Add("   해결방안: 1) GDAL 라이브러리 재설치 2) Visual C++ Redistributable 설치");
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"❌ GDAL 드라이버 확인 실패: {ex.Message}");
                    diagnostics.Add("   해결방안: GDAL 라이브러리가 올바르게 설치되었는지 확인");
                }
                
                // 4. QC_ERRORS 스키마 확인
                var schemaValid = await ValidateQcErrorsSchemaAsync(gdbPath);
                if (schemaValid)
                {
                    diagnostics.Add("✅ QC_ERRORS 스키마 유효");
                }
                else
                {
                    diagnostics.Add("❌ QC_ERRORS 스키마 누락 또는 손상");
                    diagnostics.Add("   해결방안: 검수 시작 시 스키마가 자동 생성됩니다");
                }
                
                // 5. 디스크 공간 확인
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(gdbPath));
                    var freeSpaceGB = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                    if (freeSpaceGB > 1)
                    {
                        diagnostics.Add($"✅ 디스크 여유 공간 충분: {freeSpaceGB:F1}GB");
                    }
                    else
                    {
                        diagnostics.Add($"⚠️ 디스크 여유 공간 부족: {freeSpaceGB:F1}GB");
                        diagnostics.Add("   해결방안: 디스크 공간을 확보하세요");
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"⚠️ 디스크 공간 확인 실패: {ex.Message}");
                }
                
                _logger.LogInformation("QC_ERRORS 시스템 진단 완료");
                return string.Join(Environment.NewLine, diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 시스템 진단 중 오류 발생");
                diagnostics.Add($"❌ 진단 중 오류 발생: {ex.Message}");
                return string.Join(Environment.NewLine, diagnostics);
            }
        }
    }
}
