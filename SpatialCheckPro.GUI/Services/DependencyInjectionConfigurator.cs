using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SpatialCheckPro.Data;
using SpatialCheckPro.Services;
using SpatialCheckPro.Services.RemainingTime;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.GUI.Views;
using SpatialCheckPro.GUI.ViewModels;
using SpatialCheckPro.Processors;
using SpatialCheckPro.Models.Config;
using System.Runtime.Versioning;

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
        [SupportedOSPlatform("windows7.0")]
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
            
            // 지오메트리 검수 기준 등록 (geometry_criteria.csv 파일에서 로드)
            services.AddSingleton<SpatialCheckPro.Models.GeometryCriteria>(serviceProvider =>
            {
                try
                {
                    // Config 디렉토리 경로 결정
                    var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var configDirectory = Path.Combine(baseDirectory, "Config");
                    
                    // 여러 경로 시도 (개발 환경 및 배포 환경 대응)
                    var possiblePaths = new[]
                    {
                        Path.Combine(baseDirectory, "Config"),
                        Path.Combine(baseDirectory, "..", "..", "..", "SpatialCheckPro", "Config"),
                        Path.Combine(baseDirectory, "..", "..", "..", "Config")
                    };

                    string? csvFilePath = null;
                    foreach (var path in possiblePaths)
                    {
                        var fullPath = Path.GetFullPath(path);
                        var testFile = Path.Combine(fullPath, "geometry_criteria.csv");
                        if (File.Exists(testFile))
                        {
                            csvFilePath = testFile;
                            break;
                        }
                    }

                    // 기본 경로 사용
                    if (csvFilePath == null)
                    {
                        csvFilePath = Path.Combine(configDirectory, "geometry_criteria.csv");
                    }

                    // CSV 파일에서 로드 시도 (동기 버전 사용 - UI 스레드 데드락 방지)
                    if (File.Exists(csvFilePath))
                    {
                        try
                        {
                            // 동기 방식으로 로드 (DI 등록 시점에는 async 사용 불가, 데드락 방지)
                            var criteria = SpatialCheckPro.Models.GeometryCriteria.LoadFromCsv(csvFilePath);
                            
                            // 로그 기록 (정적 클래스이므로 System.Diagnostics 사용)
                            System.Diagnostics.Debug.WriteLine($"[DependencyInjectionConfigurator] geometry_criteria.csv 파일에서 지오메트리 검수 기준 로드 완료: {csvFilePath}");
                            
                            return criteria;
                        }
                        catch (Exception ex)
                        {
                            // 로드 실패 시 기본값 사용 및 경고 로그
                            System.Diagnostics.Debug.WriteLine($"[DependencyInjectionConfigurator] geometry_criteria.csv 파일 로드 실패, 기본값 사용: {csvFilePath}, 오류: {ex.Message}");
                            return SpatialCheckPro.Models.GeometryCriteria.CreateDefault();
                        }
                    }
                    else
                    {
                        // 파일이 없으면 기본값 사용 및 정보 로그
                        System.Diagnostics.Debug.WriteLine($"[DependencyInjectionConfigurator] geometry_criteria.csv 파일이 없어 기본값 사용: {csvFilePath}");
                        return SpatialCheckPro.Models.GeometryCriteria.CreateDefault();
                    }
                }
                catch (Exception ex)
                {
                    // 예외 발생 시 기본값 사용
                    System.Diagnostics.Debug.WriteLine($"[DependencyInjectionConfigurator] 지오메트리 검수 기준 초기화 중 오류 발생, 기본값 사용: {ex.Message}");
                    return SpatialCheckPro.Models.GeometryCriteria.CreateDefault();
                }
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
            
            // 검수 메트릭 수집기 등록
            services.AddSingleton<ValidationMetricsCollector>();
            
            // QC_errors 경로 관리자 등록
            services.AddSingleton<QcErrorsPathManager>();
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
            
            // GDAL 데이터 분석 서비스
            services.AddSingleton<GdalDataAnalysisService>();
            
            // 공간 인덱스 서비스
            services.AddSingleton<SpatialIndexService>();
            
            // 피처 필터 서비스
            services.AddSingleton<IFeatureFilterService, FeatureFilterService>();

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

            // *** 대용량 파일 처리 서비스 ***
            services.AddSingleton<SpatialCheckPro.Services.ILargeFileProcessor, SpatialCheckPro.Services.LargeFileProcessor>();

            // *** FileAnalysisService는 DI 컨테이너에 등록하지 않고 직접 생성하여 순환 참조 방지 ***
            
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

            // 지오메트리 검수 프로세서
            services.AddSingleton<IGeometryCheckProcessor, GeometryCheckProcessor>();
        }

        /// <summary>
        /// 성능 및 최적화 서비스들 등록
        /// </summary>
        private static void ConfigurePerformanceServices(IServiceCollection services)
        {
            // 메모리 최적화 서비스
            services.AddSingleton<MemoryOptimizationService>();
            
            // 병렬 처리 관리자들
            services.AddSingleton<ParallelProcessingManager>();
            
            // 고성능 지오메트리 검증기
            services.AddSingleton<HighPerformanceGeometryValidator>();
        }

        /// <summary>
        /// GUI 관련 서비스들 등록
        /// </summary>
        [SupportedOSPlatform("windows7.0")]
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
