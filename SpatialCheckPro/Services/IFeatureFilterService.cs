using OSGeo.OGR;
using System.Collections.Generic;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 피처 필터링 서비스 인터페이스 (공통 예외 코드를 기준으로 검수 대상 제외)
    /// </summary>
    public interface IFeatureFilterService
    {
        /// <summary>
        /// 제외 대상 객체변동 코드 목록
        /// </summary>
        IReadOnlyCollection<string> ExcludedObjectChangeCodes { get; }

        /// <summary>
        /// 레이어에 OBJFLTN_SE 제외 필터를 적용합니다.
        /// </summary>
        /// <param name="layer">필터를 적용할 레이어</param>
        /// <param name="stageName">검수 단계명 (로그용)</param>
        /// <param name="tableName">레이어/테이블명</param>
        /// <returns>필터 적용 결과</returns>
        FeatureFilterApplyResult ApplyObjectChangeFilter(Layer layer, string stageName, string tableName);

        /// <summary>
        /// 단일 피처를 제외해야 하는지 여부를 반환합니다.
        /// </summary>
        /// <param name="feature">검사 대상 피처</param>
        /// <param name="layerName">레이어명</param>
        /// <param name="excludedCode">제외 사유 코드</param>
        /// <returns>제외 대상 여부</returns>
        bool ShouldSkipFeature(Feature feature, string? layerName, out string? excludedCode);
    }
}
