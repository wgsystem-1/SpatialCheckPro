using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// PROJ/GDAL 환경 변수를 안전하게 구성하는 도우미
    /// </summary>
    public static class ProjEnvironmentManager
    {
        private static readonly object SyncRoot = new();
        private static string? _lastResolvedPath;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetShortPathNameNative(
            [MarshalAs(UnmanagedType.LPTStr)] string path,
            [MarshalAs(UnmanagedType.LPTStr)] StringBuilder shortPath,
            int shortPathLength);

        /// <summary>
        /// 애플리케이션 기준 경로에서 PROJ 환경을 구성합니다.
        /// </summary>
        public static string ConfigureFromApplicationBase(string appBaseDirectory, ILogger? logger = null)
        {
            var gdalSharePath = Path.Combine(appBaseDirectory, "gdal", "share");
            return ConfigureFromSharePath(gdalSharePath, logger);
        }

        /// <summary>
        /// 지정된 GDAL share 경로를 사용해 PROJ 환경을 구성합니다.
        /// </summary>
        public static string ConfigureFromSharePath(string projSharePath, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(projSharePath) || !Directory.Exists(projSharePath))
            {
                logger?.LogError("PROJ 데이터 디렉터리를 찾을 수 없습니다: {Path}", projSharePath);
                return projSharePath;
            }

            lock (SyncRoot)
            {
                var accessiblePath = EnsureProjPathAccessible(projSharePath, logger);
                ApplyEnvironment(accessiblePath, logger);
                _lastResolvedPath = accessiblePath;
                logger?.LogInformation("PROJ 경로 적용: {Resolved} (원본: {Original})", accessiblePath, projSharePath);
                return accessiblePath;
            }
        }

        /// <summary>
        /// 마지막으로 적용된 PROJ 경로를 반환합니다.
        /// </summary>
        public static string? GetLastResolvedPath() => _lastResolvedPath;

        private static void ApplyEnvironment(string projPath, ILogger? logger)
        {
            Environment.SetEnvironmentVariable("PROJ_LIB", projPath);
            Environment.SetEnvironmentVariable("PROJ_DATA", projPath);
            Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projPath);
            Environment.SetEnvironmentVariable("PROJ_SEARCH_PATHS", projPath);
            Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");

            try
            {
                Gdal.SetConfigOption("PROJ_LIB", projPath);
                Gdal.SetConfigOption("PROJ_DATA", projPath);
                Gdal.SetConfigOption("PROJ_SEARCH_PATHS", projPath);
                Gdal.SetConfigOption("PROJ_NETWORK", "OFF");
            }
            catch (DllNotFoundException dllEx)
            {
                logger?.LogWarning(dllEx, "GDAL 네이티브 라이브러리를 로드하지 못해 GDAL 설정 옵션 적용을 건너뜁니다.");
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "GDAL 설정 옵션 적용 중 경고가 발생했습니다.");
            }
        }

        private static string EnsureProjPathAccessible(string originalPath, ILogger? logger)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return originalPath;
            }

            if (IsAsciiPath(originalPath))
            {
                logger?.LogDebug("PROJ 경로가 이미 ASCII로 안전합니다: {Path}", originalPath);
                return originalPath;
            }

            var shortPath = TryGetShortPathName(originalPath, logger);
            if (!string.IsNullOrEmpty(shortPath) && shortPath != originalPath && IsAsciiPath(shortPath))
            {
                logger?.LogInformation("PROJ 경로를 8.3 형식으로 변환했습니다: {Original} -> {Short}", originalPath, shortPath);
                return shortPath;
            }

            var fallbackPath = CopyToAsciiSafePath(originalPath, logger);
            if (!string.IsNullOrEmpty(fallbackPath))
            {
                return fallbackPath;
            }

            logger?.LogWarning("PROJ 경로를 변환하지 못해 원본을 사용합니다: {Path}", originalPath);
            return originalPath;
        }

        private static string? CopyToAsciiSafePath(string originalPath, ILogger? logger)
        {
            try
            {
                var baseRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrWhiteSpace(baseRoot))
                {
                    baseRoot = Environment.GetEnvironmentVariable("SystemDrive");
                    if (string.IsNullOrWhiteSpace(baseRoot))
                    {
                        baseRoot = "C:";
                    }
                }

                var fallbackPath = Path.Combine(baseRoot, "SpatialCheckPro", "gdal_share");
                if (Directory.Exists(fallbackPath))
                {
                    var projPath = Path.Combine(fallbackPath, "proj.db");
                    if (!File.Exists(projPath))
                    {
                        Directory.Delete(fallbackPath, true);
                    }
                }

                CopyDirectoryRecursive(originalPath, fallbackPath);
                logger?.LogInformation("PROJ 데이터를 ASCII 경로로 복사했습니다: {Path}", fallbackPath);
                return fallbackPath;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "PROJ 데이터를 안전 경로로 복사하지 못했습니다.");
                return null;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"원본 디렉터리를 찾을 수 없습니다: {sourceDir}");
            }

            Directory.CreateDirectory(destinationDir);

            foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(destinationDir, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var target = Path.Combine(destinationDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        private static string TryGetShortPathName(string path, ILogger? logger)
        {
            try
            {
                var shortPath = new StringBuilder(512);
                int result = GetShortPathNameNative(path, shortPath, shortPath.Capacity);
                if (result > 0 && result < shortPath.Capacity)
                {
                    return shortPath.ToString();
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "짧은 경로 변환 실패: {Path}", path);
            }

            return path;
        }

        private static bool IsAsciiPath(string path)
        {
            foreach (var ch in path)
            {
                if (ch > 127)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

