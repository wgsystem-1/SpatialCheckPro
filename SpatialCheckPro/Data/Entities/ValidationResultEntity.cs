using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 검수 결과 엔티티 클래스
    /// </summary>
    public class ValidationResultEntity
    {
        /// <summary>
        /// 검수 ID (Primary Key)
        /// </summary>
        [Key]
        public string ValidationId { get; set; } = string.Empty;

        /// <summary>
        /// 검수 대상 파일 ID (Foreign Key)
        /// </summary>
        public int TargetFileId { get; set; }

        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 전체 검수 상태
        /// </summary>
        public ValidationStatus Status { get; set; }

        /// <summary>
        /// 전체 오류 개수
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// 전체 경고 개수
        /// </summary>
        public int TotalWarnings { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 검수 소요 시간 (밀리초)
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// 업데이트 시간
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 설정 해시
        /// </summary>
        public string? ConfigHash { get; set; }

        /// <summary>
        /// 검수 대상 파일 (Navigation Property)
        /// </summary>
        public SpatialFileInfoEntity TargetFile { get; set; } = null!;

        /// <summary>
        /// 단계별 검수 결과 목록 (Navigation Property)
        /// </summary>
        public List<StageResultEntity> StageResults { get; set; } = new List<StageResultEntity>();

        /// <summary>
        /// 검수 소요 시간 (밀리초)
        /// </summary>
        public long ElapsedMilliseconds => CompletedAt.HasValue 
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds 
            : 0;

        /// <summary>
        /// 검수 성공 여부
        /// </summary>
        public bool IsSuccess => Status == ValidationStatus.Completed && TotalErrors == 0;

        /// <summary>
        /// 도메인 모델로 변환
        /// </summary>
        /// <returns>ValidationResult 도메인 모델</returns>
        public Models.ValidationResult ToDomainModel()
        {
            return new Models.ValidationResult
            {
                ValidationId = ValidationId,
                TargetFile = TargetFile?.FilePath ?? string.Empty,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                Status = Status,
                TotalErrors = TotalErrors,
                TotalWarnings = TotalWarnings,
                TableCheckResult = ConvertStageResultToTableCheckResult(StageResults.FirstOrDefault(s => s.StageNumber == 1)),
                SchemaCheckResult = ConvertStageResultToSchemaCheckResult(StageResults.FirstOrDefault(s => s.StageNumber == 2)),
                GeometryCheckResult = ConvertStageResultToGeometryCheckResult(StageResults.FirstOrDefault(s => s.StageNumber == 3)),
                RelationCheckResult = ConvertStageResultToRelationCheckResult(StageResults.FirstOrDefault(s => s.StageNumber == 4)),
                AttributeRelationCheckResult = ConvertStageResultToAttributeRelationCheckResult(StageResults.FirstOrDefault(s => s.StageNumber == 5))
            };
        }

        /// <summary>
        /// 도메인 모델에서 엔티티로 변환
        /// </summary>
        /// <param name="domainModel">ValidationResult 도메인 모델</param>
        /// <returns>ValidationResultEntity</returns>
        public static ValidationResultEntity FromDomainModel(Models.ValidationResult domainModel)
        {
            var entity = new ValidationResultEntity
            {
                ValidationId = domainModel.ValidationId,
                StartedAt = domainModel.StartedAt,
                CompletedAt = domainModel.CompletedAt,
                Status = domainModel.Status,
                TotalErrors = domainModel.TotalErrors,
                TotalWarnings = domainModel.TotalWarnings
            };

            // 단계별 결과 추가
            if (domainModel.TableCheckResult != null)
                entity.StageResults.Add(StageResultEntity.FromDomainModel(domainModel.TableCheckResult.ToStageResult(), domainModel.ValidationId));
            
            if (domainModel.SchemaCheckResult != null)
                entity.StageResults.Add(StageResultEntity.FromDomainModel(domainModel.SchemaCheckResult.ToStageResult(), domainModel.ValidationId));
            
            if (domainModel.GeometryCheckResult != null)
                entity.StageResults.Add(StageResultEntity.FromDomainModel(domainModel.GeometryCheckResult.ToStageResult(), domainModel.ValidationId));
            
            if (domainModel.RelationCheckResult != null)
                entity.StageResults.Add(StageResultEntity.FromDomainModel(domainModel.RelationCheckResult.ToStageResult(), domainModel.ValidationId));

            if (domainModel.AttributeRelationCheckResult != null)
                entity.StageResults.Add(StageResultEntity.FromDomainModel(domainModel.AttributeRelationCheckResult.ToStageResult(), domainModel.ValidationId));

            return entity;
        }

        /// <summary>
        /// StageResultEntity를 TableCheckResult로 변환
        /// </summary>
        private static TableCheckResult? ConvertStageResultToTableCheckResult(StageResultEntity? stageResult)
        {
            if (stageResult == null) return null;
            
            return new TableCheckResult
            {
                CheckId = stageResult.Id.ToString(),
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = new List<ValidationError>(),
                Warnings = new List<ValidationError>()
            };
        }

        /// <summary>
        /// StageResultEntity를 SchemaCheckResult로 변환
        /// </summary>
        private static SchemaCheckResult? ConvertStageResultToSchemaCheckResult(StageResultEntity? stageResult)
        {
            if (stageResult == null) return null;
            
            return new SchemaCheckResult
            {
                CheckId = stageResult.Id.ToString(),
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = new List<ValidationError>(),
                Warnings = new List<ValidationError>()
            };
        }

        /// <summary>
        /// StageResultEntity를 GeometryCheckResult로 변환
        /// </summary>
        private static GeometryCheckResult? ConvertStageResultToGeometryCheckResult(StageResultEntity? stageResult)
        {
            if (stageResult == null) return null;
            
            return new GeometryCheckResult
            {
                CheckId = stageResult.Id.ToString(),
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = new List<ValidationError>(),
                Warnings = new List<ValidationError>()
            };
        }

        /// <summary>
        /// StageResultEntity를 RelationCheckResult로 변환
        /// </summary>
        private static RelationCheckResult? ConvertStageResultToRelationCheckResult(StageResultEntity? stageResult)
        {
            if (stageResult == null) return null;
            
            return new RelationCheckResult
            {
                CheckId = stageResult.Id.ToString(),
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = new List<ValidationError>(),
                Warnings = new List<ValidationError>()
            };
        }

        /// <summary>
        /// StageStatus를 CheckStatus로 변환
        /// </summary>
        private static CheckStatus ConvertStageStatusToCheckStatus(StageStatus stageStatus)
        {
            return stageStatus switch
            {
                StageStatus.Completed => CheckStatus.Passed,
                StageStatus.Failed => CheckStatus.Failed,
                StageStatus.CompletedWithWarnings => CheckStatus.Warning,
                StageStatus.Skipped => CheckStatus.Skipped,
                _ => CheckStatus.Failed
            };
        }

        private static AttributeRelationCheckResult? ConvertStageResultToAttributeRelationCheckResult(StageResultEntity? stage)
        {
            if (stage == null) return null;
            var result = new AttributeRelationCheckResult
            {
                CheckId = "STAGE5",
                CheckName = stage.StageName,
                Status = stage.Status == Models.Enums.StageStatus.Completed || stage.Status == Models.Enums.StageStatus.CompletedWithWarnings
                    ? CheckStatus.Passed
                    : (stage.Status == Models.Enums.StageStatus.Failed ? CheckStatus.Failed : CheckStatus.Running),
                StartedAt = stage.StartedAt,
                CompletedAt = stage.CompletedAt,
                Message = stage.ErrorMessage ?? string.Empty
            };

            foreach (var c in stage.CheckResults)
            {
                result.ErrorCount += c.ErrorCount;
                result.WarningCount += c.WarningCount;
                // CheckResultEntity는 Errors 컬렉션 하나에 경고/오류가 함께 들어가며, Severity로 구분됨
                foreach (var ee in c.Errors)
                {
                    var dm = ee.ToDomainModel();
                    if (ee.Severity == ErrorSeverity.Warning)
                        result.Warnings.Add(dm);
                    else
                        result.Errors.Add(dm);
                }
            }
            return result;
        }
    }
}
