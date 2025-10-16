using SpatialCheckPro.Data;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 결과 집계 및 저장을 담당하는 서비스 구현체
    /// </summary>
    public class ValidationResultService : IValidationResultService
    {
        private readonly ILogger<ValidationResultService> _logger;
        private readonly ValidationDbContext _dbContext;

        public ValidationResultService(
            ILogger<ValidationResultService> logger,
            ValidationDbContext dbContext)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        /// <summary>
        /// 검수 결과를 SQLite 데이터베이스에 저장
        /// </summary>
        public async Task<bool> SaveValidationResultAsync(ValidationResult validationResult)
        {
            try
            {
                _logger.LogInformation("검수 결과를 저장합니다. ID: {ValidationId}", validationResult.ValidationId);

                using var transaction = await _dbContext.Database.BeginTransactionAsync();

                try
                {
                    // 공간정보 파일 정보 저장 또는 업데이트
                    var spatialFileEntity = await GetOrCreateSpatialFileEntityAsync(new SpatialFileInfo { FilePath = validationResult.TargetFile });

                    // 검수 결과 엔티티 생성
                    var validationEntity = new ValidationResultEntity
                    {
                        ValidationId = validationResult.ValidationId,
                        TargetFileId = spatialFileEntity.Id,
                        StartedAt = validationResult.StartedAt,
                        CompletedAt = validationResult.CompletedAt,
                        Status = validationResult.Status,
                        TotalErrors = validationResult.TotalErrors,
                        TotalWarnings = validationResult.TotalWarnings,
                        ErrorMessage = validationResult.ErrorMessage,
                        DurationMs = validationResult.CompletedAt.HasValue 
                            ? (long)(validationResult.CompletedAt.Value - validationResult.StartedAt).TotalMilliseconds 
                            : 0,
                        UpdatedAt = DateTime.Now
                    };

                    // 기존 검수 결과가 있는지 확인
                    var existingEntity = await _dbContext.ValidationResults
                        .FirstOrDefaultAsync(v => v.ValidationId == validationResult.ValidationId);

                    if (existingEntity != null)
                    {
                        // 업데이트
                        _dbContext.Entry(existingEntity).CurrentValues.SetValues(validationEntity);
                        validationEntity = existingEntity;
                    }
                    else
                    {
                        // 새로 추가
                        _dbContext.ValidationResults.Add(validationEntity);
                    }

                    await _dbContext.SaveChangesAsync();

                    // 단계별 결과 저장
                    await SaveStageResultsAsync(validationResult, validationEntity.ValidationId);

                    await transaction.CommitAsync();

                    _logger.LogInformation("검수 결과 저장이 완료되었습니다. ID: {ValidationId}", validationResult.ValidationId);
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "검수 결과 저장 중 트랜잭션 오류가 발생했습니다. ID: {ValidationId}", validationResult.ValidationId);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 저장 중 오류가 발생했습니다. ID: {ValidationId}", validationResult.ValidationId);
                return false;
            }
        }

        /// <summary>
        /// 검수 결과 조회
        /// </summary>
        public async Task<ValidationResult?> GetValidationResultAsync(string validationId)
        {
            try
            {
                _logger.LogDebug("검수 결과를 조회합니다. ID: {ValidationId}", validationId);

                var entity = await _dbContext.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .FirstOrDefaultAsync(v => v.ValidationId == validationId);

                if (entity == null)
                {
                    _logger.LogWarning("검수 결과를 찾을 수 없습니다. ID: {ValidationId}", validationId);
                    return null;
                }

                return ConvertToValidationResult(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 조회 중 오류가 발생했습니다. ID: {ValidationId}", validationId);
                return null;
            }
        }

        /// <summary>
        /// 파일별 검수 이력 조회
        /// </summary>
        public async Task<IEnumerable<ValidationResult>> GetValidationHistoryAsync(string filePath, int limit = 10)
        {
            try
            {
                _logger.LogDebug("파일별 검수 이력을 조회합니다. 파일: {FilePath}, 제한: {Limit}", filePath, limit);

                var entities = await _dbContext.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .Where(v => v.TargetFile.FilePath == filePath)
                    .OrderByDescending(v => v.StartedAt)
                    .Take(limit)
                    .ToListAsync();

                return entities.Select(ConvertToValidationResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일별 검수 이력 조회 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return Enumerable.Empty<ValidationResult>();
            }
        }

        /// <summary>
        /// 전체 검수 이력 조회
        /// </summary>
        public async Task<IEnumerable<ValidationResult>> GetAllValidationHistoryAsync(
            DateTime? startDate = null, 
            DateTime? endDate = null, 
            ValidationStatus? status = null, 
            int limit = 100)
        {
            try
            {
                _logger.LogDebug("전체 검수 이력을 조회합니다. 시작: {StartDate}, 종료: {EndDate}, 상태: {Status}, 제한: {Limit}",
                    startDate, endDate, status, limit);

                var query = _dbContext.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .AsQueryable();

                if (startDate.HasValue)
                {
                    query = query.Where(v => v.StartedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(v => v.StartedAt <= endDate.Value);
                }

                if (status.HasValue)
                {
                    query = query.Where(v => v.Status == status.Value);
                }

                var entities = await query
                    .OrderByDescending(v => v.StartedAt)
                    .Take(limit)
                    .ToListAsync();

                return entities.Select(ConvertToValidationResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "전체 검수 이력 조회 중 오류가 발생했습니다.");
                return Enumerable.Empty<ValidationResult>();
            }
        }

        /// <summary>
        /// 중복 검수 방지를 위한 최근 검수 결과 확인
        /// </summary>
        public async Task<ValidationResult?> GetRecentValidationResultAsync(string filePath, DateTime fileModifiedTime, string configHash)
        {
            try
            {
                _logger.LogDebug("최근 검수 결과를 확인합니다. 파일: {FilePath}, 수정시간: {ModifiedTime}, 설정해시: {ConfigHash}",
                    filePath, fileModifiedTime, configHash);

                var entity = await _dbContext.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .Where(v => v.TargetFile.FilePath == filePath &&
                               v.TargetFile.ModifiedAt == fileModifiedTime &&
                               v.ConfigHash == configHash &&
                               v.Status == ValidationStatus.Completed)
                    .OrderByDescending(v => v.StartedAt)
                    .FirstOrDefaultAsync();

                return entity != null ? ConvertToValidationResult(entity) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "최근 검수 결과 확인 중 오류가 발생했습니다. 파일: {FilePath}", filePath);
                return null;
            }
        }

        /// <summary>
        /// 검수 결과 삭제
        /// </summary>
        public async Task<bool> DeleteValidationResultAsync(string validationId)
        {
            try
            {
                _logger.LogInformation("검수 결과를 삭제합니다. ID: {ValidationId}", validationId);

                var entity = await _dbContext.ValidationResults
                    .FirstOrDefaultAsync(v => v.ValidationId == validationId);

                if (entity == null)
                {
                    _logger.LogWarning("삭제할 검수 결과를 찾을 수 없습니다. ID: {ValidationId}", validationId);
                    return false;
                }

                _dbContext.ValidationResults.Remove(entity);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("검수 결과가 삭제되었습니다. ID: {ValidationId}", validationId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 삭제 중 오류가 발생했습니다. ID: {ValidationId}", validationId);
                return false;
            }
        }

        /// <summary>
        /// 오래된 검수 결과 정리
        /// </summary>
        public async Task<int> CleanupOldValidationResultsAsync(int retentionDays = 30)
        {
            try
            {
                _logger.LogInformation("오래된 검수 결과를 정리합니다. 보관기간: {RetentionDays}일", retentionDays);

                var cutoffDate = DateTime.Now.AddDays(-retentionDays);
                var oldResults = await _dbContext.ValidationResults
                    .Where(v => v.StartedAt < cutoffDate)
                    .ToListAsync();

                if (!oldResults.Any())
                {
                    _logger.LogInformation("정리할 오래된 검수 결과가 없습니다.");
                    return 0;
                }

                _dbContext.ValidationResults.RemoveRange(oldResults);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("오래된 검수 결과 {Count}개가 정리되었습니다.", oldResults.Count);
                return oldResults.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오래된 검수 결과 정리 중 오류가 발생했습니다.");
                return 0;
            }
        }

        /// <summary>
        /// 검수 통계 조회
        /// </summary>
        public async Task<Models.ValidationStatistics> GetValidationStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                _logger.LogDebug("검수 통계를 조회합니다. 시작: {StartDate}, 종료: {EndDate}", startDate, endDate);

                var query = _dbContext.ValidationResults.AsQueryable();

                if (startDate.HasValue)
                {
                    query = query.Where(v => v.StartedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(v => v.StartedAt <= endDate.Value);
                }

                var validations = await query
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                    .ToListAsync();

                var statistics = new Models.ValidationStatistics
                {
                    TotalValidations = validations.Count,
                    SuccessfulValidations = validations.Count(v => v.Status == ValidationStatus.Completed),
                    FailedValidations = validations.Count(v => v.Status == ValidationStatus.Failed),
                    CancelledValidations = validations.Count(v => v.Status == ValidationStatus.Cancelled),
                    TotalErrors = validations.Sum(v => v.TotalErrors),
                    TotalWarnings = validations.Sum(v => v.TotalWarnings),
                    GeneratedAt = DateTime.Now
                };

                // 평균 검수 소요 시간 계산
                var completedValidations = validations.Where(v => v.DurationMs > 0).ToList();
                if (completedValidations.Any())
                {
                    statistics.AverageValidationTimeMinutes = completedValidations.Average(v => v.DurationMs) / 1000.0 / 60.0;
                }

                // 가장 많이 검수된 파일 형식
                if (validations.Any())
                {
                    statistics.MostValidatedFormat = validations
                        .GroupBy(v => v.TargetFile.Format)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
                }

                // 단계별 실패 통계
                foreach (var validation in validations)
                {
                    foreach (var stage in validation.StageResults.Where(s => s.Status == StageStatus.Failed))
                    {
                        if (statistics.StageFailureCount.ContainsKey(stage.StageNumber))
                        {
                            statistics.StageFailureCount[stage.StageNumber]++;
                        }
                        else
                        {
                            statistics.StageFailureCount[stage.StageNumber] = 1;
                        }
                    }
                }

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 통계 조회 중 오류가 발생했습니다.");
                return new Models.ValidationStatistics { GeneratedAt = DateTime.Now };
            }
        }

        /// <summary>
        /// 검수 결과 요약 정보 생성
        /// </summary>
        public ValidationSummary CreateValidationSummary(ValidationResult validationResult)
        {
            var summary = new ValidationSummary
            {
                ValidationId = validationResult.ValidationId,
                FileName = System.IO.Path.GetFileName(validationResult.TargetFile),
                FileFormat = GetFileFormat(validationResult.TargetFile),
                Status = validationResult.Status,
                StartedAt = validationResult.StartedAt,
                CompletedAt = validationResult.CompletedAt,
                TotalErrors = validationResult.TotalErrors,
                TotalWarnings = validationResult.TotalWarnings
            };

            if (validationResult.CompletedAt.HasValue)
            {
                summary.Duration = validationResult.CompletedAt.Value - validationResult.StartedAt;
            }

            // 완료된 단계 수 계산
            var stageResults = new List<StageResult>();
            if (validationResult.TableCheckResult != null) stageResults.Add(ConvertCheckResultToStageResult(validationResult.TableCheckResult, 1, "테이블 검수"));
            if (validationResult.SchemaCheckResult != null) stageResults.Add(ConvertCheckResultToStageResult(validationResult.SchemaCheckResult, 2, "스키마 검수"));
            if (validationResult.GeometryCheckResult != null) stageResults.Add(ConvertCheckResultToStageResult(validationResult.GeometryCheckResult, 3, "지오메트리 검수"));
            if (validationResult.RelationCheckResult != null) stageResults.Add(ConvertCheckResultToStageResult(validationResult.RelationCheckResult, 4, "관계 검수"));

            summary.CompletedStages = stageResults.Count(s => s.Status == StageStatus.Completed);

            // 단계별 상태 설정
            if (validationResult.TableCheckResult != null)
                summary.StageStatuses[1] = ConvertCheckStatusToStageStatus(validationResult.TableCheckResult.Status);
            if (validationResult.SchemaCheckResult != null)
                summary.StageStatuses[2] = ConvertCheckStatusToStageStatus(validationResult.SchemaCheckResult.Status);
            if (validationResult.GeometryCheckResult != null)
                summary.StageStatuses[3] = ConvertCheckStatusToStageStatus(validationResult.GeometryCheckResult.Status);
            if (validationResult.RelationCheckResult != null)
                summary.StageStatuses[4] = ConvertCheckStatusToStageStatus(validationResult.RelationCheckResult.Status);

            // 성공률 계산
            if (summary.TotalStages > 0)
            {
                summary.SuccessRate = (double)summary.CompletedStages / summary.TotalStages * 100.0;
            }

            return summary;
        }

        /// <summary>
        /// 검수 결과 통계 계산
        /// </summary>
        public Models.ValidationStatistics CalculateStatistics(ValidationResult validationResult)
        {
            var statistics = new Models.ValidationStatistics
            {
                TotalValidations = 1,
                TotalErrors = validationResult.TotalErrors,
                TotalWarnings = validationResult.TotalWarnings,
                GeneratedAt = DateTime.Now
            };

            // 검수 소요 시간 계산
            if (validationResult.CompletedAt.HasValue)
            {
                statistics.Duration = validationResult.CompletedAt.Value - validationResult.StartedAt;
                statistics.SuccessfulValidations = validationResult.Status == ValidationStatus.Completed ? 1 : 0;
                statistics.FailedValidations = validationResult.Status == ValidationStatus.Failed ? 1 : 0;
                statistics.CancelledValidations = validationResult.Status == ValidationStatus.Cancelled ? 1 : 0;
            }

            statistics.MostValidatedFormat = GetFileFormat(validationResult.TargetFile);

            return statistics;
        }

        /// <summary>
        /// 단계별 검수 결과 집계
        /// </summary>
        public ValidationAggregate AggregateStageResults(IEnumerable<StageResult> stageResults)
        {
            var aggregate = new ValidationAggregate();
            var stageList = stageResults.ToList();

            foreach (var stage in stageList)
            {
                var stageAggregate = new StageAggregate
                {
                    StageNumber = stage.StageNumber,
                    StageName = stage.StageName,
                    Status = stage.Status,
                    CheckCount = stage.CheckResults.Count,
                    ErrorCount = stage.CheckResults.Sum(c => c.ErrorCount),
                    WarningCount = stage.CheckResults.Sum(c => c.WarningCount)
                };

                if (stage.CompletedAt.HasValue)
                {
                    stageAggregate.Duration = stage.CompletedAt.Value - stage.StartedAt;
                }

                aggregate.StageAggregates[stage.StageNumber] = stageAggregate;

                // 전체 집계에 추가
                aggregate.TotalChecks += stageAggregate.CheckCount;
                aggregate.TotalErrors += stageAggregate.ErrorCount;
                aggregate.TotalWarnings += stageAggregate.WarningCount;

                // 검수 항목별 집계
                foreach (var check in stage.CheckResults)
                {
                    var checkAggregate = new CheckAggregate
                    {
                        CheckId = check.CheckId,
                        CheckName = check.CheckName,
                        Status = check.Status,
                        TotalCount = check.TotalCount,
                        ErrorCount = check.ErrorCount,
                        WarningCount = check.WarningCount
                    };

                    if (check.TotalCount > 0)
                    {
                        checkAggregate.SuccessRate = (double)(check.TotalCount - check.ErrorCount) / check.TotalCount * 100.0;
                    }

                    aggregate.CheckAggregates[check.CheckId] = checkAggregate;

                    // 상태별 카운트
                    switch (check.Status)
                    {
                        case CheckStatus.Passed:
                            aggregate.PassedChecks++;
                            break;
                        case CheckStatus.Failed:
                            aggregate.FailedChecks++;
                            break;
                        case CheckStatus.Warning:
                            aggregate.WarningChecks++;
                            break;
                    }
                }
            }

            return aggregate;
        }

        #region Private Methods

        /// <summary>
        /// 공간정보 파일 엔티티 조회 또는 생성
        /// </summary>
        private async Task<SpatialFileInfoEntity> GetOrCreateSpatialFileEntityAsync(SpatialFileInfo spatialFile)
        {
            var existingEntity = await _dbContext.SpatialFiles
                .FirstOrDefaultAsync(f => f.FilePath == spatialFile.FilePath);

            if (existingEntity != null)
            {
                // 기존 엔티티 업데이트
                existingEntity.FileName = spatialFile.FileName;
                existingEntity.Format = spatialFile.Format;
                existingEntity.FileSize = spatialFile.FileSize;
                existingEntity.CoordinateSystem = spatialFile.CoordinateSystem;
                existingEntity.TablesJson = spatialFile.Tables;
                existingEntity.ModifiedAt = spatialFile.ModifiedAt ?? DateTime.Now;

                return existingEntity;
            }
            else
            {
                // 새 엔티티 생성
                var newEntity = new SpatialFileInfoEntity
                {
                    FilePath = spatialFile.FilePath,
                    FileName = spatialFile.FileName,
                    Format = spatialFile.Format,
                    FileSize = spatialFile.FileSize,
                    CoordinateSystem = spatialFile.CoordinateSystem,
                    TablesJson = spatialFile.Tables,
                    CreatedAt = spatialFile.CreatedAt ?? DateTime.Now,
                    ModifiedAt = spatialFile.ModifiedAt ?? DateTime.Now
                };

                _dbContext.SpatialFiles.Add(newEntity);
                await _dbContext.SaveChangesAsync();

                return newEntity;
            }
        }

        /// <summary>
        /// 단계별 결과 저장
        /// </summary>
        private async Task SaveStageResultsAsync(ValidationResult validationResult, string validationId)
        {
            // 기존 단계 결과 삭제
            var existingStages = await _dbContext.StageResults
                .Where(s => s.ValidationId == validationId)
                .ToListAsync();

            if (existingStages.Any())
            {
                _dbContext.StageResults.RemoveRange(existingStages);
                await _dbContext.SaveChangesAsync();
            }

            // 새 단계 결과 저장
            var stageResults = new List<StageResult>();
            if (validationResult.TableCheckResult != null) stageResults.Add(validationResult.TableCheckResult.ToStageResult());
            if (validationResult.SchemaCheckResult != null) stageResults.Add(validationResult.SchemaCheckResult.ToStageResult());
            if (validationResult.GeometryCheckResult != null) stageResults.Add(validationResult.GeometryCheckResult.ToStageResult());
            if (validationResult.RelationCheckResult != null) stageResults.Add(validationResult.RelationCheckResult.ToStageResult());

            foreach (var stageResult in stageResults)
            {
                var stageEntity = new StageResultEntity
                {
                    ValidationId = validationId,
                    StageNumber = stageResult.StageNumber,
                    StageName = stageResult.StageName,
                    Status = stageResult.Status,
                    StartedAt = stageResult.StartedAt,
                    CompletedAt = stageResult.CompletedAt,
                    ErrorMessage = stageResult.ErrorMessage,
                    DurationMs = stageResult.CompletedAt.HasValue 
                        ? (long)(stageResult.CompletedAt.Value - stageResult.StartedAt).TotalMilliseconds 
                        : 0
                };

                _dbContext.StageResults.Add(stageEntity);
                await _dbContext.SaveChangesAsync();

                // 검수 항목 결과 저장
                await SaveCheckResultsAsync(stageResult.CheckResults, stageEntity.Id);
            }
        }

        /// <summary>
        /// 검수 항목 결과 저장
        /// </summary>
        private async Task SaveCheckResultsAsync(List<CheckResult> checkResults, int stageResultId)
        {
            foreach (var checkResult in checkResults)
            {
                var checkEntity = new CheckResultEntity
                {
                    StageResultId = stageResultId,
                    CheckId = checkResult.CheckId,
                    CheckName = checkResult.CheckName,
                    Status = checkResult.Status,
                    TotalCount = checkResult.TotalCount,
                    ErrorCount = checkResult.ErrorCount,
                    WarningCount = checkResult.WarningCount
                };

                _dbContext.CheckResults.Add(checkEntity);
                await _dbContext.SaveChangesAsync();

                // 오류 정보 저장
                await SaveValidationErrorsAsync(checkResult.Errors, checkEntity.Id);
                await SaveValidationErrorsAsync(checkResult.Warnings, checkEntity.Id);
            }
        }

        /// <summary>
        /// 검수 오류 정보 저장
        /// </summary>
        private async Task SaveValidationErrorsAsync(List<ValidationError> errors, int checkResultId)
        {
            foreach (var error in errors)
            {
                var errorEntity = new ValidationErrorEntity
                {
                    ErrorId = error.ErrorId,
                    CheckResultId = checkResultId,
                    TableName = error.TableName,
                    FeatureId = error.FeatureId,
                    Message = error.Message,
                    Severity = error.Severity,
                    ErrorType = error.ErrorType,
                    OccurredAt = DateTime.Now,
                    MetadataJson = error.Metadata
                };

                if (error.Location != null)
                {
                    errorEntity.LocationX = error.Location.X;
                    errorEntity.LocationY = error.Location.Y;
                    errorEntity.LocationZ = error.Location.Z;
                    errorEntity.LocationCoordinateSystem = error.Location.CoordinateSystem;
                }

                _dbContext.ValidationErrors.Add(errorEntity);
            }

            if (errors.Any())
            {
                await _dbContext.SaveChangesAsync();
            }
        }

        /// <summary>
        /// 엔티티를 ValidationResult로 변환
        /// </summary>
        private ValidationResult ConvertToValidationResult(ValidationResultEntity entity)
        {
            var result = new ValidationResult
            {
                ValidationId = entity.ValidationId,
                TargetFile = entity.TargetFile?.FilePath ?? string.Empty,
                StartedAt = entity.StartedAt,
                CompletedAt = entity.CompletedAt,
                Status = entity.Status,
                TotalErrors = entity.TotalErrors,
                TotalWarnings = entity.TotalWarnings,
                ErrorMessage = entity.ErrorMessage
            };

            // 단계별 결과 변환
            foreach (var stageEntity in entity.StageResults.OrderBy(s => s.StageNumber))
            {
                var stageResult = ConvertToStageResult(stageEntity);

                switch (stageEntity.StageNumber)
                {
                    case 1:
                        result.TableCheckResult = ConvertStageResultToTableCheckResult(stageResult);
                        break;
                    case 2:
                        result.SchemaCheckResult = ConvertStageResultToSchemaCheckResult(stageResult);
                        break;
                    case 3:
                        result.GeometryCheckResult = ConvertStageResultToGeometryCheckResult(stageResult);
                        break;
                    case 4:
                        result.RelationCheckResult = ConvertStageResultToRelationCheckResult(stageResult);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// 엔티티를 SpatialFileInfo로 변환
        /// </summary>
        private SpatialFileInfo ConvertToSpatialFileInfo(SpatialFileInfoEntity entity)
        {
            return new SpatialFileInfo
            {
                FilePath = entity.FilePath,
                FileName = entity.FileName,
                Format = entity.Format,
                FileSize = entity.FileSize,
                CoordinateSystem = entity.CoordinateSystem,
                Tables = entity.TablesJson,
                CreatedAt = entity.CreatedAt,
                ModifiedAt = entity.ModifiedAt
            };
        }

        /// <summary>
        /// 엔티티를 StageResult로 변환
        /// </summary>
        private StageResult ConvertToStageResult(StageResultEntity entity)
        {
            var stageResult = new StageResult
            {
                StageNumber = entity.StageNumber,
                StageName = entity.StageName,
                Status = entity.Status,
                StartedAt = entity.StartedAt,
                CompletedAt = entity.CompletedAt,
                ErrorMessage = entity.ErrorMessage
            };

            // 검수 항목 결과 변환
            foreach (var checkEntity in entity.CheckResults)
            {
                var checkResult = new CheckResult
                {
                    CheckId = checkEntity.CheckId,
                    CheckName = checkEntity.CheckName,
                    Status = checkEntity.Status,
                    TotalCount = checkEntity.TotalCount,
                    ErrorCount = checkEntity.ErrorCount,
                    WarningCount = checkEntity.WarningCount
                };

                // 오류 정보 변환
                foreach (var errorEntity in checkEntity.Errors)
                {
                    var error = new ValidationError
                    {
                        ErrorId = errorEntity.ErrorId,
                        TableName = errorEntity.TableName,
                        FeatureId = errorEntity.FeatureId,
                        Message = errorEntity.Message,
                        Severity = errorEntity.Severity,
                        ErrorType = errorEntity.ErrorType,
                        Metadata = errorEntity.MetadataJson
                    };

                    if (errorEntity.LocationX.HasValue && errorEntity.LocationY.HasValue)
                    {
                        error.Location = new GeographicLocation
                        {
                            X = errorEntity.LocationX.Value,
                            Y = errorEntity.LocationY.Value,
                            Z = errorEntity.LocationZ ?? 0.0,
                            CoordinateSystem = errorEntity.LocationCoordinateSystem ?? string.Empty
                        };
                    }

                    if (error.Severity == ErrorSeverity.Error || error.Severity == ErrorSeverity.Critical)
                    {
                        checkResult.Errors.Add(error);
                    }
                    else
                    {
                        checkResult.Warnings.Add(error);
                    }
                }

                stageResult.CheckResults.Add(checkResult);
            }

            return stageResult;
        }

        /// <summary>
        /// 설정 해시 생성
        /// </summary>
        private string GenerateConfigHash(string configContent)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(configContent));
            return Convert.ToBase64String(hashBytes);
        }



        /// <summary>
        /// 파일 경로에서 파일 형식을 추출합니다
        /// </summary>
        private static SpatialFileFormat GetFileFormat(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".shp" => SpatialFileFormat.SHP,
                ".gdb" => SpatialFileFormat.FileGDB,
                ".gpkg" => SpatialFileFormat.GeoPackage,
                _ => SpatialFileFormat.SHP
            };
        }

        /// <summary>
        /// CheckResult를 StageResult로 변환합니다
        /// </summary>
        private static StageResult ConvertCheckResultToStageResult(TableCheckResult checkResult, int stageNumber, string stageName)
        {
            return new StageResult
            {
                StageNumber = stageNumber,
                StageName = stageName,
                Status = ConvertCheckStatusToStageStatus(checkResult.Status),
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now,
                CheckResults = new List<CheckResult> { checkResult }
            };
        }

        /// <summary>
        /// CheckResult를 StageResult로 변환합니다 (SchemaCheckResult)
        /// </summary>
        private static StageResult ConvertCheckResultToStageResult(SchemaCheckResult checkResult, int stageNumber, string stageName)
        {
            return new StageResult
            {
                StageNumber = stageNumber,
                StageName = stageName,
                Status = ConvertCheckStatusToStageStatus(checkResult.Status),
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now,
                CheckResults = new List<CheckResult> { checkResult }
            };
        }

        /// <summary>
        /// CheckResult를 StageResult로 변환합니다 (GeometryCheckResult)
        /// </summary>
        private static StageResult ConvertCheckResultToStageResult(GeometryCheckResult checkResult, int stageNumber, string stageName)
        {
            return new StageResult
            {
                StageNumber = stageNumber,
                StageName = stageName,
                Status = ConvertCheckStatusToStageStatus(checkResult.Status),
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now,
                CheckResults = new List<CheckResult> { checkResult }
            };
        }

        /// <summary>
        /// CheckResult를 StageResult로 변환합니다 (RelationCheckResult)
        /// </summary>
        private static StageResult ConvertCheckResultToStageResult(RelationCheckResult checkResult, int stageNumber, string stageName)
        {
            return new StageResult
            {
                StageNumber = stageNumber,
                StageName = stageName,
                Status = ConvertCheckStatusToStageStatus(checkResult.Status),
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now,
                CheckResults = new List<CheckResult> { checkResult }
            };
        }

        /// <summary>
        /// CheckStatus를 StageStatus로 변환합니다
        /// </summary>
        private static StageStatus ConvertCheckStatusToStageStatus(CheckStatus checkStatus)
        {
            return checkStatus switch
            {
                CheckStatus.NotStarted => StageStatus.NotStarted,
                CheckStatus.Running => StageStatus.Running,
                CheckStatus.Passed => StageStatus.Completed,
                CheckStatus.Failed => StageStatus.Failed,
                CheckStatus.Warning => StageStatus.Completed,
                _ => StageStatus.NotStarted
            };
        }

        /// <summary>
        /// StageResult를 TableCheckResult로 변환
        /// </summary>
        private static TableCheckResult ConvertStageResultToTableCheckResult(StageResult stageResult)
        {
            return new TableCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 SchemaCheckResult로 변환
        /// </summary>
        private static SchemaCheckResult ConvertStageResultToSchemaCheckResult(StageResult stageResult)
        {
            return new SchemaCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 GeometryCheckResult로 변환
        /// </summary>
        private static GeometryCheckResult ConvertStageResultToGeometryCheckResult(StageResult stageResult)
        {
            return new GeometryCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
            };
        }

        /// <summary>
        /// StageResult를 RelationCheckResult로 변환
        /// </summary>
        private static RelationCheckResult ConvertStageResultToRelationCheckResult(StageResult stageResult)
        {
            return new RelationCheckResult
            {
                CheckId = stageResult.StageId,
                CheckName = stageResult.StageName,
                Status = ConvertStageStatusToCheckStatus(stageResult.Status),
                ErrorCount = stageResult.ErrorCount,
                WarningCount = stageResult.WarningCount,
                Errors = stageResult.Errors,
                Warnings = stageResult.Warnings,
                Metadata = stageResult.Metadata
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

        /// <summary>
        /// TableCheckResult를 CheckResult로 변환
        /// </summary>
        private static CheckResult ConvertToCheckResult(TableCheckResult checkResult)
        {
            return new CheckResult
            {
                CheckId = checkResult.CheckId,
                CheckName = checkResult.CheckName,
                Status = checkResult.Status,
                ErrorCount = checkResult.ErrorCount,
                WarningCount = checkResult.WarningCount,
                Errors = checkResult.Errors,
                Warnings = checkResult.Warnings
            };
        }

        /// <summary>
        /// SchemaCheckResult를 CheckResult로 변환
        /// </summary>
        private static CheckResult ConvertToCheckResult(SchemaCheckResult checkResult)
        {
            return new CheckResult
            {
                CheckId = checkResult.CheckId,
                CheckName = checkResult.CheckName,
                Status = checkResult.Status,
                ErrorCount = checkResult.ErrorCount,
                WarningCount = checkResult.WarningCount,
                Errors = checkResult.Errors,
                Warnings = checkResult.Warnings
            };
        }

        /// <summary>
        /// GeometryCheckResult를 CheckResult로 변환
        /// </summary>
        private static CheckResult ConvertToCheckResult(GeometryCheckResult checkResult)
        {
            return new CheckResult
            {
                CheckId = checkResult.CheckId,
                CheckName = checkResult.CheckName,
                Status = checkResult.Status,
                ErrorCount = checkResult.ErrorCount,
                WarningCount = checkResult.WarningCount,
                Errors = checkResult.Errors,
                Warnings = checkResult.Warnings
            };
        }

        /// <summary>
        /// RelationCheckResult를 CheckResult로 변환
        /// </summary>
        private static CheckResult ConvertToCheckResult(RelationCheckResult checkResult)
        {
            return new CheckResult
            {
                CheckId = checkResult.CheckId,
                CheckName = checkResult.CheckName,
                Status = checkResult.Status,
                ErrorCount = checkResult.ErrorCount,
                WarningCount = checkResult.WarningCount,
                Errors = checkResult.Errors,
                Warnings = checkResult.Warnings
            };
        }

        #endregion
    }
}
