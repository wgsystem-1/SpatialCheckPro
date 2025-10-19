using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using OSGeo.GDAL;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// File Geodatabase 스키마 추출 서비스
    /// </summary>
    public class SchemaExtractionService
    {
        private readonly ILogger<SchemaExtractionService> _logger;

        public SchemaExtractionService(ILogger<SchemaExtractionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// File Geodatabase의 모든 피처클래스 스키마를 Markdown으로 추출
        /// </summary>
        public async Task<string> ExtractAllFeatureClassSchemasAsync(string gdbPath)
        {
            try
            {
                _logger.LogInformation("File Geodatabase 스키마 추출 시작: {GdbPath}", gdbPath);

                // GDAL 초기화
                Gdal.AllRegister();
                Ogr.RegisterAll();

                var sb = new StringBuilder();
                
                // 헤더 작성
                sb.AppendLine("# File Geodatabase 스키마 정보");
                sb.AppendLine();
                sb.AppendLine($"**파일 경로**: `{gdbPath}`");
                sb.AppendLine($"**추출 일시**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                // 데이터소스 열기
                using var dataSource = Ogr.Open(gdbPath, 0);
                if (dataSource == null)
                {
                    throw new Exception($"File Geodatabase를 열 수 없습니다: {gdbPath}");
                }

                sb.AppendLine($"**총 레이어 수**: {dataSource.GetLayerCount()}");
                sb.AppendLine();

                // 목차 생성
                sb.AppendLine("## 목차");
                sb.AppendLine();

                var layerNames = new List<string>();
                for (int i = 0; i < dataSource.GetLayerCount(); i++)
                {
                    using var layer = dataSource.GetLayerByIndex(i);
                    if (layer != null)
                    {
                        string layerName = layer.GetName();
                        layerNames.Add(layerName);
                        sb.AppendLine($"- [{layerName}](#{layerName.ToLower().Replace("_", "-")})");
                    }
                }
                sb.AppendLine();

                // 각 레이어 상세 정보
                sb.AppendLine("## 레이어 상세 정보");
                sb.AppendLine();

                foreach (var layerName in layerNames)
                {
                    using var layer = dataSource.GetLayerByName(layerName);
                    if (layer != null)
                    {
                        await ExtractLayerSchemaAsync(layer, sb);
                    }
                }

                _logger.LogInformation("File Geodatabase 스키마 추출 완료");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File Geodatabase 스키마 추출 실패: {GdbPath}", gdbPath);
                throw;
            }
        }

        /// <summary>
        /// 개별 레이어 스키마 추출
        /// </summary>
        private async Task ExtractLayerSchemaAsync(Layer layer, StringBuilder sb)
        {
            try
            {
                string layerName = layer.GetName();
                var layerDefn = layer.GetLayerDefn();
                var spatialRef = layer.GetSpatialRef();

                _logger.LogDebug("레이어 스키마 추출 중: {LayerName}", layerName);

                sb.AppendLine($"### {layerName}");
                sb.AppendLine();

                // 기본 정보
                sb.AppendLine("#### 기본 정보");
                sb.AppendLine();
                sb.AppendLine($"- **레이어명**: {layerName}");
                sb.AppendLine($"- **지오메트리 타입**: {layerDefn.GetGeomType()}");
                sb.AppendLine($"- **피처 개수**: {layer.GetFeatureCount(0)}");

                if (spatialRef != null)
                {
                    try
                    {
                        spatialRef.AutoIdentifyEPSG();
                        string authName = spatialRef.GetAuthorityName(null) ?? "";
                        string authCode = spatialRef.GetAuthorityCode(null) ?? "";
                        
                        if (!string.IsNullOrEmpty(authName) && !string.IsNullOrEmpty(authCode))
                        {
                            sb.AppendLine($"- **좌표계**: {authName}:{authCode}");
                        }
                        else
                        {
                            sb.AppendLine($"- **좌표계**: 정의되지 않음");
                        }
                    }
                    catch
                    {
                        sb.AppendLine($"- **좌표계**: 확인 불가");
                    }
                }
                else
                {
                    sb.AppendLine($"- **좌표계**: 없음");
                }
                sb.AppendLine();

                // 필드 정보
                sb.AppendLine("#### 필드 정보");
                sb.AppendLine();
                sb.AppendLine("| 순번 | 필드명 | 타입 | 길이 | 정밀도 | Nullable | 기본값 |");
                sb.AppendLine("|------|--------|------|------|--------|----------|--------|");

                for (int j = 0; j < layerDefn.GetFieldCount(); j++)
                {
                    using var fieldDefn = layerDefn.GetFieldDefn(j);

                    string fieldName = fieldDefn.GetName();
                    string fieldType = fieldDefn.GetFieldTypeName(fieldDefn.GetFieldType());
                    int width = fieldDefn.GetWidth();
                    int precision = fieldDefn.GetPrecision();
                    bool isNullable = fieldDefn.IsNullable() != 0;
                    string defaultValue = fieldDefn.GetDefault() ?? "";

                    sb.AppendLine($"| {j + 1} | {fieldName} | {fieldType} | {width} | {precision} | {(isNullable ? "Y" : "N")} | {defaultValue} |");
                }
                sb.AppendLine();

                // 샘플 데이터 (첫 3개 레코드)
                sb.AppendLine("#### 샘플 데이터");
                sb.AppendLine();

                layer.ResetReading();
                int sampleCount = 0;

                while (sampleCount < 3)
                {
                    using var feature = layer.GetNextFeature();
                    if (feature == null) break;

                    sb.AppendLine($"**레코드 {sampleCount + 1}:**");
                    sb.AppendLine();

                    for (int j = 0; j < layerDefn.GetFieldCount(); j++)
                    {
                        using var fieldDefn = layerDefn.GetFieldDefn(j);
                        string fieldName = fieldDefn.GetName();
                        string fieldValue = "";
                        
                        try
                        {
                            fieldValue = feature.GetFieldAsString(j) ?? "NULL";
                        }
                        catch
                        {
                            fieldValue = "읽기 오류";
                        }

                        // 긴 값은 잘라서 표시
                        if (fieldValue.Length > 50)
                        {
                            fieldValue = fieldValue.Substring(0, 47) + "...";
                        }

                        sb.AppendLine($"- **{fieldName}**: {fieldValue}");
                    }
                    sb.AppendLine();
                    sampleCount++;
                }

                sb.AppendLine("---");
                sb.AppendLine();

                await Task.Delay(1); // 비동기 처리를 위한 최소 지연
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "레이어 스키마 추출 실패: {LayerName}", layer.GetName());
                sb.AppendLine($"### {layer.GetName()} - 오류 발생");
                sb.AppendLine();
                sb.AppendLine($"**오류**: {ex.Message}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        /// <summary>
        /// 스키마를 파일로 저장
        /// </summary>
        public async Task SaveSchemaToFileAsync(string gdbPath, string outputPath)
        {
            try
            {
                string schemaContent = await ExtractAllFeatureClassSchemasAsync(gdbPath);
                await File.WriteAllTextAsync(outputPath, schemaContent, Encoding.UTF8);
                _logger.LogInformation("스키마 파일 저장 완료: {OutputPath}", outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스키마 파일 저장 실패: {OutputPath}", outputPath);
                throw;
            }
        }
    }
}