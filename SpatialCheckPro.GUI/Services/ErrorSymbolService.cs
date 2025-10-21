using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 심볼 및 스타일 관리 서비스 구현
    /// </summary>
    public class ErrorSymbolService : IErrorSymbolService
    {
        private readonly ILogger<ErrorSymbolService> _logger;
        private SymbolStyleSettings _styleSettings;
        private readonly Dictionary<string, SymbolCacheItem> _symbolCache;
        private readonly object _cacheLock = new object();

        /// <summary>
        /// ErrorSymbolService 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        public ErrorSymbolService(ILogger<ErrorSymbolService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _symbolCache = new Dictionary<string, SymbolCacheItem>();
            _styleSettings = CreateDefaultStyleSettings();
        }

        /// <summary>
        /// ErrorFeature에 대한 심볼 생성
        /// </summary>
        /// <param name="errorFeature">ErrorFeature 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>생성된 ErrorSymbol</returns>
        public ErrorSymbol CreateSymbol(ErrorFeature errorFeature, double zoomLevel)
        {
            try
            {
                // 캐시 키 생성
                var cacheKey = $"{errorFeature.Id}_{errorFeature.Severity}_{errorFeature.ErrorType}_{errorFeature.Status}_{zoomLevel:F1}";
                
                lock (_cacheLock)
                {
                    // 캐시에서 조회
                    if (_symbolCache.TryGetValue(cacheKey, out var cachedItem))
                    {
                        cachedItem.LastUsedAt = DateTime.UtcNow;
                        cachedItem.UseCount++;
                        return cachedItem.Symbol;
                    }
                }

                // 새로운 심볼 생성
                var symbol = new ErrorSymbol
                {
                    Id = errorFeature.Id,
                    X = errorFeature.X,
                    Y = errorFeature.Y,
                    Shape = GetErrorTypeSymbol(errorFeature.ErrorType).ToString(),
                    Size = CalculateSymbolSize(_styleSettings.DefaultSymbolSize, zoomLevel),
                    FillColor = Colors.Red,
                    IsVisible = true,
                    ZIndex = 20
                };

                // 상태별 스타일 적용
                symbol.StrokeColor = Colors.Black;
                symbol.StrokeWidth = 1.0;
                symbol.Opacity = 1.0;

                // 캐시에 저장
                lock (_cacheLock)
                {
                    _symbolCache[cacheKey] = new SymbolCacheItem
                    {
                        Key = cacheKey,
                        Symbol = symbol
                    };

                    // 캐시 크기 제한 (1000개)
                    if (_symbolCache.Count > 1000)
                    {
                        CleanupCache();
                    }
                }

                _logger.LogDebug("심볼 생성 완료: {Id}, 크기: {Size}", 
                    errorFeature.Id, symbol.Size);

                return symbol;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "심볼 생성 실패: {Id}", errorFeature.Id);
                return CreateDefaultSymbol(errorFeature, zoomLevel);
            }
        }

        /// <summary>
        /// 심각도별 색상 조회
        /// </summary>
        /// <param name="severity">심각도</param>
        /// <returns>해당 심각도의 색상</returns>
        public Color GetSeverityColor(string severity)
        {
            if (_styleSettings.SeverityColors.TryGetValue(severity, out var color))
            {
                return color;
            }

            // 기본 색상 매핑
            return severity.ToUpper() switch
            {
                "CRIT" or "CRITICAL" => Colors.Red,
                "MAJOR" => Colors.Orange,
                "MINOR" => Colors.Yellow,
                "INFO" => Colors.Blue,
                _ => Colors.Gray
            };
        }

        /// <summary>
        /// 오류 타입별 심볼 모양 조회
        /// </summary>
        /// <param name="errorType">오류 타입</param>
        /// <returns>해당 오류 타입의 심볼 모양</returns>
        public SymbolShape GetErrorTypeSymbol(string errorType)
        {
            if (_styleSettings.ErrorTypeSymbols.TryGetValue(errorType, out var shape))
            {
                return shape;
            }

            // 기본 심볼 매핑
            return errorType.ToUpper() switch
            {
                "GEOM" => SymbolShape.Circle,
                "REL" => SymbolShape.Square,
                "ATTR" => SymbolShape.Triangle,
                "SCHEMA" => SymbolShape.Diamond,
                _ => SymbolShape.Circle
            };
        }

        /// <summary>
        /// 상태별 스타일 조회
        /// </summary>
        /// <param name="status">오류 상태</param>
        /// <returns>해당 상태의 스타일</returns>
        public ErrorStyle GetStatusStyle(string status)
        {
            if (_styleSettings.StatusStyles.TryGetValue(status, out var style))
            {
                return style;
            }

            // 기본 스타일 매핑
            return status.ToUpper() switch
            {
                "OPEN" => new ErrorStyle
                {
                    StrokeColor = Colors.Black,
                    StrokeWidth = 1.0,
                    Opacity = 1.0
                },
                "INPROGRESS" => new ErrorStyle
                {
                    StrokeColor = Colors.Blue,
                    StrokeWidth = 2.0,
                    Opacity = 0.8,
                    DashPattern = new double[] { 3, 3 }
                },
                "FIXED" => new ErrorStyle
                {
                    StrokeColor = Colors.Green,
                    StrokeWidth = 2.0,
                    Opacity = 0.6
                },
                "IGNORED" => new ErrorStyle
                {
                    StrokeColor = Colors.Gray,
                    StrokeWidth = 1.0,
                    Opacity = 0.4
                },
                _ => new ErrorStyle
                {
                    StrokeColor = Colors.Black,
                    StrokeWidth = 1.0,
                    Opacity = 1.0
                }
            };
        }

        /// <summary>
        /// 줌 레벨에 따른 심볼 크기 계산
        /// </summary>
        /// <param name="baseSize">기본 크기</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>조정된 심볼 크기</returns>
        public double CalculateSymbolSize(double baseSize, double zoomLevel)
        {
            // 줌 레벨에 따른 크기 조정 (로그 스케일)
            var scaleFactor = Math.Pow(_styleSettings.ZoomScaleFactor, zoomLevel - 10); // 기준 줌 레벨 10
            var adjustedSize = baseSize * scaleFactor;

            // 최소/최대 크기 제한
            return Math.Max(_styleSettings.MinSymbolSize, 
                   Math.Min(_styleSettings.MaxSymbolSize, adjustedSize));
        }

        /// <summary>
        /// 선택된 ErrorFeature의 하이라이트 심볼 생성
        /// </summary>
        /// <param name="errorFeature">ErrorFeature 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>하이라이트 심볼</returns>
        public ErrorSymbol CreateHighlightSymbol(ErrorFeature errorFeature, double zoomLevel)
        {
            var baseSymbol = CreateSymbol(errorFeature, zoomLevel);
            
            return new ErrorSymbol
            {
                Id = $"{errorFeature.Id}_highlight",
                X = errorFeature.X,
                Y = errorFeature.Y,
                Shape = baseSymbol.Shape,
                Size = baseSymbol.Size * 1.5, // 1.5배 크기
                FillColor = _styleSettings.HighlightColor,
                StrokeColor = _styleSettings.SelectionColor,
                StrokeWidth = _styleSettings.HighlightStrokeWidth,
                Opacity = 0.8,
                IsVisible = true,
                ZIndex = 1000, // 최상위 표시
                IsAnimated = _styleSettings.UseAnimation
            };
        }

        /// <summary>
        /// 클러스터 심볼 생성
        /// </summary>
        /// <param name="cluster">ErrorCluster 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>클러스터 심볼</returns>
        public ErrorSymbol CreateClusterSymbol(ErrorCluster cluster, double zoomLevel)
        {
            var errorCount = cluster.Errors.Count;
            var size = CalculateClusterSize(errorCount, zoomLevel);
            
            return new ErrorSymbol
            {
                Id = cluster.Id,
                X = cluster.CenterX,
                Y = cluster.CenterY,
                Shape = "Circle",
                Size = size,
                FillColor = _styleSettings.ClusterColor,
                StrokeColor = Colors.White,
                StrokeWidth = 2.0,
                Opacity = 0.9,
                IsVisible = true,
                ZIndex = 500,
                Text = errorCount.ToString(),
                TextColor = _styleSettings.ClusterTextColor,
                TextSize = size * 0.4
            };
        }



        /// <summary>
        /// 기본 스타일 설정 생성
        /// </summary>
        /// <returns>기본 스타일 설정</returns>
        private SymbolStyleSettings CreateDefaultStyleSettings()
        {
            return new SymbolStyleSettings
            {
                SeverityColors = new Dictionary<string, Color>
                {
                    ["CRIT"] = Colors.Red,
                    ["MAJOR"] = Colors.Orange,
                    ["MINOR"] = Colors.Yellow,
                    ["INFO"] = Colors.Blue
                },
                ErrorTypeSymbols = new Dictionary<string, SymbolShape>
                {
                    ["GEOM"] = SymbolShape.Circle,
                    ["REL"] = SymbolShape.Square,
                    ["ATTR"] = SymbolShape.Triangle,
                    ["SCHEMA"] = SymbolShape.Diamond
                },
                StatusStyles = new Dictionary<string, ErrorStyle>
                {
                    ["OPEN"] = new ErrorStyle
                    {
                        StrokeColor = Colors.Black,
                        StrokeWidth = 1.0,
                        Opacity = 1.0
                    },
                    ["FIXED"] = new ErrorStyle
                    {
                        StrokeColor = Colors.Green,
                        StrokeWidth = 2.0,
                        Opacity = 0.6
                    }
                },
                DefaultSymbolSize = 8.0,
                MinSymbolSize = 4.0,
                MaxSymbolSize = 32.0,
                ZoomScaleFactor = 1.2,
                HighlightColor = Colors.Yellow,
                HighlightStrokeWidth = 3.0,
                SelectionColor = Colors.Cyan,
                ClusterColor = Colors.Purple,
                ClusterTextColor = Colors.White,
                UseAnimation = true,
                AnimationDurationMs = 300
            };
        }

        /// <summary>
        /// 기본 심볼 생성 (오류 발생 시 폴백)
        /// </summary>
        /// <param name="errorFeature">ErrorFeature 객체</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>기본 심볼</returns>
        private ErrorSymbol CreateDefaultSymbol(ErrorFeature errorFeature, double zoomLevel)
        {
            return new ErrorSymbol
            {
                Id = errorFeature.Id,
                X = errorFeature.X,
                Y = errorFeature.Y,
                Shape = "Circle",
                Size = CalculateSymbolSize(8.0, zoomLevel),
                FillColor = Colors.Gray,
                StrokeColor = Colors.Black,
                StrokeWidth = 1.0,
                Opacity = 1.0,
                IsVisible = true,
                ZIndex = 0
            };
        }

        /// <summary>
        /// 심각도별 Z-Index 계산
        /// </summary>
        /// <param name="severity">심각도</param>
        /// <returns>Z-Index 값</returns>
        private int GetZIndexBySeverity(string severity)
        {
            return severity.ToUpper() switch
            {
                "CRIT" => 100,
                "MAJOR" => 80,
                "MINOR" => 60,
                "INFO" => 40,
                _ => 20
            };
        }

        /// <summary>
        /// 클러스터 크기 계산
        /// </summary>
        /// <param name="errorCount">오류 개수</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>클러스터 크기</returns>
        private double CalculateClusterSize(int errorCount, double zoomLevel)
        {
            var baseSize = 16.0 + Math.Log10(errorCount) * 8.0; // 로그 스케일
            return CalculateSymbolSize(baseSize, zoomLevel);
        }

        /// <summary>
        /// 심볼 스타일 설정 업데이트
        /// </summary>
        /// <param name="styleSettings">새로운 스타일 설정</param>
        public async Task UpdateStyleSettingsAsync(SymbolStyleSettings styleSettings)
        {
            await Task.Run(() =>
            {
                _styleSettings = styleSettings ?? throw new ArgumentNullException(nameof(styleSettings));
                ClearSymbolCache(); // 캐시 초기화
                _logger.LogInformation("심볼 스타일 설정 업데이트 완료");
            });
        }

        /// <summary>
        /// 현재 스타일 설정 조회
        /// </summary>
        /// <returns>현재 스타일 설정</returns>
        public SymbolStyleSettings GetCurrentStyleSettings()
        {
            return _styleSettings;
        }

        /// <summary>
        /// 기본 스타일 설정으로 초기화
        /// </summary>
        public void ResetToDefaultStyles()
        {
            _styleSettings = CreateDefaultStyleSettings();
            ClearSymbolCache();
            _logger.LogInformation("기본 스타일 설정으로 초기화 완료");
        }

        /// <summary>
        /// 사용자 정의 색상 팔레트 설정
        /// </summary>
        /// <param name="colorPalette">색상 팔레트</param>
        public void SetCustomColorPalette(Dictionary<string, Color> colorPalette)
        {
            foreach (var kvp in colorPalette)
            {
                _styleSettings.SeverityColors[kvp.Key] = kvp.Value;
            }
            ClearSymbolCache();
            _logger.LogInformation("사용자 정의 색상 팔레트 설정 완료: {Count}개 색상", colorPalette.Count);
        }

        /// <summary>
        /// 심볼 캐시 초기화
        /// </summary>
        public void ClearSymbolCache()
        {
            lock (_cacheLock)
            {
                var count = _symbolCache.Count;
                _symbolCache.Clear();
                _logger.LogDebug("심볼 캐시 초기화 완료: {Count}개 항목 제거", count);
            }
        }

        /// <summary>
        /// 캐시 정리 (LRU 방식)
        /// </summary>
        private void CleanupCache()
        {
            var itemsToRemove = _symbolCache.Values
                .OrderBy(item => item.LastUsedAt)
                .Take(_symbolCache.Count - 800) // 800개까지 유지
                .Select(item => item.Key)
                .ToList();

            foreach (var key in itemsToRemove)
            {
                _symbolCache.Remove(key);
            }

            _logger.LogDebug("캐시 정리 완료: {Count}개 항목 제거", itemsToRemove.Count);
        }
    }
}