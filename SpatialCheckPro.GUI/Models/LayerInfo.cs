using System;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 지도 레이어 정보를 나타내는 모델 클래스
    /// </summary>
    public class LayerInfo
    {
        /// <summary>
        /// 레이어 이름
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 피처 개수
        /// </summary>
        public long FeatureCount { get; set; }

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// QC 오류 레이어 여부
        /// </summary>
        public bool IsQcError { get; set; }

        /// <summary>
        /// 레이어 표시 여부
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 레이어 투명도 (0.0 ~ 1.0)
        /// </summary>
        public double Opacity { get; set; } = 1.0;

        /// <summary>
        /// 레이어 설명
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 레이어 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 레이어 표시 이름 (UI용)
        /// </summary>
        public string DisplayName => IsQcError ? $"🔴 {Name}" : $"📍 {Name}";

        /// <summary>
        /// 피처 개수 표시 문자열
        /// </summary>
        public string FeatureCountText => FeatureCount > 0 ? $"({FeatureCount:N0}개)" : "(빈 레이어)";

        /// <summary>
        /// 지오메트리 타입 표시 문자열
        /// </summary>
        public string GeometryTypeText => GeometryType switch
        {
            "wkbPoint" or "Point" => "점",
            "wkbLineString" or "LineString" => "선",
            "wkbPolygon" or "Polygon" => "면",
            "wkbMultiPoint" or "MultiPoint" => "다중점",
            "wkbMultiLineString" or "MultiLineString" => "다중선",
            "wkbMultiPolygon" or "MultiPolygon" => "다중면",
            _ => GeometryType
        };

        /// <summary>
        /// 레이어 상태 텍스트
        /// </summary>
        public string StatusText => IsVisible ? "표시됨" : "숨김";

        public override string ToString()
        {
            return $"{DisplayName} {FeatureCountText} - {GeometryTypeText}";
        }
    }
}