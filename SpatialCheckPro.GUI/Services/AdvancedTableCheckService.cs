using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 고도화된 테이블 검수 서비스 - 정확한 매칭 및 추가 피처클래스 검출
    /// </summary>
    public class AdvancedTableCheckService
    {
        private readonly ILogger<AdvancedTableCheckService> _logger;
        private readonly GdalDataAnalysisService _gdalService;
        private readonly ParallelProcessingManager? _parallelProcessingManager;
        private readonly SpatialCheckPro.Models.Config.PerformanceSettings _performanceSettings;

        public AdvancedTableCheckService(ILogger<AdvancedTableCheckService> logger, GdalDataAnalysisService gdalService, 
            ParallelProcessingManager? parallelProcessingManager = null, 
            SpatialCheckPro.Models.Config.PerformanceSettings? performanceSettings = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gdalService = gdalService ?? throw new ArgumentNullException(nameof(gdalService));
            _parallelProcessingManager = parallelProcessingManager;
            _performanceSettings = performanceSettings ?? new SpatialCheckPro.Models.Config.PerformanceSettings();
        }

        /// <summary>
        /// 고도화된 테이블 검수 수행
        /// </summary>
        public async Task<AdvancedTableCheckResult> PerformAdvancedTableCheckAsync(string dataSourcePath, IValidationDataProvider dataProvider, List<TableCheckConfig> configList, IProgress<(double percentage, string message)>? progress = null)
        {
            _logger.LogInformation("고도화된 테이블 검수 시작: {DataSourcePath}, 설정 테이블 수: {ConfigCount}", 
                dataSourcePath, configList.Count);

            var result = new AdvancedTableCheckResult
            {
                TotalTables = configList.Count,
                PassedTables = 0,
                FailedTables = 0,
                WarningCount = 0,
                ErrorCount = 0,
                TableItems = new List<TableCheckItem>(),
                AdditionalFeatureClasses = new List<AdditionalFeatureClass>()
            };

            try
            {
                // 1. 데이터 소스에서 모든 피처클래스 정보 가져오기
                var allFeatureClasses = await GetAllFeatureClassesAsync(dataSourcePath, dataProvider);
                _logger.LogInformation("📋 데이터 소스에서 발견된 피처클래스: {Count}개", allFeatureClasses.Count);
                
                foreach (var fc in allFeatureClasses)
                {
                    _logger.LogDebug("  - {Name}: {GeometryType} ({FeatureCount}개 피처)", 
                        fc.Name, fc.GeometryType, fc.FeatureCount);
                }

                // 2. 설정된 각 테이블에 대해 병렬 검수 수행
                List<TableCheckItem> tableItems;
                
                if (_parallelProcessingManager != null && _performanceSettings.EnableTableParallelProcessing)
                {
                    _logger.LogInformation("테이블 검수 병렬 처리 모드로 실행: {TableCount}개 테이블", configList.Count);
                    
                    // 테이블별 병렬 처리
                    var configItems = configList.Select(config => (object)config).ToList();
                    
                    var tableResults = await _parallelProcessingManager.ExecuteTableParallelProcessingAsync(
                        configItems,
                        async (item) =>
                        {
                            var config = (TableCheckConfig)item;
                            // CheckTableAsync는 더 이상 gdbPath를 직접 사용하지 않음
                            return await CheckTableAsync(config, allFeatureClasses);
                        },
                        null, // progress는 병렬 처리에서 직접 처리하지 않음
                        "테이블 검수"
                    );
                    
                    tableItems = tableResults.Where(r => r != null).Cast<TableCheckItem>().ToList();
                }
                else
                {
                    _logger.LogInformation("테이블 검수 순차 처리 모드로 실행: {TableCount}개 테이블", configList.Count);
                    
                    // 순차 처리
                    tableItems = new List<TableCheckItem>();
                    int processedCount = 0;
                    
                    foreach (var config in configList)
                    {
                        var tableItem = await CheckTableAsync(config, allFeatureClasses);
                        tableItems.Add(tableItem);
                        
                        // 진행률 보고
                        processedCount++;
                        if (progress != null)
                        {
                            var percentage = (processedCount * 100.0) / configList.Count;
                            var message = $"테이블 검수 중... ({processedCount}/{configList.Count}) {config.TableName}";
                            progress.Report((percentage, message));
                        }
                    }
                }
                
                // 결과 처리
                foreach (var tableItem in tableItems)
                {
                    result.TableItems.Add(tableItem);

                    if (tableItem.TableExistsCheck == "Y" && tableItem.FeatureTypeCheck == "Y")
                    {
                        result.PassedTables++;
                    }
                    else
                    {
                        result.FailedTables++;
                        if (tableItem.TableExistsCheck == "N")
                            result.ErrorCount++;
                        else
                            result.WarningCount++;
                    }
                }

                // 3. 설정파일에 없는 추가 피처클래스 검출 및 오류로 처리
                var additionalFeatureClasses = FindAdditionalFeatureClasses(configList, allFeatureClasses);
                result.AdditionalFeatureClasses = additionalFeatureClasses;
                
                if (additionalFeatureClasses.Any())
                {
                    // 추가 피처클래스를 오류로 처리
                    result.ErrorCount += additionalFeatureClasses.Count;
                    _logger.LogError("❌ 설정파일에 정의되지 않은 피처클래스 {Count}개 발견 (오류로 처리):", additionalFeatureClasses.Count);
                    
                    // 추가 피처클래스를 TableCheckItem으로 변환하여 결과에 포함
                    foreach (var additional in additionalFeatureClasses)
                    {
                        var additionalTableItem = new TableCheckItem
                        {
                            TableId = additional.Name,
                            TableName = additional.Name,
                            ExpectedFeatureType = "정의되지 않음",
                            ExpectedCoordinateSystem = "정의되지 않음",
                            TableExistsCheck = "Y", // 실제로는 존재함
                            FeatureTypeCheck = "N", // 정의되지 않았으므로 실패
                            FeatureCount = additional.FeatureCount,
                            ActualFeatureType = additional.GeometryType,
                            ActualFeatureClassName = additional.Name
                        };
                        
                        result.TableItems.Add(additionalTableItem);
                        result.FailedTables++; // 실패한 테이블로 카운트
                        
                        _logger.LogError("  - {Name}: {GeometryType} ({FeatureCount}개 피처) - 정의되지 않은 테이블", 
                            additional.Name, additional.GeometryType, additional.FeatureCount);
                    }
                }

                _logger.LogInformation("✅ 고도화된 테이블 검수 완료: 통과 {Passed}개, 실패 {Failed}개, 추가 {Additional}개", 
                    result.PassedTables, result.FailedTables, result.AdditionalFeatureClasses.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "고도화된 테이블 검수 중 오류 발생");
                result.ErrorCount++;
                return result;
            }
        }

        /// <summary>
        /// 데이터 소스에서 모든 피처클래스 목록 가져오기
        /// </summary>
        private async Task<List<FeatureClassInfo>> GetAllFeatureClassesAsync(string dataSourcePath, IValidationDataProvider dataProvider)
        {
            var featureClasses = new List<FeatureClassInfo>();
            try
            {
                var layerNames = await dataProvider.GetLayerNamesAsync();

                foreach (var layerName in layerNames)
                {
                    // ORG_ 백업 레이어만 제외
                    if (layerName.StartsWith("ORG_", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("검수 제외 레이어: {LayerName}", layerName);
                        continue;
                    }
                    
                    // 스키마와 피처 정보를 가져와야 하지만, IValidationDataProvider에 아직 해당 기능이 완벽하지 않으므로
                    // 우선 이름만으로 FeatureClassInfo를 생성합니다. 상세 정보는 추후 추가 필요.
                    // 빠른 경로: 레이어 정의 기반 메타(타입/카운트) 우선 조회
                    var (gType, fCount) = await _gdalService.GetLayerInfoAsync(dataSourcePath, layerName);
                    var schema = await dataProvider.GetSchemaAsync(layerName);

                    var featureClass = new FeatureClassInfo
                    {
                        Name = layerName,
                        Exists = true,
                        GeometryType = ConvertGeometryType(gType),
                        FeatureCount = fCount,
                        FieldCount = schema.Count
                    };

                    featureClasses.Add(featureClass);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "피처클래스 목록 조회 중 오류 발생");
            }

            return featureClasses;
        }


        /// <summary>
        /// 개별 테이블 검수 수행
        /// </summary>
        private Task<TableCheckItem> CheckTableAsync(TableCheckConfig config, List<FeatureClassInfo> allFeatureClasses)
        {
            return Task.Run(() =>
            {
                var tableItem = new TableCheckItem
            {
                TableId = config.TableId,
                TableName = config.TableName,
                ExpectedFeatureType = config.GeometryType,
                ExpectedCoordinateSystem = config.CoordinateSystem,
                TableExistsCheck = "N",
                FeatureTypeCheck = "N",
                FeatureCount = 0,
                ActualFeatureType = "",
                ActualFeatureClassName = ""
            };

            try
            {
                // 1. 테이블 존재 여부 확인 (대소문자 무시)
                var matchingFeatureClass = allFeatureClasses.FirstOrDefault(fc => 
                    string.Equals(fc.Name, config.TableId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeTableId(config.TableId), NormalizeTableId(fc.Name), StringComparison.OrdinalIgnoreCase));

                if (matchingFeatureClass == null)
                {
                    _logger.LogWarning("❌ 테이블 없음: {TableId}", config.TableId);
                    tableItem.TableExistsCheck = "N";
                    return tableItem;
                }

                // 2. 테이블 존재 확인됨
                tableItem.TableExistsCheck = "Y";
                tableItem.FeatureCount = (int)matchingFeatureClass.FeatureCount;
                tableItem.ActualFeatureType = matchingFeatureClass.GeometryType;
                tableItem.ActualFeatureClassName = matchingFeatureClass.Name;

                _logger.LogInformation("✅ 테이블 발견: {TableId} -> {ActualName} ({FeatureCount}개 피처)", 
                    config.TableId, matchingFeatureClass.Name, matchingFeatureClass.FeatureCount);

                // 3. 지오메트리 타입 검증
                var expectedType = (config.GeometryType?.Trim() ?? "").ToUpperInvariant();
                var actualType = matchingFeatureClass.GeometryType?.Trim() ?? "";

                _logger.LogInformation("🔍 지오메트리 타입 비교: 예상='{Expected}', 실제='{Actual}'", 
                    expectedType, actualType);

                // 멀티/싱글 호환 허용: MULTIPOLYGON==POLYGON, MULTILINESTRING==LINESTRING, MULTIPOINT==POINT
                bool featureTypeMatches = string.Equals(expectedType, actualType, StringComparison.OrdinalIgnoreCase)
                    || (expectedType == "MULTIPOLYGON" && actualType.Equals("POLYGON", StringComparison.OrdinalIgnoreCase))
                    || (expectedType == "POLYGON" && actualType.Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase))
                    || (expectedType == "MULTILINESTRING" && actualType.Equals("LINESTRING", StringComparison.OrdinalIgnoreCase))
                    || (expectedType == "LINESTRING" && actualType.Equals("MULTILINESTRING", StringComparison.OrdinalIgnoreCase))
                    || (expectedType == "MULTIPOINT" && actualType.Equals("POINT", StringComparison.OrdinalIgnoreCase))
                    || (expectedType == "POINT" && actualType.Equals("MULTIPOINT", StringComparison.OrdinalIgnoreCase));
                tableItem.FeatureTypeCheck = featureTypeMatches ? "Y" : "N";

                if (featureTypeMatches)
                {
                    _logger.LogInformation("✅ 지오메트리 타입 일치: {TableId}", config.TableId);
                }
                else
                {
                    _logger.LogWarning("❌ 지오메트리 타입 불일치: {TableId} - 예상: {Expected}, 실제: {Actual}", 
                        config.TableId, expectedType, actualType);
                }

                return tableItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테이블 검수 중 오류: {TableId}", config.TableId);
                return tableItem;
            }
            });
        }

        /// <summary>
        /// 설정파일에 없는 추가 피처클래스 검출
        /// </summary>
        private List<AdditionalFeatureClass> FindAdditionalFeatureClasses(List<TableCheckConfig> configList, List<FeatureClassInfo> allFeatureClasses)
        {
            var additionalFeatureClasses = new List<AdditionalFeatureClass>();
            var configTableIds = configList.Select(c => c.TableId.ToUpper()).ToHashSet();

            foreach (var featureClass in allFeatureClasses)
            {
                // ORG_ 백업 레이어는 추가 검출 대상에서 제외
                if (featureClass.Name.StartsWith("ORG_", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("추가 피처클래스 검사에서 ORG_ 레이어 제외: {Name}", featureClass.Name);
                    continue;
                }
                if (!configTableIds.Contains(featureClass.Name.ToUpper()))
                {
                    additionalFeatureClasses.Add(new AdditionalFeatureClass
                    {
                        Name = featureClass.Name,
                        GeometryType = featureClass.GeometryType,
                        FeatureCount = (int)featureClass.FeatureCount,
                        Reason = "설정파일에 정의되지 않은 피처클래스"
                    });
                }
            }

            return additionalFeatureClasses;
        }

        /// <summary>
        /// OGR 지오메트리 타입을 문자열로 변환
        /// </summary>
        private string ConvertGeometryType(wkbGeometryType geomType)
        {
            return geomType switch
            {
                wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => "POINT",
                wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => "LINESTRING",
                wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => "POLYGON",
                wkbGeometryType.wkbUnknown => "UNKNOWN",
                _ => $"OTHER_{geomType}"
            };
        }

        private static string NormalizeTableId(string name)
        {
            // 예: tn_rodway_ctln ↔ TN_RODWAY_CTLN, 접두사/대소문자 차이 보정
            return (name ?? string.Empty)
                .Trim()
                .Replace("ORG_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();
        }
    }

    /// <summary>
    /// 고도화된 테이블 검수 결과 모델
    /// </summary>
    public class AdvancedTableCheckResult
    {
        public int TotalTables { get; set; }
        public int PassedTables { get; set; }
        public int FailedTables { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public List<TableCheckItem> TableItems { get; set; } = new List<TableCheckItem>();
        public List<AdditionalFeatureClass> AdditionalFeatureClasses { get; set; } = new List<AdditionalFeatureClass>();
    }

    /// <summary>
    /// 테이블 검수 항목 모델
    /// </summary>
    public class TableCheckItem
    {
        public string TableId { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ExpectedFeatureType { get; set; } = "";
        public string ExpectedCoordinateSystem { get; set; } = "";
        public string TableExistsCheck { get; set; } = "N";
        public string FeatureTypeCheck { get; set; } = "N";
        public int FeatureCount { get; set; }
        public string ActualFeatureType { get; set; } = "";
        public string ActualFeatureClassName { get; set; } = "";
    }

    /// <summary>
    /// 추가 피처클래스 모델
    /// </summary>
    public class AdditionalFeatureClass
    {
        public string Name { get; set; } = "";
        public string GeometryType { get; set; } = "";
        public int FeatureCount { get; set; }
        public string Reason { get; set; } = "";
    }
}
