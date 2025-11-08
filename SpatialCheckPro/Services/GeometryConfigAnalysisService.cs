using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 지오메트리 검수 설정 파일 분석 서비스
    /// </summary>
    public class GeometryConfigAnalysisService
    {
        private readonly ILogger<GeometryConfigAnalysisService> _logger;

        public GeometryConfigAnalysisService(ILogger<GeometryConfigAnalysisService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 지오메트리 검수 설정 파일에서 실제 검수 항목 수를 계산합니다
        /// </summary>
        /// <param name="configPath">설정 파일 경로</param>
        /// <returns>검수 항목 분석 결과</returns>
        public GeometryConfigAnalysisResult AnalyzeGeometryConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    _logger.LogWarning("지오메트리 검수 설정 파일을 찾을 수 없습니다: {ConfigPath}", configPath);
                    return new GeometryConfigAnalysisResult
                    {
                        TotalTables = 0,
                        TotalRules = 0,
                        RulesPerTable = new Dictionary<string, int>()
                    };
                }

                var lines = File.ReadAllLines(configPath, System.Text.Encoding.UTF8);
                if (lines.Length < 2)
                {
                    _logger.LogWarning("지오메트리 검수 설정 파일이 비어있습니다: {ConfigPath}", configPath);
                    return new GeometryConfigAnalysisResult
                    {
                        TotalTables = 0,
                        TotalRules = 0,
                        RulesPerTable = new Dictionary<string, int>()
                    };
                }

                // 헤더 분석
                var header = lines[0].Split(',');
                var baseCols = new[] { "TableId", "TableName", "GeometryType" };
                var ruleColumns = header.Where(h => !baseCols.Contains(h, StringComparer.OrdinalIgnoreCase)).ToArray(); // 기본 컬럼 제외
                
                _logger.LogDebug("지오메트리 검수 규칙 컬럼: {RuleColumns}", string.Join(", ", ruleColumns));

                var totalRules = 0;
                var totalTables = 0;
                var rulesPerTable = new Dictionary<string, int>();

                // 각 테이블별 규칙 분석
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var fields = line.Split(',');
                    if (fields.Length < 4) continue; // 최소 필드 수 확인

                    var tableId = fields[0].Trim();
                    var tableName = fields[1].Trim();
                    
                    if (string.IsNullOrEmpty(tableId) || !tableId.StartsWith("tn_")) continue;

                    totalTables++;
                    
                    // 해당 테이블의 활성화된 규칙 수 계산
                    var activeRules = 0;
                    for (int j = 3; j < fields.Length && j - 3 < ruleColumns.Length; j++)
                    {
                        var ruleValue = fields[j].Trim().ToUpper();
                        if (ruleValue == "Y")
                        {
                            activeRules++;
                        }
                    }

                    totalRules += activeRules;
                    rulesPerTable[tableId] = activeRules;

                    _logger.LogDebug("테이블 {TableId} ({TableName}): {ActiveRules}개 규칙 활성화", 
                        tableId, tableName, activeRules);
                }

                var result = new GeometryConfigAnalysisResult
                {
                    TotalTables = totalTables,
                    TotalRules = totalRules,
                    RulesPerTable = rulesPerTable,
                    RuleColumns = ruleColumns
                };

                _logger.LogInformation("지오메트리 검수 설정 분석 완료: {TotalTables}개 테이블, {TotalRules}개 규칙", 
                    totalTables, totalRules);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 설정 파일 분석 중 오류 발생: {ConfigPath}", configPath);
                return new GeometryConfigAnalysisResult
                {
                    TotalTables = 0,
                    TotalRules = 0,
                    RulesPerTable = new Dictionary<string, int>()
                };
            }
        }

        /// <summary>
        /// 특정 테이블의 활성화된 규칙 수를 조회합니다
        /// </summary>
        /// <param name="configPath">설정 파일 경로</param>
        /// <param name="tableId">테이블 ID</param>
        /// <returns>활성화된 규칙 수</returns>
        public int GetActiveRulesForTable(string configPath, string tableId)
        {
            try
            {
                var result = AnalyzeGeometryConfig(configPath);
                return result.RulesPerTable.TryGetValue(tableId, out var rules) ? rules : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블별 규칙 수 조회 중 오류 발생: {TableId}", tableId);
                return 0;
            }
        }
    }

    /// <summary>
    /// 지오메트리 검수 설정 분석 결과
    /// </summary>
    public class GeometryConfigAnalysisResult
    {
        /// <summary>
        /// 총 테이블 수
        /// </summary>
        public int TotalTables { get; set; }

        /// <summary>
        /// 총 규칙 수 (모든 테이블의 활성화된 규칙 합계)
        /// </summary>
        public int TotalRules { get; set; }

        /// <summary>
        /// 테이블별 활성화된 규칙 수
        /// </summary>
        public Dictionary<string, int> RulesPerTable { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 규칙 컬럼명 목록
        /// </summary>
        public string[] RuleColumns { get; set; } = Array.Empty<string>();
    }
}
