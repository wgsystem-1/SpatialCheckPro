#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// null 값 여부를 Visibility로 변환하는 컨버터
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// null 여부를 Visibility로 변환합니다
        /// </summary>
        /// <param name="value">입력 값</param>
        /// <param name="targetType">타겟 유형</param>
        /// <param name="parameter">반전 여부 (true시 null이면 Visible)</param>
        /// <param name="culture">문화권</param>
        /// <returns>Visibility 결과</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var invert = parameter is string text && bool.TryParse(text, out var invertFlag) && invertFlag;
            var isNull = value is null;

            if (invert)
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 역변환 (사용하지 않음)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


