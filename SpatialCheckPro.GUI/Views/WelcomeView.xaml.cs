using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 환영 화면
    /// </summary>
    public partial class WelcomeView : UserControl
    {
        private readonly ILogger<WelcomeView>? _logger;
        private readonly ValidationMetricsCollector? _metricsCollector;

        public event EventHandler? QuickStartRequested;

        public WelcomeView()
        {
            InitializeComponent();
            
            // 서비스 가져오기
            var app = Application.Current as App;
            _logger = app?.GetService<ILogger<WelcomeView>>();
            _metricsCollector = app?.GetService<ValidationMetricsCollector>();
            
            LoadLastValidationInfo();
        }

        /// <summary>
        /// 최근 검수 정보 로드
        /// </summary>
        private void LoadLastValidationInfo()
        {
            try
            {
                if (_metricsCollector != null)
                {
                    // TODO: 최근 검수 정보 가져오기
                    LastValidationText.Text = "정보 없음";
                }
                else
                {
                    LastValidationText.Text = "-";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "최근 검수 정보 로드 실패");
                LastValidationText.Text = "-";
            }
        }

        /// <summary>
        /// 빠른 시작 버튼 클릭
        /// </summary>
        private void QuickStartButton_Click(object sender, RoutedEventArgs e)
        {
            QuickStartRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
