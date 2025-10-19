#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// bool 값을 Visibility로 변환하는 컨버터
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// true/false 값을 Visibility로 변환합니다
        /// </summary>
        /// <param name="value">변환할 값</param>
        /// <param name="targetType">타겟 타입</param>
        /// <param name="parameter">반전 파라미터</param>
        /// <param name="culture">문화권</param>
        /// <returns>Visibility 값</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var boolValue = value is bool flag && flag;
            var invert = parameter is string text && bool.TryParse(text, out var invertFlag) && invertFlag;

            if (invert)
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 역변환
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                var result = visibility == Visibility.Visible;
                var invert = parameter is string text && bool.TryParse(text, out var invertFlag) && invertFlag;
                return invert ? !result : result;
            }

            throw new InvalidOperationException("Visibility 값만 변환할 수 있습니다.");
        }
    }
}


