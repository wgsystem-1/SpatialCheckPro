# 🔧 Cursor AI 수정 지시서: 관계 검수 오류 지오메트리 추출

## 📋 수정 개요

### 목적
4단계(관계 검수), 5단계(속성관계 검수) 오류를 저장할 때 원본 FileGDB에서 지오메트리를 추출하여 **QC_Errors_Point 피처 테이블**에 저장되도록 수정

### 현재 문제점
- **4단계(REL)**: GeometryWKT가 없으면 NoGeom에 저장됨 (불확실)
- **5단계(ATTR_REL)**: 항상 X=0, Y=0으로 NoGeom에 저장됨 ❌

### 목표
- **1, 2단계**: QC_Errors_NoGeom 테이블에 저장 (현재 유지)
- **3, 4, 5단계**: QC_Errors_Point 피처 테이블에 저장 (수정 필요)

---

## 📁 수정 대상 파일

### 주요 파일
```
/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs
```

### 참조 파일 (수정 불필요, 참고용)
```
/home/user/SpatialCheckPro/SpatialCheckPro/Services/QcErrorService.cs
/home/user/SpatialCheckPro/SpatialCheckPro/Services/QcErrorDataService.cs
```

---

## 🔨 수정 작업 1: ConvertSpatialRelationErrorToQcError 메서드 (4단계)

### 파일 위치
`/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

### 수정 대상 메서드
**현재**: `ConvertSpatialRelationErrorToQcError` (120-160줄)

### 수정 내용

#### BEFORE (현재 코드)
```csharp
/// <summary>
/// 공간 관계 오류를 QcError로 변환합니다
/// </summary>
/// <param name="spatialError">공간 관계 오류</param>
/// <param name="runId">검수 실행 ID</param>
/// <returns>변환된 QcError</returns>
private QcError ConvertSpatialRelationErrorToQcError(SpatialRelationError spatialError, string runId)
{
    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "REL",
        ErrCode = GetSpatialRelationErrorCode(spatialError.RelationType, spatialError.ErrorType),
        Severity = ConvertErrorSeverityToString(spatialError.Severity),
        Status = "OPEN",
        RuleId = $"SPATIAL_{spatialError.RelationType}_{spatialError.ErrorType}",
        SourceClass = spatialError.SourceLayer,
        SourceOID = spatialError.SourceObjectId,
        SourceGlobalID = null, // 향후 구현
        X = spatialError.ErrorLocationX,
        Y = spatialError.ErrorLocationY,
        GeometryWKT = string.IsNullOrWhiteSpace(spatialError.GeometryWKT) ? null : spatialError.GeometryWKT,
        GeometryType = DetermineGeometryTypeFromWKT(spatialError.GeometryWKT).ToUpperInvariant(),
        ErrorValue = spatialError.TargetObjectId?.ToString() ?? "",
        ThresholdValue = spatialError.TargetLayer,
        Message = spatialError.Message,
        RunID = runId,
        CreatedUTC = spatialError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RelationType"] = spatialError.RelationType.ToString(),
        ["ErrorType"] = spatialError.ErrorType,
        ["SourceLayer"] = spatialError.SourceLayer,
        ["TargetLayer"] = spatialError.TargetLayer,
        ["SourceObjectId"] = spatialError.SourceObjectId,
        ["TargetObjectId"] = spatialError.TargetObjectId,
        ["DetectedAt"] = spatialError.DetectedAt,
        ["Properties"] = spatialError.Properties
    };

    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    return qcError;
}
```

#### AFTER (수정 후 코드)
```csharp
/// <summary>
/// 공간 관계 오류를 QcError로 변환합니다 (원본 FGDB에서 지오메트리 추출)
/// </summary>
/// <param name="spatialError">공간 관계 오류</param>
/// <param name="runId">검수 실행 ID</param>
/// <param name="sourceGdbPath">원본 FileGDB 경로</param>
/// <returns>변환된 QcError</returns>
private async Task<QcError> ConvertSpatialRelationErrorToQcErrorAsync(
    SpatialRelationError spatialError,
    string runId,
    string sourceGdbPath)
{
    // 원본 FGDB에서 지오메트리 추출
    var (geometry, x, y, geometryType) = await _qcErrorService.ExtractGeometryInfoAsync(
        sourceGdbPath,
        spatialError.SourceLayer,
        spatialError.SourceObjectId.ToString()
    );

    // WKT 변환
    string? extractedWkt = null;
    if (geometry != null)
    {
        try
        {
            geometry.ExportToWkt(out extractedWkt);
        }
        catch
        {
            extractedWkt = null;
        }
    }

    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "REL",
        ErrCode = GetSpatialRelationErrorCode(spatialError.RelationType, spatialError.ErrorType),
        Severity = ConvertErrorSeverityToString(spatialError.Severity),
        Status = "OPEN",
        RuleId = $"SPATIAL_{spatialError.RelationType}_{spatialError.ErrorType}",
        SourceClass = spatialError.SourceLayer,
        SourceOID = spatialError.SourceObjectId,
        SourceGlobalID = null,

        // ✅ 추출된 지오메트리 정보 사용 (추출 실패 시 원본 데이터 폴백)
        X = (x != 0 || y != 0) ? x : spatialError.ErrorLocationX,
        Y = (x != 0 || y != 0) ? y : spatialError.ErrorLocationY,
        Geometry = geometry?.Clone(),  // ✅ Geometry 객체 설정
        GeometryWKT = extractedWkt ?? spatialError.GeometryWKT,
        GeometryType = (geometryType != "Unknown")
            ? geometryType
            : DetermineGeometryTypeFromWKT(spatialError.GeometryWKT).ToUpperInvariant(),

        ErrorValue = spatialError.TargetObjectId?.ToString() ?? "",
        ThresholdValue = spatialError.TargetLayer,
        Message = spatialError.Message,
        RunID = runId,
        CreatedUTC = spatialError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RelationType"] = spatialError.RelationType.ToString(),
        ["ErrorType"] = spatialError.ErrorType,
        ["SourceLayer"] = spatialError.SourceLayer,
        ["TargetLayer"] = spatialError.TargetLayer,
        ["SourceObjectId"] = spatialError.SourceObjectId,
        ["TargetObjectId"] = spatialError.TargetObjectId,
        ["DetectedAt"] = spatialError.DetectedAt,
        ["Properties"] = spatialError.Properties,
        ["GeometryExtracted"] = geometry != null  // 추출 성공 여부 기록
    };
    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    // 리소스 해제
    geometry?.Dispose();

    return qcError;
}
```

### 주요 변경 사항
1. ✅ 메서드를 **비동기(`async Task<QcError>`)** 로 변경
2. ✅ `sourceGdbPath` 파라미터 추가
3. ✅ `ExtractGeometryInfoAsync()` 호출하여 지오메트리 추출
4. ✅ `Geometry` 객체 설정 (`geometry?.Clone()`)
5. ✅ 추출된 WKT, X, Y 좌표 사용
6. ✅ 리소스 해제 (`geometry?.Dispose()`)

---

## 🔨 수정 작업 2: ConvertAttributeRelationErrorToQcError 메서드 (5단계)

### 파일 위치
`/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

### 수정 대상 메서드
**현재**: `ConvertAttributeRelationErrorToQcError` (169-213줄)

### 수정 내용

#### BEFORE (현재 코드)
```csharp
/// <summary>
/// 속성 관계 오류를 QcError로 변환합니다
/// </summary>
/// <param name="attributeError">속성 관계 오류</param>
/// <param name="runId">검수 실행 ID</param>
/// <returns>변환된 QcError</returns>
private QcError ConvertAttributeRelationErrorToQcError(AttributeRelationError attributeError, string runId)
{
    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "ATTR_REL",
        ErrCode = GetAttributeRelationErrorCode(attributeError.RuleName),
        Severity = ConvertErrorSeverityToString(attributeError.Severity),
        Status = "OPEN",
        RuleId = $"ATTR_REL_{attributeError.RuleName}",
        SourceClass = attributeError.TableName,
        SourceOID = attributeError.ObjectId,
        SourceGlobalID = null, // 향후 구현
        X = 0, // 속성 오류는 공간 위치가 없음
        Y = 0,
        GeometryWKT = null,
        GeometryType = "NoGeometry",
        ErrorValue = attributeError.ActualValue,
        ThresholdValue = attributeError.ExpectedValue,
        Message = attributeError.Message,
        RunID = runId,
        CreatedUTC = attributeError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RuleName"] = attributeError.RuleName,
        ["FieldName"] = attributeError.FieldName,
        ["TableName"] = attributeError.TableName,
        ["ExpectedValue"] = attributeError.ExpectedValue,
        ["ActualValue"] = attributeError.ActualValue,
        ["Details"] = attributeError.Details,
        ["SuggestedFix"] = attributeError.SuggestedFix ?? "",
        ["RelatedTableName"] = attributeError.RelatedTableName ?? "",
        ["RelatedObjectId"] = attributeError.RelatedObjectId,
        ["DetectedAt"] = attributeError.DetectedAt,
        ["Properties"] = attributeError.Properties
    };

    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    return qcError;
}
```

#### AFTER (수정 후 코드)
```csharp
/// <summary>
/// 속성 관계 오류를 QcError로 변환합니다 (원본 FGDB에서 지오메트리 추출)
/// </summary>
/// <param name="attributeError">속성 관계 오류</param>
/// <param name="runId">검수 실행 ID</param>
/// <param name="sourceGdbPath">원본 FileGDB 경로</param>
/// <returns>변환된 QcError</returns>
private async Task<QcError> ConvertAttributeRelationErrorToQcErrorAsync(
    AttributeRelationError attributeError,
    string runId,
    string sourceGdbPath)
{
    // ✅ 원본 FGDB에서 지오메트리 추출 (5단계도 지오메트리 필요)
    var (geometry, x, y, geometryType) = await _qcErrorService.ExtractGeometryInfoAsync(
        sourceGdbPath,
        attributeError.TableName,
        attributeError.ObjectId.ToString()
    );

    // WKT 변환
    string? extractedWkt = null;
    if (geometry != null)
    {
        try
        {
            geometry.ExportToWkt(out extractedWkt);
        }
        catch
        {
            extractedWkt = null;
        }
    }

    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "ATTR_REL",
        ErrCode = GetAttributeRelationErrorCode(attributeError.RuleName),
        Severity = ConvertErrorSeverityToString(attributeError.Severity),
        Status = "OPEN",
        RuleId = $"ATTR_REL_{attributeError.RuleName}",
        SourceClass = attributeError.TableName,
        SourceOID = attributeError.ObjectId,
        SourceGlobalID = null,

        // ✅ 추출된 좌표 사용 (0,0이 아닌 실제 좌표)
        X = x,
        Y = y,
        Geometry = geometry?.Clone(),  // ✅ Geometry 객체 설정
        GeometryWKT = extractedWkt,
        GeometryType = (geometryType != "Unknown") ? geometryType : "NoGeometry",

        ErrorValue = attributeError.ActualValue,
        ThresholdValue = attributeError.ExpectedValue,
        Message = attributeError.Message,
        RunID = runId,
        CreatedUTC = attributeError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RuleName"] = attributeError.RuleName,
        ["FieldName"] = attributeError.FieldName,
        ["TableName"] = attributeError.TableName,
        ["ExpectedValue"] = attributeError.ExpectedValue,
        ["ActualValue"] = attributeError.ActualValue,
        ["Details"] = attributeError.Details,
        ["SuggestedFix"] = attributeError.SuggestedFix ?? "",
        ["RelatedTableName"] = attributeError.RelatedTableName ?? "",
        ["RelatedObjectId"] = attributeError.RelatedObjectId,
        ["DetectedAt"] = attributeError.DetectedAt,
        ["Properties"] = attributeError.Properties,
        ["GeometryExtracted"] = geometry != null  // 추출 성공 여부 기록
    };
    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    // 리소스 해제
    geometry?.Dispose();

    return qcError;
}
```

### 주요 변경 사항
1. ✅ 메서드를 **비동기(`async Task<QcError>`)** 로 변경
2. ✅ `sourceGdbPath` 파라미터 추가
3. ✅ `ExtractGeometryInfoAsync()` 호출하여 지오메트리 추출
4. ✅ `Geometry` 객체 설정
5. ✅ **X = 0, Y = 0 제거** → 실제 추출된 좌표 사용
6. ✅ GeometryType을 추출된 값으로 설정
7. ✅ 리소스 해제

---

## 🔨 수정 작업 3: SaveRelationValidationResultAsync 메서드 수정

### 파일 위치
`/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

### 수정 대상 메서드
**현재**: `SaveRelationValidationResultAsync` (34-112줄)

### 수정 내용

#### BEFORE (현재 코드 - 핵심 부분만)
```csharp
// 공간 관계 오류 변환
foreach (var spatialError in relationResult.SpatialErrors)
{
    var qcError = ConvertSpatialRelationErrorToQcError(spatialError, runId);
    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
    // 좌표 미기록 시 GeometryWKT에서 좌표 추정(센터) 시도는 저장 단계에서 처리되므로 여기서는 통과
    qcErrors.Add(qcError);
}

// 속성 관계 오류 변환
foreach (var attributeError in relationResult.AttributeErrors)
{
    var qcError = ConvertAttributeRelationErrorToQcError(attributeError, runId);
    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
    qcErrors.Add(qcError);
}

_logger.LogInformation("QcError 변환 완료: {TotalErrors}개 오류를 {QcErrorCount}개 QcError로 변환",
    totalErrors, qcErrors.Count);

// QC_ERRORS에 저장
var successCount = 0;
var failedCount = 0;

foreach (var qcError in qcErrors)
{
    var success = await SaveSingleQcErrorAsync(qcErrorsGdbPath, qcError);
    if (success)
    {
        successCount++;
    }
    else
    {
        failedCount++;
        _logger.LogWarning("QcError 저장 실패: {ErrorCode} ({SourceClass}:{SourceOID})",
            qcError.ErrCode, qcError.SourceClass, qcError.SourceOID);
    }
}
```

#### AFTER (수정 후 코드)
```csharp
var qcErrors = new List<QcError>();

// ✅ 공간 관계 오류 변환 (비동기)
foreach (var spatialError in relationResult.SpatialErrors)
{
    var qcError = await ConvertSpatialRelationErrorToQcErrorAsync(
        spatialError,
        runId,
        sourceGdbPath);  // ✅ 원본 FGDB 경로 전달
    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
    qcErrors.Add(qcError);
}

// ✅ 속성 관계 오류 변환 (비동기)
foreach (var attributeError in relationResult.AttributeErrors)
{
    var qcError = await ConvertAttributeRelationErrorToQcErrorAsync(
        attributeError,
        runId,
        sourceGdbPath);  // ✅ 원본 FGDB 경로 전달
    qcError.SourceFile = System.IO.Path.GetFileName(sourceGdbPath);
    qcErrors.Add(qcError);
}

_logger.LogInformation("QcError 변환 완료: {TotalErrors}개 오류를 {QcErrorCount}개 QcError로 변환",
    totalErrors, qcErrors.Count);

// ✅ 배치 저장 사용 (성능 최적화 - QcErrorDataService 활용)
var successCount = await _qcErrorService.BatchAppendQcErrorsAsync(qcErrorsGdbPath, qcErrors);

var allSuccess = successCount == qcErrors.Count;
_logger.LogInformation("관계 검수 결과 저장 완료: 성공 {Success}개, 실패 {Failed}개, 총 {Total}개",
    successCount, qcErrors.Count - successCount, qcErrors.Count);

return allSuccess;
```

### 주요 변경 사항
1. ✅ `ConvertSpatialRelationErrorToQcError` → `ConvertSpatialRelationErrorToQcErrorAsync` 호출
2. ✅ `ConvertAttributeRelationErrorToQcError` → `ConvertAttributeRelationErrorToQcErrorAsync` 호출
3. ✅ `sourceGdbPath` 파라미터 전달
4. ✅ **개별 저장** → **배치 저장**으로 변경 (성능 향상)
5. ✅ `_qcErrorService.BatchAppendQcErrorsAsync` 사용

---

## 🔨 수정 작업 4: QcErrorService 의존성 확인 (수정 불필요, 확인만)

### 확인 사항
`RelationErrorsIntegrator` 클래스에 이미 `QcErrorService`가 주입되어 있는지 확인하세요.

### 현재 코드 (17-23줄)
```csharp
public class RelationErrorsIntegrator
{
    private readonly ILogger<RelationErrorsIntegrator> _logger;
    private readonly QcErrorService _qcErrorService;  // ✅ 이미 주입되어 있음

    public RelationErrorsIntegrator(ILogger<RelationErrorsIntegrator> logger, QcErrorService qcErrorService)
    {
        _logger = logger;
        _qcErrorService = qcErrorService;
    }
```

### 결과
✅ 이미 `_qcErrorService`가 주입되어 있으므로 **추가 수정 불필요**

`ExtractGeometryInfoAsync` 메서드는 `QcErrorService` 클래스에 있으므로 다음과 같이 호출 가능:
```csharp
await _qcErrorService.ExtractGeometryInfoAsync(sourceGdbPath, tableName, objectId);
```

---

## ✅ 수정 완료 후 확인 사항

### 1. 컴파일 확인
- [ ] 프로젝트가 오류 없이 빌드되는지 확인
- [ ] 비동기 메서드 시그니처 변경 관련 경고 없는지 확인

### 2. 메서드 호출 체인 확인
- [ ] `SaveRelationValidationResultAsync`에서 두 변환 메서드를 `await`로 호출
- [ ] `sourceGdbPath`가 올바르게 전달되는지 확인
- [ ] 배치 저장 메서드가 정상 호출되는지 확인

### 3. 리소스 관리 확인
- [ ] `geometry?.Dispose()` 호출 확인
- [ ] 메모리 누수 방지 확인

### 4. 로그 확인
실행 후 로그에서 다음을 확인:
- [ ] "원본 FileGDB에서 지오메트리 추출 성공" 로그
- [ ] "QcError 변환 완료" 로그
- [ ] "관계 검수 결과 저장 완료" 로그
- [ ] QC_Errors_Point에 저장되었다는 로그

### 5. 데이터베이스 확인
검수 실행 후 QC_ERRORS.gdb 확인:
- [ ] **QC_Errors_Point** 피처 클래스에 4단계, 5단계 오류가 저장되었는지
- [ ] 좌표가 (0, 0)이 아닌 실제 좌표인지
- [ ] SHAPE 컬럼에 지오메트리가 있는지
- [ ] **QC_Errors_NoGeom**에는 1, 2단계 오류만 있는지

---

## 🎯 예상 결과

### 수정 전
```
1단계 (테이블)    → QC_Errors_NoGeom ✅
2단계 (스키마)    → QC_Errors_NoGeom ✅
3단계 (지오메트리) → QC_Errors_Point ✅
4단계 (관계)      → QC_Errors_Point/NoGeom ⚠️ (불확실)
5단계 (속성관계)  → QC_Errors_NoGeom ❌ (X=0, Y=0)
```

### 수정 후
```
1단계 (테이블)    → QC_Errors_NoGeom ✅
2단계 (스키마)    → QC_Errors_NoGeom ✅
3단계 (지오메트리) → QC_Errors_Point ✅
4단계 (관계)      → QC_Errors_Point ✅ (지오메트리 추출)
5단계 (속성관계)  → QC_Errors_Point ✅ (지오메트리 추출)
```

---

## ⚠️ 주의사항

### 1. 성능 고려
- 대량 오류 발생 시 지오메트리 추출로 인한 처리 시간 증가 가능
- **해결책**: 배치 저장 사용 (이미 적용됨)

### 2. 원본 FGDB 접근 실패 처리
- 원본 파일이 잠겨있거나 삭제된 경우 처리
- **현재 구현**: `ExtractGeometryInfoAsync`가 실패 시 (null, 0, 0, "Unknown") 반환
- **폴백**: 원본 좌표 (`spatialError.ErrorLocationX/Y`) 사용

### 3. ObjectId 유효성
- ObjectId가 존재하지 않는 경우 추출 실패
- **현재 구현**: 3단계와 동일하게 "TABLE", "UNKNOWN" 처리

### 4. 리소스 누수 방지
- `Geometry` 객체는 반드시 `Dispose()` 호출
- **확인**: 각 변환 메서드 마지막에 `geometry?.Dispose()` 있는지 확인

---

## 📝 추가 권장 사항 (선택)

### ValidationResultConverter.cs 정리
`ToQcErrorsFromNonGeometryStages` 메서드가 REL, ATTR_REL을 처리하지 않도록 수정 권장:

```csharp
// ValidationResultConverter.cs:551-562 수정
public List<QcError> ToQcErrorsFromNonGeometryStages(ValidationResult validationResult, string runId)
{
    var all = new List<QcError>();

    // 0, 1, 2단계만 처리 (비지오메트리)
    all.AddRange(ToQcErrorsFromCheckResult(validationResult.FileGdbCheckResult, "FILEGDB", runId));
    all.AddRange(ToQcErrorsFromCheckResult(validationResult.TableCheckResult, "TABLE", runId));
    all.AddRange(ToQcErrorsFromCheckResult(validationResult.SchemaCheckResult, "SCHEMA", runId));

    // REL, ATTR_REL은 RelationErrorsIntegrator.SaveRelationValidationResultAsync 사용
    // all.AddRange(ToQcErrorsFromCheckResult(validationResult.RelationCheckResult, "REL", runId));
    // all.AddRange(ToQcErrorsFromCheckResult(validationResult.AttributeRelationCheckResult, "ATTR_REL", runId));

    return all;
}
```

**이유**: 중복 처리 방지 및 일관성 유지

---

## 🧪 테스트 방법

### 1. 단위 테스트 시나리오
```
시나리오 1: 4단계 공간 관계 오류 저장
- 입력: SpatialRelationError (SourceLayer, SourceObjectId 유효)
- 기대 결과: QC_Errors_Point에 저장, 좌표 != (0, 0)

시나리오 2: 5단계 속성 관계 오류 저장
- 입력: AttributeRelationError (TableName, ObjectId 유효)
- 기대 결과: QC_Errors_Point에 저장, 좌표 != (0, 0)

시나리오 3: ObjectId 없는 경우
- 입력: ObjectId = "UNKNOWN"
- 기대 결과: 테이블 대표 지오메트리 추출 또는 폴백
```

### 2. 통합 테스트
```
1. 관계 검수 실행
2. SaveRelationValidationResultAsync 호출
3. QC_ERRORS.gdb 확인
4. QC_Errors_Point 피처 클래스 조회
5. 좌표 및 SHAPE 컬럼 확인
```

---

## 📚 참고 자료

### 관련 메서드 위치
- `ExtractGeometryInfoAsync`: `/home/user/SpatialCheckPro/SpatialCheckPro/Services/QcErrorService.cs` (768-1080줄)
- `BatchAppendQcErrorsAsync`: `/home/user/SpatialCheckPro/SpatialCheckPro/Services/QcErrorDataService.cs` (482-605줄)
- `CreateSimplePoint`: `/home/user/SpatialCheckPro/SpatialCheckPro/Services/QcErrorDataService.cs` (833-957줄)

### 저장 레이어 결정 로직
`QcErrorDataService.cs:223` 및 `QcErrorDataService.cs:541`
```csharp
layerName = pointGeometryCandidate != null ? "QC_Errors_Point" : "QC_Errors_NoGeom";
targetLayer = pointGeometry != null ? pointLayer : noGeomLayer;
```

---

## ✨ 수정 요약

### 수정 파일: 1개
- `/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

### 수정 메서드: 3개
1. `ConvertSpatialRelationErrorToQcError` → `ConvertSpatialRelationErrorToQcErrorAsync` (비동기, 지오메트리 추출)
2. `ConvertAttributeRelationErrorToQcError` → `ConvertAttributeRelationErrorToQcErrorAsync` (비동기, 지오메트리 추출)
3. `SaveRelationValidationResultAsync` (비동기 호출, 배치 저장)

### 핵심 변경 사항
- ✅ 원본 FGDB에서 지오메트리 추출 (`ExtractGeometryInfoAsync`)
- ✅ Geometry 객체 설정
- ✅ 실제 좌표 사용 (0, 0 제거)
- ✅ 배치 저장으로 성능 최적화
- ✅ 리소스 관리 (`Dispose`)

### 기대 효과
**4단계, 5단계 오류가 QC_Errors_Point 피처 테이블에 정확한 좌표와 함께 저장됩니다!** 🎉

---

## 🚀 시작하기

**Cursor AI에게 다음과 같이 지시하세요:**

```
위 지시서의 "수정 작업 1, 2, 3"을 순서대로 적용해서
/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs
파일을 수정해줘.

BEFORE 코드를 AFTER 코드로 정확히 교체하고,
메서드 시그니처, 파라미터, 비동기 호출을 모두 반영해줘.
```

---

**작성일**: 2025-10-27
**버전**: 1.0
**작성자**: Claude Code Analysis
