using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 피처 선택 및 시각화 서비스 구현
    /// </summary>
    public class ErrorSelectionService : IErrorSelectionService
    {
        private readonly ILogger<ErrorSelectionService> _logger;
        private readonly ISpatialIndexService _spatialIndexService;
        private readonly HashSet<string> _selectedErrorFeatureIds = new HashSet<string>();
        private readonly Dictionary<string, SavedSelection> _savedSelections = new Dictionary<string, SavedSelection>();
        private readonly List<string> _selectionOrder = new List<string>();
        private SelectionVisualSettings _visualSettings = new SelectionVisualSettings();

        /// <summary>
        /// 선택 변경 이벤트
        /// </summary>
        public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

        /// <summary>
        /// ErrorSelectionService 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="spatialIndexService">공간 인덱스 서비스</param>
        public ErrorSelectionService(ILogger<ErrorSelectionService> logger, ISpatialIndexService spatialIndexService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spatialIndexService = spatialIndexService ?? throw new ArgumentNullException(nameof(spatialIndexService));
        }

        /// <summary>
        /// ErrorFeature 선택
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택 성공 여부</returns>
        public async Task<bool> SelectErrorFeatureAsync(string errorFeatureId, bool isMultiSelect = false)
        {
            try
            {
                _logger.LogDebug("ErrorFeature 선택: {Id}, 다중선택: {MultiSelect}", errorFeatureId, isMultiSelect);

                var newlySelected = new List<string>();
                var deselected = new List<string>();

                // 단일 선택인 경우 기존 선택 해제
                if (!isMultiSelect && _selectedErrorFeatureIds.Any())
                {
                    deselected.AddRange(_selectedErrorFeatureIds);
                    _selectedErrorFeatureIds.Clear();
                    _selectionOrder.Clear();
                }

                // 새로운 선택 추가
                if (_selectedErrorFeatureIds.Add(errorFeatureId))
                {
                    newlySelected.Add(errorFeatureId);
                    _selectionOrder.Add(errorFeatureId);
                }

                // 선택 변경 이벤트 발생
                var changeType = isMultiSelect ? SelectionChangeType.MultiSelectAdd : SelectionChangeType.SingleSelect;
                await RaiseSelectionChangedEventAsync(newlySelected, deselected, changeType, "사용자 선택");

                _logger.LogDebug("ErrorFeature 선택 완료: 총 {Count}개 선택됨", _selectedErrorFeatureIds.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 선택 실패: {Id}", errorFeatureId);
                return false;
            }
        }

        /// <summary>
        /// ErrorFeature 선택 해제
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID (null이면 모든 선택 해제)</param>
        /// <returns>선택 해제 성공 여부</returns>
        public async Task<bool> DeselectErrorFeatureAsync(string? errorFeatureId = null)
        {
            try
            {
                var deselected = new List<string>();

                if (errorFeatureId == null)
                {
                    // 모든 선택 해제
                    deselected.AddRange(_selectedErrorFeatureIds);
                    _selectedErrorFeatureIds.Clear();
                    _selectionOrder.Clear();
                    
                    _logger.LogDebug("모든 ErrorFeature 선택 해제: {Count}개", deselected.Count);
                    
                    await RaiseSelectionChangedEventAsync(new List<string>(), deselected, SelectionChangeType.ClearAll, "모든 선택 해제");
                }
                else
                {
                    // 특정 ErrorFeature 선택 해제
                    if (_selectedErrorFeatureIds.Remove(errorFeatureId))
                    {
                        deselected.Add(errorFeatureId);
                        _selectionOrder.Remove(errorFeatureId);
                        
                        _logger.LogDebug("ErrorFeature 선택 해제: {Id}", errorFeatureId);
                        
                        await RaiseSelectionChangedEventAsync(new List<string>(), deselected, SelectionChangeType.Deselect, "사용자 선택 해제");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorFeature 선택 해제 실패: {Id}", errorFeatureId);
                return false;
            }
        }

        /// <summary>
        /// 선택된 ErrorFeature 목록 조회
        /// </summary>
        /// <returns>선택된 ErrorFeature 목록</returns>
        public async Task<List<ErrorFeature>> GetSelectedErrorFeaturesAsync()
        {
            try
            {
                var selectedFeatures = new List<ErrorFeature>();

                foreach (var featureId in _selectedErrorFeatureIds)
                {
                    // 실제 구현에서는 ErrorFeature 저장소에서 조회
                    // 현재는 간단한 구현으로 빈 목록 반환
                    await Task.Delay(1); // 비동기 메서드 형태 유지
                }

                return selectedFeatures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택된 ErrorFeature 목록 조회 실패");
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// ErrorFeature 선택 상태 확인
        /// </summary>
        /// <param name="errorFeatureId">ErrorFeature ID</param>
        /// <returns>선택 여부</returns>
        public bool IsErrorFeatureSelected(string errorFeatureId)
        {
            return _selectedErrorFeatureIds.Contains(errorFeatureId);
        }

        /// <summary>
        /// 선택된 ErrorFeature 개수 조회
        /// </summary>
        /// <returns>선택된 개수</returns>
        public int GetSelectedCount()
        {
            return _selectedErrorFeatureIds.Count;
        }

        /// <summary>
        /// 영역 선택 (사각형 드래그)
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택된 ErrorFeature 목록</returns>
        public async Task<List<ErrorFeature>> SelectErrorFeaturesInAreaAsync(double minX, double minY, double maxX, double maxY, bool isMultiSelect = false)
        {
            try
            {
                _logger.LogDebug("영역 선택: ({MinX}, {MinY}) - ({MaxX}, {MaxY}), 다중선택: {MultiSelect}", 
                    minX, minY, maxX, maxY, isMultiSelect);

                // 공간 인덱스를 사용한 영역 내 ErrorFeature 검색
                var featuresInArea = await _spatialIndexService.SearchWithinBoundsAsync(minX, minY, maxX, maxY);

                var newlySelected = new List<string>();
                var deselected = new List<string>();

                // 단일 선택인 경우 기존 선택 해제
                if (!isMultiSelect && _selectedErrorFeatureIds.Any())
                {
                    deselected.AddRange(_selectedErrorFeatureIds);
                    _selectedErrorFeatureIds.Clear();
                    _selectionOrder.Clear();
                }

                // 영역 내 ErrorFeature 선택
                foreach (var feature in featuresInArea)
                {
                    if (_selectedErrorFeatureIds.Add(feature.Id))
                    {
                        newlySelected.Add(feature.Id);
                        _selectionOrder.Add(feature.Id);
                    }
                }

                // 선택 변경 이벤트 발생
                await RaiseSelectionChangedEventAsync(newlySelected, deselected, SelectionChangeType.AreaSelect, "영역 선택");

                _logger.LogDebug("영역 선택 완료: {NewCount}개 새로 선택, 총 {TotalCount}개 선택됨", 
                    newlySelected.Count, _selectedErrorFeatureIds.Count);

                return featuresInArea;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "영역 선택 실패: ({MinX}, {MinY}) - ({MaxX}, {MaxY})", minX, minY, maxX, maxY);
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// 조건에 따른 ErrorFeature 선택
        /// </summary>
        /// <param name="selectionCriteria">선택 조건</param>
        /// <param name="isMultiSelect">다중 선택 여부</param>
        /// <returns>선택된 ErrorFeature 목록</returns>
        public async Task<List<ErrorFeature>> SelectErrorFeaturesByCriteriaAsync(ErrorSelectionCriteria selectionCriteria, bool isMultiSelect = false)
        {
            try
            {
                _logger.LogDebug("조건 선택 시작: 다중선택: {MultiSelect}", isMultiSelect);

                // 실제 구현에서는 조건에 맞는 ErrorFeature를 검색
                // 현재는 간단한 구현으로 빈 목록 반환
                var matchingFeatures = new List<ErrorFeature>();

                var newlySelected = new List<string>();
                var deselected = new List<string>();

                // 단일 선택인 경우 기존 선택 해제
                if (!isMultiSelect && _selectedErrorFeatureIds.Any())
                {
                    deselected.AddRange(_selectedErrorFeatureIds);
                    _selectedErrorFeatureIds.Clear();
                    _selectionOrder.Clear();
                }

                // 조건에 맞는 ErrorFeature 선택
                foreach (var feature in matchingFeatures)
                {
                    if (selectionCriteria.MaxSelectionCount.HasValue && 
                        newlySelected.Count >= selectionCriteria.MaxSelectionCount.Value)
                    {
                        break;
                    }

                    if (_selectedErrorFeatureIds.Add(feature.Id))
                    {
                        newlySelected.Add(feature.Id);
                        _selectionOrder.Add(feature.Id);
                    }
                }

                // 선택 변경 이벤트 발생
                await RaiseSelectionChangedEventAsync(newlySelected, deselected, SelectionChangeType.CriteriaSelect, "조건 선택");

                _logger.LogDebug("조건 선택 완료: {NewCount}개 새로 선택, 총 {TotalCount}개 선택됨", 
                    newlySelected.Count, _selectedErrorFeatureIds.Count);

                return matchingFeatures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "조건 선택 실패");
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// 선택 반전 (선택된 것은 해제, 해제된 것은 선택)
        /// </summary>
        /// <returns>반전 후 선택된 ErrorFeature 목록</returns>
        public async Task<List<ErrorFeature>> InvertSelectionAsync()
        {
            try
            {
                _logger.LogDebug("선택 반전 시작: 현재 {Count}개 선택됨", _selectedErrorFeatureIds.Count);

                // 실제 구현에서는 모든 ErrorFeature 목록을 가져와서 반전
                // 현재는 간단한 구현으로 기존 선택만 해제
                var deselected = new List<string>(_selectedErrorFeatureIds);
                _selectedErrorFeatureIds.Clear();
                _selectionOrder.Clear();

                // 선택 변경 이벤트 발생
                await RaiseSelectionChangedEventAsync(new List<string>(), deselected, SelectionChangeType.InvertSelection, "선택 반전");

                _logger.LogDebug("선택 반전 완료: {DeselectedCount}개 해제됨", deselected.Count);

                return new List<ErrorFeature>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택 반전 실패");
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// 선택 상태 저장
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>저장 성공 여부</returns>
        public Task<bool> SaveSelectionAsync(string selectionName)
        {
            try
            {
                _logger.LogDebug("선택 상태 저장: {Name}, {Count}개 선택됨", selectionName, _selectedErrorFeatureIds.Count);

                var savedSelection = new SavedSelection
                {
                    Name = selectionName,
                    SelectedErrorFeatureIds = new List<string>(_selectedErrorFeatureIds),
                    SavedAt = DateTime.UtcNow,
                    Description = $"{_selectedErrorFeatureIds.Count}개 ErrorFeature 선택됨"
                };

                _savedSelections[selectionName] = savedSelection;

                _logger.LogDebug("선택 상태 저장 완료: {Name}", selectionName);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택 상태 저장 실패: {Name}", selectionName);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 선택 상태 복원
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>복원 성공 여부</returns>
        public async Task<bool> RestoreSelectionAsync(string selectionName)
        {
            try
            {
                if (!_savedSelections.TryGetValue(selectionName, out var savedSelection))
                {
                    _logger.LogWarning("저장된 선택 상태를 찾을 수 없습니다: {Name}", selectionName);
                    return false;
                }

                _logger.LogDebug("선택 상태 복원: {Name}, {Count}개 선택", selectionName, savedSelection.SelectedErrorFeatureIds.Count);

                var deselected = new List<string>(_selectedErrorFeatureIds);
                var newlySelected = new List<string>(savedSelection.SelectedErrorFeatureIds);

                // 기존 선택 해제 후 저장된 선택 복원
                _selectedErrorFeatureIds.Clear();
                _selectionOrder.Clear();

                foreach (var featureId in savedSelection.SelectedErrorFeatureIds)
                {
                    _selectedErrorFeatureIds.Add(featureId);
                    _selectionOrder.Add(featureId);
                }

                // 선택 변경 이벤트 발생
                await RaiseSelectionChangedEventAsync(newlySelected, deselected, SelectionChangeType.RestoreSelection, $"선택 상태 복원: {selectionName}");

                _logger.LogDebug("선택 상태 복원 완료: {Name}, 총 {Count}개 선택됨", selectionName, _selectedErrorFeatureIds.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택 상태 복원 실패: {Name}", selectionName);
                return false;
            }
        }

        /// <summary>
        /// 저장된 선택 상태 목록 조회
        /// </summary>
        /// <returns>선택 상태 이름 목록</returns>
        public async Task<List<string>> GetSavedSelectionsAsync()
        {
            return await Task.FromResult(_savedSelections.Keys.ToList());
        }

        /// <summary>
        /// 선택 상태 삭제
        /// </summary>
        /// <param name="selectionName">선택 상태 이름</param>
        /// <returns>삭제 성공 여부</returns>
        public Task<bool> DeleteSavedSelectionAsync(string selectionName)
        {
            try
            {
                var removed = _savedSelections.Remove(selectionName);
                if (removed)
                {
                    _logger.LogDebug("선택 상태 삭제 완료: {Name}", selectionName);
                }
                else
                {
                    _logger.LogWarning("삭제할 선택 상태를 찾을 수 없습니다: {Name}", selectionName);
                }
                return Task.FromResult(removed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택 상태 삭제 실패: {Name}", selectionName);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 선택 시각화 설정 업데이트
        /// </summary>
        /// <param name="visualSettings">시각화 설정</param>
        public void UpdateSelectionVisualSettings(SelectionVisualSettings visualSettings)
        {
            _visualSettings = visualSettings ?? throw new ArgumentNullException(nameof(visualSettings));
            _logger.LogDebug("선택 시각화 설정 업데이트 완료");
        }

        /// <summary>
        /// 현재 선택 시각화 설정 조회
        /// </summary>
        /// <returns>현재 시각화 설정</returns>
        public SelectionVisualSettings GetSelectionVisualSettings()
        {
            return _visualSettings;
        }

        /// <summary>
        /// 선택 변경 이벤트 발생
        /// </summary>
        /// <param name="newlySelected">새로 선택된 ID 목록</param>
        /// <param name="deselected">선택 해제된 ID 목록</param>
        /// <param name="changeType">변경 유형</param>
        /// <param name="changeReason">변경 사유</param>
        private async Task RaiseSelectionChangedEventAsync(List<string> newlySelected, List<string> deselected, 
            SelectionChangeType changeType, string changeReason)
        {
            try
            {
                var eventArgs = new SelectionChangedEventArgs
                {
                    SelectedErrorFeatureIds = new List<string>(_selectedErrorFeatureIds),
                    NewlySelectedIds = newlySelected,
                    DeselectedIds = deselected,
                    ChangeType = changeType,
                    ChangeReason = changeReason
                };

                SelectionChanged?.Invoke(this, eventArgs);

                await Task.Delay(1); // 비동기 메서드 형태 유지
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택 변경 이벤트 발생 실패");
            }
        }
    }
}