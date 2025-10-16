using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지도 상호작용 처리 서비스 구현
    /// </summary>
    public class MapInteractionService : IMapInteractionService
    {
        private readonly ILogger<MapInteractionService> _logger;
        private readonly IErrorLayerService _errorLayerService;
        private readonly IErrorSelectionService _selectionService;
        
        private MapInteractionSettings _settings = new MapInteractionSettings();
        private MapInteractionMode _currentMode = MapInteractionMode.Default;
        private DateTime _lastClickTime = DateTime.MinValue;
        private (double X, double Y) _lastClickPosition = (0, 0);

        /// <summary>
        /// ErrorFeature 클릭 이벤트
        /// </summary>
        public event EventHandler<ErrorFeatureClickedEventArgs>? ErrorFeatureClicked;

        /// <summary>
        /// ErrorFeature 더블클릭 이벤트
        /// </summary>
        public event EventHandler<ErrorFeatureDoubleClickedEventArgs>? ErrorFeatureDoubleClicked;

        /// <summary>
        /// ErrorFeature 마우스 오버 이벤트
        /// </summary>
        public event EventHandler<ErrorFeatureMouseOverEventArgs>? ErrorFeatureMouseOver;

        /// <summary>
        /// 영역 선택 완료 이벤트
        /// </summary>
        public event EventHandler<AreaSelectionCompletedEventArgs>? AreaSelectionCompleted;

        /// <summary>
        /// MapInteractionService 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="errorLayerService">오류 레이어 서비스</param>
        /// <param name="selectionService">선택 서비스</param>
        public MapInteractionService(ILogger<MapInteractionService> logger, 
            IErrorLayerService errorLayerService, 
            IErrorSelectionService selectionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _errorLayerService = errorLayerService ?? throw new ArgumentNullException(nameof(errorLayerService));
            _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        }

        /// <summary>
        /// 지도 클릭 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <param name="modifierKeys">수정 키</param>
        /// <returns>클릭 처리 결과</returns>
        public async Task<MapClickResult> HandleMapClickAsync(double screenX, double screenY, MapBounds mapBounds, ModifierKeys modifierKeys)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogDebug("지도 클릭 처리: 화면({ScreenX}, {ScreenY}), 수정키: {ModifierKeys}", 
                    screenX, screenY, modifierKeys);

                // 더블클릭 감지
                var currentTime = DateTime.UtcNow;
                var timeDiff = (currentTime - _lastClickTime).TotalMilliseconds;
                var positionDiff = Math.Sqrt(Math.Pow(screenX - _lastClickPosition.X, 2) + Math.Pow(screenY - _lastClickPosition.Y, 2));

                if (timeDiff <= _settings.DoubleClickInterval && positionDiff <= _settings.ClickTolerance)
                {
                    // 더블클릭으로 처리
                    var doubleClickResult = await HandleMapDoubleClickAsync(screenX, screenY, mapBounds);
                    stopwatch.Stop();
                    
                    return new MapClickResult
                    {
                        Success = doubleClickResult.Success,
                        ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                        AdditionalInfo = "더블클릭으로 처리됨"
                    };
                }

                // 지도 좌표 변환
                var (mapX, mapY) = _errorLayerService.ScreenToMap(screenX, screenY, mapBounds);

                // ErrorFeature 히트 테스트
                var hitFeatures = await _errorLayerService.HitTestAsync(screenX, screenY, _settings.ClickTolerance, mapBounds);

                var result = new MapClickResult
                {
                    ClickedErrorFeatures = hitFeatures,
                    MapCoordinate = (mapX, mapY),
                    Success = true
                };

                // ErrorFeature 클릭 처리
                if (hitFeatures.Any())
                {
                    await HandleErrorFeatureClickAsync(hitFeatures, screenX, screenY, mapX, mapY, modifierKeys);
                }
                else
                {
                    // 빈 공간 클릭 - 선택 해제 (Ctrl 키가 눌리지 않은 경우)
                    if (!modifierKeys.HasFlag(ModifierKeys.Control))
                    {
                        await _selectionService.DeselectErrorFeatureAsync();
                    }
                }

                // 클릭 정보 저장 (더블클릭 감지용)
                _lastClickTime = currentTime;
                _lastClickPosition = (screenX, screenY);

                stopwatch.Stop();
                result.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                _logger.LogDebug("지도 클릭 처리 완료: {FeatureCount}개 피처 발견, {Time:F2}ms", 
                    hitFeatures.Count, result.ProcessingTimeMs);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 클릭 처리 실패: 화면({ScreenX}, {ScreenY})", screenX, screenY);
                stopwatch.Stop();
                
                return new MapClickResult
                {
                    Success = false,
                    ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                    AdditionalInfo = ex.Message
                };
            }
        }

        /// <summary>
        /// 지도 더블클릭 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>더블클릭 처리 결과</returns>
        public async Task<MapDoubleClickResult> HandleMapDoubleClickAsync(double screenX, double screenY, MapBounds mapBounds)
        {
            try
            {
                _logger.LogDebug("지도 더블클릭 처리: 화면({ScreenX}, {ScreenY})", screenX, screenY);

                var (mapX, mapY) = _errorLayerService.ScreenToMap(screenX, screenY, mapBounds);
                var hitFeatures = await _errorLayerService.HitTestAsync(screenX, screenY, _settings.ClickTolerance, mapBounds);

                var result = new MapDoubleClickResult
                {
                    MapCoordinate = (mapX, mapY),
                    Success = true
                };

                if (hitFeatures.Any())
                {
                    // ErrorFeature 더블클릭 - 상세 정보 표시
                    var primaryFeature = hitFeatures.First();
                    result.DoubleClickedErrorFeature = primaryFeature;
                    result.ShowDetails = true;

                    // ErrorFeature 더블클릭 이벤트 발생
                    ErrorFeatureDoubleClicked?.Invoke(this, new ErrorFeatureDoubleClickedEventArgs
                    {
                        ErrorFeature = primaryFeature,
                        ScreenPosition = (screenX, screenY),
                        MapPosition = (mapX, mapY)
                    });

                    _logger.LogDebug("ErrorFeature 더블클릭: {Id}", primaryFeature.Id);
                }
                else
                {
                    // 빈 공간 더블클릭 - 줌 인
                    result.ZoomPerformed = true;
                    _logger.LogDebug("빈 공간 더블클릭: 줌 인 수행");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 더블클릭 처리 실패: 화면({ScreenX}, {ScreenY})", screenX, screenY);
                return new MapDoubleClickResult { Success = false };
            }
        }

        /// <summary>
        /// 지도 드래그 시작 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>드래그 시작 처리 결과</returns>
        public Task<MapDragStartResult> HandleMapDragStartAsync(double screenX, double screenY, MapBounds mapBounds)
        {
            try
            {
                _logger.LogDebug("지도 드래그 시작: 화면({ScreenX}, {ScreenY})", screenX, screenY);

                var (mapX, mapY) = _errorLayerService.ScreenToMap(screenX, screenY, mapBounds);

                var result = new MapDragStartResult
                {
                    AllowDrag = true,
                    StartMapCoordinate = (mapX, mapY)
                };

                // 현재 모드에 따른 드래그 모드 결정
                result.DragMode = _currentMode switch
                {
                    MapInteractionMode.AreaSelection => DragMode.AreaSelection,
                    MapInteractionMode.Zoom => DragMode.ZoomBox,
                    MapInteractionMode.Measure => DragMode.Measure,
                    _ => DragMode.Pan
                };

                _logger.LogDebug("드래그 시작 허용: 모드 {DragMode}", result.DragMode);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 드래그 시작 처리 실패: 화면({ScreenX}, {ScreenY})", screenX, screenY);
                return Task.FromResult(new MapDragStartResult { AllowDrag = false });
            }
        }

        /// <summary>
        /// 지도 드래그 진행 이벤트 처리
        /// </summary>
        /// <param name="startScreenX">시작 화면 X 좌표</param>
        /// <param name="startScreenY">시작 화면 Y 좌표</param>
        /// <param name="currentScreenX">현재 화면 X 좌표</param>
        /// <param name="currentScreenY">현재 화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>드래그 진행 처리 결과</returns>
        public Task<MapDragProgressResult> HandleMapDragProgressAsync(double startScreenX, double startScreenY, 
            double currentScreenX, double currentScreenY, MapBounds mapBounds)
        {
            try
            {
                var distance = Math.Sqrt(Math.Pow(currentScreenX - startScreenX, 2) + Math.Pow(currentScreenY - startScreenY, 2));
                
                if (distance < _settings.DragStartThreshold)
                {
                    return Task.FromResult(new MapDragProgressResult { ContinueDrag = false });
                }

                var result = new MapDragProgressResult
                {
                    ContinueDrag = true,
                    ShowVisualFeedback = true
                };

                // 드래그 영역 계산 (영역 선택 모드인 경우)
                if (_currentMode == MapInteractionMode.AreaSelection)
                {
                    var (startMapX, startMapY) = _errorLayerService.ScreenToMap(startScreenX, startScreenY, mapBounds);
                    var (currentMapX, currentMapY) = _errorLayerService.ScreenToMap(currentScreenX, currentScreenY, mapBounds);

                    result.DragArea = new BoundingBox
                    {
                        MinX = Math.Min(startMapX, currentMapX),
                        MinY = Math.Min(startMapY, currentMapY),
                        MaxX = Math.Max(startMapX, currentMapX),
                        MaxY = Math.Max(startMapY, currentMapY)
                    };
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 드래그 진행 처리 실패");
                return Task.FromResult(new MapDragProgressResult { ContinueDrag = false });
            }
        }

        /// <summary>
        /// 지도 드래그 완료 이벤트 처리
        /// </summary>
        /// <param name="startScreenX">시작 화면 X 좌표</param>
        /// <param name="startScreenY">시작 화면 Y 좌표</param>
        /// <param name="endScreenX">끝 화면 X 좌표</param>
        /// <param name="endScreenY">끝 화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <param name="modifierKeys">수정 키</param>
        /// <returns>드래그 완료 처리 결과</returns>
        public async Task<MapDragCompleteResult> HandleMapDragCompleteAsync(double startScreenX, double startScreenY, 
            double endScreenX, double endScreenY, MapBounds mapBounds, ModifierKeys modifierKeys)
        {
            try
            {
                _logger.LogDebug("지도 드래그 완료: ({StartX}, {StartY}) -> ({EndX}, {EndY})", 
                    startScreenX, startScreenY, endScreenX, endScreenY);
                var result = new MapDragCompleteResult { Success = true };

                // 드래그 거리 확인
                var distance = Math.Sqrt(Math.Pow(endScreenX - startScreenX, 2) + Math.Pow(endScreenY - startScreenY, 2));
                
                if (distance < _settings.DragStartThreshold)
                {
                    // 드래그가 아닌 클릭으로 처리
                    return result;
                }

                // 영역 선택 처리
                if (_currentMode == MapInteractionMode.AreaSelection || _settings.AllowAreaSelection)
                {
                    var (startMapX, startMapY) = _errorLayerService.ScreenToMap(startScreenX, startScreenY, mapBounds);
                    var (endMapX, endMapY) = _errorLayerService.ScreenToMap(endScreenX, endScreenY, mapBounds);

                    var dragArea = new BoundingBox
                    {
                        MinX = Math.Min(startMapX, endMapX),
                        MinY = Math.Min(startMapY, endMapY),
                        MaxX = Math.Max(startMapX, endMapX),
                        MaxY = Math.Max(startMapY, endMapY)
                    };

                    result.DragArea = dragArea;

                    // 영역 내 ErrorFeature 선택
                    var selectedFeatures = await _selectionService.SelectErrorFeaturesInAreaAsync(
                        dragArea.MinX, dragArea.MinY, dragArea.MaxX, dragArea.MaxY, 
                        modifierKeys.HasFlag(ModifierKeys.Control));

                    result.SelectedErrorFeatures = selectedFeatures;
                    result.AreaSelectionPerformed = true;

                    // 영역 선택 완료 이벤트 발생
                    AreaSelectionCompleted?.Invoke(this, new AreaSelectionCompletedEventArgs
                    {
                        SelectionArea = dragArea,
                        SelectedErrorFeatures = selectedFeatures,
                        ModifierKeys = modifierKeys
                    });

                    _logger.LogDebug("영역 선택 완료: {Count}개 ErrorFeature 선택됨", selectedFeatures.Count);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 드래그 완료 처리 실패");
                return new MapDragCompleteResult { Success = false };
            }
        }

        /// <summary>
        /// 지도 마우스 오버 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>마우스 오버 처리 결과</returns>
        public async Task<MapMouseOverResult> HandleMapMouseOverAsync(double screenX, double screenY, MapBounds mapBounds)
        {
            try
            {
                if (!_settings.ShowTooltips)
                {
                    return new MapMouseOverResult();
                }

                var (mapX, mapY) = _errorLayerService.ScreenToMap(screenX, screenY, mapBounds);
                var hitFeatures = await _errorLayerService.HitTestAsync(screenX, screenY, _settings.ClickTolerance, mapBounds);

                var result = new MapMouseOverResult();

                if (hitFeatures.Any())
                {
                    var primaryFeature = hitFeatures.First();
                    result.HoveredErrorFeature = primaryFeature;
                    result.ShowTooltip = true;
                    result.TooltipContent = CreateTooltipContent(primaryFeature);
                    result.ChangeCursor = true;
                    result.NewCursor = Cursors.Hand;

                    // ErrorFeature 마우스 오버 이벤트 발생
                    ErrorFeatureMouseOver?.Invoke(this, new ErrorFeatureMouseOverEventArgs
                    {
                        ErrorFeature = primaryFeature,
                        ScreenPosition = (screenX, screenY),
                        MapPosition = (mapX, mapY),
                        IsEntering = true
                    });
                }
                else
                {
                    result.ChangeCursor = true;
                    result.NewCursor = Cursors.Arrow;

                    // 마우스 벗어남 이벤트 발생
                    ErrorFeatureMouseOver?.Invoke(this, new ErrorFeatureMouseOverEventArgs
                    {
                        ErrorFeature = null,
                        ScreenPosition = (screenX, screenY),
                        MapPosition = (mapX, mapY),
                        IsEntering = false
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지도 마우스 오버 처리 실패: 화면({ScreenX}, {ScreenY})", screenX, screenY);
                return new MapMouseOverResult();
            }
        }

        /// <summary>
        /// 키보드 단축키 이벤트 처리
        /// </summary>
        /// <param name="key">눌린 키</param>
        /// <param name="modifierKeys">수정 키</param>
        /// <returns>키보드 처리 결과</returns>
        public async Task<KeyboardHandleResult> HandleKeyboardInputAsync(Key key, ModifierKeys modifierKeys)
        {
            try
            {
                if (!_settings.EnableKeyboardShortcuts)
                {
                    return new KeyboardHandleResult { Handled = false };
                }

                _logger.LogDebug("키보드 입력 처리: {Key}, 수정키: {ModifierKeys}", key, modifierKeys);

                var result = new KeyboardHandleResult { Handled = true };

                // 키보드 단축키 처리
                switch (key)
                {
                    case Key.Escape:
                        await _selectionService.DeselectErrorFeatureAsync();
                        result.Action = "모든 선택 해제";
                        break;

                    case Key.A when modifierKeys.HasFlag(ModifierKeys.Control):
                        // Ctrl+A: 모든 ErrorFeature 선택 (실제 구현에서는 조건 선택 사용)
                        result.Action = "모든 ErrorFeature 선택";
                        break;

                    case Key.Delete:
                        // Delete: 선택된 ErrorFeature 상태 변경 (예: 무시됨으로 변경)
                        result.Action = "선택된 ErrorFeature 상태 변경";
                        break;

                    case Key.F5:
                        // F5: 새로고침
                        result.Action = "지도 새로고침";
                        break;

                    case Key.S when modifierKeys.HasFlag(ModifierKeys.Control):
                        // Ctrl+S: 선택 상태 저장
                        await _selectionService.SaveSelectionAsync($"Selection_{DateTime.Now:yyyyMMdd_HHmmss}");
                        result.Action = "선택 상태 저장";
                        break;

                    default:
                        result.Handled = false;
                        break;
                }

                if (result.Handled)
                {
                    _logger.LogDebug("키보드 단축키 처리 완료: {Action}", result.Action);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "키보드 입력 처리 실패: {Key}", key);
                return new KeyboardHandleResult { Handled = false, AdditionalInfo = ex.Message };
            }
        }

        /// <summary>
        /// 상호작용 설정 업데이트
        /// </summary>
        /// <param name="settings">상호작용 설정</param>
        public void UpdateInteractionSettings(MapInteractionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger.LogDebug("지도 상호작용 설정 업데이트 완료");
        }

        /// <summary>
        /// 현재 상호작용 설정 조회
        /// </summary>
        /// <returns>현재 상호작용 설정</returns>
        public MapInteractionSettings GetInteractionSettings()
        {
            return _settings;
        }

        /// <summary>
        /// 상호작용 모드 설정
        /// </summary>
        /// <param name="mode">상호작용 모드</param>
        public void SetInteractionMode(MapInteractionMode mode)
        {
            _currentMode = mode;
            _logger.LogDebug("지도 상호작용 모드 변경: {Mode}", mode);
        }

        /// <summary>
        /// 현재 상호작용 모드 조회
        /// </summary>
        /// <returns>현재 상호작용 모드</returns>
        public MapInteractionMode GetInteractionMode()
        {
            return _currentMode;
        }

        /// <summary>
        /// ErrorFeature 클릭 처리
        /// </summary>
        /// <param name="hitFeatures">클릭된 ErrorFeature 목록</param>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapX">지도 X 좌표</param>
        /// <param name="mapY">지도 Y 좌표</param>
        /// <param name="modifierKeys">수정 키</param>
        private async Task HandleErrorFeatureClickAsync(List<ErrorFeature> hitFeatures, 
            double screenX, double screenY, double mapX, double mapY, ModifierKeys modifierKeys)
        {
            try
            {
                // 가장 가까운 ErrorFeature 선택 (첫 번째)
                var primaryFeature = hitFeatures.First();

                // ErrorFeature 선택 처리
                var isMultiSelect = modifierKeys.HasFlag(ModifierKeys.Control) && _settings.AllowMultiSelection;
                await _selectionService.SelectErrorFeatureAsync(primaryFeature.Id, isMultiSelect);

                // ErrorFeature 클릭 이벤트 발생
                ErrorFeatureClicked?.Invoke(this, new ErrorFeatureClickedEventArgs
                {
                    ErrorFeature = primaryFeature,
                    ScreenPosition = (screenX, screenY),
                    MapPosition = (mapX, mapY),
                    ModifierKeys = modifierKeys
                });

                _logger.LogDebug("ErrorFeature 클릭 처리 완료: {Id}, 다중선택: {MultiSelect}", 
                    primaryFeature.Id, isMultiSelect);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 클릭 처리 실패");
            }
        }

        /// <summary>
        /// 툴팁 내용 생성
        /// </summary>
        /// <param name="errorFeature">ErrorFeature</param>
        /// <returns>툴팁 내용</returns>
        private string CreateTooltipContent(ErrorFeature errorFeature)
        {
            return $"오류 코드: {errorFeature.ErrorCode}\n" +
                   $"심각도: {errorFeature.Severity}\n" +
                   $"상태: {errorFeature.Status}\n" +
                   $"메시지: {errorFeature.Message}";
        }
    }
}