using System;
using System.Globalization;
using System.Windows.Data;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// 단계 상태를 색상으로 변환하는 컨버터
    /// </summary>
    public class StageStatusToColorConverter : IValueConverter
    {
        /// <summary>
        /// 단계 상태를 색상으로 변환합니다
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StageStatus status)
            {
                return status switch
                {
                    StageStatus.NotStarted => GetBrush("BorderColor"),
                    StageStatus.Pending => GetBrush("TextSecondaryColor"),
                    StageStatus.Running => GetBrush("PrimaryColor"),
                    StageStatus.Completed => GetBrush("SuccessColor"),
                    StageStatus.CompletedWithWarnings => GetBrush("WarningColor"),
                    StageStatus.Failed => GetBrush("ErrorColor"),
                    StageStatus.Skipped => GetBrush("BorderColor"),
                    StageStatus.Blocked => GetBrush("ErrorColor"),
                    _ => GetBrush("BorderColor")
                };
            }

            return GetBrush("BorderColor");
        }

        private static System.Windows.Media.Brush GetBrush(string resourceKey)
        {
            return (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource(resourceKey);
        }

        /// <summary>
        /// 역변환 (사용되지 않음)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}