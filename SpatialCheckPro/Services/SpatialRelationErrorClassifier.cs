using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 관계 오류 분류 및 결과 처리를 담당하는 클래스
    /// </summary>
    public class SpatialRelationErrorClassifier
    {
        private readonly ILogger<SpatialRelationErrorClassifier> _logger;

        public SpatialRelationErrorClassifier(ILogger<SpatialRelationErrorClassifier> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 공간 관계 오류의 심각도를 자동으로 분류합니다
        /// </summary>
        /// <param name="error">공간 관계 오류</param>
        /// <param name="rule">적용된 규칙</param>
        /// <returns>분류된 심각도</returns>
        public ErrorSeverity ClassifyErrorSeverity(SpatialRelationError error, SpatialRelationRule rule)
        {
            try
            {
                // 1. 규칙에서 지정된 기본 심각도 사용
                var baseSeverity = rule.ViolationSeverity;

                // 2. 오류 타입별 심각도 조정
                var adjustedSeverity = AdjustSeverityByErrorType(baseSeverity, error.ErrorType, error.RelationType);

                // 3. 지오메트리 특성에 따른 심각도 조정
                adjustedSeverity = AdjustSeverityByGeometry(adjustedSeverity, error);

                // 4. 공간적 맥락에 따른 심각도 조정
                adjustedSeverity = AdjustSeverityBySpatialContext(adjustedSeverity, error, rule);

                _logger.LogDebug("오류 심각도 분류 완료: {ErrorType} -> {Severity}", 
                    error.ErrorType, adjustedSeverity);

                return adjustedSeverity;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "오류 심각도 분류 중 예외 발생, 기본값 사용: {DefaultSeverity}", 
                    rule.ViolationSeverity);
                return rule.ViolationSeverity;
            }
        }

        /// <summary>
        /// 공간 관계 오류 목록을 처리하고 상세 정보를 매핑합니다 (TopologyRule 버전)
        /// </summary>
        /// <param name="errors">공간 관계 오류 목록</param>
        /// <param name="rule">적용된 위상 규칙</param>
        /// <returns>처리된 오류 목록</returns>
        public async Task<List<SpatialRelationError>> ProcessErrorsAsync(
            List<SpatialRelationError> errors, 
            TopologyRule rule)
        {
            var processedErrors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("위상 관계 오류 처리 시작: {ErrorCount}개", errors.Count);

                foreach (var error in errors)
                {
                    // 1. 심각도 분류 (TopologyRule 기반)
                    error.Severity = ClassifyErrorSeverityForTopology(error, rule);

                    // 2. 상세 정보 매핑
                    await EnrichErrorDetailsForTopologyAsync(error, rule);

                    // 3. WKT 지오메트리 검증 및 정리
                    ValidateAndCleanGeometryWKT(error);

                    // 4. 오류 위치 좌표 정확성 검증
                    ValidateErrorLocation(error);

                    // 5. 추가 메타데이터 설정
                    SetAdditionalMetadataForTopology(error, rule);

                    processedErrors.Add(error);
                }

                // 6. 오류 목록 정렬 (심각도 순)
                processedErrors = SortErrorsBySeverity(processedErrors);

                _logger.LogInformation("위상 관계 오류 처리 완료: {ProcessedCount}개", processedErrors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "위상 관계 오류 처리 중 예외 발생");
                throw;
            }

            return processedErrors;
        }

        /// <summary>
        /// 공간 관계 오류 목록을 처리하고 상세 정보를 매핑합니다
        /// </summary>
        /// <param name="errors">공간 관계 오류 목록</param>
        /// <param name="rule">적용된 규칙</param>
        /// <returns>처리된 오류 목록</returns>
        public async Task<List<SpatialRelationError>> ProcessErrorsAsync(
            List<SpatialRelationError> errors, 
            SpatialRelationRule rule)
        {
            var processedErrors = new List<SpatialRelationError>();

            try
            {
                _logger.LogInformation("공간 관계 오류 처리 시작: {ErrorCount}개", errors.Count);

                foreach (var error in errors)
                {
                    // 1. 심각도 분류
                    error.Severity = ClassifyErrorSeverity(error, rule);

                    // 2. 상세 정보 매핑
                    await EnrichErrorDetailsAsync(error, rule);

                    // 3. WKT 지오메트리 검증 및 정리
                    ValidateAndCleanGeometryWKT(error);

                    // 4. 오류 위치 좌표 정확성 검증
                    ValidateErrorLocation(error);

                    // 5. 추가 메타데이터 설정
                    SetAdditionalMetadata(error, rule);

                    processedErrors.Add(error);
                }

                // 6. 오류 목록 정렬 (심각도 순)
                processedErrors = SortErrorsBySeverity(processedErrors);

                _logger.LogInformation("공간 관계 오류 처리 완료: {ProcessedCount}개", processedErrors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 관계 오류 처리 중 예외 발생");
                throw;
            }

            return processedErrors;
        }

        /// <summary>
        /// 오류 타입별로 심각도를 조정합니다
        /// </summary>
        private ErrorSeverity AdjustSeverityByErrorType(
            ErrorSeverity baseSeverity, 
            string errorType, 
            SpatialRelationType relationType)
        {
            // 중요한 공간 관계 위반은 심각도 상향 조정
            var criticalErrorTypes = new[]
            {
                "POINT_IN_POLYGON_VIOLATION",
                "LINE_POLYGON_INTERSECTION_VIOLATION"
            };

            var criticalRelationTypes = new[]
            {
                SpatialRelationType.Within,
                SpatialRelationType.Contains,
                SpatialRelationType.Crosses
            };

            if (criticalErrorTypes.Contains(errorType) && 
                criticalRelationTypes.Contains(relationType))
            {
                return UpgradeSeverity(baseSeverity);
            }

            // 일반적인 교차 관계는 기본 심각도 유지
            if (relationType == SpatialRelationType.Intersects)
            {
                return baseSeverity;
            }

            return baseSeverity;
        }

        /// <summary>
        /// 지오메트리 특성에 따라 심각도를 조정합니다
        /// </summary>
        private ErrorSeverity AdjustSeverityByGeometry(ErrorSeverity baseSeverity, SpatialRelationError error)
        {
            try
            {
                if (string.IsNullOrEmpty(error.GeometryWKT))
                {
                    return baseSeverity;
                }

                // WKT에서 지오메트리 타입 추출
                var geometryType = ExtractGeometryTypeFromWKT(error.GeometryWKT);

                // 복잡한 지오메트리는 심각도 상향 조정
                if (IsComplexGeometry(error.GeometryWKT))
                {
                    return UpgradeSeverity(baseSeverity);
                }

                // 매우 작은 지오메트리는 심각도 하향 조정
                if (IsVerySmallGeometry(error.GeometryWKT))
                {
                    return DowngradeSeverity(baseSeverity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "지오메트리 기반 심각도 조정 중 오류 발생");
            }

            return baseSeverity;
        }

        /// <summary>
        /// 공간적 맥락에 따라 심각도를 조정합니다
        /// </summary>
        private ErrorSeverity AdjustSeverityBySpatialContext(
            ErrorSeverity baseSeverity, 
            SpatialRelationError error, 
            SpatialRelationRule rule)
        {
            // 허용 오차 범위 내의 오류는 심각도 하향 조정
            if (rule.Tolerance > 0 && IsWithinTolerance(error, rule.Tolerance))
            {
                return DowngradeSeverity(baseSeverity);
            }

            // 경계 근처의 오류는 심각도 하향 조정
            if (IsNearBoundary(error))
            {
                return DowngradeSeverity(baseSeverity);
            }

            return baseSeverity;
        }

        /// <summary>
        /// 오류 상세 정보를 보강합니다
        /// </summary>
        private async Task EnrichErrorDetailsAsync(SpatialRelationError error, SpatialRelationRule rule)
        {
            await Task.Run(() =>
            {
                // 기본 메시지 개선
                error.Message = GenerateDetailedErrorMessage(error, rule);

                // 추가 속성 정보 설정
                error.Properties["ErrorClassification"] = ClassifyErrorCategory(error);
                error.Properties["SpatialContext"] = AnalyzeSpatialContext(error);
                error.Properties["RecommendedAction"] = SuggestRecommendedAction(error, rule);
                error.Properties["ProcessedAt"] = DateTime.UtcNow;
            });
        }

        /// <summary>
        /// WKT 지오메트리를 검증하고 정리합니다
        /// </summary>
        private void ValidateAndCleanGeometryWKT(SpatialRelationError error)
        {
            try
            {
                if (string.IsNullOrEmpty(error.GeometryWKT))
                {
                    _logger.LogWarning("오류 {ObjectId}의 WKT 지오메트리가 비어있습니다", error.SourceObjectId);
                    return;
                }

                // GDAL을 사용하여 WKT 유효성 검증
                var geometry = Geometry.CreateFromWkt(error.GeometryWKT);
                if (geometry == null)
                {
                    _logger.LogWarning("오류 {ObjectId}의 WKT 지오메트리가 유효하지 않습니다", error.SourceObjectId);
                    error.GeometryWKT = string.Empty;
                    return;
                }

                // WKT 정규화
                string wkt;
                geometry.ExportToWkt(out wkt);
                error.GeometryWKT = wkt ?? string.Empty;
                geometry.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WKT 지오메트리 검증 중 오류 발생: ObjectId={ObjectId}", error.SourceObjectId);
                error.GeometryWKT = string.Empty;
            }
        }

        /// <summary>
        /// 오류 위치 좌표의 정확성을 검증합니다
        /// </summary>
        private void ValidateErrorLocation(SpatialRelationError error)
        {
            // 좌표가 유효한 범위 내에 있는지 확인
            if (double.IsNaN(error.ErrorLocationX) || double.IsInfinity(error.ErrorLocationX) ||
                double.IsNaN(error.ErrorLocationY) || double.IsInfinity(error.ErrorLocationY))
            {
                _logger.LogWarning("오류 {ObjectId}의 위치 좌표가 유효하지 않습니다", error.SourceObjectId);
                
                // WKT에서 좌표 추출 시도
                if (!string.IsNullOrEmpty(error.GeometryWKT))
                {
                    var coordinates = ExtractCoordinatesFromWKT(error.GeometryWKT);
                    if (coordinates.HasValue)
                    {
                        error.ErrorLocationX = coordinates.Value.X;
                        error.ErrorLocationY = coordinates.Value.Y;
                    }
                }
            }
        }

        /// <summary>
        /// 추가 메타데이터를 설정합니다
        /// </summary>
        private void SetAdditionalMetadata(SpatialRelationError error, SpatialRelationRule rule)
        {
            error.Properties["ValidationVersion"] = "1.0";
            error.Properties["RuleDescription"] = rule.Description;
            error.Properties["GeometryType"] = ExtractGeometryTypeFromWKT(error.GeometryWKT);
            error.Properties["ErrorId"] = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// 오류 목록을 심각도 순으로 정렬합니다
        /// </summary>
        private List<SpatialRelationError> SortErrorsBySeverity(List<SpatialRelationError> errors)
        {
            var severityOrder = new Dictionary<ErrorSeverity, int>
            {
                { ErrorSeverity.Critical, 0 },
                { ErrorSeverity.Error, 1 },
                { ErrorSeverity.Warning, 2 },
                { ErrorSeverity.Info, 3 }
            };

            return errors.OrderBy(e => severityOrder.GetValueOrDefault(e.Severity, 4))
                        .ThenBy(e => e.SourceObjectId)
                        .ToList();
        }

        /// <summary>
        /// 심각도를 상향 조정합니다
        /// </summary>
        private ErrorSeverity UpgradeSeverity(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Info => ErrorSeverity.Warning,
                ErrorSeverity.Warning => ErrorSeverity.Error,
                ErrorSeverity.Error => ErrorSeverity.Critical,
                ErrorSeverity.Critical => ErrorSeverity.Critical,
                _ => severity
            };
        }

        /// <summary>
        /// 심각도를 하향 조정합니다
        /// </summary>
        private ErrorSeverity DowngradeSeverity(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Critical => ErrorSeverity.Error,
                ErrorSeverity.Error => ErrorSeverity.Warning,
                ErrorSeverity.Warning => ErrorSeverity.Info,
                ErrorSeverity.Info => ErrorSeverity.Info,
                _ => severity
            };
        }

        /// <summary>
        /// 상세한 오류 메시지를 생성합니다
        /// </summary>
        private string GenerateDetailedErrorMessage(SpatialRelationError error, SpatialRelationRule rule)
        {
            var baseMessage = error.Message;
            var relationTypeKorean = GetRelationTypeKorean(error.RelationType);
            
            return $"{baseMessage} (규칙: {rule.RuleName}, 관계: {relationTypeKorean}, 위치: {error.ErrorLocationX:F2}, {error.ErrorLocationY:F2})";
        }

        /// <summary>
        /// 오류 카테고리를 분류합니다
        /// </summary>
        private string ClassifyErrorCategory(SpatialRelationError error)
        {
            return error.RelationType switch
            {
                SpatialRelationType.Contains or SpatialRelationType.Within => "포함관계오류",
                SpatialRelationType.Intersects or SpatialRelationType.Crosses => "교차관계오류",
                SpatialRelationType.Touches => "접촉관계오류",
                SpatialRelationType.Overlaps => "겹침관계오류",
                SpatialRelationType.Disjoint => "분리관계오류",
                _ => "일반공간관계오류"
            };
        }

        /// <summary>
        /// 공간적 맥락을 분석합니다
        /// </summary>
        private string AnalyzeSpatialContext(SpatialRelationError error)
        {
            var context = new List<string>();

            if (IsNearBoundary(error))
            {
                context.Add("경계근처");
            }

            if (IsComplexGeometry(error.GeometryWKT))
            {
                context.Add("복잡지오메트리");
            }

            if (IsVerySmallGeometry(error.GeometryWKT))
            {
                context.Add("소형지오메트리");
            }

            return context.Count > 0 ? string.Join(",", context) : "일반";
        }

        /// <summary>
        /// 권장 조치를 제안합니다
        /// </summary>
        private string SuggestRecommendedAction(SpatialRelationError error, SpatialRelationRule rule)
        {
            return error.Severity switch
            {
                ErrorSeverity.Critical => "즉시수정필요",
                ErrorSeverity.Error => "수정권장",
                ErrorSeverity.Warning => "검토필요",
                ErrorSeverity.Info => "참고사항",
                _ => "검토필요"
            };
        }

        /// <summary>
        /// 공간 관계 타입의 한국어 명칭을 반환합니다
        /// </summary>
        private string GetRelationTypeKorean(SpatialRelationType relationType)
        {
            return relationType switch
            {
                SpatialRelationType.Contains => "포함",
                SpatialRelationType.Within => "내부위치",
                SpatialRelationType.Intersects => "교차",
                SpatialRelationType.Touches => "접촉",
                SpatialRelationType.Overlaps => "겹침",
                SpatialRelationType.Disjoint => "분리",
                SpatialRelationType.Crosses => "횡단",
                SpatialRelationType.Equals => "동일",
                _ => "알수없음"
            };
        }

        /// <summary>
        /// WKT에서 지오메트리 타입을 추출합니다
        /// </summary>
        private string ExtractGeometryTypeFromWKT(string wkt)
        {
            if (string.IsNullOrEmpty(wkt)) return "UNKNOWN";

            var upperWkt = wkt.ToUpper();
            if (upperWkt.StartsWith("POINT")) return "POINT";
            if (upperWkt.StartsWith("LINESTRING")) return "LINESTRING";
            if (upperWkt.StartsWith("POLYGON")) return "POLYGON";
            if (upperWkt.StartsWith("MULTIPOINT")) return "MULTIPOINT";
            if (upperWkt.StartsWith("MULTILINESTRING")) return "MULTILINESTRING";
            if (upperWkt.StartsWith("MULTIPOLYGON")) return "MULTIPOLYGON";

            return "UNKNOWN";
        }

        /// <summary>
        /// 복잡한 지오메트리인지 확인합니다
        /// </summary>
        private bool IsComplexGeometry(string wkt)
        {
            if (string.IsNullOrEmpty(wkt)) return false;
            
            // Multi 지오메트리이거나 좌표점이 많은 경우 복잡한 것으로 판단
            return wkt.ToUpper().Contains("MULTI") || wkt.Split(',').Length > 10;
        }

        /// <summary>
        /// 매우 작은 지오메트리인지 확인합니다
        /// </summary>
        private bool IsVerySmallGeometry(string wkt)
        {
            // 실제 구현에서는 지오메트리 크기를 계산해야 함
            // 여기서는 단순화된 로직 사용
            return false;
        }

        /// <summary>
        /// 허용 오차 범위 내에 있는지 확인합니다
        /// </summary>
        private bool IsWithinTolerance(SpatialRelationError error, double tolerance)
        {
            // 실제 구현에서는 오차 계산 로직 필요
            return false;
        }

        /// <summary>
        /// 경계 근처에 있는지 확인합니다
        /// </summary>
        private bool IsNearBoundary(SpatialRelationError error)
        {
            // 실제 구현에서는 경계 근접성 계산 로직 필요
            return false;
        }

        /// <summary>
        /// WKT에서 좌표를 추출합니다
        /// </summary>
        private (double X, double Y)? ExtractCoordinatesFromWKT(string wkt)
        {
            try
            {
                var geometry = Geometry.CreateFromWkt(wkt);
                if (geometry != null)
                {
                    var envelope = new OSGeo.OGR.Envelope();
                    geometry.GetEnvelope(envelope);
                    var centerX = (envelope.MinX + envelope.MaxX) / 2;
                    var centerY = (envelope.MinY + envelope.MaxY) / 2;
                    geometry.Dispose();
                    return (centerX, centerY);
                }
            }
            catch
            {
                // 오류 발생 시 null 반환
            }

            return null;
        }

        #region TopologyRule 관련 메서드들

        /// <summary>
        /// 위상 규칙 기반으로 오류 심각도를 분류합니다
        /// </summary>
        private ErrorSeverity ClassifyErrorSeverityForTopology(SpatialRelationError error, TopologyRule rule)
        {
            try
            {
                // 1. 위상 규칙 타입별 기본 심각도 설정
                var baseSeverity = GetBaseSeverityForTopologyRule(rule.RuleType);

                // 2. 예외 허용 여부에 따른 심각도 조정
                if (rule.AllowExceptions && IsExceptionConditionMet(error, rule))
                {
                    baseSeverity = DowngradeSeverity(baseSeverity);
                }

                // 3. 허용 오차 범위 내의 오류는 심각도 하향 조정
                if (rule.Tolerance > 0 && IsWithinToleranceForTopology(error, rule.Tolerance))
                {
                    baseSeverity = DowngradeSeverity(baseSeverity);
                }

                // 4. 지오메트리 특성에 따른 심각도 조정
                baseSeverity = AdjustSeverityByGeometry(baseSeverity, error);

                _logger.LogDebug("위상 오류 심각도 분류 완료: {RuleType} -> {Severity}", 
                    rule.RuleType, baseSeverity);

                return baseSeverity;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "위상 오류 심각도 분류 중 예외 발생, 기본값 사용: Error");
                return ErrorSeverity.Error;
            }
        }

        /// <summary>
        /// 위상 규칙 타입별 기본 심각도를 반환합니다
        /// </summary>
        private ErrorSeverity GetBaseSeverityForTopologyRule(TopologyRuleType ruleType)
        {
            return ruleType switch
            {
                TopologyRuleType.MustNotOverlap => ErrorSeverity.Error,
                TopologyRuleType.MustNotHaveGaps => ErrorSeverity.Critical,
                TopologyRuleType.MustBeCoveredBy => ErrorSeverity.Warning,
                TopologyRuleType.MustCover => ErrorSeverity.Warning,
                TopologyRuleType.MustNotIntersect => ErrorSeverity.Error,
                TopologyRuleType.MustBeProperlyInside => ErrorSeverity.Warning,
                TopologyRuleType.MustNotSelfOverlap => ErrorSeverity.Critical,
                TopologyRuleType.MustNotSelfIntersect => ErrorSeverity.Critical,
                _ => ErrorSeverity.Error
            };
        }

        /// <summary>
        /// 위상 규칙의 예외 조건이 충족되는지 확인합니다
        /// </summary>
        private bool IsExceptionConditionMet(SpatialRelationError error, TopologyRule rule)
        {
            if (!rule.AllowExceptions || rule.ExceptionConditions.Count == 0)
            {
                return false;
            }

            // 예외 조건 검사 로직 (향후 확장 가능)
            foreach (var condition in rule.ExceptionConditions)
            {
                if (EvaluateExceptionCondition(error, condition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 예외 조건을 평가합니다
        /// </summary>
        private bool EvaluateExceptionCondition(SpatialRelationError error, string condition)
        {
            // 간단한 조건 평가 로직 (향후 확장 가능)
            if (condition.Contains("SMALL_AREA") && IsVerySmallGeometry(error.GeometryWKT))
            {
                return true;
            }

            if (condition.Contains("BOUNDARY") && IsNearBoundary(error))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 위상 규칙에 대한 허용 오차 범위 내에 있는지 확인합니다
        /// </summary>
        private bool IsWithinToleranceForTopology(SpatialRelationError error, double tolerance)
        {
            // 겹침 면적이 허용 오차 이하인지 확인
            if (error.Properties.ContainsKey("OverlapArea"))
            {
                if (error.Properties["OverlapArea"] is double overlapArea)
                {
                    return overlapArea <= tolerance;
                }
            }

            // 기본적으로 허용 오차 범위 밖으로 간주
            return false;
        }

        /// <summary>
        /// 위상 규칙 기반으로 오류 상세 정보를 보강합니다
        /// </summary>
        private async Task EnrichErrorDetailsForTopologyAsync(SpatialRelationError error, TopologyRule rule)
        {
            await Task.Run(() =>
            {
                // 기본 메시지 개선
                error.Message = GenerateDetailedTopologyErrorMessage(error, rule);

                // 추가 속성 정보 설정
                error.Properties["TopologyRuleType"] = rule.RuleType.ToString();
                error.Properties["ErrorClassification"] = ClassifyTopologyErrorCategory(error, rule);
                error.Properties["SpatialContext"] = AnalyzeSpatialContext(error);
                error.Properties["RecommendedAction"] = SuggestTopologyRecommendedAction(error, rule);
                error.Properties["ProcessedAt"] = DateTime.UtcNow;
            });
        }

        /// <summary>
        /// 위상 규칙에 대한 추가 메타데이터를 설정합니다
        /// </summary>
        private void SetAdditionalMetadataForTopology(SpatialRelationError error, TopologyRule rule)
        {
            error.Properties["ValidationVersion"] = "1.0";
            error.Properties["TopologyRuleId"] = rule.RuleId;
            error.Properties["Tolerance"] = rule.Tolerance;
            error.Properties["AllowExceptions"] = rule.AllowExceptions;
            error.Properties["GeometryType"] = ExtractGeometryTypeFromWKT(error.GeometryWKT);
            error.Properties["ErrorId"] = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// 상세한 위상 오류 메시지를 생성합니다
        /// </summary>
        private string GenerateDetailedTopologyErrorMessage(SpatialRelationError error, TopologyRule rule)
        {
            var baseMessage = error.Message;
            var ruleTypeKorean = GetTopologyRuleTypeKorean(rule.RuleType);
            
            return $"{baseMessage} (위상규칙: {ruleTypeKorean}, 허용오차: {rule.Tolerance}, 위치: {error.ErrorLocationX:F2}, {error.ErrorLocationY:F2})";
        }

        /// <summary>
        /// 위상 오류 카테고리를 분류합니다
        /// </summary>
        private string ClassifyTopologyErrorCategory(SpatialRelationError error, TopologyRule rule)
        {
            return rule.RuleType switch
            {
                TopologyRuleType.MustNotOverlap => "겹침위반오류",
                TopologyRuleType.MustNotHaveGaps => "틈새오류",
                TopologyRuleType.MustBeCoveredBy => "덮임오류",
                TopologyRuleType.MustCover => "덮기오류",
                TopologyRuleType.MustNotIntersect => "교차금지오류",
                TopologyRuleType.MustBeProperlyInside => "내부위치오류",
                TopologyRuleType.MustNotSelfOverlap => "자체겹침오류",
                TopologyRuleType.MustNotSelfIntersect => "자체교차오류",
                _ => "일반위상오류"
            };
        }

        /// <summary>
        /// 위상 규칙에 대한 권장 조치를 제안합니다
        /// </summary>
        private string SuggestTopologyRecommendedAction(SpatialRelationError error, TopologyRule rule)
        {
            return rule.RuleType switch
            {
                TopologyRuleType.MustNotOverlap => "겹침영역제거",
                TopologyRuleType.MustNotHaveGaps => "틈새채우기",
                TopologyRuleType.MustBeCoveredBy => "덮임관계확인",
                TopologyRuleType.MustCover => "덮기관계확인",
                TopologyRuleType.MustNotIntersect => "교차부분수정",
                TopologyRuleType.MustBeProperlyInside => "위치관계수정",
                TopologyRuleType.MustNotSelfOverlap => "자체겹침수정",
                TopologyRuleType.MustNotSelfIntersect => "자체교차수정",
                _ => "검토필요"
            };
        }

        /// <summary>
        /// 위상 규칙 타입의 한국어 명칭을 반환합니다
        /// </summary>
        private string GetTopologyRuleTypeKorean(TopologyRuleType ruleType)
        {
            return ruleType switch
            {
                TopologyRuleType.MustNotOverlap => "겹침금지",
                TopologyRuleType.MustNotHaveGaps => "틈새금지",
                TopologyRuleType.MustBeCoveredBy => "덮임필수",
                TopologyRuleType.MustCover => "덮기필수",
                TopologyRuleType.MustNotIntersect => "교차금지",
                TopologyRuleType.MustBeProperlyInside => "내부위치필수",
                TopologyRuleType.MustNotSelfOverlap => "자체겹침금지",
                TopologyRuleType.MustNotSelfIntersect => "자체교차금지",
                _ => "알수없음"
            };
        }

        #endregion
    }
}
