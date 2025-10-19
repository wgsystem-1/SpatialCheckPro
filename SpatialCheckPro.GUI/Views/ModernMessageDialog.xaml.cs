using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 현대적 메시지 다이얼로그 - 검수 완료 안내용
    /// </summary>
    public partial class ModernMessageDialog : Window
    {
        /// <summary>
        /// PDF 경로
        /// </summary>
        public string? PdfPath { get; set; }

        /// <summary>
        /// HTML 경로
        /// </summary>
        public string? HtmlPath { get; set; }

        public ModernMessageDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 다이얼로그 내용을 구성합니다
        /// </summary>
        public void Configure(string title, string message, string? pdfPath, string? htmlPath)
        {
            TitleText.Text = title;
            BodyText.Text = message;

            PdfPath = pdfPath;
            HtmlPath = htmlPath;

            if (!string.IsNullOrWhiteSpace(pdfPath))
            {
                PdfRow.Visibility = Visibility.Visible;
                PdfPathText.Text = pdfPath;
            }

            if (!string.IsNullOrWhiteSpace(htmlPath))
            {
                HtmlRow.Visibility = Visibility.Visible;
                HtmlPathText.Text = htmlPath;
            }
        }

        /// <summary>
        /// 상태에 따른 아이콘/색상을 설정합니다
        /// </summary>
        /// <param name="theme">light/dark</param>
        /// <param name="status">success/partial/fail</param>
        public void ApplyStyle(string theme = "light", string status = "success")
        {
            // 다크/라이트 테마 색상
            var isDark = string.Equals(theme, "dark", System.StringComparison.OrdinalIgnoreCase);
            RootBorder.Background = isDark ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55)) : System.Windows.Media.Brushes.White;
            RootBorder.BorderBrush = isDark ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235));
            TitleText.Foreground = isDark ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
            BodyText.Foreground = isDark ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81));

            // 상태 색상/아이콘
            if (string.Equals(status, "success", System.StringComparison.OrdinalIgnoreCase))
            {
                IconBadge.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // green
                IconPath.Data = System.Windows.Media.Geometry.Parse("M10 15l-3.5-3.5 1.1-1.1L10 12.8l5.3-5.3 1.1 1.1z");
            }
            else if (string.Equals(status, "partial", System.StringComparison.OrdinalIgnoreCase))
            {
                IconBadge.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // yellow
                IconPath.Data = System.Windows.Media.Geometry.Parse("M10 4v8l6 3"); // clock-like
            }
            else
            {
                IconBadge.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // red
                IconPath.Data = System.Windows.Media.Geometry.Parse("M6 6l8 8M14 6l-8 8"); // X
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 페이드 인 애니메이션
            RootBorder.Opacity = 0;
            for (int i = 0; i <= 10; i++)
            {
                await System.Threading.Tasks.Task.Delay(12);
                RootBorder.Opacity = i / 10.0;
            }
        }

        private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 페이드 아웃 애니메이션
            if (RootBorder.Opacity > 0 && RootBorder.Opacity <= 1)
            {
                e.Cancel = true;
                for (int i = 10; i >= 0; i--)
                {
                    await System.Threading.Tasks.Task.Delay(12);
                    RootBorder.Opacity = i / 10.0;
                }
                e.Cancel = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CopyPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(PdfPath)) Clipboard.SetText(PdfPath);
        }

        private void CopyHtml_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(HtmlPath)) Clipboard.SetText(HtmlPath);
        }

        private void OpenPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(PdfPath) && File.Exists(PdfPath)) Process.Start(new ProcessStartInfo(PdfPath) { UseShellExecute = true });
        }

        private void OpenHtml_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(HtmlPath) && File.Exists(HtmlPath)) Process.Start(new ProcessStartInfo(HtmlPath) { UseShellExecute = true });
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var target = !string.IsNullOrWhiteSpace(PdfPath) ? Path.GetDirectoryName(PdfPath) : (!string.IsNullOrWhiteSpace(HtmlPath) ? Path.GetDirectoryName(HtmlPath) : null);
            if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }
}


