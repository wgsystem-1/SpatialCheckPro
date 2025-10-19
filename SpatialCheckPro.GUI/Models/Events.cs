#nullable enable
using System;
using System.Collections.Generic;
using OSGeo.OGR;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 지도 범위 변경 이벤트 인수
    /// </summary>
    public class MapExtentChangedEventArgs : EventArgs
    {
        public Envelope NewExtent { get; set; } = new Envelope();
    }

    /// <summary>
    /// 레이어 로드 완료 이벤트 인수
    /// </summary>
    public class LayerLoadedEventArgs : EventArgs
    {
        public string LayerName { get; set; } = string.Empty;
        public int FeatureCount { get; set; }
        public bool IsSuccessful { get; set; }
    }

    /// <summary>
    /// 피처 선택 이벤트 인수
    /// </summary>
    public class FeatureSelectedEventArgs : EventArgs
    {
        public string? FeatureId { get; set; }
        public string? LayerName { get; set; }
        public Dictionary<string, object>? Attributes { get; set; }
    }



    /// <summary>
    /// 지도 상태 변경 이벤트 인수
    /// </summary>
    public class MapStatusChangedEventArgs : EventArgs
    {
        public string Status { get; set; } = string.Empty;
    }
}