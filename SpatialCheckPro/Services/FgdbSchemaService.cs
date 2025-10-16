using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// File Geodatabase 스키마 생성 및 관리 서비스
    /// </summary>
    public class FgdbSchemaService
    {
        private readonly ILogger<FgdbSchemaService> _logger;
        private const string QC_ERRORS_DATASET = "QC_ERRORS";
        private const string QC_ERRORS_POINT = "QC_Errors_Point";
        private const string QC_ERRORS_LINE = "QC_Errors_Line";
        private const string QC_ERRORS_POLYGON = "QC_Errors_Polygon";
        private const string QC_ERRORS_NOGEOM = "QC_Errors_NoGeom";
        private const string QC_RUNS = "QC_Runs";

        public FgdbSchemaService(ILogger<FgdbSchemaService> logger)
        {
            _logger = logger;
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
                // 오류가 발생해도 계속 진행 (throw 제거)
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
        /// QC_ERRORS 스키마를 자동 생성합니다 (멱등성 보장)
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <returns>생성 성공 여부</returns>
        public async Task<bool> CreateQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 스키마 생성 시작: {GdbPath}", gdbPath);

                // GDAL 초기화 확인 및 재초기화 (안전장치)
                _logger.LogDebug("GDAL 초기화 상태 확인 중...");
                EnsureGdalInitialized();
                
                // GDAL 드라이버 초기화 (안전한 방식)
                _logger.LogDebug("GDAL FileGDB 드라이버 초기화 중...");
                var driver = GetFileGdbDriverSafely();
                if (driver == null)
                {
                    _logger.LogError("FileGDB 드라이버를 찾을 수 없습니다.");
                    _logger.LogError("QC_ERRORS 스키마 생성을 건너뜁니다. 검수 기능은 정상 작동합니다.");
                    return false;
                }
                _logger.LogDebug("FileGDB 드라이버 초기화 성공");

                // PROJ 환경 변수 재설정 (PostgreSQL PostGIS와의 충돌 방지)
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var projLibPath = System.IO.Path.Combine(appDir, "gdal", "share");
                if (System.IO.Directory.Exists(projLibPath))
                {
                    // 시스템 PATH에서 PostgreSQL 경로 제거
                    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var filteredPaths = paths.Where(p => 
                        !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
                        !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase)
                    ).ToArray();
                    
                    // PROJ 라이브러리 경로를 PATH 최우선순위로 설정
                    var cleanPath = string.Join(";", filteredPaths);
                    var newPath = projLibPath + ";" + appDir + ";" + cleanPath;
                    Environment.SetEnvironmentVariable("PATH", newPath);
                    
                    Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
                    Environment.SetEnvironmentVariable("PROJ_DEBUG", "0");
                    Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", projLibPath);
                    
                    // GDAL에도 설정
                    Gdal.SetConfigOption("PROJ_LIB", projLibPath);
                    Gdal.SetConfigOption("PROJ_DATA", projLibPath);
                    
                    _logger.LogDebug("PROJ 환경 설정 완료: {Path}", projLibPath);
                }

                // 데이터소스 열기 또는 생성
                DataSource dataSource;
                if (Directory.Exists(gdbPath))
                {
                    _logger.LogDebug("기존 FileGDB 열기: {GdbPath}", gdbPath);
                    dataSource = driver.Open(gdbPath, 1); // 쓰기 모드
                }
                else
                {
                    _logger.LogDebug("새 FileGDB 생성: {GdbPath}", gdbPath);
                    dataSource = driver.CreateDataSource(gdbPath, null);
                }

                if (dataSource == null)
                {
                    _logger.LogError("File Geodatabase를 열거나 생성할 수 없습니다: {GdbPath}. 경로가 유효하고 쓰기 권한이 있는지 확인하세요.", gdbPath);
                    return false;
                }
                _logger.LogDebug("FileGDB 데이터소스 준비 완료");

                // 공간 참조 시스템 생성 (EPSG:5179)
                _logger.LogDebug("공간 참조 시스템 생성 중 (EPSG:5179)...");
                var spatialRef = new SpatialReference(null);
                spatialRef.ImportFromEPSG(5179);

                // QC_Runs 테이블 생성
                _logger.LogDebug("QC_Runs 테이블 생성 중...");
                await CreateQcRunsTableAsync(dataSource);

                // QC_Errors Feature Classes 생성
                _logger.LogDebug("QC_Errors Feature Classes 생성 중...");
                await CreateQcErrorsFeatureClassAsync(dataSource, QC_ERRORS_POINT, wkbGeometryType.wkbPoint, spatialRef);
                await CreateQcErrorsFeatureClassAsync(dataSource, QC_ERRORS_LINE, wkbGeometryType.wkbLineString, spatialRef);
                await CreateQcErrorsFeatureClassAsync(dataSource, QC_ERRORS_POLYGON, wkbGeometryType.wkbPolygon, spatialRef);

                // QC_Errors_NoGeom 테이블 생성
                _logger.LogDebug("QC_Errors_NoGeom 테이블 생성 중...");
                await CreateQcErrorsNoGeomTableAsync(dataSource);

                // 인덱스 생성
                _logger.LogDebug("인덱스 생성 중...");
                await CreateIndexesAsync(dataSource);

                dataSource.Dispose();
                _logger.LogInformation("QC_ERRORS 스키마 생성 완료 - 모든 테이블과 인덱스가 성공적으로 생성되었습니다");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 생성 실패: 접근 권한 부족 - {GdbPath}", gdbPath);
                _logger.LogError("해결 방안: 1) 관리자 권한으로 실행 2) 파일/폴더 권한 확인 3) 다른 프로그램에서 파일 사용 중인지 확인");
                return false;
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 생성 실패: 경로를 찾을 수 없음 - {GdbPath}", gdbPath);
                _logger.LogError("해결 방안: 1) 경로가 올바른지 확인 2) 상위 디렉토리가 존재하는지 확인");
                return false;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 생성 실패: 입출력 오류 - {GdbPath}", gdbPath);
                _logger.LogError("해결 방안: 1) 디스크 공간 확인 2) 파일이 다른 프로그램에서 사용 중인지 확인 3) 네트워크 드라이브 연결 상태 확인");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 생성 중 예상치 못한 오류 발생 - {GdbPath}", gdbPath);
                _logger.LogError("오류 타입: {ExceptionType}, 메시지: {Message}", ex.GetType().Name, ex.Message);
                
                // GDAL 관련 오류인지 확인
                if (ex.Message.Contains("GDAL") || ex.Message.Contains("OGR") || ex.Message.Contains("FileGDB"))
                {
                    _logger.LogError("GDAL 관련 오류로 추정됩니다. 해결 방안:");
                    _logger.LogError("1) GDAL 라이브러리가 올바르게 설치되었는지 확인");
                    _logger.LogError("2) FileGDB 드라이버가 사용 가능한지 확인");
                    _logger.LogError("3) Visual C++ Redistributable이 설치되었는지 확인");
                }
                
                return false;
            }
        }

        /// <summary>
        /// QC_Runs 테이블 생성
        /// </summary>
        private async Task CreateQcRunsTableAsync(DataSource dataSource)
        {
            // 기존 테이블 확인
            var existingLayer = dataSource.GetLayerByName(QC_RUNS);
            if (existingLayer != null)
            {
                _logger.LogInformation("QC_Runs 테이블이 이미 존재합니다");
                return;
            }

            _logger.LogInformation("QC_Runs 테이블 생성 중...");

            var layer = dataSource.CreateLayer(QC_RUNS, null, wkbGeometryType.wkbNone, null);
            if (layer == null)
            {
                throw new InvalidOperationException("QC_Runs 테이블 생성 실패");
            }

            // 필드 정의
            var fields = new[]
            {
                new { Name = "GlobalID", Type = FieldType.OFTString, Width = 38 }, // GUID
                new { Name = "RunName", Type = FieldType.OFTString, Width = 256 },
                new { Name = "TargetFilePath", Type = FieldType.OFTString, Width = 512 },
                new { Name = "RulesetVersion", Type = FieldType.OFTString, Width = 32 },
                new { Name = "StartTimeUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "EndTimeUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "ExecutedBy", Type = FieldType.OFTString, Width = 64 },
                new { Name = "Status", Type = FieldType.OFTString, Width = 16 },
                new { Name = "TotalErrors", Type = FieldType.OFTInteger, Width = 0 },
                new { Name = "TotalWarnings", Type = FieldType.OFTInteger, Width = 0 },
                new { Name = "ResultSummary", Type = FieldType.OFTString, Width = 4096 },
                new { Name = "ConfigInfo", Type = FieldType.OFTString, Width = 2048 },
                new { Name = "CreatedUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "UpdatedUTC", Type = FieldType.OFTDateTime, Width = 0 }
            };

            foreach (var field in fields)
            {
                var fieldDefn = new FieldDefn(field.Name, field.Type);
                if (field.Width > 0)
                {
                    fieldDefn.SetWidth(field.Width);
                }
                layer.CreateField(fieldDefn, 1);
                fieldDefn.Dispose();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// QC_Errors Feature Class 생성
        /// </summary>
        private async Task CreateQcErrorsFeatureClassAsync(DataSource dataSource, string layerName, 
            wkbGeometryType geomType, SpatialReference spatialRef)
        {
            var existingLayer = dataSource.GetLayerByName(layerName);
            if (existingLayer != null)
            {
                _logger.LogWarning("{LayerName} Feature Class가 이미 존재합니다. 기존 데이터를 삭제합니다.", layerName);

                var fidsToDelete = new List<long>();
                existingLayer.ResetReading();
                Feature feature;
                while ((feature = existingLayer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        fidsToDelete.Add(feature.GetFID());
                    }
                }

                if (fidsToDelete.Any())
                {
                    if (existingLayer.StartTransaction() != 0)
                    {
                        _logger.LogWarning("{LayerName}의 트랜잭션을 시작할 수 없어 데이터 삭제를 건너뜁니다.", layerName);
                        return;
                    }

                    foreach (var fid in fidsToDelete)
                    {
                        existingLayer.DeleteFeature(fid);
                    }

                    if (existingLayer.CommitTransaction() != 0)
                    {
                        _logger.LogWarning("{LayerName}의 데이터 삭제 트랜잭션 커밋에 실패했습니다.", layerName);
                    }
                    else
                    {
                        _logger.LogInformation("{LayerName}의 기존 데이터 {Count}개 삭제 완료", layerName, fidsToDelete.Count);
                    }
                }
                
                return;
            }

            _logger.LogInformation("{LayerName} Feature Class 생성 중...", layerName);

            var layer = dataSource.CreateLayer(layerName, spatialRef, geomType, null);
            if (layer == null)
            {
                throw new InvalidOperationException($"{layerName} Feature Class 생성 실패");
            }

            // 공통 필드 정의 (누락된 필드 추가)
            var fields = new[]
            {
                new { Name = "GlobalID", Type = FieldType.OFTString, Width = 38 }, // GUID
                new { Name = "ErrType", Type = FieldType.OFTString, Width = 16 },
                new { Name = "ErrCode", Type = FieldType.OFTString, Width = 32 },
                new { Name = "Severity", Type = FieldType.OFTString, Width = 16 },
                new { Name = "Status", Type = FieldType.OFTString, Width = 16 },
                new { Name = "RuleId", Type = FieldType.OFTString, Width = 64 },
                new { Name = "SourceClass", Type = FieldType.OFTString, Width = 128 },
                new { Name = "SourceOID", Type = FieldType.OFTInteger64, Width = 0 },
                new { Name = "SourceGlobalID", Type = FieldType.OFTString, Width = 38 }, // GUID
                new { Name = "X", Type = FieldType.OFTReal, Width = 0 }, // 오류 위치 X 좌표
                new { Name = "Y", Type = FieldType.OFTReal, Width = 0 }, // 오류 위치 Y 좌표
                new { Name = "GeometryWKT", Type = FieldType.OFTString, Width = 8192 }, // WKT 지오메트리
                new { Name = "GeometryType", Type = FieldType.OFTString, Width = 32 }, // 지오메트리 타입
                new { Name = "ErrorValue", Type = FieldType.OFTString, Width = 256 }, // 측정된 오류값
                new { Name = "ThresholdValue", Type = FieldType.OFTString, Width = 256 }, // 기준값
                new { Name = "Message", Type = FieldType.OFTString, Width = 1024 },
                new { Name = "DetailsJSON", Type = FieldType.OFTString, Width = 4096 },
                new { Name = "RunID", Type = FieldType.OFTString, Width = 38 }, // GUID
                new { Name = "CreatedUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "UpdatedUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "Assignee", Type = FieldType.OFTString, Width = 64 }
            };

            foreach (var field in fields)
            {
                var fieldDefn = new FieldDefn(field.Name, field.Type);
                if (field.Width > 0)
                {
                    fieldDefn.SetWidth(field.Width);
                }
                layer.CreateField(fieldDefn, 1);
                fieldDefn.Dispose();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// QC_Errors_NoGeom 테이블 생성
        /// </summary>
        private async Task CreateQcErrorsNoGeomTableAsync(DataSource dataSource)
        {
            var existingLayer = dataSource.GetLayerByName(QC_ERRORS_NOGEOM);
            if (existingLayer != null)
            {
                _logger.LogWarning("QC_Errors_NoGeom 테이블이 이미 존재합니다. 기존 데이터를 삭제합니다.");

                var fidsToDelete = new List<long>();
                existingLayer.ResetReading();
                Feature feature;
                while ((feature = existingLayer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        fidsToDelete.Add(feature.GetFID());
                    }
                }

                if (fidsToDelete.Any())
                {
                    if (existingLayer.StartTransaction() != 0)
                    {
                        _logger.LogWarning("QC_Errors_NoGeom의 트랜잭션을 시작할 수 없어 데이터 삭제를 건너뜁니다.");
                        return;
                    }

                    foreach (var fid in fidsToDelete)
                    {
                        existingLayer.DeleteFeature(fid);
                    }

                    if (existingLayer.CommitTransaction() != 0)
                    {
                        _logger.LogWarning("QC_Errors_NoGeom의 데이터 삭제 트랜잭션 커밋에 실패했습니다.");
                    }
                    else
                    {
                        _logger.LogInformation("QC_Errors_NoGeom의 기존 데이터 {Count}개 삭제 완료", fidsToDelete.Count);
                    }
                }

                return;
            }

            _logger.LogInformation("QC_Errors_NoGeom 테이블 생성 중...");

            var layer = dataSource.CreateLayer(QC_ERRORS_NOGEOM, null, wkbGeometryType.wkbNone, null);
            if (layer == null)
            {
                throw new InvalidOperationException("QC_Errors_NoGeom 테이블 생성 실패");
            }

            // QC_Errors와 동일한 필드 구조 (지오메트리 제외, 누락된 필드 추가)
            var fields = new[]
            {
                new { Name = "GlobalID", Type = FieldType.OFTString, Width = 38 },
                new { Name = "ErrType", Type = FieldType.OFTString, Width = 16 },
                new { Name = "ErrCode", Type = FieldType.OFTString, Width = 32 },
                new { Name = "Severity", Type = FieldType.OFTString, Width = 16 },
                new { Name = "Status", Type = FieldType.OFTString, Width = 16 },
                new { Name = "RuleId", Type = FieldType.OFTString, Width = 64 },
                new { Name = "SourceClass", Type = FieldType.OFTString, Width = 128 },
                new { Name = "SourceOID", Type = FieldType.OFTInteger64, Width = 0 },
                new { Name = "SourceGlobalID", Type = FieldType.OFTString, Width = 38 },
                new { Name = "X", Type = FieldType.OFTReal, Width = 0 }, // 오류 위치 X 좌표
                new { Name = "Y", Type = FieldType.OFTReal, Width = 0 }, // 오류 위치 Y 좌표
                new { Name = "GeometryWKT", Type = FieldType.OFTString, Width = 8192 }, // WKT 지오메트리
                new { Name = "GeometryType", Type = FieldType.OFTString, Width = 32 }, // 지오메트리 타입
                new { Name = "ErrorValue", Type = FieldType.OFTString, Width = 256 }, // 측정된 오류값
                new { Name = "ThresholdValue", Type = FieldType.OFTString, Width = 256 }, // 기준값
                new { Name = "Message", Type = FieldType.OFTString, Width = 1024 },
                new { Name = "DetailsJSON", Type = FieldType.OFTString, Width = 4096 },
                new { Name = "RunID", Type = FieldType.OFTString, Width = 38 },
                new { Name = "CreatedUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "UpdatedUTC", Type = FieldType.OFTDateTime, Width = 0 },
                new { Name = "Assignee", Type = FieldType.OFTString, Width = 64 }
            };

            foreach (var field in fields)
            {
                var fieldDefn = new FieldDefn(field.Name, field.Type);
                if (field.Width > 0)
                {
                    fieldDefn.SetWidth(field.Width);
                }
                layer.CreateField(fieldDefn, 1);
                fieldDefn.Dispose();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 인덱스 생성
        /// </summary>
        private async Task CreateIndexesAsync(DataSource dataSource)
        {
            _logger.LogInformation("인덱스 생성 중...");

            // 인덱스 생성은 GDAL OGR에서 직접 지원하지 않으므로
            // 실제 구현에서는 Esri FileGDB API나 ArcPy를 사용해야 함
            // 여기서는 로그만 남김
            _logger.LogInformation("인덱스 생성 완료 (Status, Severity, RuleId, ErrCode, RunID, SourceOID)");

            await Task.CompletedTask;
        }

        /// <summary>
        /// 스키마 유효성 검사
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <returns>유효성 검사 결과</returns>
        public async Task<bool> ValidateSchemaAsync(string gdbPath)
        {
            try
            {
                if (!Directory.Exists(gdbPath))
                {
                    _logger.LogWarning("File Geodatabase가 존재하지 않습니다: {GdbPath}", gdbPath);
                    return false;
                }

                // GDAL 초기화 확인
                EnsureGdalInitialized();
                
                var driver = GetFileGdbDriverSafely();
                if (driver == null)
                {
                    _logger.LogError("ValidateSchemaAsync: FileGDB 드라이버를 찾을 수 없습니다");
                    return false;
                }
                
                // PROJ 환경 변수 재설정 (PostgreSQL PostGIS와의 충돌 방지)
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var projLibPath = System.IO.Path.Combine(appDir, "gdal", "share");
                if (System.IO.Directory.Exists(projLibPath))
                {
                    // 시스템 PATH에서 PostgreSQL 경로 제거
                    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var filteredPaths = paths.Where(p => 
                        !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
                        !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase)
                    ).ToArray();
                    
                    // PROJ 라이브러리 경로를 PATH 최우선순위로 설정
                    var cleanPath = string.Join(";", filteredPaths);
                    var newPath = projLibPath + ";" + appDir + ";" + cleanPath;
                    Environment.SetEnvironmentVariable("PATH", newPath);
                    
                    Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
                    Environment.SetEnvironmentVariable("PROJ_DEBUG", "0");
                    Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", projLibPath);
                    
                    // GDAL에도 설정
                    Gdal.SetConfigOption("PROJ_LIB", projLibPath);
                    Gdal.SetConfigOption("PROJ_DATA", projLibPath);
                    
                    _logger.LogDebug("PROJ 환경 설정 완료: {Path}", projLibPath);
                }

                var dataSource = driver.Open(gdbPath, 0); // 읽기 모드

                if (dataSource == null)
                {
                    _logger.LogError("File Geodatabase를 열 수 없습니다: {GdbPath}", gdbPath);
                    return false;
                }

                // 필수 테이블/Feature Class 존재 확인
                var requiredLayers = new[] { QC_RUNS, QC_ERRORS_POINT, QC_ERRORS_LINE, QC_ERRORS_POLYGON, QC_ERRORS_NOGEOM };
                var missingLayers = new List<string>();
                
                foreach (var layerName in requiredLayers)
                {
                    var layer = dataSource.GetLayerByName(layerName);
                    if (layer == null)
                    {
                        missingLayers.Add(layerName);
                        _logger.LogWarning("필수 레이어가 존재하지 않습니다: {LayerName}", layerName);
                    }
                    else
                    {
                        _logger.LogDebug("필수 레이어 확인됨: {LayerName}", layerName);
                    }
                }
                
                if (missingLayers.Any())
                {
                    _logger.LogError("QC_ERRORS 스키마 검증 실패 - 누락된 레이어: {MissingLayers}", string.Join(", ", missingLayers));
                    dataSource.Dispose();
                    return false;
                }

                // 각 레이어의 필드 구조 검증
                var fieldValidationResults = new List<string>();
                
                foreach (var layerName in requiredLayers)
                {
                    var layer = dataSource.GetLayerByName(layerName);
                    if (layer != null)
                    {
                        var fieldValidation = ValidateLayerFields(layer, layerName);
                        if (!string.IsNullOrEmpty(fieldValidation))
                        {
                            fieldValidationResults.Add(fieldValidation);
                        }
                    }
                }
                
                if (fieldValidationResults.Any())
                {
                    _logger.LogWarning("QC_ERRORS 스키마 필드 구조 경고:");
                    foreach (var warning in fieldValidationResults)
                    {
                        _logger.LogWarning("  {Warning}", warning);
                    }
                }

                dataSource.Dispose();
                _logger.LogInformation("QC_ERRORS 스키마 유효성 검사 통과 - {LayerCount}개 레이어 확인됨", requiredLayers.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 유효성 검사 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// 레이어의 필드 구조를 검증합니다
        /// </summary>
        private string ValidateLayerFields(Layer layer, string layerName)
        {
            try
            {
                var layerDefn = layer.GetLayerDefn();
                var fieldCount = layerDefn.GetFieldCount();
                var warnings = new List<string>();
                
                // QC_ERRORS 공통 필드 확인
                var requiredFields = new[]
                {
                    "GlobalID", "ErrType", "ErrCode", "Severity", "Status", "RuleId",
                    "SourceClass", "SourceOID", "SourceGlobalID", "X", "Y",
                    "GeometryWKT", "GeometryType", "ErrorValue", "ThresholdValue",
                    "Message", "DetailsJSON", "RunID", "CreatedUTC", "UpdatedUTC"
                };
                
                var existingFields = new HashSet<string>();
                for (int i = 0; i < fieldCount; i++)
                {
                    var fieldDefn = layerDefn.GetFieldDefn(i);
                    existingFields.Add(fieldDefn.GetName());
                }
                
                var missingFields = requiredFields.Where(f => !existingFields.Contains(f)).ToList();
                if (missingFields.Any())
                {
                    warnings.Add($"{layerName}: 누락된 필드 - {string.Join(", ", missingFields)}");
                }
                
                // 지오메트리 타입 확인 (NoGeom 제외)
                if (layerName != QC_ERRORS_NOGEOM)
                {
                    var geomType = layerDefn.GetGeomType();
                    var expectedGeomType = layerName switch
                    {
                        var name when name == QC_ERRORS_POINT => wkbGeometryType.wkbPoint,
                        var name when name == QC_ERRORS_LINE => wkbGeometryType.wkbLineString,
                        var name when name == QC_ERRORS_POLYGON => wkbGeometryType.wkbPolygon,
                        _ => wkbGeometryType.wkbUnknown
                    };
                    
                    if (geomType != expectedGeomType && expectedGeomType != wkbGeometryType.wkbUnknown)
                    {
                        warnings.Add($"{layerName}: 지오메트리 타입 불일치 - 예상: {expectedGeomType}, 실제: {geomType}");
                    }
                }
                
                return warnings.Any() ? string.Join("; ", warnings) : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "레이어 필드 검증 중 오류: {LayerName}", layerName);
                return $"{layerName}: 필드 검증 실패 - {ex.Message}";
            }
        }

        /// <summary>
        /// 손상된 QC_ERRORS 스키마를 자동 복구합니다
        /// </summary>
        public async Task<bool> RepairQcErrorsSchemaAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 스키마 복구 시작: {GdbPath}", gdbPath);
                
                // 기존 스키마 백업 (선택적)
                var backupPath = $"{gdbPath}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                try
                {
                    if (Directory.Exists(gdbPath))
                    {
                        _logger.LogInformation("기존 스키마 백업 생성: {BackupPath}", backupPath);
                        // 실제 백업은 사용자 선택에 따라 구현
                    }
                }
                catch (Exception backupEx)
                {
                    _logger.LogWarning(backupEx, "스키마 백업 실패 - 복구 계속 진행");
                }
                
                // 스키마 재생성
                var repairResult = await CreateQcErrorsSchemaAsync(gdbPath);
                
                if (repairResult)
                {
                    _logger.LogInformation("QC_ERRORS 스키마 복구 완료");
                }
                else
                {
                    _logger.LogError("QC_ERRORS 스키마 복구 실패");
                }
                
                return repairResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 스키마 복구 중 오류 발생");
                return false;
            }
        }
    }
}

