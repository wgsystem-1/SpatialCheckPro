using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// QC_ERRORS FileGDB 데이터 로딩 및 관리 서비스 구현
    /// </summary>
    public class QcErrorsDataService : IQcErrorsDataService
    {
        private readonly ILogger<QcErrorsDataService> _logger;
        private readonly QcErrorService _qcErrorService;
        private string? _currentQcErrorsPath;
        private bool _isConnected = false;

        /// <summary>
        /// QcErrorsDataService 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="qcErrorService">QC 오류 서비스</param>
        public QcErrorsDataService(ILogger<QcErrorsDataService> logger, QcErrorService qcErrorService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qcErrorService = qcErrorService ?? throw new ArgumentNullException(nameof(qcErrorService));
        }

        /// <summary>
        /// 현재 연결 상태
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 현재 연결된 QC_ERRORS FileGDB 경로
        /// </summary>
        public string? CurrentQcErrorsPath => _currentQcErrorsPath;

        /// <summary>
        /// QC_ERRORS FileGDB 연결 및 유효성 검사
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>연결 성공 여부</returns>
        public async Task<bool> ConnectToQcErrorsAsync(string qcErrorsGdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS FileGDB 연결 시도: {Path}", qcErrorsGdbPath);

                // 파일 존재 여부 확인
                if (!System.IO.Directory.Exists(qcErrorsGdbPath))
                {
                    _logger.LogError("QC_ERRORS FileGDB가 존재하지 않습니다: {Path}", qcErrorsGdbPath);
                    return false;
                }

                // QC_ERRORS 스키마 유효성 검사
                var schemaValid = await _qcErrorService.ValidateQcErrorsSchemaAsync(qcErrorsGdbPath);
                if (!schemaValid)
                {
                    _logger.LogError("QC_ERRORS 스키마가 유효하지 않습니다: {Path}", qcErrorsGdbPath);
                    return false;
                }

                // 연결 성공
                _currentQcErrorsPath = qcErrorsGdbPath;
                _isConnected = true;

                _logger.LogInformation("QC_ERRORS FileGDB 연결 성공: {Path}", qcErrorsGdbPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS FileGDB 연결 실패: {Path}", qcErrorsGdbPath);
                _isConnected = false;
                _currentQcErrorsPath = null;
                return false;
            }
        }

        /// <summary>
        /// QC_ERRORS 테이블에서 모든 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>QC 오류 목록</returns>
        public async Task<List<QcError>> LoadAllQcErrorsAsync(string qcErrorsGdbPath)
        {
            try
            {
                _logger.LogInformation("모든 QC_ERRORS 데이터 로딩 시작: {Path}", qcErrorsGdbPath);

                var qcErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, null);
                
                _logger.LogInformation("QC_ERRORS 데이터 로딩 완료: {Count}개", qcErrors.Count);
                return qcErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 데이터 로딩 실패: {Path}", qcErrorsGdbPath);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// 특정 검수 실행 ID의 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="runId">검수 실행 ID</param>
        /// <returns>해당 실행의 QC 오류 목록</returns>
        public async Task<List<QcError>> LoadQcErrorsByRunIdAsync(string qcErrorsGdbPath, string runId)
        {
            try
            {
                _logger.LogInformation("RunId별 QC_ERRORS 데이터 로딩: {Path}, RunId: {RunId}", qcErrorsGdbPath, runId);

                var qcErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, runId);
                
                _logger.LogInformation("RunId별 QC_ERRORS 데이터 로딩 완료: {Count}개", qcErrors.Count);
                return qcErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunId별 QC_ERRORS 데이터 로딩 실패: {Path}, RunId: {RunId}", qcErrorsGdbPath, runId);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// 심각도별 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="severities">심각도 목록</param>
        /// <returns>필터링된 QC 오류 목록</returns>
        public async Task<List<QcError>> LoadQcErrorsBySeverityAsync(string qcErrorsGdbPath, List<string> severities)
        {
            try
            {
                _logger.LogInformation("심각도별 QC_ERRORS 데이터 로딩: {Path}, Severities: {Severities}", 
                    qcErrorsGdbPath, string.Join(", ", severities));

                var allErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, null);
                var filteredErrors = allErrors.ToList(); // Severity 필터 폐지
                
                _logger.LogInformation("심각도별 QC_ERRORS 데이터 로딩 완료: {Count}개", filteredErrors.Count);
                return filteredErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "심각도별 QC_ERRORS 데이터 로딩 실패: {Path}", qcErrorsGdbPath);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// 공간 범위 내의 오류 데이터 로드
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        /// <returns>공간 범위 내 QC 오류 목록</returns>
        public async Task<List<QcError>> LoadQcErrorsInBoundsAsync(string qcErrorsGdbPath, double minX, double minY, double maxX, double maxY)
        {
            try
            {
                _logger.LogInformation("공간 범위별 QC_ERRORS 데이터 로딩: {Path}, Bounds: ({MinX}, {MinY}) - ({MaxX}, {MaxY})", 
                    qcErrorsGdbPath, minX, minY, maxX, maxY);

                var allErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, null);
                var spatialErrors = allErrors.Where(e => 
                    e.X >= minX && e.X <= maxX && 
                    e.Y >= minY && e.Y <= maxY).ToList();
                
                _logger.LogInformation("공간 범위별 QC_ERRORS 데이터 로딩 완료: {Count}개", spatialErrors.Count);
                return spatialErrors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "공간 범위별 QC_ERRORS 데이터 로딩 실패: {Path}", qcErrorsGdbPath);
                return new List<QcError>();
            }
        }

        /// <summary>
        /// QC_ERRORS 테이블의 통계 정보 조회
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>QC 오류 통계</returns>
        public async Task<QcErrorStatistics> GetQcErrorStatisticsAsync(string qcErrorsGdbPath)
        {
            try
            {
                _logger.LogInformation("QC_ERRORS 통계 정보 조회: {Path}", qcErrorsGdbPath);

                var allErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, null);
                
                var statistics = new QcErrorStatistics
                {
                    TotalErrorCount = allErrors.Count
                };

                if (allErrors.Any())
                {
                    // 심각도별 통계는 비활성화(오류 유형 중심)
                    statistics.SeverityCounts = new Dictionary<string, int>();

                    // 오류 타입별 개수
                    statistics.ErrorTypeCounts = allErrors
                        .GroupBy(e => e.ErrType)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // 지오메트리 타입별 개수
                    statistics.GeometryTypeCounts = allErrors
                        .GroupBy(e => e.GeometryType)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // 원본 피처 클래스별 개수
                    statistics.SourceClassCounts = allErrors
                        .GroupBy(e => e.SourceClass)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // 검수 실행별 개수
                    statistics.RunIdCounts = allErrors
                        .GroupBy(e => e.RunId)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // 날짜 범위
                    statistics.FirstCreatedAt = allErrors.Min(e => e.CreatedUTC);
                    statistics.LastCreatedAt = allErrors.Max(e => e.CreatedUTC);
                }

                _logger.LogInformation("QC_ERRORS 통계 정보 조회 완료: 총 {Count}개 오류", statistics.TotalErrorCount);
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QC_ERRORS 통계 정보 조회 실패: {Path}", qcErrorsGdbPath);
                return new QcErrorStatistics();
            }
        }

        /// <summary>
        /// 검수 실행 목록 조회
        /// </summary>
        /// <param name="qcErrorsGdbPath">QC_ERRORS FileGDB 경로</param>
        /// <returns>검수 실행 목록</returns>
        public async Task<List<QcRun>> GetQcRunsAsync(string qcErrorsGdbPath)
        {
            try
            {
                _logger.LogInformation("검수 실행 목록 조회: {Path}", qcErrorsGdbPath);

                var allErrors = await _qcErrorService.GetQcErrorsAsync(qcErrorsGdbPath, null);
                
                var qcRuns = allErrors
                    .GroupBy(e => e.RunId)
                    .Select(g => new QcRun
                    {
                        RunId = g.Key,
                        StartTime = g.Min(e => e.CreatedUTC),
                        EndTime = g.Max(e => e.CreatedUTC),
                        ErrorCount = g.Count(),
                        Status = "Completed"
                    })
                    .OrderByDescending(r => r.StartTime)
                    .ToList();

                _logger.LogInformation("검수 실행 목록 조회 완료: {Count}개 실행", qcRuns.Count);
                return qcRuns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 실행 목록 조회 실패: {Path}", qcErrorsGdbPath);
                return new List<QcRun>();
            }
        }

        /// <summary>
        /// QC_ERRORS FileGDB 연결 해제
        /// </summary>
        public void Disconnect()
        {
            _isConnected = false;
            _currentQcErrorsPath = null;
            _logger.LogInformation("QC_ERRORS FileGDB 연결 해제 완료");
        }
    }
}
