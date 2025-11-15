using SpatialCheckPro.Services;
using SpatialCheckPro.Data;
using SpatialCheckPro.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace SpatialCheckPro.Extensions
{
    /// <summary>
    /// 서비스 컬렉션 확장 메서드
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 파일 처리 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddFileServices(this IServiceCollection services)
        {
            // 파일 처리 서비스 등록
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<ILargeFileProcessor, LargeFileProcessor>();
            services.AddScoped<FileAnalysisService>();

            return services;
        }

        /// <summary>
        /// CSV 설정 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddConfigServices(this IServiceCollection services)
        {
            // CSV 설정 서비스 등록
            services.AddScoped<ICsvConfigService, CsvConfigService>();

            return services;
        }

        /// <summary>
        /// 오류 처리 관련 서비스를 등록합니다 (라이브러리에서는 제거됨)
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddErrorServices(this IServiceCollection services)
        {
            // GUI 프로젝트에서 구현됨
            return services;
        }

        /// <summary>
        /// 편집 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddEditServices(this IServiceCollection services)
        {
            // 공간정보 편집 서비스 등록
            services.AddScoped<ISpatialEditService, SpatialEditService>();
            
            // 지오메트리 편집 도구 서비스는 GUI에서 구현됨

            return services;
        }

        /// <summary>
        /// 지도 뷰어 관련 서비스를 등록합니다 (라이브러리에서는 제거됨)
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddMapServices(this IServiceCollection services)
        {
            // GUI 프로젝트에서 구현됨
            return services;
        }

        /// <summary>
        /// 로깅 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddLoggingServices(this IServiceCollection services)
        {
            // Serilog 로거 설정
            var serilogLogger = LoggingService.ConfigureSerilog();
            
            // Microsoft.Extensions.Logging과 연결
            var loggerFactory = LoggingService.CreateLoggerFactory(serilogLogger);
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            
            // 전역 예외 핸들러는 GUI에서 구현됨

            return services;
        }

        /// <summary>
        /// 데이터베이스 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
        {
            // SQLite 데이터베이스 컨텍스트 등록
            services.AddDbContext<ValidationDbContext>(options =>
            {
                var connectionString = "Data Source=ValidationResults.db;Cache=Shared";
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
                options.EnableServiceProviderCaching();
            });

            services.AddDbContextFactory<ValidationDbContext>(options =>
            {
                var connectionString = "Data Source=ValidationResults.db;Cache=Shared";
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
            });

            return services;
        }

        /// <summary>
        /// 설정 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddSettingsServices(this IServiceCollection services)
        {
            // 애플리케이션 설정 서비스 등록
            services.AddSingleton<IAppSettingsService, AppSettingsService>();

            return services;
        }

        /// <summary>
        /// 검수 관련 서비스를 등록합니다 (GUI용 간소화 버전)
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddValidationServices(this IServiceCollection services)
        {
            // GUI에서는 SimpleValidationService만 사용하므로 복잡한 서비스들은 제외
            // services.AddScoped<IValidationService, ValidationService>();
            // services.AddScoped<IValidationResultService, ValidationResultService>();

            // 검수 프로세서도 GUI에서는 사용하지 않음
            // services.AddScoped<ITableCheckProcessor, TableCheckProcessor>();
            // services.AddScoped<ISchemaCheckProcessor, SchemaCheckProcessor>();
            // services.AddScoped<IGeometryCheckProcessor, GeometryCheckProcessor>();
            services.AddScoped<IRelationCheckProcessor, SpatialCheckPro.Processors.RelationCheckProcessor>();

            return services;
        }

        /// <summary>
        /// 공간 인덱스 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddSpatialIndexServices(this IServiceCollection services)
        {
            // 메모리 관리자 등록
            services.AddSingleton<IMemoryManager, MemoryManager>();
            
            // 공간 인덱스 서비스 등록
            services.AddScoped<ISpatialIndexManager, SpatialIndexManager>();
            services.AddTransient<RTreeSpatialIndex>();
            services.AddTransient<OptimizedRTreeSpatialIndex>();
            services.AddTransient<QuadTreeSpatialIndex>();
            services.AddTransient<GridSpatialIndex>();
            
            // 벤치마크 서비스 등록
            services.AddScoped<SpatialIndexBenchmark>();
            
            // 공간 관계 분석기는 향후 구현 예정
            // services.AddScoped<ISpatialRelationAnalyzer, SpatialRelationAnalyzer>();

            return services;
        }

        /// <summary>
        /// 보고서 관련 서비스를 등록합니다 (GUI용 간소화 버전)
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddReportServices(this IServiceCollection services)
        {
            // GUI에서는 ReportView에서 직접 보고서를 생성하므로 복잡한 서비스들은 제외
            // services.AddScoped<IReportService, ReportService>();
            // services.AddScoped<IHtmlReportService, HtmlReportService>();
            // services.AddScoped<IExcelReportService, ExcelReportService>();
            // services.AddScoped<IPdfReportService, PdfReportService>();

            return services;
        }

        /// <summary>
        /// 보안 관련 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddSecurityServices(this IServiceCollection services)
        {
            // 보안 서비스 등록
            services.AddScoped<IFileSecurityService, FileSecurityService>();
            services.AddScoped<IDataProtectionService, DataProtectionService>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<ISecurityMonitoringService, SecurityMonitoringService>();

            return services;
        }

        /// <summary>
        /// 공간정보 검수 시스템의 모든 핵심 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddGeoSpatialValidationServices(this IServiceCollection services)
        {
            services.AddSettingsServices();
            services.AddLoggingServices();
            // services.AddDatabaseServices(); // 임시 비활성화 (데이터베이스 없이 실행)
            services.AddFileServices();
            services.AddConfigServices();
            services.AddValidationServices();
            services.AddSpatialIndexServices();
            services.AddReportServices();
            services.AddSecurityServices();
            services.AddErrorServices();
            services.AddMapServices();
            services.AddEditServices();

            return services;
        }

        /// <summary>
        /// 모든 애플리케이션 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            return services.AddGeoSpatialValidationServices();
        }
    }
}
