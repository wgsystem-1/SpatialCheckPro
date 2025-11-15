using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC_errors 폴더 경로 및 오류 결과 FileGDB 관리 서비스
    /// </summary>
    public class QcErrorsPathManager
    {
        private readonly ILogger<QcErrorsPathManager> _logger;
        private const string QC_ERRORS_FOLDER = "QC_errors";
        private const string QC_SUFFIX = "_QC";
        
        // 제외할 패턴
        private static readonly string[] ExcludedPatterns = {
            "QC_errors",
            "QC_results", 
            "_backup",
            "_temp",
            ".old"
        };

        public QcErrorsPathManager(ILogger<QcErrorsPathManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 입력 경로에 따른 QC_errors 폴더 경로를 반환합니다
        /// </summary>
        public string GetQcErrorsDirectory(string inputPath)
        {
            if (IsFileGdb(inputPath))
            {
                // FileGDB 선택 시: 부모 폴더에 QC_errors 생성
                var parentDir = Directory.GetParent(inputPath)?.FullName 
                    ?? throw new InvalidOperationException($"부모 디렉터리를 찾을 수 없습니다: {inputPath}");
                return Path.Combine(parentDir, QC_ERRORS_FOLDER);
            }
            else if (Directory.Exists(inputPath))
            {
                // 폴더 선택 시: 해당 폴더에 QC_errors 생성
                return Path.Combine(inputPath, QC_ERRORS_FOLDER);
            }
            
            throw new ArgumentException($"유효하지 않은 경로입니다: {inputPath}");
        }

        /// <summary>
        /// 오류 결과 FileGDB 경로를 생성합니다
        /// </summary>
        public string GetQcErrorGdbPath(string sourceGdbPath, DateTime? timestamp = null)
        {
            var gdbName = Path.GetFileName(sourceGdbPath);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(gdbName);
            var qcErrorsDir = GetQcErrorsDirectory(sourceGdbPath);
            
            // 타임스탬프 생성 (파일명 뒤쪽에 추가)
            var ts = timestamp ?? DateTime.Now;
            var timestampStr = ts.ToString("yyyyMMdd_HHmmss");
            
            var qcGdbName = $"{nameWithoutExtension}{QC_SUFFIX}_{timestampStr}.gdb";
            return Path.Combine(qcErrorsDir, qcGdbName);
        }

        /// <summary>
        /// 검수 대상 FileGDB 목록을 찾습니다
        /// </summary>
        public List<FileGdbInfo> FindValidationTargets(string folderPath)
        {
            var results = new List<FileGdbInfo>();
            
            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("폴더가 존재하지 않습니다: {Path}", folderPath);
                return results;
            }

            // .gdb 폴더 검색
            var gdbPaths = Directory.GetDirectories(folderPath, "*.gdb", SearchOption.AllDirectories)
                .Where(path => !IsExcludedPath(path))
                .Where(path => IsValidFileGdb(path))
                .OrderBy(path => path)
                .ToList();

            foreach (var gdbPath in gdbPaths)
            {
                try
                {
                    var info = new FileGdbInfo
                    {
                        FullPath = gdbPath,
                        Name = Path.GetFileName(gdbPath),
                        SizeInBytes = CalculateDirectorySize(gdbPath),
                        RelativePath = Path.GetRelativePath(folderPath, gdbPath)
                    };
                    results.Add(info);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FileGDB 정보 수집 실패: {Path}", gdbPath);
                }
            }

            return results;
        }

        /// <summary>
        /// 제외된 FileGDB 목록을 찾습니다
        /// </summary>
        public List<ExcludedItemInfo> FindExcludedItems(string folderPath)
        {
            var results = new List<ExcludedItemInfo>();
            
            if (!Directory.Exists(folderPath))
                return results;

            var allGdbs = Directory.GetDirectories(folderPath, "*.gdb", SearchOption.AllDirectories);
            
            foreach (var gdbPath in allGdbs)
            {
                var excludeReason = GetExcludeReason(gdbPath);
                if (!string.IsNullOrEmpty(excludeReason))
                {
                    results.Add(new ExcludedItemInfo
                    {
                        FullPath = gdbPath,
                        Name = Path.GetFileName(gdbPath),
                        Reason = excludeReason,
                        RelativePath = Path.GetRelativePath(folderPath, gdbPath)
                    });
                }
            }

            return results.OrderBy(x => x.RelativePath).ToList();
        }

        /// <summary>
        /// 경로가 제외 대상인지 확인합니다
        /// </summary>
        private bool IsExcludedPath(string path)
        {
            return ExcludedPatterns.Any(pattern => 
                path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 제외 이유를 반환합니다
        /// </summary>
        private string GetExcludeReason(string path)
        {
            foreach (var pattern in ExcludedPatterns)
            {
                if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return pattern switch
                    {
                        "QC_errors" => "오류 결과 폴더",
                        "QC_results" => "검수 결과 폴더",
                        "_backup" => "백업 폴더",
                        "_temp" => "임시 폴더",
                        ".old" => "이전 버전 폴더",
                        _ => $"{pattern} 패턴 일치"
                    };
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// FileGDB인지 확인합니다
        /// </summary>
        public bool IsFileGdb(string path)
        {
            return path.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path);
        }

        /// <summary>
        /// 유효한 FileGDB인지 확인합니다
        /// </summary>
        private bool IsValidFileGdb(string path)
        {
            try
            {
                // 기본 .gdbtable 파일 존재 여부 확인
                var hasGdbTables = Directory.EnumerateFiles(path, "*.gdbtable", SearchOption.TopDirectoryOnly).Any();
                return hasGdbTables;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileGDB 유효성 검사 실패: {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// 디렉터리 크기를 계산합니다
        /// </summary>
        public long CalculateDirectorySize(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "디렉터리 크기 계산 실패: {Path}", path);
                return 0;
            }
        }

        /// <summary>
        /// FileGDB 정보
        /// </summary>
        public class FileGdbInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public long SizeInBytes { get; set; }
            public string SizeText => FormatFileSize(SizeInBytes);

            private static string FormatFileSize(long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = bytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        /// <summary>
        /// 제외된 항목 정보
        /// </summary>
        public class ExcludedItemInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }
    }
}
