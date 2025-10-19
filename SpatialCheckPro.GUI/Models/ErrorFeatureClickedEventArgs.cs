using System;
using System.Collections.Generic;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 오류 피처 클릭 이벤트 인자 클래스
    /// </summary>
    public class ErrorFeatureClickedEventArgs : EventArgs
    {
        /// <summary>
        /// 클릭된 오류 피처 (가장 가까운 오류)
        /// </summary>
        public ErrorFeature? ClickedError { get; set; }

        /// <summary>
        /// 클릭 위치의 모든 오류 피처 목록 (거리순 정렬)
        /// </summary>
        public List<ErrorFeature> AllErrorsAtLocation { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 클릭한 지도 좌표 X
        /// </summary>
        public double ClickX { get; set; }

        /// <summary>
        /// 클릭한 지도 좌표 Y
        /// </summary>
        public double ClickY { get; set; }

        /// <summary>
        /// 허용 거리 (미터)
        /// </summary>
        public double Tolerance { get; set; } = 50.0;

        /// <summary>
        /// 다중 선택 모드 여부 (Ctrl+클릭)
        /// </summary>
        public bool IsMultiSelect { get; set; }

        /// <summary>
        /// 겹친 오류 개수
        /// </summary>
        public int OverlappingErrorCount => AllErrorsAtLocation.Count;

        /// <summary>
        /// 오류가 발견되었는지 여부
        /// </summary>
        public bool HasErrors => AllErrorsAtLocation.Count > 0;

        /// <summary>
        /// 가장 가까운 오류까지의 거리 (미터)
        /// </summary>
        public double DistanceToNearestError { get; set; }

        /// <summary>
        /// 클릭 이벤트 발생 시간
        /// </summary>
        public DateTime ClickTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 생성자
        /// </summary>
        public ErrorFeatureClickedEventArgs()
        {
        }

        /// <summary>
        /// 생성자 (기본 정보 포함)
        /// </summary>
        /// <param name="clickX">클릭 X 좌표</param>
        /// <param name="clickY">클릭 Y 좌표</param>
        /// <param name="tolerance">허용 거리</param>
        /// <param name="isMultiSelect">다중 선택 모드</param>
        public ErrorFeatureClickedEventArgs(double clickX, double clickY, double tolerance = 50.0, bool isMultiSelect = false)
        {
            ClickX = clickX;
            ClickY = clickY;
            Tolerance = tolerance;
            IsMultiSelect = isMultiSelect;
        }

        /// <summary>
        /// 오류 목록을 거리순으로 정렬하고 가장 가까운 오류를 설정합니다
        /// </summary>
        public void SortErrorsByDistance()
        {
            if (AllErrorsAtLocation.Count == 0)
                return;

            // 거리 계산 및 정렬
            AllErrorsAtLocation.Sort((e1, e2) =>
            {
                var dist1 = e1.DistanceTo(ClickX, ClickY);
                var dist2 = e2.DistanceTo(ClickX, ClickY);
                return dist1.CompareTo(dist2);
            });

            // 가장 가까운 오류 설정
            ClickedError = AllErrorsAtLocation[0];
            DistanceToNearestError = ClickedError.DistanceTo(ClickX, ClickY);
        }

        /// <summary>
        /// 특정 심각도의 오류만 필터링합니다
        /// </summary>
        /// <param name="severity">필터링할 심각도</param>
        /// <returns>필터링된 오류 목록</returns>
        public List<ErrorFeature> GetErrorsBySeverity(string severity)
        {
            return AllErrorsAtLocation.FindAll(e => 
                e.QcError.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 특정 오류 타입의 오류만 필터링합니다
        /// </summary>
        /// <param name="errorType">필터링할 오류 타입</param>
        /// <returns>필터링된 오류 목록</returns>
        public List<ErrorFeature> GetErrorsByType(string errorType)
        {
            return AllErrorsAtLocation.FindAll(e => 
                e.QcError.ErrType.Equals(errorType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 이벤트 정보의 문자열 표현을 반환합니다
        /// </summary>
        /// <returns>이벤트 정보 문자열</returns>
        public override string ToString()
        {
            var errorInfo = HasErrors ? 
                $"{OverlappingErrorCount}개 오류 발견, 가장 가까운 거리: {DistanceToNearestError:F2}m" : 
                "오류 없음";
            
            return $"ErrorFeatureClicked at ({ClickX:F2}, {ClickY:F2}): {errorInfo}";
        }
    }
}