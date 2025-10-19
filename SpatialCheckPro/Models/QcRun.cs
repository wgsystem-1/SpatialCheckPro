using System;
using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// QC_Runs 테이블의 검수 실행 이력을 나타내는 모델
    /// </summary>
    public class QcRun
    {
        /// <summary>고유 식별자 (GUID, 자동 생성)</summary>
        public string GlobalID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>검수 실행 ID (호환성을 위해 읽기/쓰기 가능)</summary>
        public string RunId 
        { 
            get => GlobalID; 
            set => GlobalID = value; 
        }

        /// <summary>검수 시작 시간 (호환성을 위해 읽기/쓰기 가능)</summary>
        public DateTime StartTime 
        { 
            get => StartTimeUTC; 
            set => StartTimeUTC = value; 
        }

        /// <summary>검수 완료 시간 (호환성을 위해 읽기/쓰기 가능)</summary>
        public DateTime? EndTime 
        { 
            get => EndTimeUTC; 
            set => EndTimeUTC = value; 
        }

        /// <summary>총 오류 개수 (호환성을 위해 읽기/쓰기 가능)</summary>
        public int ErrorCount 
        { 
            get => TotalErrors; 
            set => TotalErrors = value; 
        }

        /// <summary>검수 실행 이름</summary>
        [Required]
        [StringLength(256)]
        public string RunName { get; set; } = string.Empty;

        /// <summary>검수 대상 파일 경로</summary>
        [Required]
        [StringLength(512)]
        public string TargetFilePath { get; set; } = string.Empty;

        /// <summary>규칙셋 버전</summary>
        [StringLength(32)]
        public string? RulesetVersion { get; set; }

        /// <summary>검수 시작 시간 (UTC)</summary>
        public DateTime StartTimeUTC { get; set; } = DateTime.UtcNow;

        /// <summary>검수 완료 시간 (UTC)</summary>
        public DateTime? EndTimeUTC { get; set; }

        /// <summary>검수 실행자</summary>
        [StringLength(64)]
        public string? ExecutedBy { get; set; }

        /// <summary>검수 상태 (RUNNING, COMPLETED, FAILED, CANCELLED)</summary>
        [Required]
        [StringLength(16)]
        public string Status { get; set; } = "RUNNING";

        /// <summary>총 오류 개수</summary>
        public int TotalErrors { get; set; }

        /// <summary>총 경고 개수</summary>
        public int TotalWarnings { get; set; }

        /// <summary>검수 결과 요약 (JSON 형태)</summary>
        [StringLength(4096)]
        public string? ResultSummary { get; set; }

        /// <summary>검수 설정 정보 (JSON 형태)</summary>
        [StringLength(2048)]
        public string? ConfigInfo { get; set; }

        /// <summary>생성 시간 (UTC)</summary>
        public DateTime CreatedUTC { get; set; } = DateTime.UtcNow;

        /// <summary>수정 시간 (UTC)</summary>
        public DateTime UpdatedUTC { get; set; } = DateTime.UtcNow;
    }
}
