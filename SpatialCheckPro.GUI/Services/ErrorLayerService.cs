using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지도 뷰어 오류 레이어 관리 서비스 구현
    /// </summary>
    public class ErrorLayerService : IErrorLayerService
    {
        private readonly ILogger<ErrorLayerService> _logger;
        private readonly IErrorSymbolService _symbolService;
        private readonly ISpatialIndexService _spatialIndexService;
        
        private List<ErrorFeature> _errorFeatures = new List<ErrorFeature>();
        private List<ErrorFeature> _filteredFeatures = new List<ErrorFeature>();
        private List<ErrorCluster> _clusters = new List<ErrorCluster>();
        private readonly HashSet<string> _highlightedFeatures = new HashSet<string>();
        
        private int _mapWidth = 800;
        private int _mapHeight = 600;
        private bool _isLayerVisible = true;
        private double _layerOpacity = 1.0;
        private bool _isClusteringEnabled = false;
        private double _clusterDistance = 50.0;
        
        private ErrorLayerStatistics _statistics = new ErrorLayerStatistics();
        private readonly List<double> _renderTimes = new List<double>();

        /// <summary>
        /// ErrorLayerService 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="symbolService">심볼 서비스</param>
        /// <param name="spatialIndexService">공간 인덱스 서비스</param>
        public ErrorLayerService(ILogger<ErrorLayerService> logger, 
            IErrorSymbolService symbolService, 
            ISpatialIndexService spatialIndexService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _symbolService = symbolService ?? throw new ArgumentNullException(nameof(symbolService));
            _spatialIndexService = spatialIndexService ?? throw new ArgumentNullException(nameof(spatialIndexService));
            
            _statistics.CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 현재 렌더링된 ErrorFeature 개수
        /// </summary>
        public int RenderedFeatureCount => _filteredFeatures.Count;

        /// <summary>
        /// 오류 레이어 가시성 상태
        /// </summary>
        public bool IsLayerVisible => _isLayerVisible;

        /// <summary>
        /// 클러스터링 활성화 상태
        /// </summary>
        public bool IsClusteringEnabled => _isClusteringEnabled;

        /// <summary>
        /// 오류 레이어 초기화
        /// </summary>
        /// <param name="mapWidth">지도 너비</param>
        /// <param name="mapHeight">지도 높이</param>
        /// <returns>초기화 성공 여부</returns>
        public Task<bool> InitializeErrorLayerAsync(int mapWidth, int mapHeight)
        {
            try
            {
                _logger.LogInformation("오류 레이어 초기화 시작: {Width}x{Height}", mapWidth, mapHeight);

                _mapWidth = mapWidth;
                _mapHeight = mapHeight;
                
                ClearErrorLayer();
                
                _statistics = new ErrorLayerStatistics
                {
                    CreatedAt = DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow
                };

                _logger.LogInformation("오류 레이어 초기화 완료");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 레이어 초기화 실패");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// ErrorFeature 목록을 오류 레이어에 추가
        /// </summary>
        /// <param name="errorFeatures">ErrorFeature 목록</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>렌더링된 오류 개수</returns>
        public async Task<int> AddErrorFeaturesToLayerAsync(List<ErrorFeature> errorFeatures, MapBounds mapBounds, double zoomLevel)
        {
            try
            {
                _logger.LogInformation("ErrorFeature 추가 시작: {Count}개", errorFeatures.Count);

                _errorFeatures = new List<ErrorFeature>(errorFeatures);
                _statistics.TotalFeatureCount = _errorFeatures.Count;

                // 공간 인덱스 구축
                await _spatialIndexService.BuildSpatialIndexAsync(_errorFeatures);

                // 뷰포트 기반 필터링
                _filteredFeatures = await FilterFeaturesByViewport(mapBounds, zoomLevel);

                // 클러스터링 수행 (활성화된 경우)
                if (_isClusteringEnabled)
                {
                    _clusters = await CreateClusters(_filteredFeatures, mapBounds, zoomLevel);
                    _statistics.ClusterCount = _clusters.Count;
                }

                _statistics.RenderedFeatureCount = _filteredFeatures.Count;
                _statistics.LastUpdatedAt = DateTime.UtcNow;
                _statistics.CurrentBounds = mapBounds;
                _statistics.CurrentZoomLevel = zoomLevel;

                _logger.LogInformation("ErrorFeature 추가 완료: 렌더링 {Rendered}/{Total}개", 
                    _filteredFeatures.Count, _errorFeatures.Count);

                return _filteredFeatures.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 추가 실패");
                return 0;
            }
        }

        /// <summary>
        /// 오류 레이어 렌더링 (이미지 생성)
        /// </summary>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>렌더링된 오류 레이어 이미지</returns>
        public async Task<ImageSource?> RenderErrorLayerAsync(MapBounds mapBounds, double zoomLevel)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                if (!_isLayerVisible || !_filteredFeatures.Any())
                {
                    return null;
                }

                _logger.LogDebug("오류 레이어 렌더링 시작: {Count}개 피처", _filteredFeatures.Count);

                // WPF DrawingVisual을 사용한 렌더링
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    // 클러스터 렌더링
                    if (_isClusteringEnabled && _clusters.Any())
                    {
                        await RenderClusters(context, _clusters, mapBounds, zoomLevel);
                    }
                    else
                    {
                        // 개별 ErrorFeature 렌더링
                        await RenderErrorFeatures(context, _filteredFeatures, mapBounds, zoomLevel);
                    }

                    // 하이라이트된 피처 렌더링
                    await RenderHighlightedFeatures(context, mapBounds, zoomLevel);
                }

                // 비트맵으로 변환
                var renderBitmap = new RenderTargetBitmap(_mapWidth, _mapHeight, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(visual);

                // 투명도 적용
                if (_layerOpacity < 1.0)
                {
                    var opacityBitmap = ApplyOpacity(renderBitmap, _layerOpacity);
                    stopwatch.Stop();
                    UpdateRenderStatistics(stopwatch.Elapsed.TotalMilliseconds);
                    return opacityBitmap;
                }

                stopwatch.Stop();
                UpdateRenderStatistics(stopwatch.Elapsed.TotalMilliseconds);

                _logger.LogDebug("오류 레이어 렌더링 완료: {Time:F2}ms", stopwatch.Elapsed.TotalMilliseconds);
                return renderBitmap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 레이어 렌더링 실패");
                stopwatch.Stop();
                return null;
            }
        }

        /// <summary>
        /// 특정 ErrorFeature 하이라이트
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>하이라이트 성공 여부</returns>
        public Task<bool> HighlightErrorFeatureAsync(string errorFeatureId, MapBounds mapBounds, double zoomLevel)
        {
            try
            {
                _highlightedFeatures.Add(errorFeatureId);
                _logger.LogDebug("ErrorFeature 하이라이트 추가: {Id}", errorFeatureId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 하이라이트 실패: {Id}", errorFeatureId);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 모든 하이라이트 제거
        /// </summary>
        public void ClearHighlights()
        {
            var count = _highlightedFeatures.Count;
            _highlightedFeatures.Clear();
            _logger.LogDebug("모든 하이라이트 제거: {Count}개", count);
        }

        /// <summary>
        /// 오류 레이어 가시성 설정
        /// </summary>
        /// <param name="isVisible">가시성 여부</param>
        public void SetLayerVisibility(bool isVisible)
        {
            _isLayerVisible = isVisible;
            _logger.LogDebug("오류 레이어 가시성 설정: {Visible}", isVisible);
        }

        /// <summary>
        /// 오류 레이어 투명도 설정
        /// </summary>
        /// <param name="opacity">투명도 (0.0 ~ 1.0)</param>
        public void SetLayerOpacity(double opacity)
        {
            _layerOpacity = Math.Max(0.0, Math.Min(1.0, opacity));
            _logger.LogDebug("오류 레이어 투명도 설정: {Opacity:F2}", _layerOpacity);
        }

        /// <summary>
        /// 필터 조건에 따른 ErrorFeature 표시/숨김
        /// </summary>
        /// <param name="filter">필터 조건</param>
        /// <returns>필터링된 ErrorFeature 개수</returns>
        public Task<int> ApplyFilterAsync(ErrorFeatureFilter filter)
        {
            try
            {
                _logger.LogDebug("ErrorFeature 필터 적용 시작");

                if (filter.IsEmpty)
                {
                    _filteredFeatures = new List<ErrorFeature>(_errorFeatures);
                }
                else
                {
                    _filteredFeatures = _errorFeatures.Where(feature => MatchesFilter(feature, filter)).ToList();
                }

                _statistics.FilteredFeatureCount = _errorFeatures.Count - _filteredFeatures.Count;
                _statistics.RenderedFeatureCount = _filteredFeatures.Count;

                _logger.LogDebug("ErrorFeature 필터 적용 완료: {Filtered}/{Total}개", 
                    _filteredFeatures.Count, _errorFeatures.Count);

                return Task.FromResult(_filteredFeatures.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 필터 적용 실패");
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// 클러스터링 활성화/비활성화
        /// </summary>
        /// <param name="enableClustering">클러스터링 사용 여부</param>
        /// <param name="clusterDistance">클러스터링 거리 (픽셀)</param>
        public void SetClusteringEnabled(bool enableClustering, double clusterDistance = 50.0)
        {
            _isClusteringEnabled = enableClustering;
            _clusterDistance = clusterDistance;
            _logger.LogDebug("클러스터링 설정: {Enabled}, 거리: {Distance}px", enableClustering, clusterDistance);
        }

        /// <summary>
        /// 지도 좌표를 화면 좌표로 변환
        /// </summary>
        /// <param name="mapX">지도 X 좌표</param>
        /// <param name="mapY">지도 Y 좌표</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>화면 좌표</returns>
        public (double ScreenX, double ScreenY) MapToScreen(double mapX, double mapY, MapBounds mapBounds)
        {
            var screenX = (mapX - mapBounds.MinX) / mapBounds.Width * _mapWidth;
            var screenY = _mapHeight - (mapY - mapBounds.MinY) / mapBounds.Height * _mapHeight; // Y축 뒤집기
            return (screenX, screenY);
        }

        /// <summary>
        /// 화면 좌표를 지도 좌표로 변환
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>지도 좌표</returns>
        public (double MapX, double MapY) ScreenToMap(double screenX, double screenY, MapBounds mapBounds)
        {
            var mapX = mapBounds.MinX + (screenX / _mapWidth) * mapBounds.Width;
            var mapY = mapBounds.MinY + ((_mapHeight - screenY) / _mapHeight) * mapBounds.Height; // Y축 뒤집기
            return (mapX, mapY);
        }

        /// <summary>
        /// 화면 좌표에서 ErrorFeature 검색
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="tolerance">허용 오차 (픽셀)</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>검색된 ErrorFeature 목록</returns>
        public async Task<List<ErrorFeature>> HitTestAsync(double screenX, double screenY, double tolerance, MapBounds mapBounds)
        {
            try
            {
                // 화면 좌표를 지도 좌표로 변환
                var (mapX, mapY) = ScreenToMap(screenX, screenY, mapBounds);
                
                // 허용 오차를 지도 단위로 변환
                var mapTolerance = (tolerance / _mapWidth) * mapBounds.Width;

                // 공간 인덱스를 사용한 검색
                var nearbyFeatures = await _spatialIndexService.SearchWithinRadiusAsync(mapX, mapY, mapTolerance);

                _logger.LogDebug("HitTest 완료: 화면({ScreenX}, {ScreenY}) -> 지도({MapX:F2}, {MapY:F2}), 결과 {Count}개", 
                    screenX, screenY, mapX, mapY, nearbyFeatures.Count);

                return nearbyFeatures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HitTest 실패: 화면({ScreenX}, {ScreenY})", screenX, screenY);
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// 오류 레이어 통계 정보 조회
        /// </summary>
        /// <returns>레이어 통계</returns>
        public async Task<ErrorLayerStatistics> GetLayerStatisticsAsync()
        {
            return await Task.FromResult(new ErrorLayerStatistics
            {
                TotalFeatureCount = _statistics.TotalFeatureCount,
                RenderedFeatureCount = _statistics.RenderedFeatureCount,
                FilteredFeatureCount = _statistics.FilteredFeatureCount,
                ClusterCount = _statistics.ClusterCount,
                AverageRenderTimeMs = _statistics.AverageRenderTimeMs,
                LastRenderTimeMs = _statistics.LastRenderTimeMs,
                TotalRenderCount = _statistics.TotalRenderCount,
                MemoryUsageBytes = _statistics.MemoryUsageBytes,
                CurrentBounds = _statistics.CurrentBounds,
                CurrentZoomLevel = _statistics.CurrentZoomLevel,
                CreatedAt = _statistics.CreatedAt,
                LastUpdatedAt = _statistics.LastUpdatedAt
            });
        }

        /// <summary>
        /// 오류 레이어 초기화
        /// </summary>
        public void ClearErrorLayer()
        {
            _errorFeatures.Clear();
            _filteredFeatures.Clear();
            _clusters.Clear();
            _highlightedFeatures.Clear();
            _spatialIndexService.ClearIndex();
            
            _statistics = new ErrorLayerStatistics
            {
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            };
            
            _logger.LogDebug("오류 레이어 초기화 완료");
        }

        /// <summary>
        /// 뷰포트 기반 ErrorFeature 필터링
        /// </summary>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>필터링된 ErrorFeature 목록</returns>
        private async Task<List<ErrorFeature>> FilterFeaturesByViewport(MapBounds mapBounds, double zoomLevel)
        {
            // 뷰포트 확장 (버퍼 추가)
            var buffer = Math.Max(mapBounds.Width, mapBounds.Height) * 0.1; // 10% 버퍼
            var expandedBounds = new MapBounds
            {
                MinX = mapBounds.MinX - buffer,
                MinY = mapBounds.MinY - buffer,
                MaxX = mapBounds.MaxX + buffer,
                MaxY = mapBounds.MaxY + buffer
            };

            return await _spatialIndexService.SearchWithinBoundsAsync(
                expandedBounds.MinX, expandedBounds.MinY, 
                expandedBounds.MaxX, expandedBounds.MaxY);
        }

        /// <summary>
        /// 클러스터 생성
        /// </summary>
        /// <param name="features">ErrorFeature 목록</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>생성된 클러스터 목록</returns>
        private async Task<List<ErrorCluster>> CreateClusters(List<ErrorFeature> features, MapBounds mapBounds, double zoomLevel)
        {
            return await Task.Run(() =>
            {
                var clusters = new List<ErrorCluster>();
                var processedFeatures = new HashSet<string>();

                foreach (var feature in features)
                {
                    if (processedFeatures.Contains(feature.Id))
                        continue;

                    var (screenX, screenY) = MapToScreen(feature.X, feature.Y, mapBounds);
                    var nearbyFeatures = new List<ErrorFeature> { feature };
                    processedFeatures.Add(feature.Id);

                    // 클러스터 거리 내의 다른 피처 찾기
                    foreach (var otherFeature in features)
                    {
                        if (processedFeatures.Contains(otherFeature.Id))
                            continue;

                        var (otherScreenX, otherScreenY) = MapToScreen(otherFeature.X, otherFeature.Y, mapBounds);
                        var distance = Math.Sqrt(Math.Pow(screenX - otherScreenX, 2) + Math.Pow(screenY - otherScreenY, 2));

                        if (distance <= _clusterDistance)
                        {
                            nearbyFeatures.Add(otherFeature);
                            processedFeatures.Add(otherFeature.Id);
                        }
                    }

                    // 클러스터 생성 (2개 이상인 경우만)
                    if (nearbyFeatures.Count > 1)
                    {
                        var centerX = nearbyFeatures.Average(f => f.X);
                        var centerY = nearbyFeatures.Average(f => f.Y);

                        clusters.Add(new ErrorCluster
                        {
                            Id = Guid.NewGuid().ToString(),
                            CenterX = centerX,
                            CenterY = centerY,
                            Errors = nearbyFeatures
                        });
                    }
                }

                return clusters;
            });
        }

        /// <summary>
        /// ErrorFeature 렌더링
        /// </summary>
        /// <param name="context">드로잉 컨텍스트</param>
        /// <param name="features">ErrorFeature 목록</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        private async Task RenderErrorFeatures(DrawingContext context, List<ErrorFeature> features, MapBounds mapBounds, double zoomLevel)
        {
            await Task.Run(() =>
            {
                foreach (var feature in features)
                {
                    var symbol = _symbolService.CreateSymbol(feature, zoomLevel);
                    var (screenX, screenY) = MapToScreen(feature.X, feature.Y, mapBounds);
                    
                    RenderSymbol(context, symbol, screenX, screenY);
                }
            });
        }

        /// <summary>
        /// 클러스터 렌더링
        /// </summary>
        /// <param name="context">드로잉 컨텍스트</param>
        /// <param name="clusters">클러스터 목록</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        private async Task RenderClusters(DrawingContext context, List<ErrorCluster> clusters, MapBounds mapBounds, double zoomLevel)
        {
            await Task.Run(() =>
            {
                foreach (var cluster in clusters)
                {
                    var symbol = _symbolService.CreateClusterSymbol(cluster, zoomLevel);
                    var (screenX, screenY) = MapToScreen(cluster.CenterX, cluster.CenterY, mapBounds);
                    
                    RenderSymbol(context, symbol, screenX, screenY);
                }
            });
        }

        /// <summary>
        /// 하이라이트된 피처 렌더링
        /// </summary>
        /// <param name="context">드로잉 컨텍스트</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        private async Task RenderHighlightedFeatures(DrawingContext context, MapBounds mapBounds, double zoomLevel)
        {
            await Task.Run(() =>
            {
                foreach (var featureId in _highlightedFeatures)
                {
                    var feature = _filteredFeatures.FirstOrDefault(f => f.Id == featureId);
                    if (feature != null)
                    {
                        var highlightSymbol = _symbolService.CreateHighlightSymbol(feature, zoomLevel);
                        var (screenX, screenY) = MapToScreen(feature.X, feature.Y, mapBounds);
                        
                        RenderSymbol(context, highlightSymbol, screenX, screenY);
                    }
                }
            });
        }

        /// <summary>
        /// 심볼 렌더링
        /// </summary>
        /// <param name="context">드로잉 컨텍스트</param>
        /// <param name="symbol">ErrorSymbol</param>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        private void RenderSymbol(DrawingContext context, ErrorSymbol symbol, double screenX, double screenY)
        {
            if (!symbol.IsVisible)
                return;

            var center = new System.Windows.Point(screenX, screenY);
            var radius = symbol.Size / 2.0;

            var fillBrush = new SolidColorBrush(symbol.FillColor) { Opacity = symbol.Opacity };
            var strokeBrush = new SolidColorBrush(symbol.StrokeColor);
            var pen = new Pen(strokeBrush, symbol.StrokeWidth);

            switch (symbol.Shape)
            {
                case "Circle":
                    context.DrawEllipse(fillBrush, pen, center, radius, radius);
                    break;
                    
                case "Square":
                    var rect = new Rect(screenX - radius, screenY - radius, symbol.Size, symbol.Size);
                    context.DrawRectangle(fillBrush, pen, rect);
                    break;
                    
                case "Triangle":
                    var triangle = CreateTriangleGeometry(center, radius);
                    context.DrawGeometry(fillBrush, pen, triangle);
                    break;
                    
                case "Diamond":
                    var diamond = CreateDiamondGeometry(center, radius);
                    context.DrawGeometry(fillBrush, pen, diamond);
                    break;
                    
                default:
                    context.DrawEllipse(fillBrush, pen, center, radius, radius);
                    break;
            }

            // 텍스트 렌더링 (클러스터 등)
            if (!string.IsNullOrEmpty(symbol.Text))
            {
                var textBrush = new SolidColorBrush(symbol.TextColor);
                var typeface = new Typeface("Arial");
                var formattedText = new FormattedText(symbol.Text, 
                    System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, typeface, symbol.TextSize, textBrush, 96);
                
                var textCenter = new System.Windows.Point(screenX - formattedText.Width / 2, screenY - formattedText.Height / 2);
                context.DrawText(formattedText, textCenter);
            }
        }

        /// <summary>
        /// 삼각형 지오메트리 생성
        /// </summary>
        /// <param name="center">중심점</param>
        /// <param name="radius">반지름</param>
        /// <returns>삼각형 지오메트리</returns>
        private Geometry CreateTriangleGeometry(System.Windows.Point center, double radius)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new System.Windows.Point(center.X, center.Y - radius) };
            
            figure.Segments.Add(new LineSegment(new System.Windows.Point(center.X - radius * 0.866, center.Y + radius * 0.5), true));
            figure.Segments.Add(new LineSegment(new System.Windows.Point(center.X + radius * 0.866, center.Y + radius * 0.5), true));
            figure.IsClosed = true;
            
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>
        /// 다이아몬드 지오메트리 생성
        /// </summary>
        /// <param name="center">중심점</param>
        /// <param name="radius">반지름</param>
        /// <returns>다이아몬드 지오메트리</returns>
        private Geometry CreateDiamondGeometry(System.Windows.Point center, double radius)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new System.Windows.Point(center.X, center.Y - radius) };
            
            figure.Segments.Add(new LineSegment(new System.Windows.Point(center.X + radius, center.Y), true));
            figure.Segments.Add(new LineSegment(new System.Windows.Point(center.X, center.Y + radius), true));
            figure.Segments.Add(new LineSegment(new System.Windows.Point(center.X - radius, center.Y), true));
            figure.IsClosed = true;
            
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>
        /// 투명도 적용
        /// </summary>
        /// <param name="source">원본 비트맵</param>
        /// <param name="opacity">투명도</param>
        /// <returns>투명도가 적용된 비트맵</returns>
        private BitmapSource ApplyOpacity(BitmapSource source, double opacity)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var brush = new ImageBrush(source) { Opacity = opacity };
                context.DrawRectangle(brush, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }

            var renderBitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(visual);
            return renderBitmap;
        }

        /// <summary>
        /// 필터 조건 매칭 확인
        /// </summary>
        /// <param name="feature">ErrorFeature</param>
        /// <param name="filter">필터 조건</param>
        /// <returns>매칭 여부</returns>
        private bool MatchesFilter(ErrorFeature feature, ErrorFeatureFilter filter)
        {
            if (filter.Severities != null && filter.Severities.Any() && !filter.Severities.Contains(feature.Severity))
                return false;

            if (filter.ErrorTypes != null && filter.ErrorTypes.Any() && !filter.ErrorTypes.Contains(feature.ErrorType))
                return false;

            if (filter.Statuses != null && filter.Statuses.Any() && !filter.Statuses.Contains(feature.Status))
                return false;

            if (filter.SourceClasses != null && filter.SourceClasses.Any() && !filter.SourceClasses.Contains(feature.SourceClass))
                return false;

            if (filter.CreatedAfter.HasValue && feature.CreatedAt < filter.CreatedAfter.Value)
                return false;

            if (filter.CreatedBefore.HasValue && feature.CreatedAt > filter.CreatedBefore.Value)
                return false;

            if (filter.SpatialBounds != null && !filter.SpatialBounds.Contains(feature.X, feature.Y))
                return false;

            if (!string.IsNullOrEmpty(filter.SearchText) && 
                !feature.Message.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// 클러스터 범위 계산
        /// </summary>
        /// <param name="features">ErrorFeature 목록</param>
        /// <returns>클러스터 범위</returns>
        private BoundingBox CalculateClusterBounds(List<ErrorFeature> features)
        {
            return new BoundingBox
            {
                MinX = features.Min(f => f.X),
                MinY = features.Min(f => f.Y),
                MaxX = features.Max(f => f.X),
                MaxY = features.Max(f => f.Y)
            };
        }

        /// <summary>
        /// 렌더링 통계 업데이트
        /// </summary>
        /// <param name="renderTimeMs">렌더링 시간</param>
        private void UpdateRenderStatistics(double renderTimeMs)
        {
            _renderTimes.Add(renderTimeMs);
            _statistics.LastRenderTimeMs = renderTimeMs;
            _statistics.TotalRenderCount++;
            
            if (_renderTimes.Count > 100) // 최근 100개만 유지
            {
                _renderTimes.RemoveAt(0);
            }
            
            _statistics.AverageRenderTimeMs = _renderTimes.Average();
            _statistics.MemoryUsageBytes = GC.GetTotalMemory(false);
        }
    }
}