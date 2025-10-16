#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// 오류 심각도를 브러시로 변환하는 컨버터
    /// </summary>
    public class SeverityToBrushConverter : IValueConverter
    {
        /// <summary>
        /// 심각도를 브러시로 변환합니다
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ErrorSeverity severity)
            {
                return GetBrush("BorderColor");
            }

            return severity switch
            {
                ErrorSeverity.Info => GetBrush("PrimaryColor"),
                ErrorSeverity.Warning => GetBrush("WarningColor"),
                ErrorSeverity.Error => GetBrush("ErrorColor"),
                ErrorSeverity.Critical => GetBrush("ErrorColor"),
                _ => GetBrush("BorderColor")
            };
        }

        /// <summary>
        /// 역변환 (미사용)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static Brush GetBrush(string key)
        {
            return (Brush)Application.Current.FindResource(key);
        }
    }
}


