using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 지도 뷰어 오류 레이어 관리 서비스 인터페이스
    /// </summary>
    public interface IErrorLayerService
    {
        /// <summary>
        /// 오류 레이어 초기화
        /// </summary>
        /// <param name="mapWidth">지도 너비</param>
        /// <param name="mapHeight">지도 높이</param>
        /// <returns>초기화 성공 여부</returns>
        Task<bool> InitializeErrorLayerAsync(int mapWidth, int mapHeight);

        /// <summary>
        /// ErrorFeature 목록을 오류 레이어에 추가
        /// </summary>
        /// <param name="errorFeatures">ErrorFeature 목록</param>
        /// <param name="mapBounds">현재 지도 범위</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>렌더링된 오류 개수</returns>
        Task<int> AddErrorFeaturesToLayerAsync(List<ErrorFeature> errorFeatures, MapBounds mapBounds, double zoomLevel);

        /// <summary>
        /// 오류 레이어 렌더링 (이미지 생성)
        /// </summary>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>렌더링된 오류 레이어 이미지</returns>
        Task<ImageSource?> RenderErrorLayerAsync(MapBounds mapBounds, double zoomLevel);

        /// <summary>
        /// 특정 ErrorFeature 하이라이트
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>하이라이트 성공 여부</returns>
        Task<bool> HighlightErrorFeatureAsync(string errorFeatureId, MapBounds mapBounds, double zoomLevel);

        /// <summary>
        /// 모든 하이라이트 제거
        /// </summary>
        void ClearHighlights();

        /// <summary>
        /// 오류 레이어 가시성 설정
        /// </summary>
        /// <param name="isVisible">가시성 여부</param>
        void SetLayerVisibility(bool isVisible);

        /// <summary>
        /// 오류 레이어 투명도 설정
        /// </summary>
        /// <param name="opacity">투명도 (0.0 ~ 1.0)</param>
        void SetLayerOpacity(double opacity);

        /// <summary>
        /// 필터 조건에 따른 ErrorFeature 표시/숨김
        /// </summary>
        /// <param name="filter">필터 조건</param>
        /// <returns>필터링된 ErrorFeature 개수</returns>
        Task<int> ApplyFilterAsync(ErrorFeatureFilter filter);

        /// <summary>
        /// 클러스터링 활성화/비활성화
        /// </summary>
        /// <param name="enableClustering">클러스터링 사용 여부</param>
        /// <param name="clusterDistance">클러스터링 거리 (픽셀)</param>
        void SetClusteringEnabled(bool enableClustering, double clusterDistance = 50.0);

        /// <summary>
        /// 지도 좌표를 화면 좌표로 변환
        /// </summary>
        /// <param name="mapX">지도 X 좌표</param>
        /// <param name="mapY">지도 Y 좌표</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>화면 좌표</returns>
        (double ScreenX, double ScreenY) MapToScreen(double mapX, double mapY, MapBounds mapBounds);

        /// <summary>
        /// 화면 좌표를 지도 좌표로 변환
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>지도 좌표</returns>
        (double MapX, double MapY) ScreenToMap(double screenX, double screenY, MapBounds mapBounds);

        /// <summary>
        /// 화면 좌표에서 ErrorFeature 검색
        /// </summary>
        /// <param name="screenX">화면 X 좌표</param>
        /// <param name="screenY">화면 Y 좌표</param>
        /// <param name="tolerance">허용 오차 (픽셀)</param>
        /// <param name="mapBounds">지도 범위</param>
        /// <returns>검색된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> HitTestAsync(double screenX, double screenY, double tolerance, MapBounds mapBounds);

        /// <summary>
        /// 오류 레이어 통계 정보 조회
        /// </summary>
        /// <returns>레이어 통계</returns>
        Task<ErrorLayerStatistics> GetLayerStatisticsAsync();

        /// <summary>
        /// 오류 레이어 초기화
        /// </summary>
        void ClearErrorLayer();

        /// <summary>
        /// 현재 렌더링된 ErrorFeature 개수
        /// </summary>
        int RenderedFeatureCount { get; }

        /// <summary>
        /// 오류 레이어 가시성 상태
        /// </summary>
        bool IsLayerVisible { get; }

        /// <summary>
        /// 클러스터링 활성화 상태
        /// </summary>
        bool IsClusteringEnabled { get; }
    }

    /// <summary>
    /// 지도 범위 정보
    /// </summary>
    public class MapBounds
    {
        /// <summary>
        /// 최소 X 좌표
        /// </summary>
        public double MinX { get; set; }

        /// <summary>
        /// 최소 Y 좌표
        /// </summary>
        public double MinY { get; set; }

        /// <summary>
        /// 최대 X 좌표
        /// </summary>
        public double MaxX { get; set; }

        /// <summary>
        /// 최대 Y 좌표
        /// </summary>
        public double MaxY { get; set; }

        /// <summary>
        /// 지도 너비
        /// </summary>
        public double Width => MaxX - MinX;

        /// <summary>
        /// 지도 높이
        /// </summary>
        public double Height => MaxY - MinY;

        /// <summary>
        /// 지도 중심점 X 좌표
        /// </summary>
        public double CenterX => (MinX + MaxX) / 2.0;

        /// <summary>
        /// 지도 중심점 Y 좌표
        /// </summary>
        public double CenterY => (MinY + MaxY) / 2.0;

        /// <summary>
        /// 점이 범위 내에 있는지 확인
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>범위 내 포함 여부</returns>
        public bool Contains(double x, double y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }

        /// <summary>
        /// 다른 범위와 교차하는지 확인
        /// </summary>
        /// <param name="other">다른 지도 범위</param>
        /// <returns>교차 여부</returns>
        public bool Intersects(MapBounds other)
        {
            return !(other.MinX > MaxX || other.MaxX < MinX || other.MinY > MaxY || other.MaxY < MinY);
        }
    }

    // ErrorFeatureFilter 클래스는 IErrorTrackingService.cs에 정의됨 (중복 제거)

    /// <summary>
    /// 오류 레이어 통계 정보
    /// </summary>
    public class ErrorLayerStatistics
    {
        /// <summary>
        /// 총 ErrorFeature 개수
        /// </summary>
        public int TotalFeatureCount { get; set; }

        /// <summary>
        /// 현재 렌더링된 ErrorFeature 개수
        /// </summary>
        public int RenderedFeatureCount { get; set; }

        /// <summary>
        /// 필터링으로 숨겨진 ErrorFeature 개수
        /// </summary>
        public int FilteredFeatureCount { get; set; }

        /// <summary>
        /// 클러스터 개수
        /// </summary>
        public int ClusterCount { get; set; }

        /// <summary>
        /// 평균 렌더링 시간 (밀리초)
        /// </summary>
        public double AverageRenderTimeMs { get; set; }

        /// <summary>
        /// 마지막 렌더링 시간 (밀리초)
        /// </summary>
        public double LastRenderTimeMs { get; set; }

        /// <summary>
        /// 총 렌더링 횟수
        /// </summary>
        public long TotalRenderCount { get; set; }

        /// <summary>
        /// 레이어 메모리 사용량 (바이트)
        /// </summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>
        /// 현재 지도 범위
        /// </summary>
        public MapBounds? CurrentBounds { get; set; }

        /// <summary>
        /// 현재 줌 레벨
        /// </summary>
        public double CurrentZoomLevel { get; set; }

        /// <summary>
        /// 레이어 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdatedAt { get; set; }
    }
}