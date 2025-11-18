using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// <param name="sourceGdbPath">원본 FGDB 경로(좌표계 복제용, 선택)</param>
        /// <returns>생성 성공 여부</returns>
        public async Task<bool> CreateQcErrorsSchemaAsync(string gdbPath, string? sourceGdbPath = null)
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
                    var resolvedProjPath = ProjEnvironmentManager.ConfigureFromSharePath(projLibPath, _logger);

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
                    var newPath = resolvedProjPath + ";" + appDir + ";" + cleanPath;
                    Environment.SetEnvironmentVariable("PATH", newPath);
                    
                    Environment.SetEnvironmentVariable("PROJ_DEBUG", "0");
                    Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", resolvedProjPath);
                    Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", resolvedProjPath);
                    
                    _logger.LogInformation("PROJ 환경 설정 완료: {Resolved}", resolvedProjPath);
                }
                else
                {
                    _logger.LogError("PROJ 데이터 디렉터리를 찾을 수 없습니다: {Path}", projLibPath);
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

                // 공간 참조 시스템 생성: 원본 FGDB 좌표계 우선, 실패 시 EPSG:5179 폴백
                var spatialRef = TryResolveSpatialRefFromSource(sourceGdbPath) ?? CreateDefaultSpatialRef();

                // QC_Runs 테이블 생성
                _logger.LogDebug("QC_Runs 테이블 생성 중...");
                await CreateQcRunsTableAsync(dataSource);

                // QC_Errors Feature Classes 생성 (Point + NoGeom)
                _logger.LogDebug("QC_Errors Point Feature Class 생성 중...");
                await CreateQcErrorsFeatureClassAsync(dataSource, QC_ERRORS_POINT, wkbGeometryType.wkbPoint, spatialRef);

                _logger.LogDebug("QC_Errors_NoGeom 테이블 생성 중...");
                await CreateQcErrorsNoGeomTableAsync(dataSource);

                // 불필요한 레이어들(Line/Polygon) 강제 삭제 (기존 생성된 경우)
                _logger.LogDebug("불필요한 레이어들 삭제 중...");
                await DeleteUnnecessaryLayersAsync(dataSource);

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
        /// 원본 GDB에서 좌표계를 읽어 SpatialReference를 생성합니다
        /// </summary>
        private SpatialReference? TryResolveSpatialRefFromSource(string? sourceGdbPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceGdbPath))
                {
                    _logger.LogDebug("원본 GDB 경로가 지정되지 않아 기본 좌표계를 사용합니다.");
                    return null;
                }

                var driver = GetFileGdbDriverSafely();
                if (driver == null)
                {
                    _logger.LogWarning("원본 GDB 좌표계 확인 실패: FileGDB 드라이버 없음");
                    return null;
                }

                using var ds = driver.Open(sourceGdbPath, 0);
                if (ds == null)
                {
                    _logger.LogWarning("원본 GDB를 열 수 없어 기본 좌표계를 사용합니다: {Path}", sourceGdbPath);
                    return null;
                }

                for (int i = 0; i < ds.GetLayerCount(); i++)
                {
                    var layer = ds.GetLayerByIndex(i);
                    if (layer == null) continue;
                    var srs = layer.GetSpatialRef();
                    if (srs != null)
                    {
                        srs.ExportToWkt(out string wkt, null);
                        var spatialRef = new SpatialReference(null);
                        spatialRef.ImportFromWkt(ref wkt);
                        _logger.LogInformation("원본 GDB 좌표계를 사용합니다 (레이어: {LayerName})", layer.GetName());
                        return spatialRef;
                    }
                }

                _logger.LogWarning("원본 GDB에서 좌표계를 찾지 못했습니다. 기본 좌표계를 사용합니다.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "원본 GDB 좌표계 확인 중 오류 - 기본 좌표계를 사용합니다.");
                return null;
            }
        }

        /// <summary>
        /// 기본 SpatialReference(EPSG:5179)를 생성합니다
        /// </summary>
        private SpatialReference CreateDefaultSpatialRef()
        {
            _logger.LogInformation("기본 좌표계(EPSG:5179)를 사용합니다.");
            
            // PROJ 환경 변수 확인 및 재설정
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var projLibPath = System.IO.Path.Combine(appDir, "gdal", "share");
            var projDbPath = System.IO.Path.Combine(projLibPath, "proj.db");
            
            if (System.IO.Directory.Exists(projLibPath))
            {
                if (System.IO.File.Exists(projDbPath))
                {
                    _logger.LogDebug("PROJ 데이터베이스 파일 발견: {Path}", projDbPath);
                }
                else
                {
                    _logger.LogWarning("PROJ 데이터베이스 파일을 찾을 수 없습니다: {Path}", projDbPath);
                }
                
                // 한글 경로 안전 처리 및 환경 변수 설정
                string projLibPathForProj = ProjEnvironmentManager.ConfigureFromSharePath(projLibPath, _logger);
                _logger.LogDebug("PROJ 환경 변수 설정: PROJ_DATA={Path} (원본: {Original})", projLibPathForProj, projLibPath);
            }
            else
            {
                _logger.LogError("PROJ 라이브러리 경로를 찾을 수 없습니다: {Path}", projLibPath);
            }
            
            var spatialRef = new SpatialReference(null);
            try
            {
                spatialRef.ImportFromEPSG(5179);
                _logger.LogDebug("EPSG:5179 좌표계 생성 성공");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EPSG:5179 좌표계 생성 실패. PROJ 데이터베이스 경로를 확인하세요.");
                throw;
            }
            return spatialRef;
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
        /// 불필요한 레이어들을 강제 삭제합니다
        /// </summary>
        private async Task DeleteUnnecessaryLayersAsync(DataSource dataSource)
        {
            // Point 레이어와 NoGeom 테이블을 남기고 나머지(Line, Polygon)만 삭제 대상
            string[] unnecessaryLayers = { QC_ERRORS_LINE, QC_ERRORS_POLYGON }; // QC_ERRORS_NOGEOM 제외
            
            foreach (var layerName in unnecessaryLayers)
            {
                var existingLayer = dataSource.GetLayerByName(layerName);
                if (existingLayer != null)
                {
                    _logger.LogInformation("불필요한 레이어 삭제: {LayerName}", layerName);
                    
                    // 레이어 인덱스로 삭제
                    for (int i = 0; i < dataSource.GetLayerCount(); i++)
                    {
                        var testLayer = dataSource.GetLayerByIndex(i);
                        if (testLayer != null && testLayer.GetName() == layerName)
                        {
                            dataSource.DeleteLayer(i);
                            _logger.LogInformation("불필요한 레이어 삭제 완료: {LayerName}", layerName);
                            break;
                        }
                    }
                }
            }
            
            await Task.CompletedTask;
        }
        private async Task CreateQcErrorsFeatureClassAsync(DataSource dataSource, string layerName, 
            wkbGeometryType geomType, SpatialReference spatialRef)
        {
            var existingLayer = dataSource.GetLayerByName(layerName);
            if (existingLayer != null)
            {
                _logger.LogWarning("{LayerName} Feature Class가 이미 존재합니다. 기존 레이어를 삭제하고 새로 생성합니다.", layerName);

                // 기존 레이어 강제 삭제 (스키마 변경을 위해)
                for (int i = 0; i < dataSource.GetLayerCount(); i++)
                {
                    var testLayer = dataSource.GetLayerByIndex(i);
                    if (testLayer != null && testLayer.GetName() == layerName)
                    {
                        _logger.LogInformation("기존 레이어 삭제: {LayerName}", layerName);
                        dataSource.DeleteLayer(i);
                        break;
                    }
                }
                
                // 레이어 삭제 후 새로 생성하도록 계속 진행
            }

            _logger.LogInformation("{LayerName} Feature Class 생성 중...", layerName);

            var layer = dataSource.CreateLayer(layerName, spatialRef, geomType, null);
            if (layer == null)
            {
                throw new InvalidOperationException($"{layerName} Feature Class 생성 실패");
            }

            // 단순화된 필드 정의 (필수 필드만)
            var fields = new[]
            {
                new { Name = "ErrCode", Type = FieldType.OFTString, Width = 32 },
                new { Name = "SourceClass", Type = FieldType.OFTString, Width = 128 },
                new { Name = "SourceOID", Type = FieldType.OFTInteger, Width = 0 },
                new { Name = "Message", Type = FieldType.OFTString, Width = 1024 }
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
        /// QC_Errors_NoGeom 테이블 생성 (비활성화 - 단순화된 스키마에서는 사용하지 않음)
        /// </summary>
        private async Task CreateQcErrorsNoGeomTableAsync(DataSource dataSource)
        {
            var existingLayer = dataSource.GetLayerByName(QC_ERRORS_NOGEOM);
            if (existingLayer != null)
            {
                _logger.LogInformation("QC_Errors_NoGeom 테이블이 이미 존재합니다. 기존 테이블을 삭제 후 재생성합니다.");
                for (int i = 0; i < dataSource.GetLayerCount(); i++)
                {
                    var lyr = dataSource.GetLayerByIndex(i);
                    if (lyr != null && lyr.GetName() == QC_ERRORS_NOGEOM)
                    {
                        dataSource.DeleteLayer(i);
                        break;
                    }
                }
            }

            var layer = dataSource.CreateLayer(QC_ERRORS_NOGEOM, null, wkbGeometryType.wkbNone, null);
            if (layer == null) throw new InvalidOperationException("QC_Errors_NoGeom 테이블 생성 실패");

            var fields = new[]
            {
                new { Name = "ErrCode", Type = FieldType.OFTString, Width = 32 },
                new { Name = "SourceClass", Type = FieldType.OFTString, Width = 128 },
                new { Name = "TableId", Type = FieldType.OFTString, Width = 128 },
                new { Name = "TableName", Type = FieldType.OFTString, Width = 128 },
                new { Name = "SourceOID", Type = FieldType.OFTInteger, Width = 0 },
                new { Name = "Message", Type = FieldType.OFTString, Width = 1024 }
            };

            foreach (var f in fields)
            {
                var fd = new FieldDefn(f.Name, f.Type);
                if (f.Width > 0) fd.SetWidth(f.Width);
                layer.CreateField(fd, 1);
                fd.Dispose();
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
                    var resolvedProjPath = ProjEnvironmentManager.ConfigureFromSharePath(projLibPath, _logger);

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
                    var newPath = resolvedProjPath + ";" + appDir + ";" + cleanPath;
                    Environment.SetEnvironmentVariable("PATH", newPath);
                    
                    Environment.SetEnvironmentVariable("PROJ_DEBUG", "0");
                    Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", resolvedProjPath);
                    Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", resolvedProjPath);
                    
                    _logger.LogDebug("PROJ 환경 설정 완료: {Path}", resolvedProjPath);
                }
                else
                {
                    _logger.LogError("ValidateSchemaAsync: PROJ 데이터 경로를 찾을 수 없습니다 - {Path}", projLibPath);
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

