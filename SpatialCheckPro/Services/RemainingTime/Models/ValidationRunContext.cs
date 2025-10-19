#nullable enable
using System.Collections.Generic;

namespace SpatialCheckPro.Services.RemainingTime.Models
{
    /// <summary>
    /// 단일 검수 실행에 대한 컨텍스트 정보
    /// </summary>
    public class ValidationRunContext
    {
        /// <summary>
        /// 대상 파일 경로
        /// </summary>
        public string TargetFilePath { get; init; } = string.Empty;

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSizeBytes { get; init; }

        /// <summary>
        /// 전체 피처 수 (알 수 없으면 -1)
        /// </summary>
        public long FeatureCount { get; init; } = -1;

        /// <summary>
        /// 레이어 수
        /// </summary>
        public int LayerCount { get; init; }

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string? CoordinateSystem { get; init; }

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    }
}


