using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 실제 스키마 추출 기반 스키마 검수 서비스
    /// </summary>
    public class SchemaValidationService
    {
        private readonly ILogger<SchemaValidationService> _logger;
        private readonly GdalDataAnalysisService _gdalService;
        private readonly IUniqueKeyValidator _uniqueKeyValidator;
        private readonly IForeignKeyValidator _foreignKeyValidator;
        private readonly ValidationHistoryService _historyService;

        /// <summary>
        /// 스키마 검수 진행률 업데이트 이벤트
        /// </summary>
        public event EventHandler<SchemaValidationProgressEventArgs>? ProgressUpdated;

        public SchemaValidationService(
            ILogger<SchemaValidationService> logger,
            GdalDataAnalysisService gdalService,
            IUniqueKeyValidator uniqueKeyValidator,
            IForeignKeyValidator foreignKeyValidator,
            ValidationHistoryService historyService)
        {
            _logger = logger;
            _gdalService = gdalService;
            _uniqueKeyValidator = uniqueKeyValidator;
            _foreignKeyValidator = foreignKeyValidator;
            _historyService = historyService;
            
            // UK/FK 검수 진행률 이벤트 구독
            _uniqueKeyValidator.ProgressUpdated += OnUniqueKeyProgressUpdated;
        }

        /// <summary>
        /// 실제 스키마 추출 기반 스키마 검수를 수행합니다
        /// </summary>
        public async Task<SchemaCheckResult> ValidateSchemaAsync(
            string gdbPath, 
            string schemaConfigPath, 
            List<TableValidationItem> validTables)
        {
            var result = new SchemaCheckResult
            {
                StartedAt = DateTime.Now,
                IsValid = true,
                ErrorCount = 0,
                WarningCount = 0,
                Message = "스키마 검수 완료",
                SchemaResults = new List<SchemaValidationItem>()
            };

            try
            {
                _logger.LogInformation("실제 스키마 추출 기반 스키마 검수 시작");
                ReportProgress("초기화", 0, "스키마 검수를 시작합니다");

                // GDAL 사용 가능 여부 확인 (없어도 기본 검수 수행)
                ReportProgress("GDAL 확인", 5, "GDAL 라이브러리 사용 가능 여부 확인 중");
                bool gdalAvailable = await _gdalService.IsGdalAvailableAsync();
                if (!gdalAvailable)
                {
                    _logger.LogWarning("GDAL 라이브러리를 사용할 수 없어 기본 스키마 검수를 수행합니다.");
                    ReportProgress("기본 검수", 5, "GDAL 없이 기본 스키마 검수를 수행합니다");
                }

                // 스키마 설정 파일 로드
                ReportProgress("설정 로드", 10, "스키마 설정 파일을 로드하는 중");
                var schemaConfigs = await LoadSchemaConfigAsync(schemaConfigPath);
                if (!schemaConfigs.Any())
                {
                    result.WarningCount++;
                    result.Message = "스키마 설정이 없어 검수를 스킵했습니다.";
                    _logger.LogWarning("스키마 설정이 없습니다.");
                    ReportProgress("완료", 100, "스키마 설정이 없어 검수를 스킵했습니다");
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                // 1단계 통과 테이블만 대상으로 스키마 검수 수행
                ReportProgress("테이블 필터링", 15, "검수 대상 테이블을 필터링하는 중");
                _logger.LogInformation("전달받은 1단계 통과 테이블: {TotalCount}개", validTables.Count);
                
                // 테이블 존재 여부 기준으로 대상 선정 (피처 개수 0이어도 스키마는 검증해야 함)
                var validTableIds = validTables
                    .Where(t => string.Equals(t.TableExistsCheck, "Y", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.TableId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("스키마 검수 대상 테이블: {TableCount}개", validTableIds.Count);
                
                if (validTableIds.Count == 0)
                {
                    _logger.LogWarning("스키마 검수 대상 테이블이 0개입니다.");
                    result.WarningCount++;
                    result.Message = "스키마 검수 대상 테이블이 없어 검수를 스킵했습니다.";
                    ReportProgress("완료", 100, "검수 대상 테이블이 없어 검수를 스킵했습니다");
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                var processedTableCount = 0;
                var totalTableCount = validTableIds.Count;

                foreach (var tableId in validTableIds)
                {
                    try
                    {
                        // 테이블별 진행률 계산 (20% ~ 90% 범위)
                        var tableProgress = 20 + (processedTableCount * 70 / totalTableCount);
                        ReportProgress("테이블 검수", tableProgress, $"테이블 {tableId} 검수 중 ({processedTableCount + 1}/{totalTableCount})", tableId);

                        // 메모리 사용량 모니터링
                        ReportMemoryUsage($"테이블_{tableId}_시작");

                        // 해당 테이블의 FeatureClass 찾기
                        var featureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, tableId);
                        if (featureClassInfo == null || !featureClassInfo.Exists)
                        {
                            _logger.LogWarning("테이블 {TableId}에 해당하는 FeatureClass를 찾을 수 없습니다", tableId);
                            processedTableCount++;
                            continue;
                        }

                        // 실제 스키마 추출
                        var actualSchema = await _gdalService.GetDetailedSchemaAsync(gdbPath, featureClassInfo.Name);
                        
                        // 해당 테이블의 스키마 설정 필터링
                        var tableSchemaConfigs = schemaConfigs.Where(sc => 
                            sc.TableId.Equals(tableId, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (!tableSchemaConfigs.Any())
                        {
                            _logger.LogWarning("테이블 {TableId}에 대한 스키마 설정이 없습니다", tableId);
                            processedTableCount++;
                            continue;
                        }
                        
                        _logger.LogInformation("테이블 {TableId} 스키마 검수 시작 - 설정 필드 {FieldCount}개", tableId, tableSchemaConfigs.Count);

                        // 필드별 스키마 검수 수행
                        await ValidateTableSchemaAsync(tableId, actualSchema, tableSchemaConfigs, result, gdbPath);
                        
                        _logger.LogInformation("테이블 {TableId} 스키마 검수 완료", tableId);
                        
                        // 메모리 사용량 모니터링
                        ReportMemoryUsage($"테이블_{tableId}_완료");
                        
                        processedTableCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "테이블 {TableId} 스키마 검수 중 오류 발생", tableId);
                        result.ErrorCount++;
                        result.Errors.Add(new ValidationError 
                        { 
                            Message = $"테이블 {tableId} 스키마 검수 중 오류: {ex.Message}" 
                        });
                        processedTableCount++;
                    }
                }

                // 전체 결과 정리
                ReportProgress("결과 정리", 95, "검수 결과를 정리하는 중");
                result.IsValid = result.ErrorCount == 0;
                
                // 통계 설정
                result.TotalColumnCount = result.SchemaResults.Count;
                result.ProcessedColumnCount = result.SchemaResults.Count(s => s.ColumnExists);
                result.SkippedColumnCount = result.SchemaResults.Count(s => !s.ColumnExists);
                
                if (result.IsValid)
                {
                    result.Message = $"스키마 검수 완료 - 모든 필드가 설정과 일치합니다 (검수 필드: {result.SchemaResults.Count}개)";
                }
                else
                {
                    result.Message = $"스키마 검수 완료 - {result.ErrorCount}개 오류, {result.WarningCount}개 경고 발견";
                }

                _logger.LogInformation("스키마 검수 완료: 오류 {ErrorCount}개, 경고 {WarningCount}개, 검수 필드 {FieldCount}개, 통계 (전체: {Total}, 처리: {Processed})", 
                    result.ErrorCount, result.WarningCount, result.SchemaResults.Count,
                    result.TotalColumnCount, result.ProcessedColumnCount);

                // 검수 이력 저장
                try
                {
                    ReportProgress("이력 저장", 98, "검수 이력을 저장하는 중");
                    await _historyService.SaveValidationHistoryAsync(Guid.NewGuid().ToString(), gdbPath, "스키마 검수 완료");
                    _logger.LogInformation("스키마 검수 이력 저장 완료");
                }
                catch (Exception historyEx)
                {
                    _logger.LogWarning(historyEx, "검수 이력 저장 실패 (검수 결과에는 영향 없음)");
                }

                ReportProgress("완료", 100, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 검수 중 전체 오류 발생");
                result.ErrorCount++;
                result.IsValid = false;
                result.Message = $"스키마 검수 중 오류 발생: {ex.Message}";
            }

            result.CompletedAt = DateTime.Now;
            return result;
        }    
    /// <summary>
        /// 개별 테이블의 스키마를 검수합니다
        /// </summary>
        private async Task ValidateTableSchemaAsync(
            string tableId,
            DetailedSchemaInfo actualSchema,
            List<SchemaConfig> expectedConfigs,
            SchemaCheckResult result,
            string gdbPath)
        {
            _logger.LogDebug("테이블 {TableId} 스키마 검수 시작 - 실제 필드 {ActualCount}개, 설정 필드 {ExpectedCount}개", 
                tableId, actualSchema.Fields.Count, expectedConfigs.Count);

            var processedFieldCount = 0;
            var totalFieldCount = expectedConfigs.Count;

            // 테이블별 UK 검사 결과 캐시 (중복 검사 방지)
            var ukResultsCache = new Dictionary<string, UniqueKeyValidationResult>();

            foreach (var expectedConfig in expectedConfigs)
            {
                try
                {
                    // 필드별 진행률 보고
                    ReportProgress("필드 검수", 0, $"필드 {expectedConfig.FieldName} 검수 중 ({processedFieldCount + 1}/{totalFieldCount})", 
                        tableId, expectedConfig.FieldName);

                    // 실제 스키마에서 해당 필드 찾기
                    var actualField = actualSchema.Fields.FirstOrDefault(f => 
                        f.Name.Equals(expectedConfig.FieldName, StringComparison.OrdinalIgnoreCase));

                    var schemaItem = new SchemaValidationItem
                    {
                        TableId = tableId,
                        ColumnName = expectedConfig.FieldName,
                        ExpectedDataType = expectedConfig.DataType,
                        ExpectedLength = expectedConfig.Length.ToString(),
                        ExpectedNotNull = expectedConfig.NotNull,
                        ColumnExists = actualField != null
                    };

                    if (actualField != null)
                    {
                        // 실제 필드 정보 설정
                        schemaItem.ActualDataType = actualField.DataType;
                        schemaItem.ActualLength = actualField.Length.ToString();
                        schemaItem.ActualNotNull = actualField.IsNullable ? "N" : "Y";

                        // 상세 비교 수행
                        await PerformDetailedFieldComparisonAsync(expectedConfig, actualField, schemaItem, gdbPath);

                        // UK/FK 검수 결과 통합 (캐시 사용)
                        await IntegrateUkFkResultsAsync(expectedConfig, schemaItem, gdbPath, ukResultsCache);
                    }
                    else
                    {
                        // 필드가 존재하지 않음
                        schemaItem.ActualDataType = "없음";
                        schemaItem.ActualLength = "0";
                        schemaItem.ActualNotNull = "N";
                        schemaItem.DataTypeMatches = false;
                        schemaItem.LengthMatches = false;
                        schemaItem.NotNullMatches = false;
                        schemaItem.UniqueKeyMatches = true; // 존재하지 않는 필드는 UK/FK 검사 스킵
                        schemaItem.ForeignKeyMatches = true;

                        _logger.LogWarning("필드 누락: 테이블 {TableId}에서 필드 '{FieldName}'을 찾을 수 없습니다", 
                            tableId, expectedConfig.FieldName);
                    }

                    // 오류내용 자동 생성
                    schemaItem.UpdateErrorContent();
                    
                    result.SchemaResults.Add(schemaItem);

                    // 오류/경고 처리
                    if (!schemaItem.IsValid)
                    {
                        result.ErrorCount++;
                        var errorDetails = new List<string>();
                        
                        if (!schemaItem.ColumnExists)
                            errorDetails.Add("필드 없음");
                        if (!schemaItem.DataTypeMatches)
                            errorDetails.Add($"타입 불일치 (예상: {schemaItem.ExpectedDataType}, 실제: {schemaItem.ActualDataType})");
                        if (!schemaItem.LengthMatches)
                            errorDetails.Add($"길이 불일치 (예상: {schemaItem.ExpectedLength}, 실제: {schemaItem.ActualLength})");
                        if (!schemaItem.NotNullMatches)
                        {
                            string expectedMsg = schemaItem.ExpectedNotNull == "Y" ? "NOT NULL 필수" : "NULL 허용";
                            string actualMsg = schemaItem.ActualNotNull == "Y" ? "NOT NULL" : "NULL 허용";
                            errorDetails.Add($"NULL 제약 불일치 (설정: {expectedMsg}, 실제: {actualMsg})");
                        }
                        if (!schemaItem.UniqueKeyMatches)
                            errorDetails.Add($"UNIQUE KEY 제약 위반");
                        if (!schemaItem.ForeignKeyMatches)
                            errorDetails.Add($"FOREIGN KEY 제약 위반");

                        result.Errors.Add(new ValidationError 
                        { 
                            Message = $"{tableId}.{expectedConfig.FieldName}: {string.Join(", ", errorDetails)}" 
                        });
                    }

                    processedFieldCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "필드 {FieldName} 검수 중 오류 발생", expectedConfig.FieldName);
                    result.ErrorCount++;
                    result.Errors.Add(new ValidationError 
                    { 
                        Message = $"{tableId}.{expectedConfig.FieldName}: 검수 중 오류 - {ex.Message}" 
                    });
                    processedFieldCount++;
                }
            }

            // 정의되지 않은(추가) 컬럼 탐지: 실제에 있으나 설정에 없는 컬럼은 오류로 기록
            try
            {
                var expectedFieldNames = expectedConfigs.Select(c => c.FieldName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var undefinedFields = actualSchema.Fields.Where(f => !expectedFieldNames.Contains(f.Name)).ToList();

                // 예외 처리 목록: FGDB가 자동 생성하는 면적/길이 필드 등 (오류로 취급하지 않음)
                var ignoredExtraFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Shape_Area", "Shape_Length", "shape_Area", "shape_Length"
                };
                foreach (var uf in undefinedFields)
                {
                    if (ignoredExtraFieldNames.Contains(uf.Name))
                    {
                        _logger.LogInformation("정의 예외 컬럼 무시: {TableId}.{Field}", tableId, uf.Name);
                        continue;
                    }
                    var item = new SchemaValidationItem
                    {
                        TableId = tableId,
                        ColumnName = uf.Name,
                        ColumnKoreanName = string.Empty,
                        ActualDataType = uf.DataType,
                        ActualLength = uf.Length.ToString(),
                        ActualNotNull = uf.IsNullable ? "N" : "Y",
                        ColumnExists = true,
                        // 정의되지 않은 컬럼 → 오류 유발
                        IsUndefinedField = true,
                        DataTypeMatches = false,
                        LengthMatches = false,
                        NotNullMatches = true,
                        UniqueKeyMatches = true,
                        ForeignKeyMatches = true,
                        Status = "정의되지 않은 컬럼"
                    };

                    // 오류내용 자동 생성
                    item.Errors.Add("스키마 설정에 정의되지 않은 컬럼");
                    item.UpdateErrorContent();

                    result.SchemaResults.Add(item);
                    result.ErrorCount++;
                    result.Errors.Add(new ValidationError { Message = $"{tableId}.{uf.Name}: 스키마 설정에 정의되지 않은 컬럼입니다" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "정의되지 않은 컬럼 탐지 중 경고");
            }
        }

        /// <summary>
        /// 필드의 상세 비교를 수행합니다
        /// </summary>
        private async Task PerformDetailedFieldComparisonAsync(
            SchemaConfig expectedConfig,
            DetailedFieldInfo actualField,
            SchemaValidationItem schemaItem,
            string gdbPath)
        {
            // 1. 데이터 타입 비교
            schemaItem.DataTypeMatches = CompareDataTypes(expectedConfig.DataType, actualField.DataType);

            // 2. 길이 비교
            schemaItem.LengthMatches = CompareLengths(expectedConfig.Length, actualField.Length, actualField.DataType);

            // 3. NOT NULL 제약 비교
            // NN 설정이 없거나 빈값이면 검사하지 않음 (항상 통과)
            if (string.IsNullOrEmpty(expectedConfig.NotNull) || expectedConfig.NotNull != "Y")
            {
                schemaItem.NotNullMatches = true; // NN 검사 대상이 아니므로 통과
                _logger.LogDebug("NN 검사 스킵: {TableId}.{FieldName} (NN설정='{NotNull}')", 
                    expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.NotNull ?? "null");
            }
            else
            {
                // NN="Y"인 경우만 실제 검사 수행
                bool actualNotNull = !actualField.IsNullable;
                schemaItem.NotNullMatches = actualNotNull; // 실제 필드가 NotNull이어야 함
                
                _logger.LogDebug("NN 검사 수행: {TableId}.{FieldName} - 예상:NotNull, 실제:{ActualNotNull}, 결과:{Result}", 
                    expectedConfig.TableId, expectedConfig.FieldName, 
                    actualNotNull ? "NotNull" : "Nullable", schemaItem.NotNullMatches);
            }

            // 4. UK/FK 제약 검사 (실제 데이터 검사)
            schemaItem.UniqueKeyMatches = await ValidateUniqueKeyAsync(expectedConfig, actualField, gdbPath);
            schemaItem.ForeignKeyMatches = await ValidateForeignKeyAsync(expectedConfig, actualField, gdbPath);

            _logger.LogDebug("필드 {FieldName} 비교 결과: 타입={TypeMatch}, 길이={LengthMatch}, NotNull={NotNullMatch}, UK={UkMatch}, FK={FkMatch}",
                expectedConfig.FieldName, schemaItem.DataTypeMatches, schemaItem.LengthMatches, schemaItem.NotNullMatches,
                schemaItem.UniqueKeyMatches, schemaItem.ForeignKeyMatches);
        }        
/// <summary>
        /// 데이터 타입을 비교합니다
        /// </summary>
        private bool CompareDataTypes(string expectedType, string actualType)
        {
            // 정규화된 타입으로 비교
            var normalizedExpected = NormalizeDataType(expectedType);
            var normalizedActual = NormalizeDataType(actualType);

            return normalizedExpected.Equals(normalizedActual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 데이터 타입을 정규화합니다
        /// </summary>
        private string NormalizeDataType(string dataType)
        {
            return dataType.ToUpper() switch
            {
                "TEXT" or "STRING" or "VARCHAR" or "CHAR" => "TEXT",
                "INTEGER" or "INT" or "LONG" or "INTEGER64" => "INTEGER",
                "REAL" or "DOUBLE" or "FLOAT" or "NUMERIC" => "REAL",
                "DATE" or "DATETIME" or "TIMESTAMP" => "DATE",
                _ => dataType.ToUpper()
            };
        }

        /// <summary>
        /// 필드 길이를 비교합니다
        /// </summary>
        private bool CompareLengths(int expectedLength, int actualLength, string dataType)
        {
            // 숫자 타입은 길이 비교 스킵
            if (dataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("REAL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // GDAL에서 길이 정보가 없는 경우 (0) 통과 처리
            if (actualLength == 0)
            {
                return true;
            }

            // ±2 오차 허용
            return Math.Abs(expectedLength - actualLength) <= 2;
        }

        /// <summary>
        /// Unique Key 제약을 검증합니다 (실제 데이터 중복 검사)
        /// </summary>
        private async Task<bool> ValidateUniqueKeyAsync(SchemaConfig expectedConfig, DetailedFieldInfo actualField, string gdbPath)
        {
            // UK 설정이 없거나 "Y"가 아니면 검사 안함
            if (string.IsNullOrEmpty(expectedConfig.UniqueKey) || expectedConfig.UniqueKey != "Y")
            {
                _logger.LogDebug("UK 검사 스킵: {TableId}.{FieldName} (UK설정='{UniqueKey}')", 
                    expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.UniqueKey ?? "null");
                return true; // 검사 대상이 아니므로 통과
            }

            try
            {
                _logger.LogInformation("UK 검사 시작: {TableId}.{FieldName}", expectedConfig.TableId, expectedConfig.FieldName);

                // 테이블명을 TableId로부터 찾기
                var featureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.TableId);
                if (featureClassInfo == null || !featureClassInfo.Exists)
                {
                    _logger.LogWarning("UK 검사 실패: 테이블 {TableId}에 해당하는 FeatureClass를 찾을 수 없습니다", expectedConfig.TableId);
                    return false;
                }

                // 실제 UK 검수 수행
                var ukResult = await _uniqueKeyValidator.ValidateUniqueKeyAsync(gdbPath, featureClassInfo.Name, expectedConfig.FieldName);
                
                if (ukResult.IsValid)
                {
                    _logger.LogInformation("UK 검사 통과: {TableId}.{FieldName} - 중복값 없음", 
                        expectedConfig.TableId, expectedConfig.FieldName);
                    return true;
                }
                else
                {
                    _logger.LogWarning("UK 검사 실패: {TableId}.{FieldName} - {DuplicateCount}개 중복값 발견", 
                        expectedConfig.TableId, expectedConfig.FieldName, ukResult.DuplicateValues);
                    
                    // 중복값 상세 정보 로깅
                    foreach (var duplicate in ukResult.Duplicates.Take(5)) // 상위 5개만 로깅
                    {
                        _logger.LogDebug("중복값: '{Value}'", duplicate.Value);
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UK 검사 중 오류 발생: {TableId}.{FieldName}", expectedConfig.TableId, expectedConfig.FieldName);
                return false; // 오류 발생 시 실패로 처리
            }
        }

        /// <summary>
        /// Foreign Key 제약을 검증합니다 (실제 참조 무결성 검사)
        /// </summary>
        private async Task<bool> ValidateForeignKeyAsync(SchemaConfig expectedConfig, DetailedFieldInfo actualField, string gdbPath)
        {
            // FK 설정이 없으면 검사 안함
            if (string.IsNullOrEmpty(expectedConfig.ForeignKey) || expectedConfig.ForeignKey != "Y")
            {
                return true; // 검사 대상이 아니므로 통과
            }

            // FK가 설정된 경우 참조 테이블과 컬럼 정보 확인
            if (string.IsNullOrEmpty(expectedConfig.ReferenceTable) || string.IsNullOrEmpty(expectedConfig.ReferenceColumn))
            {
                _logger.LogWarning("FK 설정 오류: {FieldName} - 참조테이블 또는 참조컬럼 정보 없음", expectedConfig.FieldName);
                return false; // FK 설정이 불완전하면 오류
            }

            try
            {
                _logger.LogInformation("FK 검사 시작: {TableId}.{FieldName} -> {RefTable}.{RefColumn}", 
                    expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn);

                // 소스 테이블명을 TableId로부터 찾기
                var sourceFeatureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.TableId);
                if (sourceFeatureClassInfo == null || !sourceFeatureClassInfo.Exists)
                {
                    _logger.LogWarning("FK 검사 실패: 소스 테이블 {TableId}에 해당하는 FeatureClass를 찾을 수 없습니다", expectedConfig.TableId);
                    return false;
                }

                // 참조 테이블명을 TableId로부터 찾기 (ReferenceTable이 TableId일 수 있음)
                var referenceFeatureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.ReferenceTable);
                string referenceTableName;
                
                if (referenceFeatureClassInfo != null && referenceFeatureClassInfo.Exists)
                {
                    // TableId로 찾은 경우
                    referenceTableName = referenceFeatureClassInfo.Name;
                }
                else
                {
                    // 직접 테이블명으로 사용
                    referenceTableName = expectedConfig.ReferenceTable;
                }

                // 실제 FK 검수 수행
                var fkResult = await _foreignKeyValidator.ValidateForeignKeyAsync(
                    gdbPath, 
                    sourceFeatureClassInfo.Name, 
                    expectedConfig.FieldName,
                    referenceTableName,
                    expectedConfig.ReferenceColumn);

                if (fkResult.IsValid)
                {
                    _logger.LogInformation("FK 검사 통과: {TableId}.{FieldName} -> {RefTable}.{RefColumn} - 참조 무결성 유지", 
                        expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn);
                    return true;
                }
                else
                {
                    _logger.LogWarning("FK 검사 실패: {TableId}.{FieldName} -> {RefTable}.{RefColumn} - {OrphanCount}개 고아 레코드 발견", 
                        expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn, fkResult.OrphanCount);
                    
                    // 고아 레코드 상세 정보 로깅 (상위 5개만)
                    foreach (var orphan in fkResult.OrphanValues.Take(5))
                    {
                        _logger.LogDebug("고아 레코드: Value='{Value}'", orphan);
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FK 검사 중 오류 발생: {TableId}.{FieldName} -> {RefTable}.{RefColumn}", 
                    expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn);
                return false; // 오류 발생 시 실패로 처리
            }
        } 
       /// <summary>
        /// 스키마 설정 파일을 로드합니다
        /// </summary>
        private async Task<List<SchemaConfig>> LoadSchemaConfigAsync(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    _logger.LogError("스키마 설정 파일을 찾을 수 없습니다: {Path}", configPath);
                    return new List<SchemaConfig>();
                }

                var configs = new List<SchemaConfig>();
                
                // 파일 잠금 문제 해결을 위한 안전한 파일 읽기
                string[] lines;
                using (var fileStream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    var content = await reader.ReadToEndAsync();
                    lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }

                // 헤더 스킵
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(',');
                    if (parts.Length >= 9) // 최소 9개 컬럼 필요
                    {
                        var config = new SchemaConfig
                        {
                            TableId = parts[0].Trim(),
                            FieldName = parts[1].Trim(),
                            DataType = parts[3].Trim(),
                            Length = int.TryParse(parts[4].Trim().Trim('"'), out int length) ? length : 0,
                            UniqueKey = parts[5].Trim(),
                            ForeignKey = parts[6].Trim(),
                            NotNull = parts[7].Trim(),
                            ReferenceTable = parts.Length > 8 ? parts[8].Trim() : string.Empty,
                            ReferenceColumn = parts.Length > 9 ? parts[9].Trim() : string.Empty
                        };
                        
                        configs.Add(config);
                    }
                }

                _logger.LogInformation("스키마 설정 로드 완료: {ConfigCount}개 설정", configs.Count);
                return configs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 설정 파일 로드 중 오류 발생: {Path}", configPath);
                return new List<SchemaConfig>();
            }
        }

        /// <summary>
        /// 스키마 검수 진행률을 보고합니다
        /// </summary>
        private void ReportProgress(string currentStage, int progressPercentage, string statusMessage, 
            string? currentTable = null, string? currentField = null)
        {
            var args = new SchemaValidationProgressEventArgs
            {
                CurrentStage = currentStage,
                ProgressPercentage = Math.Max(0, Math.Min(100, progressPercentage)),
                StatusMessage = statusMessage,
                CurrentTable = currentTable,
                CurrentField = currentField,
                Timestamp = DateTime.UtcNow
            };

            ProgressUpdated?.Invoke(this, args);
            _logger.LogDebug("스키마 검수 진행률: {Stage} {Progress}% - {Message}", 
                currentStage, progressPercentage, statusMessage);
        }

        /// <summary>
        /// UK 검수 진행률 이벤트 핸들러
        /// </summary>
        private void OnUniqueKeyProgressUpdated(object? sender, UniqueKeyValidationProgressEventArgs e)
        {
            // UK 검수 진행률을 스키마 검수 진행률로 변환
            var message = $"UK 검수 진행 중 - {e.StatusMessage}";
            ReportProgress("UK검수", e.Progress, message, "", "");
        }

        /// <summary>
        /// UK/FK 검수 결과를 SchemaValidationItem에 통합합니다 (캐싱 지원)
        /// </summary>
        private async Task IntegrateUkFkResultsAsync(SchemaConfig expectedConfig, SchemaValidationItem schemaItem, string gdbPath, Dictionary<string, UniqueKeyValidationResult>? ukResultsCache = null)
        {
            try
            {
                // UK 검수 결과가 있는 경우 통합
                if (!string.IsNullOrEmpty(expectedConfig.UniqueKey) && expectedConfig.UniqueKey == "Y")
                {
                    var featureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.TableId);
                    if (featureClassInfo != null && featureClassInfo.Exists)
                    {
                        // 캐시 키 생성 (테이블명 + 필드명)
                        string cacheKey = $"{featureClassInfo.Name}.{expectedConfig.FieldName}";
                        UniqueKeyValidationResult ukResult;

                        // 캐시에서 결과 확인
                        if (ukResultsCache != null && ukResultsCache.ContainsKey(cacheKey))
                        {
                            ukResult = ukResultsCache[cacheKey];
                            _logger.LogDebug("UK 검사 결과 캐시 사용: {CacheKey}", cacheKey);
                        }
                        else
                        {
                            // 실제 UK 검사 수행
                            ukResult = await _uniqueKeyValidator.ValidateUniqueKeyAsync(gdbPath, featureClassInfo.Name, expectedConfig.FieldName);
                            
                            // 캐시에 저장
                            if (ukResultsCache != null)
                            {
                                ukResultsCache[cacheKey] = ukResult;
                                _logger.LogDebug("UK 검사 결과 캐시 저장: {CacheKey}", cacheKey);
                            }
                        }

                        // 결과 통합
                        schemaItem.UniqueKeyMatches = ukResult.IsValid;
                        schemaItem.DuplicateValueCount = ukResult.DuplicateValues;
                        schemaItem.DuplicateValues = ukResult.Duplicates.Select(d => $"{d.Value}({d.Count}개)").ToList();

                        if (!ukResult.IsValid)
                        {
                            _logger.LogWarning("UK 검사 실패: {TableId}.{FieldName} - {DuplicateCount}개 중복값 발견", 
                                expectedConfig.TableId, expectedConfig.FieldName, ukResult.DuplicateValues);
                        }
                        else
                        {
                            _logger.LogInformation("UK 검사 통과: {TableId}.{FieldName} - 중복값 없음", 
                                expectedConfig.TableId, expectedConfig.FieldName);
                        }
                    }
                }
                else
                {
                    // UK 검사 대상이 아닌 경우 통과 처리
                    schemaItem.UniqueKeyMatches = true;
                    schemaItem.DuplicateValueCount = 0;
                    schemaItem.DuplicateValues = new List<string>();
                }

                // FK 검수 결과가 있는 경우 통합
                if (!string.IsNullOrEmpty(expectedConfig.ForeignKey) && expectedConfig.ForeignKey == "Y" &&
                    !string.IsNullOrEmpty(expectedConfig.ReferenceTable) && !string.IsNullOrEmpty(expectedConfig.ReferenceColumn))
                {
                    var sourceFeatureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.TableId);
                    if (sourceFeatureClassInfo != null && sourceFeatureClassInfo.Exists)
                    {
                        // 참조 테이블명 결정
                        var referenceFeatureClassInfo = await _gdalService.FindFeatureClassByTableIdAsync(gdbPath, expectedConfig.ReferenceTable);
                        string referenceTableName = referenceFeatureClassInfo?.Exists == true 
                            ? referenceFeatureClassInfo.Name 
                            : expectedConfig.ReferenceTable;

                        var fkResult = await _foreignKeyValidator.ValidateForeignKeyAsync(
                            gdbPath, 
                            sourceFeatureClassInfo.Name, 
                            expectedConfig.FieldName,
                            referenceTableName,
                            expectedConfig.ReferenceColumn);
                        
                        // FK 결과 통합
                        schemaItem.ForeignKeyMatches = fkResult.IsValid;
                        schemaItem.OrphanRecordCount = fkResult.OrphanCount;
                        schemaItem.OrphanValues = fkResult.OrphanValues;
                        schemaItem.OrphanRecordValues = fkResult.OrphanValues; // 동기화

                        if (!fkResult.IsValid)
                        {
                            _logger.LogWarning("FK 검사 실패: {TableId}.{FieldName} -> {RefTable}.{RefColumn} - {OrphanCount}개 고아 레코드 발견", 
                                expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn, fkResult.OrphanCount);
                        }
                        else
                        {
                            _logger.LogInformation("FK 검사 통과: {TableId}.{FieldName} -> {RefTable}.{RefColumn} - 참조 무결성 유지", 
                                expectedConfig.TableId, expectedConfig.FieldName, expectedConfig.ReferenceTable, expectedConfig.ReferenceColumn);
                        }
                    }
                }
                else
                {
                    // FK 검사 대상이 아닌 경우 통과 처리
                    schemaItem.ForeignKeyMatches = true;
                    schemaItem.OrphanRecordCount = 0;
                    schemaItem.OrphanValues = new List<string>();
                    schemaItem.OrphanRecordValues = new List<string>(); // 동기화
                }

                // 표준화된 오류 메시지 생성
                if (!schemaItem.IsValid || schemaItem.DuplicateValueCount > 0 || schemaItem.OrphanRecordCount > 0)
                {
                    var standardizedMessage = schemaItem.GetStandardizedErrorMessage();
                    if (!schemaItem.Errors.Contains(standardizedMessage))
                    {
                        schemaItem.Errors.Add(standardizedMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UK/FK 검수 결과 통합 중 오류 발생: {TableId}.{FieldName}", 
                    expectedConfig.TableId, expectedConfig.FieldName);
                
                schemaItem.Errors.Add($"UK/FK 검수 결과 통합 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 메모리 사용량을 모니터링하고 보고합니다
        /// </summary>
        private void ReportMemoryUsage(string context)
        {
            var currentMemory = GC.GetTotalMemory(false);
            var memoryMB = currentMemory / (1024.0 * 1024.0);
            
            _logger.LogDebug("메모리 사용량 ({Context}): {MemoryMB:F2} MB", context, memoryMB);
            
            // 메모리 사용량이 높으면 가비지 컬렉션 수행
            if (memoryMB > 500) // 500MB 이상 사용 시
            {
                _logger.LogInformation("높은 메모리 사용량 감지 ({MemoryMB:F2} MB), 가비지 컬렉션 수행", memoryMB);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var afterGC = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                _logger.LogInformation("가비지 컬렉션 완료: {BeforeMB:F2} MB → {AfterMB:F2} MB", memoryMB, afterGC);
            }
        }
    }

    /// <summary>
    /// 스키마 검수 진행률 이벤트 인자
    /// </summary>
    public class SchemaValidationProgressEventArgs : EventArgs
    {
        /// <summary>
        /// 현재 단계
        /// </summary>
        public string CurrentStage { get; set; } = string.Empty;

        /// <summary>
        /// 진행률 (0-100)
        /// </summary>
        public int ProgressPercentage { get; set; }

        /// <summary>
        /// 상태 메시지
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 현재 처리 중인 테이블
        /// </summary>
        public string? CurrentTable { get; set; }

        /// <summary>
        /// 현재 처리 중인 필드
        /// </summary>
        public string? CurrentField { get; set; }

        /// <summary>
        /// 타임스탬프
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 스키마 설정 클래스
    /// </summary>
    public class SchemaConfig
    {
        public string TableId { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int Length { get; set; }
        public string UniqueKey { get; set; } = string.Empty;
        public string ForeignKey { get; set; } = string.Empty;
        public string NotNull { get; set; } = string.Empty;
        public string ReferenceTable { get; set; } = string.Empty;
        public string ReferenceColumn { get; set; } = string.Empty;
    }
}