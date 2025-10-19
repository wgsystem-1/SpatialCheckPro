using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지도 상호작용 처리 서비스 인터페이스
    /// </summary>
    public interface IMapInteractionService
    {
        /// <summary>
        /// 지도 클릭 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <param name="modifierKeys">수정 키 (Ctrl, Shift 등)</param>
        /// <returns>클릭 처리 결과</returns>
        Task<MapClickResult> HandleMapClickAsync(double screenX, double screenY, MapBounds mapBounds, ModifierKeys modifierKeys);

        /// <summary>
        /// 지도 더블클릭 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>더블클릭 처리 결과</returns>
        Task<MapDoubleClickResult> HandleMapDoubleClickAsync(double screenX, double screenY, MapBounds mapBounds);

        /// <summary>
        /// 지도 드래그 시작 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>드래그 시작 처리 결과</returns>
        Task<MapDragStartResult> HandleMapDragStartAsync(double screenX, double screenY, MapBounds mapBounds);

        /// <summary>
        /// 지도 드래그 진행 이벤트 처리
        /// </summary>
        /// <param name="startScreenX">시작 화면 X 좌표</param>
        /// <param name="startScreenY">시작 화면 Y 좌표</param>
        /// <param name="currentScreenX">현재 화면 X 좌표</param>
        /// <param name="currentScreenY">현재 화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>드래그 진행 처리 결과</returns>
        Task<MapDragProgressResult> HandleMapDragProgressAsync(double startScreenX, double startScreenY, 
            double currentScreenX, double currentScreenY, MapBounds mapBounds);

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
        Task<MapDragCompleteResult> HandleMapDragCompleteAsync(double startScreenX, double startScreenY, 
            double endScreenX, double endScreenY, MapBounds mapBounds, ModifierKeys modifierKeys);

        /// <summary>
        /// 지도 마우스 오버 이벤트 처리
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <returns>마우스 오버 처리 결과</returns>
        Task<MapMouseOverResult> HandleMapMouseOverAsync(double screenX, double screenY, MapBounds mapBounds);

        /// <summary>
        /// 키보드 단축키 이벤트 처리
        /// </summary>
        /// <param name="key">눌린 키</param>
        /// <param name="modifierKeys">수정 키</param>
        /// <returns>키보드 처리 결과</returns>
        Task<KeyboardHandleResult> HandleKeyboardInputAsync(Key key, ModifierKeys modifierKeys);

        /// <summary>
        /// 상호작용 설정 업데이트
        /// </summary>
        /// <param name="settings">상호작용 설정</param>
        void UpdateInteractionSettings(MapInteractionSettings settings);

        /// <summary>
        /// 현재 상호작용 설정 조회
        /// </summary>
        /// <returns>현재 상호작용 설정</returns>
        MapInteractionSettings GetInteractionSettings();

        /// <summary>
        /// 상호작용 모드 설정
        /// </summary>
        /// <param name="mode">상호작용 모드</param>
        void SetInteractionMode(MapInteractionMode mode);

        /// <summary>
        /// 현재 상호작용 모드 조회
        /// </summary>
        /// <returns>현재 상호작용 모드</returns>
        MapInteractionMode GetInteractionMode();

        /// <summary>
        /// ErrorFeature 클릭 이벤트
        /// </summary>
        event EventHandler<ErrorFeatureClickedEventArgs>? ErrorFeatureClicked;

        /// <summary>
        /// ErrorFeature 더블클릭 이벤트
        /// </summary>
        event EventHandler<ErrorFeatureDoubleClickedEventArgs>? ErrorFeatureDoubleClicked;

        /// <summary>
        /// ErrorFeature 마우스 오버 이벤트
        /// </summary>
        event EventHandler<ErrorFeatureMouseOverEventArgs>? ErrorFeatureMouseOver;

        /// <summary>
        /// 영역 선택 완료 이벤트
        /// </summary>
        event EventHandler<AreaSelectionCompletedEventArgs>? AreaSelectionCompleted;
    }

    /// <summary>
    /// 지도 상호작용 모드
    /// </summary>
    public enum MapInteractionMode
    {
        /// <summary>
        /// 기본 모드 (선택 및 네비게이션)
        /// </summary>
        Default,

        /// <summary>
        /// 선택 모드
        /// </summary>
        Selection,

        /// <summary>
        /// 영역 선택 모드
        /// </summary>
        AreaSelection,

        /// <summary>
        /// 팬 모드 (지도 이동)
        /// </summary>
        Pan,

        /// <summary>
        /// 줌 모드
        /// </summary>
        Zoom,

        /// <summary>
        /// 측정 모드
        /// </summary>
        Measure,

        /// <summary>
        /// 정보 조회 모드
        /// </summary>
        Identify
    }

    /// <summary>
    /// 지도 상호작용 설정
    /// </summary>
    public class MapInteractionSettings
    {
        /// <summary>
        /// 클릭 허용 거리 (픽셀)
        /// </summary>
        public double ClickTolerance { get; set; } = 5.0;

        /// <summary>
        /// 더블클릭 시간 간격 (밀리초)
        /// </summary>
        public int DoubleClickInterval { get; set; } = 300;

        /// <summary>
        /// 드래그 시작 최소 거리 (픽셀)
        /// </summary>
        public double DragStartThreshold { get; set; } = 3.0;

        /// <summary>
        /// 마우스 오버 지연 시간 (밀리초)
        /// </summary>
        public int MouseOverDelay { get; set; } = 100;

        /// <summary>
        /// 툴팁 표시 여부
        /// </summary>
        public bool ShowTooltips { get; set; } = true;

        /// <summary>
        /// 툴팁 지연 시간 (밀리초)
        /// </summary>
        public int TooltipDelay { get; set; } = 500;

        /// <summary>
        /// 키보드 단축키 사용 여부
        /// </summary>
        public bool EnableKeyboardShortcuts { get; set; } = true;

        /// <summary>
        /// 컨텍스트 메뉴 사용 여부
        /// </summary>
        public bool EnableContextMenu { get; set; } = true;

        /// <summary>
        /// 애니메이션 효과 사용 여부
        /// </summary>
        public bool UseAnimations { get; set; } = true;

        /// <summary>
        /// 애니메이션 지속 시간 (밀리초)
        /// </summary>
        public int AnimationDuration { get; set; } = 300;

        /// <summary>
        /// 다중 선택 허용 여부
        /// </summary>
        public bool AllowMultiSelection { get; set; } = true;

        /// <summary>
        /// 영역 선택 허용 여부
        /// </summary>
        public bool AllowAreaSelection { get; set; } = true;
    }

    /// <summary>
    /// 지도 클릭 결과
    /// </summary>
    public class MapClickResult
    {
        /// <summary>
        /// 클릭된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> ClickedErrorFeatures { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 클릭된 지도 좌표
        /// </summary>
        public (double X, double Y) MapCoordinate { get; set; }

        /// <summary>
        /// 클릭 처리 성공 여부
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 처리 시간 (밀리초)
        /// </summary>
        public double ProcessingTimeMs { get; set; }

        /// <summary>
        /// 추가 정보
        /// </summary>
        public string? AdditionalInfo { get; set; }
    }

    /// <summary>
    /// 지도 더블클릭 결과
    /// </summary>
    public class MapDoubleClickResult
    {
        /// <summary>
        /// 더블클릭된 ErrorFeature
        /// </summary>
        public ErrorFeature? DoubleClickedErrorFeature { get; set; }

        /// <summary>
        /// 더블클릭된 지도 좌표
        /// </summary>
        public (double X, double Y) MapCoordinate { get; set; }

        /// <summary>
        /// 줌 동작 수행 여부
        /// </summary>
        public bool ZoomPerformed { get; set; }

        /// <summary>
        /// 상세 정보 표시 여부
        /// </summary>
        public bool ShowDetails { get; set; }

        /// <summary>
        /// 처리 성공 여부
        /// </summary>
        public bool Success { get; set; }
    }

    /// <summary>
    /// 지도 드래그 시작 결과
    /// </summary>
    public class MapDragStartResult
    {
        /// <summary>
        /// 드래그 허용 여부
        /// </summary>
        public bool AllowDrag { get; set; }

        /// <summary>
        /// 드래그 모드
        /// </summary>
        public DragMode DragMode { get; set; }

        /// <summary>
        /// 시작 지도 좌표
        /// </summary>
        public (double X, double Y) StartMapCoordinate { get; set; }
    }

    /// <summary>
    /// 지도 드래그 진행 결과
    /// </summary>
    public class MapDragProgressResult
    {
        /// <summary>
        /// 드래그 계속 허용 여부
        /// </summary>
        public bool ContinueDrag { get; set; }

        /// <summary>
        /// 현재 드래그 영역
        /// </summary>
        public BoundingBox? DragArea { get; set; }

        /// <summary>
        /// 시각적 피드백 표시 여부
        /// </summary>
        public bool ShowVisualFeedback { get; set; }
    }

    /// <summary>
    /// 지도 드래그 완료 결과
    /// </summary>
    public class MapDragCompleteResult
    {
        /// <summary>
        /// 드래그 영역
        /// </summary>
        public BoundingBox? DragArea { get; set; }

        /// <summary>
        /// 영역 선택 수행 여부
        /// </summary>
        public bool AreaSelectionPerformed { get; set; }

        /// <summary>
        /// 선택된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> SelectedErrorFeatures { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 처리 성공 여부
        /// </summary>
        public bool Success { get; set; }
    }

    /// <summary>
    /// 지도 마우스 오버 결과
    /// </summary>
    public class MapMouseOverResult
    {
        /// <summary>
        /// 마우스 오버된 ErrorFeature
        /// </summary>
        public ErrorFeature? HoveredErrorFeature { get; set; }

        /// <summary>
        /// 툴팁 표시 여부
        /// </summary>
        public bool ShowTooltip { get; set; }

        /// <summary>
        /// 툴팁 내용
        /// </summary>
        public string? TooltipContent { get; set; }

        /// <summary>
        /// 커서 변경 여부
        /// </summary>
        public bool ChangeCursor { get; set; }

        /// <summary>
        /// 새로운 커서 타입
        /// </summary>
        public Cursor? NewCursor { get; set; }
    }

    /// <summary>
    /// 키보드 처리 결과
    /// </summary>
    public class KeyboardHandleResult
    {
        /// <summary>
        /// 키 처리 성공 여부
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        /// 수행된 동작
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// 추가 정보
        /// </summary>
        public string? AdditionalInfo { get; set; }
    }

    /// <summary>
    /// 드래그 모드
    /// </summary>
    public enum DragMode
    {
        /// <summary>
        /// 지도 팬
        /// </summary>
        Pan,

        /// <summary>
        /// 영역 선택
        /// </summary>
        AreaSelection,

        /// <summary>
        /// 줌 박스
        /// </summary>
        ZoomBox,

        /// <summary>
        /// 측정
        /// </summary>
        Measure
    }

    /// <summary>
    /// ErrorFeature 클릭 이벤트 인자
    /// </summary>
    public class ErrorFeatureClickedEventArgs : EventArgs
    {
        /// <summary>
        /// 클릭된 ErrorFeature
        /// </summary>
        public ErrorFeature ErrorFeature { get; set; } = new ErrorFeature();

        /// <summary>
        /// 클릭 위치 (화면 좌표)
        /// </summary>
        public (double X, double Y) ScreenPosition { get; set; }

        /// <summary>
        /// 클릭 위치 (지도 좌표)
        /// </summary>
        public (double X, double Y) MapPosition { get; set; }

        /// <summary>
        /// 수정 키
        /// </summary>
        public ModifierKeys ModifierKeys { get; set; }

        /// <summary>
        /// 클릭 시간
        /// </summary>
        public DateTime ClickTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// ErrorFeature 더블클릭 이벤트 인자
    /// </summary>
    public class ErrorFeatureDoubleClickedEventArgs : EventArgs
    {
        /// <summary>
        /// 더블클릭된 ErrorFeature
        /// </summary>
        public ErrorFeature ErrorFeature { get; set; } = new ErrorFeature();

        /// <summary>
        /// 더블클릭 위치 (화면 좌표)
        /// </summary>
        public (double X, double Y) ScreenPosition { get; set; }

        /// <summary>
        /// 더블클릭 위치 (지도 좌표)
        /// </summary>
        public (double X, double Y) MapPosition { get; set; }

        /// <summary>
        /// 더블클릭 시간
        /// </summary>
        public DateTime DoubleClickTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// ErrorFeature 마우스 오버 이벤트 인자
    /// </summary>
    public class ErrorFeatureMouseOverEventArgs : EventArgs
    {
        /// <summary>
        /// 마우스 오버된 ErrorFeature
        /// </summary>
        public ErrorFeature? ErrorFeature { get; set; }

        /// <summary>
        /// 마우스 위치 (화면 좌표)
        /// </summary>
        public (double X, double Y) ScreenPosition { get; set; }

        /// <summary>
        /// 마우스 위치 (지도 좌표)
        /// </summary>
        public (double X, double Y) MapPosition { get; set; }

        /// <summary>
        /// 마우스 진입 여부 (true: 진입, false: 벗어남)
        /// </summary>
        public bool IsEntering { get; set; }
    }

    /// <summary>
    /// 영역 선택 완료 이벤트 인자
    /// </summary>
    public class AreaSelectionCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// 선택 영역
        /// </summary>
        public BoundingBox SelectionArea { get; set; } = new BoundingBox();

        /// <summary>
        /// 선택된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> SelectedErrorFeatures { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 수정 키
        /// </summary>
        public ModifierKeys ModifierKeys { get; set; }

        /// <summary>
        /// 선택 완료 시간
        /// </summary>
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}