using OSGeo.OGR;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// DataSource 풀링 서비스 인터페이스
    /// 동일한 GDB 파일에 대한 중복 오픈을 방지하여 I/O 성능을 최적화합니다.
    /// </summary>
    public interface IDataSourcePool : IDisposable
    {
        /// <summary>
        /// DataSource를 가져옵니다. 캐시된 것이 있으면 재사용하고, 없으면 새로 생성합니다.
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>DataSource 객체 (사용 후 ReturnDataSource 호출 필요)</returns>
        DataSource? GetDataSource(string gdbPath);
        
        /// <summary>
        /// DataSource 사용을 완료했음을 알립니다.
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="dataSource">반환할 DataSource</param>
        void ReturnDataSource(string gdbPath, DataSource dataSource);
        
        /// <summary>
        /// 특정 경로의 DataSource를 풀에서 제거합니다.
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        void RemoveDataSource(string gdbPath);
        
        /// <summary>
        /// 모든 DataSource를 정리합니다.
        /// </summary>
        void ClearPool();
        
        /// <summary>
        /// 현재 풀에 있는 DataSource 수를 반환합니다.
        /// </summary>
        int PoolSize { get; }
    }
}
