using SpatialCheckPro.Models;
using OSGeo.OGR;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 격자(Grid) 기반 공간 인덱스 구현
    /// </summary>
    public class GridSpatialIndex : ISpatialIndex, IDisposable
    {
        private readonly ILogger<GridSpatialIndex> _logger;
        private GridCell[,] _grid;
        private int _featureCount;
        private readonly int _gridWidth;
        private readonly int _gridHeight;
        private SpatialEnvelope _bounds;
        private double _cellWidth;
        private double _cellHeight;
        private readonly Dictionary<long, SpatialEnvelope> _featureEnvelopes;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="gridWidth">격자 가로 개수</param>
        /// <param name="gridHeight">격자 세로 개수</param>
        public GridSpatialIndex(ILogger<GridSpatialIndex> logger, int gridWidth = 100, int gridHeight = 100)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
            _featureEnvelopes = new Dictionary<long, SpatialEnvelope>();
            Clear();
        }

        /// <summary>
        /// 지정된 레이어로부터 공간 인덱스를 구축합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>구축 완료 여부</returns>
        public async Task<bool> BuildIndexAsync(string gdbPath, string layerName)
        {
            try
            {
                _logger.LogInformation("Grid 인덱스 구축 시작: {LayerName} (격자 크기: {GridWidth}x{GridHeight})", 
                    layerName, _gridWidth, _gridHeight);
                
                // GDAL 초기화
                Ogr.RegisterAll();
                
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    _logger.LogError("File Geodatabase를 열 수 없습니다: {GdbPath}", gdbPath);
                    return false;
                }

                var layer = dataSource.GetLayerByName(layerName);
                if (layer == null)
                {
                    _logger.LogError("레이어를 찾을 수 없습니다: {LayerName}", layerName);
                    return false;
                }

                // 전체 범위 계산
                var layerEnvelope = new OSGeo.OGR.Envelope();
                layer.GetExtent(layerEnvelope, 1);
                
                _bounds = new SpatialEnvelope(
                    layerEnvelope.MinX, layerEnvelope.MinY, 
                    layerEnvelope.MaxX, layerEnvelope.MaxY);

                // 격자 초기화
                InitializeGrid();

                // 피처들을 순회하며 인덱스에 추가
                layer.ResetReading();
                Feature feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    try
                    {
                        var geometry = feature.GetGeometryRef();
                        if (geometry != null)
                        {
                            var envelope = new OSGeo.OGR.Envelope();
                            geometry.GetEnvelope(envelope);
                            
                            var spatialEnvelope = new SpatialEnvelope(
                                envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);

                            var featureId = feature.GetFID();
                            
                            // 피처 범위 저장
                            _featureEnvelopes[featureId] = spatialEnvelope;
                            
                            // Grid에 삽입
                            await InsertFeatureAsync(featureId, spatialEnvelope);
                            
                            processedCount++;
                            
                            if (processedCount % 1000 == 0)
                            {
                                _logger.LogDebug("인덱스 구축 진행: {ProcessedCount}개 피처 처리됨", processedCount);
                            }
                        }
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                _featureCount = processedCount;
                
                // 격자 통계 로깅
                LogGridStatistics();
                
                _logger.LogInformation("Grid 인덱스 구축 완료: {LayerName}, {FeatureCount}개 피처", layerName, _featureCount);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Grid 인덱스 구축 중 오류 발생: {LayerName}", layerName);
                return false;
            }
        }

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색합니다
        /// </summary>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <returns>교차하는 피처 ID 목록</returns>
        public async Task<List<long>> QueryIntersectingFeaturesAsync(SpatialEnvelope searchEnvelope)
        {
            return await Task.Run(() =>
            {
                var results = new HashSet<long>(); // 중복 제거를 위해 HashSet 사용
                
                if (_grid == null || _bounds == null)
                    return results.ToList();

                // 검색 범위와 교차하는 격자 셀들 찾기
                var startCol = Math.Max(0, (int)Math.Floor((searchEnvelope.MinX - _bounds.MinX) / _cellWidth));
                var endCol = Math.Min(_gridWidth - 1, (int)Math.Floor((searchEnvelope.MaxX - _bounds.MinX) / _cellWidth));
                var startRow = Math.Max(0, (int)Math.Floor((searchEnvelope.MinY - _bounds.MinY) / _cellHeight));
                var endRow = Math.Min(_gridHeight - 1, (int)Math.Floor((searchEnvelope.MaxY - _bounds.MinY) / _cellHeight));

                // 해당 격자 셀들에서 피처 검색
                for (int row = startRow; row <= endRow; row++)
                {
                    for (int col = startCol; col <= endCol; col++)
                    {
                        var cell = _grid[row, col];
                        if (cell != null && cell.FeatureIds.Count > 0)
                        {
                            foreach (var featureId in cell.FeatureIds)
                            {
                                results.Add(featureId);
                            }
                        }
                    }
                }
                
                _logger.LogDebug("공간 질의 완료: {ResultCount}개 피처 검색됨 (격자 범위: {StartRow}-{EndRow}, {StartCol}-{EndCol})", 
                    results.Count, startRow, endRow, startCol, endCol);
                
                return results.ToList();
            });
        }

        /// <summary>
        /// 인덱스에 포함된 피처 수를 반환합니다
        /// </summary>
        /// <returns>피처 수</returns>
        public int GetFeatureCount()
        {
            return _featureCount;
        }

        /// <summary>
        /// 인덱스를 초기화합니다
        /// </summary>
        public void Clear()
        {
            _grid = null;
            _featureCount = 0;
            _bounds = null;
            _cellWidth = 0;
            _cellHeight = 0;
            _featureEnvelopes.Clear();
            _logger.LogDebug("Grid 인덱스가 초기화되었습니다");
        }

        /// <summary>
        /// 격자 초기화
        /// </summary>
        private void InitializeGrid()
        {
            _grid = new GridCell[_gridHeight, _gridWidth];
            _cellWidth = _bounds.Width / _gridWidth;
            _cellHeight = _bounds.Height / _gridHeight;

            for (int row = 0; row < _gridHeight; row++)
            {
                for (int col = 0; col < _gridWidth; col++)
                {
                    var cellMinX = _bounds.MinX + col * _cellWidth;
                    var cellMinY = _bounds.MinY + row * _cellHeight;
                    var cellMaxX = cellMinX + _cellWidth;
                    var cellMaxY = cellMinY + _cellHeight;

                    var cellBounds = new SpatialEnvelope(cellMinX, cellMinY, cellMaxX, cellMaxY);
                    _grid[row, col] = new GridCell(cellBounds);
                }
            }

            _logger.LogDebug("격자 초기화 완료: {GridWidth}x{GridHeight}, 셀 크기: {CellWidth:F2}x{CellHeight:F2}", 
                _gridWidth, _gridHeight, _cellWidth, _cellHeight);
        }

        /// <summary>
        /// 피처를 Grid에 삽입
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <param name="envelope">피처의 공간 범위</param>
        private async Task InsertFeatureAsync(long featureId, SpatialEnvelope envelope)
        {
            await Task.Run(() =>
            {
                if (_grid == null || _bounds == null)
                    return;

                // 피처가 교차하는 격자 셀들 찾기
                var startCol = Math.Max(0, (int)Math.Floor((envelope.MinX - _bounds.MinX) / _cellWidth));
                var endCol = Math.Min(_gridWidth - 1, (int)Math.Floor((envelope.MaxX - _bounds.MinX) / _cellWidth));
                var startRow = Math.Max(0, (int)Math.Floor((envelope.MinY - _bounds.MinY) / _cellHeight));
                var endRow = Math.Min(_gridHeight - 1, (int)Math.Floor((envelope.MaxY - _bounds.MinY) / _cellHeight));

                // 해당 격자 셀들에 피처 추가
                for (int row = startRow; row <= endRow; row++)
                {
                    for (int col = startCol; col <= endCol; col++)
                    {
                        var cell = _grid[row, col];
                        if (cell != null && cell.Bounds.Intersects(envelope))
                        {
                            cell.FeatureIds.Add(featureId);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 특정 피처의 공간 범위를 반환
        /// </summary>
        /// <param name="featureId">피처 ID</param>
        /// <returns>공간 범위</returns>
        public SpatialEnvelope GetFeatureEnvelope(long featureId)
        {
            return _featureEnvelopes.TryGetValue(featureId, out var envelope) ? envelope : null;
        }

        /// <summary>
        /// 격자 통계 로깅
        /// </summary>
        private void LogGridStatistics()
        {
            if (_grid == null)
                return;

            int totalCells = _gridWidth * _gridHeight;
            int emptyCells = 0;
            int maxFeaturesInCell = 0;
            int totalFeatureReferences = 0;

            for (int row = 0; row < _gridHeight; row++)
            {
                for (int col = 0; col < _gridWidth; col++)
                {
                    var cell = _grid[row, col];
                    if (cell == null || cell.FeatureIds.Count == 0)
                    {
                        emptyCells++;
                    }
                    else
                    {
                        maxFeaturesInCell = Math.Max(maxFeaturesInCell, cell.FeatureIds.Count);
                        totalFeatureReferences += cell.FeatureIds.Count;
                    }
                }
            }

            double avgFeaturesPerCell = totalFeatureReferences / (double)(totalCells - emptyCells);
            double cellUtilization = (totalCells - emptyCells) / (double)totalCells * 100;

            _logger.LogInformation("격자 통계 - 총 셀: {TotalCells}, 빈 셀: {EmptyCells}, " +
                                 "셀 활용률: {CellUtilization:F1}%, 셀당 평균 피처 수: {AvgFeatures:F1}, " +
                                 "최대 피처 수: {MaxFeatures}",
                totalCells, emptyCells, cellUtilization, avgFeaturesPerCell, maxFeaturesInCell);
        }

        /// <summary>
        /// 인덱스 통계 정보를 반환
        /// </summary>
        /// <returns>통계 정보</returns>
        public Dictionary<string, object> GetStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["FeatureCount"] = _featureCount,
                ["GridWidth"] = _gridWidth,
                ["GridHeight"] = _gridHeight,
                ["IndexType"] = "Grid"
            };

            if (_bounds != null)
            {
                stats["Bounds"] = _bounds;
                stats["CellWidth"] = _cellWidth;
                stats["CellHeight"] = _cellHeight;
            }

            if (_grid != null)
            {
                int totalCells = _gridWidth * _gridHeight;
                int emptyCells = 0;
                int maxFeaturesInCell = 0;
                int totalFeatureReferences = 0;

                for (int row = 0; row < _gridHeight; row++)
                {
                    for (int col = 0; col < _gridWidth; col++)
                    {
                        var cell = _grid[row, col];
                        if (cell == null || cell.FeatureIds.Count == 0)
                        {
                            emptyCells++;
                        }
                        else
                        {
                            maxFeaturesInCell = Math.Max(maxFeaturesInCell, cell.FeatureIds.Count);
                            totalFeatureReferences += cell.FeatureIds.Count;
                        }
                    }
                }

                stats["TotalCells"] = totalCells;
                stats["EmptyCells"] = emptyCells;
                stats["CellUtilization"] = (totalCells - emptyCells) / (double)totalCells * 100;
                stats["MaxFeaturesInCell"] = maxFeaturesInCell;
                stats["TotalFeatureReferences"] = totalFeatureReferences;
                
                if (totalCells > emptyCells)
                {
                    stats["AvgFeaturesPerCell"] = totalFeatureReferences / (double)(totalCells - emptyCells);
                }
            }

            return stats;
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            Clear();
            _logger.LogDebug("Grid 인덱스 리소스가 해제되었습니다");
        }
    }

    /// <summary>
    /// 격자 셀
    /// </summary>
    internal class GridCell
    {
        /// <summary>
        /// 셀의 공간 범위
        /// </summary>
        public SpatialEnvelope Bounds { get; private set; }

        /// <summary>
        /// 셀에 포함된 피처 ID들
        /// </summary>
        public List<long> FeatureIds { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="bounds">셀 범위</param>
        public GridCell(SpatialEnvelope bounds)
        {
            Bounds = bounds;
            FeatureIds = new List<long>();
        }
    }
}
