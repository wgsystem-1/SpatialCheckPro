# 전체 지오메트리 오류 위치 정확도 종합 개선 방안

## 📋 문서 개요

**작성일**: 2025-10-27
**목적**: 13가지 모든 지오메트리 오류 타입의 위치 추출 현황 분석 및 개선 방안 제시
**범위**: 기존 4가지(겹침, 스파이크, 언더슛, 오버슛) 외 추가 9가지 오류 타입 포함

---

## 🎯 Executive Summary

### 전체 오류 타입 분류 (13가지)

| 번호 | 오류 타입 | 오류 코드 | 현재 X,Y 추출 | 지오메트리 추출 | 개선 필요 |
|------|-----------|----------|--------------|---------------|----------|
| 1 | 중복 지오메트리 | - | ❌ | ⚠️ (있으나 미사용) | ✅ |
| 2 | 겹침 지오메트리 | - | ❌ | ⚠️ (교차 영역 미추출) | ✅ |
| 3 | 짧은 선 객체 | GEOM_SHORT_LINE | ❌ | ❌ | ✅ |
| 4 | 작은 면적 | GEOM_SMALL_AREA | ❌ | ❌ | ✅ |
| 5 | 최소 정점 부족 | GEOM_MIN_VERTEX | ❌ | ❌ | ✅ |
| 6 | 슬리버 폴리곤 | GEOM_SLIVER | ❌ | ❌ | ✅ |
| 7 | 스파이크 | GEOM_SPIKE | ❌ | ✅ (좌표 계산되나 미반환) | ✅ |
| 8 | 자체 꼬임/교차 | GEOM_INVALID | ❌ | ⚠️ (교차점 미추출) | ✅ |
| 9 | 자기 중첩 | GEOM_NOT_SIMPLE | ❌ | ⚠️ (중첩 영역 미추출) | ✅ |
| 10 | 언더슛 | - | ❌ | ✅ (좌표 계산되나 미반환) | ✅ |
| 11 | 오버슛 | - | ❌ | ✅ (좌표 계산되나 미반환) | ✅ |
| 12 | NULL/Empty 지오메트리 | - | ❌ | ❌ (지오메트리 없음) | ⬜ (개선 불가) |
| 13 | 홀 폴리곤/링 방향 | GEOM_INVALID | ❌ | ⚠️ (문제 위치 미추출) | ✅ |

**범례**:
- ✅ 가능/필요
- ❌ 불가능/없음
- ⚠️ 부분적으로 가능
- ⬜ 해당 없음

---

## 📊 오류 타입별 상세 분석

### 1️⃣ 중복 지오메트리 (Duplicate Geometry)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Services/HighPerformanceGeometryValidator.cs`
- **라인**: 76-85
- **검출 방식**: WKT 기반 해시맵 비교

#### 현재 코드
```csharp
errorDetails.Add(new GeometryErrorDetail
{
    ObjectId = objectId.ToString(),
    ErrorType = "중복 지오메트리",
    ErrorValue = $"정확히 동일한 지오메트리 (그룹 크기: {group.Count})",
    ThresholdValue = coordinateTolerance > 0 ? $"좌표 허용오차 {coordinateTolerance}m" : "Exact match",
    DetailMessage = coordinateTolerance > 0
        ? $"OBJECTID {objectId}: 좌표 허용오차 {coordinateTolerance}m 이내 동일한 지오메트리"
        : $"OBJECTID {objectId}: 완전히 동일한 지오메트리"
    // ❌ X, Y 좌표 없음
    // ❌ GeometryWkt 없음
});
```

#### 문제점
- 중복된 지오메트리 전체는 있지만, 대표 위치(중심점/첫 점)를 추출하지 않음
- 여러 개의 중복 객체 중 어느 것을 참조해야 하는지 불명확

#### 개선 방안
```csharp
// ✅ 개선된 코드
var (objectId, geometry) = group[i];
var envelope = new OSGeo.OGR.Envelope();
geometry.GetEnvelope(envelope);
double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

string wkt;
geometry.ExportToWkt(out wkt);

errorDetails.Add(new GeometryErrorDetail
{
    ObjectId = objectId.ToString(),
    ErrorType = "중복 지오메트리",
    ErrorValue = $"정확히 동일한 지오메트리 (그룹 크기: {group.Count})",
    ThresholdValue = coordinateTolerance > 0 ? $"좌표 허용오차 {coordinateTolerance}m" : "Exact match",
    DetailMessage = coordinateTolerance > 0
        ? $"OBJECTID {objectId}: 좌표 허용오차 {coordinateTolerance}m 이내 동일한 지오메트리"
        : $"OBJECTID {objectId}: 완전히 동일한 지오메트리",
    X = centerX,  // ✅ 중심점 X
    Y = centerY,  // ✅ 중심점 Y
    GeometryWkt = wkt  // ✅ 전체 지오메트리
});
```

#### 우선순위: **중간** (Medium)
- 중복 객체는 이미 지오메트리가 완전히 동일하므로 정확한 위치 파악이 상대적으로 덜 중요
- 하지만 QC_Errors_Point에 저장하려면 X, Y 필수

---

### 2️⃣ 겹침 지오메트리 (Overlap)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Services/HighPerformanceGeometryValidator.cs`
- **라인**: 124-131
- **검출 방식**: 공간 인덱스 기반 교차 영역 계산

#### 현재 코드
```csharp
foreach (var overlap in overlaps)
{
    errorDetails.Add(new GeometryErrorDetail
    {
        ObjectId = overlap.ObjectId.ToString(),
        ErrorType = "겹침 지오메트리",
        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
        ThresholdValue = $"{tolerance}m",
        DetailMessage = $"OBJECTID {overlap.ObjectId}: 겹침 영역 {overlap.OverlapArea:F2}㎡ 검출"
        // ❌ X, Y 좌표 없음
        // ❌ 교차 영역 지오메트리 없음
    });
}
```

#### 문제점
- **겹침 영역 면적은 계산**되었으나, **교차 영역의 정확한 위치(중심점)를 추출하지 않음**
- `SpatialIndexService.FindOverlaps()`는 OverlapInfo 객체를 반환하지만, 교차 지오메트리는 포함하지 않음

#### 개선 방안
**Step 1: SpatialIndexService.FindOverlaps() 수정**
```csharp
// OverlapInfo 클래스에 교차 지오메트리 추가
public class OverlapInfo
{
    public long ObjectId { get; set; }
    public long OtherObjectId { get; set; }
    public double OverlapArea { get; set; }
    public OSGeo.OGR.Geometry? IntersectionGeometry { get; set; }  // ✅ 추가
}
```

**Step 2: 교차 영역 중심점 추출**
```csharp
foreach (var overlap in overlaps)
{
    double centerX = 0, centerY = 0;
    string? intersectionWkt = null;

    if (overlap.IntersectionGeometry != null)
    {
        var envelope = new OSGeo.OGR.Envelope();
        overlap.IntersectionGeometry.GetEnvelope(envelope);
        centerX = (envelope.MinX + envelope.MaxX) / 2.0;
        centerY = (envelope.MinY + envelope.MaxY) / 2.0;

        overlap.IntersectionGeometry.ExportToWkt(out intersectionWkt);
    }

    errorDetails.Add(new GeometryErrorDetail
    {
        ObjectId = overlap.ObjectId.ToString(),
        ErrorType = "겹침 지오메트리",
        ErrorValue = $"겹침 영역: {overlap.OverlapArea:F2}㎡",
        ThresholdValue = $"{tolerance}m",
        DetailMessage = $"OBJECTID {overlap.ObjectId}와 {overlap.OtherObjectId}: 겹침 영역 {overlap.OverlapArea:F2}㎡ 검출",
        X = centerX,  // ✅ 교차 영역 중심점 X
        Y = centerY,  // ✅ 교차 영역 중심점 Y
        GeometryWkt = intersectionWkt  // ✅ 교차 영역 지오메트리
    });
}
```

#### 우선순위: **높음** (High)
- 겹침 오류는 정확한 교차 위치 파악이 매우 중요
- 사용자가 어디서 겹쳤는지 정확히 확인해야 수정 가능

---

### 3️⃣ 짧은 선 객체 (Short Line)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 304-318
- **검출 방식**: LineString.Length() < MinLineLength

#### 현재 코드
```csharp
if (length < _criteria.MinLineLength && length > 0)
{
    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_SHORT_LINE",
        Message = $"선이 너무 짧습니다: {length:F3}m (최소: {_criteria.MinLineLength}m)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Warning
        // ❌ ValidationError는 X, Y 필드가 없음
    });
}
```

#### 문제점
- `ValidationError` 객체는 좌표 필드가 없음
- 짧은 선의 중점 또는 시작점 좌표를 추출하지 않음
- 이 오류는 `GeometryErrorDetail`이 아닌 `ValidationError`로 저장됨

#### 개선 방안

**Option 1: ValidationError → GeometryErrorDetail 변환 시 좌표 추출**

현재 시스템에서 `ValidationError`를 `GeometryErrorDetail`로 변환하는 부분이 있다면, 그 시점에서 원본 FGDB에서 지오메트리를 다시 읽어와 중점을 추출해야 합니다.

```csharp
// QcErrorService의 변환 로직에서
private async Task<GeometryErrorDetail> ConvertValidationError(
    ValidationError error, string sourceGdbPath)
{
    // 원본 FGDB에서 지오메트리 추출
    var (geometry, x, y, geomType) = await ExtractGeometryInfoAsync(
        sourceGdbPath, error.TableName, error.FeatureId);

    // LineString의 중점 계산
    double midX = 0, midY = 0;
    string? wkt = null;

    if (geometry != null && geometry.GetGeometryType() == wkbGeometryType.wkbLineString)
    {
        int pointCount = geometry.GetPointCount();
        int midIndex = pointCount / 2;
        midX = geometry.GetX(midIndex);
        midY = geometry.GetY(midIndex);

        geometry.ExportToWkt(out wkt);
    }

    return new GeometryErrorDetail
    {
        ObjectId = error.FeatureId,
        ErrorType = "짧은 선 객체",
        ErrorValue = error.Message,
        DetailMessage = error.Message,
        X = midX,  // ✅ 선 중점 X
        Y = midY,  // ✅ 선 중점 Y
        GeometryWkt = wkt  // ✅ 전체 선 지오메트리
    };
}
```

**Option 2: 검출 시점에 바로 GeometryErrorDetail 생성**

더 나은 방법은 검출 시점에 바로 좌표를 추출하는 것입니다:

```csharp
if (length < _criteria.MinLineLength && length > 0)
{
    // ✅ 선의 중점 계산
    int pointCount = workingGeometry.GetPointCount();
    int midIndex = pointCount / 2;
    double midX = workingGeometry.GetX(midIndex);
    double midY = workingGeometry.GetY(midIndex);

    string wkt;
    workingGeometry.ExportToWkt(out wkt);

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_SHORT_LINE",
        Message = $"선이 너무 짧습니다: {length:F3}m (최소: {_criteria.MinLineLength}m)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Warning,
        Metadata =
        {
            ["X"] = midX.ToString(),  // ✅ 메타데이터에 좌표 저장
            ["Y"] = midY.ToString(),
            ["GeometryWkt"] = wkt
        }
    });
}
```

#### 우선순위: **중간-낮음** (Medium-Low)
- 짧은 선은 전체가 짧으므로, 시작점/중점/끝점 모두 큰 차이 없음
- 하지만 QC_Errors_Point에 저장하려면 좌표 필요

---

### 4️⃣ 작은 면적 (Small Area)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 320-334
- **검출 방식**: Polygon.GetArea() < MinPolygonArea

#### 현재 코드
```csharp
if (area > 0 && area < _criteria.MinPolygonArea)
{
    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_SMALL_AREA",
        Message = $"면적이 너무 작습니다: {area:F2}㎡ (최소: {_criteria.MinPolygonArea}㎡)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Warning
        // ❌ 좌표 없음
    });
}
```

#### 문제점
- 작은 폴리곤의 중심점(Centroid) 또는 Envelope 중심을 추출하지 않음
- `ValidationError`에 좌표 필드 없음

#### 개선 방안
```csharp
if (area > 0 && area < _criteria.MinPolygonArea)
{
    // ✅ 폴리곤 중심점 계산 (Envelope 중심)
    var envelope = new OSGeo.OGR.Envelope();
    workingGeometry.GetEnvelope(envelope);
    double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
    double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

    string wkt;
    workingGeometry.ExportToWkt(out wkt);

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_SMALL_AREA",
        Message = $"면적이 너무 작습니다: {area:F2}㎡ (최소: {_criteria.MinPolygonArea}㎡)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Warning,
        Metadata =
        {
            ["X"] = centerX.ToString(),  // ✅ 중심점 X
            ["Y"] = centerY.ToString(),  // ✅ 중심점 Y
            ["GeometryWkt"] = wkt
        }
    });
}
```

#### 대안: Centroid vs Envelope 중심
- **Envelope 중심**: 계산 빠름, 항상 Envelope 내부에 위치
- **Centroid**: 더 정확한 기하학적 중심, 하지만 복잡한 도형에서는 외부에 위치할 수 있음

작은 면적 객체는 대부분 단순하므로 **Envelope 중심**이 안전합니다.

#### 우선순위: **중간-낮음** (Medium-Low)
- 작은 폴리곤은 전체 크기가 작아 중심점 정확도가 덜 중요
- 하지만 Point 레이어 저장을 위해서는 필요

---

### 5️⃣ 최소 정점 부족 (Minimum Vertex)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 336-356
- **검출 방식**: 정점 수 < 최소 요구 정점 수

#### 현재 코드
```csharp
if (!minVertexCheck.IsValid)
{
    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_MIN_VERTEX",
        Message = $"정점 수가 부족합니다: {minVertexCheck.ObservedVertices}개 (최소: {minVertexCheck.RequiredVertices}개){detail}",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error,
        Metadata =
        {
            ["PolygonDebug"] = BuildPolygonDebugInfo(workingGeometry, minVertexCheck)
        }
        // ❌ 좌표 없음
    });
}
```

#### 문제점
- 정점이 부족한 객체의 대표 위치(첫 번째 정점 또는 중심점)를 추출하지 않음

#### 개선 방안
```csharp
if (!minVertexCheck.IsValid)
{
    // ✅ 첫 번째 정점 추출 (가장 간단한 방법)
    double x = 0, y = 0;
    if (workingGeometry.GetPointCount() > 0)
    {
        x = workingGeometry.GetX(0);
        y = workingGeometry.GetY(0);
    }
    else
    {
        // Fallback: Envelope 중심
        var envelope = new OSGeo.OGR.Envelope();
        workingGeometry.GetEnvelope(envelope);
        x = (envelope.MinX + envelope.MaxX) / 2.0;
        y = (envelope.MinY + envelope.MaxY) / 2.0;
    }

    string wkt;
    workingGeometry.ExportToWkt(out wkt);

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_MIN_VERTEX",
        Message = $"정점 수가 부족합니다: {minVertexCheck.ObservedVertices}개 (최소: {minVertexCheck.RequiredVertices}개){detail}",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error,
        Metadata =
        {
            ["PolygonDebug"] = BuildPolygonDebugInfo(workingGeometry, minVertexCheck),
            ["X"] = x.ToString(),  // ✅ 대표 위치 X
            ["Y"] = y.ToString(),  // ✅ 대표 위치 Y
            ["GeometryWkt"] = wkt
        }
    });
}
```

#### 우선순위: **낮음** (Low)
- 정점 부족 오류는 객체 전체의 문제이므로 특정 위치보다는 객체 식별이 중요
- 하지만 일관성을 위해 좌표 추출 권장

---

### 6️⃣ 슬리버 폴리곤 (Sliver Polygon)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 456-497
- **검출 방식**: 면적, 형태지수, 신장률 기반

#### 현재 코드
```csharp
private bool IsSliverPolygon(Geometry geometry, out string message)
{
    // ... 계산 ...
    if (area < _criteria.SliverArea &&
        shapeIndex < _criteria.SliverShapeIndex &&
        elongation > _criteria.SliverElongation)
    {
        message = $"슬리버 폴리곤: 면적={area:F2}㎡ (< {_criteria.SliverArea}), " +
                  $"형태지수={shapeIndex:F3} (< {_criteria.SliverShapeIndex}), " +
                  $"신장률={elongation:F1} (> {_criteria.SliverElongation})";
        return true;
        // ❌ 좌표 반환 없음
    }
}
```

이 메서드는 bool만 반환하고, 호출하는 곳에서 ValidationError를 생성합니다.

#### 문제점
- 슬리버 폴리곤의 대표 위치(중심선의 중점 등)를 추출하지 않음
- 슬리버는 가늘고 긴 형태이므로, 단순 Envelope 중심보다는 **중심선(Skeleton)의 중점**이 더 적절

#### 개선 방안

**간단한 방법: Envelope 중심**
```csharp
if (IsSliverPolygon(workingGeometry, out message))
{
    // ✅ Envelope 중심 추출
    var envelope = new OSGeo.OGR.Envelope();
    workingGeometry.GetEnvelope(envelope);
    double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
    double centerY = (envelope.MinY + envelope.MaxY) / 2.0;

    string wkt;
    workingGeometry.ExportToWkt(out wkt);

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_SLIVER",
        Message = message,
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Warning,
        Metadata =
        {
            ["X"] = centerX.ToString(),
            ["Y"] = centerY.ToString(),
            ["GeometryWkt"] = wkt
        }
    });
}
```

**고급 방법: 외부 링의 중점**
```csharp
// 슬리버는 가늘고 긴 형태이므로, 외부 링의 중간 정점을 대표점으로 사용
if (workingGeometry.GetGeometryCount() > 0)
{
    var exteriorRing = workingGeometry.GetGeometryRef(0);
    int pointCount = exteriorRing.GetPointCount();
    int midIndex = pointCount / 2;
    double midX = exteriorRing.GetX(midIndex);
    double midY = exteriorRing.GetY(midIndex);
}
```

#### 우선순위: **중간** (Medium)
- 슬리버는 시각적으로 찾기 어려운 오류이므로 정확한 위치 파악이 유용
- 하지만 가늘고 긴 형태 자체가 특징이므로 대략적인 중심만으로도 충분

---

### 7️⃣ 스파이크 (Spike) - **이미 분석됨**

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 537-566

#### 현재 코드
```csharp
for (int i = 1; i < pointCount - 1; i++)
{
    var x1 = geometry.GetX(i - 1);
    var y1 = geometry.GetY(i - 1);
    var x2 = geometry.GetX(i);  // ✅ 스파이크 정점 좌표
    var y2 = geometry.GetY(i);  // ✅ 이미 계산됨
    var x3 = geometry.GetX(i + 1);
    var y3 = geometry.GetY(i + 1);

    var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

    if (angle < threshold)
    {
        message = $"스파이크 검출: 정점 {i}번 각도 {angle:F1}도 (임계값: {threshold}도)";
        return true;  // ❌ x2, y2를 반환하지 않음
    }
}
```

#### 개선 방안 (이미 제시됨)
```csharp
// 메서드 시그니처 변경
private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message, out double spikeX, out double spikeY)
{
    message = string.Empty;
    spikeX = 0;
    spikeY = 0;

    for (int i = 1; i < pointCount - 1; i++)
    {
        var x2 = geometry.GetX(i);
        var y2 = geometry.GetY(i);
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

#### 우선순위: **높음** (High)
- **이미 분석 완료**
- 스파이크는 정확한 정점 위치 파악이 매우 중요

---

### 8️⃣ 자체 꼬임/교차 (Self-Intersection)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 213-223
- **검출 방식**: GEOS `IsValid()` 내장 검사

#### 현재 코드
```csharp
if (!geometry.IsValid())
{
    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_INVALID",
        Message = "지오메트리 유효성 오류 (자체꼬임, 자기중첩, 홀폴리곤, 링방향 등)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error
        // ❌ 좌표 없음
        // ❌ 교차점 위치 불명
    });
}
```

#### 문제점
- **GEOS IsValid()는 단순히 true/false만 반환**하며, **어디서 교차했는지 정보를 제공하지 않음**
- 자체 교차점을 추출하려면 추가 분석 필요

#### 개선 방안

**방법 1: NetTopologySuite (NTS) 사용**

NTS는 `IsValid` 외에도 `ValidationError`를 통해 교차점 좌표를 제공할 수 있습니다:

```csharp
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

if (!geometry.IsValid())
{
    // GDAL Geometry를 NTS Geometry로 변환
    geometry.ExportToWkt(out string wkt);
    var reader = new NetTopologySuite.IO.WKTReader();
    var ntsGeom = reader.Read(wkt);

    // NTS 유효성 검사로 교차점 추출
    var validator = new IsValidOp(ntsGeom);
    var validationError = validator.ValidationError;

    double errorX = 0, errorY = 0;
    if (validationError != null && validationError.Coordinate != null)
    {
        errorX = validationError.Coordinate.X;
        errorY = validationError.Coordinate.Y;
    }
    else
    {
        // Fallback: Envelope 중심
        var envelope = new OSGeo.OGR.Envelope();
        geometry.GetEnvelope(envelope);
        errorX = (envelope.MinX + envelope.MaxX) / 2.0;
        errorY = (envelope.MinY + envelope.MaxY) / 2.0;
    }

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_INVALID",
        Message = $"지오메트리 유효성 오류: {validationError?.Message ?? "자체꼬임 또는 위상 오류"}",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error,
        Metadata =
        {
            ["X"] = errorX.ToString(),  // ✅ 교차점 또는 대표 위치 X
            ["Y"] = errorY.ToString(),  // ✅ 교차점 또는 대표 위치 Y
            ["GeometryWkt"] = wkt
        }
    });
}
```

**방법 2: 수동 교차점 계산 (고급)**

LineString의 경우 모든 선분 쌍을 비교하여 교차점을 계산:

```csharp
// 모든 선분 쌍을 비교
for (int i = 0; i < pointCount - 1; i++)
{
    for (int j = i + 2; j < pointCount - 1; j++)
    {
        // 선분 (i, i+1)과 (j, j+1)의 교차 검사
        if (SegmentsIntersect(i, j, geometry, out double intersectX, out double intersectY))
        {
            // 교차점 발견
            errorX = intersectX;
            errorY = intersectY;
            break;
        }
    }
}
```

#### 우선순위: **높음** (High)
- 자체 꼬임은 심각한 위상 오류로, 정확한 교차 위치 파악이 중요
- NTS를 이미 프로젝트에서 사용 중이므로 구현 용이

---

### 9️⃣ 자기 중첩 (Self-Overlap)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro.GUI/Services/GeometryValidationService.cs`
- **라인**: 373-404

#### 현재 코드
```csharp
private async Task<List<GeometryErrorDetail>> CheckSelfOverlapAsync(Layer layer)
{
    // NTS IsValid로 검사
    if (!nts.IsValid)
    {
        details.Add(new GeometryErrorDetail
        {
            ObjectId = GetObjectId(feature),
            ErrorType = "자기중첩",
            DetailMessage = "NTS 유효성 검사에서 위상 오류 감지"
            // ❌ X, Y 좌표 없음
        });
    }
}
```

#### 문제점
- 자기 중첩 영역의 위치를 추출하지 않음
- NTS `IsValid`만으로는 중첩 위치를 알 수 없음

#### 개선 방안

자기 중첩은 **폴리곤의 링(Ring)들이 서로 겹치는 경우** 발생합니다.

```csharp
private async Task<List<GeometryErrorDetail>> CheckSelfOverlapAsync(Layer layer)
{
    var details = new List<GeometryErrorDetail>();
    await Task.Run(() =>
    {
        var reader = new NetTopologySuite.IO.WKTReader();
        layer.ResetReading();
        Feature feature;
        while ((feature = layer.GetNextFeature()) != null)
        {
            try
            {
                var geom = feature.GetGeometryRef();
                if (geom == null || geom.IsEmpty()) continue;
                if (geom.GetGeometryType() != wkbGeometryType.wkbPolygon &&
                    geom.GetGeometryType() != wkbGeometryType.wkbMultiPolygon) continue;

                geom.ExportToWkt(out string wkt);
                var nts = reader.Read(wkt);

                // ✅ NTS ValidationError로 문제 위치 추출
                var validator = new NetTopologySuite.Operation.Valid.IsValidOp(nts);
                var validationError = validator.ValidationError;

                if (!nts.IsValid && validationError != null)
                {
                    double errorX = 0, errorY = 0;

                    if (validationError.Coordinate != null)
                    {
                        errorX = validationError.Coordinate.X;
                        errorY = validationError.Coordinate.Y;
                    }
                    else
                    {
                        // Fallback
                        var envelope = nts.EnvelopeInternal;
                        errorX = envelope.Centre.X;
                        errorY = envelope.Centre.Y;
                    }

                    details.Add(new GeometryErrorDetail
                    {
                        ObjectId = GetObjectId(feature),
                        ErrorType = "자기중첩",
                        DetailMessage = $"위상 오류: {validationError.Message}",
                        X = errorX,  // ✅ 오류 위치 X
                        Y = errorY,  // ✅ 오류 위치 Y
                        GeometryWkt = wkt
                    });
                }
            }
            finally { feature.Dispose(); }
        }
    });
    return details;
}
```

#### 우선순위: **높음** (High)
- 자기 중첩은 심각한 위상 오류
- NTS를 이미 사용 중이므로 ValidationError 활용 가능

---

### 🔟 언더슛 (Undershoot) - **이미 분석됨**

#### 현재 구현 위치
- **파일**: `SpatialCheckPro.GUI/Services/GeometryValidationService.cs`
- **라인**: 406-501

#### 현재 코드
```csharp
var coord = new NetTopologySuite.Operation.Distance.DistanceOp(p, closestLine).NearestPoints()[1];
var closestPointOnTarget = new NetTopologySuite.Geometries.Point(coord);

// ✅ 좌표 계산됨
var sourceEndpoint = p;  // 원점 끝점
var closestPointOnTarget;  // 대상 선상의 가장 가까운 점

details.Add(new GeometryErrorDetail
{
    ObjectId = objectId,
    ErrorType = isEndpoint ? "오버슛" : "언더슛",
    DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)"
    // ❌ sourceEndpoint 좌표 미설정
});
```

#### 개선 방안 (이미 제시됨)
```csharp
details.Add(new GeometryErrorDetail
{
    ObjectId = objectId,
    ErrorType = "언더슛",
    DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)",
    X = p.X,  // ✅ 끝점 X
    Y = p.Y,  // ✅ 끝점 Y
    GeometryWkt = CreateGapLineWkt(p, closestPointOnTarget)  // ✅ 간격 선분
});
```

#### 우선순위: **높음** (High) - **이미 분석 완료**

---

### 1️⃣1️⃣ 오버슛 (Overshoot) - **이미 분석됨**

#### 현재 구현 위치
- 언더슛과 동일한 메서드에서 처리 (GeometryValidationService.cs:406-501)

#### 개선 방안 (이미 제시됨)
```csharp
details.Add(new GeometryErrorDetail
{
    ObjectId = objectId,
    ErrorType = "오버슛",
    DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)",
    X = p.X,  // ✅ 돌출 끝점 X
    Y = p.Y,  // ✅ 돌출 끝점 Y
    GeometryWkt = CreateGapLineWkt(p, closestPointOnTarget)  // ✅ 간격 선분
});
```

#### 우선순위: **높음** (High) - **이미 분석 완료**

---

### 1️⃣2️⃣ NULL/Empty/Invalid 지오메트리

#### 현재 구현 위치
- **파일**: `SpatialCheckPro.GUI/Services/GeometryValidationService.cs`
- **라인**: 202-213

#### 현재 코드
```csharp
if (g == null)
{
    list.Add(new GeometryErrorDetail {
        ObjectId = GetObjectId(f),
        ErrorType = "기본검수",
        DetailMessage = "NULL 지오메트리"
        // ❌ X, Y 좌표 없음 (지오메트리가 없으므로 추출 불가)
    });
}
if (g.IsEmpty())
{
    list.Add(new GeometryErrorDetail {
        ObjectId = GetObjectId(f),
        ErrorType = "기본검수",
        DetailMessage = "빈 지오메트리"
        // ❌ X, Y 좌표 없음
    });
}
```

#### 문제점
- **NULL 또는 Empty 지오메트리는 좌표를 추출할 수 없음**
- 이 오류는 본질적으로 지오메트리가 존재하지 않는 경우

#### 개선 방안

**해결 불가능 - QC_Errors_NoGeom 테이블에 저장해야 함**

```csharp
// NULL/Empty는 좌표가 없으므로 X=0, Y=0으로 저장
// QcErrorDataService의 3-stage fallback에서 자동으로 NoGeom 테이블로 분기됨
list.Add(new GeometryErrorDetail {
    ObjectId = GetObjectId(f),
    ErrorType = "기본검수",
    DetailMessage = "NULL 지오메트리",
    X = 0,  // ⚠️ 명시적으로 0 설정
    Y = 0   // ⚠️ NoGeom 테이블로 저장됨
});
```

#### 우선순위: **해당 없음** (N/A)
- 지오메트리가 없는 오류이므로 위치 개선 불가능
- 현재 NoGeom 테이블 저장 방식이 올바름

---

### 1️⃣3️⃣ 홀 폴리곤/링 방향 (Polygon-in-Polygon / Ring Orientation)

#### 현재 구현 위치
- **파일**: `SpatialCheckPro/Processors/GeometryCheckProcessor.cs`
- **라인**: 213-223 (GEOS IsValid 내에 포함)

#### 현재 상태
- GEOS `IsValid()` 검사는 다음을 모두 포함:
  - 자체 꼬임
  - 자기 중첩
  - **홀(Hole)이 쉘(Shell) 외부에 있는 경우**
  - **링 방향이 잘못된 경우** (외부 링 시계 반대, 내부 링 시계 방향)

#### 문제점
- `IsValid()`는 단순 true/false만 반환하므로, **어떤 종류의 오류인지 구분 불가**
- 홀 폴리곤 오류의 경우, **문제가 있는 홀의 위치**를 파악해야 함

#### 개선 방안

**NTS를 사용한 상세 오류 분류**

```csharp
if (!geometry.IsValid())
{
    geometry.ExportToWkt(out string wkt);
    var reader = new NetTopologySuite.IO.WKTReader();
    var ntsGeom = reader.Read(wkt);

    var validator = new NetTopologySuite.Operation.Valid.IsValidOp(ntsGeom);
    var validationError = validator.ValidationError;

    string errorType = "지오메트리 유효성 오류";
    double errorX = 0, errorY = 0;

    if (validationError != null)
    {
        // NTS TopologyValidationError 타입 확인
        var errorTypeCode = validationError.ErrorType;

        switch (errorTypeCode)
        {
            case NetTopologySuite.Operation.Valid.TopologyValidationErrors.SelfIntersection:
                errorType = "자체 꼬임";
                break;
            case NetTopologySuite.Operation.Valid.TopologyValidationErrors.HoleOutsideShell:
                errorType = "홀이 쉘 외부에 위치";
                break;
            case NetTopologySuite.Operation.Valid.TopologyValidationErrors.RingNotClosed:
                errorType = "링이 닫히지 않음";
                break;
            case NetTopologySuite.Operation.Valid.TopologyValidationErrors.RingSelfIntersection:
                errorType = "링 자체 교차";
                break;
            // ... 기타 오류 타입들
        }

        // 오류 발생 좌표 추출
        if (validationError.Coordinate != null)
        {
            errorX = validationError.Coordinate.X;
            errorY = validationError.Coordinate.Y;
        }
        else
        {
            // Fallback
            var envelope = new OSGeo.OGR.Envelope();
            geometry.GetEnvelope(envelope);
            errorX = (envelope.MinX + envelope.MaxX) / 2.0;
            errorY = (envelope.MinY + envelope.MaxY) / 2.0;
        }
    }

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_INVALID",
        Message = $"{errorType}: {validationError?.Message ?? "위상 오류"}",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error,
        Metadata =
        {
            ["X"] = errorX.ToString(),  // ✅ 오류 위치 X
            ["Y"] = errorY.ToString(),  // ✅ 오류 위치 Y
            ["GeometryWkt"] = wkt,
            ["ErrorType"] = errorType
        }
    });
}
```

#### 우선순위: **높음** (High)
- 홀 폴리곤 오류는 복잡한 위상 오류로 정확한 위치 파악 필요
- NTS ValidationError의 상세 정보 활용 가능

---

## 🎯 우선순위별 개선 로드맵

### Priority 1: 긴급 (Urgent) - Stage 4, 5 저장 문제
- **관계 검수 (REL)**: RelationErrorsIntegrator.cs 수정 (이미 지시서 작성됨)
- **속성 관계 검수 (ATTR_REL)**: 지오메트리 추출 로직 추가

### Priority 2: 높음 (High) - 정확한 위치 파악이 중요한 오류
1. **겹침 지오메트리**: 교차 영역 중심점 추출
2. **스파이크**: 스파이크 정점 좌표 반환
3. **언더슛**: 끝점 좌표 추출
4. **오버슛**: 돌출 끝점 좌표 추출
5. **자체 꼬임/교차**: NTS ValidationError로 교차점 추출
6. **자기 중첩**: NTS ValidationError로 중첩 위치 추출
7. **홀 폴리곤**: NTS ValidationError로 문제 홀 위치 추출

### Priority 3: 중간 (Medium)
1. **중복 지오메트리**: Envelope 중심점 추출
2. **슬리버 폴리곤**: 외부 링 중점 또는 Envelope 중심

### Priority 4: 낮음 (Low) - 상대적으로 덜 중요
1. **짧은 선 객체**: 선 중점 추출
2. **작은 면적**: Envelope 중심점 추출
3. **최소 정점 부족**: 첫 번째 정점 또는 중심점 추출

### Priority 5: 해당 없음 (N/A)
- **NULL/Empty 지오메트리**: 개선 불가능, 현재 NoGeom 저장 방식 유지

---

## 📋 구현 체크리스트

### Phase 1: ValidationError → GeometryErrorDetail 좌표 전달 인프라 구축
- [ ] `ValidationError` 클래스에 `Metadata` 딕셔너리 활용 (이미 존재)
- [ ] `QcErrorService`의 변환 로직에서 Metadata["X"], Metadata["Y"] 읽기
- [ ] 테스트: Metadata를 통한 좌표 전달 확인

### Phase 2: High Priority 오류 타입 개선
- [ ] **겹침**: SpatialIndexService에 IntersectionGeometry 추가
- [ ] **스파이크**: CheckSpikeInSingleGeometry 메서드 시그니처 수정
- [ ] **언더슛/오버슛**: GeometryValidationService에 X, Y 설정
- [ ] **자체 꼬임**: NTS ValidationError 활용
- [ ] **자기 중첩**: NTS ValidationError 활용
- [ ] **홀 폴리곤**: NTS TopologyValidationErrors로 오류 타입 분류

### Phase 3: Medium/Low Priority 오류 타입 개선
- [ ] **중복**: HighPerformanceGeometryValidator에 Envelope 중심 추가
- [ ] **슬리버**: IsSliverPolygon 호출 후 좌표 추출
- [ ] **짧은 선**: CheckBasicGeometricPropertiesInternalAsync에 중점 추가
- [ ] **작은 면적**: Envelope 중심 추가
- [ ] **최소 정점**: 첫 정점 추출

### Phase 4: 통합 테스트
- [ ] 모든 오류 타입이 QC_Errors_Point에 올바르게 저장되는지 확인
- [ ] X, Y 좌표가 0,0이 아닌지 확인
- [ ] GeometryWkt가 올바르게 저장되는지 확인

---

## 🔧 공통 유틸리티 함수 제안

모든 오류 타입에서 반복적으로 사용되는 좌표 추출 로직을 공통 함수로 분리:

```csharp
public static class GeometryCoordinateExtractor
{
    /// <summary>
    /// Envelope 중심점 추출
    /// </summary>
    public static (double X, double Y) GetEnvelopeCenter(OSGeo.OGR.Geometry geometry)
    {
        var envelope = new OSGeo.OGR.Envelope();
        geometry.GetEnvelope(envelope);
        double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
        double centerY = (envelope.MinY + envelope.MaxY) / 2.0;
        return (centerX, centerY);
    }

    /// <summary>
    /// LineString 중점 추출
    /// </summary>
    public static (double X, double Y) GetLineStringMidpoint(OSGeo.OGR.Geometry lineString)
    {
        if (lineString.GetGeometryType() != wkbGeometryType.wkbLineString)
            throw new ArgumentException("Geometry must be LineString");

        int pointCount = lineString.GetPointCount();
        if (pointCount == 0) return (0, 0);

        int midIndex = pointCount / 2;
        return (lineString.GetX(midIndex), lineString.GetY(midIndex));
    }

    /// <summary>
    /// NTS ValidationError에서 좌표 추출, 없으면 Envelope 중심 반환
    /// </summary>
    public static (double X, double Y) GetValidationErrorLocation(
        NetTopologySuite.Geometries.Geometry ntsGeometry,
        NetTopologySuite.Operation.Valid.TopologyValidationError? validationError)
    {
        if (validationError?.Coordinate != null)
        {
            return (validationError.Coordinate.X, validationError.Coordinate.Y);
        }
        else
        {
            var envelope = ntsGeometry.EnvelopeInternal;
            return (envelope.Centre.X, envelope.Centre.Y);
        }
    }

    /// <summary>
    /// 두 점 사이의 간격 선분 WKT 생성
    /// </summary>
    public static string CreateGapLineWkt(
        NetTopologySuite.Geometries.Point startPoint,
        NetTopologySuite.Geometries.Point endPoint)
    {
        var lineString = new NetTopologySuite.Geometries.LineString(
            new[] { startPoint.Coordinate, endPoint.Coordinate });
        return lineString.ToText();
    }
}
```

---

## 📊 예상 효과

### Before (현재)
- **Stage 1, 2**: NoGeom 테이블 저장 ✅ 정상
- **Stage 3**: Point 테이블 저장 ✅ 정상
- **Stage 4**: ~40% NoGeom, ~60% Point (불완전)
- **Stage 5**: 100% NoGeom (X=0, Y=0)
- **지오메트리 오류**: 대부분 X=0, Y=0 또는 대략적인 위치

### After (개선 후)
- **Stage 1, 2**: NoGeom 테이블 저장 ✅ 유지
- **Stage 3**: Point 테이블 저장 ✅ 유지
- **Stage 4**: 100% Point 테이블 저장 (지오메트리 추출)
- **Stage 5**: 조건부 Point 저장 (FGDB에 지오메트리 있는 경우)
- **지오메트리 오류**:
  - 겹침 → 교차 영역 중심점
  - 스파이크 → 정확한 스파이크 정점
  - 언더슛/오버슛 → 정확한 끝점
  - 자체 꼬임/중첩 → NTS 검출 교차점
  - 기타 → 적절한 대표 위치

---

## 🎓 참고 자료

### GEOS vs NTS 비교

| 기능 | GEOS (GDAL/OGR) | NetTopologySuite |
|------|----------------|------------------|
| IsValid() | ✅ true/false만 | ✅ ValidationError 제공 |
| 교차점 추출 | ❌ 불가능 | ✅ Coordinate 반환 |
| 오류 타입 분류 | ❌ 불가능 | ✅ TopologyValidationErrors |
| 성능 | 매우 빠름 | 빠름 |
| 권장 용도 | 대용량 스트리밍 검사 | 상세 오류 위치 파악 |

### 권장 전략
1. **1차 검사**: GEOS IsValid()로 빠른 검사
2. **2차 분석**: IsValid() = false인 경우, NTS로 상세 위치 파악

---

## 📝 결론

총 **13가지 지오메트리 오류 타입** 중:
- **12가지**는 위치 정확도 개선 가능
- **1가지**(NULL/Empty)는 본질적으로 개선 불가능

**우선순위**:
1. **긴급**: Stage 4, 5 저장 문제 해결
2. **높음**: 겹침, 스파이크, 언더슛, 오버슛, 자체 꼬임, 자기 중첩, 홀 폴리곤
3. **중간**: 중복, 슬리버
4. **낮음**: 짧은 선, 작은 면적, 최소 정점

**주요 기술 스택**:
- **GDAL/OGR**: 기본 지오메트리 처리, Envelope 중심
- **NetTopologySuite**: 상세 오류 위치 파악 (ValidationError)
- **공통 유틸리티**: 반복 로직 함수화

이 종합 개선 방안을 단계적으로 구현하면, 모든 지오메트리 오류가 정확한 위치 정보와 함께 QC_Errors_Point 테이블에 저장될 것입니다.
