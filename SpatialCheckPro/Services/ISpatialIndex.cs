using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 인덱스 인터페이스
    /// </summary>
    public interface ISpatialIndex
    {
        /// <summary>
        /// 지정된 레이어로부터 공간 인덱스를 구축합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <returns>구축 완료 여부</returns>
        Task<bool> BuildIndexAsync(string gdbPath, string layerName);

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색합니다
        /// </summary>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <returns>교차하는 피처 ID 목록</returns>
        Task<List<long>> QueryIntersectingFeaturesAsync(SpatialEnvelope searchEnvelope);

        /// <summary>
        /// 인덱스에 포함된 피처 수를 반환합니다
        /// </summary>
        /// <returns>피처 수</returns>
        int GetFeatureCount();

        /// <summary>
        /// 인덱스를 초기화합니다
        /// </summary>
        void Clear();
    }
}
