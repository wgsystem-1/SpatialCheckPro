using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 피처 선택 및 시각화 서비스 인터페이스
    /// </summary>
    public interface IErrorSelectionService
    {
        /// <summary>
        /// ErrorFeature 선택
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택 성공 여부</returns>
        Task<bool> SelectErrorFeatureAsync(string errorFeatureId, bool isMultiSelect = false);

        /// <summary>
        /// ErrorFeature 선택 해제
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID (null이면 모든 선택 해제)</param>
        /// <returns>선택 해제 성공 여부</returns>
        Task<bool> DeselectErrorFeatureAsync(string? errorFeatureId = null);

        /// <summary>
        /// 선택된 ErrorFeature 목록 조회
        /// </summary>
        /// <returns>선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> GetSelectedErrorFeaturesAsync();

        /// <summary>
        /// ErrorFeature 선택 상태 확인
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <returns>선택 여부</returns>
        bool IsErrorFeatureSelected(string errorFeatureId);

        /// <summary>
        /// 선택된 ErrorFeature 개수 조회
        /// </summary>
        /// <returns>선택된 개수</returns>
        int GetSelectedCount();

        /// <summary>
        /// 영역 선택 (사각형 드래그)
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SelectErrorFeaturesInAreaAsync(double minX, double minY, double maxX, double maxY, bool isMultiSelect = false);

        /// <summary>
        /// 조건에 따른 ErrorFeature 선택
        /// </summary>
        /// <param name="selectionCriteria">선택 조건</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SelectErrorFeaturesByCriteriaAsync(ErrorSelectionCriteria selectionCriteria, bool isMultiSelect = false);

        /// <summary>
        /// 선택 반전 (선택된 것은 해제, 해제된 것은 선택)
        /// </summary>
        /// <returns>반전 후 선택된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> InvertSelectionAsync();

        /// <summary>
        /// 선택 상태 저장
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveSelectionAsync(string selectionName);

        /// <summary>
        /// 선택 상태 복원
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>복원 성공 여부</returns>
        Task<bool> RestoreSelectionAsync(string selectionName);

        /// <summary>
        /// 저장된 선택 상태 목록 조회
        /// </summary>
        /// <returns>선택 상태 이름 목록</returns>
        Task<List<string>> GetSavedSelectionsAsync();

        /// <summary>
        /// 선택 상태 삭제
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>삭제 성공 여부</returns>
        Task<bool> DeleteSavedSelectionAsync(string selectionName);

        /// <summary>
        /// 선택 변경 이벤트
        /// </summary>
        event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

        /// <summary>
        /// 선택 시각화 설정 업데이트
        /// </summary>
        /// <param name="visualSettings">시각화 설정</param>
        void UpdateSelectionVisualSettings(SelectionVisualSettings visualSettings);

        /// <summary>
        /// 현재 선택 시각화 설정 조회
        /// </summary>
        /// <returns>현재 시각화 설정</returns>
        SelectionVisualSettings GetSelectionVisualSettings();
    }

    /// <summary>
    /// ErrorFeature 선택 조건
    /// </summary>
    public class ErrorSelectionCriteria
    {
        /// <summary>
        /// 심각도 조건
        /// </summary>
        public List<string>? Severities { get; set; }

        /// <summary>
        /// 오류 타입 조건
        /// </summary>
        public List<string>? ErrorTypes { get; set; }

        /// <summary>
        /// 상태 조건
        /// </summary>
        public List<string>? Statuses { get; set; }

        /// <summary>
        /// 원본 피처 클래스 조건
        /// </summary>
        public List<string>? SourceClasses { get; set; }

        /// <summary>
        /// 생성 날짜 범위 시작
        /// </summary>
        public DateTime? CreatedAfter { get; set; }

        /// <summary>
        /// 생성 날짜 범위 끝
        /// </summary>
        public DateTime? CreatedBefore { get; set; }

        /// <summary>
        /// 공간 범위 조건
        /// </summary>
        public BoundingBox? SpatialBounds { get; set; }

        /// <summary>
        /// 텍스트 검색 조건
        /// </summary>
        public string? SearchText { get; set; }

        /// <summary>
        /// 최대 선택 개수 제한
        /// </summary>
        public int? MaxSelectionCount { get; set; }

        /// <summary>
        /// 조건이 비어있는지 확인
        /// </summary>
        public bool IsEmpty => 
            (Severities == null || !Severities.Any()) &&
            (ErrorTypes == null || !ErrorTypes.Any()) &&
            (Statuses == null || !Statuses.Any()) &&
            (SourceClasses == null || !SourceClasses.Any()) &&
            CreatedAfter == null &&
            CreatedBefore == null &&
            SpatialBounds == null &&
            string.IsNullOrEmpty(SearchText);
    }

    /// <summary>
    /// 선택 시각화 설정
    /// </summary>
    public class SelectionVisualSettings
    {
        /// <summary>
        /// 선택 색상
        /// </summary>
        public System.Windows.Media.Color SelectionColor { get; set; } = System.Windows.Media.Colors.Cyan;

        /// <summary>
        /// 선택 테두리 색상
        /// </summary>
        public System.Windows.Media.Color SelectionStrokeColor { get; set; } = System.Windows.Media.Colors.Blue;

        /// <summary>
        /// 선택 테두리 두께
        /// </summary>
        public double SelectionStrokeWidth { get; set; } = 2.0;

        /// <summary>
        /// 선택 투명도 (0.0 ~ 1.0)
        /// </summary>
        public double SelectionOpacity { get; set; } = 0.8;

        /// <summary>
        /// 선택 크기 배율
        /// </summary>
        public double SelectionSizeMultiplier { get; set; } = 1.3;

        /// <summary>
        /// 선택 애니메이션 사용 여부
        /// </summary>
        public bool UseSelectionAnimation { get; set; } = true;

        /// <summary>
        /// 선택 애니메이션 지속 시간 (밀리초)
        /// </summary>
        public int SelectionAnimationDurationMs { get; set; } = 200;

        /// <summary>
        /// 선택 깜빡임 효과 사용 여부
        /// </summary>
        public bool UseBlinkEffect { get; set; } = false;

        /// <summary>
        /// 깜빡임 주기 (밀리초)
        /// </summary>
        public int BlinkIntervalMs { get; set; } = 500;

        /// <summary>
        /// 다중 선택 시 다른 색상 사용 여부
        /// </summary>
        public bool UseMultiSelectColor { get; set; } = true;

        /// <summary>
        /// 다중 선택 색상
        /// </summary>
        public System.Windows.Media.Color MultiSelectColor { get; set; } = System.Windows.Media.Colors.Orange;

        /// <summary>
        /// 선택 순서 표시 여부
        /// </summary>
        public bool ShowSelectionOrder { get; set; } = false;

        /// <summary>
        /// 선택 순서 텍스트 색상
        /// </summary>
        public System.Windows.Media.Color SelectionOrderTextColor { get; set; } = System.Windows.Media.Colors.White;

        /// <summary>
        /// 선택 순서 텍스트 크기
        /// </summary>
        public double SelectionOrderTextSize { get; set; } = 10.0;
    }

    /// <summary>
    /// 선택 변경 이벤트 인자
    /// </summary>
    public class SelectionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 선택된 ErrorFeature ID 목록
        /// </summary>
        public List<string> SelectedErrorFeatureIds { get; set; } = new List<string>();

        /// <summary>
        /// 새로 선택된 ErrorFeature ID 목록
        /// </summary>
        public List<string> NewlySelectedIds { get; set; } = new List<string>();

        /// <summary>
        /// 선택 해제된 ErrorFeature ID 목록
        /// </summary>
        public List<string> DeselectedIds { get; set; } = new List<string>();

        /// <summary>
        /// 선택 변경 유형
        /// </summary>
        public SelectionChangeType ChangeType { get; set; }

        /// <summary>
        /// 선택 변경 시간
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 선택 변경 원인
        /// </summary>
        public string? ChangeReason { get; set; }
    }

    /// <summary>
    /// 선택 변경 유형
    /// </summary>
    public enum SelectionChangeType
    {
        /// <summary>
        /// 단일 선택
        /// </summary>
        SingleSelect,

        /// <summary>
        /// 다중 선택 추가
        /// </summary>
        MultiSelectAdd,

        /// <summary>
        /// 선택 해제
        /// </summary>
        Deselect,

        /// <summary>
        /// 모든 선택 해제
        /// </summary>
        ClearAll,

        /// <summary>
        /// 영역 선택
        /// </summary>
        AreaSelect,

        /// <summary>
        /// 조건 선택
        /// </summary>
        CriteriaSelect,

        /// <summary>
        /// 선택 반전
        /// </summary>
        InvertSelection,

        /// <summary>
        /// 선택 상태 복원
        /// </summary>
        RestoreSelection
    }

    /// <summary>
    /// 저장된 선택 상태 정보
    /// </summary>
    public class SavedSelection
    {
        /// <summary>
        /// 선택 상태 이름
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 선택된 ErrorFeature ID 목록
        /// </summary>
        public List<string> SelectedErrorFeatureIds { get; set; } = new List<string>();

        /// <summary>
        /// 저장 시간
        /// </summary>
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 설명
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 선택 조건 (조건 선택인 경우)
        /// </summary>
        public ErrorSelectionCriteria? SelectionCriteria { get; set; }
    }
}