using System;
using System.IO;
using System.Linq;
using iTextSharp.text;
using iTextSharp.text.pdf;
using SpatialCheckPro.Models;
using iTextSharp.text.pdf.draw;
using System.Collections.Generic; // Added for List

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// PDF 보고서 생성 서비스
    /// </summary>
    public class PdfReportService
    {
        // 색상 팔레트 (HTML 스타일 유사 톤)
        private static readonly BaseColor PrimaryBlue = new BaseColor(59, 130, 246);      // #3B82F6
        private static readonly BaseColor HeaderBg = new BaseColor(243, 244, 246);        // #F3F4F6
        private static readonly BaseColor RowAltBg = new BaseColor(248, 250, 252);        // #F8FAFC
        private static readonly BaseColor BorderGray = new BaseColor(209, 213, 219);      // #D1D5DB
        private static readonly BaseColor TextDark = new BaseColor(31, 41, 55);           // #1F2937
        private static readonly BaseColor TextMuted = new BaseColor(107, 114, 128);       // #6B7280
        private static readonly BaseColor SuccessGreen = new BaseColor(16, 185, 129);     // #10B981
        private static readonly BaseColor ErrorRed = new BaseColor(239, 68, 68);          // #EF4444
        private static readonly BaseColor WarningYellow = new BaseColor(245, 158, 11);    // #F59E0B

        /// <summary>
        /// PDF 보고서를 생성합니다
        /// </summary>
        /// <param name="result">검수 결과</param>
        /// <param name="outputPath">출력 파일 경로</param>
        public void GeneratePdfReport(ValidationResult result, string outputPath)
        {
            try
            {
                // 한글 폰트 설정
                var fontPath = GetKoreanFontPath();
                BaseFont baseFont;
                
                if (!string.IsNullOrEmpty(fontPath) && File.Exists(fontPath))
                {
                    baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                }
                else
                {
                    // 기본 폰트 사용 (한글 지원 안됨)
                    baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
                }

                var titleFont = new Font(baseFont, 18, Font.BOLD, TextDark);
                var headerFont = new Font(baseFont, 14, Font.BOLD, TextDark);
                var normalFont = new Font(baseFont, 10, Font.NORMAL, TextDark);
                var smallFont = new Font(baseFont, 9, Font.NORMAL, TextDark);
                var mutedFont = new Font(baseFont, 9, Font.NORMAL, TextMuted);

                // PDF 문서 생성
                using var document = new Document(PageSize.A4, 50, 50, 50, 50);
                using var writer = PdfWriter.GetInstance(document, new FileStream(outputPath, FileMode.Create));
                
                document.Open();

                // 제목
                var title = new Paragraph("공간정보 검수 보고서", titleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 20
                };
                document.Add(title);

                // 제목 하단 컬러 라인 (HTML의 헤더 하단 보더 효과)
                var line = new LineSeparator(2f, 100f, PrimaryBlue, Element.ALIGN_CENTER, -2);
                document.Add(new Chunk(line));
                document.Add(new Paragraph("\n"));

                // 검수 요약 정보
                AddSummarySection(document, result, headerFont, normalFont);

                // 1단계: 테이블 검수 결과
                if (result.TableCheckResult != null)
                {
                    AddTableCheckSection(document, result.TableCheckResult, headerFont, normalFont, smallFont);
                }

                // 2단계: 스키마 검수 결과
                if (result.SchemaCheckResult != null)
                {
                    AddSchemaCheckSection(document, result.SchemaCheckResult, headerFont, normalFont, smallFont);
                }

                // 3단계: 지오메트리 검수 결과
                if (result.GeometryCheckResult != null)
                {
                    var geoSummary = new PdfPTable(2);
                    geoSummary.WidthPercentage = 100;
                    geoSummary.SetWidths(new float[] { 1f, 1f });

                    AddSummaryCell(geoSummary, "중복/겹침", result.GeometryCheckResult.GeometryResults.Sum(r => r.DuplicateCount + r.OverlapCount).ToString(), smallFont, BaseColor.Red);
                    AddSummaryCell(geoSummary, "자체꼬임/중첩", result.GeometryCheckResult.GeometryResults.Sum(r => r.SelfIntersectionCount + r.SelfOverlapCount).ToString(), smallFont, BaseColor.Red);
                    AddSummaryCell(geoSummary, "슬리버/스파이크", result.GeometryCheckResult.GeometryResults.Sum(r => r.SliverCount + r.SpikeCount).ToString(), smallFont, BaseColor.Red);
                    AddSummaryCell(geoSummary, "짧은객체/작은면적", result.GeometryCheckResult.GeometryResults.Sum(r => r.ShortObjectCount + r.SmallAreaCount).ToString(), smallFont, WarningYellow);
                    AddSummaryCell(geoSummary, "홀/최소정점", result.GeometryCheckResult.GeometryResults.Sum(r => r.PolygonInPolygonCount + r.MinPointCount).ToString(), smallFont, WarningYellow);
                    AddSummaryCell(geoSummary, "언더슛/오버슛", result.GeometryCheckResult.GeometryResults.Sum(r => r.UndershootCount + r.OvershootCount).ToString(), smallFont, BaseColor.Red);
                    
                    var geoSection = new Chunk("3단계: 지오메트리 검수", headerFont);
                    document.Add(new Paragraph(geoSection));
                    document.Add(geoSummary);
                    document.Add(new Paragraph("\n"));

                    // 각 테이블별 검사 결과를 집계
                    var tableResults = result.GeometryCheckResult.GeometryResults
                        .GroupBy(r => new { r.TableId, r.TableName })
                        .Select(g => new 
                        {
                            g.Key.TableId,
                            g.Key.TableName,
                            TotalErrors = g.Sum(i => i.ErrorCount),
                            DuplicateCount = g.Sum(i => i.DuplicateCount),
                            OverlapCount = g.Sum(i => i.OverlapCount),
                            SelfIntersectionCount = g.Sum(i => i.SelfIntersectionCount),
                            SliverCount = g.Sum(i => i.SliverCount),
                            ShortObjectCount = g.Sum(i => i.ShortObjectCount),
                            SmallAreaCount = g.Sum(i => i.SmallAreaCount),
                            PolygonInPolygonCount = g.Sum(i => i.PolygonInPolygonCount),
                            MinPointCount = g.Sum(i => i.MinPointCount),
                            SpikeCount = g.Sum(i => i.SpikeCount),
                            SelfOverlapCount = g.Sum(i => i.SelfOverlapCount),
                            UndershootCount = g.Sum(i => i.UndershootCount),
                            OvershootCount = g.Sum(i => i.OvershootCount)
                        })
                        .OrderBy(r => r.TableName)
                        .ToList();

                    if (tableResults.Any())
                    {
                        // 전체 요약
                        var totalErrors = tableResults.Sum(r => r.TotalErrors);
                        var summary = new Paragraph($"총 {tableResults.Count}개 테이블 검사, {totalErrors}개 오류 발견", normalFont)
                        {
                            SpacingAfter = 10
                        };
                        document.Add(summary);

                        // 상세 테이블
                        var table = new PdfPTable(4) { WidthPercentage = 100 };
                        table.SetWidths(new float[] { 25, 45, 15, 15 });
                        AddTableHeader(table, new[] { "Table Name", "Check Item", "Error Count", "Result" }, smallFont);

                        int rowIndex = 0;
                        foreach (var res in tableResults)
                        {
                            bool hasError = res.TotalErrors > 0;
                            // 테이블명 셀 (여러 행에 걸쳐 표시)
                            var nameCell = new PdfPCell(new Phrase(res.TableName, smallFont))
                            {
                                Rowspan = 11, // 검사 항목 개수
                                VerticalAlignment = Element.ALIGN_MIDDLE,
                                BackgroundColor = rowIndex % 2 == 1 ? RowAltBg : BaseColor.White,
                                BorderColor = BorderGray
                            };
                            table.AddCell(nameCell);

                            // 각 검사 항목 추가
                            AddDetailRow(table, "중복 지오메트리", res.DuplicateCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "객체 간 겹침", res.OverlapCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "자체 꼬임", res.SelfIntersectionCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "슬리버 폴리곤", res.SliverCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "짧은 객체", res.ShortObjectCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "작은 면적 객체", res.SmallAreaCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "홀 폴리곤 오류", res.PolygonInPolygonCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "최소 정점 개수", res.MinPointCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "스파이크(돌기)", res.SpikeCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "자기 중첩", res.SelfOverlapCount, smallFont, rowIndex % 2 == 1);
                            AddDetailRow(table, "언더슛/오버슛", res.UndershootCount + res.OvershootCount, smallFont, rowIndex % 2 == 1);

                            rowIndex++;
                        }
                        document.Add(table);
                    }
                }

                // 4단계: 속성 관계 검수 결과
                if (result.AttributeRelationCheckResult != null)
                {
                    AddAttributeRelationCheckSection(document, result.AttributeRelationCheckResult, headerFont, normalFont, smallFont);
                }

                // 5단계: 공간 관계 검수 결과
                if (result.RelationCheckResult != null)
                {
                    AddRelationCheckSection(document, result.RelationCheckResult, headerFont, normalFont, smallFont);
                }

                document.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"PDF 보고서 생성 중 오류 발생: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 검수 요약 섹션을 추가합니다
        /// </summary>
        private void AddSummarySection(Document document, ValidationResult result, Font headerFont, Font normalFont)
        {
            // 섹션 제목
            var sectionTitle = new Paragraph("검수 요약", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            // 요약 테이블 (레이블 셀 배경, 경계선 색상 적용)
            var summaryTable = new PdfPTable(2) { WidthPercentage = 100 };
            summaryTable.SetWidths(new float[] { 30, 70 });

            AddTableRow(summaryTable, "검수 ID", result.ValidationId ?? "N/A", normalFont);
            AddTableRow(summaryTable, "대상 파일", result.TargetFile, normalFont);
            AddTableRow(summaryTable, "검수 시작", result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"), normalFont);
            AddTableRow(summaryTable, "검수 완료", result.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "진행 중", normalFont);
            AddTableRow(summaryTable, "소요 시간", FormatTimeSpan(result.ProcessingTime), normalFont);
            AddTableRow(summaryTable, "검수 상태", result.IsValid ? "통과" : "실패", normalFont);
            AddTableRow(summaryTable, "총 오류", result.ErrorCount.ToString(), normalFont);
            // 경고 항목 제거

            document.Add(summaryTable);
        }

        /// <summary>
        /// 테이블 검수 섹션을 추가합니다
        /// </summary>
        private void AddTableCheckSection(Document document, TableCheckResult result, Font headerFont, Font normalFont, Font smallFont)
        {
            // 섹션 제목
            var sectionTitle = new Paragraph("1단계: 테이블 검수 결과", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            // 요약 정보
            var summary = new Paragraph($"총 {result.TableResults.Count}개 테이블, 오류 {result.ErrorCount}개", normalFont)
            {
                SpacingAfter = 10
            };
            document.Add(summary);

            // 테이블 결과
            if (result.TableResults.Any())
            {
                var table = new PdfPTable(6) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 15, 15, 25, 15, 15, 15 });

                // 헤더
                AddTableHeader(table, new[] { "Table Id", "Table Name", "Actual FeatureClass", "Expected Type", "Actual Type", "Type Match" }, smallFont);

                // 데이터 (행 교차 배경)
                int rowIndex = 0;
                foreach (var item in result.TableResults)
                {
                    AddTableRow(table, new[] {
                        item.TableId,
                        item.TableName,
                        item.DisplayActualFeatureClassName,
                        item.FeatureType,
                        item.ActualFeatureType,
                        item.FeatureTypeCheck
                    }, smallFont, rowIndex++ % 2 == 1);
                }

                document.Add(table);
            }
        }

        /// <summary>
        /// 스키마 검수 섹션을 추가합니다
        /// </summary>
        private void AddSchemaCheckSection(Document document, SchemaCheckResult result, Font headerFont, Font normalFont, Font smallFont)
        {
            // 섹션 제목
            var sectionTitle = new Paragraph("2단계: 스키마 검수 결과", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            // 요약 정보
            var summary = new Paragraph($"처리 {result.ProcessedColumnCount}개, 스킵 {result.SkippedColumnCount}개, 오류 {result.ErrorCount}개", normalFont)
            {
                SpacingAfter = 10
            };
            document.Add(summary);

            // 스키마 결과
            if (result.SchemaResults.Any())
            {
                var table = new PdfPTable(9) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 12, 12, 15, 10, 10, 8, 8, 8, 10 });

                // 헤더
                AddTableHeader(table, new[] { "Table Id", "Field Name", "Field Alias", "Expected Type", "Actual Type", "Expected Length", "Actual Length", "NN Match", "Result" }, smallFont);

                // 데이터 (행 교차 배경)
                int rowIndex = 0;
                foreach (var item in result.SchemaResults)
                {
                    AddTableRow(table, new[] {
                        item.TableId,
                        item.ColumnName,
                        item.ColumnKoreanName,
                        item.ExpectedDataType,
                        item.ActualDataType,
                        item.ExpectedLength,
                        item.ActualLength,
                        item.NotNullMatchesDisplay,
                        item.IsValidDisplay
                    }, smallFont, rowIndex++ % 2 == 1);
                }

                document.Add(table);
            }
        }

        /// <summary>
        /// 지오메트리 검수 섹션을 추가합니다
        /// </summary>
        private void AddGeometryCheckSection(Document document, GeometryCheckResult result, Font headerFont, Font normalFont, Font smallFont)
        {
            var sectionTitle = new Paragraph("3단계: 지오메트리 검수 결과", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            // 각 테이블별 검사 결과를 집계
            var tableResults = result.GeometryResults
                .GroupBy(r => new { r.TableId, r.TableName })
                .Select(g => new 
                {
                    g.Key.TableId,
                    g.Key.TableName,
                    TotalErrors = g.Sum(i => i.ErrorCount),
                    DuplicateCount = g.Sum(i => i.DuplicateCount),
                    OverlapCount = g.Sum(i => i.OverlapCount),
                    SelfIntersectionCount = g.Sum(i => i.SelfIntersectionCount),
                    SliverCount = g.Sum(i => i.SliverCount),
                    ShortObjectCount = g.Sum(i => i.ShortObjectCount),
                    SmallAreaCount = g.Sum(i => i.SmallAreaCount),
                    PolygonInPolygonCount = g.Sum(i => i.PolygonInPolygonCount),
                    MinPointCount = g.Sum(i => i.MinPointCount),
                    SpikeCount = g.Sum(i => i.SpikeCount),
                    SelfOverlapCount = g.Sum(i => i.SelfOverlapCount),
                    UndershootCount = g.Sum(i => i.UndershootCount),
                    OvershootCount = g.Sum(i => i.OvershootCount)
                })
                .OrderBy(r => r.TableName)
                .ToList();

            if (tableResults.Any())
            {
                // 전체 요약
                var totalErrors = tableResults.Sum(r => r.TotalErrors);
                var summary = new Paragraph($"총 {tableResults.Count}개 테이블 검사, {totalErrors}개 오류 발견", normalFont)
                {
                    SpacingAfter = 10
                };
                document.Add(summary);

                // 상세 테이블
                var table = new PdfPTable(4) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 25, 45, 15, 15 });
                AddTableHeader(table, new[] { "Table Name", "Check Item", "Error Count", "Result" }, smallFont);

                int rowIndex = 0;
                foreach (var res in tableResults)
                {
                    bool hasError = res.TotalErrors > 0;
                    // 테이블명 셀 (여러 행에 걸쳐 표시)
                    var nameCell = new PdfPCell(new Phrase(res.TableName, smallFont))
                    {
                        Rowspan = 11, // 검사 항목 개수
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        BackgroundColor = rowIndex % 2 == 1 ? RowAltBg : BaseColor.White,
                        BorderColor = BorderGray
                    };
                    table.AddCell(nameCell);

                    // 각 검사 항목 추가
                    AddDetailRow(table, "중복 지오메트리", res.DuplicateCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "객체 간 겹침", res.OverlapCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "자체 꼬임", res.SelfIntersectionCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "슬리버 폴리곤", res.SliverCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "짧은 객체", res.ShortObjectCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "작은 면적 객체", res.SmallAreaCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "홀 폴리곤 오류", res.PolygonInPolygonCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "최소 정점 개수", res.MinPointCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "스파이크(돌기)", res.SpikeCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "자기 중첩", res.SelfOverlapCount, smallFont, rowIndex % 2 == 1);
                    AddDetailRow(table, "언더슛/오버슛", res.UndershootCount + res.OvershootCount, smallFont, rowIndex % 2 == 1);

                    rowIndex++;
                }
                document.Add(table);
            }
        }
        
        /// <summary>
        /// 지오메트리 검수 상세 행 추가 헬퍼
        /// </summary>
        private void AddDetailRow(PdfPTable table, string checkName, int errorCount, Font font, bool useAltBg)
        {
            var bgColor = useAltBg ? RowAltBg : BaseColor.White;

            table.AddCell(new PdfPCell(new Phrase(checkName, font)) { BackgroundColor = bgColor, BorderColor = BorderGray, Padding = 5 });
            table.AddCell(new PdfPCell(new Phrase(errorCount.ToString(), font)) { HorizontalAlignment = Element.ALIGN_RIGHT, BackgroundColor = bgColor, BorderColor = BorderGray, Padding = 5 });
            
            var resultPhrase = new Phrase(errorCount > 0 ? "실패" : "통과", new Font(font.BaseFont, font.Size, font.Style, errorCount > 0 ? ErrorRed : SuccessGreen));
            table.AddCell(new PdfPCell(resultPhrase) { HorizontalAlignment = Element.ALIGN_CENTER, BackgroundColor = bgColor, BorderColor = BorderGray, Padding = 5 });
        }

        /// <summary>
        /// 관계 검수 섹션을 추가합니다
        /// </summary>
        private void AddRelationCheckSection(Document document, RelationCheckResult result, Font headerFont, Font normalFont, Font smallFont)
        {
            // 섹션 제목
            var sectionTitle = new Paragraph("5단계: 공간 관계 검수 결과", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            // 요약
            var summary = new Paragraph($"검수 상태: {(result.IsValid ? "성공" : "실패")}, 처리 시간: {result.ProcessingTime.TotalSeconds:F1}초", normalFont)
            {
                SpacingAfter = 10
            };
            document.Add(summary);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                var msg = new Paragraph($"메시지: {result.Message}", normalFont) { SpacingAfter = 10 };
                document.Add(msg);
            }

            // 상세: 공간/속성 관계 오류 테이블
            if (result.Errors != null && result.Errors.Any())
            {
                var spatial = result.Errors.Where(e => !string.IsNullOrWhiteSpace(e.ErrorCode) && e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();
                var attr = result.Errors.Where(e => string.IsNullOrWhiteSpace(e.ErrorCode) || !e.ErrorCode.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)).ToList();

                // 공간 관계 오류 표
                if (spatial.Any())
                {
                    var spatialTitle = new Paragraph("공간 관계 오류 상세", normalFont) { SpacingBefore = 8, SpacingAfter = 6 };
                    document.Add(spatialTitle);
                    var table = new PdfPTable(5) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 20, 15, 20, 15, 30 });
                    AddTableHeader(table, new[] { "원본레이어", "관계타입", "오류유형", "원본객체ID", "메시지" }, smallFont);
                    int rowIndex = 0;
                    foreach (var e in spatial)
                    {
                        var srcLayer = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                        var relType = e.Metadata != null && e.Metadata.TryGetValue("RelationType", out var rt) ? Convert.ToString(rt) ?? string.Empty : string.Empty;
                        var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                        AddTableRow(table, new[] { srcLayer, relType, e.ErrorCode ?? "", oid, TruncateText(e.Message ?? "", 120) }, smallFont, rowIndex++ % 2 == 1);
                    }
                    document.Add(table);
                }

                // 속성 관계 오류 표
                if (attr.Any())
                {
                    var attrTitle = new Paragraph("속성 관계 오류 상세", normalFont) { SpacingBefore = 12, SpacingAfter = 6 };
                    document.Add(attrTitle);
                    var table = new PdfPTable(7) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 15, 15, 15, 12, 14, 14, 15 });
                    AddTableHeader(table, new[] { "테이블명", "필드명", "규칙명", "객체ID", "기대값", "실제값", "메시지" }, smallFont);
                    int rowIndex = 0;
                    foreach (var e in attr)
                    {
                        var tableName = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                        var field = e.FieldName ?? (e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty);
                        var rule = string.IsNullOrWhiteSpace(e.ErrorCode) ? (e.Metadata != null && e.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : e.ErrorCode;
                        var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                        var expected = e.ExpectedValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Expected", out var exv) ? Convert.ToString(exv) ?? string.Empty : string.Empty);
                        var actual = e.ActualValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Actual", out var acv) ? Convert.ToString(acv) ?? string.Empty : string.Empty);
                        AddTableRow(table, new[] { tableName, field, rule, oid, TruncateText(expected, 60), TruncateText(actual, 60), TruncateText(e.Message ?? "", 120) }, smallFont, rowIndex++ % 2 == 1);
                    }
                    document.Add(table);
                }
            }
        }

        /// <summary>
        /// 속성 관계 검수 섹션을 추가합니다 (5단계)
        /// </summary>
        private void AddAttributeRelationCheckSection(Document document, AttributeRelationCheckResult result, Font headerFont, Font normalFont, Font smallFont)
        {
            var sectionTitle = new Paragraph("4단계: 속성 관계 검수 결과", headerFont)
            {
                SpacingBefore = 20,
                SpacingAfter = 10
            };
            document.Add(sectionTitle);

            var summary = new Paragraph($"검수 상태: {(result.IsValid ? "성공" : "실패")}, 처리 시간: {result.ProcessingTime.TotalSeconds:F1}초", normalFont)
            {
                SpacingAfter = 10
            };
            document.Add(summary);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                var msg = new Paragraph($"메시지: {result.Message}", normalFont) { SpacingAfter = 10 };
                document.Add(msg);
            }

            // 상세 표 (테이블/필드/규칙/객체ID/기대/실제/메시지)
            var all = (result.Errors ?? new List<ValidationError>()).Concat(result.Warnings ?? new List<ValidationError>()).ToList();
            if (all.Any())
            {
                var table = new PdfPTable(7) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 15, 15, 15, 12, 14, 14, 15 });
                AddTableHeader(table, new[] { "테이블명", "필드명", "규칙명", "객체ID", "기대값", "실제값", "메시지" }, smallFont);
                int rowIndex = 0;
                foreach (var e in all)
                {
                    var tableName = string.IsNullOrWhiteSpace(e.TableName) ? (e.SourceTable ?? string.Empty) : e.TableName;
                    var field = e.FieldName ?? (e.Metadata != null && e.Metadata.TryGetValue("FieldName", out var fn) ? Convert.ToString(fn) ?? string.Empty : string.Empty);
                    var rule = string.IsNullOrWhiteSpace(e.ErrorCode) ? (e.Metadata != null && e.Metadata.TryGetValue("RuleName", out var rn) ? Convert.ToString(rn) ?? "ATTRIBUTE_CHECK" : "ATTRIBUTE_CHECK") : e.ErrorCode;
                    var oid = !string.IsNullOrWhiteSpace(e.FeatureId) ? e.FeatureId : (e.SourceObjectId?.ToString() ?? string.Empty);
                    var expected = e.ExpectedValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Expected", out var exv) ? Convert.ToString(exv) ?? string.Empty : string.Empty);
                    var actual = e.ActualValue ?? (e.Metadata != null && e.Metadata.TryGetValue("Actual", out var acv) ? Convert.ToString(acv) ?? string.Empty : string.Empty);
                    AddTableRow(table, new[] { tableName, field, rule, oid, TruncateText(expected, 60), TruncateText(actual, 60), TruncateText(e.Message ?? "", 120) }, smallFont, rowIndex++ % 2 == 1);
                }
                document.Add(table);
            }
        }

        /// <summary>
        /// 테이블 헤더를 추가합니다
        /// </summary>
        private void AddTableHeader(PdfPTable table, string[] headers, Font font)
        {
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, font))
                {
                    BackgroundColor = HeaderBg,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6,
                    BorderColor = BorderGray
                };
                table.AddCell(cell);
            }
        }

        /// <summary>
        /// 테이블 행을 추가합니다
        /// </summary>
        private void AddTableRow(PdfPTable table, string[] values, Font font)
        {
            foreach (var value in values)
            {
                var cell = new PdfPCell(new Phrase(value ?? "", font))
                {
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    Padding = 5,
                    BorderColor = BorderGray
                };
                table.AddCell(cell);
            }
        }

        /// <summary>
        /// 테이블 행을 추가합니다 (2열)
        /// </summary>
        private void AddTableRow(PdfPTable table, string key, string value, Font font)
        {
            // 키 셀
            var keyCell = new PdfPCell(new Phrase(key, font))
            {
                BackgroundColor = HeaderBg,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 6,
                BorderColor = BorderGray
            };
            table.AddCell(keyCell);

            // 값 셀
            var valueCell = new PdfPCell(new Phrase(value ?? "", font))
            {
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 6,
                BorderColor = BorderGray
            };
            table.AddCell(valueCell);
        }

        /// <summary>
        /// 테이블 행을 추가합니다 (교차 배경)
        /// </summary>
        private void AddTableRow(PdfPTable table, string[] values, Font font, bool useAltBackground)
        {
            foreach (var value in values)
            {
                var cell = new PdfPCell(new Phrase(value ?? "", font))
                {
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    Padding = 5,
                    BorderColor = BorderGray,
                    BackgroundColor = useAltBackground ? RowAltBg : new BaseColor(255,255,255)
                };
                table.AddCell(cell);
            }
        }

        /// <summary>
        /// 요약 셀을 추가합니다
        /// </summary>
        private void AddSummaryCell(PdfPTable table, string label, string value, Font font, BaseColor color)
        {
            // 수정된 부분: 라벨 셀은 기본 폰트로 추가
            var labelCell = new PdfPCell(new Phrase(label, font));
            labelCell.HorizontalAlignment = Element.ALIGN_LEFT;
            labelCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            labelCell.BackgroundColor = new BaseColor(241, 245, 249); // 밝은 회색 배경
            labelCell.Padding = 8;
            labelCell.Border = Rectangle.NO_BORDER;
            table.AddCell(labelCell);

            // 값 셀은 지정된 폰트와 색상으로 추가
            var valueCell = new PdfPCell(new Phrase(value, font));
            valueCell.HorizontalAlignment = Element.ALIGN_CENTER;
            valueCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            valueCell.BackgroundColor = color;
            valueCell.Padding = 8;
            valueCell.Border = Rectangle.NO_BORDER;
            table.AddCell(valueCell);
        }

        /// <summary>
        /// 시간 간격을 포맷합니다
        /// </summary>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds < 60)
            {
                return $"{timeSpan.TotalSeconds:F1}초";
            }
            else if (timeSpan.TotalMinutes < 60)
            {
                return $"{timeSpan.TotalMinutes:F1}분";
            }
            else
            {
                return timeSpan.ToString(@"hh\:mm\:ss");
            }
        }

        /// <summary>
        /// 한글 폰트 경로를 가져옵니다
        /// </summary>
        private string GetKoreanFontPath()
        {
            // Windows 시스템 폰트 경로들
            var fontPaths = new[]
            {
                @"C:\Windows\Fonts\malgun.ttf",     // 맑은 고딕
                @"C:\Windows\Fonts\gulim.ttc",      // 굴림
                @"C:\Windows\Fonts\batang.ttc",     // 바탕
                @"C:\Windows\Fonts\dotum.ttc"       // 돋움
            };

            return fontPaths.FirstOrDefault(File.Exists) ?? "";
        }

        /// <summary>
        /// 심각도의 표시명을 반환합니다
        /// </summary>
        /// <param name="severity">오류 심각도</param>
        /// <returns>표시명</returns>
        private string GetSeverityDisplayName(Models.Enums.ErrorSeverity severity)
        {
            return severity switch
            {
                Models.Enums.ErrorSeverity.Critical => "치명적",
                Models.Enums.ErrorSeverity.Error => "오류",
                Models.Enums.ErrorSeverity.Warning => "경고",
                Models.Enums.ErrorSeverity.Info => "정보",
                _ => "알 수 없음"
            };
        }

        /// <summary>
        /// 텍스트를 지정된 길이로 자릅니다
        /// </summary>
        /// <param name="text">원본 텍스트</param>
        /// <param name="maxLength">최대 길이</param>
        /// <returns>잘린 텍스트</returns>
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? "";

            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}
