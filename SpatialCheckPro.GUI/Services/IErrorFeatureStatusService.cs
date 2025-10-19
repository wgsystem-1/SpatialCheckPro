#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 피처 상태 관리 서비스 인터페이스 (GUI 전용)
    /// </summary>
    public interface IErrorFeatureStatusService
    {
        /// <summary>
        /// 오류 피처 상태 변경
        /// </summary>
        /// <param name="errorFeature">오류 피처</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>변경 성공 여부</returns>
        Task<bool> ChangeStatusAsync(ErrorFeature errorFeature, string newStatus);

        /// <summary>
        /// 오류 피처 목록 조회
        /// </summary>
        /// <returns>오류 피처 목록</returns>
        Task<List<ErrorFeature>> GetErrorFeaturesAsync();

        /// <summary>
        /// 일괄 상태 변경
        /// </summary>
        /// <param name="errorFeatures">오류 피처 목록</param>
        /// <param name="newStatus">새로운 상태</param>
        /// <returns>변경 결과</returns>
        Task<BatchStatusChangeResult> ChangeBatchStatusAsync(List<ErrorFeature> errorFeatures, ErrorStatus newStatus);

        /// <summary>
        /// 상태 변경 이벤트
        /// </summary>
        event EventHandler<StatusChangedEventArgs> StatusChanged;
    }
}