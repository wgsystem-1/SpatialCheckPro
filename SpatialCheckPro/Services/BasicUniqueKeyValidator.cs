using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 기본 고유키 검증 구현체
    /// </summary>
    public class BasicUniqueKeyValidator : IUniqueKeyValidator
    {
        private readonly ILogger<BasicUniqueKeyValidator> _logger;

#pragma warning disable CS0067 // 이벤트가 사용되지 않음 - 인터페이스 요구사항
        public event EventHandler<UniqueKeyValidationProgressEventArgs>? ProgressUpdated;
#pragma warning restore CS0067

        public BasicUniqueKeyValidator(ILogger<BasicUniqueKeyValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 고유키 중복 검사를 수행합니다
        /// </summary>
        public async Task<UniqueKeyValidationResult> ValidateUniqueKeyAsync(string gdbPath, string tableName, string fieldName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("UK 중복 검사 시작: {TableName}.{FieldName}", tableName, fieldName);

                    using var dataSource = Ogr.Open(gdbPath, 0);
                    if (dataSource == null)
                    {
                    return new UniqueKeyValidationResult
                    {
                        IsValid = false,
                        Message = "File Geodatabase를 열 수 없습니다",
                        TableName = tableName,
                        FieldName = fieldName
                    };
                    }

                    using var layer = dataSource.GetLayerByName(tableName);
                    if (layer == null)
                    {
                    return new UniqueKeyValidationResult
                    {
                        IsValid = false,
                        Message = $"테이블 '{tableName}'을 찾을 수 없습니다",
                        TableName = tableName,
                        FieldName = fieldName
                    };
                    }

                    // 필드 인덱스 찾기
                    var layerDefn = layer.GetLayerDefn();
                    int fieldIndex = -1;
                    for (int i = 0; i < layerDefn.GetFieldCount(); i++)
                    {
                        using var fieldDefn = layerDefn.GetFieldDefn(i);
                        if (fieldDefn.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            fieldIndex = i;
                            break;
                        }
                    }

                    if (fieldIndex == -1)
                    {
                    return new UniqueKeyValidationResult
                    {
                        IsValid = false,
                        Message = $"필드 '{fieldName}'을 찾을 수 없습니다",
                        TableName = tableName,
                        FieldName = fieldName
                    };
                    }

                    // 모든 값을 수집하여 중복 확인
                    var valueCount = new Dictionary<string, int>();
                    layer.ResetReading();

                    Feature? feature;
                    int processedCount = 0;
                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        using (feature)
                        {
                            var value = feature.GetFieldAsString(fieldIndex);
                            if (!string.IsNullOrEmpty(value))
                            {
                                valueCount[value] = valueCount.GetValueOrDefault(value, 0) + 1;
                            }
                            processedCount++;
                            
                            // 진행률 로깅 (1000개마다)
                            if (processedCount % 1000 == 0)
                            {
                                _logger.LogDebug("UK 검사 진행: {TableName}.{FieldName} - {ProcessedCount}개 레코드 처리됨", 
                                    tableName, fieldName, processedCount);
                            }
                        }
                    }

                    // 중복된 값들 찾기
                    var duplicates = valueCount.Where(kvp => kvp.Value > 1).ToList();
                    int totalDuplicates = duplicates.Sum(kvp => kvp.Value - 1); // 중복 개수 (원본 제외)
                    var duplicateValues = duplicates.Select(kvp => $"{kvp.Key}({kvp.Value}개)").Take(10).ToList();

                    _logger.LogInformation("UK 중복 검사 완료: {FieldName} - {DuplicateCount}개 중복 (총 {ProcessedCount}개 레코드 검사)", 
                        fieldName, totalDuplicates, processedCount);

                    return new UniqueKeyValidationResult
                    {
                        IsValid = totalDuplicates == 0,
                        DuplicateValues = totalDuplicates,
                        Duplicates = duplicates.Select(d => new DuplicateValueInfo 
                        { 
                            Value = d.Key, 
                            Count = d.Value 
                        }).ToList(),
                        Message = totalDuplicates == 0 ? "중복 없음" : $"{totalDuplicates}개 중복 발견",
                        TableName = tableName,
                        FieldName = fieldName,
                        TotalRecords = processedCount,
                        UniqueValues = valueCount.Count,
                        ValidatedAt = DateTime.Now
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UK 중복 검사 실패: {FieldName}", fieldName);
                    return new UniqueKeyValidationResult
                    {
                        IsValid = false,
                        Message = $"UK 검사 중 오류 발생: {ex.Message}",
                        TableName = tableName,
                        FieldName = fieldName
                    };
                }
            });
        }
    }
}
