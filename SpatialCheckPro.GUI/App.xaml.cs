using System.Windows;
using System.Text;
using System;
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

namespace SpatialCheckPro.GUI
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        
        public ServiceProvider? ServiceProvider => _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            string appLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");
            
            try
            {
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App OnStartup 시작\n");
                
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
                
                System.IO.File.AppendAllText(appLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ServiceProvider 빌드\n");
                _serviceProvider = serviceCollection.BuildServiceProvider();

                // 의존성 주입 검증은 현재 비활성화됨

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
            // 예외를 로그 파일에 기록
            try
            {
                var logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, 
                    "Logs", 
                    $"error_{DateTime.Now:yyyyMMdd}.log");
                
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath) ?? "Logs");
                
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 예외 발생\n" +
                                $"메시지: {e.Exception.Message}\n" +
                                $"타입: {e.Exception.GetType().FullName}\n" +
                                $"스택 트레이스:\n{e.Exception.StackTrace}\n" +
                                $"내부 예외: {e.Exception.InnerException?.Message}\n" +
                                $"{new string('=', 80)}\n\n";
                
                System.IO.File.AppendAllText(logPath, logMessage, System.Text.Encoding.UTF8);
            }
            catch { }

            // 예외 처리 로직
            MessageBox.Show($"예상치 못한 오류가 발생했습니다:\n\n{e.Exception.Message}\n\n" +
                           $"자세한 내용은 Logs 폴더의 로그 파일을 확인하세요.", 
                           "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}