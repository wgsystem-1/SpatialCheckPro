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
            // 콘솔 출력 인코딩을 UTF-8로 고정 (한글 깨짐 방지)
            try
            {
                System.Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                System.Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch { }

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            // 의존성 주입 검증은 현재 비활성화됨

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
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
            // 예외 처리 로직
            MessageBox.Show($"예상치 못한 오류가 발생했습니다: {e.Exception.Message}", "오류", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}