using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services;
using SpatialCheckPro.GUI.Models;
using SpatialCheckPro.GUI.Models.Events;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;
using ErrorFeatureFilter = SpatialCheckPro.GUI.Models.ErrorFeatureFilter;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지도 뷰어 오류 추적 통합 서비스 인터페이스
    /// </summary>
    public interface IMapErrorTrackingService
    {
        /// <summary>
        /// 오류 추적 시스템 초기화
        /// </summary>
        /// <param name="mapWidth">지도 너비</param>
        /// <param name="mapHeight">지도 높이</param>
        /// <returns>초기화 성공 여부</returns>
        Task<bool> InitializeAsync(int mapWidth, int mapHeight);

        /// <summary>
        /// QC_ERRORS 데이터 로드 및 지도에 표시
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>로드된 오류 개수</returns>
        Task<int> LoadAndDisplayErrorsAsync(string qcErrorsGdbPath, MapBounds mapBounds, double zoomLevel);

        /// <summary>
        /// 지도 클릭 이벤트 처리 및 오류 선택
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> HandleMapClickSelectionAsync(double screenX, double screenY, MapBounds mapBounds, bool isMultiSelect = false);

        /// <summary>
        /// 선택된 오류의 상태 변경
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <param name="userId">사용자 ID</param>
        /// <param name="comment">변경 사유</param>
        /// <returns>상태 변경 결과</returns>
        Task<BatchStatusChangeResult> ChangeSelectedErrorStatusAsync(string qcErrorsGdbPath, ErrorStatus newStatus, string userId, string? comment = null);

        /// <summary>
        /// 오류 목록에서 지도로 이동
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <param name="zoomLevel">목표 줌 레벨</param>
        /// <returns>이동 성공 여부 및 새로운 지도 범위</returns>
        Task<(bool Success, MapBounds? NewBounds)> NavigateToErrorAsync(string errorFeatureId, double zoomLevel = 15.0);

        /// <summary>
        /// 오류 필터링 적용
        /// </summary>
        /// <param name="filter">필터 조건</param>
        /// <returns>필터링된 오류 개수</returns>
        Task<int> ApplyErrorFilterAsync(ErrorFeatureFilter filter);

        /// <summary>
        /// 오류 클러스터링 설정
        /// </summary>
        /// <param name="enableClustering">클러스터링 사용 여부</param>
        /// <param name="clusterDistance">클러스터링 거리</param>
        void SetClusteringEnabled(bool enableClustering, double clusterDistance = 50.0);

        /// <summary>
        /// 오류 레이어 가시성 설정
        /// </summary>
        /// <param name="isVisible">가시성 여부</param>
        void SetErrorLayerVisibility(bool isVisible);

        /// <summary>
        /// 선택된 오류 목록 조회
        /// </summary>
        /// <returns>선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> GetSelectedErrorsAsync();

        /// <summary>
        /// 모든 선택 해제
        /// </summary>
        /// <returns>해제 성공 여부</returns>
        Task<bool> ClearSelectionAsync();

        /// <summary>
        /// 오류 추적 통계 조회
        /// </summary>
        /// <returns>통계 정보</returns>
        Task<ErrorTrackingStatistics> GetStatisticsAsync();

        /// <summary>
        /// 오류 선택 변경 이벤트
        /// </summary>
        event EventHandler<ErrorSelectionChangedEventArgs>? ErrorSelectionChanged;

        /// <summary>
        /// 오류 상태 변경 이벤트
        /// </summary>
        event EventHandler<SpatialCheckPro.GUI.Models.Events.ErrorStatusChangedEventArgs>? ErrorStatusChanged;

        /// <summary>
        /// 지도 네비게이션 요청 이벤트
        /// </summary>
        event EventHandler<MapNavigationRequestedEventArgs>? MapNavigationRequested;

        /// <summary>
        /// 오류 상세 정보 요청 이벤트
        /// </summary>
        event EventHandler<ErrorDetailsRequestedEventArgs>? ErrorDetailsRequested;
    }

    /// <summary>
    /// 오류 추적 통계 정보
    /// </summary>
    public class ErrorTrackingStatistics
    {
        /// <summary>
        /// 총 오류 개수
        /// </summary>
        public int TotalErrorCount { get; set; }

        /// <summary>
        /// 표시된 오류 개수
        /// </summary>
        public int DisplayedErrorCount { get; set; }

        /// <summary>
        /// 선택된 오류 개수
        /// </summary>
        public int SelectedErrorCount { get; set; }

        /// <summary>
        /// 필터링된 오류 개수
        /// </summary>
        public int FilteredErrorCount { get; set; }

        /// <summary>
        /// 심각도별 개수
        /// </summary>
        public Dictionary<string, int> SeverityCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 상태별 개수
        /// </summary>
        public Dictionary<string, int> StatusCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 오류 타입별 개수
        /// </summary>
        public Dictionary<string, int> ErrorTypeCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 클러스터 개수
        /// </summary>
        public int ClusterCount { get; set; }

        /// <summary>
        /// 평균 렌더링 시간 (밀리초)
        /// </summary>
        public double AverageRenderTimeMs { get; set; }

        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdatedAt { get; set; }
    }

    /// <summary>
    /// 오류 선택 변경 이벤트 인자
    /// </summary>
    public class ErrorSelectionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 선택된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> SelectedErrors { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 새로 선택된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> NewlySelectedErrors { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 선택 해제된 ErrorFeature 목록
        /// </summary>
        public List<ErrorFeature> DeselectedErrors { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 선택 변경 유형
        /// </summary>
        public SelectionChangeType ChangeType { get; set; }

        /// <summary>
        /// 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }

    // ErrorStatusChangedEventArgs는 SpatialCheckPro.Models 네임스페이스에서 공통으로 사용

    /// <summary>
    /// 지도 네비게이션 요청 이벤트 인자
    /// </summary>
    public class MapNavigationRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// 목표 ErrorFeature
        /// </summary>
        public ErrorFeature? TargetError { get; set; }

        /// <summary>
        /// 목표 지도 범위
        /// </summary>
        public MapBounds? TargetBounds { get; set; }

        /// <summary>
        /// 목표 줌 레벨
        /// </summary>
        public double TargetZoomLevel { get; set; }

        /// <summary>
        /// 애니메이션 사용 여부
        /// </summary>
        public bool UseAnimation { get; set; } = true;

        /// <summary>
        /// 하이라이트 표시 여부
        /// </summary>
        public bool HighlightTarget { get; set; } = true;

        /// <summary>
        /// 네비게이션 요청 시간
        /// </summary>
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 오류 상세 정보 요청 이벤트 인자
    /// </summary>
    public class ErrorDetailsRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// 상세 정보를 요청한 ErrorFeature
        /// </summary>
        public ErrorFeature ErrorFeature { get; set; } = new ErrorFeature();

        /// <summary>
        /// 요청 위치 (화면 좌표)
        /// </summary>
        public (double X, double Y) RequestPosition { get; set; }

        /// <summary>
        /// 팝업 표시 여부
        /// </summary>
        public bool ShowPopup { get; set; } = true;

        /// <summary>
        /// 모달 다이얼로그 표시 여부
        /// </summary>
        public bool ShowModal { get; set; } = false;

        /// <summary>
        /// 요청 시간
        /// </summary>
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
}