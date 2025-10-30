using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Data;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 데이터 관리 서비스 구현체
    /// </summary>
    public class ValidationDataService : IValidationDataService
    {
        private readonly ValidationDbContext _context;
        private readonly ILogger<ValidationDataService> _logger;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="context">데이터베이스 컨텍스트</param>
        /// <param name="logger">로거</param>
        public ValidationDataService(ValidationDbContext context, ILogger<ValidationDataService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 검수 결과 저장
        /// </summary>
        public async Task<bool> SaveValidationResultAsync(ValidationResult validationResult)
        {
            try
            {
                _logger.LogInformation("검수 결과를 저장합니다. ValidationId: {ValidationId}", validationResult.ValidationId);

                // 공간정보 파일 정보 먼저 저장 또는 조회
                var spatialFileEntity = await GetOrCreateSpatialFileEntityAsync(new SpatialFileInfo { FilePath = validationResult.TargetFile });
                
                // 검수 결과 엔티티 생성
                var validationEntity = ValidationResultEntity.FromDomainModel(validationResult);
                validationEntity.TargetFileId = spatialFileEntity.Id;

                // Phase 2 Item #11: AsSplitQuery로 N+1 쿼리 최적화
                // 기존 검수 결과가 있으면 업데이트, 없으면 추가
                var existingEntity = await _context.ValidationResults
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .AsSplitQuery() // 카테시안 곱 방지 - 4개의 별도 쿼리로 분리
                    .FirstOrDefaultAsync(v => v.ValidationId == validationResult.ValidationId);

                if (existingEntity != null)
                {
                    // 기존 데이터 삭제 후 새로 추가
                    _context.ValidationResults.Remove(existingEntity);
                }

                _context.ValidationResults.Add(validationEntity);
                await _context.SaveChangesAsync();

                _logger.LogInformation("검수 결과 저장이 완료되었습니다. ValidationId: {ValidationId}", validationResult.ValidationId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 저장 중 오류가 발생했습니다. ValidationId: {ValidationId}, Error: {ErrorMessage}", 
                    validationResult.ValidationId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 검수 결과 조회 (ID로)
        /// </summary>
        public async Task<ValidationResult?> GetValidationResultAsync(string validationId)
        {
            try
            {
                _logger.LogInformation("검수 결과를 조회합니다. ValidationId: {ValidationId}", validationId);

                // Phase 2 Item #11: AsSplitQuery로 N+1 쿼리 최적화
                var entity = await _context.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .AsSplitQuery() // 카테시안 곱 방지 - 5개의 별도 쿼리로 분리
                    .FirstOrDefaultAsync(v => v.ValidationId == validationId);

                if (entity == null)
                {
                    _logger.LogWarning("검수 결과를 찾을 수 없습니다. ValidationId: {ValidationId}", validationId);
                    return null;
                }

                return entity.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 조회 중 오류가 발생했습니다. ValidationId: {ValidationId}, Error: {ErrorMessage}", 
                    validationId, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 검수 결과 목록 조회 (페이징)
        /// </summary>
        public async Task<(List<ValidationResult> Results, int TotalCount)> GetValidationResultsAsync(int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                _logger.LogInformation("검수 결과 목록을 조회합니다. Page: {PageNumber}, Size: {PageSize}", pageNumber, pageSize);

                var totalCount = await _context.ValidationResults.CountAsync();

                // Phase 2 Item #11: Include만 필요한 필드로 제한, AsSplitQuery 적용
                var entities = await _context.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults) // CheckResults와 Errors는 목록 조회 시 불필요하므로 제외
                    .AsSplitQuery() // 카테시안 곱 방지
                    .OrderByDescending(v => v.StartedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var results = entities.Select(e => e.ToDomainModel()).ToList();

                _logger.LogInformation("검수 결과 목록 조회가 완료되었습니다. 조회된 개수: {Count}, 전체 개수: {TotalCount}", 
                    results.Count, totalCount);

                return (results, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 목록 조회 중 오류가 발생했습니다. Error: {ErrorMessage}", ex.Message);
                return (new List<ValidationResult>(), 0);
            }
        }

        /// <summary>
        /// 파일별 검수 결과 조회
        /// </summary>
        public async Task<List<ValidationResult>> GetValidationResultsByFileAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("파일별 검수 결과를 조회합니다. FilePath: {FilePath}", filePath);

                // Phase 2 Item #11: AsSplitQuery로 N+1 쿼리 최적화
                var entities = await _context.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                        .ThenInclude(s => s.CheckResults)
                            .ThenInclude(c => c.Errors)
                    .AsSplitQuery() // 카테시안 곱 방지
                    .Where(v => v.TargetFile.FilePath == filePath)
                    .OrderByDescending(v => v.StartedAt)
                    .ToListAsync();

                var results = entities.Select(e => e.ToDomainModel()).ToList();

                _logger.LogInformation("파일별 검수 결과 조회가 완료되었습니다. FilePath: {FilePath}, 조회된 개수: {Count}", 
                    filePath, results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일별 검수 결과 조회 중 오류가 발생했습니다. FilePath: {FilePath}, Error: {ErrorMessage}", 
                    filePath, ex.Message);
                return new List<ValidationResult>();
            }
        }

        /// <summary>
        /// 검수 결과 삭제
        /// </summary>
        public async Task<bool> DeleteValidationResultAsync(string validationId)
        {
            try
            {
                _logger.LogInformation("검수 결과를 삭제합니다. ValidationId: {ValidationId}", validationId);

                var entity = await _context.ValidationResults
                    .FirstOrDefaultAsync(v => v.ValidationId == validationId);

                if (entity == null)
                {
                    _logger.LogWarning("삭제할 검수 결과를 찾을 수 없습니다. ValidationId: {ValidationId}", validationId);
                    return false;
                }

                _context.ValidationResults.Remove(entity);
                await _context.SaveChangesAsync();

                _logger.LogInformation("검수 결과 삭제가 완료되었습니다. ValidationId: {ValidationId}", validationId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 삭제 중 오류가 발생했습니다. ValidationId: {ValidationId}, Error: {ErrorMessage}", 
                    validationId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 공간정보 파일 정보 저장
        /// </summary>
        public async Task<SpatialFileInfo?> SaveSpatialFileInfoAsync(SpatialFileInfo spatialFileInfo)
        {
            try
            {
                _logger.LogInformation("공간정보 파일 정보를 저장합니다. FilePath: {FilePath}", spatialFileInfo.FilePath);

                var entity = await GetOrCreateSpatialFileEntityAsync(spatialFileInfo);
                await _context.SaveChangesAsync();

                _logger.LogInformation("공간정보 파일 정보 저장이 완료되었습니다. FilePath: {FilePath}, Id: {Id}", 
                    spatialFileInfo.FilePath, entity.Id);

                return entity.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간정보 파일 정보 저장 중 오류가 발생했습니다. FilePath: {FilePath}, Error: {ErrorMessage}", 
                    spatialFileInfo.FilePath, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 공간정보 파일 정보 조회
        /// </summary>
        public async Task<SpatialFileInfo?> GetSpatialFileInfoAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("공간정보 파일 정보를 조회합니다. FilePath: {FilePath}", filePath);

                var entity = await _context.SpatialFiles
                    .FirstOrDefaultAsync(f => f.FilePath == filePath);

                if (entity == null)
                {
                    _logger.LogWarning("공간정보 파일 정보를 찾을 수 없습니다. FilePath: {FilePath}", filePath);
                    return null;
                }

                return entity.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간정보 파일 정보 조회 중 오류가 발생했습니다. FilePath: {FilePath}, Error: {ErrorMessage}", 
                    filePath, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 검수 통계 조회
        /// </summary>
        public async Task<Models.ValidationStatistics> GetValidationStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("검수 통계를 조회합니다.");

                var totalValidations = await _context.ValidationResults.CountAsync();
                var successfulValidations = await _context.ValidationResults
                    .CountAsync(v => v.Status == ValidationStatus.Completed && v.TotalErrors == 0);
                var failedValidations = await _context.ValidationResults
                    .CountAsync(v => v.Status == ValidationStatus.Failed || v.TotalErrors > 0);

                var totalErrors = await _context.ValidationResults.SumAsync(v => v.TotalErrors);
                var totalWarnings = await _context.ValidationResults.SumAsync(v => v.TotalWarnings);

                var completedValidations = await _context.ValidationResults
                    .Where(v => v.CompletedAt.HasValue)
                    .Select(v => new { v.StartedAt, v.CompletedAt })
                    .ToListAsync();

                var averageTime = completedValidations.Any() 
                    ? completedValidations.Average(v => (v.CompletedAt!.Value - v.StartedAt).TotalMilliseconds)
                    : 0;

                var validatedFilesCount = await _context.SpatialFiles.CountAsync();

                var statistics = new Models.ValidationStatistics
                {
                    TotalValidations = totalValidations,
                    SuccessfulValidations = successfulValidations,
                    FailedValidations = failedValidations,
                    TotalErrors = totalErrors,
                    TotalWarnings = totalWarnings,
                    AverageValidationTimeMinutes = averageTime / 1000.0 / 60.0, // 밀리초를 분으로 변환
                    GeneratedAt = DateTime.Now
                };

                _logger.LogInformation("검수 통계 조회가 완료되었습니다. 총 검수: {Total}, 성공: {Success}, 실패: {Failed}", 
                    totalValidations, successfulValidations, failedValidations);

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 통계 조회 중 오류가 발생했습니다. Error: {ErrorMessage}", ex.Message);
                return new Models.ValidationStatistics();
            }
        }

        /// <summary>
        /// 오류 통계 조회 (오류 유형별)
        /// </summary>
        public async Task<Dictionary<ErrorType, int>> GetErrorStatisticsByTypeAsync()
        {
            try
            {
                _logger.LogInformation("오류 유형별 통계를 조회합니다.");

                var errorStats = await _context.ValidationErrors
                    .GroupBy(e => e.ErrorType)
                    .Select(g => new { ErrorType = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ErrorType, x => x.Count);

                _logger.LogInformation("오류 유형별 통계 조회가 완료되었습니다. 유형 개수: {TypeCount}", errorStats.Count);

                return errorStats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 유형별 통계 조회 중 오류가 발생했습니다. Error: {ErrorMessage}", ex.Message);
                return new Dictionary<ErrorType, int>();
            }
        }

        /// <summary>
        /// 최근 검수 결과 조회
        /// </summary>
        public async Task<List<ValidationResult>> GetRecentValidationResultsAsync(int count = 10)
        {
            try
            {
                _logger.LogInformation("최근 검수 결과를 조회합니다. Count: {Count}", count);

                // Phase 2 Item #11: AsSplitQuery 적용
                var entities = await _context.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                    .AsSplitQuery() // 카테시안 곱 방지
                    .OrderByDescending(v => v.StartedAt)
                    .Take(count)
                    .ToListAsync();

                var results = entities.Select(e => e.ToDomainModel()).ToList();

                _logger.LogInformation("최근 검수 결과 조회가 완료되었습니다. 조회된 개수: {Count}", results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "최근 검수 결과 조회 중 오류가 발생했습니다. Error: {ErrorMessage}", ex.Message);
                return new List<ValidationResult>();
            }
        }

        /// <summary>
        /// 검수 결과 검색
        /// </summary>
        public async Task<List<ValidationResult>> SearchValidationResultsAsync(ValidationSearchCriteria searchCriteria)
        {
            try
            {
                _logger.LogInformation("검수 결과를 검색합니다.");

                // Phase 2 Item #11: AsSplitQuery 적용
                var query = _context.ValidationResults
                    .Include(v => v.TargetFile)
                    .Include(v => v.StageResults)
                    .AsSplitQuery() // 카테시안 곱 방지
                    .AsQueryable();

                // 파일명 검색
                if (!string.IsNullOrEmpty(searchCriteria.FileName))
                {
                    query = query.Where(v => v.TargetFile.FileName.Contains(searchCriteria.FileName));
                }

                // 상태 검색
                if (searchCriteria.Status.HasValue)
                {
                    query = query.Where(v => v.Status == searchCriteria.Status.Value);
                }

                // 시작 일시 범위 검색
                if (searchCriteria.StartDateFrom.HasValue)
                {
                    query = query.Where(v => v.StartedAt >= searchCriteria.StartDateFrom.Value);
                }

                if (searchCriteria.StartDateTo.HasValue)
                {
                    query = query.Where(v => v.StartedAt <= searchCriteria.StartDateTo.Value);
                }

                // 오류 개수 범위 검색
                if (searchCriteria.MinErrors.HasValue)
                {
                    query = query.Where(v => v.TotalErrors >= searchCriteria.MinErrors.Value);
                }

                if (searchCriteria.MaxErrors.HasValue)
                {
                    query = query.Where(v => v.TotalErrors <= searchCriteria.MaxErrors.Value);
                }

                // 파일 형식 검색
                if (searchCriteria.FileFormat.HasValue)
                {
                    query = query.Where(v => v.TargetFile.Format == searchCriteria.FileFormat.Value);
                }

                var entities = await query
                    .OrderByDescending(v => v.StartedAt)
                    .ToListAsync();

                var results = entities.Select(e => e.ToDomainModel()).ToList();

                _logger.LogInformation("검수 결과 검색이 완료되었습니다. 검색된 개수: {Count}", results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 결과 검색 중 오류가 발생했습니다. Error: {ErrorMessage}", ex.Message);
                return new List<ValidationResult>();
            }
        }

        /// <summary>
        /// 검수 오류 배치 저장 (Phase 2 Item #12: 배치 삽입 최적화)
        /// - 1000개 단위 배치 삽입
        /// - 트랜잭션으로 원자성 보장
        /// - ChangeTracker.Clear()로 메모리 압박 방지
        /// </summary>
        public async Task<bool> SaveValidationErrorsBatchAsync(List<ValidationError> errors, string validationId)
        {
            if (errors == null || errors.Count == 0)
            {
                _logger.LogWarning("저장할 오류가 없습니다. ValidationId: {ValidationId}", validationId);
                return true;
            }

            const int BATCH_SIZE = 1000;

            try
            {
                _logger.LogInformation("검수 오류 배치 저장 시작: ValidationId={ValidationId}, Count={Count}",
                    validationId, errors.Count);

                var startTime = DateTime.Now;

                // 트랜잭션 시작
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    int totalSaved = 0;

                    // 배치 단위로 삽입
                    for (int i = 0; i < errors.Count; i += BATCH_SIZE)
                    {
                        var batch = errors.Skip(i).Take(BATCH_SIZE).ToList();

                        // ValidationError를 Entity로 변환
                        var errorEntities = batch.Select(e =>
                        {
                            var entity = ValidationErrorEntity.FromDomainModel(e);
                            entity.CheckResultId = int.Parse(validationId);
                            return entity;
                        }).ToList();

                        // 배치 추가
                        _context.ValidationErrors.AddRange(errorEntities);
                        await _context.SaveChangesAsync();

                        totalSaved += batch.Count;

                        // 메모리 압박 방지 - ChangeTracker 정리
                        _context.ChangeTracker.Clear();

                        _logger.LogDebug("배치 저장 진행: {Saved}/{Total} ({Progress:P1})",
                            totalSaved, errors.Count, (double)totalSaved / errors.Count);
                    }

                    // 트랜잭션 커밋
                    await transaction.CommitAsync();

                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    _logger.LogInformation("검수 오류 배치 저장 완료: ValidationId={ValidationId}, Count={Count}, 소요시간={Elapsed:F2}초",
                        validationId, totalSaved, elapsed);

                    return true;
                }
                catch (Exception)
                {
                    // 트랜잭션 롤백
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 오류 배치 저장 중 오류 발생: ValidationId={ValidationId}, Error={ErrorMessage}",
                    validationId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 공간정보 파일 엔티티 조회 또는 생성
        /// </summary>
        private async Task<SpatialFileInfoEntity> GetOrCreateSpatialFileEntityAsync(SpatialFileInfo spatialFileInfo)
        {
            var existingEntity = await _context.SpatialFiles
                .FirstOrDefaultAsync(f => f.FilePath == spatialFileInfo.FilePath);

            if (existingEntity != null)
            {
                // 기존 엔티티 업데이트
                existingEntity.FileName = spatialFileInfo.FileName;
                existingEntity.Format = spatialFileInfo.Format;
                existingEntity.FileSize = spatialFileInfo.FileSize;
                existingEntity.CoordinateSystem = spatialFileInfo.CoordinateSystem;
                existingEntity.ModifiedAt = spatialFileInfo.ModifiedAt ?? DateTime.Now;
                existingEntity.TablesJson = spatialFileInfo.Tables;

                return existingEntity;
            }
            else
            {
                // 새 엔티티 생성
                var newEntity = SpatialFileInfoEntity.FromDomainModel(spatialFileInfo);
                _context.SpatialFiles.Add(newEntity);
                return newEntity;
            }
        }
    }
}
