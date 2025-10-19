using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.Text.Json;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 관계 검수 오류를 QC_ERRORS 시스템과 통합하는 서비스
    /// </summary>
    public class RelationErrorsIntegrator
    {
        private readonly ILogger<RelationErrorsIntegrator> _logger;
        private readonly QcErrorService _qcErrorService;

        public RelationErrorsIntegrator(ILogger<RelationErrorsIntegrator> logger, QcErrorService qcErrorService)
        {
            _logger = logger;
            _qcErrorService = qcErrorService;
        }

        /// <summary>
        /// 관계 검수 결과를 QC_ERRORS 시스템에 저장합니다
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="relationResult">관계 검수 결과</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <param name="sourceGdbPath">원본 FileGDB 경로</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> SaveRelationValidationResultAsync(
            string qcErrorsGdbPath, 
            RelationValidationResult relationResult, 
            string runId, 
            string sourceGdbPath)
        {
            try
            {
                _logger.LogInformation("관계 검수 결과 QC_ERRORS 저장 시작: 공간오류 {SpatialCount}개, 속성오류 {AttributeCount}개", 
                    relationResult.SpatialErrorCount, relationResult.AttributeErrorCount);

                var totalErrors = relationResult.TotalErrorCount;
                if (totalErrors == 0)
                {
                    _logger.LogInformation("저장할 관계 검수 오류가 없습니다 - 검수 통과");
                    return true;
                }

                // QC_ERRORS 데이터베이스 초기화
                var initResult = await _qcErrorService.InitializeQcErrorsDatabaseAsync(qcErrorsGdbPath);
                if (!initResult)
                {
                    _logger.LogError("QC_ERRORS 데이터베이스 초기화 실패");
                    return false;
                }

                var qcErrors = new List<QcError>();

                // 공간 관계 오류 변환
                foreach (var spatialError in relationResult.SpatialErrors)
                {
                    var qcError = ConvertSpatialRelationErrorToQcError(spatialError, runId);
                    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
                    // 좌표 미기록 시 GeometryWKT에서 좌표 추정(센터) 시도는 저장 단계에서 처리되므로 여기서는 통과
                    qcErrors.Add(qcError);
                }

                // 속성 관계 오류 변환
                foreach (var attributeError in relationResult.AttributeErrors)
                {
                    var qcError = ConvertAttributeRelationErrorToQcError(attributeError, runId);
                    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
                    qcErrors.Add(qcError);
                }

                _logger.LogInformation("QcError 변환 완료: {TotalErrors}개 오류를 {QcErrorCount}개 QcError로 변환", 
                    totalErrors, qcErrors.Count);

                // QC_ERRORS에 저장
                var successCount = 0;
                var failedCount = 0;

                foreach (var qcError in qcErrors)
                {
                var success = await SaveSingleQcErrorAsync(qcErrorsGdbPath, qcError);
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogWarning("QcError 저장 실패: {ErrorCode} ({SourceClass}:{SourceOID})", 
                            qcError.ErrCode, qcError.SourceClass, qcError.SourceOID);
                    }
                }

                var allSuccess = successCount == qcErrors.Count;
                _logger.LogInformation("관계 검수 결과 저장 완료: 성공 {Success}개, 실패 {Failed}개, 총 {Total}개", 
                    successCount, failedCount, qcErrors.Count);

                return allSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "관계 검수 결과 저장 중 오류 발생");
                return false;
            }
        }

        /// <summary>
        /// 공간 관계 오류를 QcError로 변환합니다
        /// </summary>
        /// <param name="spatialError">공간 관계 오류</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <returns>변환된 QcError</returns>
        private QcError ConvertSpatialRelationErrorToQcError(SpatialRelationError spatialError, string runId)
        {
            var qcError = new QcError
            {
                GlobalID = Guid.NewGuid().ToString(),
                ErrType = "REL",
                ErrCode = GetSpatialRelationErrorCode(spatialError.RelationType, spatialError.ErrorType),
                Severity = ConvertErrorSeverityToString(spatialError.Severity),
                Status = "OPEN",
                RuleId = $"SPATIAL_{spatialError.RelationType}_{spatialError.ErrorType}",
                SourceClass = spatialError.SourceLayer,
                SourceOID = spatialError.SourceObjectId,
                SourceGlobalID = null, // 향후 구현
                X = spatialError.ErrorLocationX,
                Y = spatialError.ErrorLocationY,
                GeometryWKT = string.IsNullOrWhiteSpace(spatialError.GeometryWKT) ? null : spatialError.GeometryWKT,
                GeometryType = DetermineGeometryTypeFromWKT(spatialError.GeometryWKT).ToUpperInvariant(),
                ErrorValue = spatialError.TargetObjectId?.ToString() ?? "",
                ThresholdValue = spatialError.TargetLayer,
                Message = spatialError.Message,
                RunID = runId,
                CreatedUTC = spatialError.DetectedAt,
                UpdatedUTC = DateTime.UtcNow
            };

            // 상세 정보를 JSON으로 저장
            var detailsDict = new Dictionary<string, object>
            {
                ["RelationType"] = spatialError.RelationType.ToString(),
                ["ErrorType"] = spatialError.ErrorType,
                ["SourceLayer"] = spatialError.SourceLayer,
                ["TargetLayer"] = spatialError.TargetLayer,
                ["SourceObjectId"] = spatialError.SourceObjectId,
                ["TargetObjectId"] = spatialError.TargetObjectId,
                ["DetectedAt"] = spatialError.DetectedAt,
                ["Properties"] = spatialError.Properties
            };

            qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

            return qcError;
        }

        /// <summary>
        /// 속성 관계 오류를 QcError로 변환합니다
        /// </summary>
        /// <param name="attributeError">속성 관계 오류</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <returns>변환된 QcError</returns>
        private QcError ConvertAttributeRelationErrorToQcError(AttributeRelationError attributeError, string runId)
        {
            var qcError = new QcError
            {
                GlobalID = Guid.NewGuid().ToString(),
                ErrType = "ATTR_REL",
                ErrCode = GetAttributeRelationErrorCode(attributeError.RuleName),
                Severity = ConvertErrorSeverityToString(attributeError.Severity),
                Status = "OPEN",
                RuleId = $"ATTR_REL_{attributeError.RuleName}",
                SourceClass = attributeError.TableName,
                SourceOID = attributeError.ObjectId,
                SourceGlobalID = null, // 향후 구현
                X = 0, // 속성 오류는 공간 위치가 없음
                Y = 0,
                GeometryWKT = null,
                GeometryType = "NoGeometry",
                ErrorValue = attributeError.ActualValue,
                ThresholdValue = attributeError.ExpectedValue,
                Message = attributeError.Message,
                RunID = runId,
                CreatedUTC = attributeError.DetectedAt,
                UpdatedUTC = DateTime.UtcNow
            };

            // 상세 정보를 JSON으로 저장
            var detailsDict = new Dictionary<string, object>
            {
                ["RuleName"] = attributeError.RuleName,
                ["FieldName"] = attributeError.FieldName,
                ["TableName"] = attributeError.TableName,
                ["ExpectedValue"] = attributeError.ExpectedValue,
                ["ActualValue"] = attributeError.ActualValue,
                ["Details"] = attributeError.Details,
                ["SuggestedFix"] = attributeError.SuggestedFix ?? "",
                ["RelatedTableName"] = attributeError.RelatedTableName ?? "",
                ["RelatedObjectId"] = attributeError.RelatedObjectId,
                ["DetectedAt"] = attributeError.DetectedAt,
                ["Properties"] = attributeError.Properties
            };

            qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

            return qcError;
        }

        /// <summary>
        /// 공간 관계 오류 코드를 생성합니다
        /// </summary>
        /// <param name="relationType">공간 관계 타입</param>
        /// <param name="errorType">오류 타입</param>
        /// <returns>오류 코드</returns>
        private string GetSpatialRelationErrorCode(SpatialRelationType relationType, string errorType)
        {
            var relationCode = relationType switch
            {
                SpatialRelationType.Contains => "CON",
                SpatialRelationType.Within => "WTH",
                SpatialRelationType.Intersects => "INT",
                SpatialRelationType.Touches => "TCH",
                SpatialRelationType.Overlaps => "OVL",
                SpatialRelationType.Disjoint => "DIS",
                SpatialRelationType.Crosses => "CRS",
                SpatialRelationType.Equals => "EQL",
                _ => "REL"
            };

            return $"REL_{relationCode}001";
        }

        /// <summary>
        /// 속성 관계 오류 코드를 생성합니다
        /// </summary>
        /// <param name="ruleName">규칙명</param>
        /// <returns>오류 코드</returns>
        private string GetAttributeRelationErrorCode(string ruleName)
        {
            var ruleHash = Math.Abs(ruleName.GetHashCode()) % 1000;
            return $"ATTR_REL{ruleHash:D3}";
        }

        /// <summary>
        /// ErrorSeverity를 문자열로 변환합니다
        /// </summary>
        /// <param name="severity">오류 심각도</param>
        /// <returns>심각도 문자열</returns>
        private string ConvertErrorSeverityToString(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Critical => "CRIT",
                ErrorSeverity.Error => "MAJOR",
                ErrorSeverity.Warning => "MINOR",
                ErrorSeverity.Info => "INFO",
                _ => "INFO"
            };
        }

        /// <summary>
        /// WKT에서 지오메트리 타입을 결정합니다 (대문자로 통일)
        /// </summary>
        /// <param name="wkt">WKT 문자열</param>
        /// <returns>지오메트리 타입</returns>
        private string DetermineGeometryTypeFromWKT(string? wkt)
        {
            if (string.IsNullOrEmpty(wkt))
                return "UNKNOWN";

            var upperWkt = wkt.ToUpper();
            if (upperWkt.StartsWith("POINT"))
                return "POINT";
            if (upperWkt.StartsWith("LINESTRING") || upperWkt.StartsWith("MULTILINESTRING"))
                return "LINESTRING";
            if (upperWkt.StartsWith("POLYGON") || upperWkt.StartsWith("MULTIPOLYGON"))
                return "POLYGON";

            return "UNKNOWN";
        }

        /// <summary>
        /// 단일 QcError를 저장합니다
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="qcError">저장할 QcError</param>
        /// <returns>성공 여부</returns>
        private async Task<bool> SaveSingleQcErrorAsync(string qcErrorsGdbPath, QcError qcError)
        {
            try
            {
                // QcErrorService의 기존 메서드를 활용하여 저장
                // 실제 구현에서는 QcErrorDataService를 직접 사용할 수도 있음
                return await _qcErrorService.UpdateQcErrorStatusAsync(qcErrorsGdbPath, qcError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QcError 저장 실패: {ErrorCode}", qcError.ErrCode);
                return false;
            }
        }

        /// <summary>
        /// 관계 검수 오류 통계를 생성합니다
        /// </summary>
        /// <param name="relationResult">관계 검수 결과</param>
        /// <returns>오류 통계 정보</returns>
        public RelationErrorStatistics GenerateErrorStatistics(RelationValidationResult relationResult)
        {
            var statistics = new RelationErrorStatistics
            {
                TotalSpatialErrors = relationResult.SpatialErrorCount,
                TotalAttributeErrors = relationResult.AttributeErrorCount,
                TotalErrors = relationResult.TotalErrorCount
            };

            // 공간 관계 오류 통계
            statistics.SpatialErrorsByType = relationResult.SpatialErrors
                .GroupBy(e => e.RelationType)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            statistics.SpatialErrorsBySeverity = relationResult.SpatialErrors
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            // 속성 관계 오류 통계
            statistics.AttributeErrorsByRule = relationResult.AttributeErrors
                .GroupBy(e => e.RuleName)
                .ToDictionary(g => g.Key, g => g.Count());

            statistics.AttributeErrorsBySeverity = relationResult.AttributeErrors
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            // 전체 심각도별 통계
            var allErrors = relationResult.SpatialErrors.Select(e => e.Severity)
                .Concat(relationResult.AttributeErrors.Select(e => e.Severity));

            statistics.OverallErrorsBySeverity = allErrors
                .GroupBy(s => s)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            return statistics;
        }

        /// <summary>
        /// QC_ERRORS와의 호환성을 확인합니다
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>호환성 확인 결과</returns>
        public async Task<bool> ValidateCompatibilityAsync(string qcErrorsGdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 시스템 호환성 확인 시작");

                // QC_ERRORS 스키마 유효성 검사
                var schemaValid = await _qcErrorService.ValidateQcErrorsSchemaAsync(qcErrorsGdbPath);
                if (!schemaValid)
                {
                    _logger.LogWarning("QC_ERRORS 스키마가 유효하지 않음 - 자동 복구 시도");
                    var repairResult = await _qcErrorService.RepairQcErrorsSchemaAsync(qcErrorsGdbPath);
                    if (!repairResult)
                    {
                        _logger.LogError("QC_ERRORS 스키마 복구 실패");
                        return false;
                    }
                }

                _logger.LogInformation("QC_ERRORS 시스템 호환성 확인 완료");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 시스템 호환성 확인 실패");
                return false;
            }
        }
    }

    /// <summary>
    /// 관계 검수 오류 통계 정보
    /// </summary>
    public class RelationErrorStatistics
    {
        /// <summary>
        /// 총 공간 관계 오류 수
        /// </summary>
        public int TotalSpatialErrors { get; set; }

        /// <summary>
        /// 총 속성 관계 오류 수
        /// </summary>
        public int TotalAttributeErrors { get; set; }

        /// <summary>
        /// 총 오류 수
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// 공간 관계 타입별 오류 수
        /// </summary>
        public Dictionary<string, int> SpatialErrorsByType { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 공간 관계 오류의 심각도별 분포
        /// </summary>
        public Dictionary<string, int> SpatialErrorsBySeverity { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 속성 관계 규칙별 오류 수
        /// </summary>
        public Dictionary<string, int> AttributeErrorsByRule { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 속성 관계 오류의 심각도별 분포
        /// </summary>
        public Dictionary<string, int> AttributeErrorsBySeverity { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 전체 오류의 심각도별 분포
        /// </summary>
        public Dictionary<string, int> OverallErrorsBySeverity { get; set; } = new Dictionary<string, int>();
    }
}
