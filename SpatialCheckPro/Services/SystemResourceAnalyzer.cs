using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 시스템 리소스 분석 및 최적화 설정 서비스
    /// </summary>
    public class SystemResourceAnalyzer
    {
        private readonly ILogger<SystemResourceAnalyzer> _logger;
        private SystemResourceInfo? _cachedResourceInfo;
        private DateTime _lastAnalysisTime = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromMinutes(5); // 5분간 캐시 유효

        public SystemResourceAnalyzer(ILogger<SystemResourceAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 시스템 리소스 정보 분석
        /// </summary>
        public SystemResourceInfo AnalyzeSystemResources()
        {
            try
            {
                // 캐시된 정보가 유효한 경우 반환
                if (_cachedResourceInfo != null && 
                    DateTime.Now - _lastAnalysisTime < _cacheValidityPeriod)
                {
                    _logger.LogDebug("캐시된 리소스 정보 반환 (마지막 분석: {LastAnalysis})", _lastAnalysisTime);
                    return _cachedResourceInfo;
                }

                _logger.LogDebug("새로운 리소스 분석 시작");
                
                var cpuUsage = GetCpuUsage();
                var memoryUsage = GetMemoryUsage();
                var availableMemoryGB = GetAvailableMemoryGB();
                var totalMemoryGB = GetTotalMemoryGB();

                var info = new SystemResourceInfo
                {
                    ProcessorCount = Environment.ProcessorCount,
                    AvailableMemoryGB = availableMemoryGB,
                    TotalMemoryGB = totalMemoryGB,
                    CurrentProcessMemoryMB = GetCurrentProcessMemoryMB(),
                    RecommendedMaxParallelism = CalculateRecommendedMaxParallelism(),
                    RecommendedBatchSize = CalculateRecommendedBatchSize(),
                    RecommendedMaxMemoryUsageMB = CalculateRecommendedMaxMemoryUsageMB(),
                    SystemLoadLevel = GetSystemLoadLevel(),
                    CpuUsagePercent = cpuUsage,
                    MemoryPressureRatio = memoryUsage / 100.0 // Convert percentage to ratio
                };

                // 캐시 업데이트
                _cachedResourceInfo = info;
                _lastAnalysisTime = DateTime.Now;

                _logger.LogDebug("시스템 리소스 분석 완료: CPU {CpuCount}개, RAM {TotalMemoryGB}GB, 권장 병렬도 {MaxParallelism}", 
                    info.ProcessorCount, info.TotalMemoryGB, info.RecommendedMaxParallelism);

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "시스템 리소스 분석 실패");
                return GetDefaultSystemResourceInfo();
            }
        }

        /// <summary>
        /// 사용 가능한 메모리 (GB)
        /// </summary>
        private double GetAvailableMemoryGB()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var availableMemory = GC.GetTotalMemory(false);
                
                // 시스템 전체 사용 가능 메모리 추정
                var totalPhysicalMemory = GetTotalPhysicalMemory();
                var usedMemory = GetUsedMemory();
                var availableMemoryBytes = totalPhysicalMemory - usedMemory;
                
                return Math.Max(0, availableMemoryBytes / (1024.0 * 1024.0 * 1024.0));
            }
            catch
            {
                return 4.0; // 기본값
            }
        }

        /// <summary>
        /// 총 메모리 (GB)
        /// </summary>
        private double GetTotalMemoryGB()
        {
            try
            {
                var totalMemory = GetTotalPhysicalMemory();
                return totalMemory / (1024.0 * 1024.0 * 1024.0);
            }
            catch
            {
                return 8.0; // 기본값
            }
        }

        /// <summary>
        /// 현재 프로세스 메모리 사용량 (MB)
        /// </summary>
        private long GetCurrentProcessMemoryMB()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 100; // 기본값
            }
        }

        /// <summary>
        /// 권장 최대 병렬도 계산
        /// </summary>
        private int CalculateRecommendedMaxParallelism()
        {
            var cpuCount = Environment.ProcessorCount;
            var availableMemoryGB = GetAvailableMemoryGB();
            var totalMemoryGB = GetTotalMemoryGB();
            
            // CPU 기반 계산: 코어 수의 75% 사용 (시스템 안정성 고려)
            var cpuBasedLimit = Math.Max(1, (int)(cpuCount * 0.75));
            
            // 메모리 기반 계산: 사용 가능한 메모리의 1/4을 사용 (4GB당 1개 코어)
            var memoryBasedLimit = Math.Max(1, (int)(availableMemoryGB / 4));
            
            // 총 메모리 기반 계산: 총 메모리의 1/8을 사용 (8GB당 1개 코어)
            var totalMemoryBasedLimit = Math.Max(1, (int)(totalMemoryGB / 8));
            
            // 가장 제한적인 값을 선택하되, 최소 1개는 보장
            var recommended = Math.Min(cpuBasedLimit, Math.Min(memoryBasedLimit, totalMemoryBasedLimit));
            recommended = Math.Max(1, Math.Min(recommended, cpuCount)); // CPU 코어 수를 초과하지 않음
            
            _logger.LogDebug("병렬도 계산: CPU {CpuCount}개, 사용가능메모리 {AvailableGB}GB, 총메모리 {TotalGB}GB → 권장 {Recommended}개", 
                cpuCount, availableMemoryGB, totalMemoryGB, recommended);
            
            return recommended;
        }

        /// <summary>
        /// 권장 배치 크기 계산
        /// </summary>
        private int CalculateRecommendedBatchSize()
        {
            var availableMemoryGB = GetAvailableMemoryGB();
            var cpuCount = Environment.ProcessorCount;
            
            // 메모리와 CPU를 고려한 배치 크기
            var memoryBasedBatch = (int)(availableMemoryGB * 1000); // 1GB당 1000개
            var cpuBasedBatch = cpuCount * 500; // CPU당 500개
            
            var recommended = Math.Min(memoryBasedBatch, cpuBasedBatch);
            recommended = Math.Max(100, Math.Min(recommended, 10000)); // 100~10000 범위
            
            _logger.LogDebug("배치 크기 계산: 메모리 {MemoryGB}GB, CPU {CpuCount}개 → 권장 {Recommended}개", 
                availableMemoryGB, cpuCount, recommended);
            
            return recommended;
        }

        /// <summary>
        /// 권장 최대 메모리 사용량 (MB)
        /// </summary>
        private int CalculateRecommendedMaxMemoryUsageMB()
        {
            var totalMemoryGB = GetTotalMemoryGB();
            var availableMemoryGB = GetAvailableMemoryGB();
            
            // 사용 가능한 메모리의 70% 사용 (시스템 안정성 고려)
            var recommended = (int)(availableMemoryGB * 0.7 * 1024);
            recommended = Math.Max(512, Math.Min(recommended, (int)(totalMemoryGB * 0.8 * 1024))); // 512MB~총메모리의 80%
            
            _logger.LogDebug("최대 메모리 사용량 계산: 총 {TotalGB}GB, 사용가능 {AvailableGB}GB → 권장 {Recommended}MB", 
                totalMemoryGB, availableMemoryGB, recommended);
            
            return recommended;
        }

        /// <summary>
        /// 시스템 부하 수준 분석
        /// </summary>
        private SystemLoadLevel GetSystemLoadLevel()
        {
            try
            {
                var cpuUsage = GetCpuUsage();
                var memoryUsage = GetMemoryUsage();
                
                if (cpuUsage > 80 || memoryUsage > 80)
                    return SystemLoadLevel.High;
                else if (cpuUsage > 60 || memoryUsage > 60)
                    return SystemLoadLevel.Medium;
                else
                    return SystemLoadLevel.Low;
            }
            catch
            {
                return SystemLoadLevel.Medium;
            }
        }

        /// <summary>
        /// CPU 사용률 (%) - 간단한 추정
        /// </summary>
        private double GetCpuUsage()
        {
            try
            {
                // 간단한 CPU 사용률 추정 (실제 구현은 더 복잡할 수 있음)
                return 30.0; // 기본값
            }
            catch
            {
                return 30.0;
            }
        }

        /// <summary>
        /// 메모리 사용률 (%)
        /// </summary>
        private double GetMemoryUsage()
        {
            try
            {
                var totalMemory = GetTotalPhysicalMemory();
                var usedMemory = GetUsedMemory();
                return (usedMemory / (double)totalMemory) * 100;
            }
            catch
            {
                return 50.0; // 기본값
            }
        }

        /// <summary>
        /// 총 물리 메모리 (바이트)
        /// </summary>
        private long GetTotalPhysicalMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows에서 실제 메모리 정보 가져오기
                    var memStatus = new MEMORYSTATUSEX();
                    memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    
                    if (WindowsAPI.GlobalMemoryStatusEx(ref memStatus))
                    {
                        return (long)memStatus.ullTotalPhys;
                    }
                }
                
                // 대체 방법: GC.GetTotalMemory로 추정
                var totalMemory = GC.GetTotalMemory(false);
                return Math.Max(totalMemory * 100, 8L * 1024 * 1024 * 1024); // 최소 8GB
            }
            catch
            {
                return 8L * 1024 * 1024 * 1024; // 8GB 기본값
            }
        }

        /// <summary>
        /// 사용 중인 메모리 (바이트)
        /// </summary>
        private long GetUsedMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows에서 실제 메모리 사용량 가져오기
                    var memStatus = new MEMORYSTATUSEX();
                    memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    
                    if (WindowsAPI.GlobalMemoryStatusEx(ref memStatus))
                    {
                        return (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
                    }
                }
                
                // 대체 방법: 프로세스 메모리로 추정
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                return workingSet * 10; // 프로세스 메모리의 10배로 추정
            }
            catch
            {
                return 4L * 1024 * 1024 * 1024; // 4GB 기본값
            }
        }

        /// <summary>
        /// 기본 시스템 리소스 정보
        /// </summary>
        private SystemResourceInfo GetDefaultSystemResourceInfo()
        {
            return new SystemResourceInfo
            {
                ProcessorCount = Environment.ProcessorCount,
                AvailableMemoryGB = 4.0,
                TotalMemoryGB = 8.0,
                CurrentProcessMemoryMB = 100,
                RecommendedMaxParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                RecommendedBatchSize = 1000,
                RecommendedMaxMemoryUsageMB = 1024,
                SystemLoadLevel = SystemLoadLevel.Medium,
                CpuUsagePercent = 30.0,
                MemoryPressureRatio = 0.5
            };
        }
    }



    /// <summary>
    /// 시스템 부하 수준
    /// </summary>
    public enum SystemLoadLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// 시스템 리소스 분석 결과 모델
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
        public double CpuUsagePercent { get; set; }
        public double MemoryPressureRatio { get; set; }
    }

    /// <summary>
    /// Windows 메모리 상태 구조체
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullExtendedVirtual;
    }

    /// <summary>
    /// Windows API 함수들
    /// </summary>
    public static class WindowsAPI
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
