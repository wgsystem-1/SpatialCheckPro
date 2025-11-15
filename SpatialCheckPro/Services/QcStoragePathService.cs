using System;
using System.IO;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC 결과 저장을 위한 GDB 경로를 생성하는 서비스
    /// </summary>
    public class QcStoragePathService
    {
        private readonly ILogger<QcStoragePathService>? _logger;
        private readonly QcErrorsPathManager _pathManager;
        
        public QcStoragePathService(ILogger<QcStoragePathService>? logger = null)
        {
            _logger = logger;
            
            // QcErrorsPathManager는 직접 생성
            var pathManagerLogger = logger != null 
                ? new LoggerFactory().CreateLogger<QcErrorsPathManager>()
                : Microsoft.Extensions.Logging.Abstractions.NullLogger<QcErrorsPathManager>.Instance;
            _pathManager = new QcErrorsPathManager(pathManagerLogger);
        }
        
        /// <summary>
        /// 검수 대상 FileGDB 경로를 기반으로 QC 결과용 GDB 경로를 생성합니다.
        /// </summary>
        /// <param name="targetGdbPath">검수 대상 FileGDB 경로 (예: D:\work\data.gdb)</param>
        /// <returns>QC_errors 폴더 내 QC용 GDB 경로 (예: D:\work\QC_errors\data_QC_20241114_093000.gdb)</returns>
        public string BuildQcGdbPath(string targetGdbPath)
        {
            if (string.IsNullOrWhiteSpace(targetGdbPath))
                throw new ArgumentException("검수 대상 GDB 경로가 비어있습니다.", nameof(targetGdbPath));

            try
            {
                // 새로운 경로 구조 사용
                var qcGdbPath = _pathManager.GetQcErrorGdbPath(targetGdbPath);
                
                // QC_errors 디렉터리 생성
                var qcErrorsDir = Path.GetDirectoryName(qcGdbPath);
                if (!string.IsNullOrEmpty(qcErrorsDir) && !Directory.Exists(qcErrorsDir))
                {
                    Directory.CreateDirectory(qcErrorsDir);
                    _logger?.LogInformation("QC_errors 디렉터리 생성: {Directory}", qcErrorsDir);
                }
                
                _logger?.LogInformation("QC GDB 경로 생성: {Path}", qcGdbPath);
                return qcGdbPath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "QC GDB 경로 생성 실패: {TargetPath}", targetGdbPath);
                
                // 실패 시 기존 방식으로 fallback
                var dir = Path.GetDirectoryName(targetGdbPath) ?? ".";
                var name = Path.GetFileNameWithoutExtension(targetGdbPath);
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture); 
                var qcName = $"{name}_QC_{ts}.gdb";
                
                return Path.Combine(dir, qcName);
            }
        }
    }
}
