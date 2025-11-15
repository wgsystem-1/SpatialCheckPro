using System.Collections.Generic;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 성능 설정
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// 배치 크기 (기본값: 10,000)
        /// </summary>
        public int BatchSize { get; set; } = 10000;

        /// <summary>
        /// 최대 메모리 사용량 (MB, 기본값: 1024MB)
        /// </summary>
        public int MaxMemoryUsageMB { get; set; } = 1024;

        /// <summary>
        /// 병렬 처리 활성화 여부
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;

        /// <summary>
        /// 최대 병렬도 (기본값: CPU 코어 수)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 연결 타임아웃 (초, 기본값: 30초)
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 쿼리 타임아웃 (초, 기본값: 300초)
        /// </summary>
        public int QueryTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// 재시도 횟수 (기본값: 3회)
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 재시도 간격 (초, 기본값: 5초)
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 캐시 활성화 여부
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 캐시 만료 시간 (분, 기본값: 60분)
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 60;

        /// <summary>
        /// 진행률 보고 간격 (레코드 수, 기본값: 1000)
        /// </summary>
        public int ProgressReportInterval { get; set; } = 1000;

        /// <summary>
        /// 가비지 컬렉션 강제 실행 간격 (레코드 수, 기본값: 50000)
        /// </summary>
        public int GCForceInterval { get; set; } = 50000;

        /// <summary>
        /// 테이블별 병렬 처리 활성화 여부
        /// </summary>
        public bool EnableTableParallelProcessing { get; set; } = true;

        /// <summary>
        /// 단계별 병렬 처리 활성화 여부
        /// </summary>
        public bool EnableStageParallelProcessing { get; set; } = false;

        /// <summary>
        /// 메모리 최적화 모드 활성화 여부
        /// </summary>
        public bool EnableMemoryOptimization { get; set; } = true;

        /// <summary>
        /// 스트리밍 모드 활성화 여부 (대용량 데이터용)
        /// </summary>
        public bool EnableStreamingMode { get; set; } = false;

        /// <summary>
        /// 스트리밍 배치 크기 (스트리밍 모드에서 사용)
        /// </summary>
        public int StreamingBatchSize { get; set; } = 1000;

        /// <summary>
        /// CPU 사용률 제한 (%)
        /// </summary>
        public int CpuUsageLimitPercent { get; set; } = 80;

        /// <summary>
        /// 메모리 사용률 제한 (%)
        /// </summary>
        public int MemoryUsageLimitPercent { get; set; } = 80;

        /// <summary>
        /// 동적 병렬도 조정 활성화 여부
        /// </summary>
        public bool EnableDynamicParallelismAdjustment { get; set; } = true;

        /// <summary>
        /// 리소스 모니터링 간격 (초)
        /// </summary>
        public int ResourceMonitoringIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// 시스템 부하가 높을 때 자동으로 병렬도 감소 여부
        /// </summary>
        public bool EnableAutomaticLoadBalancing { get; set; } = true;

        /// <summary>
        /// 최소 병렬도 (시스템 부하가 높을 때)
        /// </summary>
        public int MinDegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// 최대 병렬도 (시스템 부하가 낮을 때)
        /// </summary>
        public int MaxDegreeOfParallelismLimit { get; set; } = Environment.ProcessorCount * 2;

        /// <summary>
        /// 메모리 압박 시 자동 GC 실행 여부
        /// </summary>
        public bool EnableAutomaticGarbageCollection { get; set; } = true;

        /// <summary>
        /// 메모리 압박 임계값 (MB)
        /// </summary>
        public int MemoryPressureThresholdMB { get; set; } = 2048;

        /// <summary>
        /// 파일별 병렬 처리 활성화 여부 (배치 검수용)
        /// </summary>
        public bool EnableFileParallelProcessing { get; set; } = false;

        /// <summary>
        /// 규칙별 병렬 처리 활성화 여부
        /// </summary>
        public bool EnableRuleParallelProcessing { get; set; } = true;

        /// <summary>
        /// 하이브리드 병렬처리 모드 활성화 여부
        /// </summary>
        public bool EnableHybridParallelProcessing { get; set; } = true;

        /// <summary>
        /// 동적 병렬처리 레벨 조정 활성화 여부
        /// </summary>
        public bool EnableDynamicLevelAdjustment { get; set; } = true;

        /// <summary>
        /// GDAL 리소스 풀 크기
        /// </summary>
        public int GdalResourcePoolSize { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 데이터베이스 연결 풀 크기
        /// </summary>
        public int DatabaseConnectionPoolSize { get; set; } = Environment.ProcessorCount * 2;

        /// <summary>
        /// 자동 고성능 모드 활성화 여부
        /// </summary>
        public bool EnableAutoHighPerformanceMode { get; set; } = true;

        /// <summary>
        /// 고성능 모드 자동 전환을 위한 파일 크기 임계값(바이트)
        /// 기본값: 2GB (appsettings.json과 일치)
        /// </summary>
        public long HighPerformanceModeSizeThresholdBytes { get; set; } = 1_932_735_283L; // 약 1.8GB

        /// <summary>
        /// 고성능 모드 자동 전환을 위한 총 피처 수 임계값
        /// 기본값: 500,000
        /// </summary>
        public long HighPerformanceModeFeatureThreshold { get; set; } = 400_000L;

        /// <summary>
        /// 스트리밍 모드 강제 사용 (파일 크기와 무관하게)
        /// </summary>
        public bool ForceStreamingMode { get; set; } = false;

        /// <summary>
        /// 사용자 지정 배치 크기
        /// </summary>
        public int CustomBatchSize { get; set; } = 1000;

        /// <summary>
        /// 프리페칭 활성화 (다음 배치를 미리 로드)
        /// </summary>
        public bool EnablePrefetching { get; set; } = false;

        /// <summary>
        /// 병렬 스트리밍 활성화 (단일 파일 내 다중 스레드 처리)
        /// </summary>
        public bool EnableParallelStreaming { get; set; } = false;

        /// <summary>
        /// 객체변동 구분 코드 중 검수에서 제외할 코드 목록 (OBJFLTN_SE 기준)
        /// </summary>
        public List<string> ExcludedObjectChangeCodes { get; set; } = new() { "OFJ008" };
    }
}
