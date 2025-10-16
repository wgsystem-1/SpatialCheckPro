using System;
using System.IO;
using System.Globalization;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC 결과 저장을 위한 GDB 경로를 생성하는 서비스
    /// </summary>
    public class QcStoragePathService
    {
        /// <summary>
        /// 검수 대상 FileGDB 경로를 기반으로 QC 결과용 GDB 경로를 생성합니다.
        /// </summary>
        /// <param name="targetGdbPath">검수 대상 FileGDB 경로 (예: D:\work\data.gdb)</param>
        /// <returns>동일 폴더에 생성될 QC용 GDB 경로 (예: D:\work\data_QC_251016073000.gdb)</returns>
        public string BuildQcGdbPath(string targetGdbPath)
        {
            if (string.IsNullOrWhiteSpace(targetGdbPath))
                throw new ArgumentException("검수 대상 GDB 경로가 비어있습니다.", nameof(targetGdbPath));

            var dir = Path.GetDirectoryName(targetGdbPath);
            if (dir == null)
            {
                dir = "."; // Fallback to current directory if path is relative
            }
            var name = Path.GetFileNameWithoutExtension(targetGdbPath);
            var ts = DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture); 
            var qcName = $"{name}_QC_{ts}.gdb";
            
            return Path.Combine(dir, qcName);
        }
    }
}
