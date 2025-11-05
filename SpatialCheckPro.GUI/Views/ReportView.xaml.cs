#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.GUI.Views
{
    /// <summary>
    /// 보고서 생성 및 관리 화면
    /// </summary>
    public partial class ReportView : UserControl
    {
        private SpatialCheckPro.Models.ValidationResult? _currentValidationResult;
        private string? _lastGeneratedReportPath;
        private readonly PdfReportService? _pdfReportService;
        // Excel 보고서 기능 제거됨
        private readonly ILogger<ReportView>? _logger;

        public ReportView()
        {
            InitializeComponent();
            
            // 서비스 가져오기 (App.xaml.cs에서 등록된 서비스)
            try
            {
                var app = Application.Current as App;
                _pdfReportService = app?.GetService<PdfReportService>();
                _logger = app?.GetService<ILogger<ReportView>>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"보고서 서비스 초기화 실패: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 검수 결과를 설정합니다
        /// </summary>
        /// <param name="validationResult">검수 결과</param>
        public void SetValidationResult(SpatialCheckPro.Models.ValidationResult? validationResult)
        {
            _currentValidationResult = validationResult;
            UpdateUI();
        }

        /// <summary>
        /// UI 상태를 업데이트합니다
        /// </summary>
        private void UpdateUI()
        {
            // 수동 보고서 생성/내보내기 UI 제거됨

            if (_currentValidationResult == null)
            {
                // 검수 결과가 없을 때 안내 메시지 표시
                UpdateReportList();
            }
        }

        // 수동 보고서 생성 기능 제거됨

        // 수동 내보내기 기능 제거됨

        /// <summary>
        /// 선택된 보고서 형식을 가져옵니다
        /// </summary>
        private string GetSelectedFormat()
        {
            if (HtmlFormatRadio.IsChecked == true) return "HTML";
            if (PdfFormatRadio.IsChecked == true) return "PDF";
            return "HTML";
        }

        /// <summary>
        /// 파일 필터를 가져옵니다
        /// </summary>
        private string GetFileFilter()
        {
            string format = GetSelectedFormat();
            return format.ToLower() switch
            {
                "html" => "HTML 파일|*.html|모든 파일|*.*",
                "pdf" => "PDF 파일|*.pdf|모든 파일|*.*",
                _ => "모든 파일|*.*"
            };
        }

        /// <summary>
        /// 보고서 내용을 생성합니다
        /// </summary>
        private string GenerateReportContent()
        {
            if (_currentValidationResult == null) return "";

            var sb = new StringBuilder();
            
            // 보고서 헤더
            sb.AppendLine("=== 공간정보 검수 보고서 ===");
            sb.AppendLine($"생성일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"검수 대상: {Path.GetFileName(_currentValidationResult.TargetFile)}");
            sb.AppendLine();

            // 검수 요약
            if (IncludeSummaryCheck.IsChecked == true)
            {
                sb.AppendLine("## 검수 요약");
                sb.AppendLine($"검수 상태: {(_currentValidationResult.IsValid ? "성공" : "실패")}");
                sb.AppendLine($"총 오류: {_currentValidationResult.ErrorCount}개");
                sb.AppendLine($"총 경고: {_currentValidationResult.WarningCount}개");
                sb.AppendLine($"검수 시간: {_currentValidationResult.ProcessingTime.TotalSeconds:F1}초");
                sb.AppendLine();
            }

            // 1단계 테이블 검수 결과
            if (_currentValidationResult.TableCheckResult != null && _currentValidationResult.TableCheckResult.TableResults.Any())
            {
                sb.AppendLine("## 1단계 테이블 검수 결과");
                sb.AppendLine();
                sb.AppendLine("┌─────────────────────────────┬─────────────────────────────┬──────────────┬──────────────┬──────────────┐");
                sb.AppendLine("│ TableId                     │ TableName                   │ FeatureCount │ ExpectedType │ TypeMatch    │");
                sb.AppendLine("├─────────────────────────────┼─────────────────────────────┼──────────────┼──────────────┼──────────────┤");

                foreach (var table in _currentValidationResult.TableCheckResult.TableResults)
                {
                    var tableId = table.TableId.Length > 27 ? table.TableId.Substring(0, 24) + "..." : table.TableId;
                    var tableName = table.TableName.Length > 27 ? table.TableName.Substring(0, 24) + "..." : table.TableName;
                    var featureCount = table.FeatureCount?.ToString() ?? "null";
                    var featureType = table.FeatureType.Length > 12 ? table.FeatureType.Substring(0, 9) + "..." : table.FeatureType;
                    var featureTypeCheck = table.FeatureTypeCheck;

                    sb.AppendLine($"│ {tableId,-27} │ {tableName,-27} │ {featureCount,12} │ {featureType,-12} │ {featureTypeCheck,12} │");
                }

                sb.AppendLine("└─────────────────────────────┴─────────────────────────────┴──────────────┴──────────────┴──────────────┘");
                sb.AppendLine($"총 {_currentValidationResult.TableCheckResult.TotalTableCount}개 테이블 중 {_currentValidationResult.TableCheckResult.ProcessedTableCount}개 처리, {_currentValidationResult.TableCheckResult.SkippedTableCount}개 스킵");
                sb.AppendLine();
            }

            // 3단계 지오메트리 검수 결과
            if (_currentValidationResult.GeometryCheckResult != null && _currentValidationResult.GeometryCheckResult.GeometryResults != null && _currentValidationResult.GeometryCheckResult.GeometryResults.Any())
            {
                sb.AppendLine("## 3단계 지오메트리 검수 결과");
                sb.AppendLine("TableId,CheckType,TotalFeatures,ProcessedFeatures,ErrorFeatures,Result,Message");
                foreach (var g in _currentValidationResult.GeometryCheckResult.GeometryResults)
                {
                    sb.AppendLine($"{g.TableId},{g.CheckType},{g.TotalFeatureCount},{g.ProcessedFeatureCount},{g.TotalErrorCount},{g.ValidationStatus},{g.ErrorMessage}");
                }
                sb.AppendLine();
            }

            // 4단계 관계 검수 결과
            if (_currentValidationResult.RelationCheckResult != null)
            {
                var rel = _currentValidationResult.RelationCheckResult;
                sb.AppendLine("## 4단계 관계 검수 결과");
                sb.AppendLine($"검수 상태: {(rel.IsValid ? "성공" : "실패")}");
                sb.AppendLine($"처리 시간: {rel.ProcessingTime.TotalSeconds:F1}초");
                if (!string.IsNullOrWhiteSpace(rel.Message)) sb.AppendLine($"메시지: {rel.Message}");

                // 공간 관계 오류 요약 (REL_ 코드)
                if (rel.Errors != null && rel.Errors.Any())
                {
                    var spatial = rel.Errors.Where(e => !string.IsNullOrWhiteSpace(e.ErrorCode) && e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();
                    var attr = rel.Errors.Where(e => string.IsNullOrWhiteSpace(e.ErrorCode) || !e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();
                    sb.AppendLine($"공간 관계 오류: {spatial.Count}개");
                    sb.AppendLine($"속성 관계 오류: {attr.Count}개");

                    // 상세 - 공간 관계 오류 표
                    if (spatial.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("### 공간 관계 오류 상세");
                        sb.AppendLine("원본레이어,관계타입,오류유형,원본객체ID,메시지");
                        foreach (var e in spatial)
                        {
                            var srcLayer = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                            var relType = e.Metadata != null && e.Metadata.TryGetValue("RelationType", out var rt) ? Convert.ToString(rt) ?? string.Empty : string.Empty;
                            var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                            sb.AppendLine($"{srcLayer},{relType},{e.ErrorCode},{oid},\"{e.Message}\"");
                        }
                    }

                    // 상세 - 속성 관계 오류 표
                    if (attr.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("### 속성 관계 오류 상세");
                        sb.AppendLine("테이블명,필드명,규칙명,객체ID,기대값,실제값,메시지");
                        foreach (var e in attr)
                        {
                            var table = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                            var field = e.FieldName ?? (e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty);
                            var rule = string.IsNullOrWhiteSpace(e.ErrorCode) ? (e.Metadata != null && e.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : e.ErrorCode;
                            var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                            var expected = e.ExpectedValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Expected", out var exv) ? Convert.ToString(exv) ?? string.Empty : string.Empty);
                            var actual = e.ActualValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Actual", out var acv) ? Convert.ToString(acv) ?? string.Empty : string.Empty);
                            sb.AppendLine($"{table},{field},{rule},{oid},\"{expected}\",\"{actual}\",\"{e.Message}\"");
                        }
                    }
                }
                sb.AppendLine();
            }

            // 상세 결과
            if (IncludeDetailsCheck.IsChecked == true)
            {
                sb.AppendLine("## 상세 검수 결과");
                sb.AppendLine($"검수 ID: {_currentValidationResult.ValidationId}");
                sb.AppendLine($"시작 시간: {_currentValidationResult.StartedAt:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"완료 시간: {_currentValidationResult.CompletedAt:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"메시지: {_currentValidationResult.Message}");
                sb.AppendLine();
            }

            // 오류 목록
            if (IncludeErrorsCheck.IsChecked == true && _currentValidationResult.ErrorCount > 0)
            {
                sb.AppendLine("## 발견된 오류");
                sb.AppendLine("- 테이블 구조 검증 과정에서 발견된 문제들");
                sb.AppendLine("- 상세한 오류 정보는 검수 로그를 참조하세요");
                sb.AppendLine();
            }

            // 경고 목록
            if (IncludeWarningsCheck.IsChecked == true && _currentValidationResult.WarningCount > 0)
            {
                sb.AppendLine("## 경고 사항");
                sb.AppendLine("- 검수 과정에서 발견된 주의사항들");
                sb.AppendLine("- 데이터 품질 개선을 위한 권장사항");
                sb.AppendLine();
            }

            // 메타데이터
            if (IncludeMetadataCheck.IsChecked == true)
            {
                sb.AppendLine("## 파일 메타데이터");
                sb.AppendLine($"파일 경로: {_currentValidationResult.TargetFile}");
                
                if (File.Exists(_currentValidationResult.TargetFile))
                {
                    var fileInfo = new FileInfo(_currentValidationResult.TargetFile);
                    sb.AppendLine($"파일 크기: {fileInfo.Length:N0} bytes");
                    sb.AppendLine($"수정일: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                }
                else if (Directory.Exists(_currentValidationResult.TargetFile))
                {
                    var dirInfo = new DirectoryInfo(_currentValidationResult.TargetFile);
                    var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                    sb.AppendLine($"포함된 파일 수: {files.Length}개");
                    sb.AppendLine($"수정일: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// HTML 보고서를 생성합니다
        /// </summary>
        public string GenerateHtmlReport()
        {
            if (_currentValidationResult == null) return "";

            var html = new StringBuilder();
            
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang='ko'>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='UTF-8'>");
            html.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.AppendLine("    <title>공간정보 검수 보고서</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        :root { --primary:#3B82F6; --muted:#6B7280; --border:#E5E7EB; --bg:#F8FAFC; --header:#F3F4F6; }");
            html.AppendLine("        *{box-sizing:border-box}");
            html.AppendLine("        body { font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; background: var(--bg); }");
            html.AppendLine("        .container { max-width: 1000px; margin: 0 auto; background: white; padding: 40px; border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }");
            html.AppendLine("        h1 { color: #1f2937; border-bottom: 3px solid #3b82f6; padding-bottom: 10px; }");
            html.AppendLine("        h2 { color: #374151; margin-top: 30px; }");
            html.AppendLine("        .summary { background: #f0f9ff; padding: 20px; border-radius: 8px; margin: 20px 0; }");
            html.AppendLine("        .success { color: #10b981; font-weight: bold; }");
            html.AppendLine("        .error { color: #ef4444; font-weight: bold; }");
            html.AppendLine("        .info-grid { display: grid; grid-template-columns: repeat(4, minmax(0,1fr)); gap: 16px; margin: 20px 0; }");
            html.AppendLine("        .info-item { background: #f8fafc; padding: 15px; border-radius: 6px; }");
            html.AppendLine("        .label { font-weight: 600; color: #6b7280; }");
            html.AppendLine("        .value { font-size: 18px; color: #1f2937; margin-top: 5px; }");
            html.AppendLine("        .toolbar { display:flex; gap:12px; align-items:center; position:sticky; top:0; background:white; padding:8px 0; z-index:5; border-bottom:1px solid var(--border);} ");
            html.AppendLine("        .search { padding:8px 10px; border:1px solid var(--border); border-radius:8px; min-width:220px; }");
            html.AppendLine("        .muted{ color: var(--muted);} ");
            html.AppendLine("        .table-wrap{ overflow:auto; max-height:480px; border:1px solid var(--border); border-radius:8px;} ");
            html.AppendLine("        .table-results { width: 100%; border-collapse: collapse; font-size: 14px; position:relative; }");
            html.AppendLine("        .table-results thead th { position: sticky; top: 0; background: var(--header); z-index: 2; }");
            html.AppendLine("        .table-results th, .table-results td { border: 1px solid #d1d5db; padding: 8px; text-align: left; white-space:nowrap; }");
            html.AppendLine("        .table-results tr:nth-child(even) { background-color: #f9fafb; }");
            html.AppendLine("        .th-sort { cursor:pointer; user-select:none; }");
            html.AppendLine("        details{ border:1px solid var(--border); border-radius:8px; padding:12px; margin:16px 0;}");
            html.AppendLine("        summary{ font-weight:600; cursor:pointer; }");
            html.AppendLine("    </style>");
            html.AppendLine("    <script>");
            html.AppendLine("      function sortTable(tableId, colIdx, type){ const tb=document.getElementById(tableId); if(!tb) return; const tbody=tb.tBodies[0]; const rows=[...tbody.rows]; const dir=tb.getAttribute('data-sort-dir')==='asc'?'desc':'asc'; tb.setAttribute('data-sort-dir',dir); const parse=(v)=>{ if(type==='num') return parseFloat(v)||0; return v.toString(); }; rows.sort((a,b)=>{ const A=parse(a.cells[colIdx].innerText.trim()); const B=parse(b.cells[colIdx].innerText.trim()); if(A<B) return dir==='asc'?-1:1; if(A>B) return dir==='asc'?1:-1; return 0;}); rows.forEach(r=>tbody.appendChild(r)); }");
            html.AppendLine("      function filterTable(tableId, q){ q=q.toLowerCase(); const tb=document.getElementById(tableId); if(!tb) return; const rows=tb.tBodies[0].rows; for(let i=0;i<rows.length;i++){ const text=rows[i].innerText.toLowerCase(); rows[i].style.display = text.includes(q)?'':'none'; } }");
            html.AppendLine("    </script>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class='container'>");
            
            // 제목
            html.AppendLine("        <h1>🗺️ 공간정보 검수 보고서</h1>");
            html.AppendLine($"        <p><strong>생성일시:</strong> {DateTime.Now:yyyy년 MM월 dd일 HH:mm:ss}</p>");
            html.AppendLine($"        <p><strong>검수 대상:</strong> {Path.GetFileName(_currentValidationResult.TargetFile)}</p>");
            
            // 파일 메타데이터 (옵션)
            if (IncludeMetadataCheck.IsChecked == true)
            {
                try
                {
                    html.AppendLine("        <details open><summary>📂 파일 메타데이터</summary>");
                    html.AppendLine("        <div class='table-wrap'>");
                    html.AppendLine("        <table class='table-results'>");
                    html.AppendLine("            <tbody>");
                    if (File.Exists(_currentValidationResult.TargetFile))
                    {
                        var fi = new FileInfo(_currentValidationResult.TargetFile);
                        html.AppendLine($"                <tr><th>파일 크기</th><td>{fi.Length:N0} bytes</td></tr>");
                        html.AppendLine($"                <tr><th>생성일</th><td>{fi.CreationTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
                        html.AppendLine($"                <tr><th>수정일</th><td>{fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
                    }
                    else if (Directory.Exists(_currentValidationResult.TargetFile))
                    {
                        var di = new DirectoryInfo(_currentValidationResult.TargetFile);
                        var files = di.GetFiles("*", SearchOption.AllDirectories);
                        html.AppendLine($"                <tr><th>포함된 파일 수</th><td>{files.Length}개</td></tr>");
                        html.AppendLine($"                <tr><th>수정일</th><td>{di.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
                    }
                    html.AppendLine("            </tbody>");
                    html.AppendLine("        </table>");
                    html.AppendLine("        </div>");
                    html.AppendLine("        </details>");
                }
                catch { /* 안전 폴백 */ }
            }
            
            // 검수 요약
            if (IncludeSummaryCheck.IsChecked == true)
            {
                html.AppendLine("        <div class='summary'>");
                html.AppendLine("            <h2>📊 검수 요약</h2>");
                html.AppendLine("            <div class='info-grid'>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>검수 상태</div>");
                html.AppendLine($"                    <div class='value {(_currentValidationResult.IsValid ? "success" : "error")}'>");
                html.AppendLine($"                        {(_currentValidationResult.IsValid ? "✅ 성공" : "❌ 실패")}");
                html.AppendLine("                    </div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>검수 시간</div>");
                html.AppendLine($"                    <div class='value'>{_currentValidationResult.ProcessingTime.TotalSeconds:F1}초</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>총 오류</div>");
                html.AppendLine($"                    <div class='value error'>{_currentValidationResult.ErrorCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>총 경고</div>");
                html.AppendLine($"                    <div class='value' style='color: #f59e0b;'>{_currentValidationResult.WarningCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }

            // 대시보드 섹션 (요약 바로 아래) - 이미지와 유사한 4열 카드 행 구성
            html.AppendLine("        <section style='margin:8px 0 24px 0'>");
            html.AppendLine("          <h2>📈 검수 결과 대시보드</h2>");
            html.AppendLine("          <p class='muted'>검수 결과 요약을 한눈에 확인하세요</p>");
            html.AppendLine("          <div class='info-grid'>");
            // 0단계: FileGDB 완전성 검수 결과
            if (_currentValidationResult.FileGdbCheckResult != null)
            {
                var f0 = _currentValidationResult.FileGdbCheckResult;
                html.AppendLine("        <h2>🧰 0단계 FileGDB 완전성 검수</h2>");
                html.AppendLine("        <div class='summary'>");
                html.AppendLine($"            <div><span class='label'>검수 상태</span> <span class='value'>{f0.Status}</span></div>");
                // CheckResult에는 Message 필드가 없으므로 메타데이터나 상태만 노출
                html.AppendLine("        </div>");
                
                // 오류/경고 상세 (옵션 적용)
                var includeErrors = IncludeErrorsCheck.IsChecked == true;
                var includeWarnings = IncludeWarningsCheck.IsChecked == true;
                if (includeErrors && f0.Errors != null && f0.Errors.Any())
                {
                    html.AppendLine("        <details open><summary>0단계 오류 상세</summary>");
                    html.AppendLine("        <div class='table-wrap'>");
                    html.AppendLine("        <table class='table-results'><thead><tr><th>테이블명</th><th>객체ID</th><th>오류코드</th><th>메시지</th></tr></thead><tbody>");
                    foreach (var e in f0.Errors)
                    {
                        html.AppendLine("            <tr>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.TableName ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.FeatureId ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.ErrorCode ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.Message ?? string.Empty)}</td>");
                        html.AppendLine("            </tr>");
                    }
                    html.AppendLine("        </tbody></table></div></details>");
                }
                if (includeWarnings && f0.Warnings != null && f0.Warnings.Any())
                {
                    html.AppendLine("        <details><summary>0단계 경고 상세</summary>");
                    html.AppendLine("        <div class='table-wrap'>");
                    html.AppendLine("        <table class='table-results'><thead><tr><th>테이블명</th><th>객체ID</th><th>코드</th><th>메시지</th></tr></thead><tbody>");
                    foreach (var w in f0.Warnings)
                    {
                        html.AppendLine("            <tr>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(w.TableName ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(w.FeatureId ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(w.ErrorCode ?? string.Empty)}</td>");
                        html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(w.Message ?? string.Empty)}</td>");
                        html.AppendLine("            </tr>");
                    }
                    html.AppendLine("        </tbody></table></div></details>");
                }
            }

            // 1단계
            if (_currentValidationResult.TableCheckResult != null)
            {
                var s1 = _currentValidationResult.TableCheckResult;
                var total = s1.TotalTableCount;
                var missing = s1.TableResults?.Count(t => t.TableExistsCheck == "N") ?? 0;
                var undefined = s1.TableResults?.Count(t => (t.ExpectedFeatureType?.Trim() ?? "") == "정의되지 않음") ?? 0;
                var zero = s1.TableResults?.Count(t => t.TableExistsCheck == "Y" && (t.FeatureCount ?? 0) == 0) ?? 0;
                html.AppendLine("            <div class='info-item' style='grid-column:1/-1;background:#EFF6FF;border:1px solid #93c5fd'><div class='label'>[1단계] 테이블 검수</div><div class='value' style='color:#2563EB;font-size:14px'>정의된/누락/미정의/객체 0</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #93c5fd'><div class='label'>정의된 테이블</div><div class='value' style='color:#2563EB'>" + total + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>정의된 테이블 누락 수</div><div class='value error'>" + missing + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>정의되지 않은 테이블 수</div><div class='value error'>" + undefined + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fde68a'><div class='label'>객체가 없는 테이블 수</div><div class='value' style='color:#b45309'>" + zero + "</div></div>");
            }
            // 2단계
            if (_currentValidationResult.SchemaCheckResult != null)
            {
                var s2 = _currentValidationResult.SchemaCheckResult;
                var cols = s2.TotalColumnCount == 0 ? (s2.SchemaResults?.Count ?? 0) : s2.TotalColumnCount;
                html.AppendLine("            <div class='info-item' style='grid-column:1/-1;background:#EFF6FF;border:1px solid #93c5fd'><div class='label'>[2단계] 스키마 검수</div><div class='value' style='color:#2563EB;font-size:14px'>총 컬럼/오류/경고</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #93c5fd'><div class='label'>총 컬럼 검사</div><div class='value' style='color:#2563EB'>" + cols + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>오류</div><div class='value error'>" + s2.ErrorCount + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fde68a'><div class='label'>경고</div><div class='value' style='color:#b45309'>" + s2.WarningCount + "</div></div>");
            }
            // 3단계
            if (_currentValidationResult.GeometryCheckResult != null)
            {
                var s3 = _currentValidationResult.GeometryCheckResult;
                var tableCount = s3.TotalTableCount;
                var errorSum = s3.ErrorCount;
                var warnSum = s3.WarningCount;
                html.AppendLine("            <div class='info-item' style='grid-column:1/-1;background:#EFF6FF;border:1px solid #93c5fd'><div class='label'>[3단계] 지오메트리 검수</div><div class='value' style='color:#2563EB;font-size:14px'>검사 테이블/오류 합계/경고 합계</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #93c5fd'><div class='label'>검사 테이블</div><div class='value' style='color:#2563EB'>" + tableCount + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>오류 합계</div><div class='value error'>" + errorSum + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fde68a'><div class='label'>경고 합계</div><div class='value' style='color:#b45309'>" + warnSum + "</div></div>");
            }
            // 4단계
            if (_currentValidationResult.AttributeRelationCheckResult != null)
            {
                var s5 = _currentValidationResult.AttributeRelationCheckResult;
                html.AppendLine("            <div class='info-item' style='grid-column:1/-1;background:#EFF6FF;border:1px solid #93c5fd'><div class='label'>[4단계] 속성 관계 검수</div><div class='value' style='color:#2563EB;font-size:14px'>검사된 규칙/오류/경고</div></div>");
                // 규칙 수 산정: TotalCount가 비어 있을 수 있어 5단계 메타데이터 또는 4단계 메타데이터 폴백 사용
                int ruleCount5 = 0;
                try
                {
                    if (s5.TotalCount > 0) ruleCount5 = s5.TotalCount;
                    else if (s5.Metadata != null && s5.Metadata.TryGetValue("RuleCount", out var r5))
                    {
                        if (r5 is int i5) ruleCount5 = i5;
                        else if (r5 is long l5) ruleCount5 = (int)l5;
                        else if (r5 is string s5s && int.TryParse(s5s, out var parsed5)) ruleCount5 = parsed5;
                    }
                    else if (_currentValidationResult.RelationCheckResult != null &&
                             _currentValidationResult.RelationCheckResult.Metadata != null &&
                             _currentValidationResult.RelationCheckResult.Metadata.TryGetValue("AttributeRuleCount", out var cntObj))
                    {
                        if (cntObj is int i) ruleCount5 = i;
                        else if (cntObj is long l) ruleCount5 = (int)l;
                        else if (cntObj is string s && int.TryParse(s, out var parsed)) ruleCount5 = parsed;
                    }
                }
                catch { /* 안전 폴백 */ }
                html.AppendLine("            <div class='info-item' style='border:1px solid #93c5fd'><div class='label'>검사된 규칙</div><div class='value' style='color:#2563EB'>" + (ruleCount5) + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>속성 관계 오류</div><div class='value error'>" + s5.ErrorCount + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fde68a'><div class='label'>경고 합계</div><div class='value' style='color:#b45309'>" + s5.WarningCount + "</div></div>");
            }
            html.AppendLine("          </div>");
            html.AppendLine("        </section>");
            // 5단계
            if (_currentValidationResult.RelationCheckResult != null)
            {
                var s4 = _currentValidationResult.RelationCheckResult;
                html.AppendLine("            <div class='info-item' style='grid-column:1/-1;background:#EFF6FF;border:1px solid #93c5fd'><div class='label'>[5단계] 공간 관계 검수</div><div class='value' style='color:#2563EB;font-size:14px'>검사된 규칙/오류/경고</div></div>");
                // 규칙 수 산정: TotalCount가 비어 있을 수 있어 메타데이터(SpatialRuleCount) 폴백 사용
                int ruleCount4 = 0;
                try
                {
                    if (s4.TotalCount > 0) ruleCount4 = s4.TotalCount;
                    else if (s4.Metadata != null && s4.Metadata.TryGetValue("SpatialRuleCount", out var sr))
                    {
                        if (sr is int i) ruleCount4 = i;
                        else if (sr is long l) ruleCount4 = (int)l;
                        else if (sr is string s && int.TryParse(s, out var parsed)) ruleCount4 = parsed;
                    }
                }
                catch { /* 안전 폴백 */ }
                html.AppendLine("            <div class='info-item' style='border:1px solid #93c5fd'><div class='label'>검사된 규칙</div><div class='value' style='color:#2563EB'>" + (ruleCount4) + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fca5a5'><div class='label'>공간 관계 오류</div><div class='value error'>" + s4.ErrorCount + "</div></div>");
                html.AppendLine("            <div class='info-item' style='border:1px solid #fde68a'><div class='label'>경고 합계</div><div class='value' style='color:#b45309'>" + s4.WarningCount + "</div></div>");
            }
            html.AppendLine("          </div>");
            html.AppendLine("        </section>");

            // 1단계 테이블 검수 결과
            if (_currentValidationResult.TableCheckResult != null && _currentValidationResult.TableCheckResult.TableResults.Any())
            {
                html.AppendLine("        <h2>📊 1단계 테이블 검수 결과</h2>");
                html.AppendLine("        <div class='toolbar'>");
                html.AppendLine("          <input class='search' placeholder='검색(테이블/타입/상태)' oninput=\"filterTable('tbl-stage1',this.value)\">");
                html.AppendLine("          <span class='muted'>헤더 클릭시 정렬</span>");
                html.AppendLine("        </div>");
                html.AppendLine("        <details open><summary>테이블 결과 표 보기/접기</summary>");
                html.AppendLine("        <div class='table-wrap'>");
                html.AppendLine("        <table id='tbl-stage1' class='table-results' data-sort-dir='asc'>");
                html.AppendLine("            <thead>");
                html.AppendLine("                <tr>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',0,'text')\">TableId</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',1,'text')\">TableName</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',2,'num')\">FeatureCount</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',3,'text')\">ExpectedType</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',4,'text')\">ActualType</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',5,'text')\">TypeMatch</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage1',6,'text')\">ActualFeatureClass</th>");
                html.AppendLine("                </tr>");
                html.AppendLine("            </thead>");
                html.AppendLine("            <tbody>");
                
                foreach (var table in _currentValidationResult.TableCheckResult.TableResults)
                {
                    var statusClass = table.FeatureTypeCheck == "Y" ? "status-y" : "status-n";
                    html.AppendLine("                <tr>");
                    html.AppendLine($"                    <td>{table.TableId}</td>");
                    html.AppendLine($"                    <td>{table.TableName}</td>");
                    html.AppendLine($"                    <td style='text-align: right;'>{(table.FeatureCount?.ToString("N0") ?? "null")}</td>");
                    html.AppendLine($"                    <td class='feature-type'>{table.FeatureType}</td>");
                    html.AppendLine($"                    <td class='feature-type'>{table.ActualFeatureType}</td>");
                    html.AppendLine($"                    <td class='{statusClass}'>{table.FeatureTypeCheck}</td>");
                    html.AppendLine($"                    <td style='font-family: monospace; font-size: 12px;'>{table.ActualFeatureClassName}</td>");
                    html.AppendLine("                </tr>");
                }
                
                html.AppendLine("            </tbody>");
                html.AppendLine("        </table>");
                html.AppendLine("        </div>");
                html.AppendLine("        </details>");
                
                // 통계 정보 추가
                var totalCount = _currentValidationResult.TableCheckResult.TableResults.Count;
                var processedCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.IsProcessed);
                var skippedCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => !t.IsProcessed);
                var matchedTypeCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.FeatureTypeCheck == "Y");
                var mismatchedTypeCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.FeatureTypeCheck == "N" && t.IsProcessed);
                
                html.AppendLine("        <div class='summary'>");
                html.AppendLine("            <h3>📈 테이블 검수 통계</h3>");
                html.AppendLine("            <div class='info-grid'>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>총 테이블 수</div>");
                html.AppendLine($"                    <div class='value'>{totalCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>처리된 테이블</div>");
                html.AppendLine($"                    <div class='value success'>{processedCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>스킵된 테이블</div>");
                html.AppendLine($"                    <div class='value' style='color: #f59e0b;'>{skippedCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>피처타입 일치</div>");
                html.AppendLine($"                    <div class='value success'>{matchedTypeCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>피처타입 불일치</div>");
                html.AppendLine($"                    <div class='value error'>{mismatchedTypeCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }

            // 2단계 스키마 검수 결과
            if (_currentValidationResult.SchemaCheckResult != null && _currentValidationResult.SchemaCheckResult.SchemaResults.Any())
            {
                html.AppendLine("        <h2>🔍 2단계 스키마 검수 결과</h2>");
                html.AppendLine("        <style>");
                html.AppendLine("            .status-warning { color: #f59e0b; font-weight: bold; }");
                html.AppendLine("        </style>");
                html.AppendLine("        <details open><summary>스키마 결과 표 보기/접기</summary>");
                html.AppendLine("        <div class='table-wrap'>");
                html.AppendLine("        <table id='tbl-stage2' class='table-results' data-sort-dir='asc'>");
                html.AppendLine("            <thead>");
                html.AppendLine("                <tr>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',0,'text')\">TableId</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',1,'text')\">FieldName</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',2,'text')\">FieldAlias</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',3,'text')\">ExpectedType</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',4,'text')\">ActualType</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',5,'text')\">LengthMatch</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',6,'text')\">NotNullMatch</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage2',7,'text')\">Result</th>");
                html.AppendLine("                </tr>");
                html.AppendLine("            </thead>");
                html.AppendLine("            <tbody>");
                
                foreach (var schema in _currentValidationResult.SchemaCheckResult.SchemaResults)
                {
                    var resultClass = schema.IsValid ? "status-y" : (schema.Errors.Any() ? "status-n" : "status-warning");
                    var resultText = schema.IsValid ? "통과" : (schema.Errors.Any() ? "실패" : "경고");
                    
                    html.AppendLine("                <tr>");
                    html.AppendLine($"                    <td>{schema.TableId}</td>");
                    html.AppendLine($"                    <td style='font-family: monospace;'>{schema.ColumnName}</td>");
                    html.AppendLine($"                    <td>{schema.ColumnKoreanName}</td>");
                    html.AppendLine($"                    <td class='feature-type'>{schema.ExpectedDataType}</td>");
                    html.AppendLine($"                    <td class='feature-type'>{schema.ActualDataType}</td>");
                    html.AppendLine($"                    <td class='{(schema.LengthMatches ? "status-y" : "status-n")}'>{(schema.LengthMatches ? "Y" : "N")}</td>");
                    html.AppendLine($"                    <td class='{(schema.NotNullMatches ? "status-y" : "status-n")}'>{(schema.NotNullMatches ? "Y" : "N")}</td>");
                    html.AppendLine($"                    <td class='{resultClass}'>{resultText}</td>");
                    html.AppendLine("                </tr>");
                }
                
                html.AppendLine("            </tbody>");
                html.AppendLine("        </table>");
                html.AppendLine("        </div>");
                html.AppendLine("        </details>");
                
                // 스키마 검수 통계 정보
                var schemaTotalCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count;
                var schemaProcessedCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.IsProcessed);
                var schemaSkippedCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => !s.IsProcessed);
                var schemaValidCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.IsValid);
                var schemaErrorCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.Errors.Any());
                var schemaWarningCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.Warnings.Any() && !s.Errors.Any());
                
                html.AppendLine("        <div class='summary'>");
                html.AppendLine("            <h3>📈 스키마 검수 통계</h3>");
                html.AppendLine("            <div class='info-grid'>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>총 컬럼 수</div>");
                html.AppendLine($"                    <div class='value'>{schemaTotalCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>처리된 컬럼</div>");
                html.AppendLine($"                    <div class='value success'>{schemaProcessedCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>스킵된 컬럼</div>");
                html.AppendLine($"                    <div class='value' style='color: #f59e0b;'>{schemaSkippedCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>검수 통과</div>");
                html.AppendLine($"                    <div class='value success'>{schemaValidCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>오류</div>");
                html.AppendLine($"                    <div class='value error'>{schemaErrorCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("                <div class='info-item'>");
                html.AppendLine("                    <div class='label'>경고</div>");
                html.AppendLine($"                    <div class='value' style='color: #f59e0b;'>{schemaWarningCount}개</div>");
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }

            // 3단계 지오메트리 검수 결과
            if (_currentValidationResult.GeometryCheckResult != null && _currentValidationResult.GeometryCheckResult.GeometryResults != null && _currentValidationResult.GeometryCheckResult.GeometryResults.Any())
            {
                html.AppendLine("        <h2>🧭 3단계 지오메트리 검수 결과</h2>");
                html.AppendLine("        <details open><summary>지오메트리 결과 표 보기/접기</summary>");
                html.AppendLine("        <div class='toolbar'>");
                html.AppendLine("          <input class='search' placeholder='검색(테이블/항목/메시지)' oninput=\"filterTable('tbl-stage3',this.value)\">");
                html.AppendLine("          <span class='muted'>헤더 클릭시 정렬</span>");
                html.AppendLine("        </div>");
                html.AppendLine("        <div class='table-wrap'>");
                html.AppendLine("        <table id='tbl-stage3' class='table-results' data-sort-dir='asc'>");
                html.AppendLine("            <thead>");
                html.AppendLine("                <tr>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',0,'text')\">TableId</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',1,'text')\">CheckType</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',2,'num')\">TotalFeatures</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',3,'num')\">ProcessedFeatures</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',4,'num')\">ErrorFeatures</th>");
                html.AppendLine("                    <th class='th-sort' onclick=\"sortTable('tbl-stage3',5,'text')\">Result</th>");
                html.AppendLine("                    <th>Message</th>");
                html.AppendLine("                </tr>");
                html.AppendLine("            </thead>");
                html.AppendLine("            <tbody>");
                foreach (var g in _currentValidationResult.GeometryCheckResult.GeometryResults)
                {
                    html.AppendLine("                <tr>");
                    html.AppendLine($"                    <td>{g.TableId}</td>");
                    html.AppendLine($"                    <td>{g.CheckType}</td>");
                    html.AppendLine($"                    <td style='text-align:right'>{g.TotalFeatureCount}</td>");
                    html.AppendLine($"                    <td style='text-align:right'>{g.ProcessedFeatureCount}</td>");
                    html.AppendLine($"                    <td style='text-align:right'>{g.TotalErrorCount}</td>");
                    html.AppendLine($"                    <td>{g.ValidationStatus}</td>");
                    html.AppendLine($"                    <td>{System.Net.WebUtility.HtmlEncode(g.ErrorMessage ?? string.Empty)}</td>");
                    html.AppendLine("                </tr>");
                }
                html.AppendLine("            </tbody>");
                html.AppendLine("        </table>");
                html.AppendLine("        </div>");
                html.AppendLine("        </details>");
            }

            // 4단계 속성 관계 검수 결과 (요약 + 상세 그리드)
            if (_currentValidationResult.AttributeRelationCheckResult != null)
            {
                var attrStage = _currentValidationResult.AttributeRelationCheckResult;
                html.AppendLine("        <h2>🧩 4단계 속성 관계 검수 결과</h2>");
                html.AppendLine("        <div class='summary'>");
                html.AppendLine($"            <div><span class='label'>검수 상태</span> <span class='value'>{(attrStage.IsValid ? "성공" : "실패")}</span></div>");
                html.AppendLine($"            <div style='margin-top:8px'><span class='label'>처리 시간</span> <span class='value'>{attrStage.ProcessingTime.TotalSeconds:F1}초</span></div>");
                if (!string.IsNullOrWhiteSpace(attrStage.Message))
                {
                    html.AppendLine($"            <div style='margin-top:8px'><span class='label'>메시지</span> <span class='value'>{System.Net.WebUtility.HtmlEncode(attrStage.Message)}</span></div>");
                }
                html.AppendLine("        </div>");
                
                // 검사된 규칙 수
                try
                {
                    var ruleCount = 0;
                    if (attrStage.ProcessedRulesCount > 0)
                    {
                        ruleCount = attrStage.ProcessedRulesCount;
                    }
                    else
                    {
                        // 설정 파일에서 규칙 수를 추정(경로를 알 수 없으므로 결과 내 Errors/Warnings 기준 보정)
                        ruleCount = Math.Max(
                            Math.Max(attrStage.Errors?.Count ?? 0, 0),
                            Math.Max(attrStage.Warnings?.Count ?? 0, 0));
                    }
                    html.AppendLine($"        <div class='info-grid' style='margin:8px 0 12px 0'>");
                    html.AppendLine($"            <div class='info-item'><div class='label'>검사된 규칙</div><div class='value'>{ruleCount}</div></div>");
                    html.AppendLine($"            <div class='info-item'><div class='label'>속성 관계 오류</div><div class='value error'>{attrStage.ErrorCount}</div></div>");
                    html.AppendLine($"            <div class='info-item'><div class='label'>경고 합계</div><div class='value' style='color:#b45309'>{attrStage.WarningCount}</div></div>");
                    html.AppendLine($"        </div>");
                }
                catch { /* 안전 폴백 */ }

                // 상세 그리드 (옵션 반영)
                if ((attrStage.Errors != null && attrStage.Errors.Any()) || (attrStage.Warnings != null && attrStage.Warnings.Any()))
                {
                    var includeErrors = IncludeErrorsCheck.IsChecked == true;
                    var includeWarnings = IncludeWarningsCheck.IsChecked == true;
                    var allAttr = new System.Collections.Generic.List<SpatialCheckPro.Models.ValidationError>();
                    if (includeErrors && attrStage.Errors != null) allAttr.AddRange(attrStage.Errors);
                    if (includeWarnings && attrStage.Warnings != null) allAttr.AddRange(attrStage.Warnings);

                    html.AppendLine("        <details open><summary>속성 관계 상세 표 보기/접기</summary>");
                    html.AppendLine("        <div class='toolbar'>");
                    html.AppendLine("          <input class='search' placeholder='검색(테이블/필드/메시지)' oninput=\"filterTable('tbl-stage4',this.value)\">");
                    html.AppendLine("          <span class='muted'>헤더 클릭시 정렬</span>");
                    html.AppendLine("        </div>");
                    html.AppendLine("        <div class='table-wrap'>");
                    html.AppendLine("        <table id='tbl-stage4' class='table-results' data-sort-dir='asc'>");
                    html.AppendLine("            <thead><tr>");
                    html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage4',0,'text')\">테이블명</th>");
                    html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage4',1,'text')\">필드명</th>");
                    html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage4',2,'text')\">오류코드/규칙</th>");
                    html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage4',3,'num')\">객체ID</th>");
                    html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage4',4,'text')\">메시지</th>");
                    html.AppendLine("            </tr></thead>");
                    html.AppendLine("            <tbody>");
                    foreach (var e in allAttr)
                    {
                        var tableName = string.IsNullOrWhiteSpace(e.TableName) ? (e.TableId ?? string.Empty) : e.TableName;
                        var field = e.FieldName ?? (e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty);
                        var rule = string.IsNullOrWhiteSpace(e.ErrorCode) ? (e.Metadata != null && e.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : e.ErrorCode;
                        var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                        html.AppendLine("                <tr>");
                        html.AppendLine($"                    <td>{tableName}</td>");
                        html.AppendLine($"                    <td>{field}</td>");
                        html.AppendLine($"                    <td>{rule}</td>");
                        html.AppendLine($"                    <td>{oid}</td>");
                        html.AppendLine($"                    <td>{System.Net.WebUtility.HtmlEncode(e.Message ?? string.Empty)}</td>");
                        html.AppendLine("                </tr>");
                    }
                    html.AppendLine("            </tbody>");
                    html.AppendLine("        </table>");
                    html.AppendLine("        </div>");
                    html.AppendLine("        </details>");
                }
            }
            
            // 5단계 공간 관계 검수 결과
            if (_currentValidationResult.RelationCheckResult != null)
            {
                var rel = _currentValidationResult.RelationCheckResult;
                html.AppendLine("        <h2>🔗 5단계 공간 관계 검수 결과</h2>");
                // 3칸 요약 카드
                html.AppendLine("        <div class='info-grid' style='margin:8px 0 12px 0'>");
                // 검사된 규칙 수 산정: RelationConfigs 수를 결과에서 직접 알 수 없으므로 오류 개수와 상태를 보정 값으로 사용
                var processedRules = rel.ProcessedRulesCount > 0 ? rel.ProcessedRulesCount : Math.Max(1, rel.Errors?.Select(e => e.Metadata != null && e.Metadata.ContainsKey("RuleId") ? e.Metadata["RuleId"] : null).Distinct().Count() ?? 1);
                html.AppendLine($"            <div class='info-item'><div class='label'>검사된 규칙</div><div class='value'>{processedRules}</div></div>");
                html.AppendLine($"            <div class='info-item'><div class='label'>공간 관계 오류</div><div class='value error'>{rel.ErrorCount}</div></div>");
                html.AppendLine($"            <div class='info-item'><div class='label'>검수 상태</div><div class='value'>{(rel.IsValid ? "성공" : "실패")}</div></div>");
                html.AppendLine("        </div>");

                if (rel.Errors != null && rel.Errors.Any())
                {
                    var spatial = rel.Errors.Where(e => !string.IsNullOrWhiteSpace(e.ErrorCode) && e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (spatial.Any())
                    {
                        if (IncludeErrorsCheck.IsChecked != true)
                        {
                            // 오류 표시 비활성화 시 공간 오류 표를 생략
                        }
                        else
                        {
                        html.AppendLine("        <details open><summary>공간 관계 오류 상세</summary>");
                        html.AppendLine("        <div class='toolbar'>");
                        html.AppendLine("          <input class='search' placeholder='검색(레이어/오류/메시지)' oninput=\"filterTable('tbl-stage5-spatial',this.value)\">");
                        html.AppendLine("          <span class='muted'>헤더 클릭시 정렬</span>");
                        html.AppendLine("        </div>");
                        html.AppendLine("        <div class='table-wrap'>");
                        html.AppendLine("        <table id='tbl-stage5-spatial' class='table-results' data-sort-dir='asc'>");
                        html.AppendLine("            <thead><tr>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-spatial',0,'text')\">원본레이어</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-spatial',1,'text')\">관계타입</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-spatial',2,'text')\">오류유형</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-spatial',3,'num')\">원본객체ID</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-spatial',4,'text')\">메시지</th>");
                        html.AppendLine("            </tr></thead>");
                        html.AppendLine("            <tbody>");
                        foreach (var e in spatial)
                        {
                            var srcLayer = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                            var relType = e.Metadata != null && e.Metadata.TryGetValue("RelationType", out var rt) ? Convert.ToString(rt) ?? string.Empty : string.Empty;
                            var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                            html.AppendLine("                <tr>");
                            html.AppendLine($"                    <td>{srcLayer}</td>");
                            html.AppendLine($"                    <td>{relType}</td>");
                            html.AppendLine($"                    <td>{e.ErrorCode}</td>");
                            html.AppendLine($"                    <td>{oid}</td>");
                            html.AppendLine($"                    <td>{System.Net.WebUtility.HtmlEncode(e.Message ?? string.Empty)}</td>");
                            html.AppendLine("                </tr>");
                        }
                        html.AppendLine("            </tbody>");
                        html.AppendLine("        </table>");
                        html.AppendLine("        </div>");
                        html.AppendLine("        </details>");
                        }
                    }
                    
                    // (옵션) 5단계에서 수집된 비공간(속성) 오류도 표시
                    var includeWarnings = IncludeWarningsCheck.IsChecked == true; // Relation의 경고는 거의 없지만 플래그 유지
                    var includeErrors = IncludeErrorsCheck.IsChecked == true;
                    var attrFromRel = rel.Errors.Where(e => string.IsNullOrWhiteSpace(e.ErrorCode) || !e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (includeErrors && attrFromRel.Any())
                    {
                        html.AppendLine("        <details><summary>속성 관계 오류(관계 단계 수집) 상세</summary>");
                        html.AppendLine("        <div class='table-wrap'>");
                        html.AppendLine("        <table id='tbl-stage5-attr' class='table-results' data-sort-dir='asc'>");
                        html.AppendLine("            <thead><tr>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-attr',0,'text')\">테이블명</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-attr',1,'text')\">필드명</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-attr',2,'text')\">코드</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-attr',3,'num')\">객체ID</th>");
                        html.AppendLine("                <th class='th-sort' onclick=\"sortTable('tbl-stage5-attr',4,'text')\">메시지</th>");
                        html.AppendLine("            </tr></thead><tbody>");
                        foreach (var e in attrFromRel)
                        {
                            var tableName = string.IsNullOrWhiteSpace(e.TableName) ? (e.TableId ?? string.Empty) : e.TableName;
                            var field = e.FieldName ?? (e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty);
                            var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                            html.AppendLine("            <tr>");
                            html.AppendLine($"                <td>{tableName}</td>");
                            html.AppendLine($"                <td>{field}</td>");
                            html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.ErrorCode ?? string.Empty)}</td>");
                            html.AppendLine($"                <td>{oid}</td>");
                            html.AppendLine($"                <td>{System.Net.WebUtility.HtmlEncode(e.Message ?? string.Empty)}</td>");
                            html.AppendLine("            </tr>");
                        }
                        html.AppendLine("            </tbody></table></div></details>");
                    }
                }
            }

            // 상세 정보
            if (IncludeDetailsCheck.IsChecked == true)
            {
                html.AppendLine("        <h2>📋 상세 검수 결과</h2>");
                html.AppendLine("        <ul>");
                html.AppendLine($"            <li><strong>검수 ID:</strong> {_currentValidationResult.ValidationId}</li>");
                html.AppendLine($"            <li><strong>시작 시간:</strong> {_currentValidationResult.StartedAt:yyyy-MM-dd HH:mm:ss}</li>");
                html.AppendLine($"            <li><strong>완료 시간:</strong> {_currentValidationResult.CompletedAt:yyyy-MM-dd HH:mm:ss}</li>");
                html.AppendLine($"            <li><strong>메시지:</strong> {_currentValidationResult.Message}</li>");
                html.AppendLine("        </ul>");
            }

            html.AppendLine("        <hr style='margin: 40px 0; border: none; border-top: 1px solid #e5e7eb;'>");
            html.AppendLine("        <p style='text-align: center; color: #6b7280; font-size: 14px;'>");
            html.AppendLine("            이 보고서는 공간정보 검수 시스템에서 자동 생성되었습니다.");
            html.AppendLine("        </p>");
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        /// <summary>
        /// 텍스트 보고서를 생성합니다
        /// </summary>
        private string GenerateTextReport()
        {
            if (_currentValidationResult == null) return "";

            var text = new StringBuilder();
            
            text.AppendLine("===============================================");
            text.AppendLine("           공간정보 검수 보고서");
            text.AppendLine("===============================================");
            text.AppendLine();
            text.AppendLine($"생성일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm:ss}");
            text.AppendLine($"검수 대상: {Path.GetFileName(_currentValidationResult.TargetFile)}");
            text.AppendLine();
            
            // 검수 요약
            if (IncludeSummaryCheck.IsChecked == true)
            {
                text.AppendLine("=== 검수 요약 ===");
                text.AppendLine($"검수 상태: {(_currentValidationResult.IsValid ? "✅ 성공" : "❌ 실패")}");
                text.AppendLine($"검수 시간: {_currentValidationResult.ProcessingTime.TotalSeconds:F1}초");
                text.AppendLine($"총 오류: {_currentValidationResult.ErrorCount}개");
                text.AppendLine($"총 경고: {_currentValidationResult.WarningCount}개");
                text.AppendLine();
            }

            // 1단계 테이블 검수 결과
            if (_currentValidationResult.TableCheckResult != null && _currentValidationResult.TableCheckResult.TableResults.Any())
            {
                text.AppendLine("=== 1단계 테이블 검수 결과 ===");
                text.AppendLine();
                
                // 테이블 헤더
                text.AppendLine("┌─────────────────────┬─────────────────────┬──────────┬──────────────┬──────────────┬──────────────┬─────────────────────┐");
                text.AppendLine("│ 테이블ID            │ 테이블명            │ 객체수   │ 예상피처타입 │ 실제피처타입 │ 피처타입확인 │ 실제FeatureClass명  │");
                text.AppendLine("├─────────────────────┼─────────────────────┼──────────┼──────────────┼──────────────┼──────────────┼─────────────────────┤");

                foreach (var table in _currentValidationResult.TableCheckResult.TableResults)
                {
                    var tableId = table.TableId.Length > 19 ? table.TableId.Substring(0, 16) + "..." : table.TableId;
                    var tableName = table.TableName.Length > 19 ? table.TableName.Substring(0, 16) + "..." : table.TableName;
                    var featureCount = table.FeatureCount?.ToString() ?? "null";
                    var expectedType = table.FeatureType.Length > 12 ? table.FeatureType.Substring(0, 9) + "..." : table.FeatureType;
                    var actualType = table.ActualFeatureType.Length > 12 ? table.ActualFeatureType.Substring(0, 9) + "..." : table.ActualFeatureType;
                    var featureTypeCheck = table.FeatureTypeCheck;
                    var actualClassName = table.ActualFeatureClassName.Length > 19 ? table.ActualFeatureClassName.Substring(0, 16) + "..." : table.ActualFeatureClassName;

                    text.AppendLine($"│ {tableId,-19} │ {tableName,-19} │ {featureCount,8} │ {expectedType,-12} │ {actualType,-12} │ {featureTypeCheck,12} │ {actualClassName,-19} │");
                }

                text.AppendLine("└─────────────────────┴─────────────────────┴──────────┴──────────────┴──────────────┴──────────────┴─────────────────────┘");
                text.AppendLine();
                
                // 통계 정보
                var totalCount = _currentValidationResult.TableCheckResult.TableResults.Count;
                var processedCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.IsProcessed);
                var skippedCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => !t.IsProcessed);
                var matchedTypeCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.FeatureTypeCheck == "Y");
                var mismatchedTypeCount = _currentValidationResult.TableCheckResult.TableResults.Count(t => t.FeatureTypeCheck == "N" && t.IsProcessed);
                
                text.AppendLine("=== 테이블 검수 통계 ===");
                text.AppendLine($"총 테이블 수: {totalCount}개");
                text.AppendLine($"처리된 테이블: {processedCount}개");
                text.AppendLine($"스킵된 테이블: {skippedCount}개");
                text.AppendLine($"피처타입 일치: {matchedTypeCount}개");
                text.AppendLine($"피처타입 불일치: {mismatchedTypeCount}개");
                text.AppendLine();
                
                // 오류/경고 상세 정보
                var tablesWithIssues = _currentValidationResult.TableCheckResult.TableResults.Where(t => t.Errors.Any() || t.Warnings.Any()).ToList();
                if (tablesWithIssues.Any())
                {
                    text.AppendLine("=== 상세 오류/경고 정보 ===");
                    foreach (var table in tablesWithIssues)
                    {
                        text.AppendLine($"테이블 '{table.TableId}' ({table.TableName}):");
                        foreach (var error in table.Errors)
                        {
                            text.AppendLine($"  [오류] {error}");
                        }
                        foreach (var warning in table.Warnings)
                        {
                            text.AppendLine($"  [경고] {warning}");
                        }
                        text.AppendLine();
                    }
                }
            }

            // 2단계 스키마 검수 결과
            if (_currentValidationResult.SchemaCheckResult != null && _currentValidationResult.SchemaCheckResult.SchemaResults.Any())
            {
                text.AppendLine("=== 2단계 스키마 검수 결과 ===");
                text.AppendLine();
                
                // 스키마 헤더
                text.AppendLine("┌─────────────────────┬─────────────────────┬─────────────────────┬──────────────┬──────────────┬──────────────┬──────────────┬──────────────┐");
                text.AppendLine("│ 테이블ID            │ 컬럼명              │ 컬럼한글명          │ 예상타입     │ 실제타입     │ 길이일치     │ NN일치       │ 검수결과     │");
                text.AppendLine("├─────────────────────┼─────────────────────┼─────────────────────┼──────────────┼──────────────┼──────────────┼──────────────┼──────────────┤");

                foreach (var schema in _currentValidationResult.SchemaCheckResult.SchemaResults)
                {
                    var tableId = schema.TableId.Length > 19 ? schema.TableId.Substring(0, 16) + "..." : schema.TableId;
                    var columnName = schema.ColumnName.Length > 19 ? schema.ColumnName.Substring(0, 16) + "..." : schema.ColumnName;
                    var koreanName = schema.ColumnKoreanName.Length > 19 ? schema.ColumnKoreanName.Substring(0, 16) + "..." : schema.ColumnKoreanName;
                    var expectedType = schema.ExpectedDataType.Length > 12 ? schema.ExpectedDataType.Substring(0, 9) + "..." : schema.ExpectedDataType;
                    var actualType = schema.ActualDataType.Length > 12 ? schema.ActualDataType.Substring(0, 9) + "..." : schema.ActualDataType;
                    var lengthMatch = schema.LengthMatches ? "Y" : "N";
                    var nnMatch = schema.NotNullMatches ? "Y" : "N";
                    var result = schema.IsValid ? "통과" : (schema.Errors.Any() ? "실패" : "경고");

                    text.AppendLine($"│ {tableId,-19} │ {columnName,-19} │ {koreanName,-19} │ {expectedType,-12} │ {actualType,-12} │ {lengthMatch,12} │ {nnMatch,12} │ {result,12} │");
                }

                text.AppendLine("└─────────────────────┴─────────────────────┴─────────────────────┴──────────────┴──────────────┴──────────────┴──────────────┴──────────────┘");
                text.AppendLine();
                
                // 스키마 통계 정보
                var schemaTotalCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count;
                var schemaProcessedCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.IsProcessed);
                var schemaSkippedCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => !s.IsProcessed);
                var schemaValidCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.IsValid);
                var schemaErrorCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.Errors.Any());
                var schemaWarningCount = _currentValidationResult.SchemaCheckResult.SchemaResults.Count(s => s.Warnings.Any() && !s.Errors.Any());
                
                text.AppendLine("=== 스키마 검수 통계 ===");
                text.AppendLine($"총 컬럼 수: {schemaTotalCount}개");
                text.AppendLine($"처리된 컬럼: {schemaProcessedCount}개");
                text.AppendLine($"스킵된 컬럼: {schemaSkippedCount}개");
                text.AppendLine($"검수 통과: {schemaValidCount}개");
                text.AppendLine($"오류: {schemaErrorCount}개");
                text.AppendLine($"경고: {schemaWarningCount}개");
                text.AppendLine();
                
                // 스키마 오류/경고 상세 정보
                var schemasWithIssues = _currentValidationResult.SchemaCheckResult.SchemaResults.Where(s => s.Errors.Any() || s.Warnings.Any()).ToList();
                if (schemasWithIssues.Any())
                {
                    text.AppendLine("=== 스키마 상세 오류/경고 정보 ===");
                    foreach (var schema in schemasWithIssues)
                    {
                        text.AppendLine($"컬럼 '{schema.TableId}.{schema.ColumnName}' ({schema.ColumnKoreanName}):");
                        foreach (var error in schema.Errors)
                        {
                            text.AppendLine($"  [오류] {error}");
                        }
                        foreach (var warning in schema.Warnings)
                        {
                            text.AppendLine($"  [경고] {warning}");
                        }
                        text.AppendLine();
                    }
                }
            }

            // 상세 정보
            if (IncludeDetailsCheck.IsChecked == true)
            {
                text.AppendLine("=== 상세 검수 결과 ===");
                text.AppendLine($"검수 ID: {_currentValidationResult.ValidationId}");
                text.AppendLine($"시작 시간: {_currentValidationResult.StartedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"완료 시간: {_currentValidationResult.CompletedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"메시지: {_currentValidationResult.Message}");
                text.AppendLine();
            }

            text.AppendLine("===============================================");
            text.AppendLine("이 보고서는 공간정보 검수 시스템에서 자동 생성되었습니다.");
            text.AppendLine("===============================================");

            return text.ToString();
        }

        /// <summary>
        /// PDF 보고서를 생성합니다
        /// </summary>
        private async Task GeneratePdfReportAsync(string filePath)
        {
            try
            {
                if (_pdfReportService == null)
                {
                    // 폴백: 기본 PDF 생성
                    _logger?.LogWarning("PDF 서비스가 없습니다. 기본 PDF 생성을 사용합니다.");
                    GenerateFallbackPdfReport(filePath);
                    return;
                }

                if (_currentValidationResult == null)
                {
                    throw new InvalidOperationException("검수 결과가 없습니다.");
                }

                // 실제 PDF 서비스 사용
                _pdfReportService.GeneratePdfReport(_currentValidationResult, filePath);
                _logger?.LogInformation("PDF 보고서 생성 완료: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PDF 보고서 생성 실패");
                // 폴백으로 HTML 기반 PDF 생성
                GenerateFallbackPdfReport(filePath);
            }
        }

        /// <summary>
        /// 폴백 PDF 보고서를 생성합니다
        /// </summary>
        private void GenerateFallbackPdfReport(string filePath)
        {
            string htmlContent = GenerateHtmlReport();
            File.WriteAllText(filePath.Replace(".pdf", ".html"), htmlContent, Encoding.UTF8);
            MessageBox.Show("PDF 생성 중 오류가 발생하여 HTML 형식으로 대체 생성되었습니다.", 
                          "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Excel 보고서 기능 완전 제거됨

        /// <summary>
        /// 보고서 미리보기를 표시합니다
        /// </summary>
        private void ShowPreview(string content)
        {
            PreviewContent.Children.Clear();
            
            var textBlock = new TextBlock
            {
                Text = content,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
            };
            
            PreviewContent.Children.Add(textBlock);
            PreviewCard.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 보고서 목록을 업데이트합니다
        /// </summary>
        private void UpdateReportList()
        {
            // 실제 구현에서는 데이터베이스나 파일에서 보고서 목록을 로드
            // 여기서는 시뮬레이션으로 현재 생성된 보고서만 표시
            
            ReportListPanel.Children.Clear();
            
            if (!string.IsNullOrEmpty(_lastGeneratedReportPath) && File.Exists(_lastGeneratedReportPath))
            {
                var reportItem = CreateReportListItem(
                    Path.GetFileName(_lastGeneratedReportPath),
                    File.GetLastWriteTime(_lastGeneratedReportPath),
                    _lastGeneratedReportPath
                );
                ReportListPanel.Children.Add(reportItem);
            }
            else
            {
                // 기본 안내 메시지
                var emptyMessage = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var icon = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("FileIcon"),
                    Fill = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(icon, 0);

                var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleText = new TextBlock
                {
                    Text = "아직 생성된 보고서가 없습니다",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
                };
                var descText = new TextBlock
                {
                    Text = "위의 '보고서 생성' 버튼을 클릭하여 첫 번째 보고서를 만들어보세요",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                textPanel.Children.Add(titleText);
                textPanel.Children.Add(descText);
                Grid.SetColumn(textPanel, 1);

                grid.Children.Add(icon);
                grid.Children.Add(textPanel);
                emptyMessage.Child = grid;

                ReportListPanel.Children.Add(emptyMessage);
            }
        }

        /// <summary>
        /// 보고서 목록 항목을 생성합니다
        /// </summary>
        private Border CreateReportListItem(string fileName, DateTime createdTime, string filePath)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 아이콘
            var icon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("FileIcon"),
                Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);

            // 파일 정보
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = fileName,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
            };
            var timeText = new TextBlock
            {
                Text = $"생성일: {createdTime:yyyy-MM-dd HH:mm:ss}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            textPanel.Children.Add(titleText);
            textPanel.Children.Add(timeText);
            Grid.SetColumn(textPanel, 1);

            // 열기 버튼
            var openButton = new Button
            {
                Content = "열기",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0, 0, 0, 0),
                FontSize = 12
            };
            openButton.Click += (s, e) => 
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일을 열 수 없습니다:\n{ex.Message}", "오류", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            Grid.SetColumn(openButton, 2);

            // 삭제 버튼
            var deleteButton = new Button
            {
                Content = "삭제",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0, 0, 0, 0),
                FontSize = 12
            };
            deleteButton.Click += (s, e) =>
            {
                var result = MessageBox.Show($"'{fileName}' 보고서를 삭제하시겠습니까?", 
                                           "보고서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        if (filePath == _lastGeneratedReportPath)
                        {
                            _lastGeneratedReportPath = null;
                        }
                        UpdateReportList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"파일 삭제 중 오류가 발생했습니다:\n{ex.Message}", 
                                      "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };
            Grid.SetColumn(deleteButton, 3);

            grid.Children.Add(icon);
            grid.Children.Add(textPanel);
            grid.Children.Add(openButton);
            grid.Children.Add(deleteButton);
            border.Child = grid;

            return border;
        }

        private async void GenerateReportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentValidationResult == null)
                {
                    MessageBox.Show("생성할 검수 결과가 없습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var format = GetSelectedFormat();
                // 검수 대상 파일명 기반 제안 파일명 생성
                var baseNameRaw = _currentValidationResult?.TargetFile;
                string baseName;
                try
                {
                    baseName = string.IsNullOrWhiteSpace(baseNameRaw) ? "검수결과" : System.IO.Path.GetFileNameWithoutExtension(baseNameRaw);
                }
                catch
                {
                    baseName = "검수결과"; // 파일명 파싱 실패 시 기본명 사용
                }
                // 파일명 규칙: 검수파일명 + _ + 날짜(yyyyMMdd) + _ + 시간(HHmmss)
                var now = DateTime.Now;
                var defaultExt = format.ToLower()=="pdf"?"pdf":"html";
                // 파일명에서 사용할 수 없는 문자는 '_'로 치환
                var invalidChars = System.IO.Path.GetInvalidFileNameChars();
                var sanitizedBaseName = new string((baseName ?? "검수결과").Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
                var suggested = $"{sanitizedBaseName}_{now:yyyyMMdd_HHmmss}.{defaultExt}";

                var sfd = new SaveFileDialog
                {
                    Filter = GetFileFilter(),
                    FileName = suggested
                };
                var ok = sfd.ShowDialog();
                if (ok != true) return;

                // PDF만 우선 지원
                if (format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
                {
                    if (_pdfReportService == null)
                    {
                        MessageBox.Show("PDF 보고서 서비스를 사용할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    _pdfReportService.GeneratePdfReport(_currentValidationResult, sfd.FileName);
                    _lastGeneratedReportPath = sfd.FileName;
                    MessageBox.Show("PDF 보고서가 생성되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // 간단한 텍스트 기반 HTML 생성 (임시)
                    var html = GenerateReportContent().Replace("\n", "<br/>");
                    await File.WriteAllTextAsync(sfd.FileName, $"<html><meta charset='utf-8'><body style='font-family:Malgun Gothic,Segoe UI,sans-serif'>{html}</body></html>");
                    _lastGeneratedReportPath = sfd.FileName;
                    MessageBox.Show("HTML 보고서가 생성되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                UpdateReportList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "보고서 생성 중 오류");
                MessageBox.Show($"보고서 생성 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}