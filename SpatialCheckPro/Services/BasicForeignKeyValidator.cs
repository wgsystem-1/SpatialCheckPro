using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 기본 외래키 검증 구현체
    /// </summary>
    public class BasicForeignKeyValidator : IForeignKeyValidator
    {
        private readonly ILogger<BasicForeignKeyValidator> _logger;

        public BasicForeignKeyValidator(ILogger<BasicForeignKeyValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 외래키 참조 무결성 검사를 수행합니다
        /// </summary>
        public async Task<ForeignKeyValidationResult> ValidateForeignKeyAsync(
            string gdbPath, 
            string sourceTable, 
            string sourceField,
            string referenceTable, 
            string referenceField)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(referenceTable) || string.IsNullOrEmpty(referenceField))
                    {
                        return new ForeignKeyValidationResult
                        {
                            IsValid = true,
                            Message = "참조 테이블 정보가 없어 FK 검사를 스킵했습니다"
                        };
                    }

                    _logger.LogInformation("FK 참조 무결성 검사 시작: {SourceTable}.{SourceField} -> {RefTable}.{RefField}", 
                        sourceTable, sourceField, referenceTable, referenceField);

                    using var dataSource = Ogr.Open(gdbPath, 0);
                    if (dataSource == null)
                    {
                        return new ForeignKeyValidationResult
                        {
                            IsValid = false,
                            Message = "File Geodatabase를 열 수 없습니다"
                        };
                    }

                    // 참조 테이블의 모든 값 수집
                    var referenceValues = GetAllFieldValues(dataSource, referenceTable, referenceField);
                    if (referenceValues == null)
                    {
                        return new ForeignKeyValidationResult
                        {
                            IsValid = false,
                            Message = $"참조 테이블 '{referenceTable}' 또는 필드 '{referenceField}'를 찾을 수 없습니다"
                        };
                    }

                    var referenceSet = new HashSet<string>(referenceValues);

                    // 소스 테이블에서 고아 레코드 찾기
                    var sourceValues = GetAllFieldValues(dataSource, sourceTable, sourceField);
                    if (sourceValues == null)
                    {
                        return new ForeignKeyValidationResult
                        {
                            IsValid = false,
                            Message = $"소스 테이블 '{sourceTable}' 또는 필드 '{sourceField}'를 찾을 수 없습니다"
                        };
                    }

                    var orphanValues = new List<string>();
                    foreach (var value in sourceValues)
                    {
                        if (!string.IsNullOrEmpty(value) && !referenceSet.Contains(value))
                        {
                            orphanValues.Add(value);
                        }
                    }

                    _logger.LogInformation("FK 참조 무결성 검사 완료: {OrphanCount}개 고아 레코드", orphanValues.Count);

                    return new ForeignKeyValidationResult
                    {
                        IsValid = orphanValues.Count == 0,
                        OrphanCount = orphanValues.Count,
                        OrphanValues = orphanValues.Take(10).ToList(),
                        Message = orphanValues.Count == 0 ? "참조 무결성 정상" : $"{orphanValues.Count}개 고아 레코드 발견"
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FK 참조 무결성 검사 실패");
                    return new ForeignKeyValidationResult
                    {
                        IsValid = false,
                        Message = $"FK 검사 중 오류 발생: {ex.Message}"
                    };
                }
            });
        }

        /// <summary>
        /// 테이블의 특정 필드 모든 값을 가져옵니다
        /// </summary>
        private List<string>? GetAllFieldValues(DataSource dataSource, string tableName, string fieldName)
        {
            try
            {
                using var layer = dataSource.GetLayerByName(tableName);
                if (layer == null) return null;

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

                if (fieldIndex == -1) return null;

                var values = new List<string>();
                layer.ResetReading();

                Feature? feature;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        var value = feature.GetFieldAsString(fieldIndex);
                        if (!string.IsNullOrEmpty(value))
                        {
                            values.Add(value);
                        }
                    }
                }

                return values;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "필드 값 수집 실패: {TableName}.{FieldName}", tableName, fieldName);
                return null;
            }
        }
    }
}
