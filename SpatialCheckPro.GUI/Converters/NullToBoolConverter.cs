using System;
using System.Globalization;
using System.Windows.Data;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// null 값을 Boolean으로 변환하는 컨버터
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        /// <summary>
        /// null 값을 Boolean으로 변환합니다
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        /// <summary>
        /// Boolean 값을 다시 변환합니다 (사용되지 않음)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}