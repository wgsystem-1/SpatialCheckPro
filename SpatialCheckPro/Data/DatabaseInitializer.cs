using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 데이터베이스 초기화를 담당하는 클래스
    /// </summary>
    public class DatabaseInitializer
    {
        private readonly ValidationDbContext _context;
        private readonly ILogger<DatabaseInitializer>? _logger;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="context">데이터베이스 컨텍스트</param>
        /// <param name="logger">로거 (선택적)</param>
        public DatabaseInitializer(ValidationDbContext context, ILogger<DatabaseInitializer>? logger = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger;
        }

        /// <summary>
        /// 데이터베이스 초기화 (비동기)
        /// </summary>
        /// <returns>초기화 성공 여부</returns>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger?.LogInformation("데이터베이스 초기화를 시작합니다.");

                // 데이터베이스가 존재하지 않으면 생성
                var created = await _context.Database.EnsureCreatedAsync();
                
                if (created)
                {
                    _logger?.LogInformation("새로운 데이터베이스가 생성되었습니다.");
                    
                    // 초기 데이터 삽입 (필요한 경우)
                    await SeedInitialDataAsync();
                }
                else
                {
                    _logger?.LogInformation("기존 데이터베이스를 사용합니다.");
                    
                    // 마이그레이션 적용
                    await ApplyMigrationsAsync();
                }

                _logger?.LogInformation("데이터베이스 초기화가 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "데이터베이스 초기화 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 마이그레이션 적용 (비동기)
        /// </summary>
        /// <returns>적용 성공 여부</returns>
        public async Task<bool> ApplyMigrationsAsync()
        {
            try
            {
                var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
                
                if (pendingMigrations.Any())
                {
                    _logger?.LogInformation("대기 중인 마이그레이션을 적용합니다: {Migrations}", 
                        string.Join(", ", pendingMigrations));
                    
                    await _context.Database.MigrateAsync();
                    
                    _logger?.LogInformation("마이그레이션이 성공적으로 적용되었습니다.");
                }
                else
                {
                    _logger?.LogInformation("적용할 마이그레이션이 없습니다.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "마이그레이션 적용 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 연결 테스트
        /// </summary>
        /// <returns>연결 성공 여부</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger?.LogInformation("데이터베이스 연결을 테스트합니다.");
                
                var canConnect = await _context.Database.CanConnectAsync();
                
                if (canConnect)
                {
                    _logger?.LogInformation("데이터베이스 연결이 성공했습니다.");
                }
                else
                {
                    _logger?.LogWarning("데이터베이스에 연결할 수 없습니다.");
                }

                return canConnect;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "데이터베이스 연결 테스트 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 정보 조회
        /// </summary>
        /// <returns>데이터베이스 정보</returns>
        public async Task<DatabaseInfo> GetDatabaseInfoAsync()
        {
            try
            {
                var info = new DatabaseInfo();

                // 데이터베이스 연결 문자열
                info.ConnectionString = _context.Database.GetConnectionString() ?? "알 수 없음";

                // 데이터베이스 제공자
                info.Provider = _context.Database.ProviderName ?? "알 수 없음";

                // 테이블 개수 조회
                info.ValidationResultsCount = await _context.ValidationResults.CountAsync();
                info.SpatialFilesCount = await _context.SpatialFiles.CountAsync();

                // 마지막 검수 일시
                var lastValidation = await _context.ValidationResults
                    .OrderByDescending(v => v.StartedAt)
                    .FirstOrDefaultAsync();
                
                info.LastValidationDate = lastValidation?.StartedAt;

                return info;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "데이터베이스 정보 조회 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                return new DatabaseInfo { ConnectionString = "오류 발생" };
            }
        }

        /// <summary>
        /// 초기 데이터 삽입
        /// </summary>
        private async Task SeedInitialDataAsync()
        {
            try
            {
                _logger?.LogInformation("초기 데이터를 삽입합니다.");

                // 필요한 경우 초기 설정 데이터나 마스터 데이터를 여기에 추가
                // 현재는 검수 결과만 저장하므로 특별한 초기 데이터는 없음

                await _context.SaveChangesAsync();
                
                _logger?.LogInformation("초기 데이터 삽입이 완료되었습니다.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "초기 데이터 삽입 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 데이터베이스 정리 (개발/테스트 용도)
        /// </summary>
        /// <returns>정리 성공 여부</returns>
        public async Task<bool> CleanupDatabaseAsync()
        {
            try
            {
                _logger?.LogWarning("데이터베이스 정리를 시작합니다. 모든 데이터가 삭제됩니다.");

                // 모든 테이블의 데이터 삭제 (외래키 제약조건 순서 고려)
                _context.ValidationErrors.RemoveRange(_context.ValidationErrors);
                _context.CheckResults.RemoveRange(_context.CheckResults);
                _context.StageResults.RemoveRange(_context.StageResults);
                _context.ValidationResults.RemoveRange(_context.ValidationResults);
                _context.SpatialFiles.RemoveRange(_context.SpatialFiles);

                await _context.SaveChangesAsync();

                _logger?.LogWarning("데이터베이스 정리가 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "데이터베이스 정리 중 오류가 발생했습니다: {ErrorMessage}", ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// 데이터베이스 정보 클래스
    /// </summary>
    public class DatabaseInfo
    {
        /// <summary>
        /// 연결 문자열
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 데이터베이스 제공자
        /// </summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과 개수
        /// </summary>
        public int ValidationResultsCount { get; set; }

        /// <summary>
        /// 공간정보 파일 개수
        /// </summary>
        public int SpatialFilesCount { get; set; }

        /// <summary>
        /// 마지막 검수 일시
        /// </summary>
        public DateTime? LastValidationDate { get; set; }
    }
}
