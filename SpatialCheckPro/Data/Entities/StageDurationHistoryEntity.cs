#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Data.Entities
{
    /// <summary>
    /// 단계별 소요 시간 이력 엔티티
    /// </summary>
    [Table("StageDurationHistory")]
    public class StageDurationHistoryEntity
    {
        /// <summary>
        /// 기본 키
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 단계 식별자
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string StageId { get; set; } = string.Empty;

        /// <summary>
        /// 단계 번호
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 단계 이름
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 단계 상태
        /// </summary>
        public StageStatus Status { get; set; }

        /// <summary>
        /// 소요 시간(초)
        /// </summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// 전체 단위 수
        /// </summary>
        public long TotalUnits { get; set; } = -1;

        /// <summary>
        /// 처리된 피처 수
        /// </summary>
        public long FeatureCount { get; set; } = -1;

        /// <summary>
        /// 파일 바이트 크기
        /// </summary>
        public long FileSizeBytes { get; set; } = -1;

        /// <summary>
        /// 좌표계 EPSG 코드 (알 수 없으면 null)
        /// </summary>
        [MaxLength(32)]
        public string? CoordinateSystem { get; set; }

        /// <summary>
        /// 수집 시각 (UTC)
        /// </summary>
        public DateTime CollectedAtUtc { get; set; }

        /// <summary>
        /// 대상 파일 해시 (동일 파일 판별)
        /// </summary>
        [MaxLength(128)]
        public string? FileHash { get; set; }

        /// <summary>
        /// 추가 메타데이터(JSON)
        /// </summary>
        public string? MetadataJson { get; set; }
    }
}


