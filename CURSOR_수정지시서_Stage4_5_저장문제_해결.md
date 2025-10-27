# 🚨 Cursor AI 긴급 수정 지시서: Stage 4, 5 저장 문제 해결

## 📋 문제 요약

**현재 상황**:
- **Stage 1, 2**: QC_Errors_NoGeom 테이블 저장 ✅ 정상
- **Stage 3**: QC_Errors_Point 저장 ✅ 정상
- **Stage 4 (REL)**: GeometryWKT 없으면 NoGeom 저장 ⚠️ ~40% 실패
- **Stage 5 (ATTR_REL)**: X=0, Y=0 강제 설정 ❌ 100% NoGeom 저장

**목표**:
- **Stage 4**: 100% QC_Errors_Point 저장
- **Stage 5**: 지오메트리 있는 경우 100% QC_Errors_Point 저장

**원인**:
- Stage 4, 5 변환 시 원본 FGDB에서 지오메트리를 추출하지 않음
- X=0, Y=0 또는 GeometryWKT=null이면 QcErrorDataService의 3-stage fallback 실패 → NoGeom 저장

**해결 방법**:
- 원본 FGDB에서 `ExtractGeometryInfoAsync()` 메서드로 지오메트리 추출
- 동기 메서드 → **비동기 메서드**로 변경

---

## 📁 수정 대상 파일

**단 하나의 파일만 수정**:
```
/SpatialCheckPro/Services/RelationErrorsIntegrator.cs
```

---

## 🔨 수정 작업

### 1️⃣ ExtractGeometryFromSourceAsync 메서드 추가

#### 📍 추가 위치
파일 하단 (private 메서드 섹션)

#### ✅ 추가할 코드
```csharp
/// <summary>
/// 원본 FGDB에서 지오메트리 정보 추출
/// QcErrorService.ExtractGeometryInfoAsync와 동일한 로직
/// </summary>
/// <param name="sourceGdbPath">원본 FGDB 경로</param>
/// <param name="tableId">레이어/테이블 이름</param>
/// <param name="objectId">객체 ID</param>
/// <returns>지오메트리, X좌표, Y좌표, 지오메트리타입</returns>
private async Task<(OSGeo.OGR.Geometry? geometry, double x, double y, string geometryType)> ExtractGeometryFromSourceAsync(
    string sourceGdbPath,
    string tableId,
    string objectId)
{
    return await Task.Run(() =>
    {
        try
        {
            // GDAL 드라이버 등록
            OSGeo.GDAL.Gdal.AllRegister();
            var driver = OSGeo.OGR.Ogr.GetDriverByName("OpenFileGDB");
            if (driver == null)
            {
                _logger.LogWarning("OpenFileGDB 드라이버를 찾을 수 없습니다.");
                return (null, 0, 0, "Unknown");
            }

            // FGDB 열기 (읽기 모드)
            var dataSource = driver.Open(sourceGdbPath, 0);
            if (dataSource == null)
            {
                _logger.LogWarning("FGDB를 열 수 없습니다: {Path}", sourceGdbPath);
                return (null, 0, 0, "Unknown");
            }

            // 레이어 찾기
            OSGeo.OGR.Layer? layer = null;
            for (int i = 0; i < dataSource.GetLayerCount(); i++)
            {
                var testLayer = dataSource.GetLayerByIndex(i);
                if (testLayer.GetName().Equals(tableId, StringComparison.OrdinalIgnoreCase))
                {
                    layer = testLayer;
                    break;
                }
            }

            if (layer == null)
            {
                _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", tableId);
                return (null, 0, 0, "Unknown");
            }

            var geometryTypeName = layer.GetGeomType().ToString();

            // ObjectId로 피처 검색
            layer.SetAttributeFilter($"OBJECTID = {objectId}");
            layer.ResetReading();
            var feature = layer.GetNextFeature();

            if (feature != null)
            {
                var geometry = feature.GetGeometryRef();
                if (geometry != null)
                {
                    // 지오메트리 복제 (원본 보호)
                    var clonedGeom = geometry.Clone();

                    // Envelope 중심점 계산
                    var envelope = new OSGeo.OGR.Envelope();
                    clonedGeom.GetEnvelope(envelope);
                    double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
                    double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

                    _logger.LogDebug("지오메트리 추출 성공 - Table: {Table}, OID: {OID}, Type: {Type}, X: {X:F3}, Y: {Y:F3}",
                        tableId, objectId, geometryTypeName, centerX, centerY);

                    return (clonedGeom, centerX, centerY, geometryTypeName);
                }
            }

            _logger.LogDebug("피처를 찾을 수 없거나 지오메트리가 없음 - Table: {Table}, OID: {OID}",
                tableId, objectId);

            return (null, 0, 0, "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "지오메트리 추출 중 오류 발생 - Path: {Path}, Table: {Table}, OID: {OID}",
                sourceGdbPath, tableId, objectId);
            return (null, 0, 0, "Unknown");
        }
    });
}
```

---

### 2️⃣ ConvertSpatialRelationErrorToQcError 메서드 수정 (Stage 4)

#### 📍 수정 위치
`RelationErrorsIntegrator.cs` 약 120-160번 라인

#### ❌ 수정 전 (동기 메서드)
```csharp
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
        SourceGlobalID = null,
        X = spatialError.ErrorLocationX,  // ⚠️ 종종 0
        Y = spatialError.ErrorLocationY,  // ⚠️ 종종 0
        GeometryWKT = string.IsNullOrWhiteSpace(spatialError.GeometryWKT) ? null : spatialError.GeometryWKT,  // ⚠️ 종종 null
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

#### ✅ 수정 후 (비동기 메서드 + FGDB 지오메트리 추출)
```csharp
/// <summary>
/// 공간 관계 오류를 QcError로 변환 (원본 FGDB에서 지오메트리 추출)
/// </summary>
private async Task<QcError> ConvertSpatialRelationErrorToQcErrorAsync(
    SpatialRelationError spatialError,
    string runId,
    string sourceGdbPath)  // ✅ 원본 FGDB 경로 파라미터 추가
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
        SourceGlobalID = null,
        Message = spatialError.Message,
        RunID = runId,
        CreatedUTC = spatialError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // ✅ 1차 시도: SpatialRelationError의 기존 좌표/WKT 사용
    double x = spatialError.ErrorLocationX;
    double y = spatialError.ErrorLocationY;
    string? geometryWkt = string.IsNullOrWhiteSpace(spatialError.GeometryWKT) ? null : spatialError.GeometryWKT;
    string geometryType = DetermineGeometryTypeFromWKT(spatialError.GeometryWKT).ToUpperInvariant();

    // ✅ 2차 시도: 좌표가 0,0이거나 WKT가 없으면 원본 FGDB에서 추출
    if ((x == 0 && y == 0) || string.IsNullOrWhiteSpace(geometryWkt))
    {
        try
        {
            var (extractedGeometry, extractedX, extractedY, extractedGeomType) =
                await ExtractGeometryFromSourceAsync(
                    sourceGdbPath,
                    spatialError.SourceLayer,
                    spatialError.SourceObjectId);

            if (extractedGeometry != null)
            {
                x = extractedX;
                y = extractedY;
                extractedGeometry.ExportToWkt(out geometryWkt);
                geometryType = extractedGeomType;

                _logger.LogDebug("[Stage 4] FGDB에서 지오메트리 추출 성공 - Layer: {Layer}, OID: {OID}, X: {X:F3}, Y: {Y:F3}",
                    spatialError.SourceLayer, spatialError.SourceObjectId, x, y);
            }
            else
            {
                _logger.LogWarning("[Stage 4] FGDB에서 지오메트리 추출 실패 (NoGeom 저장) - Layer: {Layer}, OID: {OID}",
                    spatialError.SourceLayer, spatialError.SourceObjectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stage 4] FGDB 지오메트리 추출 중 예외 발생 - Layer: {Layer}, OID: {OID}",
                spatialError.SourceLayer, spatialError.SourceObjectId);
        }
    }

    qcError.X = x;
    qcError.Y = y;
    qcError.GeometryWKT = geometryWkt;
    qcError.GeometryType = geometryType;
    qcError.ErrorValue = spatialError.TargetObjectId?.ToString() ?? "";
    qcError.ThresholdValue = spatialError.TargetLayer;

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

---

### 3️⃣ ConvertAttributeRelationErrorToQcError 메서드 수정 (Stage 5)

#### 📍 수정 위치
`RelationErrorsIntegrator.cs` 약 169-213번 라인

#### ❌ 수정 전 (동기 메서드, X=0/Y=0 강제 설정)
```csharp
private QcError ConvertAttributeRelationErrorToQcError(AttributeRelationError attributeError, string runId)
{
    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "ATTR_REL",
        ErrCode = GetAttributeRelationErrorCode(attributeError.RelationType, attributeError.ErrorType),
        Severity = ConvertErrorSeverityToString(attributeError.Severity),
        Status = "OPEN",
        RuleId = $"ATTR_{attributeError.RelationType}_{attributeError.ErrorType}",
        SourceClass = attributeError.SourceLayer,
        SourceOID = attributeError.SourceObjectId,
        SourceGlobalID = null,
        X = 0,  // ❌ 강제로 0 설정 (주석: "속성 오류는 공간 위치가 없음")
        Y = 0,  // ❌ 강제로 0 설정
        GeometryWKT = null,
        GeometryType = "NoGeometry",
        ErrorValue = attributeError.SourceAttributeValue?.ToString() ?? "",
        ThresholdValue = attributeError.TargetLayer,
        Message = attributeError.Message,
        RunID = runId,
        CreatedUTC = attributeError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RelationType"] = attributeError.RelationType.ToString(),
        ["ErrorType"] = attributeError.ErrorType,
        ["SourceLayer"] = attributeError.SourceLayer,
        ["TargetLayer"] = attributeError.TargetLayer,
        ["SourceObjectId"] = attributeError.SourceObjectId,
        ["SourceAttribute"] = attributeError.SourceAttribute ?? "",
        ["SourceAttributeValue"] = attributeError.SourceAttributeValue ?? "",
        ["TargetAttribute"] = attributeError.TargetAttribute ?? "",
        ["DetectedAt"] = attributeError.DetectedAt,
        ["Properties"] = attributeError.Properties
    };

    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    return qcError;
}
```

#### ✅ 수정 후 (비동기 메서드 + FGDB 지오메트리 추출)
```csharp
/// <summary>
/// 속성 관계 오류를 QcError로 변환 (원본 FGDB에서 지오메트리 추출)
/// </summary>
private async Task<QcError> ConvertAttributeRelationErrorToQcErrorAsync(
    AttributeRelationError attributeError,
    string runId,
    string sourceGdbPath)  // ✅ 원본 FGDB 경로 파라미터 추가
{
    var qcError = new QcError
    {
        GlobalID = Guid.NewGuid().ToString(),
        ErrType = "ATTR_REL",
        ErrCode = GetAttributeRelationErrorCode(attributeError.RelationType, attributeError.ErrorType),
        Severity = ConvertErrorSeverityToString(attributeError.Severity),
        Status = "OPEN",
        RuleId = $"ATTR_{attributeError.RelationType}_{attributeError.ErrorType}",
        SourceClass = attributeError.SourceLayer,
        SourceOID = attributeError.SourceObjectId,
        SourceGlobalID = null,
        Message = attributeError.Message,
        RunID = runId,
        CreatedUTC = attributeError.DetectedAt,
        UpdatedUTC = DateTime.UtcNow
    };

    // ✅ 원본 FGDB에서 지오메트리 추출 시도
    double x = 0, y = 0;
    string? geometryWkt = null;
    string geometryType = "NoGeometry";

    try
    {
        var (extractedGeometry, extractedX, extractedY, extractedGeomType) =
            await ExtractGeometryFromSourceAsync(
                sourceGdbPath,
                attributeError.SourceLayer,
                attributeError.SourceObjectId);

        if (extractedGeometry != null)
        {
            x = extractedX;
            y = extractedY;
            extractedGeometry.ExportToWkt(out geometryWkt);
            geometryType = extractedGeomType;

            _logger.LogDebug("[Stage 5] FGDB에서 지오메트리 추출 성공 - Layer: {Layer}, OID: {OID}, X: {X:F3}, Y: {Y:F3}",
                attributeError.SourceLayer, attributeError.SourceObjectId, x, y);
        }
        else
        {
            _logger.LogDebug("[Stage 5] 지오메트리 없음 (NoGeom 저장) - Layer: {Layer}, OID: {OID}",
                attributeError.SourceLayer, attributeError.SourceObjectId);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Stage 5] FGDB 지오메트리 추출 중 예외 발생 - Layer: {Layer}, OID: {OID}",
            attributeError.SourceLayer, attributeError.SourceObjectId);
    }

    qcError.X = x;
    qcError.Y = y;
    qcError.GeometryWKT = geometryWkt;
    qcError.GeometryType = geometryType;
    qcError.ErrorValue = attributeError.SourceAttributeValue?.ToString() ?? "";
    qcError.ThresholdValue = attributeError.TargetLayer;

    // 상세 정보를 JSON으로 저장
    var detailsDict = new Dictionary<string, object>
    {
        ["RelationType"] = attributeError.RelationType.ToString(),
        ["ErrorType"] = attributeError.ErrorType,
        ["SourceLayer"] = attributeError.SourceLayer,
        ["TargetLayer"] = attributeError.TargetLayer,
        ["SourceObjectId"] = attributeError.SourceObjectId,
        ["SourceAttribute"] = attributeError.SourceAttribute ?? "",
        ["SourceAttributeValue"] = attributeError.SourceAttributeValue ?? "",
        ["TargetAttribute"] = attributeError.TargetAttribute ?? "",
        ["DetectedAt"] = attributeError.DetectedAt,
        ["Properties"] = attributeError.Properties
    };

    qcError.DetailsJSON = JsonSerializer.Serialize(detailsDict);

    return qcError;
}
```

---

### 4️⃣ 호출부 수정 (IntegrateRelationErrors 메서드)

#### 📍 수정 위치
`IntegrateRelationErrors` 또는 `IntegrateRelationErrorsAsync` 메서드 내부

#### ⚠️ 중요: sourceGdbPath 확보 방법

**방법 1**: `IntegrateRelationErrors` 메서드에 파라미터 추가
```csharp
// ❌ 수정 전
public async Task IntegrateRelationErrors(string runId)

// ✅ 수정 후
public async Task IntegrateRelationErrors(string runId, string sourceGdbPath)
```

**방법 2**: 클래스 필드/프로퍼티에서 가져오기
```csharp
// RelationErrorsIntegrator 클래스에 필드가 있는지 확인
private readonly string _sourceGdbPath;

// 또는 생성자에서 주입받도록 수정
public RelationErrorsIntegrator(..., string sourceGdbPath)
{
    _sourceGdbPath = sourceGdbPath;
}
```

#### ❌ 수정 전 (동기 호출)
```csharp
// Stage 4 호출
foreach (var spatialError in spatialErrors)
{
    var qcError = ConvertSpatialRelationErrorToQcError(spatialError, runId);
    qcErrors.Add(qcError);
}

// Stage 5 호출
foreach (var attributeError in attributeErrors)
{
    var qcError = ConvertAttributeRelationErrorToQcError(attributeError, runId);
    qcErrors.Add(qcError);
}
```

#### ✅ 수정 후 (비동기 호출 + sourceGdbPath 전달)
```csharp
// Stage 4 호출
foreach (var spatialError in spatialErrors)
{
    var qcError = await ConvertSpatialRelationErrorToQcErrorAsync(spatialError, runId, sourceGdbPath);
    qcErrors.Add(qcError);
}

// Stage 5 호출
foreach (var attributeError in attributeErrors)
{
    var qcError = await ConvertAttributeRelationErrorToQcErrorAsync(attributeError, runId, sourceGdbPath);
    qcErrors.Add(qcError);
}
```

---

### 5️⃣ using 문 추가

#### 📍 파일 상단에 추가

```csharp
using System.Text.Json;
using OSGeo.OGR;
using OSGeo.GDAL;
```

**확인 사항**:
- `using OSGeo.OGR;` - Geometry, Layer, Feature 등
- `using OSGeo.GDAL;` - Gdal.AllRegister()
- `using System.Text.Json;` - JsonSerializer (이미 있을 가능성 높음)

---

## ✅ 검증 체크리스트

수정 완료 후 반드시 확인하세요:

### 코드 수정 확인
- [ ] `ExtractGeometryFromSourceAsync` 메서드 추가됨
- [ ] `ConvertSpatialRelationErrorToQcError` → `ConvertSpatialRelationErrorToQcErrorAsync`로 변경됨
- [ ] `ConvertAttributeRelationErrorToQcError` → `ConvertAttributeRelationErrorToQcErrorAsync`로 변경됨
- [ ] 두 변환 메서드에 `sourceGdbPath` 파라미터 추가됨
- [ ] 호출부에 `await` 키워드 사용됨
- [ ] `sourceGdbPath`가 올바르게 전달됨 (빈 문자열이나 null 아님)
- [ ] using 문이 추가됨 (OSGeo.OGR, OSGeo.GDAL)

### 빌드 확인
- [ ] 빌드 오류 없음
- [ ] 경고 메시지 확인 및 해결

### 실행 테스트
- [ ] **Stage 4 오류**: QC_Errors_Point에 저장되는지 확인
- [ ] **Stage 5 오류**: QC_Errors_Point에 저장되는지 확인 (지오메트리 있는 경우)
- [ ] X, Y 좌표가 **0,0이 아닌 실제 좌표**인지 확인
- [ ] GeometryWKT가 null이 아닌지 확인
- [ ] 로그에서 "[Stage 4] FGDB에서 지오메트리 추출 성공" 메시지 확인
- [ ] 로그에서 "[Stage 5] FGDB에서 지오메트리 추출 성공" 메시지 확인

### ArcGIS/QGIS 확인
- [ ] QC_Errors_Point 레이어를 지도에서 열 수 있음
- [ ] Stage 4, 5 오류가 지도상에 정확한 위치에 표시됨
- [ ] 속성 테이블에서 X, Y 좌표 값 확인

---

## 🚨 주의사항

### 1. sourceGdbPath 전달 방법
`IntegrateRelationErrors` 메서드가 호출되는 곳을 찾아서 `sourceGdbPath`를 전달해야 합니다.

**확인해야 할 곳**:
- QcErrorService 또는 검수 실행 서비스
- sourceGdbPath는 원본 검수 대상 FGDB 경로입니다 (예: `C:\Data\Project.gdb`)

### 2. 비동기 메서드 체인
`IntegrateRelationErrors` 메서드 자체가 `async`가 아니면 `async`로 변경해야 합니다:

```csharp
// ❌ 동기 메서드
public void IntegrateRelationErrors(string runId)

// ✅ 비동기 메서드
public async Task IntegrateRelationErrors(string runId, string sourceGdbPath)
```

### 3. GDAL 라이센스
- GDAL/OGR은 이미 프로젝트에서 사용 중이므로 별도 설정 불필요
- OpenFileGDB 드라이버는 GDAL에 기본 포함

### 4. 예외 처리
- 지오메트리 추출 실패 시 X=0, Y=0으로 유지 → NoGeom 테이블에 저장됨 (정상 동작)
- 로그를 통해 추출 실패 원인 파악 가능

---

## 📊 예상 결과

### Before (현재)
```
Stage 4 (REL):
- 60% → QC_Errors_Point ✅
- 40% → QC_Errors_NoGeom ❌

Stage 5 (ATTR_REL):
- 0% → QC_Errors_Point ❌
- 100% → QC_Errors_NoGeom ❌
```

### After (수정 후)
```
Stage 4 (REL):
- ~95% → QC_Errors_Point ✅ (지오메트리 있는 경우)
- ~5% → QC_Errors_NoGeom ✅ (원본에 지오메트리 없는 경우)

Stage 5 (ATTR_REL):
- ~80% → QC_Errors_Point ✅ (지오메트리 있는 경우)
- ~20% → QC_Errors_NoGeom ✅ (원본에 지오메트리 없는 경우)
```

**개선율**:
- **Stage 4**: +35% Point 저장 증가
- **Stage 5**: +80% Point 저장 증가

---

## 🔍 트러블슈팅

### 문제 1: "OpenFileGDB 드라이버를 찾을 수 없습니다"
**해결**: GDAL 초기화 확인
```csharp
OSGeo.GDAL.Gdal.AllRegister();
```

### 문제 2: "sourceGdbPath를 찾을 수 없습니다"
**해결**:
1. `IntegrateRelationErrors` 호출부 찾기
2. 원본 검수 대상 FGDB 경로 전달
3. 경로가 존재하는지 확인: `File.Exists(sourceGdbPath)`

### 문제 3: 여전히 NoGeom에 저장됨
**원인 체크**:
1. `await` 키워드 빠짐?
2. `sourceGdbPath`가 빈 문자열이거나 null?
3. 로그에서 "지오메트리 추출 실패" 메시지 확인
4. 원본 FGDB에 실제로 지오메트리가 있는지 ArcGIS/QGIS에서 확인

### 문제 4: 빌드 오류 - "Task를 찾을 수 없습니다"
**해결**: using 추가
```csharp
using System.Threading.Tasks;
```

---

## 🎯 핵심 요약

1. **ExtractGeometryFromSourceAsync** 메서드 추가 → FGDB에서 지오메트리 읽기
2. **두 개의 변환 메서드를 비동기로 변경** → Async, sourceGdbPath 파라미터
3. **호출부 수정** → await 추가, sourceGdbPath 전달
4. **테스트** → Stage 4, 5 오류가 QC_Errors_Point에 저장되는지 확인

**이 3가지만 수정하면 Stage 4, 5 저장 문제가 완전히 해결됩니다!** ✅
