using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// Boolean 값을 Brush로 변환하는 컨버터
    /// True일 때 빨간색, False일 때 기본색
    /// </summary>
    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOverdue && isOverdue)
            {
                // 초과 시 빨간색
                return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #EF4444
            }
            
            // 정상 시 기본색 (다크 그레이)
            return new SolidColorBrush(Color.FromRgb(31, 41, 55)); // #1F2937
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
