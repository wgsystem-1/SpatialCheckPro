#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SpatialCheckPro.Services;
using SpatialCheckPro.Processors;
using SpatialCheckPro.GUI.Services;
using SpatialCheckPro.GUI.Views;
using SpatialCheckPro.GUI.ViewModels;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services.RemainingTime;
using SpatialCheckPro.Data;

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        public IServiceProvider? ServiceProvider => _serviceProvider;

        static App()
        {
            // 애플리케이션 시작 전에 PROJ 환경 설정
            // 한글 CSV(Excel 저장 CP949) 지원
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
            SetupProjEnvironment();
        }

        /// <summary>
        /// PROJ 라이브러리 환경을 설정합니다
        /// </summary>
        private static void SetupProjEnvironment()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var projLibPath = Path.Combine(appDir, "gdal", "share");
                
                // 시스템 PATH에서 PostgreSQL 경로 제거
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var filteredPaths = paths.Where(p => 
                    !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
                    !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase)
                ).ToArray();
                
                // PROJ 라이브러리 경로를 PATH 최우선순위로 설정
                var cleanPath = string.Join(";", filteredPaths);
                var newPath = projLibPath + ";" + appDir + ";" + cleanPath;
                Environment.SetEnvironmentVariable("PATH", newPath);
                
                // PROJ 환경 변수 설정
                Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath);
                Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath);
                Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projLibPath);
                Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
                Environment.SetEnvironmentVariable("PROJ_DEBUG", "3");
                Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", projLibPath);
                Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", projLibPath);
                
                // GDAL 환경 변수 설정
                var gdalDataPath = Path.Combine(appDir, "gdal", "data");
                Environment.SetEnvironmentVariable("GDAL_DATA", gdalDataPath);
                
                // 콘솔 출력 제거
            }
            catch (Exception)
            {
                // 콘솔 출력 제거
            }
        }

        /// <summary>
        /// 서비스 컨테이너 구성
        /// </summary>
        private void ConfigureServices()
        {
            string debugLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
            
            try
            {
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ConfigureServices 내부 시작\n");
                var services = new ServiceCollection();

                // 로그 디렉토리 자동 생성 및 로깅 설정
                EnsureLogDirectoryExists();
                services.AddLogging(builder =>
                {
                    // 콘솔 로거 제거 (파일/Debug 로거만 사용)
                    builder.AddDebug();
                    builder.AddProvider(new FileLoggerProvider());
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

                // 기본 서비스들 (의존성 없음)
                services.AddSingleton<CsvConfigService>();
                
                // 데이터베이스 관련 서비스 등록 (다른 서비스들이 필요로 함)
                services.AddSingleton<IAppSettingsService, AppSettingsService>();
                
                // 지오메트리 검수 설정 분석 서비스 등록
                services.AddSingleton<GeometryConfigAnalysisService>();
                
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
                
                // ValidationDbContext 자체도 등록 (다른 서비스에서 필요할 수 있음)
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
                
            // 성능 및 병렬 처리 관련 서비스들 먼저 등록
            services.AddSingleton<SystemResourceAnalyzer>();
            services.AddSingleton<SpatialCheckPro.Models.Config.PerformanceSettings>();
            services.AddSingleton<CentralizedResourceMonitor>();
            services.AddSingleton<ParallelProcessingManager>();
            services.AddSingleton<ParallelPerformanceMonitor>();
            services.AddSingleton<AdvancedParallelProcessingManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<AdvancedParallelProcessingManager>>();
                var resourceMonitor = provider.GetRequiredService<CentralizedResourceMonitor>();
                var settings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
                var memoryOptimization = provider.GetRequiredService<MemoryOptimizationService>();
                var performanceMonitor = provider.GetRequiredService<ParallelPerformanceMonitor>();
                return new AdvancedParallelProcessingManager(logger, resourceMonitor, settings, memoryOptimization, performanceMonitor);
            });
        services.AddSingleton<StageParallelProcessingManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<StageParallelProcessingManager>>();
            var advancedParallelProcessingManager = provider.GetRequiredService<AdvancedParallelProcessingManager>();
            var settings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
            return new StageParallelProcessingManager(logger, advancedParallelProcessingManager, settings);
        });
        services.AddSingleton<PerformanceBenchmarkService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<PerformanceBenchmarkService>>();
            var performanceMonitor = provider.GetRequiredService<ParallelPerformanceMonitor>();
            var resourceAnalyzer = provider.GetRequiredService<SystemResourceAnalyzer>();
            var memoryOptimization = provider.GetRequiredService<MemoryOptimizationService>();
            return new PerformanceBenchmarkService(logger, performanceMonitor, resourceAnalyzer, memoryOptimization);
        });
        services.AddSingleton<MemoryOptimizationService>();
        services.AddSingleton<SpatialIndexService>();
        services.AddSingleton<StreamingGeometryProcessor>();
        services.AddSingleton<HighPerformanceGeometryValidator>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<HighPerformanceGeometryValidator>>();
            var spatialIndexService = provider.GetRequiredService<SpatialIndexService>();
            var memoryOptimization = provider.GetRequiredService<MemoryOptimizationService>();
            var parallelProcessingManager = provider.GetRequiredService<ParallelProcessingManager>();
            var settings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
            return new HighPerformanceGeometryValidator(logger, spatialIndexService, memoryOptimization, parallelProcessingManager, settings);
        });
                
                // GDAL 관련 서비스들을 먼저 등록
                services.AddSingleton<GdalInitializationService>();
                services.AddSingleton<GdalDataAnalysisService>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<GdalDataAnalysisService>>();
                    var dataSourcePool = provider.GetRequiredService<IDataSourcePool>(); // DataSourcePool 주입
                    var cacheService = provider.GetRequiredService<IDataCacheService>(); // Cache 주입
                    return new GdalDataAnalysisService(logger, dataSourcePool, cacheService);
                });
            services.AddSingleton<GeometryValidationService>();
                
                // FGDB 관련 서비스들 먼저 등록
                services.AddSingleton<FgdbSchemaService>();
                services.AddSingleton<QcErrorDataService>();
                services.AddSingleton<ValidationResultConverter>();
                services.AddSingleton<ShapefileToFgdbMigrationService>();
                services.AddSingleton<QcErrorIntegrationService>();
                services.AddSingleton<SchemaExtractionService>();
                services.AddSingleton<RelationErrorsIntegrator>();
                
                // 새로 추가된 서비스들 등록
                services.AddSingleton<IUniqueKeyValidator, BasicUniqueKeyValidator>();
                services.AddSingleton<IForeignKeyValidator, BasicForeignKeyValidator>();
                services.AddSingleton<ValidationHistoryService>();
                
                // 이제 SchemaValidationService 등록 가능
                services.AddSingleton<SchemaValidationService>();
                
                // 관계 검수 프로세서 등록
                services.AddSingleton<IRelationCheckProcessor>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<RelationCheckProcessor>>();
                    var parallelProcessingManager = provider.GetRequiredService<ParallelProcessingManager>();
                    var performanceSettings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
                    var streamingProcessor = provider.GetRequiredService<StreamingGeometryProcessor>();
                    return new RelationCheckProcessor(logger, parallelProcessingManager, performanceSettings, streamingProcessor);
                });
                
                // 보고서 생성 서비스 등록 (Excel 제거)
                services.AddSingleton<PdfReportService>();
                services.AddSingleton<IReportService, ReportService>();
                
                // 속성 검수 프로세서 등록
                services.AddSingleton<IAttributeCheckProcessor, AttributeCheckProcessor>();
                
                // DataSourcePool 등록 (누락된 서비스)
                services.AddSingleton<IDataSourcePool, DataSourcePool>();
                services.AddSingleton<IDataCacheService, DataCacheService>(); // 데이터 캐시 서비스 등록
                services.AddSingleton<GdbToSqliteConverter>(); // 변환 서비스 등록
                services.AddSingleton<GdbDataProvider>(); // GDB 데이터 제공자 등록
                services.AddSingleton<SqliteDataProvider>(); // SQLite 데이터 제공자 등록
                
                // QcStoragePathService 등록 (QcErrorService 이전에 등록)
                services.AddSingleton<QcStoragePathService>();

                // 뷰모델 등록
                services.AddSingleton<IRemainingTimeEstimator, AdaptiveRemainingTimeEstimator>();
                services.AddSingleton<StageSummaryCollectionViewModel>();
                services.AddSingleton<AlertAggregationService>();

                // QcErrorService 등록 (의존하는 서비스들 이후에 등록)
                services.AddSingleton<QcErrorService>();
                
                // 고도화된 테이블 검수 서비스 등록 (SimpleValidationService보다 먼저 등록)
                services.AddSingleton<AdvancedTableCheckService>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<AdvancedTableCheckService>>();
                    var gdalService = provider.GetRequiredService<GdalDataAnalysisService>();
                    var parallelProcessingManager = provider.GetRequiredService<ParallelProcessingManager>();
                    var performanceSettings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
                    return new AdvancedTableCheckService(logger, gdalService, parallelProcessingManager, performanceSettings);
                });
                
                // SimpleValidationService를 팩토리로 등록 (모든 서비스 포함)
                services.AddSingleton<SimpleValidationService>(provider =>
                {
                    try
                    {
                        // IServiceProvider를 사용하여 필요한 모든 서비스를 가져옵니다.
                        var logger = provider.GetRequiredService<ILogger<SimpleValidationService>>();
                        var csvService = provider.GetRequiredService<CsvConfigService>();
                        var gdalService = provider.GetRequiredService<GdalDataAnalysisService>();
                        var geometryService = provider.GetRequiredService<GeometryValidationService>();
                        var schemaService = provider.GetRequiredService<SchemaValidationService>();
                        var qcErrorService = provider.GetRequiredService<QcErrorService>();
                        var advancedTableCheckService = provider.GetRequiredService<AdvancedTableCheckService>();
                        var relationProcessor = provider.GetRequiredService<IRelationCheckProcessor>();
                        var attributeCheckProcessor = provider.GetRequiredService<IAttributeCheckProcessor>();
                        var relationErrorsIntegrator = provider.GetRequiredService<RelationErrorsIntegrator>();
                        var dataSourcePool = provider.GetRequiredService<IDataSourcePool>();
                        var gdbToSqliteConverter = provider.GetRequiredService<GdbToSqliteConverter>();
                        var parallelProcessingManager = provider.GetRequiredService<ParallelProcessingManager>();
                        var advancedParallelProcessingManager = provider.GetRequiredService<AdvancedParallelProcessingManager>();
                        var resourceMonitor = provider.GetRequiredService<CentralizedResourceMonitor>();
                        var performanceSettings = provider.GetRequiredService<SpatialCheckPro.Models.Config.PerformanceSettings>();
                        var stageParallelProcessingManager = provider.GetRequiredService<StageParallelProcessingManager>();
                        var performanceMonitor = provider.GetRequiredService<ParallelPerformanceMonitor>();
                        var validationResultConverter = provider.GetRequiredService<ValidationResultConverter>();

                        // IServiceProvider 자체도 주입합니다.
                        return new SimpleValidationService(logger, csvService, gdalService, geometryService, schemaService, 
                            qcErrorService, advancedTableCheckService, relationProcessor, attributeCheckProcessor, 
                            relationErrorsIntegrator, dataSourcePool, provider, gdbToSqliteConverter, 
                            parallelProcessingManager, advancedParallelProcessingManager, resourceMonitor, 
                            performanceSettings, stageParallelProcessingManager, performanceMonitor,
                            validationResultConverter);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SimpleValidationService 생성 실패: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"SimpleValidationService 생성 실패: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
                            System.Diagnostics.Debug.WriteLine($"InnerException StackTrace: {ex.InnerException.StackTrace}");
                        }
                        throw;
                    }
                });

                // 디버깅용 GDB 분석기 등록 제거

                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceProvider 빌드 시작\n");
                _serviceProvider = services.BuildServiceProvider();
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceProvider 빌드 완료\n");
                // 콘솔 출력 제거

                try
                {
                    // 워밍업: 앱 시작 시 1회 리소스 분석 캐시 생성
                    var resourceMonitor = _serviceProvider.GetService<CentralizedResourceMonitor>();
                    resourceMonitor?.GetResourceInfo("AppStartupWarmup", forceRefresh: true);
                }
                catch { }
            }
            catch (Exception ex)
            {
                // 디버그 출력으로 예외 확인
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ConfigureServices 예외: {ex.Message}\n{ex.StackTrace}\n");
                System.Diagnostics.Debug.WriteLine($"ConfigureServices 예외 발생: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                
                // 최소한의 서비스만으로 구성
                var services = new ServiceCollection();
                services.AddLogging(builder => { /* 콘솔 로거 제거 */ });
                _serviceProvider = services.BuildServiceProvider();
            }
        }

        /// <summary>
        /// 로그 디렉토리 자동 생성
        /// </summary>
        private void EnsureLogDirectoryExists()
        {
            try
            {
                // 실행 파일 디렉토리 기준으로 로그 디렉토리 경로 설정
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var logDirectory = Path.Combine(appDirectory, "Logs");
                
                // 로그 디렉토리가 없으면 생성
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                    Console.WriteLine($"로그 디렉토리 생성됨: {logDirectory}");
                }
                else
                {
                    Console.WriteLine($"로그 디렉토리 확인됨: {logDirectory}");
                }
                
                // debug.log 파일 경로도 확인
                var debugLogPath = Path.Combine(appDirectory, "debug.log");
                Console.WriteLine($"디버그 로그 파일 경로: {debugLogPath}");
                
                // 기존 debug.log 파일 삭제 (새로운 실행마다 깨끗하게 시작)
                try
                {
                    if (File.Exists(debugLogPath))
                    {
                        File.Delete(debugLogPath);
                        Console.WriteLine("기존 debug.log 파일 삭제됨");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"기존 debug.log 파일 삭제 실패: {ex.Message}");
                    // 파일이 사용 중일 수 있으므로 무시
                }
                
                // 새로운 로그 파일 생성
                File.WriteAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Information] 로그 파일 생성됨{Environment.NewLine}");
                Console.WriteLine("새로운 debug.log 파일 생성됨");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"로그 디렉토리 생성 실패: {ex.Message}");
                // 로그 디렉토리 생성 실패해도 애플리케이션은 계속 실행
            }
        }

        /// <summary>
        /// 서비스 인스턴스 가져오기 (안전한 버전)
        /// </summary>
        public T? GetService<T>() where T : class
        {
            try
            {
                return _serviceProvider?.GetService<T>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"서비스 {typeof(T).Name} 가져오기 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 필수 서비스 인스턴스 가져오기 (예외 발생)
        /// </summary>
        public T GetRequiredService<T>() where T : class
        {
            return _serviceProvider?.GetService<T>() ?? throw new InvalidOperationException($"필수 서비스 {typeof(T).Name}을 찾을 수 없습니다.");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 초기화 디버깅을 위한 로그 파일 생성
            string debugLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
            
            try
            {
                File.WriteAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SpatialCheckPro 시작\n");
                
                // 콘솔 출력 제거
                
                // PROJ 라이브러리 경로 설정 (PostgreSQL PostGIS와의 충돌 방지)
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var projLibPath = System.IO.Path.Combine(appDir, "gdal", "share");
                if (System.IO.Directory.Exists(projLibPath))
                {
                    // 시스템 환경 변수에서 PostgreSQL 경로 제거
                    var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
                    var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var filteredPaths = paths.Where(p => !p.Contains("PostgreSQL") && !p.Contains("postgis")).ToArray();
                    Environment.SetEnvironmentVariable("PATH", string.Join(";", filteredPaths), EnvironmentVariableTarget.Process);
                    
                    // PROJ 관련 환경 변수 설정 (더 강력하게)
                    Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projLibPath, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF", EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("PROJ_DEBUG", "0", EnvironmentVariableTarget.Process);
                    
                    // 추가 PROJ 환경 변수 설정
                    Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", projLibPath, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", projLibPath, EnvironmentVariableTarget.Process);
                    
                    // 콘솔 출력 제거
                }
                
                // 서비스 컨테이너 구성
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ConfigureServices 시작\n");
                ConfigureServices();
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ConfigureServices 완료\n");
                
                // 로그 시스템 확인
                var logger = GetService<ILogger<App>>();
                if (logger != null)
                {
                    logger.LogInformation("애플리케이션 시작됨");
                    logger.LogInformation("로그 시스템 정상 작동 확인");
                }
                
                // GDAL 초기화를 백그라운드에서 지연 실행 (애플리케이션 시작 속도 향상)
                // 콘솔 출력 제거
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var gdalService = GetService<GdalInitializationService>();
                        if (gdalService != null)
                        {
                            // 백그라운드에서 GDAL 초기화 (60초 타임아웃)
                            await Task.Run(() => gdalService.Initialize());
                            if (gdalService.IsInitialized)
                            {
                                logger?.LogInformation("GDAL 초기화 완료 (백그라운드)");
                                // 콘솔 출력 제거
                            }
                            else
                            {
                                logger?.LogWarning("GDAL 초기화 실패 (백그라운드)");
                                // 콘솔 출력 제거
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "GDAL 초기화 중 오류 발생 (백그라운드)");
                        // 콘솔 출력 제거
                    }
                });
                
                // 콘솔 출력 제거
                
                // MainWindow 생성 및 표시
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow 생성 시작\n");
                var mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow 표시 완료\n");
                
                // 명령줄 인자에 GDB 경로가 전달된 경우 자동 검수 시작
                try
                {
                    if (e.Args != null && e.Args.Length > 0)
                    {
                        var argPath = e.Args[0];
                        if (!string.IsNullOrWhiteSpace(argPath))
                        {
                            logger?.LogInformation("명령줄 인자 수신: {Arg}", argPath);
                            // UI 스레드에서 비동기 실행
                            mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
                            {
                                try
                                {
                                    await mainWindow.StartValidationForPathAsync(argPath);
                                }
                                catch (Exception ex)
                                {
                                    logger?.LogError(ex, "자동 검수 시작 중 오류");
                                }
                            }));
                        }
                    }
                }
                catch { }
                
                // 기본 OnStartup 호출
                base.OnStartup(e);
                
                // 콘솔 출력 제거
            }
            catch (Exception ex)
            {
                File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OnStartup 예외: {ex.Message}\n{ex.StackTrace}\n");
                // 콘솔 출력 제거
                MessageBox.Show($"애플리케이션 시작 중 오류가 발생했습니다.\n\n{ex.Message}", 
                    "시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var message = "처리되지 않은 예외가 발생했습니다." + Environment.NewLine + Environment.NewLine + 
                         "오류: " + e.Exception.Message + Environment.NewLine + Environment.NewLine +
                         "스택 트레이스:" + Environment.NewLine + e.Exception.StackTrace;
                         
            MessageBox.Show(message, "예외 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}