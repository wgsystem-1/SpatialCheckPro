using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 오류 타입별 통계 항목
    /// </summary>
    public class ErrorTypeItem
    {
        public string ErrorType { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public double BarWidth { get; set; }
    }

    /// <summary>
    /// 테이블별 오류 통계 항목
    /// </summary>
    public class TableErrorItem
    {
        public string TableId { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(TableId) 
            ? TableName 
            : $"{TableId} - {TableName}";
        public int ErrorCount { get; set; }
        public double BarWidth { get; set; }
        public Brush SeverityColor { get; set; } = Brushes.Gray;
    }

    /// <summary>
    /// 와이드 스크린 최적화 대시보드
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class WideDashboardView : UserControl
    {
        private readonly ILogger<WideDashboardView>? _logger;
        private SpatialCheckPro.Models.ValidationResult? _validationResult;

        public WideDashboardView()
        {
            InitializeComponent();
            
            var app = Application.Current as App;
            _logger = app?.GetService<ILogger<WideDashboardView>>();
        }

        /// <summary>
        /// 검수 결과를 설정하고 대시보드를 업데이트합니다
        /// </summary>
        public void SetValidationResult(SpatialCheckPro.Models.ValidationResult result)
        {
            _validationResult = result;
            UpdateDashboard();
        }

        private void UpdateDashboard()
        {
            if (_validationResult == null) return;

            try
            {
                // KPI 업데이트
                UpdateKpis();
                
                // 차트 업데이트
                UpdateStageErrorsChart();
                UpdateErrorTypesChart();
                UpdateTableErrorsChart();
                
                // 권장 조치사항
                UpdateRecommendation();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "대시보드 업데이트 중 오류");
            }
        }

        private void UpdateKpis()
        {
            if (_validationResult == null) return;

            // 검수 결과
            ValidationResultText.Text = _validationResult.IsValid ? "✓" : "✗";
            ValidationResultLabel.Text = _validationResult.IsValid ? "성공" : "실패";
            ValidationResultText.Foreground = _validationResult.IsValid 
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) 
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            // 총 오류
            TotalErrorsText.Text = _validationResult.ErrorCount.ToString("N0");

            // 검수 시간
            ProcessingTimeText.Text = FormatTimeSpan(_validationResult.ProcessingTime);

            // 처리 항목 (전체 피처 수)
            var totalFeatures = _validationResult.TableCheckResult?.TableResults?.Sum(t => t.FeatureCount ?? 0) ?? 0;
            ProcessedItemsText.Text = totalFeatures.ToString("N0");

            // 성공률
            var successRate = 100.0 - (_validationResult.ErrorCount * 100.0 / Math.Max(totalFeatures, 1));
            SuccessRateText.Text = $"{successRate:F1}%";
            SuccessRateText.Foreground = successRate >= 90 
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                : successRate >= 70
                    ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            // 검수 테이블
            var totalTables = _validationResult.TableCheckResult?.TotalTableCount ?? 0;
            TotalTablesText.Text = totalTables.ToString();

            // 파일 크기
            try
            {
                if (!string.IsNullOrEmpty(_validationResult.TargetFile) && Directory.Exists(_validationResult.TargetFile))
                {
                    var dirInfo = new System.IO.DirectoryInfo(_validationResult.TargetFile);
                    var totalSize = dirInfo.EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length);
                    FileSizeText.Text = FormatFileSize(totalSize);
                }
                else if (!string.IsNullOrEmpty(_validationResult.TargetFile) && File.Exists(_validationResult.TargetFile))
                {
                    var fileInfo = new System.IO.FileInfo(_validationResult.TargetFile);
                    FileSizeText.Text = FormatFileSize(fileInfo.Length);
                }
                else
                {
                    FileSizeText.Text = "N/A";
                }
            }
            catch
            {
                FileSizeText.Text = "N/A";
            }
        }

        private void UpdateStageErrorsChart()
        {
            if (_validationResult == null) return;

            var maxError = _validationResult.ErrorCount;
            const double barMaxWidth = 200.0; // 고정 최대 너비
            
            // 데이터 레벨에서 이미 재분류되었으므로 직접 사용
            var attributeErrorCount = _validationResult.AttributeRelationCheckResult?.ErrorCount ?? 0;
            var relationErrorCount = _validationResult.RelationCheckResult?.ErrorCount ?? 0;
            
            var stageErrors = new List<dynamic>
            {
                new { StageNumber = 0, StageName = "FileGDB", ErrorCount = 0, MaxErrorCount = maxError, ErrorColor = GetErrorColor(0), BarWidth = 0.0 },
                new { StageNumber = 1, StageName = "테이블", ErrorCount = _validationResult.TableCheckResult?.ErrorCount ?? 0, MaxErrorCount = maxError, ErrorColor = GetErrorColor(_validationResult.TableCheckResult?.ErrorCount ?? 0), BarWidth = CalculateBarWidth(_validationResult.TableCheckResult?.ErrorCount ?? 0, maxError, barMaxWidth) },
                new { StageNumber = 2, StageName = "스키마", ErrorCount = _validationResult.SchemaCheckResult?.ErrorCount ?? 0, MaxErrorCount = maxError, ErrorColor = GetErrorColor(_validationResult.SchemaCheckResult?.ErrorCount ?? 0), BarWidth = CalculateBarWidth(_validationResult.SchemaCheckResult?.ErrorCount ?? 0, maxError, barMaxWidth) },
                new { StageNumber = 3, StageName = "지오메트리", ErrorCount = _validationResult.GeometryCheckResult?.ErrorCount ?? 0, MaxErrorCount = maxError, ErrorColor = GetErrorColor(_validationResult.GeometryCheckResult?.ErrorCount ?? 0), BarWidth = CalculateBarWidth(_validationResult.GeometryCheckResult?.ErrorCount ?? 0, maxError, barMaxWidth) },
                new { StageNumber = 4, StageName = "속성관계", ErrorCount = attributeErrorCount, MaxErrorCount = maxError, ErrorColor = GetErrorColor(attributeErrorCount), BarWidth = CalculateBarWidth(attributeErrorCount, maxError, barMaxWidth) },
                new { StageNumber = 5, StageName = "공간관계", ErrorCount = relationErrorCount, MaxErrorCount = maxError, ErrorColor = GetErrorColor(relationErrorCount), BarWidth = CalculateBarWidth(relationErrorCount, maxError, barMaxWidth) }
            };

            StageErrorsList.ItemsSource = stageErrors;
        }
        
        /// <summary>
        /// 오류 개수 대비 바 차트 너비 계산
        /// </summary>
        private static double CalculateBarWidth(int errorCount, int maxErrorCount, double maxWidth)
        {
            if (maxErrorCount <= 0) return 0;
            return (errorCount / (double)maxErrorCount) * maxWidth;
        }

        private void UpdateErrorTypesChart()
        {
            if (_validationResult == null) return;

            // 지오메트리 오류 타입 집계
            var errorTypes = new Dictionary<string, int>();
            
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var result in _validationResult.GeometryCheckResult.GeometryResults)
                {
                    if (result.DuplicateCount > 0) AddOrUpdate(errorTypes, "중복", result.DuplicateCount);
                    if (result.OverlapCount > 0) AddOrUpdate(errorTypes, "겹침", result.OverlapCount);
                    if (result.SelfIntersectionCount > 0) AddOrUpdate(errorTypes, "자체교차", result.SelfIntersectionCount);
                    if (result.SelfOverlapCount > 0) AddOrUpdate(errorTypes, "자기중첩", result.SelfOverlapCount);
                    if (result.SliverCount > 0) AddOrUpdate(errorTypes, "슬리버", result.SliverCount);
                    if (result.SpikeCount > 0) AddOrUpdate(errorTypes, "스파이크", result.SpikeCount);
                    if (result.ShortObjectCount > 0) AddOrUpdate(errorTypes, "짧은객체", result.ShortObjectCount);
                    if (result.SmallAreaCount > 0) AddOrUpdate(errorTypes, "작은면적", result.SmallAreaCount);
                    if (result.PolygonInPolygonCount > 0) AddOrUpdate(errorTypes, "홀", result.PolygonInPolygonCount);
                    if (result.MinPointCount > 0) AddOrUpdate(errorTypes, "최소정점", result.MinPointCount);
                    if (result.UndershootCount > 0) AddOrUpdate(errorTypes, "언더슛", result.UndershootCount);
                    if (result.OvershootCount > 0) AddOrUpdate(errorTypes, "오버슛", result.OvershootCount);
                    if (result.BasicValidationErrorCount > 0) AddOrUpdate(errorTypes, "기본검수", result.BasicValidationErrorCount);
                }
            }

            var maxCount = errorTypes.Values.DefaultIfEmpty(0).Max();
            var totalErrors = errorTypes.Values.Sum();
            
            var topErrors = errorTypes
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => new ErrorTypeItem
                {
                    ErrorType = kv.Key,
                    Count = kv.Value,
                    Percentage = totalErrors > 0 ? (kv.Value * 100.0 / totalErrors) : 0,
                    BarWidth = maxCount > 0 ? (kv.Value * 200.0 / maxCount) : 0
                })
                .ToList();

            ErrorTypesList.ItemsSource = topErrors;
        }

        private void UpdateTableErrorsChart()
        {
            if (_validationResult == null) return;

            // 테이블별 오류 집계 (실제 피처클래스 이름을 키로 사용)
            var tableErrors = new Dictionary<string, (string TableId, string TableName, int ErrorCount)>();

            // 실제 검수된 테이블 목록 수집 (각 검수 단계의 결과에서 실제 검수된 테이블만 수집)
            var validatedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // TableId로 실제 한글명을 조회하기 위한 Dictionary (대소문자 무시)
            var tableNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // 1단계: TableCheckResult에서 실제 존재하는 테이블 수집
            if (_validationResult.TableCheckResult?.TableResults != null)
            {
                foreach (var tableResult in _validationResult.TableCheckResult.TableResults)
                {
                    // 테이블이 실제로 존재하는 경우만 추가
                    if (tableResult.TableExistsCheck == "Y" || !string.IsNullOrEmpty(tableResult.ActualFeatureClassName))
                    {
                        // TableId와 ActualFeatureClassName 모두 추가 (대소문자 차이 대응)
                        if (!string.IsNullOrEmpty(tableResult.TableId))
                        {
                            validatedTables.Add(tableResult.TableId);
                            // TableId로 한글명 조회 가능하도록 저장
                            if (!string.IsNullOrEmpty(tableResult.TableName))
                            {
                                tableNameLookup[tableResult.TableId] = tableResult.TableName;
                            }
                        }
                        if (!string.IsNullOrEmpty(tableResult.ActualFeatureClassName))
                        {
                            validatedTables.Add(tableResult.ActualFeatureClassName);
                            // ActualFeatureClassName으로도 한글명 조회 가능하도록 저장
                            if (!string.IsNullOrEmpty(tableResult.TableName))
                            {
                                tableNameLookup[tableResult.ActualFeatureClassName] = tableResult.TableName;
                            }
                        }
                    }
                }
            }

            // 2단계: SchemaCheckResult에서 실제 검수된 테이블 수집
            if (_validationResult.SchemaCheckResult?.SchemaResults != null)
            {
                foreach (var schemaResult in _validationResult.SchemaCheckResult.SchemaResults)
                {
                    if (!string.IsNullOrEmpty(schemaResult.TableId))
                    {
                        validatedTables.Add(schemaResult.TableId);
                        // 스키마 결과에는 TableName이 없으므로 TableCheckResult에서 가져온 정보 사용
                    }
                }
            }

            // 3단계: GeometryCheckResult에서 실제 검수된 테이블 수집
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var geometryResult in _validationResult.GeometryCheckResult.GeometryResults)
                {
                    if (!string.IsNullOrEmpty(geometryResult.TableId))
                    {
                        validatedTables.Add(geometryResult.TableId);
                        // GeometryResult의 TableName도 저장
                        if (!string.IsNullOrEmpty(geometryResult.TableName))
                        {
                            tableNameLookup[geometryResult.TableId] = geometryResult.TableName;
                        }
                    }
                }
            }

            // 1단계: 테이블 검수 오류
            if (_validationResult.TableCheckResult?.TableResults != null)
            {
                foreach (var tableResult in _validationResult.TableCheckResult.TableResults)
                {
                    if (tableResult.Errors.Any() && !string.IsNullOrEmpty(tableResult.TableId))
                    {
                        AddOrUpdateTableError(tableErrors, tableResult.TableId, tableResult.TableId, tableResult.TableName, tableResult.Errors.Count);
                    }
                }
            }

            // 2단계: 스키마 검수 오류 (테이블별로 그룹화, 실제 검수된 테이블만 포함)
            if (_validationResult.SchemaCheckResult?.SchemaResults != null)
            {
                // 테이블별로 스키마 오류 개수 집계 (실제 검수된 테이블만)
                var schemaErrorsByTable = _validationResult.SchemaCheckResult.SchemaResults
                    .Where(s => !s.IsValid && !string.IsNullOrEmpty(s.TableId) && validatedTables.Contains(s.TableId))
                    .GroupBy(s => s.TableId)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var kvp in schemaErrorsByTable)
                {
                    // 실제 한글명 조회 (없으면 TableId 사용)
                    var displayName = tableNameLookup.TryGetValue(kvp.Key, out var name) ? name : kvp.Key;
                    AddOrUpdateTableError(tableErrors, kvp.Key, kvp.Key, displayName, kvp.Value);
                }
            }

            // 3단계: 지오메트리 검수 오류 (실제 검수된 테이블만 포함)
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var result in _validationResult.GeometryCheckResult.GeometryResults)
                {
                    if (!string.IsNullOrEmpty(result.TableId) && validatedTables.Contains(result.TableId))
                    {
                        var displayName = !string.IsNullOrEmpty(result.TableName) 
                            ? result.TableName 
                            : (tableNameLookup.TryGetValue(result.TableId, out var name) ? name : result.TableId);
                        AddOrUpdateTableError(tableErrors, result.TableId, result.TableId, displayName, result.TotalErrorCount);
                    }
                }
            }

            // 4단계: 속성 관계 검수 오류 (SourceTable 우선 사용, 실제 검수된 테이블만 포함)
            if (_validationResult.AttributeRelationCheckResult?.Errors != null)
            {
                foreach (var error in _validationResult.AttributeRelationCheckResult.Errors)
                {
                    // SourceTable을 우선적으로 사용하고, 없으면 TableId 사용
                    var tableId = !string.IsNullOrEmpty(error.SourceTable) ? error.SourceTable : error.TableId;

                    // 실제 검수된 테이블만 통계에 포함
                    if (!string.IsNullOrEmpty(tableId) && validatedTables.Contains(tableId))
                    {
                        // 실제 한글명 조회 (TableCheckResult/GeometryResult에서 가져온 한글명 우선, 없으면 error.TableName, 그것도 없으면 tableId)
                        var displayName = tableNameLookup.TryGetValue(tableId, out var name) 
                            ? name 
                            : (!string.IsNullOrEmpty(error.TableName) ? error.TableName : tableId);
                        AddOrUpdateTableError(tableErrors, tableId, tableId, displayName, 1);
                    }
                }
            }

            // 5단계: 공간 관계 검수 오류 (SourceTable 우선 사용, 실제 검수된 테이블만 포함)
            if (_validationResult.RelationCheckResult?.Errors != null)
            {
                foreach (var error in _validationResult.RelationCheckResult.Errors)
                {
                    // SourceTable을 우선적으로 사용하고, 없으면 TableId 사용
                    var tableId = !string.IsNullOrEmpty(error.SourceTable) ? error.SourceTable : error.TableId;

                    // 실제 검수된 테이블만 통계에 포함
                    if (!string.IsNullOrEmpty(tableId) && validatedTables.Contains(tableId))
                    {
                        // 실제 한글명 조회 (TableCheckResult/GeometryResult에서 가져온 한글명 우선, 없으면 error.TableName, 그것도 없으면 tableId)
                        var displayName = tableNameLookup.TryGetValue(tableId, out var name) 
                            ? name 
                            : (!string.IsNullOrEmpty(error.TableName) ? error.TableName : tableId);
                        AddOrUpdateTableError(tableErrors, tableId, tableId, displayName, 1);
                    }
                }
            }

            var maxCount = tableErrors.Values.Select(v => v.ErrorCount).DefaultIfEmpty(0).Max();

            var topTables = tableErrors.Values
                .OrderByDescending(v => v.ErrorCount)
                .Take(5)
                .Select(v => new TableErrorItem
                {
                    TableId = v.TableId,
                    TableName = v.TableName,
                    ErrorCount = v.ErrorCount,
                    BarWidth = maxCount > 0 ? (v.ErrorCount * 150.0 / maxCount) : 0,
                    SeverityColor = GetErrorColor(v.ErrorCount)
                })
                .ToList();

            TableErrorsList.ItemsSource = topTables;
        }

        /// <summary>
        /// 테이블 오류 정보를 추가하거나 업데이트합니다
        /// </summary>
        private void AddOrUpdateTableError(Dictionary<string, (string TableId, string TableName, int ErrorCount)> dict, 
            string key, string tableId, string tableName, int count)
        {
            if (dict.ContainsKey(key))
            {
                var existing = dict[key];
                dict[key] = (existing.TableId, existing.TableName, existing.ErrorCount + count);
            }
            else
            {
                dict[key] = (tableId, tableName, count);
            }
        }

        private void UpdateRecommendation()
        {
            if (_validationResult == null) return;

            var recommendations = new List<string>();

            // 가장 많은 오류가 있는 단계 찾기 (데이터 레벨에서 이미 재분류됨)
            var stageErrors = new[]
            {
                ("테이블", _validationResult.TableCheckResult?.ErrorCount ?? 0),
                ("스키마", _validationResult.SchemaCheckResult?.ErrorCount ?? 0),
                ("지오메트리", _validationResult.GeometryCheckResult?.ErrorCount ?? 0),
                ("속성 관계", _validationResult.AttributeRelationCheckResult?.ErrorCount ?? 0),
                ("공간 관계", _validationResult.RelationCheckResult?.ErrorCount ?? 0)
            };

            var maxStage = stageErrors.OrderByDescending(s => s.Item2).First();
            if (maxStage.Item2 > 0)
            {
                var percentage = maxStage.Item2 * 100.0 / _validationResult.ErrorCount;
                recommendations.Add($"{maxStage.Item1} 검수에서 가장 많은 오류({maxStage.Item2}개, {percentage:F0}%)가 발견되었습니다.");
            }

            // 가장 많은 오류 타입
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                var results = _validationResult.GeometryCheckResult.GeometryResults;
                var overlapCount = results.Sum(r => r.OverlapCount);
                if (overlapCount > 0)
                {
                    recommendations.Add($"겹침 오류({overlapCount}개)를 우선 수정하세요.");
                }
            }

            // 가장 많은 오류가 있는 테이블
            var tableErrorsForRecommendation = new Dictionary<string, (string TableId, string TableName, int ErrorCount)>();

            // 실제 검수된 테이블 목록 수집 (각 검수 단계의 결과에서 실제 검수된 테이블만 수집)
            var validatedTablesForRecommendation = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // TableId로 실제 한글명을 조회하기 위한 Dictionary (대소문자 무시)
            var tableNameLookupForRecommendation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // 1단계: TableCheckResult에서 실제 존재하는 테이블 수집
            if (_validationResult.TableCheckResult?.TableResults != null)
            {
                foreach (var tableResult in _validationResult.TableCheckResult.TableResults)
                {
                    // 테이블이 실제로 존재하는 경우만 추가
                    if (tableResult.TableExistsCheck == "Y" || !string.IsNullOrEmpty(tableResult.ActualFeatureClassName))
                    {
                        // TableId와 ActualFeatureClassName 모두 추가 (대소문자 차이 대응)
                        if (!string.IsNullOrEmpty(tableResult.TableId))
                        {
                            validatedTablesForRecommendation.Add(tableResult.TableId);
                            // TableId로 한글명 조회 가능하도록 저장
                            if (!string.IsNullOrEmpty(tableResult.TableName))
                            {
                                tableNameLookupForRecommendation[tableResult.TableId] = tableResult.TableName;
                            }
                        }
                        if (!string.IsNullOrEmpty(tableResult.ActualFeatureClassName))
                        {
                            validatedTablesForRecommendation.Add(tableResult.ActualFeatureClassName);
                            // ActualFeatureClassName으로도 한글명 조회 가능하도록 저장
                            if (!string.IsNullOrEmpty(tableResult.TableName))
                            {
                                tableNameLookupForRecommendation[tableResult.ActualFeatureClassName] = tableResult.TableName;
                            }
                        }
                    }
                }
            }

            // 2단계: SchemaCheckResult에서 실제 검수된 테이블 수집
            if (_validationResult.SchemaCheckResult?.SchemaResults != null)
            {
                foreach (var schemaResult in _validationResult.SchemaCheckResult.SchemaResults)
                {
                    if (!string.IsNullOrEmpty(schemaResult.TableId))
                    {
                        validatedTablesForRecommendation.Add(schemaResult.TableId);
                    }
                }
            }

            // 3단계: GeometryCheckResult에서 실제 검수된 테이블 수집
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var geometryResult in _validationResult.GeometryCheckResult.GeometryResults)
                {
                    if (!string.IsNullOrEmpty(geometryResult.TableId))
                    {
                        validatedTablesForRecommendation.Add(geometryResult.TableId);
                        // GeometryResult의 TableName도 저장
                        if (!string.IsNullOrEmpty(geometryResult.TableName))
                        {
                            tableNameLookupForRecommendation[geometryResult.TableId] = geometryResult.TableName;
                        }
                    }
                }
            }

            // 1단계: 테이블 검수 오류
            if (_validationResult.TableCheckResult?.TableResults != null)
            {
                foreach (var tableResult in _validationResult.TableCheckResult.TableResults)
                {
                    if (tableResult.Errors.Any() && !string.IsNullOrEmpty(tableResult.TableId))
                    {
                        AddOrUpdateTableError(tableErrorsForRecommendation, tableResult.TableId, tableResult.TableId, tableResult.TableName, tableResult.Errors.Count);
                    }
                }
            }

            // 2단계: 스키마 검수 오류 (테이블별로 그룹화, 실제 검수된 테이블만 포함)
            if (_validationResult.SchemaCheckResult?.SchemaResults != null)
            {
                // 테이블별로 스키마 오류 개수 집계 (실제 검수된 테이블만)
                var schemaErrorsByTable = _validationResult.SchemaCheckResult.SchemaResults
                    .Where(s => !s.IsValid && !string.IsNullOrEmpty(s.TableId) && validatedTablesForRecommendation.Contains(s.TableId))
                    .GroupBy(s => s.TableId)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var kvp in schemaErrorsByTable)
                {
                    // 실제 한글명 조회 (없으면 TableId 사용)
                    var displayName = tableNameLookupForRecommendation.TryGetValue(kvp.Key, out var name) ? name : kvp.Key;
                    AddOrUpdateTableError(tableErrorsForRecommendation, kvp.Key, kvp.Key, displayName, kvp.Value);
                }
            }

            // 3단계: 지오메트리 검수 오류 (실제 검수된 테이블만 포함)
            if (_validationResult.GeometryCheckResult?.GeometryResults != null)
            {
                foreach (var result in _validationResult.GeometryCheckResult.GeometryResults)
                {
                    if (!string.IsNullOrEmpty(result.TableId) && validatedTablesForRecommendation.Contains(result.TableId))
                    {
                        var displayName = !string.IsNullOrEmpty(result.TableName) 
                            ? result.TableName 
                            : (tableNameLookupForRecommendation.TryGetValue(result.TableId, out var name) ? name : result.TableId);
                        AddOrUpdateTableError(tableErrorsForRecommendation, result.TableId, result.TableId, displayName, result.TotalErrorCount);
                    }
                }
            }

            // 4단계: 속성 관계 검수 오류 (SourceTable 우선 사용, 실제 검수된 테이블만 포함)
            if (_validationResult.AttributeRelationCheckResult?.Errors != null)
            {
                foreach (var error in _validationResult.AttributeRelationCheckResult.Errors)
                {
                    // SourceTable을 우선적으로 사용하고, 없으면 TableId 사용
                    var tableId = !string.IsNullOrEmpty(error.SourceTable) ? error.SourceTable : error.TableId;

                    // 실제 검수된 테이블만 통계에 포함
                    if (!string.IsNullOrEmpty(tableId) && validatedTablesForRecommendation.Contains(tableId))
                    {
                        // 실제 한글명 조회 (TableCheckResult/GeometryResult에서 가져온 한글명 우선, 없으면 error.TableName, 그것도 없으면 tableId)
                        var displayName = tableNameLookupForRecommendation.TryGetValue(tableId, out var name) 
                            ? name 
                            : (!string.IsNullOrEmpty(error.TableName) ? error.TableName : tableId);
                        AddOrUpdateTableError(tableErrorsForRecommendation, tableId, tableId, displayName, 1);
                    }
                }
            }

            // 5단계: 공간 관계 검수 오류 (SourceTable 우선 사용, 실제 검수된 테이블만 포함)
            if (_validationResult.RelationCheckResult?.Errors != null)
            {
                foreach (var error in _validationResult.RelationCheckResult.Errors)
                {
                    // SourceTable을 우선적으로 사용하고, 없으면 TableId 사용
                    var tableId = !string.IsNullOrEmpty(error.SourceTable) ? error.SourceTable : error.TableId;

                    // 실제 검수된 테이블만 통계에 포함
                    if (!string.IsNullOrEmpty(tableId) && validatedTablesForRecommendation.Contains(tableId))
                    {
                        // 실제 한글명 조회 (TableCheckResult/GeometryResult에서 가져온 한글명 우선, 없으면 error.TableName, 그것도 없으면 tableId)
                        var displayName = tableNameLookupForRecommendation.TryGetValue(tableId, out var name) 
                            ? name 
                            : (!string.IsNullOrEmpty(error.TableName) ? error.TableName : tableId);
                        AddOrUpdateTableError(tableErrorsForRecommendation, tableId, tableId, displayName, 1);
                    }
                }
            }

            if (tableErrorsForRecommendation.Any())
            {
                var maxTable = tableErrorsForRecommendation.Values.OrderByDescending(v => v.ErrorCount).First();
                if (maxTable.ErrorCount > 0)
                {
                    var displayName = string.IsNullOrEmpty(maxTable.TableId) 
                        ? maxTable.TableName 
                        : $"{maxTable.TableId}";
                    recommendations.Add($"{displayName} 레이어를 집중 검토하세요({maxTable.ErrorCount}개 오류).");
                }
            }

            RecommendationText.Text = recommendations.Any() 
                ? string.Join(" ", recommendations)
                : "모든 검수 항목이 정상입니다.";
        }

        private void AddOrUpdate(Dictionary<string, int> dict, string key, int count)
        {
            if (dict.ContainsKey(key))
                dict[key] += count;
            else
                dict[key] = count;
        }

        private Brush GetErrorColor(int errorCount)
        {
            if (errorCount == 0) return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // 초록
            if (errorCount < 10) return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // 연한 초록
            if (errorCount < 50) return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // 주황
            return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 빨강
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            return $"{ts.Seconds}초";
        }

        private string FormatFileSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            if (bytes >= GB)
                return $"{bytes / (double)GB:F2} GB";
            if (bytes >= MB)
                return $"{bytes / (double)MB:F2} MB";
            if (bytes >= KB)
                return $"{bytes / (double)KB:F2} KB";
            return $"{bytes} bytes";
        }

    }
}
