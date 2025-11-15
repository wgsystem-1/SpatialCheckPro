using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using System.IO;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간정보 파일 처리를 담당하는 서비스 구현체
    /// </summary>
    public class FileService : IFileService
    {
        private readonly ILogger<FileService> _logger;
        private readonly ILargeFileProcessor _largeFileProcessor;
        private const long LARGE_FILE_THRESHOLD = 1_932_735_283L; // 약 1.8GB

        /// <summary>
        /// 지원되는 파일 확장자 목록
        /// </summary>
        private readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".shp", ".gdb", ".gpkg"
        };

        public FileService(ILogger<FileService> logger, ILargeFileProcessor largeFileProcessor)
        {
            _logger = logger;
            _largeFileProcessor = largeFileProcessor;
            
            // GDAL 초기화
            InitializeGdal();
        }

        /// <summary>
        /// GDAL 라이브러리 초기화
        /// </summary>
        private void InitializeGdal()
        {
            try
            {
                Gdal.AllRegister();
                Ogr.RegisterAll();
                _logger.LogInformation("GDAL 라이브러리가 성공적으로 초기화되었습니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 라이브러리 초기화 중 오류가 발생했습니다.");
                throw;
            }
        }

        /// <summary>
        /// 폴더에서 지원되는 공간정보 파일을 자동 인식
        /// </summary>
        public async Task<IEnumerable<SpatialFileInfo>> DetectSpatialFilesAsync(string folderPath, bool includeSubfolders = true)
        {
            _logger.LogInformation("폴더에서 공간정보 파일 검색을 시작합니다: {FolderPath}", folderPath);

            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("지정된 폴더가 존재하지 않습니다: {FolderPath}", folderPath);
                return Enumerable.Empty<SpatialFileInfo>();
            }

            var spatialFiles = new List<SpatialFileInfo>();

            try
            {
                // 검색 옵션 설정
                var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                // Shapefile 검색 (.shp)
                var shpFiles = Directory.GetFiles(folderPath, "*.shp", searchOption);
                foreach (var shpFile in shpFiles)
                {
                    var fileInfo = await ExtractFileMetadataAsync(shpFile);
                    if (fileInfo != null)
                    {
                        spatialFiles.Add(fileInfo);
                    }
                }

                // File Geodatabase 검색 (.gdb 폴더)
                var gdbDirectories = Directory.GetDirectories(folderPath, "*.gdb", searchOption);
                foreach (var gdbDir in gdbDirectories)
                {
                    var fileInfo = await ExtractFileMetadataAsync(gdbDir);
                    if (fileInfo != null)
                    {
                        spatialFiles.Add(fileInfo);
                    }
                }

                // GeoPackage 검색 (.gpkg)
                var gpkgFiles = Directory.GetFiles(folderPath, "*.gpkg", searchOption);
                foreach (var gpkgFile in gpkgFiles)
                {
                    var fileInfo = await ExtractFileMetadataAsync(gpkgFile);
                    if (fileInfo != null)
                    {
                        spatialFiles.Add(fileInfo);
                    }
                }

                _logger.LogInformation("총 {Count}개의 공간정보 파일을 발견했습니다.", spatialFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "폴더 검색 중 오류가 발생했습니다: {FolderPath}", folderPath);
            }

            return spatialFiles;
        }

        /// <summary>
        /// 단일 파일의 메타데이터 추출
        /// </summary>
        public async Task<SpatialFileInfo?> ExtractFileMetadataAsync(string filePath)
        {
            var startTime = DateTime.Now;
            try
            {
                _logger.LogDebug("파일 메타데이터 추출을 시작합니다: {FilePath}", filePath);

                if (!IsSupportedFormat(filePath))
                {
                    _logger.LogWarning("지원되지 않는 파일 형식입니다: {FilePath}", filePath);
                    return null;
                }

                var fileInfo = new SpatialFileInfo
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Format = GetFileFormat(filePath)
                };

                // 파일 시스템 정보 설정
                if (File.Exists(filePath))
                {
                    var fileSystemInfo = new FileInfo(filePath);
                    fileInfo.FileSize = fileSystemInfo.Length;
                    fileInfo.CreatedAt = fileSystemInfo.CreationTime;
                    fileInfo.ModifiedAt = fileSystemInfo.LastWriteTime;
                }
                else if (Directory.Exists(filePath)) // FileGDB의 경우
                {
                    var dirInfo = new DirectoryInfo(filePath);
                    fileInfo.FileSize = GetDirectorySize(dirInfo);
                    fileInfo.CreatedAt = dirInfo.CreationTime;
                    fileInfo.ModifiedAt = dirInfo.LastWriteTime;
                }

                // GDAL을 사용하여 공간정보 메타데이터 추출
                await ExtractSpatialMetadataAsync(fileInfo);

                var duration = DateTime.Now - startTime;
                LoggingService.LogFileAccess(_logger, "메타데이터 추출", filePath, true, duration);
                return fileInfo;
            }
            catch (Exception ex)
            {
                var duration = DateTime.Now - startTime;
                LoggingService.LogFileAccess(_logger, "메타데이터 추출", filePath, false, duration);
                _logger.LogError(ex, "파일 메타데이터 추출 중 오류가 발생했습니다: {FilePath}", filePath);
                return null;
            }
        }

        /// <summary>
        /// GDAL을 사용하여 공간정보 메타데이터 추출
        /// </summary>
        private async Task ExtractSpatialMetadataAsync(SpatialFileInfo fileInfo)
        {
            await Task.Run(() =>
            {
                DataSource? dataSource = null;
                try
                {
                    // 데이터 소스 열기
                    dataSource = Ogr.Open(fileInfo.FilePath, 0); // 읽기 전용
                    if (dataSource == null)
                    {
                        _logger.LogWarning("데이터 소스를 열 수 없습니다: {FilePath}", fileInfo.FilePath);
                        return;
                    }

                    // 레이어(테이블) 정보 추출
                    var layerCount = dataSource.GetLayerCount();
                    _logger.LogDebug("레이어 수: {LayerCount}", layerCount);

                    for (int i = 0; i < layerCount; i++)
                    {
                        var layer = dataSource.GetLayerByIndex(i);
                        if (layer != null)
                        {
                            var tableInfo = ExtractTableInfo(layer, i.ToString());
                            fileInfo.Tables.Add(tableInfo);

                            // 첫 번째 레이어의 좌표계를 파일의 좌표계로 설정
                            if (i == 0 && !string.IsNullOrEmpty(tableInfo.CoordinateSystem))
                            {
                                fileInfo.CoordinateSystem = tableInfo.CoordinateSystem;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "공간정보 메타데이터 추출 중 오류가 발생했습니다: {FilePath}", fileInfo.FilePath);
                }
                finally
                {
                    dataSource?.Dispose();
                }
            });
        }

        /// <summary>
        /// 레이어에서 테이블 정보 추출
        /// </summary>
        private TableInfo ExtractTableInfo(Layer layer, string tableId)
        {
            var tableInfo = new TableInfo
            {
                TableId = tableId,
                TableName = layer.GetName(),
                RecordCount = (int)layer.GetFeatureCount(1) // 정확한 개수 계산
            };

            try
            {
                // 좌표계 정보 추출
                var spatialRef = layer.GetSpatialRef();
                if (spatialRef != null)
                {
                    spatialRef.ExportToWkt(out string wkt, null);
                    tableInfo.CoordinateSystem = wkt;
                }

                // 지오메트리 타입 추출
                var geomType = layer.GetGeomType();
                tableInfo.GeometryType = geomType.ToString();

                // 컬럼 정보 추출
                var layerDefn = layer.GetLayerDefn();
                var fieldCount = layerDefn.GetFieldCount();

                for (int i = 0; i < fieldCount; i++)
                {
                    var fieldDefn = layerDefn.GetFieldDefn(i);
                    var columnInfo = new ColumnInfo
                    {
                        ColumnName = fieldDefn.GetName(),
                        DataType = fieldDefn.GetFieldTypeName(fieldDefn.GetFieldType()),
                        Length = fieldDefn.GetWidth(),
                        IsNullable = fieldDefn.IsNullable() == 0 // 0이면 nullable, 1이면 not nullable
                    };

                    tableInfo.Columns.Add(columnInfo);
                }

                _logger.LogDebug("테이블 정보 추출 완료: {TableName}, 레코드 수: {RecordCount}, 컬럼 수: {ColumnCount}",
                    tableInfo.TableName, tableInfo.RecordCount, tableInfo.Columns.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 정보 추출 중 오류가 발생했습니다: {TableName}", tableInfo.TableName);
            }

            return tableInfo;
        }

        /// <summary>
        /// 대용량 파일을 청크 단위로 분할 처리
        /// </summary>
        public async Task<FileProcessResult> ProcessLargeFileAsync(string filePath, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("대용량 파일 처리를 시작합니다: {FilePath}", filePath);

                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    return new FileProcessResult
                    {
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        ErrorMessage = "파일이 존재하지 않습니다."
                    };
                }

                if (!IsLargeFile(filePath))
                {
                    // 일반 파일 처리
                    var fileInfo = await ExtractFileMetadataAsync(filePath);
                    return new FileProcessResult
                    {
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        IsSuccess = fileInfo != null,
                        FileInfo = fileInfo,
                        ProcessedChunks = 1,
                        TotalChunks = 1
                    };
                }
                else
                {
                    // 대용량 파일 청크 처리 - LargeFileProcessor 사용
                    IProgress<ProcessProgress>? detailedProgress = null;
                    if (progress != null)
                    {
                        detailedProgress = new Progress<ProcessProgress>(p => progress.Report(p.Percentage));
                    }

                    using var fileStream = File.OpenRead(filePath);
                    var processingResult = await _largeFileProcessor.ProcessFileStreamAsync(fileStream, stream => 
                    {
                        // 파일 스트림 처리 로직
                        return Task.FromResult(true);
                    }, cancellationToken);
                    
                    return new FileProcessResult
                    {
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        IsSuccess = processingResult.Success,
                        ProcessedBytes = processingResult.ProcessedBytes,
                        ErrorMessage = processingResult.Success ? string.Empty : "파일 처리 실패"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "대용량 파일 처리 중 오류가 발생했습니다: {FilePath}", filePath);
                return new FileProcessResult
                {
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now,
                    ErrorMessage = ex.Message
                };
            }
        }



        /// <summary>
        /// 파일 형식 검증
        /// </summary>
        public bool IsSupportedFormat(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            // FileGDB는 폴더 형태
            if (Directory.Exists(filePath) && filePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return true;

            // 파일 확장자 확인
            var extension = Path.GetExtension(filePath);
            return _supportedExtensions.Contains(extension);
        }

        /// <summary>
        /// 파일이 대용량인지 확인 (2GB 초과)
        /// </summary>
        public bool IsLargeFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    return new FileInfo(filePath).Length > LARGE_FILE_THRESHOLD;
                }
                else if (Directory.Exists(filePath))
                {
                    return GetDirectorySize(new DirectoryInfo(filePath)) > LARGE_FILE_THRESHOLD;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 크기 확인 중 오류가 발생했습니다: {FilePath}", filePath);
            }

            return false;
        }

        /// <summary>
        /// 파일 형식 결정
        /// </summary>
        private SpatialFileFormat GetFileFormat(string filePath)
        {
            if (Directory.Exists(filePath) && filePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return SpatialFileFormat.FileGDB;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".shp" => SpatialFileFormat.SHP,
                ".gpkg" => SpatialFileFormat.GeoPackage,
                _ => SpatialFileFormat.SHP // 기본값
            };
        }

        /// <summary>
        /// 디렉토리 크기 계산
        /// </summary>
        private long GetDirectorySize(DirectoryInfo directory)
        {
            try
            {
                return directory.GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "디렉토리 크기 계산 중 오류가 발생했습니다: {DirectoryPath}", directory.FullName);
                return 0;
            }
        }
    }
}
