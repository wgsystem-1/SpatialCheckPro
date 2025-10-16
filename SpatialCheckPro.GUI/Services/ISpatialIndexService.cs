using System.Collections.Generic;
using System.Threading.Tasks;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 공간 인덱스 서비스 인터페이스
    /// </summary>
    public interface ISpatialIndexService
    {
        /// <summary>
        /// 공간 인덱스 생성
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <param name="errorFeatures">인덱싱할 ErrorFeature 목록</param>
        /// <returns>인덱스 생성 성공 여부</returns>
        Task<bool> CreateIndexAsync(string indexKey, List<ErrorFeature> errorFeatures);

        /// <summary>
        /// 공간 인덱스 업데이트
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <param name="errorFeatures">업데이트할 ErrorFeature 목록</param>
        /// <returns>인덱스 업데이트 성공 여부</returns>
        Task<bool> UpdateIndexAsync(string indexKey, List<ErrorFeature> errorFeatures);

        /// <summary>
        /// 바운딩 박스로 검색
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <returns>검색된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SearchByBoundsAsync(string indexKey, double minX, double minY, double maxX, double maxY);

        /// <summary>
        /// 점 주변 검색
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <param name="x">중심 X 좌표</param>
        /// <param name="y">중심 Y 좌표</param>
        /// <param name="radius">검색 반경</param>
        /// <returns>검색된 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SearchByRadiusAsync(string indexKey, double x, double y, double radius);

        /// <summary>
        /// 가장 가까운 ErrorFeature 검색
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <param name="x">기준 X 좌표</param>
        /// <param name="y">기준 Y 좌표</param>
        /// <param name="count">반환할 개수</param>
        /// <param name="maxDistance">최대 검색 거리 (선택사항)</param>
        /// <returns>가장 가까운 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SearchNearestAsync(string indexKey, double x, double y, int count, double? maxDistance = null);

        /// <summary>
        /// 공간 인덱스 제거
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <returns>인덱스 제거 성공 여부</returns>
        Task<bool> RemoveIndexAsync(string indexKey);

        /// <summary>
        /// 인덱스 존재 여부 확인
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <returns>인덱스 존재 여부</returns>
        bool IndexExists(string indexKey);

        /// <summary>
        /// 인덱스 통계 정보 조회
        /// </summary>
        /// <param name="indexKey">인덱스 키</param>
        /// <returns>인덱스된 ErrorFeature 개수</returns>
        Task<int> GetIndexedCountAsync(string indexKey);

        /// <summary>
        /// ErrorFeature 목록으로 공간 인덱스 구축
        /// </summary>
        /// <param name="errorFeatures">ErrorFeature 목록</param>
        /// <returns>인덱스 구축 성공 여부</returns>
        Task<bool> BuildSpatialIndexAsync(List<ErrorFeature> errorFeatures);

        /// <summary>
        /// 바운딩 박스 내 ErrorFeature 검색
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <returns>바운딩 박스 내 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SearchWithinBoundsAsync(double minX, double minY, double maxX, double maxY);

        /// <summary>
        /// 점 기준 반경 내 ErrorFeature 검색
        /// </summary>
        /// <param name="centerX">중심점 X 좌표</param>
        /// <param name="centerY">중심점 Y 좌표</param>
        /// <param name="radius">검색 반경 (미터)</param>
        /// <returns>반경 내 ErrorFeature 목록</returns>
        Task<List<ErrorFeature>> SearchWithinRadiusAsync(double centerX, double centerY, double radius);

        /// <summary>
        /// 공간 인덱스 초기화
        /// </summary>
        void ClearIndex();
    }
}