using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 렌더링 서비스 구현
    /// </summary>
    public class ErrorRenderingService : IErrorRenderingService
    {
        private readonly ILogger<ErrorRenderingService> _logger;
        private readonly List<ErrorFeature> _renderedErrors = new List<ErrorFeature>();

        public ErrorRenderingService(ILogger<ErrorRenderingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 오류 피처들을 지도에 렌더링합니다
        /// </summary>
        public async Task<bool> RenderErrorsAsync(List<ErrorFeature> errors)
        {
            try
            {
                _logger.LogInformation("오류 렌더링 시작: {Count}개", errors.Count);
                
                // 기존 렌더링된 오류들 제거
                await ClearRenderedErrorsAsync();
                
                // 새로운 오류들 렌더링
                foreach (var error in errors)
                {
                    await RenderSingleErrorAsync(error);
                    _renderedErrors.Add(error);
                }
                
                _logger.LogInformation("오류 렌더링 완료: {Count}개", errors.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 렌더링 실패");
                return false;
            }
        }

        /// <summary>
        /// 렌더링된 오류들을 지웁니다
        /// </summary>
        public async Task ClearRenderedErrorsAsync()
        {
            try
            {
                _logger.LogDebug("렌더링된 오류 제거: {Count}개", _renderedErrors.Count);
                
                foreach (var error in _renderedErrors)
                {
                    await RemoveSingleErrorAsync(error);
                }
                
                _renderedErrors.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "렌더링된 오류 제거 실패");
            }
        }

        /// <summary>
        /// 특정 오류를 하이라이트합니다
        /// </summary>
        public async Task HighlightErrorAsync(string errorId, TimeSpan duration)
        {
            try
            {
                var error = _renderedErrors.Find(e => e.Id == errorId);
                if (error != null)
                {
                    error.Symbol.ApplyHighlightStyle();
                    
                    // 지정된 시간 후 하이라이트 제거
                    _ = Task.Delay(duration).ContinueWith(_ =>
                    {
                        error.Symbol.RemoveHighlightStyle();
                    });
                    
                    _logger.LogDebug("오류 하이라이트: {ErrorId}, 지속시간: {Duration}", errorId, duration);
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 하이라이트 실패: {ErrorId}", errorId);
            }
        }

        /// <summary>
        /// 단일 오류를 렌더링합니다
        /// </summary>
        private async Task RenderSingleErrorAsync(ErrorFeature error)
        {
            // 실제 구현에서는 지도 컨트롤에 오류 심볼을 추가
            await Task.Delay(1); // 렌더링 시뮬레이션
            
            _logger.LogTrace("오류 렌더링: {ErrorId} at ({X}, {Y})", 
                error.Id, error.QcError.X, error.QcError.Y);
        }

        /// <summary>
        /// 단일 오류를 제거합니다
        /// </summary>
        private async Task RemoveSingleErrorAsync(ErrorFeature error)
        {
            // 실제 구현에서는 지도 컨트롤에서 오류 심볼을 제거
            await Task.Delay(1); // 제거 시뮬레이션
            
            _logger.LogTrace("오류 제거: {ErrorId}", error.Id);
        }
    }
}