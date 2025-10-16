using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간 인덱스 관리자 인터페이스
    /// </summary>
    public interface ISpatialIndexManager
    {
        /// <summary>
        /// 공간 인덱스를 생성합니다
        /// </summary>
        /// <param name="gdbPath">File Geodatabase 경로</param>
        /// <param name="layerName">레이어명</param>
        /// <param name="indexType">인덱스 타입</param>
        /// <returns>생성된 공간 인덱스</returns>
        Task<ISpatialIndex> CreateSpatialIndexAsync(
            string gdbPath,
            string layerName,
            SpatialIndexType indexType = SpatialIndexType.RTree);

        /// <summary>
        /// 지정된 범위와 교차하는 피처들을 검색합니다
        /// </summary>
        /// <param name="index">공간 인덱스</param>
        /// <param name="searchEnvelope">검색 범위</param>
        /// <returns>교차하는 피처 ID 목록</returns>
        Task<List<long>> QueryIntersectingFeaturesAsync(
            ISpatialIndex index,
            SpatialEnvelope searchEnvelope);

        /// <summary>
        /// 두 인덱스 간의 공간 관계를 질의합니다
        /// </summary>
        /// <param name="sourceIndex">원본 인덱스</param>
        /// <param name="targetIndex">대상 인덱스</param>
        /// <param name="relationType">공간 관계 타입</param>
        /// <returns>공간 질의 결과 목록</returns>
        Task<List<SpatialQueryResult>> QuerySpatialRelationAsync(
            ISpatialIndex sourceIndex,
            ISpatialIndex targetIndex,
            SpatialRelationType relationType);

        /// <summary>
        /// 인덱스 캐시를 정리합니다
        /// </summary>
        void ClearCache();
    }
}
