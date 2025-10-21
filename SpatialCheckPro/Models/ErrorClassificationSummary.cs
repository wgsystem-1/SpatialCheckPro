using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 지오메트리/비지오메트리 오류 분류 요약
    /// </summary>
    public class ErrorClassificationSummary
    {
        /// <summary>지오메트리 오류 총합</summary>
        public int GeometryErrorCount { get; set; }

        /// <summary>비지오메트리 오류 총합 (FileGDB/테이블/스키마/관계/속성관계 등)</summary>
        public int NonGeometryErrorCount { get; set; }

        /// <summary>지오메트리 오류 코드별 개수</summary>
        public Dictionary<string, int> GeometryByType { get; set; } = new();

        /// <summary>비지오메트리 오류 코드별 개수</summary>
        public Dictionary<string, int> NonGeometryByType { get; set; } = new();
    }
}


