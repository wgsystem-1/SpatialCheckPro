namespace SpatialCheckPro.Services
{
    /// <summary>
    /// GDAL 기반 데이터 읽기 인터페이스
    /// </summary>
    public interface IGdalDataReader
    {
        /// <summary>
        /// 테이블 존재 여부를 확인합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <returns>테이블 존재 여부</returns>
        Task<bool> IsTableExistsAsync(string gdbPath, string tableName);

        /// <summary>
        /// 필드 존재 여부를 확인합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="fieldName">필드명</param>
        /// <returns>필드 존재 여부</returns>
        Task<bool> IsFieldExistsAsync(string gdbPath, string tableName, string fieldName);

        /// <summary>
        /// 필드의 모든 값을 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="fieldName">필드명</param>
        /// <param name="batchSize">배치 크기</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>필드값 목록</returns>
        Task<List<string>> GetAllFieldValuesAsync(
            string gdbPath, 
            string tableName, 
            string fieldName,
            int batchSize = 10000,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 테이블의 레코드 수를 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <returns>레코드 수</returns>
        Task<long> GetRecordCountAsync(string gdbPath, string tableName);

        /// <summary>
        /// 필드값을 스트리밍 방식으로 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="fieldName">필드명</param>
        /// <param name="batchSize">배치 크기</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>필드값 스트림</returns>
        IAsyncEnumerable<string> GetFieldValuesStreamAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            int batchSize = 10000,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 특정 필드값을 가진 레코드의 ObjectId 목록을 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="fieldName">필드명</param>
        /// <param name="fieldValue">필드값</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>ObjectId 목록</returns>
        Task<List<long>> GetObjectIdsForFieldValueAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            string fieldValue,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 필드값별 개수를 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="fieldName">필드명</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>필드값별 개수 딕셔너리</returns>
        Task<Dictionary<string, int>> GetFieldValueCountsAsync(
            string gdbPath,
            string tableName,
            string fieldName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 피처를 스트리밍 방식으로 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>피처 스트림</returns>
        IAsyncEnumerable<OSGeo.OGR.Feature> GetFeaturesStreamAsync(
            string gdbPath,
            string tableName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 테이블 스키마 정보를 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <returns>필드명과 타입의 딕셔너리</returns>
        Task<Dictionary<string, Type>> GetTableSchemaAsync(string gdbPath, string tableName);

        /// <summary>
        /// 특정 ObjectId의 피처를 조회합니다
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <param name="tableName">테이블명</param>
        /// <param name="objectId">ObjectId</param>
        /// <returns>피처 객체</returns>
        Task<OSGeo.OGR.Feature?> GetFeatureByIdAsync(string gdbPath, string tableName, long objectId);
    }
}
