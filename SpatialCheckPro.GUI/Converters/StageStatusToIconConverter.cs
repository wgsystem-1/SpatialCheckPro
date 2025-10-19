using System;
using System.Globalization;
using System.Windows.Data;
using SpatialCheckPro.GUI.Constants;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.GUI.Converters
{
    /// <summary>
    /// 단계 상태를 아이콘으로 변환하는 컨버터
    /// </summary>
    public class StageStatusToIconConverter : IValueConverter
    {
        /// <summary>
        /// 단계 상태를 아이콘 문자열로 변환합니다
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StageStatus status)
            {
                return status switch
                {
                    StageStatus.NotStarted => IconCodes.Warning,
                    StageStatus.Pending => IconCodes.Progress,
                    StageStatus.Running => IconCodes.Play,
                    StageStatus.Completed => IconCodes.Success,
                    StageStatus.CompletedWithWarnings => IconCodes.Warning,
                    StageStatus.Failed => IconCodes.Error,
                    StageStatus.Skipped => IconCodes.Refresh,
                    StageStatus.Blocked => IconCodes.Alert,
                    _ => IconCodes.Info
                };
            }

            return IconCodes.Info;
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