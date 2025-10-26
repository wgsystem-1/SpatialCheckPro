using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 기존 검수 결과를 QC_Errors 형태로 변환하는 서비스
    /// </summary>
    public class ValidationResultConverter
    {
        /// <summary>
        /// 검수 결과에서 오류를 지오메트리/비지오메트리로 분류합니다
        /// </summary>
        public ErrorClassificationSummary ClassifyErrors(ValidationResult validationResult)
        {
            var summary = new ErrorClassificationSummary();

            // 0단계
            if (validationResult.FileGdbCheckResult != null)
            {
                AccumulateNonGeometry(summary, "FILEGDB", validationResult.FileGdbCheckResult.ErrorCount);
            }

            // 1단계
            if (validationResult.TableCheckResult != null)
            {
                var t = validationResult.TableCheckResult;
                AccumulateNonGeometry(summary, "TABLE_MISSING", t.TableResults?.Count(x => string.Equals(x.TableExistsCheck, "N", StringComparison.OrdinalIgnoreCase)) ?? 0);
                AccumulateNonGeometry(summary, "TABLE_ZERO_FEATURES", t.TableResults?.Count(x => string.Equals(x.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase) && (x.FeatureCount ?? 0) == 0) ?? 0);
            }

            // 2단계
            if (validationResult.SchemaCheckResult != null)
            {
                AccumulateNonGeometry(summary, "SCHEMA", validationResult.SchemaCheckResult.ErrorCount);
            }

            // 3단계 (지오메트리)
            if (validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var r in validationResult.GeometryCheckResult.GeometryResults)
                {
                    AccumulateGeometry(summary, "GEOM_DUPLICATE", r.DuplicateCount);
                    AccumulateGeometry(summary, "GEOM_OVERLAP", r.OverlapCount);
                    AccumulateGeometry(summary, "GEOM_SELF_INTERSECTION", r.SelfIntersectionCount);
                    AccumulateGeometry(summary, "GEOM_SELF_OVERLAP", r.SelfOverlapCount);
                    AccumulateGeometry(summary, "GEOM_SLIVER", r.SliverCount);
                    AccumulateGeometry(summary, "GEOM_SPIKE", r.SpikeCount);
                    AccumulateGeometry(summary, "GEOM_SHORT_OBJECT", r.ShortObjectCount);
                    AccumulateGeometry(summary, "GEOM_SMALL_AREA", r.SmallAreaCount);
                    AccumulateGeometry(summary, "GEOM_POLYGON_IN_POLYGON", r.PolygonInPolygonCount);
                    AccumulateGeometry(summary, "GEOM_MIN_POINT", r.MinPointCount);
                    AccumulateGeometry(summary, "GEOM_UNDERSHOOT", r.UndershootCount);
                    AccumulateGeometry(summary, "GEOM_OVERSHOOT", r.OvershootCount);
                }
            }

            // 4단계 (관계 – 공간관계지만 결과 표현은 비지오메트리 카테고리로 분리 유지)
            if (validationResult.RelationCheckResult != null)
            {
                AccumulateNonGeometry(summary, "REL", validationResult.RelationCheckResult.ErrorCount);
            }

            // 5단계 (속성관계)
            if (validationResult.AttributeRelationCheckResult != null)
            {
                AccumulateNonGeometry(summary, "ATTR_REL", validationResult.AttributeRelationCheckResult.ErrorCount);
            }

            return summary;
        }

        private static void AccumulateGeometry(ErrorClassificationSummary s, string code, int count)
        {
            if (count <= 0) return;
            s.GeometryErrorCount += count;
            if (!s.GeometryByType.ContainsKey(code)) s.GeometryByType[code] = 0;
            s.GeometryByType[code] += count;
        }

        private static void AccumulateNonGeometry(ErrorClassificationSummary s, string code, int count)
        {
            if (count <= 0) return;
            s.NonGeometryErrorCount += count;
            if (!s.NonGeometryByType.ContainsKey(code)) s.NonGeometryByType[code] = 0;
            s.NonGeometryByType[code] += count;
        }
        private readonly ILogger<ValidationResultConverter> _logger;

        public ValidationResultConverter(ILogger<ValidationResultConverter> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// ValidationResult를 QcError 목록으로 변환합니다
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <param name="runId">실행 ID</param>
        /// <returns>QcError 목록</returns>
        public List<QcError> ConvertValidationResultToQcErrors(ValidationResult validationResult, Guid runId)
        {
            var qcErrors = new List<QcError>();

            try
            {
                _logger.LogInformation("ValidationResult를 QcError로 변환 시작: {ValidationId}", validationResult.ValidationId);

                // 1단계: 테이블 검수 결과 변환
                if (validationResult.TableCheckResult?.TableResults != null)
                {
                    foreach (var tableResult in validationResult.TableCheckResult.TableResults)
                    {
                        qcErrors.AddRange(ConvertTableValidationItem(tableResult, runId));
                    }
                }

                // 2단계: 스키마 검수 결과 변환
                if (validationResult.SchemaCheckResult?.SchemaResults != null)
                {
                    foreach (var schemaResult in validationResult.SchemaCheckResult.SchemaResults)
                    {
                        qcErrors.AddRange(ConvertSchemaValidationItem(schemaResult, runId));
                    }
                }

                // 3단계: 지오메트리 검수 결과 변환
                if (validationResult.GeometryCheckResult?.GeometryResults != null)
                {
                    foreach (var geometryResult in validationResult.GeometryCheckResult.GeometryResults)
                    {
                        qcErrors.AddRange(ConvertGeometryValidationItem(geometryResult, runId));
                    }
                }

                // 4단계: 관계 검수 결과 변환 (현재 미구현)
                // 관계 검수 결과 구조가 정의되면 추가 구현 필요

                _logger.LogInformation("ValidationResult 변환 완료: {Count}개 QcError 생성", qcErrors.Count);
                return qcErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ValidationResult 변환 중 오류 발생");
                return qcErrors;
            }
        }

        /// <summary>
        /// 테이블 검수 결과를 QcError로 변환
        /// </summary>
        private List<QcError> ConvertTableValidationItem(TableValidationItem tableResult, Guid runId)
        {
            var qcErrors = new List<QcError>();

            // 테이블 검수에서 오류가 있는 경우만 QcError 생성
            if (!tableResult.IsValid)
            {
                var qcError = new QcError
                {
                    GlobalID = Guid.NewGuid().ToString(),
                    ErrType = QcErrorType.SCHEMA.ToString(),
                    ErrCode = GenerateErrorCode("TBL", tableResult.TableId ?? "Unknown"),
                    Severity = string.Empty,
                    Status = string.Empty,
                    RuleId = $"TABLE_CHECK_{tableResult.TableId}",
                    SourceClass = tableResult.TableName ?? "Unknown",
                    SourceOID = 0, // 테이블 검수는 특정 객체 없음
                    SourceGlobalID = null,
                    Message = $"테이블 검수 실패: {tableResult.TableName}",
                    DetailsJSON = JsonSerializer.Serialize(new
                    {
                        CheckType = "테이블 검수",
                        TableId = tableResult.TableId,
                        TableName = tableResult.TableName,
                        ExpectedFeatureType = tableResult.FeatureType,
                        ActualFeatureType = tableResult.ActualFeatureType,
                        FeatureCount = tableResult.FeatureCount,
                        IsValid = tableResult.IsValid
                    }),
                    RunID = runId.ToString(),
                    Geometry = null
                };

                qcErrors.Add(qcError);
            }

            return qcErrors;
        }

        /// <summary>
        /// 스키마 검수 결과를 QcError로 변환
        /// </summary>
        private List<QcError> ConvertSchemaValidationItem(SchemaValidationItem schemaResult, Guid runId)
        {
            var qcErrors = new List<QcError>();

            // 스키마 검수에서 오류가 있는 경우만 QcError 생성
            if (!schemaResult.IsValid)
            {
                var qcError = new QcError
                {
                    GlobalID = Guid.NewGuid().ToString(),
                    ErrType = QcErrorType.SCHEMA.ToString(),
                    ErrCode = GenerateErrorCode("SCH", schemaResult.ColumnName ?? "Unknown"),
                    Severity = QcSeverity.MAJOR.ToString(),
                    Status = QcStatus.OPEN.ToString(),
                    RuleId = $"SCHEMA_CHECK_{schemaResult.TableId}_{schemaResult.ColumnName}",
                    SourceClass = schemaResult.TableId ?? "Unknown",
                    SourceOID = 0, // 스키마 검수는 특정 객체 없음
                    SourceGlobalID = null,
                    Message = $"스키마 검수 실패: {schemaResult.TableId}.{schemaResult.ColumnName}",
                    DetailsJSON = JsonSerializer.Serialize(new
                    {
                        CheckType = "스키마 검수",
                        TableId = schemaResult.TableId,
                        ColumnName = schemaResult.ColumnName,
                        ExpectedDataType = schemaResult.ExpectedDataType,
                        ActualDataType = schemaResult.ActualDataType,
                        ExpectedLength = schemaResult.ExpectedLength,
                        ActualLength = schemaResult.ActualLength,
                        ColumnExists = schemaResult.ColumnExists,
                        DataTypeMatches = schemaResult.DataTypeMatches,
                        LengthMatches = schemaResult.LengthMatches,
                        NotNullMatches = schemaResult.NotNullMatches,
                        UniqueKeyMatches = schemaResult.UniqueKeyMatches,
                        ForeignKeyMatches = schemaResult.ForeignKeyMatches
                    }),
                    RunID = runId.ToString(),
                    Geometry = null
                };

                qcErrors.Add(qcError);
            }

            return qcErrors;
        }

        /// <summary>
        /// 지오메트리 검수 결과를 QcError로 변환
        /// </summary>
        private List<QcError> ConvertGeometryValidationItem(GeometryValidationItem geometryResult, Guid runId)
        {
            var qcErrors = new List<QcError>();

            // 지오메트리 검수에서 오류가 있는 경우 각 오류별로 QcError 생성
            if (geometryResult.ErrorDetails != null)
            {
                foreach (var errorDetail in geometryResult.ErrorDetails)
                {
                    // WKT로부터 Geometry 객체 생성
                    Geometry? geometry = null;
                    if (!string.IsNullOrWhiteSpace(errorDetail.GeometryWkt))
                    {
                        try
                        {
                            string wkt = errorDetail.GeometryWkt;
                            geometry = Ogr.CreateGeometryFromWkt(ref wkt, null);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "WKT로부터 지오메트리 생성 실패: {Wkt}", errorDetail.GeometryWkt);
                        }
                    }

                    // 좌표/WKT 보완을 위해 사전 계산
                    double outX = errorDetail.X;
                    double outY = errorDetail.Y;
                    if ((outX == 0 && outY == 0) && geometry != null)
                    {
                        try
                        {
                            // 지오메트리 타입에 따른 대표 좌표 추출 (첫 점 또는 엔벨로프 중심)
                            switch (geometry.GetGeometryType())
                            {
                                case wkbGeometryType.wkbPoint:
                                {
                                    var p = new double[3];
                                    geometry.GetPoint(0, p);
                                    outX = p[0]; outY = p[1];
                                    break;
                                }
                                case wkbGeometryType.wkbMultiPoint:
                                {
                                    if (geometry.GetGeometryCount() > 0)
                                    {
                                        var first = geometry.GetGeometryRef(0);
                                        if (first != null)
                                        {
                                            var p = new double[3];
                                            first.GetPoint(0, p);
                                            outX = p[0]; outY = p[1];
                                        }
                                    }
                                    break;
                                }
                                case wkbGeometryType.wkbLineString:
                                {
                                    if (geometry.GetPointCount() > 0)
                                    {
                                        var p = new double[3];
                                        geometry.GetPoint(0, p);
                                        outX = p[0]; outY = p[1];
                                    }
                                    break;
                                }
                                case wkbGeometryType.wkbMultiLineString:
                                {
                                    if (geometry.GetGeometryCount() > 0)
                                    {
                                        var firstLine = geometry.GetGeometryRef(0);
                                        if (firstLine != null && firstLine.GetPointCount() > 0)
                                        {
                                            var p = new double[3];
                                            firstLine.GetPoint(0, p);
                                            outX = p[0]; outY = p[1];
                                        }
                                    }
                                    break;
                                }
                                case wkbGeometryType.wkbPolygon:
                                {
                                    if (geometry.GetGeometryCount() > 0)
                                    {
                                        var ring = geometry.GetGeometryRef(0);
                                        if (ring != null && ring.GetPointCount() > 0)
                                        {
                                            var p = new double[3];
                                            ring.GetPoint(0, p);
                                            outX = p[0]; outY = p[1];
                                        }
                                    }
                                    break;
                                }
                                case wkbGeometryType.wkbMultiPolygon:
                                {
                                    if (geometry.GetGeometryCount() > 0)
                                    {
                                        var poly = geometry.GetGeometryRef(0);
                                        if (poly != null && poly.GetGeometryCount() > 0)
                                        {
                                            var ring = poly.GetGeometryRef(0);
                                            if (ring != null && ring.GetPointCount() > 0)
                                            {
                                                var p = new double[3];
                                                ring.GetPoint(0, p);
                                                outX = p[0]; outY = p[1];
                                            }
                                        }
                                    }
                                    break;
                                }
                                default:
                                {
                                    var env = new Envelope();
                                    geometry.GetEnvelope(env);
                                    outX = (env.MinX + env.MaxX) / 2.0;
                                    outY = (env.MinY + env.MaxY) / 2.0;
                                    break;
                                }
                            }
                        }
                        catch { /* 좌표 보완 실패 시 무시 */ }
                    }

                    var qcError = new QcError
                    {
                        GlobalID = Guid.NewGuid().ToString(),
                        ErrType = QcErrorType.GEOM.ToString(),
                        ErrCode = GenerateErrorCode("GEO", errorDetail.ErrorType ?? "Unknown"),
                        Severity = string.Empty,
                        Status = string.Empty,
                        RuleId = $"GEOMETRY_CHECK_{geometryResult.TableId}_{errorDetail.ErrorType}",
                        SourceClass = geometryResult.TableId ?? "Unknown",
                        SourceOID = ParseSourceOID(errorDetail.ObjectId),
                        SourceGlobalID = null,
                        Message = errorDetail.DetailMessage ?? "지오메트리 오류",
                        DetailsJSON = JsonSerializer.Serialize(new
                        {
                            CheckType = "지오메트리 검수",
                            TableId = geometryResult.TableId,
                            CheckType_Detail = geometryResult.CheckType,
                            ErrorType = errorDetail.ErrorType,
                            ObjectId = errorDetail.ObjectId,
                            ErrorValue = errorDetail.ErrorValue,
                            ThresholdValue = errorDetail.ThresholdValue,
                            DetailMessage = errorDetail.DetailMessage
                        }),
                        RunID = runId.ToString(),
                        Geometry = geometry, // 생성된 지오메트리 객체 할당
                        GeometryWKT = errorDetail.GeometryWkt,
                        GeometryType = QcError.DetermineGeometryType(errorDetail.GeometryWkt),
                        X = outX,
                        Y = outY,
                        ErrorValue = errorDetail.ErrorValue,
                        ThresholdValue = errorDetail.ThresholdValue
                    };

                    qcErrors.Add(qcError);
                }
            }

            return qcErrors;
        }

        /// <summary>
        /// 관계 검수 결과를 QcError로 변환
        /// </summary>
        private List<QcError> ConvertRelationCheckResult(CheckResult checkResult, Guid runId)
        {
            var qcErrors = new List<QcError>();

            foreach (var error in checkResult.Errors)
            {
                var qcError = new QcError
                {
                    GlobalID = Guid.NewGuid().ToString(),
                    ErrType = QcErrorType.REL.ToString(),
                    ErrCode = GenerateErrorCode("REL", checkResult.CheckId),
                    Severity = string.Empty,
                    Status = string.Empty,
                    RuleId = $"RELATION_CHECK_{checkResult.CheckId}",
                    SourceClass = error.TableName ?? "Unknown",
                    SourceOID = ParseSourceOID(error.FeatureId),
                    SourceGlobalID = null, // 필요시 추후 설정
                    Message = error.Message,
                    DetailsJSON = JsonSerializer.Serialize(new
                    {
                        CheckType = "관계 검수",
                        CheckName = checkResult.CheckName,
                        ErrorSeverity = error.Severity.ToString(),
                        Location = error.Location,
                        RelatedTable = error.Metadata?.GetValueOrDefault("RelatedTable"),
                        RelatedFeatureId = error.Metadata?.GetValueOrDefault("RelatedFeatureId"),
                        RelationType = error.Metadata?.GetValueOrDefault("RelationType"),
                        Metadata = error.Metadata
                    }),
                    RunID = runId.ToString(),
                    Geometry = CreateGeometryFromLocation(error.Location)
                };

                qcErrors.Add(qcError);
            }

            return qcErrors;
        }

        /// <summary>
        /// 오류 코드 생성
        /// </summary>
        private string GenerateErrorCode(string prefix, string checkId)
        {
            // 체크 ID를 기반으로 3자리 숫자 생성
            var hash = Math.Abs(checkId.GetHashCode()) % 1000;
            return $"{prefix}{hash:D3}";
        }

        /// <summary>
        /// 심각도 결정
        /// </summary>
        private QcSeverity DetermineSeverity(ErrorSeverity errorSeverity)
        {
            return errorSeverity switch
            {
                ErrorSeverity.Critical => QcSeverity.CRIT,
                ErrorSeverity.Error => QcSeverity.MAJOR,
                ErrorSeverity.Warning => QcSeverity.MINOR,
                ErrorSeverity.Info => QcSeverity.INFO,
                _ => QcSeverity.MINOR
            };
        }

        /// <summary>
        /// FeatureId에서 SourceOID 파싱
        /// </summary>
        private long ParseSourceOID(string? featureId)
        {
            if (string.IsNullOrEmpty(featureId))
                return 0;

            // "OBJ_12345" 형태에서 숫자 부분 추출
            if (featureId.StartsWith("OBJ_"))
            {
                if (long.TryParse(featureId.Substring(4), out long oid))
                    return oid;
            }

            // 순수 숫자인 경우
            if (long.TryParse(featureId, out long directOid))
                return directOid;

            return 0;
        }

        /// <summary>
        /// GeographicLocation에서 GDAL Geometry 생성
        /// </summary>
        private Geometry? CreateGeometryFromLocation(GeographicLocation? location)
        {
            if (location == null)
                return null;

            try
            {
                var point = new Geometry(wkbGeometryType.wkbPoint);
                point.AddPoint(location.X, location.Y, location.Z);
                return point;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "지오메트리 생성 실패: {Location}", location);
                return null;
            }
        }

        /// <summary>
        /// QcRun 객체 생성
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <param name="targetFilePath">대상 파일 경로</param>
        /// <param name="executedBy">실행자</param>
        /// <returns>QcRun 객체</returns>
        public QcRun CreateQcRun(ValidationResult validationResult, string targetFilePath, string? executedBy = null)
        {
            var qcRun = new QcRun
            {
                GlobalID = Guid.NewGuid().ToString(),
                RunName = $"검수_{DateTime.Now:yyyyMMdd_HHmmss}",
                TargetFilePath = targetFilePath,
                RulesetVersion = "1.0",
                StartTimeUTC = validationResult.StartedAt,
                EndTimeUTC = validationResult.CompletedAt,
                ExecutedBy = executedBy ?? Environment.UserName,
                Status = DetermineRunStatus(validationResult.Status).ToString(),
                TotalErrors = validationResult.TotalErrors,
                TotalWarnings = validationResult.TotalWarnings,
                ResultSummary = JsonSerializer.Serialize(new
                {
                    ValidationId = validationResult.ValidationId,
                    TargetFile = Path.GetFileName(validationResult.TargetFile ?? "Unknown"),
                    TableCheckErrors = validationResult.TableCheckResult?.ErrorCount ?? 0,
                    SchemaCheckErrors = validationResult.SchemaCheckResult?.ErrorCount ?? 0,
                    GeometryCheckErrors = validationResult.GeometryCheckResult?.ErrorCount ?? 0,
                    RelationCheckErrors = validationResult.RelationCheckResult?.ErrorCount ?? 0
                }),
                ConfigInfo = JsonSerializer.Serialize(new
                {
                    TableCheckEnabled = validationResult.TableCheckResult != null,
                    SchemaCheckEnabled = validationResult.SchemaCheckResult != null,
                    GeometryCheckEnabled = validationResult.GeometryCheckResult != null,
                    RelationCheckEnabled = validationResult.RelationCheckResult != null
                })
            };

            return qcRun;
        }

        /// <summary>
        /// ValidationStatus를 QcRunStatus로 변환
        /// </summary>
        private QcRunStatus DetermineRunStatus(ValidationStatus validationStatus)
        {
            return validationStatus switch
            {
                ValidationStatus.Running => QcRunStatus.RUNNING,
                ValidationStatus.Completed => QcRunStatus.COMPLETED,
                ValidationStatus.Failed => QcRunStatus.FAILED,
                ValidationStatus.Cancelled => QcRunStatus.CANCELLED,
                _ => QcRunStatus.COMPLETED
            };
        }

        /// <summary>
        /// CheckResult의 ValidationError들을 QcError로 변환합니다 (비지오메트리 일반용)
        /// </summary>
        public List<QcError> ToQcErrorsFromCheckResult(CheckResult? checkResult, string errType, string runId)
        {
            var list = new List<QcError>();
            if (checkResult == null || checkResult.Errors == null || checkResult.Errors.Count == 0)
            {
                return list;
            }

            foreach (var e in checkResult.Errors)
            {
                var sourceClass = !string.IsNullOrWhiteSpace(e.TableName)
                    ? e.TableName
                    : (!string.IsNullOrWhiteSpace(e.SourceTable) ? e.SourceTable! : (e.TargetTable ?? ""));

                long sourceOid = 0;
                if (e.SourceObjectId.HasValue) sourceOid = e.SourceObjectId.Value;
                else if (!string.IsNullOrWhiteSpace(e.FeatureId) && long.TryParse(e.FeatureId, out var parsed)) sourceOid = parsed;

                var details = new Dictionary<string, object?>
                {
                    ["FieldName"] = e.FieldName,
                    ["ActualValue"] = e.ActualValue,
                    ["ExpectedValue"] = e.ExpectedValue,
                    ["TargetTable"] = e.TargetTable,
                    ["TargetObjectId"] = e.TargetObjectId,
                    ["ErrorCode"] = e.ErrorCode,
                    ["Severity"] = e.Severity.ToString(),
                    ["ErrorTypeEnum"] = e.ErrorType.ToString(),
                    ["OccurredAt"] = e.OccurredAt,
                    ["Metadata"] = e.Metadata,
                    ["Details"] = e.Details
                };

                var qc = new QcError
                {
                    GlobalID = Guid.NewGuid().ToString(),
                    ErrType = errType,
                    ErrCode = string.IsNullOrWhiteSpace(e.ErrorCode) ? errType : e.ErrorCode,
                    Severity = DetermineSeverity(e.Severity).ToString(),
                    Status = QcStatus.OPEN.ToString(),
                    RuleId = $"{errType}_{e.ErrorCode ?? e.ErrorType.ToString()}",
                    SourceClass = sourceClass,
                    SourceOID = sourceOid,
                    SourceGlobalID = null,
                    X = e.X ?? 0,
                    Y = e.Y ?? 0,
                    GeometryWKT = e.GeometryWKT,
                    GeometryType = QcError.DetermineGeometryType(e.GeometryWKT),
                    Geometry = null,
                    ErrorValue = e.ActualValue ?? string.Empty,
                    ThresholdValue = e.ExpectedValue ?? string.Empty,
                    Message = e.Message,
                    DetailsJSON = JsonSerializer.Serialize(details),
                    RunID = runId,
                    CreatedUTC = DateTime.UtcNow,
                    UpdatedUTC = DateTime.UtcNow
                };

                list.Add(qc);
            }

            return list;
        }

        /// <summary>
        /// 전체 ValidationResult에서 비지오메트리 QcError 목록 생성
        /// (FileGDB/테이블/스키마/관계/속성관계)
        /// </summary>
        public List<QcError> ToQcErrorsFromNonGeometryStages(ValidationResult validationResult, string runId)
        {
            var all = new List<QcError>();

            all.AddRange(ToQcErrorsFromCheckResult(validationResult.FileGdbCheckResult, "FILEGDB", runId));
            all.AddRange(ToQcErrorsFromCheckResult(validationResult.TableCheckResult, "TABLE", runId));
            all.AddRange(ToQcErrorsFromCheckResult(validationResult.SchemaCheckResult, "SCHEMA", runId));
            all.AddRange(ToQcErrorsFromCheckResult(validationResult.RelationCheckResult, "REL", runId));
            all.AddRange(ToQcErrorsFromCheckResult(validationResult.AttributeRelationCheckResult, "ATTR_REL", runId));

            return all;
        }
    }
}
