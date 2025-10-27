# 🔍 4, 5단계 저장 실패 및 지오메트리 위치 정확도 개선 방안

## 📋 분석 개요

**분석 일자**: 2025-10-27
**분석 대상**:
1. 4단계(관계 검수), 5단계(속성관계 검수) 오류 저장 실패 원인
2. 지오메트리 오류(겹침, 스파이크, 언더슛, 오버슛) 위치 정확도 문제

---

## 🚨 중요: Cursor 수정 미반영 확인

### 현재 상태
**파일**: `/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

**확인 결과**: ❌ **수정이 적용되지 않음**

#### 현재 코드 상태 (수정 전)
```csharp
// 120줄: 여전히 동기 메서드
private QcError ConvertSpatialRelationErrorToQcError(SpatialRelationError spatialError, string runId)

// 169줄: 여전히 동기 메서드, X=0, Y=0
private QcError ConvertAttributeRelationErrorToQcError(AttributeRelationError attributeError, string runId)
{
    X = 0,  // ❌ 여전히 0
    Y = 0,  // ❌ 여전히 0
}

// 65줄, 74줄: await 없음
var qcError = ConvertSpatialRelationErrorToQcError(spatialError, runId);  // ❌ 동기 호출
var qcError = ConvertAttributeRelationErrorToQcError(attributeError, runId);  // ❌ 동기 호출
```

### 수정이 반영되지 않은 이유
1. **Cursor가 파일을 저장하지 않음**: 변경 후 저장 누락
2. **다른 파일을 수정**: 경로 오인
3. **수정 후 되돌림**: Git checkout 또는 undo 발생
4. **브랜치 문제**: 다른 브랜치에서 작업

### 해결 방법
**Cursor에게 다시 명확히 지시**:
```
파일 경로: /home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs

이 파일을 열고, CURSOR_수정지시서_관계검수_지오메트리추출.md의
수정 작업 1, 2, 3을 정확히 적용해줘.

반드시 파일을 저장하고 저장 완료를 확인해줘.
```

---

## 📊 문제 1: 4단계, 5단계 저장 실패 원인 규명

### 🔴 4단계: 관계 검수 (REL) 저장 실패

#### 원인 분석

**파일**: `RelationErrorsIntegrator.cs:120-160`

```csharp
private QcError ConvertSpatialRelationErrorToQcError(SpatialRelationError spatialError, string runId)
{
    var qcError = new QcError
    {
        // ...
        X = spatialError.ErrorLocationX,  // ⚠️ spatialError에 좌표가 있을 수도 없을 수도
        Y = spatialError.ErrorLocationY,
        GeometryWKT = string.IsNullOrWhiteSpace(spatialError.GeometryWKT) ? null : spatialError.GeometryWKT,
        // ❌ Geometry 객체 없음
    };
    return qcError;
}
```

#### 저장 분기 로직 (QcErrorDataService.cs:189-223)
```
1단계 폴백: qcError.Geometry
    → null이므로 실패

2단계 폴백: qcError.GeometryWKT
    → spatialError.GeometryWKT가 있으면 성공 ✅
    → 없으면 실패 ❌

3단계 폴백: qcError.X != 0 || qcError.Y != 0
    → spatialError.ErrorLocationX/Y가 있으면 성공 ✅
    → 둘 다 0이면 실패 ❌

결과:
- GeometryWKT 또는 좌표가 있으면 → QC_Errors_Point
- 둘 다 없으면 → QC_Errors_NoGeom ❌
```

#### 문제점
1. **원본 FGDB에서 지오메트리를 추출하지 않음**
2. **SpatialRelationError에 의존**하지만 이 데이터가 불완전할 수 있음
3. **Geometry 객체 미생성**으로 1단계 폴백 실패

#### 발생 확률
- `spatialError.GeometryWKT`가 비어있고
- `spatialError.ErrorLocationX/Y`가 모두 0인 경우
- → **NoGeom에 저장됨** (약 30-40% 추정)

---

### 🔴 5단계: 속성관계 검수 (ATTR_REL) 저장 실패

#### 원인 분석

**파일**: `RelationErrorsIntegrator.cs:169-213`

```csharp
private QcError ConvertAttributeRelationErrorToQcError(AttributeRelationError attributeError, string runId)
{
    var qcError = new QcError
    {
        // ...
        X = 0,  // ❌ 의도적으로 0 설정 (속성 오류는 공간 위치 없음)
        Y = 0,  // ❌ 의도적으로 0 설정
        GeometryWKT = null,  // ❌ WKT 없음
        GeometryType = "NoGeometry",
        // ❌ Geometry 없음
    };
    return qcError;
}
```

#### 저장 분기 로직
```
1단계 폴백: qcError.Geometry → null ❌
2단계 폴백: qcError.GeometryWKT → null ❌
3단계 폴백: X=0, Y=0 → 조건 불충족 ❌

결과:
→ 항상 QC_Errors_NoGeom에 저장 ❌❌❌
```

#### 문제점
1. **속성 관계 오류도 공간 정보가 필요한데** 의도적으로 제거함
2. **TableName과 ObjectId가 있음에도** 지오메트리를 추출하지 않음
3. **100% NoGeom 저장** (요구사항 위배)

---

### 📌 근본 원인 요약

| 단계 | 현재 동작 | 문제점 | 저장 위치 |
|------|----------|--------|----------|
| **4단계 REL** | SpatialRelationError 의존 | GeometryWKT/좌표 불안정 | Point/NoGeom 혼재 ⚠️ |
| **5단계 ATTR_REL** | X=0, Y=0 강제 설정 | 지오메트리 추출 안 함 | 항상 NoGeom ❌ |

**핵심 문제**: **원본 FGDB에서 지오메트리를 추출하지 않음**

---

## 📊 문제 2: 지오메트리 오류 위치 정확도 문제

### 🔴 문제 현황

현재 **모든 지오메트리 오류**에서 `GeometryErrorDetail`에 **X, Y 좌표가 설정되지 않음**

#### 영향받는 오류 유형

| 오류 유형 | 구현 위치 | 좌표 설정 | 문제점 |
|----------|----------|----------|--------|
| **겹침** | HighPerformanceGeometryValidator:124 | ❌ 없음 | 겹침 영역만 반환, 위치 불명 |
| **스파이크** | GeometryCheckProcessor:428-440 | ❌ 없음 | 정점 인덱스만 알고 좌표 미추출 |
| **언더슛** | GeometryValidationService:490 | ❌ 없음 | 끝점 위치 미추출 |
| **오버슛** | GeometryValidationService:490 | ❌ 없음 | 끝점 위치 미추출 |
| **중복** | HighPerformanceGeometryValidator:76 | ❌ 없음 | ObjectId만 반환 |
| **슬리버** | GeometryCheckProcessor:414 | ❌ 없음 | 메시지만 저장 |

---

### 🔍 상세 분석

#### 1️⃣ 겹침 오류 (Overlap)

**파일**: `HighPerformanceGeometryValidator.cs:124-131`

```csharp
errorDetails.Add(new GeometryErrorDetail
{
    ObjectId = overlap.ObjectId.ToString(),
    ErrorType = "겹침 지오메트리",
    ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
    ThresholdValue = $"{tolerance}m",
    DetailMessage = $"OBJECTID {overlap.ObjectId}: 겹침 영역 {overlap.OverlapArea:F2}㎡ 검출"
    // ❌ X, Y 좌표 없음
    // ❌ GeometryWkt 없음
});
```

**문제점**:
- `SpatialIndexService.FindOverlaps`가 겹침 정보만 반환
- **겹침이 발생한 정확한 위치**(교차 영역 중심점)를 알 수 없음
- 사용자가 지도에서 오류를 찾기 어려움

**해결 필요 정보**:
- 겹침 교차 영역(Intersection Geometry)
- 교차 영역의 중심점 좌표
- 겹친 대상 ObjectId

---

#### 2️⃣ 스파이크 오류 (Spike)

**파일**: `GeometryCheckProcessor.cs:537-560`

```csharp
private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message)
{
    // ...
    for (int i = 1; i < pointCount - 1; i++)
    {
        var x1 = geometry.GetX(i - 1);
        var y1 = geometry.GetY(i - 1);
        var x2 = geometry.GetX(i);  // ✅ 스파이크 정점 좌표
        var y2 = geometry.GetY(i);  // ✅ 스파이크 정점 좌표
        var x3 = geometry.GetX(i + 1);
        var y3 = geometry.GetY(i + 1);

        var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

        if (angle < threshold)
        {
            message = $"스파이크 검출: 정점 {i}번 각도 {angle:F1}도 (임계값: {threshold}도)";
            // ❌ x2, y2 좌표를 반환하지 않음!
            return true;
        }
    }
}
```

**문제점**:
- 스파이크 정점의 **정확한 좌표 (x2, y2)를 알고 있지만** 반환하지 않음
- `out string message`로만 정보 전달
- `GeometryErrorDetail`에 좌표 미설정

**해결 필요 정보**:
- 스파이크 정점 좌표 (x2, y2)
- 정점 인덱스
- 각도 값

---

#### 3️⃣ 언더슛/오버슛 오류 (Undershoot/Overshoot)

**파일**: `GeometryValidationService.cs:406-495`

```csharp
private async Task<List<GeometryErrorDetail>> CheckUndershootOvershootAsync(Layer layer)
{
    // NetTopologySuite 사용
    var sourceEndpoint = sourceCoord;  // ✅ 끝점 좌표 있음

    var closestPoint = DistanceOp.NearestPoints(sourceEndpoint, ntsTarget);
    var closestPointOnTarget = closestPoint[1];  // ✅ 가장 가까운 점 좌표

    var minDistance = sourceEndpoint.Distance(closestPointOnTarget);

    details.Add(new GeometryErrorDetail
    {
        ObjectId = objectId,
        ErrorType = isEndpoint ? "오버슛" : "언더슛",
        DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)"
        // ❌ sourceEndpoint 좌표 미설정
        // ❌ closestPointOnTarget 좌표 미설정
    });
}
```

**문제점**:
- 끝점 좌표 (`sourceEndpoint`)를 알고 있지만 미설정
- 가장 가까운 대상 점 좌표도 알고 있지만 미설정
- 이격 거리만 메시지로 전달

**해결 필요 정보**:
- 선 끝점 좌표
- 대상 레이어의 가장 가까운 점 좌표
- 이격 거리

---

### 📌 지오메트리 오류 위치 문제 근본 원인

**공통 문제**: `GeometryErrorDetail` 클래스 설계 시 **X, Y 좌표를 필수로 설정하지 않음**

**파일**: `Models/GeometryValidationItem.cs:263-323`

```csharp
public class GeometryErrorDetail
{
    public string ObjectId { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorValue { get; set; } = string.Empty;
    public string ThresholdValue { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string DetailMessage { get; set; } = string.Empty;

    public double X { get; set; }  // ✅ 필드는 있지만
    public double Y { get; set; }  // ✅ 설정되지 않음

    public string? GeometryWkt { get; set; }  // ✅ 필드는 있지만 설정되지 않음
}
```

**결과**:
1. X, Y가 기본값 0.0으로 남음
2. GeometryWkt도 null
3. QcError로 변환 시:
   ```csharp
   // ValidationResultConverter.cs:300-301
   X = errorDetail.X,  // 0
   Y = errorDetail.Y,  // 0
   ```
4. 3단계 폴백 실패 (X=0, Y=0)
5. → **QC_Errors_NoGeom에 저장됨** ❌

---

## 💡 해결 방안

### ✅ 해결 방안 1: 4, 5단계 지오메트리 추출 (최우선)

**목표**: 4단계, 5단계 오류를 **QC_Errors_Point**에 저장

#### 수정 대상
`/home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs`

#### 수정 내용
이미 작성된 지시서(`CURSOR_수정지시서_관계검수_지오메트리추출.md`)대로 수정:

1. **ConvertSpatialRelationErrorToQcError** → Async 변환 + 지오메트리 추출
2. **ConvertAttributeRelationErrorToQcError** → Async 변환 + 지오메트리 추출
3. **SaveRelationValidationResultAsync** → 비동기 호출 + 배치 저장

**핵심 로직**:
```csharp
// 원본 FGDB에서 지오메트리 추출
var (geometry, x, y, geometryType) = await _qcErrorService.ExtractGeometryInfoAsync(
    sourceGdbPath,
    spatialError.SourceLayer,
    spatialError.SourceObjectId.ToString()
);

var qcError = new QcError
{
    // ...
    X = x,  // ✅ 추출된 실제 좌표
    Y = y,  // ✅ 추출된 실제 좌표
    Geometry = geometry?.Clone(),  // ✅ Geometry 객체
    GeometryWKT = extractedWkt,  // ✅ WKT
};
```

**예상 효과**:
- 4단계, 5단계 **100% QC_Errors_Point**에 저장 ✅
- 좌표 정확도 향상
- 지도 시각화 가능

---

### ✅ 해결 방안 2: 지오메트리 오류 위치 정확도 개선

#### 2-1. 겹침 오류 (Overlap) 개선

**파일**: `HighPerformanceGeometryValidator.cs:106-145`

**현재 코드**:
```csharp
var overlaps = _spatialIndexService.FindOverlaps(layerName, spatialIndex);

foreach (var overlap in overlaps)
{
    errorDetails.Add(new GeometryErrorDetail
    {
        ObjectId = overlap.ObjectId.ToString(),
        ErrorType = "겹침 지오메트리",
        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
        // ❌ X, Y 없음
    });
}
```

**수정 후**:
```csharp
var overlaps = _spatialIndexService.FindOverlaps(layerName, spatialIndex);

foreach (var overlap in overlaps)
{
    // ✅ 겹침 교차 영역 계산
    var sourceGeom = GetGeometryByObjectId(layer, overlap.ObjectId);
    var targetGeom = GetGeometryByObjectId(layer, overlap.OverlappingObjectId);

    var intersection = sourceGeom.Intersection(targetGeom);  // 교차 영역
    var centroid = intersection.Centroid();  // 중심점

    // ✅ WKT 추출
    string intersectionWkt = null;
    intersection.ExportToWkt(out intersectionWkt);

    errorDetails.Add(new GeometryErrorDetail
    {
        ObjectId = overlap.ObjectId.ToString(),
        ErrorType = "겹침 지오메트리",
        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡, 대상: {overlap.OverlappingObjectId}",
        ThresholdValue = $"{tolerance}m",
        X = centroid.GetX(0),  // ✅ 교차 영역 중심 X
        Y = centroid.GetY(0),  // ✅ 교차 영역 중심 Y
        GeometryWkt = intersectionWkt,  // ✅ 교차 영역 WKT
        DetailMessage = $"OBJECTID {overlap.ObjectId}와 {overlap.OverlappingObjectId} 겹침 ({overlap.OverlapArea:F2}㎡)"
    });

    // 리소스 해제
    sourceGeom?.Dispose();
    targetGeom?.Dispose();
    intersection?.Dispose();
    centroid?.Dispose();
}
```

**개선 효과**:
- 겹침이 발생한 **정확한 위치** 표시
- 겹친 대상 ObjectId 기록
- 교차 영역 형상 저장 (WKT)

---

#### 2-2. 스파이크 오류 (Spike) 개선

**파일**: `GeometryCheckProcessor.cs:537-560`

**현재 코드**:
```csharp
private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message)
{
    for (int i = 1; i < pointCount - 1; i++)
    {
        var x2 = geometry.GetX(i);
        var y2 = geometry.GetY(i);
        var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

        if (angle < threshold)
        {
            message = $"스파이크 검출: 정점 {i}번 각도 {angle:F1}도";
            return true;  // ❌ 좌표 반환 안 함
        }
    }
}
```

**수정 후** (메서드 시그니처 변경):
```csharp
private bool CheckSpikeInSingleGeometry(
    Geometry geometry,
    out string message,
    out double spikeX,  // ✅ 추가
    out double spikeY)  // ✅ 추가
{
    spikeX = 0;
    spikeY = 0;
    message = string.Empty;

    for (int i = 1; i < pointCount - 1; i++)
    {
        var x1 = geometry.GetX(i - 1);
        var y1 = geometry.GetY(i - 1);
        var x2 = geometry.GetX(i);  // 스파이크 정점
        var y2 = geometry.GetY(i);
        var x3 = geometry.GetX(i + 1);
        var y3 = geometry.GetY(i + 1);

        var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

        if (angle < threshold)
        {
            message = $"스파이크 검출: 정점 {i}번 각도 {angle:F1}도 (임계값: {threshold}도)";
            spikeX = x2;  // ✅ 스파이크 정점 X
            spikeY = y2;  // ✅ 스파이크 정점 Y
            return true;
        }
    }
    return false;
}
```

**호출 코드 수정**:
```csharp
// GeometryCheckProcessor.cs:428-440
if (config.ShouldCheckSpikes)
{
    if (HasSpike(geometry, out string spikeMessage, out double spikeX, out double spikeY))
    {
        errors.Add(new ValidationError
        {
            ErrorCode = "GEOM_SPIKE",
            Message = spikeMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning,
            X = spikeX,  // ✅ 좌표 설정
            Y = spikeY   // ✅ 좌표 설정
        });
    }
}
```

**HasSpike 메서드도 수정**:
```csharp
private bool HasSpike(Geometry geometry, out string message, out double x, out double y)
{
    message = string.Empty;
    x = 0;
    y = 0;

    int geomCount = geometry.GetGeometryCount();
    if (geomCount > 0)
    {
        for (int g = 0; g < geomCount; g++)
        {
            var part = geometry.GetGeometryRef(g);
            if (part != null && CheckSpikeInSingleGeometry(part, out message, out x, out y))
            {
                return true;
            }
        }
    }
    else
    {
        return CheckSpikeInSingleGeometry(geometry, out message, out x, out y);
    }

    return false;
}
```

**개선 효과**:
- 스파이크 **정확한 위치** (x2, y2) 저장
- 지도에서 정점 위치 시각화 가능
- 수정 작업 효율 향상

---

#### 2-3. 언더슛/오버슛 오류 개선

**파일**: `GeometryValidationService.cs:406-495`

**현재 코드**:
```csharp
var sourceEndpoint = sourceCoord;
var closestPoint = DistanceOp.NearestPoints(sourceEndpoint, ntsTarget);
var closestPointOnTarget = closestPoint[1];

details.Add(new GeometryErrorDetail
{
    ObjectId = objectId,
    ErrorType = isEndpoint ? "오버슛" : "언더슛",
    DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)"
    // ❌ 좌표 없음
});
```

**수정 후**:
```csharp
var sourceEndpoint = sourceCoord;
var closestPoint = DistanceOp.NearestPoints(sourceEndpoint, ntsTarget);
var closestPointOnTarget = closestPoint[1];
var minDistance = sourceEndpoint.Distance(closestPointOnTarget);

// ✅ 끝점과 가까운 점의 중간 위치 (오류 표시 위치)
double errorX = (sourceEndpoint.X + closestPointOnTarget.X) / 2;
double errorY = (sourceEndpoint.Y + closestPointOnTarget.Y) / 2;

// ✅ 선 지오메트리 생성 (끝점 → 가장 가까운 점)
var gapLineWkt = $"LINESTRING({sourceEndpoint.X} {sourceEndpoint.Y}, {closestPointOnTarget.X} {closestPointOnTarget.Y})";

details.Add(new GeometryErrorDetail
{
    ObjectId = objectId,
    ErrorType = isEndpoint ? "오버슛" : "언더슛",
    X = errorX,  // ✅ 중간 지점 X
    Y = errorY,  // ✅ 중간 지점 Y
    GeometryWkt = gapLineWkt,  // ✅ 간격 라인 WKT
    ErrorValue = $"{minDistance:F3}m",
    DetailMessage = $"선 끝점 비연결 (이격 {minDistance:F3}m, 끝점:({sourceEndpoint.X:F2}, {sourceEndpoint.Y:F2}))"
});
```

**개선 효과**:
- 끝점과 대상 사이의 **정확한 간격 위치** 표시
- 간격 라인 시각화 가능
- 수정 위치 즉시 파악

---

### ✅ 해결 방안 3: ValidationError 모델 확장

**파일**: `Models/ValidationError.cs` (또는 해당 위치)

**현재 상태 확인 필요**:
```csharp
public class ValidationError
{
    public string ErrorCode { get; set; }
    public string Message { get; set; }
    public string TableName { get; set; }
    public string FeatureId { get; set; }
    public ErrorSeverity Severity { get; set; }

    // ⚠️ X, Y 필드 있는지 확인 필요
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? GeometryWKT { get; set; }
}
```

**없다면 추가**:
```csharp
public class ValidationError
{
    // ... 기존 필드

    /// <summary>
    /// 오류 발생 X 좌표
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// 오류 발생 Y 좌표
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// 오류 지오메트리 WKT
    /// </summary>
    public string? GeometryWKT { get; set; }
}
```

---

## 📝 구현 우선순위

### 우선순위 1 (긴급) ⭐⭐⭐
**4, 5단계 지오메트리 추출**
- 파일: `RelationErrorsIntegrator.cs`
- 작업: 지시서대로 수정
- 효과: 4, 5단계 오류 Point 저장 보장

### 우선순위 2 (높음) ⭐⭐
**겹침 오류 위치 정확도 개선**
- 파일: `HighPerformanceGeometryValidator.cs`
- 작업: 교차 영역 중심점 추출
- 효과: 가장 빈번한 오류 유형 개선

### 우선순위 3 (중간) ⭐
**스파이크 오류 위치 개선**
- 파일: `GeometryCheckProcessor.cs`
- 작업: 메서드 시그니처 변경 + 좌표 반환
- 효과: 정점 위치 정확 표시

### 우선순위 4 (중간) ⭐
**언더슛/오버슛 위치 개선**
- 파일: `GeometryValidationService.cs`
- 작업: 끝점 좌표 추출 + 간격 라인 생성
- 효과: 연결성 오류 시각화

---

## 🧪 테스트 계획

### 테스트 1: 4, 5단계 저장 확인
```
1. 관계 검수 실행
2. SaveRelationValidationResultAsync 호출
3. QC_ERRORS.gdb 확인
4. QC_Errors_Point에 4, 5단계 오류 존재 확인
5. 좌표가 (0, 0)이 아닌지 확인
6. SHAPE 컬럼 확인
```

### 테스트 2: 겹침 오류 위치 확인
```
1. 겹침 오류 검출
2. ErrorDetails에 X, Y 설정 확인
3. GeometryWkt에 교차 영역 저장 확인
4. QGIS/ArcGIS에서 시각화
```

### 테스트 3: 스파이크 위치 확인
```
1. 스파이크 검출
2. 정점 좌표 설정 확인
3. 지도에서 해당 정점 위치 확인
```

---

## 📊 예상 효과

### 수정 전
```
4단계 REL:      Point 60% / NoGeom 40%  ⚠️
5단계 ATTR_REL:  NoGeom 100%             ❌

지오메트리 오류: 좌표 없음 (X=0, Y=0)   ❌
→ QC_Errors_NoGeom에 저장
→ 지도 시각화 불가
```

### 수정 후
```
4단계 REL:      Point 100%  ✅
5단계 ATTR_REL:  Point 100%  ✅

지오메트리 오류: 정확한 위치 좌표  ✅
→ QC_Errors_Point에 저장
→ 지도 시각화 가능
→ 수정 작업 효율 향상
```

---

## 🚀 즉시 실행 가능한 작업

### 1단계: RelationErrorsIntegrator.cs 수정
```bash
# Cursor AI에게 지시
파일: /home/user/SpatialCheckPro/SpatialCheckPro/Services/RelationErrorsIntegrator.cs
지시서: CURSOR_수정지시서_관계검수_지오메트리추출.md

수정 작업 1, 2, 3을 정확히 적용하고 저장 확인
```

### 2단계: 컴파일 확인
```bash
dotnet build SpatialCheckPro.sln
```

### 3단계: 테스트 실행
```bash
# 관계 검수 실행
# QC_ERRORS.gdb 확인
# QC_Errors_Point 레이어 조회
```

---

## 📚 참고 자료

### 관련 파일 목록
1. `RelationErrorsIntegrator.cs` - 4, 5단계 오류 변환
2. `QcErrorDataService.cs` - 저장 레이어 결정 (3단계 폴백)
3. `HighPerformanceGeometryValidator.cs` - 겹침, 중복 검사
4. `GeometryCheckProcessor.cs` - 스파이크, 슬리버 검사
5. `GeometryValidationService.cs` - 언더슛, 오버슛 검사

### 핵심 로직 위치
- 저장 분기: `QcErrorDataService.cs:223, 541`
- 지오메트리 추출: `QcErrorService.cs:768-1080`
- Point 생성: `QcErrorDataService.cs:833-957`

---

## ✅ 체크리스트

### RelationErrorsIntegrator 수정
- [ ] ConvertSpatialRelationErrorToQcErrorAsync 구현
- [ ] ConvertAttributeRelationErrorToQcErrorAsync 구현
- [ ] SaveRelationValidationResultAsync 수정
- [ ] 컴파일 성공 확인
- [ ] 테스트 실행
- [ ] QC_Errors_Point 저장 확인

### 지오메트리 위치 개선
- [ ] 겹침 오류 - 교차 영역 중심점 추출
- [ ] 스파이크 오류 - 정점 좌표 반환
- [ ] 언더슛/오버슛 - 끝점 좌표 추출
- [ ] ValidationError 모델 확장 (필요시)
- [ ] 컴파일 성공 확인
- [ ] 지도 시각화 테스트

---

**작성일**: 2025-10-27
**버전**: 1.0
**작성자**: Claude Code Analysis
**문서 유형**: 원인 분석 및 해결 방안
