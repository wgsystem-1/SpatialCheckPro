using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 검수 항목 결과 엔티티 클래스
    /// </summary>
    public class CheckResultEntity
    {
        /// <summary>
        /// 엔티티 ID (Primary Key)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 단계 결과 ID (Foreign Key)
        /// </summary>
        public int StageResultId { get; set; }

        /// <summary>
        /// 검수 항목 ID
        /// </summary>
        public string CheckId { get; set; } = string.Empty;

        /// <summary>
        /// 검수 항목명
        /// </summary>
        public string CheckName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과 상태
        /// </summary>
        public CheckStatus Status { get; set; }

        /// <summary>
        /// 검수 대상 개수
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 단계 결과 (Navigation Property)
        /// </summary>
        public StageResultEntity StageResult { get; set; } = null!;

        /// <summary>
        /// 오류 목록 (Navigation Property)
        /// </summary>
        public List<ValidationErrorEntity> Errors { get; set; } = new List<ValidationErrorEntity>();

        /// <summary>
        /// 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 개수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 성공률 (백분율)
        /// </summary>
        public double SuccessRate => TotalCount > 0 
            ? ((double)(TotalCount - ErrorCount) / TotalCount) * 100 
            : 100;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsPassed => Status == CheckStatus.Passed;

        /// <summary>
        /// 도메인 모델로 변환
        /// </summary>
        /// <returns>CheckResult 도메인 모델</returns>
        public CheckResult ToDomainModel()
        {
            var domainModel = new CheckResult
            {
                CheckId = CheckId,
                CheckName = CheckName,
                Status = Status,
                TotalCount = TotalCount
            };

            // 오류와 경고 분리
            foreach (var error in Errors)
            {
                var validationError = error.ToDomainModel();
                if (error.Severity == ErrorSeverity.Warning)
                {
                    domainModel.Warnings.Add(validationError);
                }
                else
                {
                    domainModel.Errors.Add(validationError);
                }
            }

            return domainModel;
        }

        /// <summary>
        /// 도메인 모델에서 엔티티로 변환
        /// </summary>
        /// <param name="domainModel">CheckResult 도메인 모델</param>
        /// <returns>CheckResultEntity</returns>
        public static CheckResultEntity FromDomainModel(CheckResult domainModel)
        {
            var entity = new CheckResultEntity
            {
                CheckId = domainModel.CheckId,
                CheckName = domainModel.CheckName,
                Status = domainModel.Status,
                TotalCount = domainModel.TotalCount
            };

            // 오류 추가
            foreach (var error in domainModel.Errors)
            {
                entity.Errors.Add(ValidationErrorEntity.FromDomainModel(error));
            }

            // 경고 추가
            foreach (var warning in domainModel.Warnings)
            {
                entity.Errors.Add(ValidationErrorEntity.FromDomainModel(warning));
            }

            return entity;
        }
    }
}
