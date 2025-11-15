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
using ConfigPerformanceSettings = SpatialCheckPro.Models.Config.PerformanceSettings;

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
        private readonly IFeatureFilterService _featureFilterService;

        // Phase 2.3: 공간 인덱스 캐싱 (중복 생성 방지)
        private readonly ConcurrentDictionary<string, object> _spatialIndexCache = new();

        public GeometryCheckProcessor(
            ILogger<GeometryCheckProcessor> logger,
            SpatialIndexService? spatialIndexService = null,
            HighPerformanceGeometryValidator? highPerfValidator = null,
            GeometryCriteria? criteria = null,
            IFeatureFilterService? featureFilterService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spatialIndexService = spatialIndexService;
            _highPerfValidator = highPerfValidator;
            _criteria = criteria ?? GeometryCriteria.CreateDefault();
            _ringClosureTolerance = _criteria.RingClosureTolerance;
            _featureFilterService = featureFilterService ?? new FeatureFilterService(
                logger as ILogger<FeatureFilterService> ?? new LoggerFactory().CreateLogger<FeatureFilterService>(),
                new ConfigPerformanceSettings());
        }

        /// <summary>
        /// 마지막 실행에서 스킵된 피처 수
        /// </summary>
        public int LastSkippedFeatureCount { get; private set; }

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

                // 필터 적용 전 총 피처 수
                var totalFeatureCountBeforeFilter = layer.GetFeatureCount(1);
                
                LastSkippedFeatureCount = 0;
                var filterOutcome = _featureFilterService.ApplyObjectChangeFilter(
                    layer,
                    "Geometry",
                    config.TableId);
                if (filterOutcome.Applied)
                {
                    LastSkippedFeatureCount = filterOutcome.ExcludedCount;
                }
                result.SkippedCount = LastSkippedFeatureCount;

                // 필터 적용 후 실제 검수 대상 피처 수
                var featureCount = layer.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처: {Count}개 (필터 전: {BeforeFilter}, 제외: {Excluded})", 
                    featureCount, totalFeatureCountBeforeFilter, LastSkippedFeatureCount);
                
                // A. 필터 적용 검증 (처음 10개 테스트)
                if (filterOutcome.Applied)
                {
                    int testCount = 0;
                    layer.ResetReading();
                    while (layer.GetNextFeature() != null && testCount < 10)
                    {
                        testCount++;
                    }
                    layer.ResetReading();
                    
                    if (testCount > 10)
                    {
                        _logger.LogWarning("필터 검증 이상: 처음 10개 요청했으나 {Actual}개 반환됨. 필터가 제대로 작동하지 않을 수 있음.", testCount);
                    }
                    else
                    {
                        _logger.LogDebug("필터 검증 통과: 처음 {Count}개 반환", testCount);
                    }
                }

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

                // === 네트워크 연결성 검사 (언더슛, 오버슛) - 별도 순회 필요 ===
                if (config.ShouldCheckUndershoot || config.ShouldCheckOvershoot)
                {
                    var networkErrors = await CheckUndershootOvershootAsync(layer, config, cancellationToken);
                    
                    if (errorWriter != null)
                    {
                        await errorWriter.WriteErrorsAsync(networkErrors);
                    }
                    else
                    {
                        result.Errors.AddRange(networkErrors);
                    }
                    result.ErrorCount += networkErrors.Count;
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
            
            // 총 피처 수 미리 캐시 (매번 호출 방지)
            var totalFeatureCount = layer.GetFeatureCount(1);

            // 오류 추가 헬퍼 (스트리밍 모드와 메모리 모드 모두 지원)
            Action<ValidationError> _AddErrorToResult = (error) =>
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
                    int skippedByFilter = 0;
                    int maxIterations = (int)(totalFeatureCount * 1.5); // 안전장치: 예상의 1.5배까지만
                    var processedFids = new HashSet<long>(); // C. FID 중복 방지

                    while ((feature = layer.GetNextFeature()) != null && processedCount < maxIterations)
                    {
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var fid = feature.GetFID();
                            
                            // C. FID 중복 체크
                            if (processedFids.Contains(fid))
                            {
                                _logger.LogWarning("중복 FID 발견: {FID}, 레이어: {Layer}", fid, config.TableId);
                                continue;
                            }
                            processedFids.Add(fid);
                            
                            // B. 수동 필터링 (GDAL 필터 우회)
                            if (_featureFilterService.ShouldSkipFeature(feature, config.TableId, out var excludedCode))
                            {
                                skippedByFilter++;
                                if (skippedByFilter <= 10) // 처음 10개만 로그
                                {
                                    _logger.LogDebug("수동 필터로 제외: FID={FID}, Code={Code}", fid, excludedCode);
                                }
                                continue;
                            }
                            
                            processedCount++;

                            var geometryRef = feature.GetGeometryRef();
                            if (geometryRef == null || geometryRef.IsEmpty())
                            {
                                continue;
                            }

                            // ========================================
                            // 1. GEOS 유효성 검사 (최적화: 빠른 검사만 수행)
                            // ========================================
                            bool needsDetailedValidation = config.ShouldCheckSelfIntersection || config.ShouldCheckSelfOverlap || config.ShouldCheckPolygonInPolygon;
                            
                            if (needsDetailedValidation)
                            {
                                // GDAL 기본 검사만 수행 (NTS 우회)
                                bool isGdalValid = true;
                                try
                                {
                                    isGdalValid = geometryRef.IsValid();
                                }
                                catch
                                {
                                    isGdalValid = false;
                                }
                                
                                if (!isGdalValid)
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

                                    _AddErrorToResult(new ValidationError
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

                                // IsSimple() 검사 (자기교차) - 최적화: GDAL만 사용
                                bool isGdalSimple = true;
                                try
                                {
                                    isGdalSimple = geometryRef.IsSimple();
                                }
                                catch
                                {
                                    isGdalSimple = false;
                                }
                                
                                if (!isGdalSimple)
                                {
                                    // 간단한 오류만 기록 (NTS 상세 분석 생략)
                                    var (centerX, centerY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometryRef);
                                    
                                    _AddErrorToResult(new ValidationError
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

                            // ========================================
                            // 2. 기본 기하 속성 검사 (최적화: 필요한 경우에만 복제)
                            // ========================================
                            bool needsGeometryProcessing = config.ShouldCheckShortObject || 
                                                          config.ShouldCheckSmallArea || 
                                                          config.ShouldCheckMinPoints ||
                                                          config.ShouldCheckSliver ||
                                                          config.ShouldCheckSpikes;
                            
                            // 디버그: 첫 피처에서 검사 조건 로그
                            if (processedCount == 1)
                            {
                                _logger.LogInformation("검사 조건: ShortObject={Short}, SmallArea={Small}, MinPoints={Min}, Sliver={Sliver}, Spikes={Spike}, NeedsProcessing={Needs}",
                                    config.ShouldCheckShortObject, config.ShouldCheckSmallArea, config.ShouldCheckMinPoints,
                                    config.ShouldCheckSliver, config.ShouldCheckSpikes, needsGeometryProcessing);
                            }
                            
                            if (needsGeometryProcessing)
                            {
                                Geometry? geometryClone = null;
                                Geometry? linearized = null;
                                Geometry? workingGeometry = null;

                                try
                                {
                                    // Geometry 복제 및 선형화 (곡선 처리) - 필요할 때만
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

                                            _AddErrorToResult(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SHORT_LINE",
                                                Message = $"선이 너무 짧습니다: {length:F3}m (최소: {_criteria.MinLineLength}m)",
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Error,
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

                                            _AddErrorToResult(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SMALL_AREA",
                                                Message = $"면적이 너무 작습니다: {area:F2}㎡ (최소: {_criteria.MinPolygonArea}㎡)",
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Error,
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

                                            _AddErrorToResult(new ValidationError
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

                                    // 3-1. 슬리버 폴리곤 검사 (최적화: workingGeometry 재사용)
                                    if (config.ShouldCheckSliver && config.GeometryType.Contains("POLYGON") && workingGeometry != null)
                                    {
                                        if (IsSliverPolygon(workingGeometry, out string sliverMessage))
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

                                            _AddErrorToResult(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SLIVER",
                                                Message = sliverMessage,
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Error,
                                                Metadata =
                                                {
                                                    ["X"] = centerX.ToString(),
                                                    ["Y"] = centerY.ToString(),
                                                    ["GeometryWkt"] = wkt
                                                }
                                            });
                                        }
                                    }

                                    // 3-2. 스파이크 검사 (최적화: workingGeometry 재사용)
                                    if (config.ShouldCheckSpikes && workingGeometry != null)
                                    {
                                        if (processedCount == 1)
                                        {
                                            _logger.LogInformation("스파이크 검사 시작: GeometryType={Type}, PointCount={Points}",
                                                workingGeometry.GetGeometryType(), workingGeometry.GetPointCount());
                                        }
                                        
                                        if (HasSpike(workingGeometry, out string spikeMessage, out double spikeX, out double spikeY))
                                        {
                                            _logger.LogInformation("스파이크 검출: FID={FID}, Message={Message}, X={X}, Y={Y}",
                                                fid, spikeMessage, spikeX, spikeY);
                                            
                                            workingGeometry.ExportToWkt(out string wkt);
                                            _AddErrorToResult(new ValidationError
                                            {
                                                ErrorCode = "GEOM_SPIKE",
                                                Message = spikeMessage,
                                                TableName = config.TableId,
                                                FeatureId = fid.ToString(),
                                                Severity = Models.Enums.ErrorSeverity.Error,
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
                                finally
                                {
                                    // Geometry 리소스 정리
                                    workingGeometry?.Dispose();
                                    linearized?.Dispose();
                                    geometryClone?.Dispose();
                                }
                            }

                            // 진행률 로깅 (100개마다)
                            if (processedCount % 100 == 0)
                            {
                                _logger.LogInformation("단일 순회 검사 진행: {Count}/{Total}", processedCount, totalFeatureCount);
                            }
                        }
                    }
                    // 루프 종료 후 최종 카운트 로그
                    var totalIterations = processedCount + skippedByFilter + (processedFids.Count - processedCount);
                    
                    if (totalIterations >= maxIterations)
                    {
                        _logger.LogError("안전장치 발동: 최대 반복 횟수({MaxIterations})에 도달하여 강제 종료. 무한 루프 가능성 있음.", maxIterations);
                    }
                    
                    if (skippedByFilter > 0)
                    {
                        _logger.LogInformation("수동 필터로 제외된 피처: {Count}개 (OBJFLTN_SE 기준)", skippedByFilter);
                    }
                    
                    if (processedFids.Count != processedCount + skippedByFilter)
                    {
                        _logger.LogWarning("FID 중복 발견: 고유 FID {Unique}개, 처리+스킵 {Total}개", 
                            processedFids.Count, processedCount + skippedByFilter);
                    }
                    
                    if (processedCount > totalFeatureCount)
                    {
                        _logger.LogWarning("예상 피처 수({Expected})를 초과하여 {Actual}개 처리됨. 필터 미작동 또는 중복 반환 의심.", 
                            totalFeatureCount, processedCount);
                    }
                    
                    _logger.LogInformation("단일 순회 완료: 검수 {Processed}개, 제외 {Skipped}개, 고유 FID {Unique}개 (예상: {Expected})", 
                        processedCount, skippedByFilter, processedFids.Count, totalFeatureCount);
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
                                            Severity = Models.Enums.ErrorSeverity.Error, // 변경: 경고 제거, 모두 오류로 처리
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
                                    Severity = Models.Enums.ErrorSeverity.Error,
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
                        if (config.ShouldCheckSpikes &&
                            HasSpike(geometry, out string spikeMessage, out double spikeX, out double spikeY))
                        {
                            geometry.ExportToWkt(out string wkt);
                            errors.Add(new ValidationError
                            {
                                ErrorCode = "GEOM_SPIKE",
                                Message = spikeMessage,
                                TableName = config.TableId,
                                FeatureId = fid.ToString(),
                                Severity = Models.Enums.ErrorSeverity.Error,
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

            // 보조 임계값: 각도 완화 상한과 최소 높이(좌표계 단위)
            double threshold = _criteria.SpikeAngleThresholdDegrees;
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
            try
            {
                if (!GeometryRepresentsPolygon(geometry))
                {
                    return string.Empty;
                }

                var info = new System.Text.StringBuilder();
                info.AppendLine($"링 개수: {geometry.GetGeometryCount()}");
                
                for (var i = 0; i < geometry.GetGeometryCount(); i++)
                {
                    try
                    {
                        using var ring = geometry.GetGeometryRef(i)?.Clone();
                        if (ring == null)
                        {
                            info.AppendLine($" - 링 {i}: NULL");
                            continue;
                        }

                        ring.FlattenTo2D();
                        var uniqueCount = GetUniquePointCount(ring);
                        var isClosed = RingIsClosed(ring, _ringClosureTolerance);
                        info.AppendLine($" - 링 {i}: 고유 정점 {uniqueCount}개, 폐합 {(isClosed ? "Y" : "N")}");
                    }
                    catch (Exception ex)
                    {
                        info.AppendLine($" - 링 {i}: 오류 ({ex.Message})");
                    }
                }

                info.AppendLine($"관측 정점: {result.ObservedVertices}, 요구 정점: {result.RequiredVertices}");
                return info.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BuildPolygonDebugInfo 실패");
                return $"디버그 정보 생성 실패: {ex.Message}";
            }
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
            try
            {
                var pointCount = ring.GetPointCount();
                if (pointCount < 2)
                {
                    return false;
                }
                
                var first = new double[3];
                var last = new double[3];
                ring.GetPoint(0, first);
                ring.GetPoint(pointCount - 1, last);
                return ArePointsClose(first, last, tolerance);
            }
            catch
            {
                return false;
            }
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
                Severity = Models.Enums.ErrorSeverity.Error,
                X = e.X,
                Y = e.Y,
                GeometryWKT = e.GeometryWkt ?? QcError.CreatePointWKT(e.X, e.Y)
            }).ToList();
        }

        /// <summary>
        /// 언더슛/오버슛 검사 (선형 객체의 네트워크 연결성 검증)
        /// - 언더슛: 선 끝점이 다른 선에 가까이 있지만 연결되지 않음 (중간 부분에 가까움)
        /// - 오버슛: 선 끝점이 다른 선의 끝점을 지나쳐 연장됨
        /// </summary>
        private async Task<List<ValidationError>> CheckUndershootOvershootAsync(
            Layer layer,
            GeometryCheckConfig config,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("언더슛/오버슛 검사 시작: {TableId}", config.TableId);
                    
                    // 선형 객체만 검사
                    if (!GeometryTypeIsLine(config.GeometryType))
                    {
                        _logger.LogDebug("선형 객체가 아니므로 언더슛/오버슛 검사 스킵: {GeometryType}", config.GeometryType);
                        return errors;
                    }

                    var reader = new WKTReader();
                    var lines = new List<(long Fid, NetTopologySuite.Geometries.LineString Geometry)>();
                    
                    // 1단계: 모든 선형 객체 수집
                    layer.ResetReading();
                    Feature? feature;
                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var geom = feature.GetGeometryRef();
                            if (geom != null && !geom.IsEmpty())
                            {
                                geom.ExportToWkt(out string wkt);
                                var ntsGeom = reader.Read(wkt);
                                
                                // MultiLineString의 경우 각 LineString을 개별 처리
                                if (ntsGeom is NetTopologySuite.Geometries.MultiLineString mls)
                                {
                                    for (int i = 0; i < mls.NumGeometries; i++)
                                    {
                                        var lineString = (NetTopologySuite.Geometries.LineString)mls.GetGeometryN(i);
                                        if (lineString != null && !lineString.IsEmpty)
                                        {
                                            lines.Add((feature.GetFID(), lineString));
                                        }
                                    }
                                }
                                else if (ntsGeom is NetTopologySuite.Geometries.LineString ls)
                                {
                                    if (!ls.IsEmpty)
                                    {
                                        lines.Add((feature.GetFID(), ls));
                                    }
                                }
                            }
                        }
                    }

                    if (lines.Count < 2)
                    {
                        _logger.LogDebug("언더슛/오버슛 검사: 선형 객체가 2개 미만이므로 스킵");
                        return errors;
                    }

                    _logger.LogInformation("언더슛/오버슛 검사: {Count}개 선형 객체 수집 완료", lines.Count);

                    // 2단계: 각 선의 끝점에 대해 연결성 검사
                    double searchDistance = _criteria.NetworkSearchDistance;
                    int undershootCount = 0;
                    int overshootCount = 0;

                    for (int i = 0; i < lines.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var (fid, line) = lines[i];
                        var startPoint = line.StartPoint;
                        var endPoint = line.EndPoint;
                        var endPoints = new[] { (startPoint, "시작점"), (endPoint, "끝점") };

                        foreach (var (point, pointName) in endPoints)
                        {
                            bool isConnected = false;
                            double minDistance = double.MaxValue;
                            NetTopologySuite.Geometries.LineString? closestLine = null;
                            long closestFid = -1;

                            // 다른 모든 선과의 거리 계산
                            for (int j = 0; j < lines.Count; j++)
                            {
                                if (i == j) continue;
                                
                                var (otherFid, otherLine) = lines[j];
                                var distance = point.Distance(otherLine);

                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                    closestLine = otherLine;
                                    closestFid = otherFid;
                                }
                                
                                // 연결됨 (허용오차 1mm)
                                if (distance < 0.001)
                                {
                                    isConnected = true;
                                    break;
                                }
                            }

                            // 연결되지 않았고, 검색 거리 내에 다른 선이 있으면 오류
                            if (!isConnected && minDistance < searchDistance && closestLine != null)
                            {
                                // 가장 가까운 점 찾기
                                var nearestPoints = new NetTopologySuite.Operation.Distance.DistanceOp(point, closestLine).NearestPoints();
                                var closestPointOnTarget = new NetTopologySuite.Geometries.Point(nearestPoints[1]);
                                
                                var targetStart = closestLine.StartPoint;
                                var targetEnd = closestLine.EndPoint;
                                
                                // 오버슛: 가장 가까운 점이 대상 선의 끝점인 경우
                                bool isEndpoint = closestPointOnTarget.Distance(targetStart) < 0.001 || 
                                                 closestPointOnTarget.Distance(targetEnd) < 0.001;
                                
                                var errorType = isEndpoint ? "오버슛" : "언더슛";
                                var errorCode = isEndpoint ? "GEOM_OVERSHOOT" : "GEOM_UNDERSHOOT";
                                
                                if (isEndpoint)
                                    overshootCount++;
                                else
                                    undershootCount++;

                                // 간격 선분 WKT 생성
                                var gapLineString = new NetTopologySuite.Geometries.LineString(
                                    new[] { point.Coordinate, closestPointOnTarget.Coordinate });
                                string gapLineWkt = gapLineString.ToText();

                                errors.Add(new ValidationError
                                {
                                    ErrorCode = errorCode,
                                    Message = $"{errorType}: {pointName}이 다른 선과 연결되지 않음 (이격거리: {minDistance:F3}m, 대상 FID: {closestFid})",
                                    TableName = config.TableId,
                                    FeatureId = fid.ToString(),
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    X = point.X,
                                    Y = point.Y,
                                    GeometryWKT = gapLineWkt,
                                    Metadata =
                                    {
                                        ["X"] = point.X.ToString(),
                                        ["Y"] = point.Y.ToString(),
                                        ["Distance"] = minDistance.ToString("F3"),
                                        ["TargetFID"] = closestFid.ToString(),
                                        ["ErrorType"] = errorType,
                                        ["GeometryWkt"] = gapLineWkt
                                    }
                                });
                                
                                // 한 피처당 하나의 오류만 보고 (성능 최적화)
                                break;
                            }
                        }
                    }

                    _logger.LogInformation("언더슛/오버슛 검사 완료: 언더슛 {Undershoot}개, 오버슛 {Overshoot}개, 총 {Total}개",
                        undershootCount, overshootCount, errors.Count);

                    return errors;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "언더슛/오버슛 검사 중 오류 발생");
                    return errors;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 지오메트리 타입이 선형인지 확인
        /// </summary>
        private static bool GeometryTypeIsLine(string geometryType)
        {
            return geometryType.Contains("LINE", StringComparison.OrdinalIgnoreCase);
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


