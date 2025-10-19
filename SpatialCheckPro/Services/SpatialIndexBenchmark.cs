using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 인덱스 성능 벤치마크 및 최적 타입 선택 서비스
    /// </summary>
    public class SpatialIndexBenchmark
    {
        private readonly ILogger<SpatialIndexBenchmark> _logger;
        private readonly ISpatialIndexManager _spatialIndexManager;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="spatialIndexManager">공간 인덱스 관리자</param>
        public SpatialIndexBenchmark(ILogger<SpatialIndexBenchmark> logger, ISpatialIndexManager spatialIndexManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spatialIndexManager = spatialIndexManager ?? throw new ArgumentNullException(nameof(spatialIndexManager));
        }

        /// <summary>
        /// 데이터 특성에 따른 최적 인덱스 타입 선택
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>최적 인덱스 타입</returns>
        public async Task<SpatialIndexType> SelectOptimalIndexTypeAsync(string gdbPath, string layerName)
        {
            try
            {
                _logger.LogInformation("최적 인덱스 타입 선택 시작: {LayerName}", layerName);

                // 데이터 특성 분석
                var dataCharacteristics = await AnalyzeDataCharacteristicsAsync(gdbPath, layerName);
                
                // 특성 기반 인덱스 타입 추천
                var recommendedType = RecommendIndexType(dataCharacteristics);
                
                _logger.LogInformation("데이터 특성 분석 완료: {LayerName} - 추천 인덱스: {IndexType}", 
                    layerName, recommendedType);
                
                return recommendedType;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "최적 인덱스 타입 선택 중 오류 발생: {LayerName}", layerName);
                return SpatialIndexType.RTree; // 기본값
            }
        }

        /// <summary>
        /// 여러 인덱스 타입의 성능 벤치마크 수행
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <param name="testQueries">테스트 질의 목록</param>
        /// <returns>벤치마크 결과</returns>
        public async Task<List<IndexBenchmarkResult>> BenchmarkIndexTypesAsync(
            string gdbPath, 
            string layerName, 
            List<SpatialEnvelope> testQueries = null)
        {
            var results = new List<IndexBenchmarkResult>();
            var indexTypes = new[] { SpatialIndexType.RTree, SpatialIndexType.QuadTree, SpatialIndexType.GridIndex };

            _logger.LogInformation("인덱스 성능 벤치마크 시작: {LayerName}", layerName);

            // 테스트 질의가 제공되지 않은 경우 기본 질의 생성
            if (testQueries == null || testQueries.Count == 0)
            {
                testQueries = await GenerateTestQueriesAsync(gdbPath, layerName);
            }

            foreach (var indexType in indexTypes)
            {
                try
                {
                    var result = await BenchmarkSingleIndexTypeAsync(gdbPath, layerName, indexType, testQueries);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "인덱스 타입 {IndexType} 벤치마크 중 오류 발생", indexType);
                    
                    // 실패한 경우에도 결과에 포함 (실패 표시)
                    results.Add(new IndexBenchmarkResult
                    {
                        IndexType = indexType,
                        LayerName = layerName,
                        BuildTimeMs = -1,
                        AvgQueryTimeMs = -1,
                        MemoryUsageMB = -1,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // 결과 정렬 (성능 순)
            results = results.Where(r => r.Success)
                           .OrderBy(r => r.OverallScore)
                           .Concat(results.Where(r => !r.Success))
                           .ToList();

            _logger.LogInformation("인덱스 성능 벤치마크 완료: {LayerName}, {ResultCount}개 결과", 
                layerName, results.Count);

            return results;
        }

        /// <summary>
        /// 단일 인덱스 타입 벤치마크
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <param name="indexType">인덱스 타입</param>
        /// <param name="testQueries">테스트 질의 목록</param>
        /// <returns>벤치마크 결과</returns>
        private async Task<IndexBenchmarkResult> BenchmarkSingleIndexTypeAsync(
            string gdbPath, 
            string layerName, 
            SpatialIndexType indexType, 
            List<SpatialEnvelope> testQueries)
        {
            var result = new IndexBenchmarkResult
            {
                IndexType = indexType,
                LayerName = layerName,
                TestQueryCount = testQueries.Count
            };

            var stopwatch = Stopwatch.StartNew();
            var initialMemory = GC.GetTotalMemory(true);

            try
            {
                // 인덱스 구축 시간 측정
                _logger.LogDebug("인덱스 구축 시작: {IndexType}", indexType);
                
                var index = await _spatialIndexManager.CreateSpatialIndexAsync(gdbPath, layerName, indexType);
                
                stopwatch.Stop();
                result.BuildTimeMs = stopwatch.ElapsedMilliseconds;
                result.FeatureCount = index.GetFeatureCount();

                // 메모리 사용량 측정
                var afterBuildMemory = GC.GetTotalMemory(false);
                result.MemoryUsageMB = (afterBuildMemory - initialMemory) / (1024.0 * 1024.0);

                // 질의 성능 측정
                var queryTimes = new List<long>();
                
                foreach (var query in testQueries)
                {
                    var queryStopwatch = Stopwatch.StartNew();
                    
                    var queryResults = await _spatialIndexManager.QueryIntersectingFeaturesAsync(index, query);
                    
                    queryStopwatch.Stop();
                    queryTimes.Add(queryStopwatch.ElapsedMilliseconds);
                    
                    result.TotalQueryResults += queryResults.Count;
                }

                // 질의 성능 통계 계산
                result.AvgQueryTimeMs = queryTimes.Average();
                result.MinQueryTimeMs = queryTimes.Min();
                result.MaxQueryTimeMs = queryTimes.Max();
                result.MedianQueryTimeMs = CalculateMedian(queryTimes);

                // 전체 점수 계산 (낮을수록 좋음)
                result.OverallScore = CalculateOverallScore(result);
                result.Success = true;

                _logger.LogDebug("인덱스 벤치마크 완료: {IndexType} - 구축: {BuildTime}ms, 평균 질의: {AvgQuery:F2}ms, 메모리: {Memory:F2}MB",
                    indexType, result.BuildTimeMs, result.AvgQueryTimeMs, result.MemoryUsageMB);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "인덱스 벤치마크 실패: {IndexType}", indexType);
            }

            return result;
        }

        /// <summary>
        /// 데이터 특성 분석
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>데이터 특성</returns>
        private async Task<DataCharacteristics> AnalyzeDataCharacteristicsAsync(string gdbPath, string layerName)
        {
            return await Task.Run(() =>
            {
                var characteristics = new DataCharacteristics { LayerName = layerName };

                try
                {
                    using var dataSource = OSGeo.OGR.Ogr.Open(gdbPath, 0);
                    if (dataSource == null)
                        return characteristics;

                    var layer = dataSource.GetLayerByName(layerName);
                    if (layer == null)
                        return characteristics;

                    // 기본 통계
                    characteristics.FeatureCount = layer.GetFeatureCount(1);
                    
                    var layerEnvelope = new OSGeo.OGR.Envelope();
                    layer.GetExtent(layerEnvelope, 1);
                    characteristics.Bounds = new SpatialEnvelope(
                        layerEnvelope.MinX, layerEnvelope.MinY, 
                        layerEnvelope.MaxX, layerEnvelope.MaxY);

                    // 샘플링을 통한 상세 분석
                    var sampleSize = Math.Min(1000, (int)characteristics.FeatureCount);
                    var areas = new List<double>();
                    var aspectRatios = new List<double>();

                    layer.ResetReading();
                    OSGeo.OGR.Feature feature;
                    int sampledCount = 0;

                    while ((feature = layer.GetNextFeature()) != null && sampledCount < sampleSize)
                    {
                        try
                        {
                            var geometry = feature.GetGeometryRef();
                            if (geometry != null)
                            {
                                var envelope = new OSGeo.OGR.Envelope();
                                geometry.GetEnvelope(envelope);
                                
                                var width = envelope.MaxX - envelope.MinX;
                                var height = envelope.MaxY - envelope.MinY;
                                var area = width * height;
                                
                                areas.Add(area);
                                
                                if (height > 0)
                                {
                                    aspectRatios.Add(width / height);
                                }
                                
                                sampledCount++;
                            }
                        }
                        finally
                        {
                            feature.Dispose();
                        }
                    }

                    // 통계 계산
                    if (areas.Count > 0)
                    {
                        characteristics.AvgFeatureArea = areas.Average();
                        characteristics.MinFeatureArea = areas.Min();
                        characteristics.MaxFeatureArea = areas.Max();
                        characteristics.FeatureAreaVariance = CalculateVariance(areas);
                    }

                    if (aspectRatios.Count > 0)
                    {
                        characteristics.AvgAspectRatio = aspectRatios.Average();
                        characteristics.AspectRatioVariance = CalculateVariance(aspectRatios);
                    }

                    // 데이터 분포 특성 계산
                    characteristics.DataDensity = characteristics.FeatureCount / (characteristics.Bounds.Width * characteristics.Bounds.Height);
                    characteristics.SpatialDistribution = CalculateSpatialDistribution(characteristics);

                    _logger.LogDebug("데이터 특성 분석 완료: {LayerName} - 피처 수: {FeatureCount}, 밀도: {Density:E2}, 평균 면적: {AvgArea:E2}",
                        layerName, characteristics.FeatureCount, characteristics.DataDensity, characteristics.AvgFeatureArea);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "데이터 특성 분석 중 오류 발생: {LayerName}", layerName);
                }

                return characteristics;
            });
        }

        /// <summary>
        /// 특성 기반 인덱스 타입 추천
        /// </summary>
        /// <param name="characteristics">데이터 특성</param>
        /// <returns>추천 인덱스 타입</returns>
        private SpatialIndexType RecommendIndexType(DataCharacteristics characteristics)
        {
            // 피처 수가 적은 경우
            if (characteristics.FeatureCount < 1000)
            {
                return SpatialIndexType.GridIndex; // 간단한 격자 인덱스
            }

            // 데이터 밀도가 높고 균등하게 분포된 경우
            if (characteristics.DataDensity > 1e-6 && characteristics.SpatialDistribution < 0.5)
            {
                return SpatialIndexType.GridIndex;
            }

            // 피처 크기 분산이 큰 경우 (다양한 크기의 객체)
            if (characteristics.FeatureAreaVariance > characteristics.AvgFeatureArea * 10)
            {
                return SpatialIndexType.RTree; // R-tree가 다양한 크기에 적합
            }

            // 데이터가 불균등하게 분포된 경우
            if (characteristics.SpatialDistribution > 0.7)
            {
                return SpatialIndexType.QuadTree; // QuadTree가 불균등 분포에 적합
            }

            // 기본적으로 R-tree 추천
            return SpatialIndexType.RTree;
        }

        /// <summary>
        /// 테스트 질의 생성
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>테스트 질의 목록</returns>
        private async Task<List<SpatialEnvelope>> GenerateTestQueriesAsync(string gdbPath, string layerName)
        {
            return await Task.Run(() =>
            {
                var queries = new List<SpatialEnvelope>();

                try
                {
                    using var dataSource = OSGeo.OGR.Ogr.Open(gdbPath, 0);
                    if (dataSource == null)
                        return queries;

                    var layer = dataSource.GetLayerByName(layerName);
                    if (layer == null)
                        return queries;

                    var layerEnvelope = new OSGeo.OGR.Envelope();
                    layer.GetExtent(layerEnvelope, 1);
                    
                    var bounds = new SpatialEnvelope(
                        layerEnvelope.MinX, layerEnvelope.MinY, 
                        layerEnvelope.MaxX, layerEnvelope.MaxY);

                    var random = new Random(42); // 재현 가능한 결과를 위해 시드 고정

                    // 다양한 크기의 질의 생성
                    var querySizes = new[] { 0.01, 0.05, 0.1, 0.2, 0.5 }; // 전체 영역 대비 비율

                    foreach (var sizeRatio in querySizes)
                    {
                        for (int i = 0; i < 10; i++) // 각 크기별로 10개씩 생성
                        {
                            var queryWidth = bounds.Width * sizeRatio;
                            var queryHeight = bounds.Height * sizeRatio;

                            var minX = bounds.MinX + random.NextDouble() * (bounds.Width - queryWidth);
                            var minY = bounds.MinY + random.NextDouble() * (bounds.Height - queryHeight);

                            queries.Add(new SpatialEnvelope(minX, minY, minX + queryWidth, minY + queryHeight));
                        }
                    }

                    _logger.LogDebug("테스트 질의 생성 완료: {QueryCount}개", queries.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "테스트 질의 생성 중 오류 발생: {LayerName}", layerName);
                }

                return queries;
            });
        }

        /// <summary>
        /// 전체 점수 계산 (낮을수록 좋음)
        /// </summary>
        /// <param name="result">벤치마크 결과</param>
        /// <returns>전체 점수</returns>
        private double CalculateOverallScore(IndexBenchmarkResult result)
        {
            // 가중치: 구축 시간 20%, 질의 시간 60%, 메모리 사용량 20%
            var normalizedBuildTime = result.BuildTimeMs / 1000.0; // 초 단위로 정규화
            var normalizedQueryTime = result.AvgQueryTimeMs;
            var normalizedMemory = result.MemoryUsageMB;

            return normalizedBuildTime * 0.2 + normalizedQueryTime * 0.6 + normalizedMemory * 0.2;
        }

        /// <summary>
        /// 중앙값 계산
        /// </summary>
        /// <param name="values">값 목록</param>
        /// <returns>중앙값</returns>
        private double CalculateMedian(List<long> values)
        {
            var sorted = values.OrderBy(x => x).ToList();
            int count = sorted.Count;
            
            if (count % 2 == 0)
            {
                return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
            }
            else
            {
                return sorted[count / 2];
            }
        }

        /// <summary>
        /// 분산 계산
        /// </summary>
        /// <param name="values">값 목록</param>
        /// <returns>분산</returns>
        private double CalculateVariance(List<double> values)
        {
            if (values.Count < 2)
                return 0;

            var mean = values.Average();
            return values.Select(x => Math.Pow(x - mean, 2)).Average();
        }

        /// <summary>
        /// 공간 분포 특성 계산
        /// </summary>
        /// <param name="characteristics">데이터 특성</param>
        /// <returns>분포 특성 (0: 균등, 1: 불균등)</returns>
        private double CalculateSpatialDistribution(DataCharacteristics characteristics)
        {
            // 간단한 분포 특성 계산 (실제로는 더 복잡한 알고리즘 필요)
            // 여기서는 면적 분산을 기반으로 추정
            if (characteristics.AvgFeatureArea == 0)
                return 0.5;

            var coefficientOfVariation = Math.Sqrt(characteristics.FeatureAreaVariance) / characteristics.AvgFeatureArea;
            return Math.Min(1.0, coefficientOfVariation / 2.0);
        }
    }

    /// <summary>
    /// 데이터 특성 정보
    /// </summary>
    public class DataCharacteristics
    {
        public string LayerName { get; set; } = string.Empty;
        public long FeatureCount { get; set; }
        public SpatialEnvelope Bounds { get; set; }
        public double AvgFeatureArea { get; set; }
        public double MinFeatureArea { get; set; }
        public double MaxFeatureArea { get; set; }
        public double FeatureAreaVariance { get; set; }
        public double AvgAspectRatio { get; set; }
        public double AspectRatioVariance { get; set; }
        public double DataDensity { get; set; }
        public double SpatialDistribution { get; set; }
    }

    /// <summary>
    /// 인덱스 벤치마크 결과
    /// </summary>
    public class IndexBenchmarkResult
    {
        public SpatialIndexType IndexType { get; set; }
        public string LayerName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        
        // 성능 지표
        public long BuildTimeMs { get; set; }
        public double AvgQueryTimeMs { get; set; }
        public double MinQueryTimeMs { get; set; }
        public double MaxQueryTimeMs { get; set; }
        public double MedianQueryTimeMs { get; set; }
        public double MemoryUsageMB { get; set; }
        
        // 기타 정보
        public int FeatureCount { get; set; }
        public int TestQueryCount { get; set; }
        public int TotalQueryResults { get; set; }
        public double OverallScore { get; set; }
    }
}
