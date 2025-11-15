using System.Windows;
using System.Text;
using System;
using System.Runtime.InteropServices;
using System.Linq;
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
using Microsoft.Extensions.Options;

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        private ServiceProvider? _serviceProvider;
        
        public ServiceProvider? ServiceProvider => _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 콘솔 창 할당 (디버그용)
            try
            {
                AllocConsole();
                System.Console.Title = "SpatialCheckPro - 디버그 콘솔";
                System.Console.WriteLine("=".PadRight(80, '='));
                System.Console.WriteLine("SpatialCheckPro 디버그 콘솔");
                System.Console.WriteLine($"시작 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                System.Console.WriteLine("=".PadRight(80, '='));
                System.Console.WriteLine();
            }
            catch { } // 이미 콘솔이 할당된 경우 무시
            
            string appLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");

            try
            {
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App OnStartup 시작\n");
                System.Console.WriteLine("[로그] 로그 파일 초기화 완료");
                
                // 콘솔 출력 인코딩을 UTF-8로 고정 (한글 깨짐 방지)
                try
                {
                    System.Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    System.Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                }
                catch { }

                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceCollection 생성\n");
                var serviceCollection = new ServiceCollection();
                
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ConfigureServices 호출\n");
                ConfigureServices(serviceCollection);
                
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceProvider 빌드 시작\n");

                // 서비스 등록 검증
                try
                {
                    var largeFileProcessorType = typeof(SpatialCheckPro.Services.ILargeFileProcessor);
                    var fileAnalysisServiceType = typeof(SpatialCheckPro.Services.FileAnalysisService);
                    System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ILargeFileProcessor 타입: {largeFileProcessorType}\n");
                    System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FileAnalysisService 타입: {fileAnalysisServiceType}\n");
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 타입 확인 중 예외: {ex.Message}\n");
                }

                _serviceProvider = serviceCollection.BuildServiceProvider();
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceProvider 빌드 완료\n");

                // 등록된 서비스 확인
                try
                {
                    var services = serviceCollection.ToList();
                    System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 등록된 서비스 수: {services.Count}\n");

                    foreach (var service in services)
                    {
                        System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 서비스: {service.ServiceType} -> {service.ImplementationType}\n");
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 서비스 목록 조회 실패: {ex.Message}\n");
                }

                // 의존성 주입 검증은 현재 비활성화됨

                // DI 컨테이너 검증 (단순 확인)
                var largeFileProcessor = _serviceProvider.GetService<SpatialCheckPro.Services.ILargeFileProcessor>();
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ILargeFileProcessor: {(largeFileProcessor != null ? "사용 가능" : "사용 불가")}\n");

                var fileAnalysisService = _serviceProvider.GetService<SpatialCheckPro.Services.FileAnalysisService>();
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FileAnalysisService: {(fileAnalysisService != null ? "사용 가능" : "사용 불가")}\n");

                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow GetRequiredService 호출\n");
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow.Show() 호출\n");
                mainWindow.Show();

                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] base.OnStartup 호출\n");
                base.OnStartup(e);
                
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App OnStartup 완료\n");
            }
            catch (Exception ex)
            {
                // 시작 단계 예외를 로그 파일에 기록
                try
                {
                    var logPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Logs",
                        $"startup_error_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath) ?? "Logs");

                    var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 시작 단계 예외 발생\n" +
                                    $"메시지: {ex.Message}\n" +
                                    $"타입: {ex.GetType().FullName}\n" +
                                    $"스택 트레이스:\n{ex.StackTrace}\n" +
                                    $"내부 예외: {ex.InnerException?.Message}\n" +
                                    $"내부 예외 스택:\n{ex.InnerException?.StackTrace}\n" +
                                    $"{new string('=', 80)}\n\n";

                    System.IO.File.WriteAllText(logPath, logMessage, System.Text.Encoding.UTF8);
                }
                catch { }

                MessageBox.Show($"애플리케이션 시작 중 오류가 발생했습니다:\n\n{ex.Message}\n\n" +
                               $"내부 예외: {ex.InnerException?.Message}\n\n" +
                               $"Logs 폴더의 startup_error 파일을 확인하세요.",
                               "시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                
                Shutdown(1);
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 체계적인 의존성 주입 설정 사용
            DependencyInjectionConfigurator.ConfigureServices(services);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }

        public T? GetService<T>() where T : class
        {
            return _serviceProvider?.GetService<T>();
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 예외를 로그 파일에 기록 (향상된 로그 경로 처리)
            string logPath = string.Empty;
            try
            {
                // 여러 로그 경로 시도 (권한 문제 대응)
                var logPaths = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", $"error_{DateTime.Now:yyyyMMdd}.log"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpatialCheckPro", "Logs", $"error_{DateTime.Now:yyyyMMdd}.log"),
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpatialCheckPro", $"error_{DateTime.Now:yyyyMMdd}.log")
                };

                foreach (var candidatePath in logPaths)
                {
                    try
                    {
                        var logDir = System.IO.Path.GetDirectoryName(candidatePath);
                        if (!string.IsNullOrEmpty(logDir))
                        {
                            System.IO.Directory.CreateDirectory(logDir);
                        }

                        // 테스트 쓰기로 권한 확인
                        System.IO.File.AppendAllText(candidatePath, "", System.Text.Encoding.UTF8);
                        logPath = candidatePath;
                        break;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(logPath))
                {
                    var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 예외 발생\n" +
                                    $"메시지: {e.Exception.Message}\n" +
                                    $"타입: {e.Exception.GetType().FullName}\n" +
                                    $"스택 트레이스:\n{e.Exception.StackTrace}\n" +
                                    $"내부 예외: {e.Exception.InnerException?.Message}\n" +
                                    $"로그 파일: {logPath}\n" +
                                    $"{new string('=', 80)}\n\n";

                    System.IO.File.AppendAllText(logPath, logMessage, System.Text.Encoding.UTF8);
                }
            }
            catch (Exception logEx)
            {
                // 로그 기록 자체가 실패한 경우
                System.Diagnostics.Debug.WriteLine($"로그 기록 실패: {logEx.Message}");
            }

            // 예외 처리 로직 (로그 경로 정보 포함)
            var errorMessage = $"예상치 못한 오류가 발생했습니다:\n\n{e.Exception.Message}\n\n";
            if (!string.IsNullOrEmpty(logPath))
            {
                errorMessage += $"자세한 내용은 다음 로그 파일을 확인하세요:\n{logPath}";
            }
            else
            {
                errorMessage += "자세한 내용은 로그 파일을 확인하세요.";
            }

            MessageBox.Show(errorMessage, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}