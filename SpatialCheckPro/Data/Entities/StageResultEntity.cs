using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 단계별 검수 결과 엔티티 클래스
    /// </summary>
    public class StageResultEntity
    {
        /// <summary>
        /// 엔티티 ID (Primary Key)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 검수 ID (Foreign Key)
        /// </summary>
        public string ValidationId { get; set; } = string.Empty;

        /// <summary>
        /// 단계 번호 (1-4)
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// 단계명
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 단계 상태
        /// </summary>
        public StageStatus Status { get; set; }

        /// <summary>
        /// 단계 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 단계 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 오류 메시지 (실패 시)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 단계 소요 시간 (밀리초)
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 개수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 결과 (Navigation Property)
        /// </summary>
        public ValidationResultEntity ValidationResult { get; set; } = null!;

        /// <summary>
        /// 검수 항목 결과 목록 (Navigation Property)
        /// </summary>
        public List<CheckResultEntity> CheckResults { get; set; } = new List<CheckResultEntity>();

        /// <summary>
        /// 단계 소요 시간 (밀리초)
        /// </summary>
        public long ElapsedMilliseconds => CompletedAt.HasValue 
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds 
            : 0;

        /// <summary>
        /// 단계별 총 오류 개수
        /// </summary>
        public int TotalErrors => CheckResults.Sum(c => c.ErrorCount);

        /// <summary>
        /// 단계별 총 경고 개수
        /// </summary>
        public int TotalWarnings => CheckResults.Sum(c => c.WarningCount);

        /// <summary>
        /// 단계 성공 여부
        /// </summary>
        public bool IsSuccess => Status == StageStatus.Completed && TotalErrors == 0;

        /// <summary>
        /// 도메인 모델로 변환
        /// </summary>
        /// <returns>StageResult 도메인 모델</returns>
        public StageResult ToDomainModel()
        {
            return new StageResult
            {
                StageNumber = StageNumber,
                StageName = StageName,
                Status = Status,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                ErrorMessage = ErrorMessage,
                CheckResults = CheckResults.Select(c => c.ToDomainModel()).ToList()
            };
        }

        /// <summary>
        /// 도메인 모델에서 엔티티로 변환
        /// </summary>
        /// <param name="domainModel">StageResult 도메인 모델</param>
        /// <param name="validationId">검수 ID</param>
        /// <returns>StageResultEntity</returns>
        public static StageResultEntity FromDomainModel(StageResult domainModel, string validationId)
        {
            var entity = new StageResultEntity
            {
                ValidationId = validationId,
                StageNumber = domainModel.StageNumber,
                StageName = domainModel.StageName,
                Status = domainModel.Status,
                StartedAt = domainModel.StartedAt,
                CompletedAt = domainModel.CompletedAt,
                ErrorMessage = domainModel.ErrorMessage
            };

            // 검수 항목 결과 추가
            foreach (var checkResult in domainModel.CheckResults)
            {
                entity.CheckResults.Add(CheckResultEntity.FromDomainModel(checkResult));
            }

            return entity;
        }
    }
}
