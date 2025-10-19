using OSGeo.OGR;
using SpatialCheckPro.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 검수 데이터 제공자 인터페이스
    /// 데이터 소스(GDB, SQLite 등)에 대한 추상화 제공
    /// </summary>
    public interface IValidationDataProvider
    {
        /// <summary>
        /// 데이터 소스를 초기화합니다.
        /// </summary>
        Task InitializeAsync(string dataSourcePath);

        /// <summary>
        /// 데이터 소스의 모든 레이어(테이블) 이름을 가져옵니다.
        /// </summary>
        Task<List<string>> GetLayerNamesAsync();

        /// <summary>
        /// 특정 레이어(테이블)의 모든 피처를 가져옵니다.
        /// </summary>
        Task<List<Feature>> GetFeaturesAsync(string layerName);

        /// <summary>
        /// 특정 레이어의 스키마 정보를 가져옵니다.
        /// </summary>
        Task<List<FieldDefn>> GetSchemaAsync(string layerName);

        /// <summary>
        /// 데이터 소스를 닫고 리소스를 정리합니다.
        /// </summary>
        void Close();
    }
}
