using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Data;
using Microsoft.EntityFrameworkCore;

namespace SpatialCheckPro.Extensions
{
    /// <summary>
    /// ServiceProvider 확장 메서드
    /// </summary>
    public static class ServiceProviderExtensions
    {
        /// <summary>
        /// 데이터베이스를 초기화합니다
        /// </summary>
        /// <param name="serviceProvider">서비스 프로바이더</param>
        /// <returns>초기화 성공 여부</returns>
        public static async Task<bool> InitializeDatabaseAsync(this IServiceProvider serviceProvider)
        {
            try
            {
                var logger = serviceProvider.GetService<ILogger<ValidationDbContext>>();
                
                // DbContext가 등록되어 있는지 확인
                var dbContext = serviceProvider.GetService<ValidationDbContext>();
                if (dbContext == null)
                {
                    logger?.LogWarning("ValidationDbContext가 등록되지 않았습니다. 데이터베이스 초기화를 건너뜁니다.");
                    return true; // DbContext가 없어도 애플리케이션은 실행 가능
                }

                // 데이터베이스 생성 및 마이그레이션
                await dbContext.Database.EnsureCreatedAsync();
                
                // 마이그레이션 적용
                var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    logger?.LogInformation("데이터베이스 마이그레이션을 적용합니다: {Migrations}", string.Join(", ", pendingMigrations));
                    await dbContext.Database.MigrateAsync();
                }

                // 기본 데이터 시드
                await SeedDefaultDataAsync(dbContext, logger);

                logger?.LogInformation("데이터베이스 초기화가 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetService<ILogger<ValidationDbContext>>();
                logger?.LogError(ex, "데이터베이스 초기화 중 오류가 발생했습니다");
                return false;
            }
        }

        /// <summary>
        /// 기본 데이터를 시드합니다
        /// </summary>
        /// <param name="dbContext">데이터베이스 컨텍스트</param>
        /// <param name="logger">로거</param>
        private static async Task SeedDefaultDataAsync(ValidationDbContext dbContext, ILogger? logger)
        {
            try
            {
                // 기본 설정 데이터가 없으면 추가
                if (!dbContext.ValidationConfigurations.Any())
                {
                    logger?.LogInformation("기본 검수 설정 데이터를 추가합니다.");
                    
                    // 기본 검수 설정 추가 로직은 필요시 구현
                    // 현재는 CSV 파일을 통해 설정을 관리하므로 생략
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "기본 데이터 시드 중 오류가 발생했습니다");
                throw;
            }
        }
    }
}
