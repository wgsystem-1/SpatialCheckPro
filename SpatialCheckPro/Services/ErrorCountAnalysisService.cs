using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services.Interfaces;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 오류 개수 분석 서비스
    /// 검수 결과와 저장된 QC_ERRORS_POINT 개수를 비교 분석
    /// </summary>
    public class ErrorCountAnalysisService : IErrorCountAnalysisService
    {
        private readonly ILogger<ErrorCountAnalysisService> _logger;
        private readonly QcErrorDataService _qcErrorDataService;

        public ErrorCountAnalysisService(
            ILogger<ErrorCountAnalysisService> logger,
            QcErrorDataService qcErrorDataService)
        {
            _logger = logger;
            _qcErrorDataService = qcErrorDataService;
        }

        /// <summary>
        /// 오류 개수 분석을 수행합니다
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>분석 결과</returns>
        public async Task<ErrorCountAnalysisResult> AnalyzeErrorCountsAsync(
            ValidationResult validationResult, 
            string gdbPath)
        {
            var result = new ErrorCountAnalysisResult
            {
                GdbPath = gdbPath,
                AnalysisTime = DateTime.UtcNow,
                Status = AnalysisStatus.Analyzing
            };

            try
            {
                _logger.LogInformation("오류 개수 분석 시작: {GdbPath}", gdbPath);

                // 1. 검수 결과에서 총 오류 개수 계산
                result.ValidationResultErrorCount = CalculateTotalErrorsFromValidationResult(validationResult);

                // 2. QC_ERRORS_POINT에서 저장된 오류 개수 조회
                result.SavedPointErrorCount = await GetSavedPointErrorCountAsync(gdbPath);

                // 3. 단계별 분석 수행
                result.StageAnalyses = await AnalyzeStageErrorsAsync(validationResult, gdbPath);

                // 4. 저장되지 않은 오류 상세 분석
                result.MissingErrorDetails = await AnalyzeMissingErrorsAsync(validationResult, gdbPath);

                // 5. 요약 메시지 생성
                result.Summary = GenerateSummaryMessage(result);

                result.Status = AnalysisStatus.Completed;
                _logger.LogInformation("오류 개수 분석 완료: 검수결과 {ValidationCount}개, 저장된 {SavedCount}개, 누락 {MissingCount}개", 
                    result.ValidationResultErrorCount, result.SavedPointErrorCount, result.MissingErrorCount);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 개수 분석 실패: {GdbPath}", gdbPath);
                result.Status = AnalysisStatus.Failed;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 검수 결과에서 총 오류 개수를 계산합니다
        /// </summary>
        private int CalculateTotalErrorsFromValidationResult(ValidationResult validationResult)
        {
            int totalErrors = 0;

            // 0단계: FileGDB 검수
            if (validationResult.FileGdbCheckResult != null)
            {
                totalErrors += validationResult.FileGdbCheckResult.ErrorCount;
            }

            // 1단계: 테이블 검수
            if (validationResult.TableCheckResult != null)
            {
                var tableResults = validationResult.TableCheckResult.TableResults ?? new List<TableValidationItem>();
                totalErrors += tableResults.Count(t => string.Equals(t.TableExistsCheck, "N", StringComparison.OrdinalIgnoreCase));
                totalErrors += tableResults.Count(t => string.Equals(t.ExpectedFeatureType?.Trim() ?? string.Empty, "정의되지 않음", StringComparison.OrdinalIgnoreCase));
                totalErrors += tableResults.Count(t => string.Equals(t.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase) && (t.FeatureCount ?? 0) == 0);
            }

            // 2단계: 스키마 검수
            if (validationResult.SchemaCheckResult != null)
            {
                totalErrors += validationResult.SchemaCheckResult.ErrorCount;
            }

            // 3단계: 지오메트리 검수
            if (validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                var geometryResults = validationResult.GeometryCheckResult.GeometryResults;
                totalErrors += geometryResults.Sum(r => r.DuplicateCount + r.OverlapCount + r.SelfIntersectionCount + 
                                                      r.SelfOverlapCount + r.SliverCount + r.SpikeCount + 
                                                      r.ShortObjectCount + r.SmallAreaCount + r.PolygonInPolygonCount + 
                                                      r.MinPointCount + r.UndershootCount + r.OvershootCount);
            }

            // 4단계: 관계 검수
            if (validationResult.RelationCheckResult != null)
            {
                totalErrors += validationResult.RelationCheckResult.ErrorCount;
            }

            // 5단계: 속성 관계 검수
            if (validationResult.AttributeRelationCheckResult != null)
            {
                totalErrors += validationResult.AttributeRelationCheckResult.ErrorCount;
            }

            return totalErrors;
        }

        /// <summary>
        /// QC_ERRORS_POINT에서 저장된 오류 개수를 조회합니다
        /// </summary>
        private async Task<int> GetSavedPointErrorCountAsync(string gdbPath)
        {
            try
            {
                var qcErrors = await _qcErrorDataService.GetQcErrorsAsync(gdbPath);
                return qcErrors.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "저장된 오류 개수 조회 실패: {GdbPath}", gdbPath);
                return 0;
            }
        }

        /// <summary>
        /// 단계별 오류 분석을 수행합니다
        /// </summary>
        private async Task<List<StageErrorAnalysis>> AnalyzeStageErrorsAsync(
            ValidationResult validationResult, 
            string gdbPath)
        {
            var stageAnalyses = new List<StageErrorAnalysis>();

            // 0단계: FileGDB 검수
            if (validationResult.FileGdbCheckResult != null)
            {
                var stage0Analysis = new StageErrorAnalysis
                {
                    StageNumber = 0,
                    StageName = "FileGDB 검증",
                    ValidationErrorCount = validationResult.FileGdbCheckResult.ErrorCount,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "FILEGDB"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "FILEGDB",
                            ErrorTypeName = "FileGDB 완전성 검사",
                            ValidationErrorCount = validationResult.FileGdbCheckResult.ErrorCount,
                            SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "FILEGDB")
                        }
                    }
                };
                stageAnalyses.Add(stage0Analysis);
            }

            // 1단계: 테이블 검수
            if (validationResult.TableCheckResult != null)
            {
                var tableResults = validationResult.TableCheckResult.TableResults ?? new List<TableValidationItem>();
                var missingTables = tableResults.Count(t => string.Equals(t.TableExistsCheck, "N", StringComparison.OrdinalIgnoreCase));
                var undefinedTables = tableResults.Count(t => string.Equals(t.ExpectedFeatureType?.Trim() ?? string.Empty, "정의되지 않음", StringComparison.OrdinalIgnoreCase));
                var zeroFeatureTables = tableResults.Count(t => string.Equals(t.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase) && (t.FeatureCount ?? 0) == 0);

                var stage1Analysis = new StageErrorAnalysis
                {
                    StageNumber = 1,
                    StageName = "테이블 검수",
                    ValidationErrorCount = missingTables + undefinedTables + zeroFeatureTables,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "TABLE"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "TABLE_MISSING",
                            ErrorTypeName = "누락된 테이블",
                            ValidationErrorCount = missingTables,
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "TABLE_MISSING")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "TABLE_UNDEFINED",
                            ErrorTypeName = "정의되지 않은 테이블",
                            ValidationErrorCount = undefinedTables,
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "TABLE_UNDEFINED")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "TABLE_ZERO_FEATURES",
                            ErrorTypeName = "피처가 없는 테이블",
                            ValidationErrorCount = zeroFeatureTables,
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "TABLE_ZERO_FEATURES")
                        }
                    }
                };
                stageAnalyses.Add(stage1Analysis);
            }

            // 2단계: 스키마 검수
            if (validationResult.SchemaCheckResult != null)
            {
                var stage2Analysis = new StageErrorAnalysis
                {
                    StageNumber = 2,
                    StageName = "스키마 검수",
                    ValidationErrorCount = validationResult.SchemaCheckResult.ErrorCount,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "SCHEMA"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "SCHEMA",
                            ErrorTypeName = "스키마 검수",
                            ValidationErrorCount = validationResult.SchemaCheckResult.ErrorCount,
                            SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "SCHEMA")
                        }
                    }
                };
                stageAnalyses.Add(stage2Analysis);
            }

            // 3단계: 지오메트리 검수
            if (validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                var geometryResults = validationResult.GeometryCheckResult.GeometryResults;
                var totalGeometryErrors = geometryResults.Sum(r => r.DuplicateCount + r.OverlapCount + r.SelfIntersectionCount + 
                                                                  r.SelfOverlapCount + r.SliverCount + r.SpikeCount + 
                                                                  r.ShortObjectCount + r.SmallAreaCount + r.PolygonInPolygonCount + 
                                                                  r.MinPointCount + r.UndershootCount + r.OvershootCount);

                var stage3Analysis = new StageErrorAnalysis
                {
                    StageNumber = 3,
                    StageName = "지오메트리 검수",
                    ValidationErrorCount = totalGeometryErrors,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "GEOM"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_DUPLICATE",
                            ErrorTypeName = "객체 중복",
                            ValidationErrorCount = geometryResults.Sum(r => r.DuplicateCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_DUPLICATE")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_OVERLAP",
                            ErrorTypeName = "겹침",
                            ValidationErrorCount = geometryResults.Sum(r => r.OverlapCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_OVERLAP")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SELF_INTERSECTION",
                            ErrorTypeName = "자기 교차",
                            ValidationErrorCount = geometryResults.Sum(r => r.SelfIntersectionCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SELF_INTERSECTION")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SELF_OVERLAP",
                            ErrorTypeName = "자기 중첩",
                            ValidationErrorCount = geometryResults.Sum(r => r.SelfOverlapCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SELF_OVERLAP")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SLIVER",
                            ErrorTypeName = "슬리버",
                            ValidationErrorCount = geometryResults.Sum(r => r.SliverCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SLIVER")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SPIKE",
                            ErrorTypeName = "스파이크",
                            ValidationErrorCount = geometryResults.Sum(r => r.SpikeCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SPIKE")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SHORT_OBJECT",
                            ErrorTypeName = "짧은 객체",
                            ValidationErrorCount = geometryResults.Sum(r => r.ShortObjectCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SHORT_OBJECT")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_SMALL_AREA",
                            ErrorTypeName = "작은 면적",
                            ValidationErrorCount = geometryResults.Sum(r => r.SmallAreaCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_SMALL_AREA")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_POLYGON_IN_POLYGON",
                            ErrorTypeName = "폴리곤 내 폴리곤",
                            ValidationErrorCount = geometryResults.Sum(r => r.PolygonInPolygonCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_POLYGON_IN_POLYGON")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_MIN_POINT",
                            ErrorTypeName = "최소 점 수",
                            ValidationErrorCount = geometryResults.Sum(r => r.MinPointCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_MIN_POINT")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_UNDERSHOOT",
                            ErrorTypeName = "언더슛",
                            ValidationErrorCount = geometryResults.Sum(r => r.UndershootCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_UNDERSHOOT")
                        },
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "GEOM_OVERSHOOT",
                            ErrorTypeName = "오버슛",
                            ValidationErrorCount = geometryResults.Sum(r => r.OvershootCount),
                            SavedErrorCount = await GetSavedErrorCountByTypeAsync(gdbPath, "GEOM_OVERSHOOT")
                        }
                    }
                };
                stageAnalyses.Add(stage3Analysis);
            }

            // 4단계: 관계 검수
            if (validationResult.RelationCheckResult != null)
            {
                var stage4Analysis = new StageErrorAnalysis
                {
                    StageNumber = 4,
                    StageName = "관계 검수",
                    ValidationErrorCount = validationResult.RelationCheckResult.ErrorCount,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "REL"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "REL",
                            ErrorTypeName = "공간 관계 검수",
                            ValidationErrorCount = validationResult.RelationCheckResult.ErrorCount,
                            SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "REL")
                        }
                    }
                };
                stageAnalyses.Add(stage4Analysis);
            }

            // 5단계: 속성 관계 검수
            if (validationResult.AttributeRelationCheckResult != null)
            {
                var stage5Analysis = new StageErrorAnalysis
                {
                    StageNumber = 5,
                    StageName = "속성 관계 검수",
                    ValidationErrorCount = validationResult.AttributeRelationCheckResult.ErrorCount,
                    SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "ATTR_REL"),
                    ErrorTypeAnalyses = new List<ErrorTypeAnalysis>
                    {
                        new ErrorTypeAnalysis
                        {
                            ErrorType = "ATTR_REL",
                            ErrorTypeName = "속성 관계 검수",
                            ValidationErrorCount = validationResult.AttributeRelationCheckResult.ErrorCount,
                            SavedErrorCount = await GetSavedErrorCountByStageAsync(gdbPath, "ATTR_REL")
                        }
                    }
                };
                stageAnalyses.Add(stage5Analysis);
            }

            return stageAnalyses;
        }

        /// <summary>
        /// 저장되지 않은 오류 상세 분석을 수행합니다
        /// </summary>
        private async Task<List<MissingErrorDetail>> AnalyzeMissingErrorsAsync(
            ValidationResult validationResult, 
            string gdbPath)
        {
            var missingErrors = new List<MissingErrorDetail>();

            try
            {
                // 저장된 오류 목록 조회
                var savedErrors = await _qcErrorDataService.GetQcErrorsAsync(gdbPath);
                var savedErrorKeys = savedErrors.Select(e => $"{e.SourceClass}:{e.SourceOID}:{e.ErrCode}").ToHashSet();

                // 3단계 지오메트리 검수 오류 분석
                if (validationResult.GeometryCheckResult?.GeometryResults != null)
                {
                    foreach (var geometryResult in validationResult.GeometryCheckResult.GeometryResults)
                    {
                        // 각 오류 타입별로 누락된 오류 확인
                        var errorTypes = new[]
                        {
                            ("GEOM_DUPLICATE", geometryResult.DuplicateCount),
                            ("GEOM_OVERLAP", geometryResult.OverlapCount),
                            ("GEOM_SELF_INTERSECTION", geometryResult.SelfIntersectionCount),
                            ("GEOM_SELF_OVERLAP", geometryResult.SelfOverlapCount),
                            ("GEOM_SLIVER", geometryResult.SliverCount),
                            ("GEOM_SPIKE", geometryResult.SpikeCount),
                            ("GEOM_SHORT_OBJECT", geometryResult.ShortObjectCount),
                            ("GEOM_SMALL_AREA", geometryResult.SmallAreaCount),
                            ("GEOM_POLYGON_IN_POLYGON", geometryResult.PolygonInPolygonCount),
                            ("GEOM_MIN_POINT", geometryResult.MinPointCount),
                            ("GEOM_UNDERSHOOT", geometryResult.UndershootCount),
                            ("GEOM_OVERSHOOT", geometryResult.OvershootCount)
                        };

                        foreach (var (errorType, errorCount) in errorTypes)
                        {
                            if (errorCount > 0)
                            {
                                var savedCount = savedErrors.Count(e => e.ErrCode == errorType && e.SourceClass == geometryResult.TableId);
                                var missingCount = errorCount - savedCount;

                                if (missingCount > 0)
                                {
                                    missingErrors.Add(new MissingErrorDetail
                                    {
                                        StageNumber = 3,
                                        ErrorType = errorType,
                                        SourceClass = geometryResult.TableId ?? "Unknown",
                                        SourceOID = 0, // 개별 객체 ID는 알 수 없음
                                        Message = $"{errorType} 오류 {missingCount}개가 저장되지 않음",
                                        FailureReason = "지오메트리 오류 저장 실패"
                                    });
                                }
                            }
                        }
                    }
                }

                return missingErrors;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "저장되지 않은 오류 상세 분석 실패");
                return missingErrors;
            }
        }

        /// <summary>
        /// 단계별 저장된 오류 개수를 조회합니다
        /// </summary>
        private async Task<int> GetSavedErrorCountByStageAsync(string gdbPath, string stagePrefix)
        {
            try
            {
                var qcErrors = await _qcErrorDataService.GetQcErrorsAsync(gdbPath);
                return qcErrors.Count(e => e.ErrType?.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "단계별 저장된 오류 개수 조회 실패: {StagePrefix}", stagePrefix);
                return 0;
            }
        }

        /// <summary>
        /// 오류 타입별 저장된 오류 개수를 조회합니다
        /// </summary>
        private async Task<int> GetSavedErrorCountByTypeAsync(string gdbPath, string errorType)
        {
            try
            {
                var qcErrors = await _qcErrorDataService.GetQcErrorsAsync(gdbPath);
                return qcErrors.Count(e => e.ErrCode == errorType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "오류 타입별 저장된 오류 개수 조회 실패: {ErrorType}", errorType);
                return 0;
            }
        }

        /// <summary>
        /// 분석 요약 메시지를 생성합니다
        /// </summary>
        private string GenerateSummaryMessage(ErrorCountAnalysisResult result)
        {
            if (result.MissingErrorCount == 0)
            {
                return $"✅ 모든 오류가 성공적으로 저장되었습니다. (총 {result.ValidationResultErrorCount}개)";
            }
            else if (result.SaveSuccessRate >= 90)
            {
                return $"⚠️ 대부분의 오류가 저장되었지만, {result.MissingErrorCount}개가 누락되었습니다. (저장률: {result.SaveSuccessRate:F1}%)";
            }
            else if (result.SaveSuccessRate >= 50)
            {
                return $"⚠️ 일부 오류가 저장되지 않았습니다. {result.MissingErrorCount}개 누락 (저장률: {result.SaveSuccessRate:F1}%)";
            }
            else
            {
                return $"❌ 많은 오류가 저장되지 않았습니다. {result.MissingErrorCount}개 누락 (저장률: {result.SaveSuccessRate:F1}%)";
            }
        }
    }
}
