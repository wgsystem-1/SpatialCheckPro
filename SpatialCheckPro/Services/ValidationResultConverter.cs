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
                    Severity = QcSeverity.MAJOR.ToString(),
                    Status = QcStatus.OPEN.ToString(),
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

                    var qcError = new QcError
                    {
                        GlobalID = Guid.NewGuid().ToString(),
                        ErrType = QcErrorType.GEOM.ToString(),
                        ErrCode = GenerateErrorCode("GEO", errorDetail.ErrorType ?? "Unknown"),
                        Severity = QcSeverity.MAJOR.ToString(),
                        Status = QcStatus.OPEN.ToString(),
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
                        X = errorDetail.X,
                        Y = errorDetail.Y,
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
                    Severity = DetermineSeverity(error.Severity).ToString(),
                    Status = QcStatus.OPEN.ToString(),
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
    }
}
