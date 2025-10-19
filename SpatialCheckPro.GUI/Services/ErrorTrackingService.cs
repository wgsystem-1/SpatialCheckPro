using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 피처 추적 서비스 구현 클래스
    /// </summary>
    public class ErrorTrackingService : IErrorTrackingService
    {
        private readonly ILogger<ErrorTrackingService> _logger;
        private readonly QcErrorService _qcErrorService;
        private readonly List<ErrorFeature> _loadedErrors = new List<ErrorFeature>();
        private readonly HashSet<string> _selectedErrorIds = new HashSet<string>();

        public ErrorTrackingService(ILogger<ErrorTrackingService> logger, QcErrorService qcErrorService)
        {
            _logger = logger;
            _qcErrorService = qcErrorService;
        }

        /// <summary>
        /// 오류 선택 이벤트
        /// </summary>
        public event EventHandler<ErrorSelectedEventArgs>? ErrorSelected;

        /// <summary>
        /// 오류 상태 변경 이벤트
        /// </summary>
        public event EventHandler<ErrorStatusChangedEventArgs>? ErrorStatusChanged;

        /// <summary>
        /// FileGDB에서 오류 피처를 로드합니다
        /// </summary>
        public async Task<List<ErrorFeature>> LoadErrorFeaturesAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("오류 피처 로드 시작: {GdbPath}", gdbPath);

                // QcErrorService를 통해 오류 데이터 로드
                var qcErrors = await _qcErrorService.GetQcErrorsAsync(gdbPath);
                
                _loadedErrors.Clear();

                foreach (var qcError in qcErrors)
                {
                    var errorFeature = new ErrorFeature
                    {
                        Id = qcError.GlobalID,
                        QcError = qcError,
                        Symbol = new SpatialCheckPro.GUI.Models.ErrorSymbol()
                        {
                            GeometryType = qcError.GeometryType ?? "Point",
                            Size = 8.0,
                            MarkerStyle = "Circle"
                        }
                    };

                    // WKT에서 지오메트리 생성
                    errorFeature.CreateGeometryFromWKT();

                    _loadedErrors.Add(errorFeature);
                }

                _logger.LogInformation("오류 피처 로드 완료: {Count}개", _loadedErrors.Count);
                return new List<ErrorFeature>(_loadedErrors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 피처 로드 실패: {GdbPath}", gdbPath);
                return new List<ErrorFeature>();
            }
        }

        /// <summary>
        /// 지정된 위치에서 허용 거리 내의 오류를 검색합니다
        /// </summary>
        public Task<List<ErrorFeature>> SearchErrorsAtLocationAsync(double x, double y, double tolerance)
        {
            try
            {
                var nearbyErrors = new List<ErrorFeature>();

                foreach (var error in _loadedErrors)
                {
                    if (error.ContainsPoint(x, y, tolerance))
                    {
                        nearbyErrors.Add(error);
                    }
                }

                // 거리순으로 정렬
                nearbyErrors.Sort((e1, e2) =>
                {
                    var dist1 = e1.DistanceTo(x, y);
                    var dist2 = e2.DistanceTo(x, y);
                    return dist1.CompareTo(dist2);
                });

                _logger.LogDebug("위치 ({X}, {Y})에서 {Count}개 오류 발견", x, y, nearbyErrors.Count);
                return Task.FromResult(nearbyErrors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 검색 실패: ({X}, {Y})", x, y);
                return Task.FromResult(new List<ErrorFeature>());
            }
        }

        /// <summary>
        /// 오류를 선택합니다
        /// </summary>
        public async Task SelectErrorAsync(string errorId)
        {
            try
            {
                var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                if (errorFeature != null)
                {
                    _selectedErrorIds.Add(errorId);
                    errorFeature.IsSelected = true;
                    errorFeature.Symbol.ApplySelectionStyle();

                    ErrorSelected?.Invoke(this, new ErrorSelectedEventArgs
                    {
                        ErrorId = errorId,
                        ErrorFeature = errorFeature
                    });

                    _logger.LogDebug("오류 선택됨: {ErrorId}", errorId);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 선택 실패: {ErrorId}", errorId);
            }
        }

        /// <summary>
        /// 오류 위치로 줌 이동합니다
        /// </summary>
        public Task<bool> ZoomToErrorAsync(string errorId)
        {
            try
            {
                var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                if (errorFeature != null)
                {
                    // 실제 구현에서는 MapView의 줌 메서드를 호출해야 함
                    _logger.LogInformation("오류 위치로 줌: {ErrorId} at ({X}, {Y})", 
                        errorId, errorFeature.QcError.X, errorFeature.QcError.Y);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "줌 이동 실패: {ErrorId}", errorId);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 오류를 하이라이트합니다
        /// </summary>
        public Task<bool> HighlightErrorAsync(string errorId, TimeSpan duration)
        {
            try
            {
                var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                if (errorFeature != null)
                {
                    errorFeature.IsHighlighted = true;
                    errorFeature.Symbol.ApplyHighlightStyle();

                    // 지정된 시간 후 하이라이트 제거
                    _ = Task.Delay(duration).ContinueWith(_ =>
                    {
                        errorFeature.IsHighlighted = false;
                        errorFeature.Symbol.RemoveHighlightStyle();
                    });

                    _logger.LogDebug("오류 하이라이트: {ErrorId}, 지속시간: {Duration}", errorId, duration);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "하이라이트 실패: {ErrorId}", errorId);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 오류 상태를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateErrorStatusAsync(string errorId, string newStatus)
        {
            try
            {
                var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                if (errorFeature != null)
                {
                    var oldStatus = errorFeature.QcError.Status;
                    
                    // QcErrorService를 통해 데이터베이스 업데이트 (gdbPath 필요)
                    var success = await _qcErrorService.UpdateErrorStatusAsync("", errorId, newStatus);
                    
                    if (success)
                    {
                        errorFeature.QcError.Status = newStatus;
                        errorFeature.LastUpdated = DateTime.UtcNow;

                        ErrorStatusChanged?.Invoke(this, new ErrorStatusChangedEventArgs
                        {
                            ErrorId = errorId,
                            OldStatus = oldStatus,
                            NewStatus = newStatus
                        });

                        _logger.LogInformation("오류 상태 업데이트: {ErrorId} {OldStatus} → {NewStatus}", 
                            errorId, oldStatus, newStatus);
                    }

                    return success;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "상태 업데이트 실패: {ErrorId}", errorId);
                return false;
            }
        }

        /// <summary>
        /// 다중 오류 상태를 업데이트합니다
        /// </summary>
        public async Task<bool> UpdateMultipleErrorsAsync(List<string> errorIds, string newStatus)
        {
            try
            {
                var success = await _qcErrorService.UpdateMultipleErrorsAsync(errorIds, newStatus);
                
                if (success)
                {
                    foreach (var errorId in errorIds)
                    {
                        var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                        if (errorFeature != null)
                        {
                            errorFeature.QcError.Status = newStatus;
                            errorFeature.LastUpdated = DateTime.UtcNow;
                        }
                    }

                    _logger.LogInformation("다중 오류 상태 업데이트 완료: {Count}개 → {NewStatus}", 
                        errorIds.Count, newStatus);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "다중 상태 업데이트 실패: {Count}개", errorIds.Count);
                return false;
            }
        }

        /// <summary>
        /// 유클리드 거리를 계산합니다
        /// </summary>
        public double CalculateDistance(double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 선택된 오류 목록을 반환합니다
        /// </summary>
        public List<ErrorFeature> GetSelectedErrors()
        {
            return _loadedErrors.Where(e => _selectedErrorIds.Contains(e.Id)).ToList();
        }

        /// <summary>
        /// 모든 선택을 해제합니다
        /// </summary>
        public void ClearSelection()
        {
            foreach (var errorId in _selectedErrorIds)
            {
                var errorFeature = _loadedErrors.FirstOrDefault(e => e.Id == errorId);
                if (errorFeature != null)
                {
                    errorFeature.IsSelected = false;
                    errorFeature.Symbol.RemoveSelectionStyle();
                }
            }

            _selectedErrorIds.Clear();
            _logger.LogDebug("모든 선택 해제됨");
        }

        /// <summary>
        /// 오류 타입별로 필터링합니다
        /// </summary>
        public List<ErrorFeature> FilterErrorsByType(List<ErrorFeature> errors, string errorType)
        {
            return errors.Where(e => e.QcError.ErrType.Equals(errorType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// 심각도별로 필터링합니다
        /// </summary>
        public List<ErrorFeature> FilterErrorsBySeverity(List<ErrorFeature> errors, string severity)
        {
            return errors.Where(e => e.QcError.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}