using CsvHelper;
using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Config;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Globalization;
using SpatialCheckPro.Services;

namespace SpatialCheckPro.Processors
{
    /// <summary>
    /// 속성 검수 프로세서
    /// </summary>
    public class AttributeCheckProcessor : IAttributeCheckProcessor
    {
        private readonly ILogger<AttributeCheckProcessor> _logger;
        private Dictionary<string, HashSet<string>>? _codelistCache;

        public AttributeCheckProcessor(ILogger<AttributeCheckProcessor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 코드리스트 파일을 로드합니다.
        /// </summary>
        public void LoadCodelist(string? codelistPath)
        {
            _codelistCache = null;

            if (string.IsNullOrWhiteSpace(codelistPath) || !File.Exists(codelistPath))
            {
                _logger.LogInformation("코드리스트 파일이 지정되지 않았거나 존재하지 않습니다: {Path}", codelistPath);
                return;
            }

            try
            {
                _codelistCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                
                using var reader = new StreamReader(codelistPath, System.Text.Encoding.UTF8);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                
                csv.Read();
                csv.ReadHeader();
                
                while (csv.Read())
                {
                    var codeSetId = csv.GetField<string>("CodeSetId");
                    var codeValue = csv.GetField<string>("CodeValue");
                    
                    if (string.IsNullOrWhiteSpace(codeSetId) || string.IsNullOrWhiteSpace(codeValue))
                        continue;
                    
                    if (!_codelistCache.ContainsKey(codeSetId))
                    {
                        _codelistCache[codeSetId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    
                    _codelistCache[codeSetId].Add(codeValue);
                }
                
                _logger.LogInformation("코드리스트 로드 완료: {Count}개 코드셋", _codelistCache.Count);
                foreach (var kvp in _codelistCache)
                {
                    _logger.LogDebug("코드셋 {CodeSetId}: {Count}개 코드", kvp.Key, kvp.Value.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "코드리스트 파일 로드 중 오류 발생: {Path}", codelistPath);
                _codelistCache = null;
            }
        }

        /// <summary>
        /// 필드 정의에서 대소문자 무시로 인덱스를 찾습니다. 없으면 -1 반환
        /// </summary>
        private static int GetFieldIndexIgnoreCase(FeatureDefn def, string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return -1;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                using var fd = def.GetFieldDefn(i);
                if (fd.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public async Task<List<ValidationError>> ValidateAsync(string gdbPath, IValidationDataProvider dataProvider, List<AttributeCheckConfig> rules, CancellationToken token = default)
        {
            var errors = new List<ValidationError>();

            // 임시 수정: dataProvider에서 gdbPath를 직접 얻을 수 없으므로, gdbPath 파라미터를 그대로 사용합니다.
            // 이상적으로는 dataProvider를 통해 데이터를 읽어야 합니다.
            using var ds = Ogr.Open(gdbPath, 0);
            if (ds == null) return errors;

            // GDB의 모든 레이어 로깅
            _logger.LogInformation("GDB에 포함된 레이어 목록:");
            for (int i = 0; i < ds.GetLayerCount(); i++)
            {
                using var layer = ds.GetLayerByIndex(i);
                if (layer != null)
                {
                    _logger.LogInformation("  - 레이어 [{Index}]: {Name}", i, layer.GetName());
                }
            }

            foreach (var rule in rules.Where(r => string.Equals(r.Enabled, "Y", StringComparison.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                
                _logger.LogDebug("속성 검수 규칙 처리 시작: RuleId={RuleId}, TableId={TableId}, FieldName={FieldName}, CheckType={CheckType}", 
                    rule.RuleId, rule.TableId, rule.FieldName, rule.CheckType);

                var layer = GetLayerByIdOrName(ds, rule.TableId, rule.TableName);
                if (layer == null)
                {
                    _logger.LogWarning("속성 검수: 레이어를 찾지 못했습니다: {TableId}/{TableName}", rule.TableId, rule.TableName);
                    // 테이블명/아이디가 대소문자/접두사 차이로 불일치할 수 있어, 전체 레이어명 로깅으로 원인 파악 도움
                    try
                    {
                        var names = new List<string>();
                        for (int i = 0; i < ds.GetLayerCount(); i++)
                        {
                            using var ly = ds.GetLayerByIndex(i);
                            if (ly != null) names.Add(ly.GetName());
                        }
                        _logger.LogInformation("GDB 레이어 목록: {Layers}", string.Join(", ", names));
                    }
                    catch { }
                    continue;
                }
                
                var featureCount = layer.GetFeatureCount(1);
                _logger.LogDebug("레이어 {LayerName} 피처 수: {Count}", layer.GetName(), featureCount);

                var defn = layer.GetLayerDefn();
                
                // 레이어의 모든 필드 목록 로깅 (디버깅용)
                var fieldNames = new List<string>();
                for (int i = 0; i < defn.GetFieldCount(); i++)
                {
                    using var fd = defn.GetFieldDefn(i);
                    fieldNames.Add(fd.GetName());
                }
                _logger.LogDebug("레이어 {LayerName} 필드 목록: {Fields}", layer.GetName(), string.Join(", ", fieldNames));
                
                // 메인 필드 인덱스 조회(대소문자 무시)
                int fieldIndex = GetFieldIndexIgnoreCase(defn, rule.FieldName);
                if (fieldIndex == -1)
                {
                    _logger.LogWarning("속성 검수: 필드를 찾지 못했습니다: {TableId}.{Field}", rule.TableId, rule.FieldName);
                    try
                    {
                        var fieldList = new List<string>();
                        for (int i = 0; i < defn.GetFieldCount(); i++)
                        {
                            using var fd2 = defn.GetFieldDefn(i);
                            fieldList.Add(fd2.GetName());
                        }
                        _logger.LogInformation("테이블 {TableId} 필드 목록: {Fields}", rule.TableId, string.Join(", ", fieldList));
                    }
                    catch { }
                    continue;
                }

                layer.ResetReading();
                Feature? f;
                while ((f = layer.GetNextFeature()) != null)
                {
                    using (f)
                    {
                        var fid = f.GetFID().ToString(CultureInfo.InvariantCulture);
                        // NULL은 규칙별로 다르게 해석할 수 있도록 null 보전
                        string? value = f.IsFieldNull(fieldIndex) ? null : f.GetFieldAsString(fieldIndex);

                        var checkType = rule.CheckType?.Trim() ?? string.Empty;

                        if (checkType.Equals("ifmultipleofthencodein", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: NumericField;base;CodeField;code1|code2
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            var numericField = p.Length > 0 && !string.IsNullOrWhiteSpace(p[0]) ? p[0] : rule.FieldName;
                            double baseVal = (p.Length > 1 && double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var bv)) ? bv : 25;
                            var codeField = p.Length > 2 ? p[2] : string.Empty;
                            var codeList = p.Length > 3 ? p[3] : string.Empty;
                            var allowed = new HashSet<string>((codeList ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

                            _logger.LogDebug("IfMultipleOfThenCodeIn 검수: RuleId={RuleId}, NumericField={NumericField}, BaseVal={BaseVal}, CodeField={CodeField}, AllowedCodes={AllowedCodes}", 
                                rule.RuleId, numericField, baseVal, codeField, string.Join("|", allowed));

                            var def = f.GetDefnRef();
                            int idxNum = GetFieldIndexIgnoreCase(def, numericField);
                            int idxCode = GetFieldIndexIgnoreCase(def, codeField);
                            if (idxNum >= 0 && idxCode >= 0)
                            {
                                var valStr = f.IsFieldNull(idxNum) ? string.Empty : f.GetFieldAsString(idxNum) ?? string.Empty;
                                var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                bool isMultiple = IsMultiple(valStr, baseVal);
                                bool violation = isMultiple && !allowed.Contains(code);
                                
                                _logger.LogDebug("  FID={FID}, {NumericField}={Value}, IsMultipleOf{BaseVal}={IsMultiple}, {CodeField}={Code}, Violation={Violation}", 
                                    fid, numericField, valStr, baseVal, isMultiple, codeField, code, violation);
                                
                                if (violation)
                                {
                                    errors.Add(new ValidationError
                                    {
                                        ErrorCode = rule.CheckType,
                                        Message = $"{numericField}가 {baseVal}의 배수인 경우 {codeField}는 ({string.Join(',', allowed)}) 이어야 함. 현재='{code}'",
                                        TableName = rule.TableId,
                                        FeatureId = fid,
                                        FieldName = rule.FieldName,
                                        Severity = ParseSeverity(rule.Severity)
                                    });
                                }
                            }
                            else
                            {
                                _logger.LogWarning("IfMultipleOfThenCodeIn 검수 필드 인덱스 오류: NumericField={NumericField}(idx={IdxNum}), CodeField={CodeField}(idx={IdxCode})", 
                                    numericField, idxNum, codeField, idxCode);
                            }
                            continue; // 다음 피처
                        }

                        // 조건부: 특정 코드인 경우 수치값이 지정 배수여야 함
                        if (checkType.Equals("ifcodethenmultipleof", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: CodeField;codes;NumericField;base
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            var codeField = p.Length > 0 ? p[0] : string.Empty;
                            var codeList = p.Length > 1 ? p[1] : string.Empty;
                            var numericField = p.Length > 2 ? p[2] : rule.FieldName;
                            double baseVal = (p.Length > 3 && double.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bv)) ? bv : 25;
                            var allowed = new HashSet<string>((codeList ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

                            _logger.LogDebug("IfCodeThenMultipleOf 검수: RuleId={RuleId}, CodeField={CodeField}, AllowedCodes={AllowedCodes}, NumericField={NumericField}, BaseVal={BaseVal}", 
                                rule.RuleId, codeField, string.Join("|", allowed), numericField, baseVal);

                            var def = f.GetDefnRef();
                            int idxCode = GetFieldIndexIgnoreCase(def, codeField);
                            int idxNum = GetFieldIndexIgnoreCase(def, numericField);
                            if (idxCode >= 0 && idxNum >= 0)
                            {
                                var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                var valStr = f.IsFieldNull(idxNum) ? string.Empty : f.GetFieldAsString(idxNum) ?? string.Empty;
                                bool isTargetCode = allowed.Contains(code);
                                bool isMultiple = IsMultiple(valStr, baseVal);
                                bool violation = isTargetCode && !isMultiple;
                                
                                _logger.LogDebug("  FID={FID}, {CodeField}={Code}, IsTargetCode={IsTargetCode}, {NumericField}={Value}, IsMultipleOf{BaseVal}={IsMultiple}, Violation={Violation}", 
                                    fid, codeField, code, isTargetCode, numericField, valStr, baseVal, isMultiple, violation);
                                
                                if (violation)
                                {
                                    errors.Add(new ValidationError
                                    {
                                        ErrorCode = rule.CheckType,
                                        Message = $"{codeField}가 ({string.Join(',', allowed)})인 경우 {numericField}는 {baseVal}의 배수여야 함. 현재='{valStr}'",
                                        TableName = rule.TableId,
                                        FeatureId = fid,
                                        FieldName = rule.FieldName,
                                        Severity = ParseSeverity(rule.Severity)
                                    });
                                }
                            }
                            else
                            {
                                _logger.LogWarning("IfCodeThenMultipleOf 검수 필드 인덱스 오류: CodeField={CodeField}(idx={IdxCode}), NumericField={NumericField}(idx={IdxNum})", 
                                    codeField, idxCode, numericField, idxNum);
                            }
                            continue; // 다음 피처
                        }

                        // 조건부: 특정 코드인 경우 수치값이 지정값과 같아야 함
                        if (checkType.Equals("ifcodethennumericequals", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: CodeField;codes;NumericField;value
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            if (p.Length >= 4)
                            {
                                var codeField = p[0];
                                var codes = new HashSet<string>(p[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                                var numericField = p[2];
                                var targetStr = p[3];
                                var def = f.GetDefnRef();
                                int idxCode = GetFieldIndexIgnoreCase(def, codeField);
                                int idxNum = GetFieldIndexIgnoreCase(def, numericField);
                                if (idxCode >= 0 && idxNum >= 0)
                                {
                                    var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                    if (codes.Contains(code))
                                    {
                                        var numStr = f.IsFieldNull(idxNum) ? string.Empty : f.GetFieldAsString(idxNum) ?? string.Empty;
                                        if (!double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num) ||
                                            !double.TryParse(targetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var target) || Math.Abs(num - target) > 1e-9)
                                        {
                                            errors.Add(new ValidationError
                                            {
                                                ErrorCode = rule.CheckType,
                                                Message = $"{codeField}가 지정코드({string.Join(',', codes)})인 경우 {numericField} = {targetStr} 이어야 함. 현재='{numStr}'",
                                                TableName = rule.TableId,
                                                FeatureId = fid,
                                                FieldName = numericField,
                                                Severity = ParseSeverity(rule.Severity)
                                            });
                                        }
                                    }
                                }
                            }
                            continue;
                        }

                        // 조건부: 특정 코드인 경우 수치값이 범위 내(배타)여야 함 (min < value < max)
                        if (checkType.Equals("ifcodethenbetweenexclusive", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: CodeField;codes;NumericField;min..max
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            if (p.Length >= 4)
                            {
                                var codeField = p[0];
                                var codes = new HashSet<string>(p[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                                var numericField = p[2];
                                var range = p[3].Split("..", StringSplitOptions.None);
                                double? min = range.Length > 0 && double.TryParse(range[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var mn) ? mn : null;
                                double? max = range.Length > 1 && double.TryParse(range[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var mx) ? mx : null;
                                var def = f.GetDefnRef();
                                int idxCode = def.GetFieldIndex(codeField);
                                int idxNum = def.GetFieldIndex(numericField);
                                if (idxCode >= 0 && idxNum >= 0)
                                {
                                    var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                    if (codes.Contains(code))
                                    {
                                        var numStr = f.IsFieldNull(idxNum) ? string.Empty : f.GetFieldAsString(idxNum) ?? string.Empty;
                                        if (!double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                                        {
                                            errors.Add(new ValidationError
                                            {
                                                ErrorCode = rule.CheckType,
                                                Message = $"{numericField} 값 파싱 실패",
                                                TableName = rule.TableId,
                                                FeatureId = fid,
                                                FieldName = numericField,
                                                Severity = ParseSeverity(rule.Severity)
                                            });
                                        }
                                        else
                                        {
                                            bool ok = true;
                                            if (min.HasValue && !(num > min.Value)) ok = false;
                                            if (max.HasValue && !(num < max.Value)) ok = false;
                                            if (!ok)
                                            {
                                                errors.Add(new ValidationError
                                                {
                                                    ErrorCode = rule.CheckType,
                                                    Message = $"{codeField}가 지정코드({string.Join(',', codes)})인 경우 {numericField}는 {min}~{max} (배타) 범위여야 함. 현재='{numStr}'",
                                                    TableName = rule.TableId,
                                                    FeatureId = fid,
                                                    FieldName = numericField,
                                                    Severity = ParseSeverity(rule.Severity)
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            continue;
                        }

                        // 조건부: 특정 코드인 경우 지정된 필드는 NULL이어야 함
                        if (checkType.Equals("ifcodethennull", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: CodeField;codes;TargetField
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            if (p.Length >= 3)
                            {
                                var codeField = p[0];
                                var codes = new HashSet<string>(p[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                                var targetField = p[2];
                                var def = f.GetDefnRef();
                                int idxCode = GetFieldIndexIgnoreCase(def, codeField);
                                int idxTarget = GetFieldIndexIgnoreCase(def, targetField);
                                
                                _logger.LogDebug("IfCodeThenNull 검사: RuleId={RuleId}, CodeField={CodeField}, Codes={Codes}, TargetField={TargetField}, FeatureId={FeatureId}", 
                                    rule.RuleId, codeField, string.Join("|", codes), targetField, fid);
                                
                                if (idxCode >= 0 && idxTarget >= 0)
                                {
                                    var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                    _logger.LogDebug("코드 필드 값: {CodeField}={Code}, 조건 코드에 포함: {IsMatch}", codeField, code, codes.Contains(code));
                                    
                                    if (codes.Contains(code))
                                    {
                                        var targetValue = f.IsFieldNull(idxTarget) ? null : f.GetFieldAsString(idxTarget);
                                        var isNotNull = !f.IsFieldNull(idxTarget) && !string.IsNullOrWhiteSpace(targetValue);
                                        
                                        _logger.LogDebug("대상 필드 검사: {TargetField}={Value}, IsNull={IsNull}, IsNotNull={IsNotNull}", 
                                            targetField, targetValue, f.IsFieldNull(idxTarget), isNotNull);
                                        
                                        if (isNotNull)
                                        {
                                            _logger.LogWarning("IfCodeThenNull 오류 발견: {CodeField}={Code}인 경우 {TargetField}는 NULL이어야 함. 현재값: '{Value}'", 
                                                codeField, code, targetField, targetValue);
                                            errors.Add(new ValidationError
                                            {
                                                ErrorCode = rule.CheckType,
                                                Message = $"{codeField}가 지정코드({string.Join(',', codes)})인 경우 {targetField}는 NULL이어야 함. 현재값: '{targetValue}'",
                                                TableName = rule.TableId,
                                                FeatureId = fid,
                                                FieldName = targetField,
                                                Severity = ParseSeverity(rule.Severity),
                                                ActualValue = targetValue ?? "NULL",
                                                ExpectedValue = "NULL"
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    if (idxCode < 0) _logger.LogWarning("코드 필드 '{CodeField}'를 레이어에서 찾을 수 없습니다.", codeField);
                                    if (idxTarget < 0) _logger.LogWarning("대상 필드 '{TargetField}'를 레이어에서 찾을 수 없습니다.", targetField);
                                }
                            }
                            continue;
                        }

                        // 조건부: 특정 코드인 경우 지정된 모든 필드는 NotNull 이어야 함
                        if (checkType.Equals("ifcodethennotnullall", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parameters: CodeField;codes;Field1|Field2|...
                            var p = (rule.Parameters ?? string.Empty).Split(';');
                            if (p.Length >= 3)
                            {
                                var codeField = p[0];
                                var codes = new HashSet<string>(p[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                                var fields = p[2].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                var def = f.GetDefnRef();
                                int idxCode = def.GetFieldIndex(codeField);
                                
                                _logger.LogDebug("IfCodeThenNotNullAll 검사: RuleId={RuleId}, CodeField={CodeField}, Codes={Codes}, Fields={Fields}, FeatureId={FeatureId}", 
                                    rule.RuleId, codeField, string.Join("|", codes), string.Join("|", fields), fid);
                                
                                if (idxCode >= 0)
                                {
                                    var code = f.GetFieldAsString(idxCode) ?? string.Empty;
                                    _logger.LogDebug("코드 필드 값: {CodeField}={Code}, 조건 코드에 포함: {IsMatch}", codeField, code, codes.Contains(code));
                                    
                                    if (codes.Contains(code))
                                    {
                                        _logger.LogDebug("조건부 검사 실행: {CodeField}={Code}가 조건 코드에 해당", codeField, code);
                                        foreach (var fld in fields)
                                        {
                                            int idx = GetFieldIndexIgnoreCase(def, fld);
                                            if (idx >= 0)
                                            {
                                                var fieldValue = f.IsFieldNull(idx) ? null : f.GetFieldAsString(idx);
                                                var isEmpty = string.IsNullOrEmpty(fieldValue);
                                                var isWhitespace = !isEmpty && string.IsNullOrWhiteSpace(fieldValue);
                                                _logger.LogDebug("필드 검사: {Field}={Value}, IsNull={IsNull}, IsEmpty={IsEmpty}, IsWhitespace={IsWhitespace}", 
                                                    fld, fieldValue, f.IsFieldNull(idx), isEmpty, isWhitespace);
                                                
                                                // NULL이거나 빈 문자열이거나 공백 문자열인 경우 오류
                                                if (idx < 0 || f.IsFieldNull(idx) || isEmpty || isWhitespace)
                                                {
                                                    var displayValue = fieldValue ?? "NULL";
                                                    if (isWhitespace) displayValue = $"'{fieldValue}' (공백)";
                                                    
                                                    _logger.LogWarning("IfCodeThenNotNullAll 오류 발견: {CodeField}={Code}인 경우 {Field}는 필수값이어야 함. 현재값: {DisplayValue}", 
                                                        codeField, code, fld, displayValue);
                                                    errors.Add(new ValidationError
                                                    {
                                                        ErrorCode = rule.CheckType,
                                                        Message = $"{codeField}가 지정코드({string.Join(',', codes)})인 경우 {fld}는 필수값이어야 함. 현재값: {displayValue}",
                                                        TableName = rule.TableId,
                                                        FeatureId = fid,
                                                        FieldName = fld,
                                                        Severity = ParseSeverity(rule.Severity),
                                                        ActualValue = displayValue,
                                                        ExpectedValue = "NOT NULL AND NOT BLANK"
                                                    });
                                                }
                                            }
                                            else
                                            {
                                                _logger.LogWarning("필드 '{Field}'를 레이어에서 찾을 수 없습니다.", fld);
                                            }
                                        }
                                    }
                                }
                            }
                            continue;
                        }

                        if (!CheckValue(rule, value, _codelistCache))
                        {
                            errors.Add(new ValidationError
                            {
                                ErrorCode = rule.CheckType,
                                Message = $"{rule.TableId}.{rule.FieldName} 값 검증 실패: '{value}' (규칙: {rule.Parameters})",
                                TableName = rule.TableId,
                                FeatureId = fid,
                                FieldName = rule.FieldName,
                                Severity = ParseSeverity(rule.Severity)
                            });
                        }
                    }
                }
            }

            return await Task.FromResult(errors);
        }

        /// <summary>
        /// 단일 속성 검수 규칙을 처리합니다 (병렬 처리용)
        /// </summary>
        public async Task<List<ValidationError>> ValidateSingleRuleAsync(string gdbPath, IValidationDataProvider dataProvider, AttributeCheckConfig rule)
        {
            var errors = new List<ValidationError>();

            try
            {
                if (!string.Equals(rule.Enabled, "Y", StringComparison.OrdinalIgnoreCase))
                {
                    return errors;
                }

                _logger.LogDebug("단일 속성 검수 규칙 처리: RuleId={RuleId}, TableId={TableId}, FieldName={FieldName}, CheckType={CheckType}", 
                    rule.RuleId, rule.TableId, rule.FieldName, rule.CheckType);

                // 임시 수정: dataProvider 대신 gdbPath 사용
                using var ds = Ogr.Open(gdbPath, 0);
                if (ds == null) return errors;

                var layer = GetLayerByIdOrName(ds, rule.TableId, rule.TableName);
                if (layer == null)
                {
                    _logger.LogWarning("레이어를 찾을 수 없습니다: TableId={TableId}, TableName={TableName}", rule.TableId, rule.TableName);
                    return errors;
                }

                var fieldIndex = GetFieldIndexIgnoreCase(layer.GetLayerDefn(), rule.FieldName);
                if (fieldIndex == -1)
                {
                    _logger.LogWarning("필드를 찾을 수 없습니다: TableId={TableId}, FieldName={FieldName}", rule.TableId, rule.FieldName);
                    return errors;
                }

                // 필터는 현재 AttributeCheckConfig에 없으므로 제거

                // 레코드 처리
                layer.ResetReading();
                Feature? feature = layer.GetNextFeature();
                while (feature != null)
                {
                    var fid = feature.GetFID();
                    var value = feature.GetFieldAsString(fieldIndex);

                    if (!CheckValue(rule, value, _codelistCache))
                    {
                        errors.Add(new ValidationError
                        {
                            ErrorCode = rule.CheckType,
                            Message = $"{rule.TableId}.{rule.FieldName} 값 검증 실패: '{value}' (규칙: {rule.Parameters})",
                            TableName = rule.TableId,
                            FeatureId = fid.ToString(),
                            FieldName = rule.FieldName,
                            Severity = ParseSeverity(rule.Severity)
                        });
                    }

                    feature.Dispose();
                    feature = layer.GetNextFeature();
                }

                _logger.LogDebug("단일 속성 검수 규칙 완료: RuleId={RuleId}, ErrorCount={ErrorCount}", rule.RuleId, errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "단일 속성 검수 규칙 처리 중 오류: RuleId={RuleId}", rule.RuleId);
            }

            return await Task.FromResult(errors);
        }

        /// <summary>
        /// 테이블ID 또는 테이블명으로 레이어를 찾습니다(대소문자 무시). 실패 시 null 반환
        /// </summary>
        private static Layer? GetLayerByIdOrName(DataSource ds, string? tableId, string? tableName)
        {
            string id = tableId?.Trim() ?? string.Empty;
            string name = tableName?.Trim() ?? string.Empty;

            // 1) 정확 일치 시도 (대소문자 그대로)
            if (!string.IsNullOrEmpty(id))
            {
                var l = ds.GetLayerByName(id);
                if (l != null) return l;
            }
            if (!string.IsNullOrEmpty(name))
            {
                var l = ds.GetLayerByName(name);
                if (l != null) return l;
            }

            // 2) 대소문자 무시 매칭 (전체 스캔)
            var targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(id)) targetSet.Add(id);
            if (!string.IsNullOrEmpty(name)) targetSet.Add(name);

            for (int i = 0; i < ds.GetLayerCount(); i++)
            {
                var lyr = ds.GetLayerByIndex(i);
                if (lyr == null) continue;
                var lname = lyr.GetName() ?? string.Empty;
                if (targetSet.Contains(lname)) return lyr;
            }

            // 3) 일부 환경에서 스키마 접두사 등으로 이름이 바뀌는 경우 대비: 끝/시작 포함 비교
            for (int i = 0; i < ds.GetLayerCount(); i++)
            {
                var lyr = ds.GetLayerByIndex(i);
                if (lyr == null) continue;
                var lname = lyr.GetName() ?? string.Empty;
                if (!string.IsNullOrEmpty(id) && lname.EndsWith(id, StringComparison.OrdinalIgnoreCase)) return lyr;
                if (!string.IsNullOrEmpty(name) && lname.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return lyr;
            }

            return null;
        }

        private static ErrorSeverity ParseSeverity(string? s)
        {
            var up = s?.ToUpperInvariant();
            if (up == "CRIT" || up == "CRITICAL") return ErrorSeverity.Critical;
            if (up == "MAJOR") return ErrorSeverity.Error; // 매핑: Major→Error
            if (up == "MINOR") return ErrorSeverity.Warning; // 매핑: Minor→Warning
            if (up == "INFO") return ErrorSeverity.Info;
            return ErrorSeverity.Error;
        }

        private static bool CheckValue(AttributeCheckConfig rule, string? value, Dictionary<string, HashSet<string>>? codelistCache)
        {
            var type = rule.CheckType?.Trim();
            var param = rule.Parameters ?? string.Empty;
            switch (type?.ToLowerInvariant())
            {
                case "codelist":
                    {
                        // Parameters가 코드셋ID인 경우 codelist.csv에서 참조
                        if (codelistCache != null && codelistCache.ContainsKey(param))
                        {
                            return codelistCache[param].Contains(value ?? string.Empty);
                        }
                        // 기존 방식: 파이프로 구분된 코드 목록
                        var codes = param.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        return codes.Contains(value);
                    }
                case "range":
                    {
                        // 형식: min..max (비워두면 개방)
                        var parts = param.Split("..", StringSplitOptions.None);
                        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return false;
                        double? min = parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var mn) ? mn : null;
                        double? max = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var mx) ? mx : null;
                        if (min.HasValue && v < min.Value) return false;
                        if (max.HasValue && v > max.Value) return false;
                        return true;
                    }
                case "regex":
                    {
                        try
                        {
                            // NULL/빈값은 검사 비대상으로 통과
                            if (string.IsNullOrWhiteSpace(value)) return true;
                            return System.Text.RegularExpressions.Regex.IsMatch(value, param);
                        }
                        catch { return false; }
                    }
                case "regexnot":
                    {
                        // NULL/빈값은 오류로 보지 않음(통과)
                        if (string.IsNullOrWhiteSpace(value)) return true;
                        try
                        {
                            return !System.Text.RegularExpressions.Regex.IsMatch(value, param);
                        }
                        catch { return true; }
                    }
                case "notnull":
                    return !string.IsNullOrWhiteSpace(value);
                case "notjamoonly":
                    {
                        var s = (value ?? string.Empty).Trim();
                        // NULL/빈값은 이 규칙 대상 아님(통과)
                        if (string.IsNullOrEmpty(s)) return true;
                        // 자음/모음이 포함된 경우 false (오류) - 혼합된 경우도 포함
                        return !System.Text.RegularExpressions.Regex.IsMatch(s, ".*[ㄱ-ㅎㅏ-ㅣ].*");
                    }
                case "notzero":
                    {
                        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return false;
                        return Math.Abs(v) > 1e-12;
                    }
                case "multipleof":
                    {
                        // Parameters: baseValue (예: 5)
                        if (!double.TryParse(param, NumberStyles.Any, CultureInfo.InvariantCulture, out var b)) return false;
                        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return false;
                        var q = Math.Round(v / b);
                        return Math.Abs(v - q * b) < 1e-9;
                    }
                default:
                    return true; // 알 수 없는 규칙은 통과
            }
        }

        private static bool IsMultiple(string value, double baseVal)
        {
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return false;
            var q = Math.Round(v / baseVal);
            return Math.Abs(v - q * baseVal) < 1e-9;
        }
    }
}


