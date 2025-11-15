namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 파일 분석 결과
    /// </summary>
    public class FileAnalysisResult
    {
        /// <summary>
        /// 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 파일 타입
        /// </summary>
        public FileType FileType { get; set; }

        /// <summary>
        /// 대용량 파일 여부
        /// </summary>
        public bool IsLargeFile { get; set; }

        /// <summary>
        /// 추천 처리 모드
        /// </summary>
        public ProcessingMode RecommendedProcessingMode { get; set; }

        /// <summary>
        /// 예상 메모리 사용량 (바이트)
        /// </summary>
        public long EstimatedMemoryUsage { get; set; }

        /// <summary>
        /// 추천 청크 크기 (바이트)
        /// </summary>
        public int RecommendedChunkSize { get; set; }

        /// <summary>
        /// 예상 피처 개수
        /// </summary>
        public long EstimatedFeatureCount { get; set; }

        /// <summary>
        /// 처리 우선순위 (높을수록 우선)
        /// </summary>
        public int ProcessingPriority { get; set; }
    }

    /// <summary>
    /// 파일 타입 열거형
    /// </summary>
    public enum FileType
    {
        /// <summary>
        /// 알 수 없는 파일 타입
        /// </summary>
        Unknown,

        /// <summary>
        /// File Geodatabase
        /// </summary>
        FileGDB,

        /// <summary>
        /// Shapefile
        /// </summary>
        Shapefile,

        /// <summary>
        /// GeoPackage
        /// </summary>
        GeoPackage
    }

    /// <summary>
    /// 처리 모드 열거형
    /// </summary>
    public enum ProcessingMode
    {
        /// <summary>
        /// 표준 처리 (모든 데이터를 메모리에 로드)
        /// </summary>
        Standard,

        /// <summary>
        /// 스트리밍 처리 (청크 단위로 처리)
        /// </summary>
        Streaming,

        /// <summary>
        /// 병렬 스트리밍 처리 (청크 단위 + 병렬 처리)
        /// </summary>
        ParallelStreaming
    }
}
