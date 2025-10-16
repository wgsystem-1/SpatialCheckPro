using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 성능 벤치마크 테스트 서비스
    /// </summary>
    public class PerformanceBenchmarkService
    {
        private readonly ILogger<PerformanceBenchmarkService> _logger;
        private readonly ParallelPerformanceMonitor _performanceMonitor;
        private readonly SystemResourceAnalyzer _resourceAnalyzer;
        private readonly MemoryOptimizationService _memoryOptimization;

        public PerformanceBenchmarkService(
            ILogger<PerformanceBenchmarkService> logger,
            ParallelPerformanceMonitor performanceMonitor,
            SystemResourceAnalyzer resourceAnalyzer,
            MemoryOptimizationService memoryOptimization)
        {
            _logger = logger;
            _performanceMonitor = performanceMonitor;
            _resourceAnalyzer = resourceAnalyzer;
            _memoryOptimization = memoryOptimization;
        }

        /// <summary>
        /// 전체 성능 벤치마크 실행
        /// </summary>
        public async Task<BenchmarkResult> RunFullBenchmarkAsync(string gdbPath)
        {
            _logger.LogInformation("성능 벤치마크 시작: {GdbPath}", gdbPath);
            var startTime = DateTime.Now;

            var result = new BenchmarkResult
            {
                TestName = "전체 성능 벤치마크",
                StartTime = startTime,
                GdbPath = gdbPath
            };

            try
            {
                // 시스템 리소스 분석
                result.SystemInfo = await AnalyzeSystemResourcesAsync();

                // 메모리 사용량 벤치마크
                result.MemoryBenchmark = await RunMemoryBenchmarkAsync();

                // CPU 병렬 처리 벤치마크
                result.CpuBenchmark = await RunCpuBenchmarkAsync();

                // GDAL 성능 벤치마크
                result.GdalBenchmark = await RunGdalBenchmarkAsync(gdbPath);

                // 종합 성능 점수 계산
                result.OverallScore = CalculateOverallScore(result);

                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;

                _logger.LogInformation("성능 벤치마크 완료: {Duration:F1}초, 종합 점수: {Score:F1}", 
                    result.Duration.TotalSeconds, result.OverallScore);

                return result;
            }
            catch (Exception)
            {
                _logger.LogError("성능 벤치마크 실행 중 오류 발생");
                result.ErrorMessage = "성능 벤치마크 실행 중 오류 발생";
                result.EndTime = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// 시스템 리소스 분석
        /// </summary>
        private async Task<SystemResourceInfo> AnalyzeSystemResourcesAsync()
        {
            var operationId = $"system_analysis_{Guid.NewGuid():N}";
            _performanceMonitor.StartOperation(operationId, "시스템 리소스 분석", 1);

            try
            {
                var cpuCount = Environment.ProcessorCount;
                var memoryInfo = GC.GetTotalMemory(false);
                var totalMemoryGB = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);
                var availableMemoryGB = totalMemoryGB * 0.7; // 추정값
                var recommendedParallelism = Math.Max(1, cpuCount / 2);
                var recommendedBatchSize = 1000;

                var systemInfo = new SystemResourceInfo
                {
                    ProcessorCount = cpuCount,
                    TotalMemoryGB = totalMemoryGB,
                    AvailableMemoryGB = availableMemoryGB,
                    CurrentProcessMemoryMB = memoryInfo / (1024 * 1024),
                    RecommendedMaxParallelism = recommendedParallelism,
                    RecommendedBatchSize = recommendedBatchSize,
                    RecommendedMaxMemoryUsageMB = (int)(availableMemoryGB * 1024 * 0.8),
                    SystemLoadLevel = SystemLoadLevel.Medium
                };

                _performanceMonitor.UpdateProgress(operationId, 1, 1);
                _performanceMonitor.CompleteOperation(operationId);

                return systemInfo;
            }
            catch (Exception)
            {
                _performanceMonitor.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// 메모리 사용량 벤치마크
        /// </summary>
        private async Task<MemoryBenchmarkResult> RunMemoryBenchmarkAsync()
        {
            var operationId = $"memory_benchmark_{Guid.NewGuid():N}";
            _performanceMonitor.StartOperation(operationId, "메모리 벤치마크", 1000);

            try
            {
                var initialMemory = GC.GetTotalMemory(false);
                var results = new List<MemoryTestResult>();

                // 다양한 메모리 할당 패턴 테스트
                for (int i = 0; i < 10; i++)
                {
                    var testStart = DateTime.Now;
                    var testMemory = GC.GetTotalMemory(false);

                    // 대용량 배열 할당
                    var largeArray = new byte[1024 * 1024 * 10]; // 10MB
                    Array.Fill(largeArray, (byte)i);

                    // 가비지 컬렉션 강제 실행
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    var testEnd = DateTime.Now;
                    var finalMemory = GC.GetTotalMemory(false);

                    results.Add(new MemoryTestResult
                    {
                        TestIndex = i,
                        InitialMemoryMB = testMemory / (1024 * 1024),
                        FinalMemoryMB = finalMemory / (1024 * 1024),
                        AllocatedMB = 10,
                        Duration = testEnd - testStart,
                        GcCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2)
                    });

                    _performanceMonitor.UpdateProgress(operationId, (i + 1) * 100, 1000);
                    await Task.Delay(100); // 메모리 안정화 대기
                }

                var finalMemoryAfterTest = GC.GetTotalMemory(false);
                var memoryIncrease = finalMemoryAfterTest - initialMemory;

                var benchmarkResult = new MemoryBenchmarkResult
                {
                    InitialMemoryMB = initialMemory / (1024 * 1024),
                    FinalMemoryMB = finalMemoryAfterTest / (1024 * 1024),
                    MemoryIncreaseMB = memoryIncrease / (1024 * 1024),
                    TestResults = results,
                    AverageAllocationTime = results.Average(r => r.Duration.TotalMilliseconds),
                    GcEfficiency = CalculateGcEfficiency(results)
                };

                _performanceMonitor.CompleteOperation(operationId);
                return benchmarkResult;
            }
            catch (Exception)
            {
                _performanceMonitor.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// CPU 병렬 처리 벤치마크
        /// </summary>
        private async Task<CpuBenchmarkResult> RunCpuBenchmarkAsync()
        {
            var operationId = $"cpu_benchmark_{Guid.NewGuid():N}";
            var testSize = 1000000;
            _performanceMonitor.StartOperation(operationId, "CPU 병렬 처리 벤치마크", testSize);

            try
            {
                var results = new List<CpuTestResult>();

                // 순차 처리 테스트
                var sequentialStart = DateTime.Now;
                var sequentialResult = await RunSequentialComputationAsync(testSize);
                var sequentialEnd = DateTime.Now;

                results.Add(new CpuTestResult
                {
                    TestType = "순차 처리",
                    Duration = sequentialEnd - sequentialStart,
                    Result = sequentialResult,
                    ItemsPerSecond = testSize / (sequentialEnd - sequentialStart).TotalSeconds
                });

                // 병렬 처리 테스트 (다양한 병렬도)
                var parallelismLevels = new[] { 2, 4, 8, Environment.ProcessorCount };
                foreach (var parallelism in parallelismLevels)
                {
                    var parallelStart = DateTime.Now;
                    var parallelResult = await RunParallelComputationAsync(testSize, parallelism);
                    var parallelEnd = DateTime.Now;

                    results.Add(new CpuTestResult
                    {
                        TestType = $"병렬 처리 ({parallelism}개)",
                        Duration = parallelEnd - parallelStart,
                        Result = parallelResult,
                        ItemsPerSecond = testSize / (parallelEnd - parallelStart).TotalSeconds,
                        ParallelismLevel = parallelism
                    });

                    _performanceMonitor.UpdateProgress(operationId, testSize / parallelismLevels.Length * Array.IndexOf(parallelismLevels, parallelism) + 1, testSize);
                }

                var bestResult = results.OrderByDescending(r => r.ItemsPerSecond).First();
                var speedup = bestResult.ItemsPerSecond / results.First().ItemsPerSecond;

                var benchmarkResult = new CpuBenchmarkResult
                {
                    TestResults = results,
                    BestConfiguration = bestResult,
                    SpeedupFactor = speedup,
                    OptimalParallelism = bestResult.ParallelismLevel ?? 1
                };

                _performanceMonitor.CompleteOperation(operationId);
                return benchmarkResult;
            }
            catch (Exception)
            {
                _performanceMonitor.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// GDAL 성능 벤치마크
        /// </summary>
        private async Task<GdalBenchmarkResult> RunGdalBenchmarkAsync(string gdbPath)
        {
            var operationId = $"gdal_benchmark_{Guid.NewGuid():N}";
            _performanceMonitor.StartOperation(operationId, "GDAL 성능 벤치마크", 100);

            try
            {
                var results = new List<GdalTestResult>();

                // GDAL 초기화 시간 측정
                var initStart = DateTime.Now;
                OSGeo.GDAL.Gdal.AllRegister();
                OSGeo.OGR.Ogr.RegisterAll();
                var initEnd = DateTime.Now;

                results.Add(new GdalTestResult
                {
                    TestType = "GDAL 초기화",
                    Duration = initEnd - initStart,
                    Success = true
                });

                // DataSource 열기 시간 측정
                var openStart = DateTime.Now;
                var dataSource = OSGeo.OGR.Ogr.Open(gdbPath, 0);
                var openEnd = DateTime.Now;

                if (dataSource != null)
                {
                    results.Add(new GdalTestResult
                    {
                        TestType = "DataSource 열기",
                        Duration = openEnd - openStart,
                        Success = true
                    });

                    // 레이어 수 확인
                    var layerCount = dataSource.GetLayerCount();
                    results.Add(new GdalTestResult
                    {
                        TestType = "레이어 수 확인",
                        Duration = TimeSpan.Zero,
                        Success = true,
                        AdditionalInfo = $"레이어 수: {layerCount}"
                    });

                    // 각 레이어의 피처 수 확인
                    for (int i = 0; i < Math.Min(layerCount, 5); i++) // 최대 5개 레이어만 테스트
                    {
                        var layerStart = DateTime.Now;
                        var layer = dataSource.GetLayerByIndex(i);
                        var featureCount = layer?.GetFeatureCount(1) ?? 0;
                        var layerEnd = DateTime.Now;

                        results.Add(new GdalTestResult
                        {
                            TestType = $"레이어 {i} 피처 수 확인",
                            Duration = layerEnd - layerStart,
                            Success = true,
                            AdditionalInfo = $"피처 수: {featureCount}"
                        });

                        _performanceMonitor.UpdateProgress(operationId, i + 1, 100);
                    }

                    dataSource.Dispose();
                }

                var benchmarkResult = new GdalBenchmarkResult
                {
                    TestResults = results,
                    AverageOperationTime = results.Where(r => r.Success).Average(r => r.Duration.TotalMilliseconds),
                    SuccessRate = results.Count(r => r.Success) / (double)results.Count * 100
                };

                _performanceMonitor.CompleteOperation(operationId);
                return benchmarkResult;
            }
            catch (Exception)
            {
                _performanceMonitor.CompleteOperation(operationId, false);
                throw;
            }
        }

        /// <summary>
        /// 순차 계산 실행
        /// </summary>
        private async Task<double> RunSequentialComputationAsync(int count)
        {
            return await Task.Run(() =>
            {
                double result = 0;
                for (int i = 0; i < count; i++)
                {
                    result += Math.Sqrt(i * i + 1) * Math.Sin(i * 0.001);
                }
                return result;
            });
        }

        /// <summary>
        /// 병렬 계산 실행
        /// </summary>
        private async Task<double> RunParallelComputationAsync(int count, int parallelism)
        {
            return await Task.Run(() =>
            {
                var partitionSize = count / parallelism;
                var tasks = new Task<double>[parallelism];

                for (int i = 0; i < parallelism; i++)
                {
                    var start = i * partitionSize;
                    var end = i == parallelism - 1 ? count : (i + 1) * partitionSize;
                    
                    tasks[i] = Task.Run(() =>
                    {
                        double result = 0;
                        for (int j = start; j < end; j++)
                        {
                            result += Math.Sqrt(j * j + 1) * Math.Sin(j * 0.001);
                        }
                        return result;
                    });
                }

                Task.WaitAll(tasks);
                return tasks.Sum(t => t.Result);
            });
        }

        /// <summary>
        /// GC 효율성 계산
        /// </summary>
        private double CalculateGcEfficiency(List<MemoryTestResult> results)
        {
            if (!results.Any()) return 0;

            var totalAllocated = results.Sum(r => r.AllocatedMB);
            var totalGcCount = results.Sum(r => r.GcCount);
            
            return totalGcCount > 0 ? totalAllocated / totalGcCount : 0;
        }

        /// <summary>
        /// 종합 성능 점수 계산
        /// </summary>
        private double CalculateOverallScore(BenchmarkResult result)
        {
            var scores = new List<double>();

            // 시스템 리소스 점수 (0-100)
            if (result.SystemInfo != null)
            {
                var cpuScore = Math.Min(100, result.SystemInfo.ProcessorCount * 10);
                var memoryScore = Math.Min(100, result.SystemInfo.AvailableMemoryGB * 10);
                scores.Add((cpuScore + memoryScore) / 2);
            }

            // 메모리 벤치마크 점수 (0-100)
            if (result.MemoryBenchmark != null)
            {
                var memoryScore = Math.Max(0, 100 - result.MemoryBenchmark.MemoryIncreaseMB);
                var gcScore = Math.Min(100, result.MemoryBenchmark.GcEfficiency * 10);
                scores.Add((memoryScore + gcScore) / 2);
            }

            // CPU 벤치마크 점수 (0-100)
            if (result.CpuBenchmark != null)
            {
                var speedupScore = Math.Min(100, result.CpuBenchmark.SpeedupFactor * 20);
                var performanceScore = Math.Min(100, result.CpuBenchmark.BestConfiguration.ItemsPerSecond / 1000);
                scores.Add((speedupScore + performanceScore) / 2);
            }

            // GDAL 벤치마크 점수 (0-100)
            if (result.GdalBenchmark != null)
            {
                var gdalScore = result.GdalBenchmark.SuccessRate;
                var speedScore = Math.Max(0, 100 - result.GdalBenchmark.AverageOperationTime);
                scores.Add((gdalScore + speedScore) / 2);
            }

            return scores.Any() ? scores.Average() : 0;
        }

        /// <summary>
        /// 벤치마크 결과를 JSON으로 내보내기
        /// </summary>
        public async Task<string> ExportBenchmarkResultAsync(BenchmarkResult result, string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(result, options);
                await File.WriteAllTextAsync(filePath, json);

                _logger.LogInformation("벤치마크 결과 내보내기 완료: {FilePath}", filePath);
                return json;
            }
            catch (Exception)
            {
                _logger.LogError("벤치마크 결과 내보내기 실패: {FilePath}", filePath);
                throw;
            }
        }
    }

    /// <summary>
    /// 벤치마크 결과
    /// </summary>
    public class BenchmarkResult
    {
        public string TestName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string GdbPath { get; set; } = string.Empty;
        public SystemResourceInfo? SystemInfo { get; set; }
        public MemoryBenchmarkResult? MemoryBenchmark { get; set; }
        public CpuBenchmarkResult? CpuBenchmark { get; set; }
        public GdalBenchmarkResult? GdalBenchmark { get; set; }
        public double OverallScore { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 시스템 리소스 정보
    /// </summary>
    public class SystemResourceInfo
    {
        public int ProcessorCount { get; set; }
        public double AvailableMemoryGB { get; set; }
        public double TotalMemoryGB { get; set; }
        public long CurrentProcessMemoryMB { get; set; }
        public int RecommendedMaxParallelism { get; set; }
        public int RecommendedBatchSize { get; set; }
        public int RecommendedMaxMemoryUsageMB { get; set; }
        public SystemLoadLevel SystemLoadLevel { get; set; }
    }

    /// <summary>
    /// 메모리 벤치마크 결과
    /// </summary>
    public class MemoryBenchmarkResult
    {
        public long InitialMemoryMB { get; set; }
        public long FinalMemoryMB { get; set; }
        public long MemoryIncreaseMB { get; set; }
        public List<MemoryTestResult> TestResults { get; set; } = new();
        public double AverageAllocationTime { get; set; }
        public double GcEfficiency { get; set; }
    }

    /// <summary>
    /// 메모리 테스트 결과
    /// </summary>
    public class MemoryTestResult
    {
        public int TestIndex { get; set; }
        public long InitialMemoryMB { get; set; }
        public long FinalMemoryMB { get; set; }
        public int AllocatedMB { get; set; }
        public TimeSpan Duration { get; set; }
        public int GcCount { get; set; }
    }

    /// <summary>
    /// CPU 벤치마크 결과
    /// </summary>
    public class CpuBenchmarkResult
    {
        public List<CpuTestResult> TestResults { get; set; } = new();
        public CpuTestResult BestConfiguration { get; set; } = new();
        public double SpeedupFactor { get; set; }
        public int OptimalParallelism { get; set; }
    }

    /// <summary>
    /// CPU 테스트 결과
    /// </summary>
    public class CpuTestResult
    {
        public string TestType { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public double Result { get; set; }
        public double ItemsPerSecond { get; set; }
        public int? ParallelismLevel { get; set; }
    }

    /// <summary>
    /// GDAL 벤치마크 결과
    /// </summary>
    public class GdalBenchmarkResult
    {
        public List<GdalTestResult> TestResults { get; set; } = new();
        public double AverageOperationTime { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// GDAL 테스트 결과
    /// </summary>
    public class GdalTestResult
    {
        public string TestType { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? AdditionalInfo { get; set; }
    }

    /// <summary>
    /// 시스템 부하 수준
    /// </summary>
    public enum SystemLoadLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}
