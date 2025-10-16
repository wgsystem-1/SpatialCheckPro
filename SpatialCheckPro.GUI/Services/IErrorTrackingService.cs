using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 피처 추적 서비스 인터페이스
    /// </summary>
    public interface IErrorTrackingService
    {
        /// <summary>
        /// 오류 피처 로드 및 관리
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>로드된 오류 피처 목록</returns>
        Task<List<ErrorFeature>> LoadErrorFeaturesAsync(string gdbPath);

        /// <summary>
        /// 지정된 위치에서 오류 검색
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="tolerance">허용 거리</param>
        /// <returns>검색된 오류 피처 목록</returns>
        Task<List<ErrorFeature>> SearchErrorsAtLocationAsync(double x, double y, double tolerance);

        /// <summary>
        /// 오류 선택
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        Task SelectErrorAsync(string errorId);

        /// <summary>
        /// 오류로 줌 이동
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        /// <returns>성공 여부</returns>
        Task<bool> ZoomToErrorAsync(string errorId);

        /// <summary>
        /// 오류 하이라이트
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        /// <param name="duration">하이라이트 지속 시간</param>
        /// <returns>성공 여부</returns>
        Task<bool> HighlightErrorAsync(string errorId, TimeSpan duration);

        /// <summary>
        /// 오류 상태 업데이트
        /// </summary>
        /// <param name="errorId">오류 ID</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>성공 여부</returns>
        Task<bool> UpdateErrorStatusAsync(string errorId, string newStatus);

        /// <summary>
        /// 다중 오류 상태 업데이트
        /// </summary>
        /// <param name="errorIds">오류 ID 목록</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>성공 여부</returns>
        Task<bool> UpdateMultipleErrorsAsync(List<string> errorIds, string newStatus);

        /// <summary>
        /// 거리 계산
        /// </summary>
        /// <param name="x1">첫 번째 점 X</param>
        /// <param name="y1">첫 번째 점 Y</param>
        /// <param name="x2">두 번째 점 X</param>
        /// <param name="y2">두 번째 점 Y</param>
        /// <returns>유클리드 거리</returns>
        double CalculateDistance(double x1, double y1, double x2, double y2);

        /// <summary>
        /// 선택된 오류 목록 반환
        /// </summary>
        /// <returns>선택된 오류 피처 목록</returns>
        List<ErrorFeature> GetSelectedErrors();

        /// <summary>
        /// 모든 선택 해제
        /// </summary>
        void ClearSelection();

        /// <summary>
        /// 오류 타입별 필터링
        /// </summary>
        /// <param name="errors">전체 오류 목록</param>
        /// <param name="errorType">필터링할 오류 타입</param>
        /// <returns>필터링된 오류 목록</returns>
        List<ErrorFeature> FilterErrorsByType(List<ErrorFeature> errors, string errorType);

        /// <summary>
        /// 심각도별 필터링
        /// </summary>
        /// <param name="errors">전체 오류 목록</param>
        /// <param name="severity">필터링할 심각도</param>
        /// <returns>필터링된 오류 목록</returns>
        List<ErrorFeature> FilterErrorsBySeverity(List<ErrorFeature> errors, string severity);

        /// <summary>
        /// 오류 선택 이벤트
        /// </summary>
        event EventHandler<ErrorSelectedEventArgs>? ErrorSelected;

        /// <summary>
        /// 오류 상태 변경 이벤트
        /// </summary>
        event EventHandler<ErrorStatusChangedEventArgs>? ErrorStatusChanged;
    }

    /// <summary>
    /// 오류 선택 이벤트 인자
    /// </summary>
    public class ErrorSelectedEventArgs : EventArgs
    {
        public string ErrorId { get; set; } = string.Empty;
        public ErrorFeature? ErrorFeature { get; set; }
        public DateTime SelectionTime { get; set; } = DateTime.Now;
    }

    // ErrorStatusChangedEventArgs는 SpatialCheckPro.Models 네임스페이스에서 공통으로 사용
}