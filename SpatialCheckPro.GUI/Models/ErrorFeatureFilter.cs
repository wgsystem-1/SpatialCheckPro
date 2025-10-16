using System;
using System.Collections.Generic;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 오류 피처 필터링 조건을 나타내는 클래스
    /// </summary>
    public class ErrorFeatureFilter
    {
        /// <summary>
        /// 오류 유형 필터 (GEOM, REL, ATTR, SCHEMA)
        /// </summary>
        public List<string> ErrorTypes { get; set; } = new List<string>();

        /// <summary>
        /// 심각도 필터 (CRIT, MAJOR, MINOR, INFO)
        /// </summary>
        public List<string> Severities { get; set; } = new List<string>();

        /// <summary>
        /// 상태 필터 (OPEN, FIXED, IGNORED, FALSE_POS)
        /// </summary>
        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>
        /// 오류 코드 필터 (DUP001, OVL001 등)
        /// </summary>
        public List<string> ErrorCodes { get; set; } = new List<string>();

        /// <summary>
        /// 소스 클래스 필터
        /// </summary>
        public List<string> SourceClasses { get; set; } = new List<string>();

        /// <summary>
        /// 담당자 필터
        /// </summary>
        public List<string> Assignees { get; set; } = new List<string>();

        /// <summary>
        /// 생성 날짜 범위 시작
        /// </summary>
        public DateTime? CreatedAfter { get; set; }

        /// <summary>
        /// 생성 날짜 범위 끝
        /// </summary>
        public DateTime? CreatedBefore { get; set; }

        /// <summary>
        /// 수정 날짜 범위 시작
        /// </summary>
        public DateTime? UpdatedAfter { get; set; }

        /// <summary>
        /// 수정 날짜 범위 끝
        /// </summary>
        public DateTime? UpdatedBefore { get; set; }

        /// <summary>
        /// 공간 범위 필터
        /// </summary>
        public BoundingBox? SpatialBounds { get; set; }

        /// <summary>
        /// 검색 텍스트 (메시지나 상세 정보에서 검색)
        /// </summary>
        public string? SearchText { get; set; }

        /// <summary>
        /// 대소문자 구분 여부
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// 필터가 비어있는지 확인
        /// </summary>
        public bool IsEmpty =>
            ErrorTypes.Count == 0 &&
            Severities.Count == 0 &&
            Statuses.Count == 0 &&
            ErrorCodes.Count == 0 &&
            SourceClasses.Count == 0 &&
            Assignees.Count == 0 &&
            CreatedAfter == null &&
            CreatedBefore == null &&
            UpdatedAfter == null &&
            UpdatedBefore == null &&
            SpatialBounds == null &&
            string.IsNullOrEmpty(SearchText);
    }
}