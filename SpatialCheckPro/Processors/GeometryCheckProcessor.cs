using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Services;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Collections.Concurrent;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Valid;
using SpatialCheckPro.Utils;
using System.IO;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 지오메트리 검수 프로세서 (GEOS 내장 검증 + 고성능 공간 인덱스 활용)
    /// ISO 19107 표준 준수 및 최적화된 알고리즘 적용
    /// </summary>
    public class GeometryCheckProcessor : IGeometryCheckProcessor
    {
        private readonly ILogger<GeometryCheckProcessor> _logger;
        private readonly SpatialIndexService? _spatialIndexService;
        private readonly HighPerformanceGeometryValidator? _highPerfValidator;
        private readonly GeometryCriteria _criteria;
        private readonly double _ringClosureTolerance;

        // Phase 2.3: 공간 인덱스 캐싱 (중복 생성 방지)
        private readonly ConcurrentDictionary<string, object> _spatialIndexCache = new();

        public GeometryCheckProcessor(
            ILogger<GeometryCheckProcessor> logger,
            SpatialIndexService? spatialIndexService = null,
            HighPerformanceGeometryValidator? highPerfValidator = null,
            GeometryCriteria? criteria = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spatialIndexService = spatialIndexService;
            _highPerfValidator = highPerfValidator;
            _criteria = criteria ?? GeometryCriteria.CreateDefault();
            _ringClosureTolerance = _criteria.RingClosureTolerance;
        }

        /// <summary>
        /// 공간 인덱스 캐시 정리
        /// Phase 2.3: 메모리 관리를 위한 캐시 클리어
        /// </summary>
        public void ClearSpatialIndexCache()
        {
            _spatialIndexCache.Clear();
            _logger.LogDebug("공간 인덱스 캐시 정리 완료");
        }

        /// <summary>
        /// 전체 지오메트리 검수 (통합 실행)
        /// Phase 1.3: Feature 순회 중복 제거 - 단일 순회로 모든 검사 수행
        /// Phase 2 Item #7: 대용량 Geometry 스트리밍 처리 - 메모리 누적 방지
        /// - 예상 효과: Geometry 검수 시간 60-70% 단축, 메모리 사용량 40% 감소 (Phase 1.3)
        /// - 예상 효과: 대용량 오류 처리 시 메모리 60% 감소 (Phase 2 Item #7)
        /// </summary>
        /// <param name="streamingOutputPath">스트리밍 출력 경로 (null이면 메모리에 누적, 경로 지정 시 디스크에 스트리밍)</param>
        public async Task<ValidationResult> ProcessAsync(
            string filePath,
            GeometryCheckConfig config,
            CancellationToken cancellationToken = default,
            string? streamingOutputPath = null)
        {
            var result = new ValidationResult { IsValid = true };
            IStreamingErrorWriter? errorWriter = null;

            try
            {
                _logger.LogInformation("지오메트리 검수 시작 (단일 순회 최적화): {TableId} ({TableName}), 스트리밍 모드: {StreamingEnabled}",
                    config.TableId, config.TableName, streamingOutputPath != null);
                var startTime = DateTime.Now;

                using var ds = Ogr.Open(filePath, 0);
                if (ds == null)
                {
                    return new ValidationResult { IsValid = false, Message = $"파일을 열 수 없습니다: {filePath}" };
                }

                var layer = ds.GetLayerByName(config.TableId);
                if (layer == null)
                {
                    _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.TableId);
                    return result;
                }

                var featureCount = layer.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처: {Count}개", featureCount);

                // Phase 2 Item #7: 스트리밍 모드 활성화
                if (!string.IsNullOrEmpty(streamingOutputPath))
                {
                    errorWriter = new StreamingErrorWriter(streamingOutputPath,
                        _logger as ILogger<StreamingErrorWriter> ??
                        new LoggerFactory().CreateLogger<StreamingErrorWriter>());
                }

                // === Phase 1.3: 단일 순회 통합 검사 ===
                // GEOS 유효성, 기본 속성, 고급 특징을 한 번의 순회로 모두 검사
                var unifiedErrors = await CheckGeometryInSinglePassAsync(layer, config, cancellationToken, errorWriter);

                if (errorWriter == null)
                {
                    // 메모리 모드: 오류 리스트 누적
                    result.Errors.AddRange(unifiedErrors);
                    result.ErrorCount += unifiedErrors.Count;
                }
                else
                {
                    // 스트리밍 모드: 통계만 업데이트
                    var stats = errorWriter.GetStatistics();
                    result.ErrorCount += stats.TotalErrorCount;
                    result.WarningCount += stats.TotalWarningCount;
                }

                // === 공간 인덱스 기반 검사 (중복, 겹침) - 별도 순회 필요 ===
                if (_highPerfValidator != null)
                {
                    if (config.ShouldCheckDuplicate)
                    {
                        var duplicateErrors = await _highPerfValidator.CheckDuplicatesHighPerformanceAsync(layer);
                        var validationErrors = ConvertToValidationErrors(duplicateErrors, config.TableId, "GEOM_DUPLICATE");

                        if (errorWriter != null)
                        {
                            await errorWriter.WriteErrorsAsync(validationErrors);
                        }
                        else
                        {
                            result.Errors.AddRange(validationErrors);
                        }
                        result.ErrorCount += validationErrors.Count;
                    }

                    if (config.ShouldCheckOverlap)
                    {
                        var overlapErrors = await _highPerfValidator.CheckOverlapsHighPerformanceAsync(
                            layer, _criteria.OverlapTolerance);
                        var validationErrors = ConvertToValidationErrors(overlapErrors, config.TableId, "GEOM_OVERLAP");

                        if (errorWriter != null)
                        {
                            await errorWriter.WriteErrorsAsync(validationErrors);
                        }
                        else
                        {
                            result.Errors.AddRange(validationErrors);
                        }
                        result.ErrorCount += validationErrors.Count;
                    }
                }

                // 스트리밍 완료 및 통계 업데이트
                if (errorWriter != null)
                {
                    var finalStats = await errorWriter.FinalizeAsync();
                    result.ErrorCount = finalStats.TotalErrorCount;
                    result.WarningCount = finalStats.TotalWarningCount;
                    result.Message = $"오류가 파일에 기록되었습니다: {streamingOutputPath}";

                    _logger.LogInformation("스트리밍 모드 통계 - 오류: {ErrorCount}개, 경고: {WarningCount}개, 출력: {OutputPath}",
                        finalStats.TotalErrorCount, finalStats.TotalWarningCount, streamingOutputPath);
                }

                result.IsValid = result.ErrorCount == 0;
                var elapsed = (DateTime.Now - startTime).TotalSeconds;

                _logger.LogInformation("지오메트리 검수 완료 (단일 순회): {TableId}, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                    config.TableId, result.ErrorCount, elapsed);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 실패: {TableId}", config.TableId);
                return new ValidationResult { IsValid = false, Message = $"검수 중 오류 발생: {ex.Message}" };
            }
            finally
            {
                // 스트리밍 리소스 정리
                errorWriter?.Dispose();
            }
        }

        /// <summary>
        /// 단일 순회로 모든 Geometry 검사 수행 (Phase 1.3 최적화 + Phase 2 Item #7 스트리밍)
        /// - GEOS 유효성 검사 (IsValid, IsSimple)
        /// - 기본 기하 속성 검사 (짧은 객체, 작은 면적, 최소 정점)
        /// - 고급 기하 특징 검사 (슬리버, 스파이크)
        /// - Phase 2 Item #7: 스트리밍 모드 - errorWriter가 제공되면 오류를 즉시 디스크에 기록
        /// </summary>
        private async Task<List<ValidationError>> CheckGeometryInSinglePassAsync(
            Layer layer,
            GeometryCheckConfig config,
            CancellationToken cancellationToken,
            IStreamingErrorWriter? errorWriter = null)
        {
            var errors = new ConcurrentBag<ValidationError>();
            var streamingMode = errorWriter != null;
            var streamingBatchSize = 1000; // 1000개마다 디스크에 플러시
            var pendingErrors = new List<ValidationError>(streamingBatchSize);
            var pendingErrorsLock = new object();

            _logger.LogInformation("단일 순회 통합 검사 시작 (GEOS + 기본속성 + 고급특징), 스트리밍 모드: {StreamingMode}",
                streamingMode);
            var startTime = DateTime.Now;

            // 오류 추가 헬퍼 (스트리밍 모드와 메모리 모드 모두 지원)
            Action<ValidationError> addError = (error) =>
            {
                if (streamingMode)
                {
                    lock (pendingErrorsLock)
                    {
                        pendingErrors.Add(error);

                        // 배치 크기 도달 시 비동기로 플러시
                        if (pendingErrors.Count >= streamingBatchSize)
                        {
                            var errorsToWrite = new List<ValidationError>(pendingErrors);
                            pendingErrors.Clear();

                            // 비동기 작업을 동기적으로 대기 (Task.Run 내부이므로 허용)
                            Task.Run(async () =>
                            {
                                await errorWriter!.WriteErrorsAsync(errorsToWrite);
                            }).Wait();
                        }
                    }
                }
                else
                {
                    errors.Add(error);
                }
            };

            await Task.Run(() =>
            {
                try
                {
                    layer.ResetReading();
                    Feature? feature;
                    int processedCount = 0;

                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            processedCount++;

                            var geometryRef = feature.GetGeometryRef();
                            if (geometryRef == null || geometryRef.IsEmpty())
                            {
                                continue;
                            }

                            var fid = feature.GetFID();

                            // ========================================
                            // 1. GEOS 유효성 검사
                            // ========================================
                            if (config.ShouldCheckSelfIntersection || config.ShouldCheckSelfOverlap || config.ShouldCheckPolygonInPolygon)
                            {
                                // GEOS IsValid() - 자체꼬임, 자기중첩, 링방향 등
                                if (!geometryRef.IsValid())
                                {
                                    geometryRef.ExportToWkt(out string wkt);
                                    var reader = new WKTReader();
                                    var ntsGeom = reader.Read(wkt);
                                    var validator = new IsValidOp(ntsGeom);
                                    var validationError = validator.ValidationError;

                                    double errorX = 0, errorY = 0;
                                    string errorTypeName = "지오메트리 유효성 오류";
                                    if (validationError != null)
                                    {
                                        errorTypeName = GeometryCoordinateExtractor.GetKoreanErrorType((int)validationError.ErrorType);
                                        (errorX, errorY) = GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
                                    }
                                    else
                                    {
                                        (errorX, errorY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometryRef);
                                    }

                                    addError(new ValidationError
                                    {
                                        ErrorCode = "GEOM_INVALID",
                                        Message = validationError != null ? $"{errorTypeName}: {validationError.Message}" : "지오메트리 유효성 오류",
                                        TableName = config.TableId,
                                        FeatureId = fid.ToString(),
                                        Severity = Models.Enums.ErrorSeverity.Error,
                                        X = errorX,
                                        Y = errorY,
                                        GeometryWKT = QcError.CreatePointWKT(errorX, errorY),
                                        Metadata =
                                        {
                                            ["X"] = errorX.ToString(),
                                            ["Y"] = errorY.ToString(),
                                            ["GeometryWkt"] = wkt,
                                            ["ErrorType"] = errorTypeName,
                                            ["OriginalGeometryWKT"] = wkt
                                        }
                                    });
                                }

                                // IsSimple() 검사 (자기교차)
                                if (!geometryRef.IsSimple())
                                {
                                    geometryRef.ExportToWkt(out string wkt);
                                    // 자기교차 지점을 찾기 위해 NTS 사용
                                    try
                                    {
                                        var reader = new WKTReader();
                                        var ntsGeom = reader.Read(wkt);
                                        var validator = new IsValidOp(ntsGeom);
                                        var validationError = validator.ValidationError;
                                        
                                        double errorX = 0, errorY = 0;
                                        if (validationError != null)
                                        {
                                            (errorX, errorY) = GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
                                        }
                                        else
                                        {
                                            (errorX, errorY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometryRef);
                                        }

                                        addError(new ValidationError
                                        {
                                            ErrorCode = "GEOM_NOT_SIMPLE",
                                            Message = "자기 교차 오류 (Self-intersection)",
                                            TableName = config.TableId,
                                            FeatureId = fid.ToString(),
                                            Severity = Models.Enums.ErrorSeverity.Error,
                                            X = errorX,
                                            Y = errorY,
                                            GeometryWKT = QcError.CreatePointWKT(errorX, errorY),
                                            Metadata =
                                            {
                                                ["X"] = errorX.ToString(),
                                                ["Y"] = errorY.ToString(),
                                                ["OriginalGeometryWKT"] = wkt
                                            }
                                        });
                                    }
                                    catch
                                    {
                                        var (centerX, centerY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometryRef);
                                        addError(new ValidationError
                                        {
                                            ErrorCode = "GEOM_NOT_SIMPLE",
                                            Message = "자기 교차 오류 (Self-intersection)",
                                            TableName = config.TableId,
                                            FeatureId = fid.ToString(),
                                            Severity = Models.Enums.ErrorSeverity.Error,
                                            X = centerX,
                                            Y = centerY,
                                            GeometryWKT = QcError.CreatePointWKT(centerX, centerY)
                                        });
                                    }
                                }
                            }

                            // ========================================
                            // 2. 기본 기하 속성 검사
                            // ========================================
                            Geometry? geometryClone = null;
                            Geometry? linearized = null;
                            Geometry? workingGeometry = null;

                            try
                            {
                                // Geometry 복제 및 선형화 (곡선 처리)
                                geometryClone = geometryRef.Clone();
                                linearized = geometryClone?.GetLinearGeometry(0, Array.Empty<string>());
                                workingGeometry = linearized ?? geometryClone;

                                if (workingGeometry != null && !workingGeometry.IsEmpty())
                                {
                                    workingGeometry.FlattenTo2D();

                                    // 2-1. 짧은 객체 검사 (선)
                                    if (config.ShouldCheckShortObject && GeometryRepresentsLine(workingGeometry))
                                    {
                                        var length = workingGeometry.Length();
                                        if (length < _criteria.MinLineLength && length > 0)
                                        {
                                            int pointCount = workingGeometry.GetPointCount();
                                            double midX = 0, midY = 0;
                                            if (pointCount > 0)
                                            {
                                                int midIndex = pointCount / 2;
                                                midX = workingGeometry.GetX(midIndex);
                                                midY = workingGeometry.GetY(midIndex);
                                            }

                                            workingGeometry.ExportToWkt(out string wkt);

                                            addError(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SHORT_LINE",
                                                Message = $"선이 너무 짧습니다: {length:F3}m (최소: {_criteria.MinLineLength}m)",
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Warning,
                                                Metadata =
                                                {
                                                    ["X"] = midX.ToString(),
                                                    ["Y"] = midY.ToString(),
                                                    ["GeometryWkt"] = wkt
                                                }
                                            });
                                        }
                                    }

                                    // 2-2. 작은 면적 검사 (폴리곤)
                                    if (config.ShouldCheckSmallArea && GeometryRepresentsPolygon(workingGeometry))
                                    {
                                        var area = workingGeometry.GetArea();
                                        if (area > 0 && area < _criteria.MinPolygonArea)
                                        {
                                            var envelope = new Envelope();
                                            workingGeometry.GetEnvelope(envelope);
                                            double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
                                            double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

                                            workingGeometry.ExportToWkt(out string wkt);

                                            addError(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SMALL_AREA",
                                                Message = $"면적이 너무 작습니다: {area:F2}㎡ (최소: {_criteria.MinPolygonArea}㎡)",
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Warning,
                                                Metadata =
                                                {
                                                    ["X"] = centerX.ToString(),
                                                    ["Y"] = centerY.ToString(),
                                                    ["GeometryWkt"] = wkt
                                                }
                                            });
                                        }
                                    }

                                    // 2-3. 최소 정점 검사
                                    if (config.ShouldCheckMinPoints)
                                    {
                                        var minVertexCheck = EvaluateMinimumVertexRequirement(workingGeometry);
                                        if (!minVertexCheck.IsValid)
                                        {
                                            var detail = string.IsNullOrWhiteSpace(minVertexCheck.Detail)
                                                ? string.Empty
                                                : $" ({minVertexCheck.Detail})";

                                            double x = 0, y = 0;
                                            if (workingGeometry.GetPointCount() > 0)
                                            {
                                                x = workingGeometry.GetX(0);
                                                y = workingGeometry.GetY(0);
                                            }
                                            else
                                            {
                                                var env = new Envelope();
                                                workingGeometry.GetEnvelope(env);
                                                x = (env.MinX + env.MaxX) / 2.0;
                                                y = (env.MinY + env.MaxY) / 2.0;
                                            }

                                            workingGeometry.ExportToWkt(out string wkt);

                                            addError(new ValidationError
                                            {
                                                ErrorCode = "GEOM_MIN_VERTEX",
                                                Message = $"정점 수가 부족합니다: {minVertexCheck.ObservedVertices}개 (최소: {minVertexCheck.RequiredVertices}개){detail}",
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Error,
                                                Metadata =
                                                {
                                                    ["PolygonDebug"] = BuildPolygonDebugInfo(workingGeometry, minVertexCheck),
                                                    ["X"] = x.ToString(),
                                                    ["Y"] = y.ToString(),
                                                    ["GeometryWkt"] = wkt
                                                }
                                            });
                                        }
                                    }

                                    // ========================================
                                    // 3. 고급 기하 특징 검사
                                    // ========================================

                                    // 3-1. 슬리버 폴리곤 검사
                                    if (config.ShouldCheckSliver && config.GeometryType.Contains("POLYGON"))
                                    {
                                        if (IsSliverPolygon(geometryRef, out string sliverMessage))
                                        {
                                            double centerX = 0, centerY = 0;
                                            if (geometryRef.GetGeometryCount() > 0)
                                            {
                                                var exteriorRing = geometryRef.GetGeometryRef(0);
                                                if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                                                {
                                                    int pointCount = exteriorRing.GetPointCount();
                                                    int midIndex = pointCount / 2;
                                                    centerX = exteriorRing.GetX(midIndex);
                                                    centerY = exteriorRing.GetY(midIndex);
                                                }
                                            }
                                            if (centerX == 0 && centerY == 0)
                                            {
                                                var env = new Envelope();
                                                geometryRef.GetEnvelope(env);
                                                centerX = (env.MinX + env.MaxX) / 2.0;
                                                centerY = (env.MinY + env.MaxY) / 2.0;
                                            }
                                            geometryRef.ExportToWkt(out string wkt);

                                            addError(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SLIVER",
                                                Message = sliverMessage,
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Warning,
                                                Metadata =
                                                {
                                                    ["X"] = centerX.ToString(),
                                                    ["Y"] = centerY.ToString(),
                                                    ["GeometryWkt"] = wkt
                                                }
                                            });
                                        }
                                    }

                                    // 3-2. 스파이크 검사
                                    if (config.ShouldCheckSpikes)
                                    {
                                        geometryRef.ExportToWkt(out string wkt);
                                        if (HasSpike(geometryRef, out string spikeMessage, out double spikeX, out double spikeY))
                                        {
                                            // result.Errors.Add(new ValidationError
                                            // {
                                            //     ErrorCode = "GEOM_SPIKE",
                                            //     Message = spikeMessage,
                                            //     TableName = config.TableId,
                                            //     FeatureId = fid.ToString(),
                                            //     Severity = Models.Enums.ErrorSeverity.Warning,
                                            //     X = spikeX,
                                            //     Y = spikeY,
                                            //     GeometryWKT = QcError.CreatePointWKT(spikeX, spikeY),
                                            //     Metadata =
                                            //     {
                                            //         ["X"] = spikeX.ToString(),
                                            //         ["Y"] = spikeY.ToString(),
                                            //         ["GeometryWkt"] = wkt,
                                            //         ["OriginalGeometryWKT"] = wkt
                                            //     }
                                            // });
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                // Geometry 리소스 정리
                                workingGeometry?.Dispose();
                                linearized?.Dispose();
                                geometryClone?.Dispose();
                            }

                            // 진행률 로깅
                            if (processedCount % 1000 == 0)
                            {
                                _logger.LogDebug("단일 순회 검사 진행: {Count}/{Total}", processedCount, layer.GetFeatureCount(1));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "단일 순회 검사 중 오류 발생");
                    throw;
                }
            }, cancellationToken);

            // 스트리밍 모드: 남은 오류 플러시
            if (streamingMode && pendingErrors.Count > 0)
            {
                List<ValidationError> errorsToFlush;
                lock (pendingErrorsLock)
                {
                    errorsToFlush = new List<ValidationError>(pendingErrors);
                    pendingErrors.Clear();
                }

                if (errorsToFlush.Count > 0)
                {
                    await errorWriter!.WriteErrorsAsync(errorsToFlush);
                    _logger.LogDebug("스트리밍 모드: 최종 배치 플러시 완료");
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;

            if (streamingMode)
            {
                var stats = errorWriter!.GetStatistics();
                _logger.LogInformation("단일 순회 통합 검사 완료 (스트리밍 모드): {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초",
                    stats.TotalErrorCount, elapsed);
                return new List<ValidationError>(); // 스트리밍 모드에서는 빈 리스트 반환
            }
            else
            {
                _logger.LogInformation("단일 순회 통합 검사 완료: {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초",
                    errors.Count, elapsed);
                return errors.ToList();
            }
        }

        public async Task<ValidationResult> CheckDuplicateGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("중복 지오메트리 검수 시작: {TableId}", config.TableId);
            
            // 중복 검사만 수행하도록 설정
            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckDuplicate = "Y" // 중복 검사만 활성화
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckOverlappingGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("겹치는 지오메트리 검수 시작: {TableId}", config.TableId);
            
            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckOverlap = "Y" // 겹침 검사만 활성화
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckTwistedGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("뒤틀린 지오메트리 검수 시작: {TableId}", config.TableId);
            
            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckSelfIntersection = "Y" // 자체꼬임 검사 활성화
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckSliverPolygonsAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("슬리버 폴리곤 검수 시작: {TableId}", config.TableId);
            
            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckSliver = "Y" // 슬리버 검사 활성화
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        #region 내부 검사 메서드

        /// <summary>
        /// GEOS 내장 유효성 검사 (ISO 19107 표준)
        /// 자체꼬임, 자기중첩, 홀 폴리곤, 링 방향 등을 한 번에 검사
        /// </summary>
        private async Task<List<ValidationError>> CheckGeosValidityInternalAsync(
            Layer layer, 
            GeometryCheckConfig config, 
            CancellationToken cancellationToken)
        {
            var errors = new ConcurrentBag<ValidationError>();
            
            _logger.LogInformation("GEOS 유효성 검사 시작 (자체꼬임, 자기중첩, 홀 폴리곤)");
            var startTime = DateTime.Now;

            await Task.Run(() =>
            {
                layer.ResetReading();
                Feature? feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedCount++;

                        var geometry = feature.GetGeometryRef();
                        if (geometry == null) continue;

                        var fid = feature.GetFID();

                        // ★ GEOS IsValid() - 가장 중요한 검사!
                        // 자체꼬임, 자기중첩, 링방향, 홀-쉘 관계 등 자동 검사
                        if (!geometry.IsValid())
                        {
                            geometry.ExportToWkt(out string wkt);
                            var reader = new WKTReader();
                            var ntsGeom = reader.Read(wkt);
                            var validator = new IsValidOp(ntsGeom);
                            var validationError = validator.ValidationError;

                            double errorX = 0, errorY = 0;
                            string errorTypeName = "지오메트리 유효성 오류";
                            if (validationError != null)
                            {
                                errorTypeName = GeometryCoordinateExtractor.GetKoreanErrorType((int)validationError.ErrorType);
                                (errorX, errorY) = GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
                            }
                            else
                            {
                                (errorX, errorY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
                            }

                            errors.Add(new ValidationError
                            {
                                ErrorCode = "GEOM_INVALID",
                                Message = validationError != null ? $"{errorTypeName}: {validationError.Message}" : "지오메트리 유효성 오류 (자체꼬임, 자기중첩, 홀폴리곤, 링방향 등)",
                                TableName = config.TableId,
                                FeatureId = fid.ToString(),
                                Severity = Models.Enums.ErrorSeverity.Error,
                                X = errorX,
                                Y = errorY,
                                GeometryWKT = QcError.CreatePointWKT(errorX, errorY),
                                Metadata =
                                {
                                    ["X"] = errorX.ToString(),
                                    ["Y"] = errorY.ToString(),
                                    ["GeometryWkt"] = wkt,
                                    ["ErrorType"] = errorTypeName,
                                    ["OriginalGeometryWKT"] = wkt
                                }
                            });
                        }

                        // IsSimple() 검사 (자기교차)
                        if (!geometry.IsSimple())
                        {
                            geometry.ExportToWkt(out string simpleWkt);
                            // 자기교차 지점을 찾기 위해 NTS 사용
                            try
                            {
                                var reader = new WKTReader();
                                var ntsGeom = reader.Read(simpleWkt);
                                var validator = new IsValidOp(ntsGeom);
                                var validationError = validator.ValidationError;
                                
                                double errorX = 0, errorY = 0;
                                if (validationError != null)
                                {
                                    (errorX, errorY) = GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
                                }
                                else
                                {
                                    (errorX, errorY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
                                }

                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "GEOM_NOT_SIMPLE",
                                    Message = "자기 교차 오류 (Self-intersection)",
                                    TableName = config.TableId,
                                    FeatureId = fid.ToString(),
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    X = errorX,
                                    Y = errorY,
                                    GeometryWKT = QcError.CreatePointWKT(errorX, errorY),
                                    Metadata =
                                    {
                                        ["X"] = errorX.ToString(),
                                        ["Y"] = errorY.ToString(),
                                        ["OriginalGeometryWKT"] = simpleWkt
                                    }
                                });
                            }
                            catch
                            {
                                var (centerX, centerY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "GEOM_NOT_SIMPLE",
                                    Message = "자기 교차 오류 (Self-intersection)",
                                    TableName = config.TableId,
                                    FeatureId = fid.ToString(),
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    X = centerX,
                                    Y = centerY,
                                    GeometryWKT = QcError.CreatePointWKT(centerX, centerY)
                                });
                            }
                        }

                        if (processedCount % 100 == 0)
                        {
                            _logger.LogDebug("GEOS 검증 진행: {Count}/{Total}", processedCount, layer.GetFeatureCount(1));
                        }
                    }
                }
            }, cancellationToken);

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("GEOS 유효성 검사 완료: {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초", 
                errors.Count, elapsed);

            return errors.ToList();
        }

        /// <summary>
        /// 기본 기하 속성 검사 (짧은 객체, 작은 면적, 최소 정점)
        /// </summary>
        private async Task<List<ValidationError>> CheckBasicGeometricPropertiesInternalAsync(
            Layer layer, 
            GeometryCheckConfig config, 
            CancellationToken cancellationToken)
        {
            var errors = new ConcurrentBag<ValidationError>();
            
            _logger.LogInformation("기본 기하 속성 검사 시작 (짧은객체, 작은면적, 최소정점)");
            var startTime = DateTime.Now;

            await Task.Run(() =>
            {
                try
                {
                    layer.ResetReading();
                    layer.SetIgnoredFields(new[] { "*" });
                    Feature? feature;

                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var geometryRef = feature.GetGeometryRef();
                            if (geometryRef == null || geometryRef.IsEmpty())
                            {
                                continue;
                            }

                            Geometry? geometryClone = null;
                            Geometry? linearized = null;
                            Geometry? workingGeometry = null;
                            try
                            {
                                geometryClone = geometryRef.Clone();
                                linearized = geometryClone?.GetLinearGeometry(0, Array.Empty<string>());
                                workingGeometry = linearized ?? geometryClone;
                                if (workingGeometry == null || workingGeometry.IsEmpty())
                                {
                                    continue;
                                }

                                workingGeometry.FlattenTo2D();

                                var fid = feature.GetFID();

                                if (config.ShouldCheckShortObject && GeometryRepresentsLine(workingGeometry))
                                {
                                    var length = workingGeometry.Length();
                                    if (length < _criteria.MinLineLength && length > 0)
                                    {
                                        int pointCount = workingGeometry.GetPointCount();
                                        double midX = 0, midY = 0;
                                        if (pointCount > 0)
                                        {
                                            int midIndex = pointCount / 2;
                                            midX = workingGeometry.GetX(midIndex);
                                            midY = workingGeometry.GetY(midIndex);
                                        }

                                        workingGeometry.ExportToWkt(out string wkt);

                                        errors.Add(new ValidationError
                                        {
                                            ErrorCode = "GEOM_SHORT_LINE",
                                            Message = $"선이 너무 짧습니다: {length:F3}m (최소: {_criteria.MinLineLength}m)",
                                            TableName = config.TableId,
                                            FeatureId = fid.ToString(),
                                            Severity = Models.Enums.ErrorSeverity.Warning,
                                            Metadata =
                                            {
                                                ["X"] = midX.ToString(),
                                                ["Y"] = midY.ToString(),
                                                ["GeometryWkt"] = wkt
                                            }
                                        });
                                    }
                                }

                                if (config.ShouldCheckSmallArea && GeometryRepresentsPolygon(workingGeometry))
                                {
                                    var area = workingGeometry.GetArea();
                                    if (area > 0 && area < _criteria.MinPolygonArea)
                                    {
                                        var envelope = new Envelope();
                                        workingGeometry.GetEnvelope(envelope);
                                        double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
                                        double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

                                        workingGeometry.ExportToWkt(out string wkt);

                                        errors.Add(new ValidationError
                                        {
                                            ErrorCode = "GEOM_SMALL_AREA",
                                            Message = $"면적이 너무 작습니다: {area:F2}㎡ (최소: {_criteria.MinPolygonArea}㎡)",
                                            TableName = config.TableId,
                                            FeatureId = fid.ToString(),
                                            Severity = Models.Enums.ErrorSeverity.Warning,
                                            Metadata =
                                            {
                                                ["X"] = centerX.ToString(),
                                                ["Y"] = centerY.ToString(),
                                                ["GeometryWkt"] = wkt
                                            }
                                        });
                                    }
                                }

                                if (config.ShouldCheckMinPoints)
                                {
                                    var minVertexCheck = EvaluateMinimumVertexRequirement(workingGeometry);
                                    if (!minVertexCheck.IsValid)
                                    {
                                        var detail = string.IsNullOrWhiteSpace(minVertexCheck.Detail)
                                            ? string.Empty
                                            : $" ({minVertexCheck.Detail})";

                                        double x = 0, y = 0;
                                        if (workingGeometry.GetPointCount() > 0)
                                        {
                                            x = workingGeometry.GetX(0);
                                            y = workingGeometry.GetY(0);
                                        }
                                        else
                                        {
                                            var env = new Envelope();
                                            workingGeometry.GetEnvelope(env);
                                            x = (env.MinX + env.MaxX) / 2.0;
                                            y = (env.MinY + env.MaxY) / 2.0;
                                        }

                                        workingGeometry.ExportToWkt(out string wkt);

                                        errors.Add(new ValidationError
                                        {
                                            ErrorCode = "GEOM_MIN_VERTEX",
                                            Message = $"정점 수가 부족합니다: {minVertexCheck.ObservedVertices}개 (최소: {minVertexCheck.RequiredVertices}개){detail}",
                                            TableName = config.TableId,
                                            FeatureId = fid.ToString(),
                                            Severity = Models.Enums.ErrorSeverity.Error,
                                            Metadata =
                                            {
                                                ["PolygonDebug"] = BuildPolygonDebugInfo(workingGeometry, minVertexCheck),
                                                ["X"] = x.ToString(),
                                                ["Y"] = y.ToString(),
                                                ["GeometryWkt"] = wkt
                                            }
                                        });
                                    }
                                }
                            }
                            finally
                            {
                                workingGeometry?.Dispose();
                                linearized?.Dispose();
                                geometryClone?.Dispose();
                            }
                        }
                    }
                }
                finally
                {
                    layer.SetIgnoredFields(Array.Empty<string>());
                }
            }, cancellationToken);

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("기본 기하 속성 검사 완료: {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초", 
                errors.Count, elapsed);

            return errors.ToList();
        }

        /// <summary>
        /// 고급 기하 특징 검사 (슬리버, 스파이크)
        /// </summary>
        private async Task<List<ValidationError>> CheckAdvancedGeometricFeaturesInternalAsync(
            Layer layer, 
            GeometryCheckConfig config, 
            CancellationToken cancellationToken)
        {
            var errors = new ConcurrentBag<ValidationError>();
            
            _logger.LogInformation("고급 기하 특징 검사 시작 (슬리버, 스파이크)");
            var startTime = DateTime.Now;

            await Task.Run(() =>
            {
                layer.ResetReading();
                Feature? feature;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var geometry = feature.GetGeometryRef();
                        if (geometry == null) continue;

                        var fid = feature.GetFID();

                        // 1. 슬리버 폴리곤 검사 (얇고 긴 폴리곤)
                        if (config.ShouldCheckSliver && config.GeometryType.Contains("POLYGON"))
                        {
                            if (IsSliverPolygon(geometry, out string sliverMessage))
                            {
                                double centerX = 0, centerY = 0;
                                if (geometry.GetGeometryCount() > 0)
                                {
                                    var exteriorRing = geometry.GetGeometryRef(0);
                                    if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                                    {
                                        int pointCount = exteriorRing.GetPointCount();
                                        int midIndex = pointCount / 2;
                                        centerX = exteriorRing.GetX(midIndex);
                                        centerY = exteriorRing.GetY(midIndex);
                                    }
                                }
                                if (centerX == 0 && centerY == 0)
                                {
                                    var env = new Envelope();
                                    geometry.GetEnvelope(env);
                                    centerX = (env.MinX + env.MaxX) / 2.0;
                                    centerY = (env.MinY + env.MaxY) / 2.0;
                                }
                                geometry.ExportToWkt(out string wkt);

                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "GEOM_SLIVER",
                                    Message = sliverMessage,
                                    TableName = config.TableId,
                                    FeatureId = fid.ToString(),
                                    Severity = Models.Enums.ErrorSeverity.Warning,
                                    X = centerX,
                                    Y = centerY,
                                    GeometryWKT = QcError.CreatePointWKT(centerX, centerY),
                                    Metadata =
                                    {
                                        ["X"] = centerX.ToString(),
                                        ["Y"] = centerY.ToString(),
                                        ["GeometryWkt"] = wkt,
                                        ["OriginalGeometryWKT"] = wkt
                                    }
                                });
                            }
                        }

                        // 2. 스파이크 검사 (뾰족한 돌출부)
                        if (config.ShouldCheckSpikes)
                        {
                            geometry.ExportToWkt(out string wkt);
                            if (HasSpike(geometry, out string spikeMessage, out double spikeX, out double spikeY))
                            {
                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "GEOM_SPIKE",
                                    Message = spikeMessage,
                                    TableName = config.TableId,
                                    FeatureId = fid.ToString(),
                                    Severity = Models.Enums.ErrorSeverity.Warning,
                                    X = spikeX,
                                    Y = spikeY,
                                    GeometryWKT = QcError.CreatePointWKT(spikeX, spikeY),
                                    Metadata =
                                    {
                                        ["X"] = spikeX.ToString(),
                                        ["Y"] = spikeY.ToString(),
                                        ["GeometryWkt"] = wkt,
                                        ["OriginalGeometryWKT"] = wkt
                                    }
                                });
                            }
                        }
                    }
                }
            }, cancellationToken);

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("고급 기하 특징 검사 완료: {ErrorCount}개 오류, 소요시간: {Elapsed:F2}초", 
                errors.Count, elapsed);

            return errors.ToList();
        }

        /// <summary>
        /// 슬리버 폴리곤 판정 (면적/형태지수/신장률 기반)
        /// </summary>
        private bool IsSliverPolygon(Geometry geometry, out string message)
        {
            message = string.Empty;

            try
            {
                        var area = GetSurfaceArea(geometry);
                
                // 면적이 0 또는 음수면 스킵
                if (area <= 0) return false;
                
                using var boundary = geometry.Boundary();
                if (boundary == null) return false;
                
                var perimeter = boundary.Length();
                if (perimeter <= 0) return false;

                // 형태 지수 (Shape Index) = 4π × Area / Perimeter²
                // 1(원)에 가까울수록 조밀, 0에 가까울수록 얇고 긺
                var shapeIndex = (4 * Math.PI * area) / (perimeter * perimeter);

                // 신장률 (Elongation) = Perimeter² / (4π × Area)
                var elongation = (perimeter * perimeter) / (4 * Math.PI * area);

                // ★ 슬리버 판정: 모든 조건을 동시에 만족해야 함 (AND 조건)
                if (area < _criteria.SliverArea && 
                    shapeIndex < _criteria.SliverShapeIndex && 
                    elongation > _criteria.SliverElongation)
                {
                    message = $"슬리버 폴리곤: 면적={area:F2}㎡ (< {_criteria.SliverArea}), " +
                              $"형태지수={shapeIndex:F3} (< {_criteria.SliverShapeIndex}), " +
                              $"신장률={elongation:F1} (> {_criteria.SliverElongation})";
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "슬리버 검사 중 오류");
            }

            return false;
        }

        /// <summary>
        /// 스파이크 검출 (뾰족한 돌출부)
        /// </summary>
        private bool HasSpike(Geometry geometry, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            try
            {
                // 지오메트리 타입 평탄화 (25D 등 변형 타입 대응)
                var flattened = wkbFlatten(geometry.GetGeometryType());

                // 멀티폴리곤: 각 폴리곤의 모든 링 검사
                if (flattened == wkbGeometryType.wkbMultiPolygon)
                {
                    var polyCount = geometry.GetGeometryCount();
                    for (int p = 0; p < polyCount; p++)
                    {
                        var polygon = geometry.GetGeometryRef(p);
                        if (polygon == null) continue;
                        if (CheckSpikeInSingleGeometry(polygon, out message, out spikeX, out spikeY))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                // 폴리곤 또는 기타: 단일 지오메트리 경로
                return CheckSpikeInSingleGeometry(geometry, out message, out spikeX, out spikeY);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "스파이크 검사 중 오류");
            }

            return false;
        }

        /// <summary>
        /// 단일 지오메트리에서 스파이크 검사
        /// - 폴리곤: 모든 링(외곽/홀)에 대해 순환 인덱싱으로 각도 검사
        /// - 링/라인스트링: 순환 인덱싱으로 각도 검사
        /// </summary>
        private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            var flattened = wkbFlatten(geometry.GetGeometryType());

            // CSV에서 로드한 임계값 사용 (도)
            var threshold = _criteria.SpikeAngleThresholdDegrees > 0 ? _criteria.SpikeAngleThresholdDegrees : 10.0;

            // 폴리곤: 각 링 검사
            if (flattened == wkbGeometryType.wkbPolygon)
            {
                var ringCount = geometry.GetGeometryCount();
                for (int r = 0; r < ringCount; r++)
                {
                    var ring = geometry.GetGeometryRef(r);
                    if (ring == null) continue;
                    if (CheckSpikeInLinearRing(ring, threshold, out message, out spikeX, out spikeY))
                    {
                        return true;
                    }
                }
                return false;
            }

            // 링 또는 라인스트링: 직접 검사
            if (flattened == wkbGeometryType.wkbLinearRing || flattened == wkbGeometryType.wkbLineString)
            {
                return CheckSpikeInLinearRing(geometry, threshold, out message, out spikeX, out spikeY);
            }

            // 멀티폴리곤은 HasSpike에서 처리, 그 외는 없음
            // 단일 멀티라인/멀티포인트는 스파이크 대상 아님
            return false;
        }

        /// <summary>
        /// LinearRing/LineString에서 스파이크 검사 (폐합 고려 순환 인덱싱)
        /// </summary>
        private bool CheckSpikeInLinearRing(Geometry ring, double thresholdDeg, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            var n = ring.GetPointCount();
            if (n < 3) return false;

            // 폐합 여부 확인 (첫점=마지막점)
            var firstX = ring.GetX(0);
            var firstY = ring.GetY(0);
            var lastX = ring.GetX(n - 1);
            var lastY = ring.GetY(n - 1);
            var closed = (Math.Abs(firstX - lastX) < 1e-9) && (Math.Abs(firstY - lastY) < 1e-9);

            // 중복 마지막점을 제외한 유효 정점 수
            var count = closed ? n - 1 : n;
            if (count < 3) return false;

            // 완화형 후보(스파이크 유사) 추적: 최소 각도 및 높이 조건
            double bestAngle = double.MaxValue;
            int bestIndex = -1;
            double bestHeight = 0;

            // 보조 임계값: 각도 완화 상한과 최소 높이(좌표계 단위)
            double threshold = _criteria.SpikeAngleThresholdDegrees;
            double fallbackAngleMax = 80.0; // 완화 기준: 80도 이내면 후보
            double minHeight = Math.Max(_criteria.MinLineLength * 0.2, 0.05); // 데이터 스케일 따라 조정

            // 스파이크 후보 수집 리스트
            var spikeCandidates = new List<(int idx,double x,double y,double angle)>();
            (int idx,double x,double y,double angle) best = default;
            double minAngle = double.MaxValue;

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;

                var x1 = ring.GetX(prev);
                var y1 = ring.GetY(prev);
                var x2 = ring.GetX(i);
                var y2 = ring.GetY(i);
                var x3 = ring.GetX(next);
                var y3 = ring.GetY(next);

                var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

                // 최소 각도 추적 (SaveAllSpikes == false 대비)
                if (angle < minAngle)
                {
                    minAngle = angle;
                    best = (i, x2, y2, angle);
                }

                // 임계각도 미만 후보 저장
                if (angle < threshold)
                {
                    spikeCandidates.Add((i, x2, y2, angle));
                }
            }

            // ----- 결과 확정 단계 -----
            if (spikeCandidates.Any())
            {
                // 가장 날카로운 스파이크 반환 (기본 동작)
                spikeX = best.x;
                spikeY = best.y;
                message = $"스파이크 검출: 정점 {best.idx}번 각도 {best.angle:F1}도";
                return true;
            }

            // 스파이크가 없으면 완화 기준으로 검사 (기존 로직)
            // ...

            // 사용하지 않는 변수 제거를 위한 주석
            // bestAngle, bestIndex, bestHeight, fallbackAngleMax는 완화 기준에서 사용될 수 있음

            return false;
        }

        /// <summary>
        /// 세 점으로 이루어진 각도 계산 (도 단위)
        /// </summary>
        private double CalculateAngle(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var v1x = x1 - x2;
            var v1y = y1 - y2;
            var v2x = x3 - x2;
            var v2y = y3 - y2;

            var dotProduct = v1x * v2x + v1y * v2y;
            var mag1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var mag2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (mag1 == 0 || mag2 == 0) return 180.0;

            var cosAngle = dotProduct / (mag1 * mag2);
            cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));

            var angleRadians = Math.Acos(cosAngle);
            return angleRadians * 180.0 / Math.PI;
        }

        /// <summary>
        /// 점과 선분 사이의 최소 거리(좌표계 단위)
        /// </summary>
        private static double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            var vx = x2 - x1;
            var vy = y2 - y1;
            var wx = px - x1;
            var wy = py - y1;

            var c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

            var c2 = vx * vx + vy * vy;
            if (c2 <= 0) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

            var t = c1 / c2;
            var projX = x1 + t * vx;
            var projY = y1 + t * vy;
            var dx = px - projX;
            var dy = py - projY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 지오메트리 타입별 최소 정점 수
        /// </summary>
        /// <summary>
        /// 지오메트리가 선형 타입인지 여부를 확인합니다
        /// </summary>
        private static bool GeometryRepresentsLine(Geometry geometry)
        {
            var type = wkbFlatten(geometry.GetGeometryType());
            return type == wkbGeometryType.wkbLineString || type == wkbGeometryType.wkbMultiLineString;
        }

        /// <summary>
        /// 지오메트리가 폴리곤 타입인지 여부를 확인합니다
        /// </summary>
        private static bool GeometryRepresentsPolygon(Geometry geometry)
        {
            var type = wkbFlatten(geometry.GetGeometryType());
            return type == wkbGeometryType.wkbPolygon || type == wkbGeometryType.wkbMultiPolygon;
        }

        /// <summary>
        /// GDAL wkb 타입에서 상위 플래그를 제거합니다
        /// </summary>
        private static wkbGeometryType wkbFlatten(wkbGeometryType type)
        {
            return (wkbGeometryType)((int)type & 0xFF);
        }

        /// <summary>
        /// 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult EvaluateMinimumVertexRequirement(Geometry geometry)
        {
            var flattenedType = wkbFlatten(geometry.GetGeometryType());
            return flattenedType switch
            {
                wkbGeometryType.wkbPoint => CheckPointMinimumVertices(geometry),
                wkbGeometryType.wkbMultiPoint => CheckMultiPointMinimumVertices(geometry),
                wkbGeometryType.wkbLineString => CheckLineStringMinimumVertices(geometry),
                wkbGeometryType.wkbMultiLineString => CheckMultiLineStringMinimumVertices(geometry),
                wkbGeometryType.wkbPolygon => CheckPolygonMinimumVertices(geometry),
                wkbGeometryType.wkbMultiPolygon => CheckMultiPolygonMinimumVertices(geometry),
                _ => MinVertexCheckResult.Valid()
            };
        }

        /// <summary>
        /// 포인트 최소 정점 조건을 평가합니다
        /// </summary>
        /// <summary>
        /// 최소 정점 판정 결과를 표현합니다
        /// </summary>
        private readonly record struct MinVertexCheckResult(bool IsValid, int ObservedVertices, int RequiredVertices, string Detail)
        {
            public static MinVertexCheckResult Valid(int observed = 0, int required = 0) => new(true, observed, required, string.Empty);

            public static MinVertexCheckResult Invalid(int observed, int required, string detail) => new(false, observed, required, detail);
        }

        private MinVertexCheckResult CheckPointMinimumVertices(Geometry geometry)
        {
            var pointCount = geometry.GetPointCount();
            return pointCount >= 1
                ? MinVertexCheckResult.Valid(pointCount, 1)
                : MinVertexCheckResult.Invalid(pointCount, 1, "포인트 정점 부족");
        }

        /// <summary>
        /// 멀티포인트 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult CheckMultiPointMinimumVertices(Geometry geometry)
        {
            var geometryCount = geometry.GetGeometryCount();
            var totalPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var component = geometry.GetGeometryRef(i)?.Clone();
                if (component == null)
                {
                    continue;
                }

                totalPoints += component.GetPointCount();
            }

            return totalPoints >= 1
                ? MinVertexCheckResult.Valid(totalPoints, 1)
                : MinVertexCheckResult.Invalid(totalPoints, 1, "멀티포인트 정점 부족");
        }

        /// <summary>
        /// 라인 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult CheckLineStringMinimumVertices(Geometry geometry)
        {
            var pointCount = geometry.GetPointCount();
            return pointCount >= 2
                ? MinVertexCheckResult.Valid(pointCount, 2)
                : MinVertexCheckResult.Invalid(pointCount, 2, "라인 정점 부족");
        }

        /// <summary>
        /// 멀티라인 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult CheckMultiLineStringMinimumVertices(Geometry geometry)
        {
            var geometryCount = geometry.GetGeometryCount();
            var aggregatedPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var component = geometry.GetGeometryRef(i)?.Clone();
                if (component == null)
                {
                    continue;
                }

                var componentCheck = CheckLineStringMinimumVertices(component);
                aggregatedPoints += componentCheck.ObservedVertices;
                if (!componentCheck.IsValid)
                {
                    return MinVertexCheckResult.Invalid(componentCheck.ObservedVertices, componentCheck.RequiredVertices, $"라인 {i} 정점 부족");
                }
            }

            return aggregatedPoints >= 2
                ? MinVertexCheckResult.Valid(aggregatedPoints, 2)
                : MinVertexCheckResult.Invalid(aggregatedPoints, 2, "멀티라인 전체 정점 부족");
        }

        /// <summary>
        /// 폴리곤 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult CheckPolygonMinimumVertices(Geometry geometry)
        {
            var ringCount = geometry.GetGeometryCount();
            if (ringCount == 0)
            {
                return MinVertexCheckResult.Invalid(0, 3, "폴리곤 링 없음");
            }

            var totalPoints = 0;
            for (var i = 0; i < ringCount; i++)
            {
                using var ring = geometry.GetGeometryRef(i)?.Clone();
                if (ring == null)
                {
                    continue;
                }

                ring.FlattenTo2D();

                if (!RingIsClosed(ring, _ringClosureTolerance))
                {
                    return MinVertexCheckResult.Invalid(ring.GetPointCount(), 3, $"링 {i}가 폐합되지 않았습니다");
                }

                var pointCount = GetUniquePointCount(ring);
                totalPoints += pointCount;

                if (pointCount < 3)
                {
                    return MinVertexCheckResult.Invalid(pointCount, 3, $"링 {i} 정점 부족");
                }
            }

            return MinVertexCheckResult.Valid(totalPoints, 3);
        }

        /// <summary>
        /// 멀티폴리곤 최소 정점 조건을 평가합니다
        /// </summary>
        private MinVertexCheckResult CheckMultiPolygonMinimumVertices(Geometry geometry)
        {
            var geometryCount = geometry.GetGeometryCount();
            var totalPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var polygon = geometry.GetGeometryRef(i)?.Clone();
                if (polygon == null)
                {
                    continue;
                }

                polygon.FlattenTo2D();
                var polygonCheck = CheckPolygonMinimumVertices(polygon);
                totalPoints += polygonCheck.ObservedVertices;
                if (!polygonCheck.IsValid)
                {
                    return MinVertexCheckResult.Invalid(polygonCheck.ObservedVertices, polygonCheck.RequiredVertices, $"폴리곤 {i} 오류: {polygonCheck.Detail}");
                }
            }

            return totalPoints >= 3
                ? MinVertexCheckResult.Valid(totalPoints, 3)
                : MinVertexCheckResult.Invalid(totalPoints, 3, "멀티폴리곤 전체 정점 부족");
        }

        private string BuildPolygonDebugInfo(Geometry geometry, MinVertexCheckResult result)
        {
            if (!GeometryRepresentsPolygon(geometry))
            {
                return string.Empty;
            }

            var info = new System.Text.StringBuilder();
            info.AppendLine($"링 개수: {geometry.GetGeometryCount()}");
            for (var i = 0; i < geometry.GetGeometryCount(); i++)
            {
                using var ring = geometry.GetGeometryRef(i)?.Clone();
                if (ring == null)
                {
                    continue;
                }

                ring.FlattenTo2D();
                var uniqueCount = GetUniquePointCount(ring);
                var isClosed = RingIsClosed(ring, _ringClosureTolerance);
                info.AppendLine($" - 링 {i}: 고유 정점 {uniqueCount}개, 폐합 {(isClosed ? "Y" : "N")}");
            }

            info.AppendLine($"관측 정점: {result.ObservedVertices}, 요구 정점: {result.RequiredVertices}");
            return info.ToString();
        }

        private int GetUniquePointCount(Geometry ring)
        {
            var tolerance = _ringClosureTolerance;
            var scaledTolerance = 1.0 / tolerance;
            var unique = new HashSet<(long X, long Y)>();
            var coordinate = new double[3];

            for (var i = 0; i < ring.GetPointCount(); i++)
            {
                ring.GetPoint(i, coordinate);
                var key = ((long)Math.Round(coordinate[0] * scaledTolerance), (long)Math.Round(coordinate[1] * scaledTolerance));
                unique.Add(key);
            }

            if (unique.Count > 1)
            {
                var firstPoint = new double[3];
                var lastPoint = new double[3];
                ring.GetPoint(0, firstPoint);
                ring.GetPoint(ring.GetPointCount() - 1, lastPoint);
                if (ArePointsClose(firstPoint, lastPoint, tolerance))
                {
                    var lastKey = ((long)Math.Round(lastPoint[0] * scaledTolerance), (long)Math.Round(lastPoint[1] * scaledTolerance));
                    unique.Remove(lastKey);
                }
            }

            return unique.Count;
        }

        private static bool RingIsClosed(Geometry ring, double tolerance)
        {
            var first = new double[3];
            var last = new double[3];
            ring.GetPoint(0, first);
            ring.GetPoint(ring.GetPointCount() - 1, last);
            return ArePointsClose(first, last, tolerance);
        }

        private static bool ArePointsClose(double[] p1, double[] p2, double tolerance)
        {
            var dx = p1[0] - p2[0];
            var dy = p1[1] - p2[1];
            var distanceSquared = (dx * dx) + (dy * dy);
            return distanceSquared <= tolerance * tolerance;
        }

        /// <summary>
        /// GeometryErrorDetail을 ValidationError로 변환
        /// </summary>
        private List<ValidationError> ConvertToValidationErrors(
            List<GeometryErrorDetail> errorDetails, 
            string tableName, 
            string errorCode)
        {
            return errorDetails.Select(e => new ValidationError
            {
                ErrorCode = errorCode,
                Message = e.DetailMessage ?? e.ErrorType,
                TableName = tableName,
                FeatureId = e.ObjectId,
                Severity = Models.Enums.ErrorSeverity.Error
            }).ToList();
        }

        #endregion
        
        /// <summary>
        /// 면적 계산 시 타입 가드: 폴리곤/멀티폴리곤에서만 면적 반환, 그 외 0
        /// </summary>
        private static double GetSurfaceArea(Geometry geometry)
        {
            try
            {
                if (geometry == null || geometry.IsEmpty()) return 0.0;
                var t = geometry.GetGeometryType();
                return t == wkbGeometryType.wkbPolygon || t == wkbGeometryType.wkbMultiPolygon
                    ? geometry.GetArea()
                    : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }
    }
}

