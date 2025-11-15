using System;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 검수 단계별 실제 소요 시간 데이터
    /// </summary>
    public class StageTimingData
    {
        /// <summary>
        /// 단계 번호 (0-5)
        /// </summary>
        public int StageNumber { get; set; }
        
        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;
        
        /// <summary>
        /// 시작 시간
        /// </summary>
        public DateTime StartTime { get; set; }
        
        /// <summary>
        /// 종료 시간
        /// </summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// 처리된 항목 수
        /// </summary>
        public long ProcessedItems { get; set; }
        
        /// <summary>
        /// 전체 항목 수
        /// </summary>
        public long TotalItems { get; set; }
        
        /// <summary>
        /// 오류 발생 수
        /// </summary>
        public int ErrorCount { get; set; }
        
        /// <summary>
        /// 경고 발생 수
        /// </summary>
        public int WarningCount { get; set; }
        
        /// <summary>
        /// 건너뛴 항목 수
        /// </summary>
        public int SkippedCount { get; set; }
        
        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsSuccessful { get; set; }
        
        /// <summary>
        /// 소요 시간 (초)
        /// </summary>
        public double ElapsedSeconds => EndTime.HasValue 
            ? (EndTime.Value - StartTime).TotalSeconds 
            : (DateTime.Now - StartTime).TotalSeconds;
    }
}
