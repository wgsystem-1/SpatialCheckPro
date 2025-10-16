using System;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 렌더링 설정 클래스
    /// </summary>
    public class RenderingSettings
    {
        /// <summary>
        /// 렌더링 품질 (1-5, 높을수록 고품질)
        /// </summary>
        public int Quality { get; set; } = 3;

        /// <summary>
        /// 안티앨리어싱 사용 여부
        /// </summary>
        public bool AntiAliasing { get; set; } = true;

        /// <summary>
        /// 최대 렌더링 오류 개수
        /// </summary>
        public int MaxRenderingCount { get; set; } = 10000;

        /// <summary>
        /// 클러스터링 임계값 (미터)
        /// </summary>
        public double ClusteringThreshold { get; set; } = 50.0;

        /// <summary>
        /// 줌 레벨 기반 크기 조정 사용 여부
        /// </summary>
        public bool ZoomBasedSizing { get; set; } = true;

        /// <summary>
        /// 뷰포트 기반 필터링 사용 여부
        /// </summary>
        public bool ViewportFiltering { get; set; } = true;

        /// <summary>
        /// 심볼 캐싱 사용 여부
        /// </summary>
        public bool SymbolCaching { get; set; } = true;

        /// <summary>
        /// 최대 캐시 크기
        /// </summary>
        public int MaxCacheSize { get; set; } = 1000;

        /// <summary>
        /// 렌더링 스레드 개수
        /// </summary>
        public int RenderingThreads { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 프로그레시브 렌더링 사용 여부
        /// </summary>
        public bool ProgressiveRendering { get; set; } = true;

        /// <summary>
        /// 렌더링 배치 크기
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// 기본 설정으로 초기화
        /// </summary>
        public static RenderingSettings Default => new RenderingSettings();

        /// <summary>
        /// 고성능 설정
        /// </summary>
        public static RenderingSettings HighPerformance => new RenderingSettings
        {
            Quality = 2,
            AntiAliasing = false,
            MaxRenderingCount = 5000,
            ClusteringThreshold = 100.0,
            ZoomBasedSizing = true,
            ViewportFiltering = true,
            SymbolCaching = true,
            MaxCacheSize = 500,
            ProgressiveRendering = true,
            BatchSize = 200
        };

        /// <summary>
        /// 고품질 설정
        /// </summary>
        public static RenderingSettings HighQuality => new RenderingSettings
        {
            Quality = 5,
            AntiAliasing = true,
            MaxRenderingCount = 20000,
            ClusteringThreshold = 25.0,
            ZoomBasedSizing = true,
            ViewportFiltering = true,
            SymbolCaching = true,
            MaxCacheSize = 2000,
            ProgressiveRendering = false,
            BatchSize = 50
        };

        /// <summary>
        /// 설정 복사본 생성
        /// </summary>
        /// <returns>복사된 설정</returns>
        public RenderingSettings Clone()
        {
            return new RenderingSettings
            {
                Quality = this.Quality,
                AntiAliasing = this.AntiAliasing,
                MaxRenderingCount = this.MaxRenderingCount,
                ClusteringThreshold = this.ClusteringThreshold,
                ZoomBasedSizing = this.ZoomBasedSizing,
                ViewportFiltering = this.ViewportFiltering,
                SymbolCaching = this.SymbolCaching,
                MaxCacheSize = this.MaxCacheSize,
                RenderingThreads = this.RenderingThreads,
                ProgressiveRendering = this.ProgressiveRendering,
                BatchSize = this.BatchSize
            };
        }

        /// <summary>
        /// 설정 유효성 검사
        /// </summary>
        /// <returns>유효성 검사 결과</returns>
        public bool IsValid()
        {
            return Quality >= 1 && Quality <= 5 &&
                   MaxRenderingCount > 0 &&
                   ClusteringThreshold > 0 &&
                   MaxCacheSize > 0 &&
                   RenderingThreads > 0 &&
                   BatchSize > 0;
        }

        /// <summary>
        /// 설정을 안전한 범위로 정규화
        /// </summary>
        public void Normalize()
        {
            Quality = Math.Max(1, Math.Min(5, Quality));
            MaxRenderingCount = Math.Max(100, MaxRenderingCount);
            ClusteringThreshold = Math.Max(1.0, ClusteringThreshold);
            MaxCacheSize = Math.Max(10, MaxCacheSize);
            RenderingThreads = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, RenderingThreads));
            BatchSize = Math.Max(10, Math.Min(1000, BatchSize));
        }
    }

    /// <summary>
    /// 렌더링 통계 클래스
    /// </summary>
    public class RenderingStatistics
    {
        /// <summary>
        /// 마지막 렌더링 시간 (밀리초)
        /// </summary>
        public long LastRenderingTimeMs { get; set; }

        /// <summary>
        /// 평균 렌더링 시간 (밀리초)
        /// </summary>
        public double AverageRenderingTimeMs { get; set; }

        /// <summary>
        /// 렌더링된 오류 개수
        /// </summary>
        public int RenderedErrorCount { get; set; }

        /// <summary>
        /// 렌더링된 클러스터 개수
        /// </summary>
        public int RenderedClusterCount { get; set; }

        /// <summary>
        /// 캐시 히트 비율 (0.0 ~ 1.0)
        /// </summary>
        public double CacheHitRatio { get; set; }

        /// <summary>
        /// 메모리 사용량 (바이트)
        /// </summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>
        /// 총 렌더링 횟수
        /// </summary>
        public int TotalRenderingCount { get; set; }

        /// <summary>
        /// 실패한 렌더링 횟수
        /// </summary>
        public int FailedRenderingCount { get; set; }

        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 성공률 (0.0 ~ 1.0)
        /// </summary>
        public double SuccessRate
        {
            get
            {
                if (TotalRenderingCount == 0) return 1.0;
                return (double)(TotalRenderingCount - FailedRenderingCount) / TotalRenderingCount;
            }
        }

        /// <summary>
        /// 초당 렌더링 오류 개수
        /// </summary>
        public double ErrorsPerSecond
        {
            get
            {
                if (LastRenderingTimeMs == 0) return 0;
                return RenderedErrorCount / (LastRenderingTimeMs / 1000.0);
            }
        }

        /// <summary>
        /// 메모리 사용량 (MB)
        /// </summary>
        public double MemoryUsageMB => MemoryUsageBytes / (1024.0 * 1024.0);

        /// <summary>
        /// 통계 초기화
        /// </summary>
        public void Reset()
        {
            LastRenderingTimeMs = 0;
            AverageRenderingTimeMs = 0;
            RenderedErrorCount = 0;
            RenderedClusterCount = 0;
            CacheHitRatio = 0;
            MemoryUsageBytes = 0;
            TotalRenderingCount = 0;
            FailedRenderingCount = 0;
            LastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// 문자열 표현
        /// </summary>
        /// <returns>통계 정보 문자열</returns>
        public override string ToString()
        {
            return $"RenderingStats: {RenderedErrorCount}개 오류, {LastRenderingTimeMs}ms, " +
                   $"성공률 {SuccessRate:P1}, 메모리 {MemoryUsageMB:F1}MB";
        }
    }
}