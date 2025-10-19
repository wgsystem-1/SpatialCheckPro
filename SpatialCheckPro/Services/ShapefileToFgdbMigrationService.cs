using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 기존 SHP 오류 결과를 FGDB로 이관하는 서비스
    /// </summary>
    public class ShapefileToFgdbMigrationService
    {
        private readonly ILogger<ShapefileToFgdbMigrationService> _logger;
        private readonly QcErrorDataService _qcErrorDataService;
        private readonly FgdbSchemaService _schemaService;

        public ShapefileToFgdbMigrationService(
            ILogger<ShapefileToFgdbMigrationService> logger,
            QcErrorDataService qcErrorDataService,
            FgdbSchemaService schemaService)
        {
            _logger = logger;
            _qcErrorDataService = qcErrorDataService;
            _schemaService = schemaService;
        }

        /// <summary>
        /// SHP 오류 파일들을 FGDB로 이관합니다
        /// </summary>
        /// <param name="shapefileDirectory">SHP 파일들이 있는 디렉토리</param>
        /// <param name="targetGdbPath">대상 FGDB 경로</param>
        /// <param name="runId">실행 ID</param>
        /// <returns>이관된 오류 개수</returns>
        public async Task<int> MigrateShapefilesToFgdbAsync(string shapefileDirectory, string targetGdbPath, Guid runId)
        {
            try
            {
                _logger.LogInformation("SHP → FGDB 이관 시작: {ShapefileDirectory} → {TargetGdbPath}", 
                    shapefileDirectory, targetGdbPath);

                if (!Directory.Exists(shapefileDirectory))
                {
                    _logger.LogError("SHP 디렉토리가 존재하지 않습니다: {Directory}", shapefileDirectory);
                    return 0;
                }

                // FGDB 스키마 생성
                if (!await _schemaService.ValidateSchemaAsync(targetGdbPath))
                {
                    if (!await _schemaService.CreateQcErrorsSchemaAsync(targetGdbPath))
                    {
                        _logger.LogError("FGDB 스키마 생성 실패");
                        return 0;
                    }
                }

                int totalMigrated = 0;

                // 표준 SHP 오류 파일 패턴 검색
                var shapefilePatterns = new[]
                {
                    "*err_pt*.shp",    // Point 오류
                    "*err_ln*.shp",    // Line 오류  
                    "*err_pg*.shp",    // Polygon 오류
                    "*error_pt*.shp",  // 대체 패턴
                    "*error_ln*.shp",
                    "*error_pg*.shp"
                };

                foreach (var pattern in shapefilePatterns)
                {
                    var shapefiles = Directory.GetFiles(shapefileDirectory, pattern, SearchOption.AllDirectories);
                    
                    foreach (var shapefile in shapefiles)
                    {
                        var migrated = await MigrateSingleShapefileAsync(shapefile, targetGdbPath, runId);
                        totalMigrated += migrated;
                    }
                }

                _logger.LogInformation("SHP → FGDB 이관 완료: {TotalMigrated}개 오류 이관", totalMigrated);
                return totalMigrated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SHP → FGDB 이관 중 오류 발생");
                return 0;
            }
        }

        /// <summary>
        /// 단일 SHP 파일을 FGDB로 이관합니다
        /// </summary>
        private async Task<int> MigrateSingleShapefileAsync(string shapefilePath, string targetGdbPath, Guid runId)
        {
            try
            {
                _logger.LogInformation("SHP 파일 이관 시작: {ShapefilePath}", shapefilePath);

                var driver = Ogr.GetDriverByName("ESRI Shapefile");
                var dataSource = driver.Open(shapefilePath, 0); // 읽기 모드

                if (dataSource == null)
                {
                    _logger.LogError("SHP 파일을 열 수 없습니다: {ShapefilePath}", shapefilePath);
                    return 0;
                }

                var layer = dataSource.GetLayerByIndex(0);
                if (layer == null)
                {
                    _logger.LogError("SHP 레이어를 찾을 수 없습니다: {ShapefilePath}", shapefilePath);
                    dataSource.Dispose();
                    return 0;
                }

                var qcErrors = new List<QcError>();
                var featureCount = layer.GetFeatureCount(1);
                
                _logger.LogInformation("SHP 피처 개수: {FeatureCount}", featureCount);

                // 모든 피처 읽기
                layer.ResetReading();
                Feature feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    try
                    {
                        var qcError = ConvertShapefileFeatureToQcError(feature, shapefilePath, runId);
                        if (qcError != null)
                        {
                            qcErrors.Add(qcError);
                        }

                        processedCount++;
                        if (processedCount % 1000 == 0)
                        {
                            _logger.LogInformation("SHP 피처 처리 진행: {Processed}/{Total}", processedCount, featureCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SHP 피처 변환 실패: FID {FID}", feature.GetFID());
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                dataSource.Dispose();

                // FGDB에 배치 삽입
                var migratedCount = await _qcErrorDataService.BatchAppendQcErrorsAsync(targetGdbPath, qcErrors);
                
                _logger.LogInformation("SHP 파일 이관 완료: {ShapefilePath}, {MigratedCount}개 이관", 
                    Path.GetFileName(shapefilePath), migratedCount);

                return migratedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SHP 파일 이관 중 오류 발생: {ShapefilePath}", shapefilePath);
                return 0;
            }
        }

        /// <summary>
        /// SHP Feature를 QcError로 변환합니다
        /// </summary>
        private QcError? ConvertShapefileFeatureToQcError(Feature feature, string shapefilePath, Guid runId)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(shapefilePath);
                var geometry = feature.GetGeometryRef();

                // 필드 매핑 (일반적인 SHP 오류 파일 구조 가정)
                var qcError = new QcError
                {
                    GlobalID = Guid.NewGuid().ToString(),
                    ErrType = DetermineErrorTypeFromFilename(fileName),
                    ErrCode = GetFieldValueAsString(feature, "ERR_CODE") ?? GenerateErrorCodeFromFilename(fileName),
                    Severity = GetFieldValueAsString(feature, "SEVERITY") ?? QcSeverity.MAJOR.ToString(),
                    Status = QcStatus.OPEN.ToString(),
                    RuleId = GetFieldValueAsString(feature, "RULE_ID") ?? $"LEGACY_{fileName.ToUpper()}",
                    SourceClass = GetFieldValueAsString(feature, "SRC_CLASS") ?? ExtractSourceClassFromFilename(fileName),
                    SourceOID = GetFieldValueAsLong(feature, "SRC_OID") ?? feature.GetFID(),
                    SourceGlobalID = null, // SHP에는 GlobalID 없음
                    Message = GetFieldValueAsString(feature, "MESSAGE") ?? GetFieldValueAsString(feature, "ERR_MSG") ?? "레거시 오류",
                    DetailsJSON = CreateDetailsJsonFromShapefileFeature(feature, fileName),
                    RunID = runId.ToString(),
                    Geometry = geometry?.Clone() // 지오메트리 복사
                };

                return qcError;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SHP Feature 변환 실패: FID {FID}", feature.GetFID());
                return null;
            }
        }

        /// <summary>
        /// 파일명에서 오류 타입 결정
        /// </summary>
        private string DetermineErrorTypeFromFilename(string fileName)
        {
            var lowerFileName = fileName.ToLower();
            
            if (lowerFileName.Contains("geom") || lowerFileName.Contains("geo"))
                return QcErrorType.GEOM.ToString();
            
            if (lowerFileName.Contains("rel") || lowerFileName.Contains("relation"))
                return QcErrorType.REL.ToString();
            
            if (lowerFileName.Contains("attr") || lowerFileName.Contains("attribute"))
                return QcErrorType.ATTR.ToString();
            
            if (lowerFileName.Contains("schema") || lowerFileName.Contains("sch"))
                return QcErrorType.SCHEMA.ToString();

            // 기본값은 지오메트리 오류
            return QcErrorType.GEOM.ToString();
        }

        /// <summary>
        /// 파일명에서 오류 코드 생성
        /// </summary>
        private string GenerateErrorCodeFromFilename(string fileName)
        {
            var hash = Math.Abs(fileName.GetHashCode()) % 1000;
            return $"LEG{hash:D3}"; // Legacy Error Code
        }

        /// <summary>
        /// 파일명에서 소스 클래스 추출
        /// </summary>
        private string ExtractSourceClassFromFilename(string fileName)
        {
            // 파일명에서 테이블명 추출 시도
            // 예: "tn_buld_err_pt" → "tn_buld"
            var parts = fileName.Split('_');
            if (parts.Length >= 2)
            {
                // "err", "error", "pt", "ln", "pg" 등을 제외한 부분 조합
                var tableParts = new List<string>();
                foreach (var part in parts)
                {
                    var lowerPart = part.ToLower();
                    if (lowerPart != "err" && lowerPart != "error" && 
                        lowerPart != "pt" && lowerPart != "ln" && lowerPart != "pg")
                    {
                        tableParts.Add(part);
                    }
                }
                
                if (tableParts.Count > 0)
                {
                    return string.Join("_", tableParts);
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// SHP Feature에서 상세 정보 JSON 생성
        /// </summary>
        private string CreateDetailsJsonFromShapefileFeature(Feature feature, string fileName)
        {
            var details = new Dictionary<string, object>
            {
                ["SourceFile"] = fileName,
                ["MigratedFrom"] = "Shapefile",
                ["OriginalFID"] = feature.GetFID()
            };

            // 모든 필드 값 추가
            var layerDefn = feature.GetDefnRef();
            for (int i = 0; i < layerDefn.GetFieldCount(); i++)
            {
                var fieldDefn = layerDefn.GetFieldDefn(i);
                var fieldName = fieldDefn.GetName();
                var fieldValue = GetFieldValueAsString(feature, fieldName);
                
                if (!string.IsNullOrEmpty(fieldValue))
                {
                    details[fieldName] = fieldValue;
                }
            }

            return System.Text.Json.JsonSerializer.Serialize(details);
        }

        /// <summary>
        /// Feature에서 문자열 필드 값 가져오기
        /// </summary>
        private string? GetFieldValueAsString(Feature feature, string fieldName)
        {
            try
            {
                var fieldIndex = feature.GetFieldIndex(fieldName);
                if (fieldIndex >= 0 && feature.IsFieldSet(fieldIndex))
                {
                    return feature.GetFieldAsString(fieldIndex);
                }
            }
            catch
            {
                // 필드가 없거나 접근 실패 시 무시
            }
            return null;
        }

        /// <summary>
        /// Feature에서 정수 필드 값 가져오기
        /// </summary>
        private long? GetFieldValueAsLong(Feature feature, string fieldName)
        {
            try
            {
                var fieldIndex = feature.GetFieldIndex(fieldName);
                if (fieldIndex >= 0 && feature.IsFieldSet(fieldIndex))
                {
                    return feature.GetFieldAsInteger64(fieldIndex);
                }
            }
            catch
            {
                // 필드가 없거나 접근 실패 시 무시
            }
            return null;
        }

        /// <summary>
        /// 이관 진행 상황 보고를 위한 이벤트
        /// </summary>
        public event EventHandler<MigrationProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 진행 상황 보고
        /// </summary>
        private void ReportProgress(string message, int processed, int total)
        {
            ProgressUpdated?.Invoke(this, new MigrationProgressEventArgs
            {
                Message = message,
                ProcessedCount = processed,
                TotalCount = total,
                ProgressPercentage = total > 0 ? (double)processed / total * 100 : 0
            });
        }
    }

    /// <summary>
    /// 이관 진행 상황 이벤트 인자
    /// </summary>
    public class MigrationProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
        public double ProgressPercentage { get; set; }
    }
}
