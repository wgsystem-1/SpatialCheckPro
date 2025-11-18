using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.GDAL;
using OSGeo.OSR;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Shapes;
using SpatialCheckPro.GUI.Models;
using SpatialCheckPro.GUI.Extensions;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// GDAL 기반 지도 서비스 - QC_Errors 오버레이 및 심볼 규칙 지원
    /// </summary>
    public class GdalMapService : IDisposable
    {
        private readonly ILogger<GdalMapService> _logger;
        private readonly Dictionary<string, DataSource> _loadedDataSources;
        private readonly Dictionary<string, Layer> _loadedLayers;
        private readonly Dictionary<string, Layer> _qcErrorLayers; // QC_Errors 레이어들
        private readonly Dictionary<string, SymbolRule> _symbolRules; // 심볼 규칙
        private readonly HashSet<string> _selectedErrorIds; // 선택된 오류 ID들
        private readonly HashSet<string> _highlightedErrorIds; // 하이라이트된 오류 ID들
        private Envelope? _currentExtent;
        // 사용되지 않는 필드와 이벤트들 제거됨 (CS0414, CS0067 경고 해결)

        /// <summary>
        /// 지도 범위 변경 이벤트
        /// </summary>
        public event EventHandler<MapExtentChangedEventArgs>? ExtentChanged;

        public GdalMapService(ILogger<GdalMapService> logger)
        {
            _logger = logger;
            _loadedDataSources = new Dictionary<string, DataSource>();
            _loadedLayers = new Dictionary<string, Layer>();
            _qcErrorLayers = new Dictionary<string, Layer>();
            _symbolRules = new Dictionary<string, SymbolRule>();
            _selectedErrorIds = new HashSet<string>();
            _highlightedErrorIds = new HashSet<string>();

            // GDAL 초기화
            InitializeGdal();
            
            // 기본 심볼 규칙 초기화
            InitializeDefaultSymbolRules();
        }

        /// <summary>
        /// GDAL 라이브러리 초기화 (안전하게)
        /// </summary>
        private void InitializeGdal()
        {
            try
            {
                _logger.LogInformation("GDAL 라이브러리 초기화 시작");
                
                // GDAL 드라이버 등록
                Gdal.AllRegister();
                Ogr.RegisterAll();

                _logger.LogInformation("GDAL 라이브러리 초기화 완료");
                
                try
                {
                    var version = Gdal.VersionInfo("RELEASE_NAME");
                    _logger.LogInformation("GDAL 버전: {Version}", version);
                }
                catch (Exception versionEx)
                {
                    _logger.LogWarning(versionEx, "GDAL 버전 정보 조회 실패");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 초기화 실패");
                throw new InvalidOperationException("GDAL 라이브러리 초기화에 실패했습니다. GDAL이 올바르게 설치되었는지 확인해주세요.", ex);
            }
        }

        /// <summary>
        /// 기본 심볼 규칙 초기화
        /// </summary>
        private void InitializeDefaultSymbolRules()
        {
            try
            {
                // 기본 심볼 규칙
                _symbolRules["DEFAULT"] = new SymbolRule
                {
                    MarkerStyle = "CIRCLE",
                    Size = 8,
                    FillColor = System.Drawing.Color.Blue,
                    OutlineColor = System.Drawing.Color.Black,
                    OutlineWidth = 1,
                    Transparency = 0.8
                };

                // 오류 코드별 심볼 규칙
                _symbolRules["DUP001"] = new SymbolRule
                {
                    MarkerStyle = "SQUARE",
                    Size = 10,
                    FillColor = System.Drawing.Color.Red,
                    OutlineColor = System.Drawing.Color.DarkRed,
                    OutlineWidth = 2,
                    Transparency = 0.9
                };

                _symbolRules["OVL001"] = new SymbolRule
                {
                    MarkerStyle = "TRIANGLE",
                    Size = 12,
                    FillColor = System.Drawing.Color.Orange,
                    OutlineColor = System.Drawing.Color.DarkOrange,
                    OutlineWidth = 2,
                    Transparency = 0.9
                };

                _logger.LogDebug("기본 심볼 규칙 초기화 완료: {Count}개", _symbolRules.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본 심볼 규칙 초기화 실패");
            }
        }

        /// <summary>
        /// File Geodatabase의 모든 레이어들을 로드합니다 (QC_Errors + 실제 공간데이터)
        /// </summary>
        public async Task<bool> LoadQcErrorLayersAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("File Geodatabase 레이어 로드 시작: {Path}", gdbPath);

                // 파일 존재 확인
                if (!File.Exists(gdbPath) && !Directory.Exists(gdbPath))
                {
                    _logger.LogError("File Geodatabase 파일이 존재하지 않습니다: {Path}", gdbPath);
                    return false;
                }

                return await Task.Run(() =>
                {
                    try
                    {
                        // 기존 데이터 정리
                        ClearLoadedData();

                        // GDAL 3.6+ 버전에서는 OpenFileGDB가 읽기/쓰기 모두 지원
                        var openFileGdbDriver = Ogr.GetDriverByName("OpenFileGDB");
                        var fileGdbDriver = Ogr.GetDriverByName("FileGDB");
                        OSGeo.OGR.Driver driver;
                        
                        if (openFileGdbDriver != null)
                        {
                            driver = openFileGdbDriver;
                            _logger.LogInformation("OpenFileGDB 드라이버를 사용합니다 (GDAL 3.6+ 읽기/쓰기 지원)");
                        }
                        else if (fileGdbDriver != null)
                        {
                            driver = fileGdbDriver;
                            _logger.LogInformation("FileGDB 드라이버를 사용합니다 (읽기/쓰기 지원)");
                        }
                        else
                        {
                            _logger.LogError("OpenFileGDB 또는 FileGDB 드라이버를 찾을 수 없습니다. GDAL이 올바르게 설치되었는지 확인해주세요.");
                            return false;
                        }

                        // PROJ 환경 변수 재설정 (PostgreSQL PostGIS와의 충돌 방지)
                        var appDir = AppDomain.CurrentDomain.BaseDirectory;
                        var projLibPath = System.IO.Path.Combine(appDir, "gdal", "share");
                        if (System.IO.Directory.Exists(projLibPath))
                        {
                            var resolvedProjPath = ProjEnvironmentManager.ConfigureFromSharePath(projLibPath, _logger);

                            // 시스템 PATH에서 PostgreSQL 경로 제거
                            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                            var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                            var filteredPaths = paths.Where(p => !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && !p.Contains("postgis", StringComparison.OrdinalIgnoreCase)).ToArray();
                            Environment.SetEnvironmentVariable("PATH", string.Join(";", filteredPaths));
                            
                            Environment.SetEnvironmentVariable("PROJ_DEBUG", "0");
                            Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", resolvedProjPath);
                            Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", resolvedProjPath);
                        }

                        // File Geodatabase 열기 (GDAL 3.6+ OpenFileGDB는 읽기/쓰기 지원)
                        var dataSource = driver.Open(gdbPath, 1); // 쓰기 모드 (1 = 쓰기 가능)
                        if (dataSource == null)
                        {
                            _logger.LogError("File Geodatabase 열기 실패: {Path}", gdbPath);
                            return false;
                        }

                        // 데이터소스 저장
                        _loadedDataSources[gdbPath] = dataSource;

                        bool anyLayerLoaded = false;
                        int totalLayers = dataSource.GetLayerCount();
                        
                        _logger.LogInformation("File Geodatabase 총 레이어 수: {Count}", totalLayers);

                        // 모든 레이어 로드
                        for (int i = 0; i < totalLayers; i++)
                        {
                            var layer = dataSource.GetLayerByIndex(i);
                            if (layer != null)
                            {
                                var layerName = layer.GetName();
                                var featureCount = layer.GetFeatureCount(0);
                                var geometryType = layer.GetGeomType();
                                
                                _logger.LogDebug("레이어 발견: {LayerName}, 피처 수: {Count}, 지오메트리 타입: {GeomType}", 
                                    layerName, featureCount, geometryType);

                                // QC_Errors 레이어와 일반 레이어 구분
                                if (layerName.StartsWith("QC_Errors", StringComparison.OrdinalIgnoreCase))
                                {
                                    _qcErrorLayers[layerName] = layer;
                                    _logger.LogInformation("QC_Errors 레이어 로드됨: {LayerName}, 피처 수: {Count}", 
                                        layerName, featureCount);
                                }
                                else
                                {
                                    _loadedLayers[layerName] = layer;
                                    _logger.LogInformation("공간데이터 레이어 로드됨: {LayerName}, 피처 수: {Count}", 
                                        layerName, featureCount);
                                }
                                
                                anyLayerLoaded = true;

                                // 레이어 범위 계산 및 업데이트
                                UpdateExtentFromLayer(layer);
                            }
                        }

                        if (anyLayerLoaded)
                        {
                            _logger.LogInformation("레이어 로드 완료: QC_Errors {QcCount}개, 공간데이터 {DataCount}개", 
                                _qcErrorLayers.Count, _loadedLayers.Count);

                            // 폴백: 레이어의 Envelope가 비어 _currentExtent가 계산되지 않은 경우, 피처 기반으로 전체 범위 계산
                            if (_currentExtent == null)
                            {
                                try
                                {
                                    var fallback = new Envelope();
                                    bool hasEnvelope = false;

                                    // 일반 레이어 + QC_Errors 모두 대상
                                    foreach (var layer in _loadedLayers.Values.Concat(_qcErrorLayers.Values))
                                    {
                                        try
                                        {
                                            layer.ResetReading();
                                            var feature = layer.GetNextFeature();
                                            if (feature != null)
                                            {
                                                try
                                                {
                                                    var geom = feature.GetGeometryRef();
                                                    if (geom != null)
                                                    {
                                                        var env = new Envelope();
                                                        geom.GetEnvelope(env);

                                                        if (!hasEnvelope)
                                                        {
                                                            fallback = new Envelope { MinX = env.MinX, MinY = env.MinY, MaxX = env.MaxX, MaxY = env.MaxY };
                                                            hasEnvelope = true;
                                                        }
                                                        else
                                                        {
                                                            fallback.MinX = Math.Min(fallback.MinX, env.MinX);
                                                            fallback.MinY = Math.Min(fallback.MinY, env.MinY);
                                                            fallback.MaxX = Math.Max(fallback.MaxX, env.MaxX);
                                                            fallback.MaxY = Math.Max(fallback.MaxY, env.MaxY);
                                                        }
                                                    }
                                                }
                                                finally
                                                {
                                                    feature.Dispose();
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogDebug(ex, "폴백 범위 계산 중 레이어 처리 오류: {Layer}", layer.GetName());
                                        }
                                    }

                                    if (hasEnvelope)
                                    {
                                        _currentExtent = fallback;
                                        _logger.LogInformation("폴백으로 전체 데이터 범위 설정: MinX={MinX:F2}, MinY={MinY:F2}, MaxX={MaxX:F2}, MaxY={MaxY:F2}", 
                                            fallback.MinX, fallback.MinY, fallback.MaxX, fallback.MaxY);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("폴백 범위 계산 실패: 피처가 없거나 지오메트리가 없음");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "폴백 전체 범위 계산 중 오류");
                                }
                            }

                            // 전체 범위 로그 출력
                            if (_currentExtent != null)
                            {
                                var extent = _currentExtent;
                                _logger.LogInformation("전체 데이터 범위: MinX={MinX:F2}, MinY={MinY:F2}, MaxX={MaxX:F2}, MaxY={MaxY:F2}", 
                                    extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
                            }

                            return true;
                        }
                        else
                        {
                            _logger.LogWarning("로드된 레이어가 없습니다");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "레이어 로드 실패: {Message}", ex.Message);
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_Errors 레이어 로드 실패: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 레이어에서 범위를 계산하여 전체 범위를 업데이트합니다
        /// </summary>
        private void UpdateExtentFromLayer(Layer layer)
        {
            try
            {
                var envelope = new Envelope();
                layer.GetExtent(envelope, 1); // 1 = force computation

                if (envelope.MinX != envelope.MaxX && envelope.MinY != envelope.MaxY)
                {
                    if (_currentExtent == null)
                    {
                        _currentExtent = envelope;
                    }
                    else
                    {
                        var current = _currentExtent;
                        _currentExtent = new Envelope
                        {
                            MinX = Math.Min(current.MinX, envelope.MinX),
                            MinY = Math.Min(current.MinY, envelope.MinY),
                            MaxX = Math.Max(current.MaxX, envelope.MaxX),
                            MaxY = Math.Max(current.MaxY, envelope.MaxY)
                        };
                    }

                    _logger.LogDebug("레이어 {LayerName} 범위: MinX={MinX:F2}, MinY={MinY:F2}, MaxX={MaxX:F2}, MaxY={MaxY:F2}", 
                        layer.GetName(), envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "레이어 {LayerName} 범위 계산 실패", layer.GetName());
            }
        }

        /// <summary>
        /// 로드된 데이터를 정리합니다
        /// </summary>
        private void ClearLoadedData()
        {
            try
            {
                // 기존 레이어들 정리
                _loadedLayers.Clear();
                _qcErrorLayers.Clear();
                _selectedErrorIds.Clear();
                _highlightedErrorIds.Clear();
                _currentExtent = null;

                // 데이터소스들 정리
                foreach (var dataSource in _loadedDataSources.Values)
                {
                    try
                    {
                        dataSource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "데이터소스 정리 중 오류 발생");
                    }
                }
                _loadedDataSources.Clear();

                _logger.LogDebug("로드된 데이터 정리 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "데이터 정리 실패");
            }
        }

        /// <summary>
        /// 오류 피처 선택
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        /// <param name="addToSelection">기존 선택에 추가할지 여부 (Ctrl+클릭)</param>
        public void SelectErrorFeature(string errorId, bool addToSelection = false)
        {
            try
            {
                if (string.IsNullOrEmpty(errorId)) return;

                if (!addToSelection)
                {
                    // 기존 선택 해제
                    _selectedErrorIds.Clear();
                }

                // 새로운 선택 추가
                _selectedErrorIds.Add(errorId);
                
                _logger.LogDebug("오류 피처 선택됨: {ErrorId}, 총 선택: {Count}개", errorId, _selectedErrorIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 피처 선택 실패: {ErrorId}", errorId);
            }
        }

        /// <summary>
        /// 현재 로드된 레이어들의 정보를 반환합니다
        /// </summary>
        public Dictionary<string, LayerInfo> GetLoadedLayersInfo()
        {
            var layersInfo = new Dictionary<string, LayerInfo>();

            try
            {
                // 일반 공간데이터 레이어들
                foreach (var kvp in _loadedLayers)
                {
                    var layer = kvp.Value;
                    var layerInfo = new LayerInfo
                    {
                        Name = kvp.Key,
                        FeatureCount = layer.GetFeatureCount(0),
                        GeometryType = layer.GetGeomType().ToString(),
                        IsQcError = false,
                        IsVisible = true
                    };
                    layersInfo[kvp.Key] = layerInfo;
                }

                // QC_Errors 레이어들
                foreach (var kvp in _qcErrorLayers)
                {
                    var layer = kvp.Value;
                    var layerInfo = new LayerInfo
                    {
                        Name = kvp.Key,
                        FeatureCount = layer.GetFeatureCount(0),
                        GeometryType = layer.GetGeomType().ToString(),
                        IsQcError = true,
                        IsVisible = true
                    };
                    layersInfo[kvp.Key] = layerInfo;
                }

                _logger.LogDebug("레이어 정보 반환: 총 {Count}개 레이어", layersInfo.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레이어 정보 조회 실패");
            }

            return layersInfo;
        }

        /// <summary>
        /// 현재 데이터의 전체 범위를 반환합니다
        /// </summary>
        public Envelope? GetCurrentExtent()
        {
            return _currentExtent;
        }

        /// <summary>
        /// 지도 이미지를 생성합니다 (간단한 구현)
        /// </summary>
        public async Task<BitmapSource?> RenderMapAsync(int width, int height, Envelope? extent = null)
        {
            try
            {
                _logger.LogInformation("지도 렌더링 시작: {Width}x{Height}", width, height);

                return await Task.Run(() =>
                {
                    try
                    {
                        // 렌더링할 범위 결정
                        var renderExtent = extent ?? _currentExtent;
                        if (renderExtent == null)
                        {
                            _logger.LogWarning("렌더링할 범위가 없습니다");
                            return null;
                        }

                        // 간단한 지도 이미지 생성 (실제 구현에서는 더 복잡한 렌더링 로직 필요)
                        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                        
                        // 배경색으로 채우기
                        var backgroundColor = Color.FromRgb(240, 248, 255); // AliceBlue
                        FillBitmap(bitmap, backgroundColor);

                        // 레이어들 렌더링 (간단한 점/선/면 표시)
                        RenderLayersOnBitmap(bitmap, renderExtent, width, height);

                        bitmap.Freeze(); // UI 스레드에서 사용할 수 있도록 고정
                        
                        _logger.LogInformation("지도 렌더링 완료");
                        return (BitmapSource)bitmap;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "지도 렌더링 실패");
                        return null;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 렌더링 실패");
                return null;
            }
        }

        /// <summary>
        /// 비트맵을 단색으로 채웁니다
        /// </summary>
        private void FillBitmap(WriteableBitmap bitmap, Color color)
        {
            try
            {
                var colorValue = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
                
                bitmap.Lock();
                try
                {
                    unsafe
                    {
                        var backBuffer = (uint*)bitmap.BackBuffer.ToPointer();
                        var pixels = bitmap.PixelWidth * bitmap.PixelHeight;
                        
                        for (int i = 0; i < pixels; i++)
                        {
                            backBuffer[i] = colorValue;
                        }
                    }
                    
                    bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                }
                finally
                {
                    bitmap.Unlock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "비트맵 채우기 실패");
            }
        }

        /// <summary>
        /// 레이어들을 비트맵에 렌더링합니다
        /// </summary>
        private void RenderLayersOnBitmap(WriteableBitmap bitmap, Envelope extent, int width, int height)
        {
            try
            {
                bitmap.Lock();
                try
                {
                    // 일반 공간데이터 레이어들 렌더링
                    foreach (var kvp in _loadedLayers)
                    {
                        RenderLayerOnBitmap(bitmap, kvp.Value, extent, width, height, false);
                    }

                    // QC_Errors 레이어들 렌더링 (위에 표시)
                    foreach (var kvp in _qcErrorLayers)
                    {
                        RenderLayerOnBitmap(bitmap, kvp.Value, extent, width, height, true);
                    }

                    bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
                finally
                {
                    bitmap.Unlock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레이어 렌더링 실패");
            }
        }

        /// <summary>
        /// 개별 레이어를 비트맵에 렌더링합니다
        /// </summary>
        private void RenderLayerOnBitmap(WriteableBitmap bitmap, Layer layer, Envelope extent, int width, int height, bool isQcError)
        {
            try
            {
                var layerName = layer.GetName();
                var featureCount = layer.GetFeatureCount(0);
                
                if (featureCount == 0)
                {
                    return;
                }

                _logger.LogDebug("레이어 렌더링: {LayerName}, 피처 수: {Count}", layerName, featureCount);

                // 좌표 변환 계산
                var scaleX = width / (extent.MaxX - extent.MinX);
                var scaleY = height / (extent.MaxY - extent.MinY);

                // 피처들 렌더링
                layer.ResetReading();
                Feature feature;
                int renderedCount = 0;
                
                while ((feature = layer.GetNextFeature()) != null && renderedCount < 1000) // 성능을 위해 최대 1000개로 제한
                {
                    try
                    {
                        var geometry = feature.GetGeometryRef();
                        if (geometry != null)
                        {
                            RenderGeometryOnBitmap(bitmap, geometry, extent, scaleX, scaleY, width, height, isQcError);
                            renderedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "피처 렌더링 실패: Layer={LayerName}", layerName);
                    }
                    finally
                    {
                        feature.Dispose();
                    }
                }

                _logger.LogDebug("레이어 렌더링 완료: {LayerName}, 렌더링된 피처: {Count}개", layerName, renderedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레이어 렌더링 실패: {LayerName}", layer.GetName());
            }
        }

        /// <summary>
        /// 지오메트리를 비트맵에 렌더링합니다
        /// </summary>
        private void RenderGeometryOnBitmap(WriteableBitmap bitmap, OSGeo.OGR.Geometry geometry, Envelope extent, 
            double scaleX, double scaleY, int width, int height, bool isQcError)
        {
            try
            {
                var geomType = geometry.GetGeometryType();
                var color = isQcError ? Color.FromRgb(255, 0, 0) : Color.FromRgb(0, 100, 200); // 빨간색 또는 파란색

                unsafe
                {
                    var backBuffer = (uint*)bitmap.BackBuffer.ToPointer();
                    var colorValue = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);

                    switch (geomType)
                    {
                        case wkbGeometryType.wkbPoint:
                        case wkbGeometryType.wkbPoint25D:
                            RenderPointGeometry(backBuffer, geometry, extent, scaleX, scaleY, width, height, colorValue);
                            break;

                        case wkbGeometryType.wkbLineString:
                        case wkbGeometryType.wkbLineString25D:
                            RenderLineGeometry(backBuffer, geometry, extent, scaleX, scaleY, width, height, colorValue);
                            break;

                        case wkbGeometryType.wkbPolygon:
                        case wkbGeometryType.wkbPolygon25D:
                            RenderPolygonGeometry(backBuffer, geometry, extent, scaleX, scaleY, width, height, colorValue);
                            break;

                        case wkbGeometryType.wkbMultiPoint:
                        case wkbGeometryType.wkbMultiLineString:
                        case wkbGeometryType.wkbMultiPolygon:
                            // 멀티 지오메트리는 개별 지오메트리로 분해하여 렌더링
                            for (int i = 0; i < geometry.GetGeometryCount(); i++)
                            {
                                var subGeometry = geometry.GetGeometryRef(i);
                                if (subGeometry != null)
                                {
                                    RenderGeometryOnBitmap(bitmap, subGeometry, extent, scaleX, scaleY, width, height, isQcError);
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "지오메트리 렌더링 실패");
            }
        }

        /// <summary>
        /// 포인트 지오메트리를 렌더링합니다
        /// </summary>
        private unsafe void RenderPointGeometry(uint* backBuffer, OSGeo.OGR.Geometry geometry, Envelope extent, 
            double scaleX, double scaleY, int width, int height, uint colorValue)
        {
            try
            {
                var x = geometry.GetX(0);
                var y = geometry.GetY(0);

                // 화면 좌표로 변환
                var screenX = (int)((x - extent.MinX) * scaleX);
                var screenY = height - (int)((y - extent.MinY) * scaleY); // Y축 뒤집기

                // 화면 범위 확인
                if (screenX >= 0 && screenX < width && screenY >= 0 && screenY < height)
                {
                    // 3x3 픽셀로 점 그리기
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var px = screenX + dx;
                            var py = screenY + dy;
                            
                            if (px >= 0 && px < width && py >= 0 && py < height)
                            {
                                backBuffer[py * width + px] = colorValue;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "포인트 렌더링 실패");
            }
        }

        /// <summary>
        /// 라인 지오메트리를 렌더링합니다
        /// </summary>
        private unsafe void RenderLineGeometry(uint* backBuffer, OSGeo.OGR.Geometry geometry, Envelope extent, 
            double scaleX, double scaleY, int width, int height, uint colorValue)
        {
            try
            {
                var pointCount = geometry.GetPointCount();
                if (pointCount < 2) return;

                for (int i = 0; i < pointCount - 1; i++)
                {
                    var x1 = geometry.GetX(i);
                    var y1 = geometry.GetY(i);
                    var x2 = geometry.GetX(i + 1);
                    var y2 = geometry.GetY(i + 1);

                    // 화면 좌표로 변환
                    var screenX1 = (int)((x1 - extent.MinX) * scaleX);
                    var screenY1 = height - (int)((y1 - extent.MinY) * scaleY);
                    var screenX2 = (int)((x2 - extent.MinX) * scaleX);
                    var screenY2 = height - (int)((y2 - extent.MinY) * scaleY);

                    // 간단한 선 그리기 (Bresenham 알고리즘 간소화)
                    DrawLine(backBuffer, screenX1, screenY1, screenX2, screenY2, width, height, colorValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "라인 렌더링 실패");
            }
        }

        /// <summary>
        /// 폴리곤 지오메트리를 렌더링합니다
        /// </summary>
        private unsafe void RenderPolygonGeometry(uint* backBuffer, OSGeo.OGR.Geometry geometry, Envelope extent, 
            double scaleX, double scaleY, int width, int height, uint colorValue)
        {
            try
            {
                // 외곽선만 렌더링 (간단한 구현)
                var ring = geometry.GetGeometryRef(0); // 외곽 링
                if (ring != null)
                {
                    RenderLineGeometry(backBuffer, ring, extent, scaleX, scaleY, width, height, colorValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "폴리곤 렌더링 실패");
            }
        }

        /// <summary>
        /// 간단한 선 그리기
        /// </summary>
        private unsafe void DrawLine(uint* backBuffer, int x1, int y1, int x2, int y2, int width, int height, uint colorValue)
        {
            try
            {
                var dx = Math.Abs(x2 - x1);
                var dy = Math.Abs(y2 - y1);
                var sx = x1 < x2 ? 1 : -1;
                var sy = y1 < y2 ? 1 : -1;
                var err = dx - dy;

                var x = x1;
                var y = y1;

                while (true)
                {
                    // 픽셀 그리기
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        backBuffer[y * width + x] = colorValue;
                    }

                    if (x == x2 && y == y2) break;

                    var e2 = 2 * err;
                    if (e2 > -dy)
                    {
                        err -= dy;
                        x += sx;
                    }
                    if (e2 < dx)
                    {
                        err += dx;
                        y += sy;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "선 그리기 실패");
            }
        }

        /// <summary>
        /// 오류 피처 선택 해제
        /// </summary>
        /// <param name="errorId">오류 ID (null이면 모든 선택 해제)</param>
        public void DeselectErrorFeature(string? errorId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(errorId))
                {
                    // 모든 선택 해제
                    var count = _selectedErrorIds.Count;
                    _selectedErrorIds.Clear();
                    _logger.LogDebug("모든 오류 피처 선택 해제: {Count}개", count);
                }
                else
                {
                    // 특정 오류 선택 해제
                    if (_selectedErrorIds.Remove(errorId))
                    {
                        _logger.LogDebug("오류 피처 선택 해제됨: {ErrorId}", errorId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 피처 선택 해제 실패: {ErrorId}", errorId);
            }
        }

        /// <summary>
        /// 선택된 오류 피처 ID 목록 가져오기
        /// </summary>
        /// <returns>선택된 오류 ID 목록</returns>
        public IReadOnlySet<string> GetSelectedErrorIds()
        {
            return _selectedErrorIds.ToHashSet();
        }

        /// <summary>
        /// 오류 피처가 선택되었는지 확인
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        /// <returns>선택 여부</returns>
        public bool IsErrorFeatureSelected(string errorId)
        {
            return !string.IsNullOrEmpty(errorId) && _selectedErrorIds.Contains(errorId);
        }

        /// <summary>
        /// 지도 범위 설정
        /// </summary>
        /// <param name="extent">지도 범위</param>
        public void SetExtent(Envelope extent)
        {
            _currentExtent = extent;
            ExtentChanged?.Invoke(this, new MapExtentChangedEventArgs { NewExtent = extent });
        }



        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            try
            {
                // 레이어 해제
                foreach (var layer in _loadedLayers.Values)
                {
                    layer?.Dispose();
                }
                _loadedLayers.Clear();

                foreach (var layer in _qcErrorLayers.Values)
                {
                    layer?.Dispose();
                }
                _qcErrorLayers.Clear();

                // 데이터 소스 해제
                foreach (var dataSource in _loadedDataSources.Values)
                {
                    dataSource?.Dispose();
                }
                _loadedDataSources.Clear();

                _logger.LogDebug("GdalMapService 리소스 해제 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GdalMapService 리소스 해제 실패");
            }
        }
    }

    /// <summary>
    /// 심볼 규칙 클래스
    /// </summary>
    public class SymbolRule
    {
        public string MarkerStyle { get; set; } = "CIRCLE";
        public double Size { get; set; } = 8;
        public System.Drawing.Color FillColor { get; set; } = System.Drawing.Color.Blue;
        public System.Drawing.Color OutlineColor { get; set; } = System.Drawing.Color.Black;
        public double OutlineWidth { get; set; } = 1;
        public double Transparency { get; set; } = 0.8;

        /// <summary>
        /// 심볼 규칙 복사
        /// </summary>
        /// <returns>복사된 심볼 규칙</returns>
        public SymbolRule Clone()
        {
            return new SymbolRule
            {
                MarkerStyle = this.MarkerStyle,
                Size = this.Size,
                FillColor = this.FillColor,
                OutlineColor = this.OutlineColor,
                OutlineWidth = this.OutlineWidth,
                Transparency = this.Transparency
            };
        }

        /// <summary>
        /// 선택 상태에 맞게 심볼 조정
        /// </summary>
        /// <param name="isSelected">선택 여부</param>
        /// <returns>조정된 심볼 규칙</returns>
        public SymbolRule AdjustForSelection(bool isSelected)
        {
            var adjusted = this.Clone();
            if (isSelected)
            {
                adjusted.Size *= 1.5;
                adjusted.OutlineWidth *= 2;
                adjusted.OutlineColor = System.Drawing.Color.Yellow;
            }
            return adjusted;
        }

        /// <summary>
        /// 하이라이트 상태에 맞게 심볼 조정
        /// </summary>
        /// <param name="isHighlighted">하이라이트 여부</param>
        /// <returns>조정된 심볼 규칙</returns>
        public SymbolRule AdjustForHighlight(bool isHighlighted)
        {
            var adjusted = this.Clone();
            if (isHighlighted)
            {
                adjusted.Size *= 1.3;
                adjusted.OutlineWidth *= 1.5;
                adjusted.OutlineColor = System.Drawing.Color.Cyan;
                adjusted.Transparency = 1.0;
            }
            return adjusted;
        }
    }
}