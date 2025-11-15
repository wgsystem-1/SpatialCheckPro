using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// OBJFLTN_SE 기반 공통 피처 제외 로직 구현체
    /// </summary>
    public class FeatureFilterService : IFeatureFilterService
    {
        private readonly ILogger<FeatureFilterService> _logger;
        private readonly HashSet<string> _excludedCodes;

        /// <summary>
        /// 생성자
        /// </summary>
        public FeatureFilterService(ILogger<FeatureFilterService> logger, PerformanceSettings performanceSettings)
        {
            _logger = logger;
            _excludedCodes = new HashSet<string>(performanceSettings.ExcludedObjectChangeCodes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> ExcludedObjectChangeCodes => _excludedCodes;

        /// <inheritdoc />
        public FeatureFilterApplyResult ApplyObjectChangeFilter(Layer layer, string stageName, string tableName)
        {
            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            if (_excludedCodes.Count == 0)
            {
                return FeatureFilterApplyResult.NotApplied;
            }

            var defn = layer.GetLayerDefn();
            if (defn == null || !ContainsField(defn, "OBJFLTN_SE"))
            {
                return FeatureFilterApplyResult.NotApplied;
            }

            var sanitizedCodes = _excludedCodes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => SanitizeCode(c.Trim()))
                .ToArray();
            if (sanitizedCodes.Length == 0)
            {
                return FeatureFilterApplyResult.NotApplied;
            }
            var inclusionFilter = $"OBJFLTN_SE IN ({string.Join(",", sanitizedCodes.Select(c => $"'{c}'"))})";

            int excludedCount = 0;
            var inclusionResult = layer.SetAttributeFilter(inclusionFilter);
            if (inclusionResult == 0)
            {
                excludedCount = (int)layer.GetFeatureCount(1);
            }
            else
            {
                _logger.LogDebug("OBJFLTN_SE 포함 필터 적용 실패(Stage: {Stage}, Table: {Table}, ReturnCode: {Code})", stageName, tableName, inclusionResult);
            }

            // 필터 초기화 후 실제 제외 필터 적용
            layer.SetAttributeFilter(null);

            var exclusionFilter = $"(OBJFLTN_SE IS NULL) OR (OBJFLTN_SE NOT IN ({string.Join(",", sanitizedCodes.Select(c => $"'{c}'"))}))";
            var exclusionResult = layer.SetAttributeFilter(exclusionFilter);
            if (exclusionResult != 0)
            {
                _logger.LogWarning("OBJFLTN_SE 제외 필터 적용 실패(Stage: {Stage}, Table: {Table}, ReturnCode: {Code})", stageName, tableName, exclusionResult);
                layer.SetAttributeFilter(null);
                return FeatureFilterApplyResult.NotApplied;
            }

            layer.ResetReading();

            if (excludedCount > 0)
            {
                _logger.LogInformation("OBJFLTN_SE 코드 제외 적용(Stage: {Stage}, Table: {Table}) - 제외 코드: {Codes}, 제외 건수: {Count}",
                    stageName,
                    tableName,
                    string.Join(", ", _excludedCodes),
                    excludedCount);
            }

            return new FeatureFilterApplyResult(true, excludedCount, exclusionFilter);
        }

        /// <inheritdoc />
        public bool ShouldSkipFeature(Feature feature, string? layerName, out string? excludedCode)
        {
            excludedCode = null;
            if (feature == null || _excludedCodes.Count == 0)
            {
                return false;
            }

            var defn = feature.GetDefnRef();
            if (defn == null)
            {
                return false;
            }

            var fieldIndex = GetFieldIndexIgnoreCase(defn, "OBJFLTN_SE");
            if (fieldIndex < 0)
            {
                return false;
            }

            string? value = feature.IsFieldNull(fieldIndex) ? null : feature.GetFieldAsString(fieldIndex);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (_excludedCodes.Contains(trimmed))
            {
                excludedCode = trimmed;
                _logger.LogDebug("OBJFLTN_SE 제외 적용(Stage: Runtime, Layer: {Layer}, Code: {Code})",
                    layerName ?? string.Empty,
                    excludedCode);
                return true;
            }

            return false;
        }

        private static bool ContainsField(FeatureDefn defn, string fieldName)
        {
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                if (fd.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetFieldIndexIgnoreCase(FeatureDefn defn, string fieldName)
        {
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                if (fd.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string SanitizeCode(string code)
        {
            return code.Replace("'", "''");
        }
    }

    /// <summary>
    /// 피처 필터 적용 결과 모델
    /// </summary>
    public sealed record FeatureFilterApplyResult(bool Applied, int ExcludedCount, string? FilterExpression)
    {
        public static FeatureFilterApplyResult NotApplied { get; } = new(false, 0, null);
    }
}
