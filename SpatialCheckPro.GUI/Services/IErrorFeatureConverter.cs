using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// QcError와 ErrorFeature 간 변환 서비스 인터페이스
    /// </summary>
    public interface IErrorFeatureConverter
    {
        /// <summary>
        /// QcError 목록을 ErrorFeature 목록으로 변환
        /// </summary>
        /// <param name="qcErrors">변환할 QcError 목록</param>
        /// <returns>변환된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> ConvertToErrorFeaturesAsync(List<QcError> qcErrors);

        /// <summary>
        /// 단일 QcError를 ErrorFeature로 변환
        /// </summary>
        /// <param name="qcError">변환할 QcError</param>
        /// <returns>변환된 ErrorFeature</returns>
        Task<ErrorFeature> ConvertToErrorFeatureAsync(QcError qcError);

        /// <summary>
        /// ErrorFeature를 QcError로 변환
        /// </summary>
        /// <param name="errorFeature">변환할 ErrorFeature</param>
        /// <returns>변환된 QcError</returns>
        Task<QcError> ConvertToQcErrorAsync(ErrorFeature errorFeature);

        /// <summary>
        /// ErrorFeature 목록을 QcError 목록으로 변환
        /// </summary>
        /// <param name="errorFeatures">변환할 ErrorFeature 목록</param>
        /// <returns>변환된 QcError 목록</returns>
        Task<List<QcError>> ConvertToQcErrorsAsync(List<ErrorFeature> errorFeatures);

        /// <summary>
        /// QcError의 변경사항을 ErrorFeature에 적용
        /// </summary>
        /// <param name="errorFeature">업데이트할 ErrorFeature</param>
        /// <param name="qcError">변경된 QcError</param>
        /// <returns>업데이트 성공 여부</returns>
        Task<bool> UpdateErrorFeatureFromQcErrorAsync(ErrorFeature errorFeature, QcError qcError);

        /// <summary>
        /// ErrorFeature의 변경사항을 QcError에 적용
        /// </summary>
        /// <param name="qcError">업데이트할 QcError</param>
        /// <param name="errorFeature">변경된 ErrorFeature</param>
        /// <returns>업데이트 성공 여부</returns>
        Task<bool> UpdateQcErrorFromErrorFeatureAsync(QcError qcError, ErrorFeature errorFeature);
    }
}