using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SpatialCheckPro.Data;
using SpatialCheckPro.Services.Interfaces;
using SpatialCheckPro.Services;
using SpatialCheckPro.Services.RemainingTime;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.GUI.Views;
using SpatialCheckPro.GUI.ViewModels;
using SpatialCheckPro.Processors;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 의존성 주입 설정을 체계적으로 관리하는 클래스
    /// </summary>
    public static class DependencyInjectionConfigurator
    {
        /// <summary>
        /// 모든 서비스를 올바른 순서로 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        public static void ConfigureServices(IServiceCollection services)
        {
            // 1단계: 기본 설정 및 로깅
            ConfigureBasicServices(services);
            
            // 2단계: 설정 모델들
            ConfigureConfigurationModels(services);
            
            // 3단계: 데이터베이스 서비스
            ConfigureDatabaseServices(services);
            
            // 4단계: 핵심 비즈니스 서비스
            ConfigureCoreServices(services);
            
            // 5단계: 검수 관련 서비스
            ConfigureValidationServices(services);
            
            // 6단계: 성능 및 최적화 서비스
            ConfigurePerformanceServices(services);
            
            // 7단계: GUI 서비스들
            ConfigureGUIServices(services);
        }

        /// <summary>
        /// 기본 서비스들 등록 (로깅, 기본 설정 등)
        /// </summary>
        private static void ConfigureBasicServices(IServiceCollection services)
        {
            // 로깅 서비스 등록
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                // 파일 로거(UTF-8) 추가
                builder.AddProvider(new FileLoggerProvider());
                builder.SetMinimumLevel(LogLevel.Information);
            });
        }

        /// <summary>
        /// 설정 모델들 등록
        /// </summary>
        private static void ConfigureConfigurationModels(IServiceCollection services)
        {
            // 설정 팩토리 등록
            services.AddConfigurationFactory();
            
            // 성능 설정 모델 등록 (팩토리를 통해 생성)
            services.AddSingleton<PerformanceSettings>(serviceProvider =>
            {
                var factory = serviceProvider.GetRequiredService<IConfigurationFactory>();
                return factory.CreateDefaultPerformanceSettings();
            });
        }

        /// <summary>
        /// 데이터베이스 관련 서비스 등록
        /// </summary>
        private static void ConfigureDatabaseServices(IServiceCollection services)
        {
            // 애플리케이션 설정 서비스 등록 (데이터베이스 설정에 필요)
            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            
            // 데이터베이스 컨텍스트 팩토리 등록
            services.AddDbContextFactory<ValidationDbContext>((serviceProvider, options) =>
            {
                var appSettingsService = serviceProvider.GetRequiredService<IAppSettingsService>();
                var databaseSettings = appSettingsService.LoadSettings().Database;
                
                options.UseSqlite(databaseSettings.ConnectionString);
                if (databaseSettings.EnableSensitiveDataLogging)
                {
                    options.EnableSensitiveDataLogging();
                }
                options.EnableServiceProviderCaching();
            });
            
            // ValidationDbContext 자체도 등록
            services.AddDbContext<ValidationDbContext>((serviceProvider, options) =>
            {
                var appSettingsService = serviceProvider.GetRequiredService<IAppSettingsService>();
                var databaseSettings = appSettingsService.LoadSettings().Database;
                
                options.UseSqlite(databaseSettings.ConnectionString);
                if (databaseSettings.EnableSensitiveDataLogging)
                {
                    options.EnableSensitiveDataLogging();
                }
            });
            
            // 남은 시간 추정기 등록
            services.AddSingleton<IRemainingTimeEstimator, AdaptiveRemainingTimeEstimator>();
        }

        /// <summary>
        /// 핵심 비즈니스 서비스들 등록
        /// </summary>
        private static void ConfigureCoreServices(IServiceCollection services)
        {
            // CSV 설정 서비스
            services.AddSingleton<CsvConfigService>();
            
            // 지오메트리 검수 설정 분석 서비스
            services.AddSingleton<GeometryConfigAnalysisService>();
            
            // 오류 개수 분석 서비스
            services.AddSingleton<IErrorCountAnalysisService, ErrorCountAnalysisService>();
            
            // GDAL 데이터 분석 서비스
            services.AddSingleton<GdalDataAnalysisService>();
            
            // 공간 인덱스 서비스
            services.AddSingleton<SpatialIndexService>();
            
            // QC 오류 관련 서비스들
            services.AddSingleton<QcErrorDataService>();
            services.AddSingleton<QcErrorService>();
            services.AddSingleton<QcStoragePathService>();
            // QGIS 프로젝트 생성기 제거
            
            // 스키마 서비스
            services.AddSingleton<FgdbSchemaService>();
            
            // 검수 히스토리 서비스
            services.AddSingleton<ValidationHistoryService>();
            
            // 스키마 검증 서비스
            services.AddSingleton<SchemaValidationService>();
            
            // 고급 테이블 검수 서비스
            services.AddSingleton<AdvancedTableCheckService>();
            
            // 관계 오류 통합 서비스
            services.AddSingleton<RelationErrorsIntegrator>();
            
            // 데이터 소스 풀 및 캐시 서비스
            services.AddSingleton<IDataSourcePool, DataSourcePool>();
            services.AddSingleton<IDataCacheService, DataCacheService>();
            
            // 데이터 프로바이더 등록
            services.AddTransient<GdbDataProvider>();
            services.AddTransient<SqliteDataProvider>();
            
            // GDB to SQLite 변환기
            services.AddSingleton<GdbToSqliteConverter>();
            
            // 유니크 키 및 외래 키 검증기
            services.AddSingleton<IUniqueKeyValidator, BasicUniqueKeyValidator>();
            services.AddSingleton<IForeignKeyValidator, BasicForeignKeyValidator>();
            
            // 검수 결과 변환기
            services.AddSingleton<ValidationResultConverter>();
            
            // 지오메트리 검증 서비스
            services.AddSingleton<GeometryValidationService>();
            
            // 검수 프로세서들
            services.AddSingleton<IRelationCheckProcessor, RelationCheckProcessor>();
            services.AddSingleton<IAttributeCheckProcessor, AttributeCheckProcessor>();
            
            // 간단한 검수 서비스
            services.AddSingleton<SimpleValidationService>();
        }

        /// <summary>
        /// 검수 관련 서비스들 등록
        /// </summary>
        private static void ConfigureValidationServices(IServiceCollection services)
        {
            // 검수 관련 서비스들은 이미 핵심 서비스에서 등록됨
            // 추가적인 검수 관련 서비스가 있다면 여기에 등록
        }

        /// <summary>
        /// 성능 및 최적화 서비스들 등록
        /// </summary>
        private static void ConfigurePerformanceServices(IServiceCollection services)
        {
            // 시스템 리소스 분석기 (다른 성능 서비스들보다 먼저 등록)
            services.AddSingleton<SystemResourceAnalyzer>();
            
            // 중앙집중식 리소스 모니터
            services.AddSingleton<CentralizedResourceMonitor>();
            
            // 메모리 최적화 서비스
            services.AddSingleton<MemoryOptimizationService>();
            
            // 병렬 처리 관리자들
            services.AddSingleton<ParallelProcessingManager>();
            services.AddSingleton<AdvancedParallelProcessingManager>();
            services.AddSingleton<StageParallelProcessingManager>();
            
            // 병렬 성능 모니터
            services.AddSingleton<ParallelPerformanceMonitor>();
            
            // 고성능 지오메트리 검증기
            services.AddSingleton<HighPerformanceGeometryValidator>();
        }

        /// <summary>
        /// GUI 관련 서비스들 등록
        /// </summary>
        private static void ConfigureGUIServices(IServiceCollection services)
        {
            // 알림 집계 서비스
            services.AddSingleton<AlertAggregationService>();
            
            // GUI 뷰들
            services.AddSingleton<MainWindow>();
            services.AddSingleton<StageSummaryCollectionViewModel>();
            services.AddSingleton<ValidationResultView>();
            services.AddSingleton<ValidationSettingsWindow>();
        }
    }
}
