using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC_ERRORS FileGDB 데이터 로딩 및 관리를 위한 서비스 인터페이스
    /// </summary>
    public interface IQcErrorsDataService
    {
        /// <summary>
        /// QC_ERRORS FileGDB 연결 및 유효성 검사
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>연결 성공 여부</returns>
        Task<bool> ConnectToQcErrorsAsync(string qcErrorsGdbPath);

        /// <summary>
        /// QC_ERRORS 테이블에서 모든 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>QC 오류 목록</returns>
        Task<List<QcError>> LoadAllQcErrorsAsync(string qcErrorsGdbPath);

        /// <summary>
        /// 특정 검수 실행 ID의 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <returns>해당 실행의 QC 오류 목록</returns>
        Task<List<QcError>> LoadQcErrorsByRunIdAsync(string qcErrorsGdbPath, string runId);

        /// <summary>
        /// 심각도별 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="severities">심각도 목록</param>
        /// <returns>필터링된 QC 오류 목록</returns>
        Task<List<QcError>> LoadQcErrorsBySeverityAsync(string qcErrorsGdbPath, List<string> severities);

        /// <summary>
        /// 공간 범위 내의 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <returns>공간 범위 내 QC 오류 목록</returns>
        Task<List<QcError>> LoadQcErrorsInBoundsAsync(string qcErrorsGdbPath, double minX, double minY, double maxX, double maxY);

        /// <summary>
        /// QC_ERRORS 테이블의 통계 정보 조회
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>QC 오류 통계</returns>
        Task<QcErrorStatistics> GetQcErrorStatisticsAsync(string qcErrorsGdbPath);

        /// <summary>
        /// 검수 실행 목록 조회
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>검수 실행 목록</returns>
        Task<List<QcRun>> GetQcRunsAsync(string qcErrorsGdbPath);

        /// <summary>
        /// QC_ERRORS FileGDB 연결 해제
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 현재 연결 상태 확인
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 현재 연결된 QC_ERRORS FileGDB 경로
        /// </summary>
        string? CurrentQcErrorsPath { get; }
    }

    /// <summary>
    /// QC 오류 통계 정보
    /// </summary>
    public class QcErrorStatistics
    {
        /// <summary>
        /// 전체 오류 개수
        /// </summary>
        public int TotalErrorCount { get; set; }

        /// <summary>
        /// 심각도별 개수
        /// </summary>
        public Dictionary<string, int> SeverityCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 오류 타입별 개수
        /// </summary>
        public Dictionary<string, int> ErrorTypeCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 지오메트리 타입별 개수
        /// </summary>
        public Dictionary<string, int> GeometryTypeCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 원본 피처 클래스별 개수
        /// </summary>
        public Dictionary<string, int> SourceClassCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 검수 실행별 개수
        /// </summary>
        public Dictionary<string, int> RunIdCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 최근 생성된 오류 시간
        /// </summary>
        public DateTime? LastCreatedAt { get; set; }

        /// <summary>
        /// 가장 오래된 오류 시간
        /// </summary>
        public DateTime? FirstCreatedAt { get; set; }
    }
}
