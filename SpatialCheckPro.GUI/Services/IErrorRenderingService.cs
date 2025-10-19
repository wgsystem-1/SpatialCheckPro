using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 렌더링 서비스 인터페이스
    /// </summary>
    public interface IErrorRenderingService
    {
        /// <summary>
        /// 오류 피처들을 지도에 렌더링합니다
        /// </summary>
        /// <param name="errors">렌더링할 오류 목록</param>
        /// <returns>렌더링 성공 여부</returns>
        Task<bool> RenderErrorsAsync(List<ErrorFeature> errors);

        /// <summary>
        /// 렌더링된 오류들을 지웁니다
        /// </summary>
        Task ClearRenderedErrorsAsync();

        /// <summary>
        /// 특정 오류를 하이라이트합니다
        /// </summary>
        /// <param name="errorId">하이라이트할 오류 ID</param>
        /// <param name="duration">하이라이트 지속 시간</param>
        Task HighlightErrorAsync(string errorId, TimeSpan duration);
    }
}