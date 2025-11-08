# 3단계 (Stage 3) 지오메트리 오류 감지 및 에러 포인트 생성 분석

## 1. 개요

3단계(지오메트리 검수)는 SpatialCheckPro에서 벡터 지오메트리의 유효성, 기본 속성, 고급 특징을 검수하는 단계입니다.
특히 **스파이크(spike) 감지**는 위치 특정 오류로 정점의 정확한 좌표를 지정할 수 있는 오류 유형입니다.

## 2. 핵심 파일 구조

### 2.1 메인 처리 파일

| 파일명 | 역할 | 위치 |
|--------|------|------|
| `GeometryCheckProcessor.cs` | 3단계 메인 프로세서 | `Processors/` |
| `HighPerformanceGeometryValidator.cs` | 고성능 검수 엔진 | `Services/` |
| `GeometryCoordinateExtractor.cs` | 좌표 추출 유틸리티 | `Utils/` |
| `ValidationResultConverter.cs` | ValidationError → QcError 변환 | `Services/` |
| `QcErrorIntegrationService.cs` | QC 오류 통합 관리 | `Services/` |
| `GeometryValidationItem.cs` | 검수 결과 모델 | `Models/` |

### 2.2 stage 3 정의

`StageDefinitions.cs`에서:
```csharp
new StageDefinition(3, "stage3_geometry_check", "지오메트리 검수", true)
```

## 3. 스파이크 감지 (Spike Detection) 상세 분석

### 3.1 스파이크 감지 흐름

#### 단계 1: 검수 프로세스 실행
파일: `GeometryCheckProcessor.cs` - `ProcessAsync()` 메서드 (라인 60-184)

```csharp
public async Task<ValidationResult> ProcessAsync(
    string filePath,
    GeometryCheckConfig config,
    CancellationToken cancellationToken = default,
    string? streamingOutputPath = null)
```

#### 단계 2: 단일 순회 통합 검사
메서드: `CheckGeometryInSinglePassAsync()` (라인 193-637)

**특징:**
- **Phase 1.3 최적화**: 단일 순회로 모든 검사 수행
- **Feature 순회 중복 제거**: O(n) 시간 복잡도
- GEOS 유효성 + 기본 속성 + 고급 특징을 한 번에 검사

#### 단계 3-2: 스파이크 검사 (라인 555-579)

```csharp
if (config.ShouldCheckSpikes)
{
    geometryRef.ExportToWkt(out string wkt);
    if (HasSpike(geometryRef, out string spikeMessage, out double spikeX, out double spikeY))
    {
        _AddErrorToResult(new ValidationError
        {
            ErrorCode = "GEOM_SPIKE",
            Message = spikeMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning,
            X = spikeX,           // ★ 정점의 정확한 X 좌표
            Y = spikeY,           // ★ 정점의 정확한 Y 좌표
            GeometryWKT = QcError.CreatePointWKT(spikeX, spikeY),
            Metadata = { ... }
        });
    }
}
```

### 3.2 HasSpike() 메서드 - 스파이크 감지 로직

파일: `GeometryCheckProcessor.cs` (라인 1184-1220)

```csharp
private bool HasSpike(Geometry geometry, out string message, out double spikeX, out double spikeY)
{
    message = string.Empty;
    spikeX = 0;    // 초기값
    spikeY = 0;    // 초기값
    
    try
    {
        // 지오메트리 타입 평탄화 (25D 등 변형 타입 대응)
        var flattened = wkbFlatten(geometry.GetGeometryType());

        // 멀티폴리곤: 각 폴리곤의 모든 링 검사
        if (flattened == wkbGeometryType.wkbMultiPolygon)
        {
            var polyCount = geometry.GetGeometryCount();
            for (int p = 0; p < polyCount; p++)
            {
                var polygon = geometry.GetGeometryRef(p);
                if (polygon == null) continue;
                if (CheckSpikeInSingleGeometry(polygon, out message, out spikeX, out spikeY))
                {
                    return true;  // 첫 스파이크에서 종료
                }
            }
            return false;
        }

        // 폴리곤 또는 기타: 단일 지오메트리 경로
        return CheckSpikeInSingleGeometry(geometry, out message, out spikeX, out spikeY);
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "스파이크 검사 중 오류");
    }

    return false;
}
```

### 3.3 CheckSpikeInSingleGeometry() - 세부 검사

파일: `GeometryCheckProcessor.cs` (라인 1227-1263)

```csharp
private bool CheckSpikeInSingleGeometry(
    Geometry geometry, 
    out string message, 
    out double spikeX, 
    out double spikeY)
{
    message = string.Empty;
    spikeX = 0;
    spikeY = 0;

    var flattened = wkbFlatten(geometry.GetGeometryType());

    // CSV에서 로드한 임계값 사용 (도)
    var threshold = _criteria.SpikeAngleThresholdDegrees > 0 
        ? _criteria.SpikeAngleThresholdDegrees 
        : 10.0;

    // 폴리곤: 각 링 검사 (외곽링 + 홀)
    if (flattened == wkbGeometryType.wkbPolygon)
    {
        var ringCount = geometry.GetGeometryCount();
        for (int r = 0; r < ringCount; r++)
        {
            var ring = geometry.GetGeometryRef(r);
            if (ring == null) continue;
            if (CheckSpikeInLinearRing(ring, threshold, out message, out spikeX, out spikeY))
            {
                return true;
            }
        }
        return false;
    }

    // 링 또는 라인스트링: 직접 검사
    if (flattened == wkbGeometryType.wkbLinearRing || 
        flattened == wkbGeometryType.wkbLineString)
    {
        return CheckSpikeInLinearRing(geometry, threshold, out message, out spikeX, out spikeY);
    }

    return false;
}
```

### 3.4 CheckSpikeInLinearRing() - 최종 각도 검사

파일: `GeometryCheckProcessor.cs` (라인 1268-1348)

**알고리즘:**
1. 링의 정점 개수 확인 (최소 3개)
2. 폐합 여부 확인 (첫점 == 마지막점)
3. **순환 인덱싱(circular indexing)** 사용:
   ```
   prev = (i - 1 + count) % count
   next = (i + 1) % count
   ```
4. 각 정점에서 3점 각도 계산
5. 임계값 미만 정점을 스파이크 후보로 저장
6. **가장 날카로운 스파이크 반환**

```csharp
for (int i = 0; i < count; i++)
{
    int prev = (i - 1 + count) % count;
    int next = (i + 1) % count;

    var x1 = ring.GetX(prev);
    var y1 = ring.GetY(prev);
    var x2 = ring.GetX(i);      // ★ 현재 정점
    var y2 = ring.GetY(i);      // ★ 현재 정점
    var x3 = ring.GetX(next);
    var y3 = ring.GetY(next);

    var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

    // 최소 각도 추적
    if (angle < minAngle)
    {
        minAngle = angle;
        best = (i, x2, y2, angle);  // ★ 정점 좌표 저장
    }

    // 임계각도 미만 후보 저장
    if (angle < threshold)
    {
        spikeCandidates.Add((i, x2, y2, angle));
    }
}

// 결과 확정
if (spikeCandidates.Any())
{
    spikeX = best.x;  // ★ 추출된 X 좌표
    spikeY = best.y;  // ★ 추출된 Y 좌표
    message = $"스파이크 검출: 정점 {best.idx}번 각도 {best.angle:F1}도";
    return true;
}
```

## 4. 오류 포인트(Error Point) 생성 메커니즘

### 4.1 좌표 추출 전략

위치 특정 오류들은 다양한 방법으로 좌표를 추출합니다:

| 오류 유형 | 좌표 추출 방식 | 파일 위치 |
|----------|---------------|----------|
| **스파이크** | 스파이크 정점의 정확한 X, Y | `CheckSpikeInLinearRing()` 라인 1335-1336 |
| **슬리버** | 외부 링의 중점 | `GeometryCheckProcessor` 라인 520-534 |
| **자기 교차** | NTS ValidationError 좌표 또는 Envelope 중심 | `GeometryCoordinateExtractor.GetValidationErrorLocation()` |
| **중복** | Envelope 중심점 | `HighPerformanceGeometryValidator` 라인 140-141 |
| **겹침** | 교차 영역 중심점 | `HighPerformanceGeometryValidator` 라인 200-203 |

### 4.2 GeometryCoordinateExtractor 유틸리티

파일: `Utils/GeometryCoordinateExtractor.cs`

```csharp
public static class GeometryCoordinateExtractor
{
    /// Envelope 중심점 추출
    public static (double X, double Y) GetEnvelopeCenter(OSGeo.OGR.Geometry geometry)
    {
        var envelope = new OSGeo.OGR.Envelope();
        geometry.GetEnvelope(envelope);
        double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
        double centerY = (envelope.MinY + envelope.MaxY) / 2.0;
        return (centerX, centerY);
    }

    /// NTS ValidationError에서 좌표 추출
    public static (double X, double Y) GetValidationErrorLocation(
        NetTopologySuite.Geometries.Geometry ntsGeometry, 
        TopologyValidationError? validationError)
    {
        // 1) ValidationError가 좌표를 제공하는 경우
        if (validationError?.Coordinate != null)
        {
            return (validationError.Coordinate.X, validationError.Coordinate.Y);
        }

        // 2) IsSimpleOp의 비단순 위치 추출
        var simpleOp = new NetTopologySuite.Operation.Valid.IsSimpleOp(ntsGeometry);
        if (!simpleOp.IsSimple())
        {
            var nonSimple = simpleOp.NonSimpleLocation;
            if (nonSimple != null)
            {
                return (nonSimple.X, nonSimple.Y);
            }
        }

        // 3) 최종 폴백: Envelope 중심
        var env = ntsGeometry.EnvelopeInternal;
        return ((env.MinX + env.MaxX) / 2.0, (env.MinY + env.MaxY) / 2.0);
    }
}
```

### 4.3 ValidationError 모델

파일: `Models/ValidationError.cs` (라인 8-141)

```csharp
public class ValidationError
{
    public string ErrorCode { get; set; }        // "GEOM_SPIKE", "GEOM_SLIVER" 등
    public string Message { get; set; }
    public string TableName { get; set; }
    public string? FeatureId { get; set; }       // 오류가 발생한 객체 ID
    public ErrorSeverity Severity { get; set; }
    
    // ★ 위치 특정 오류용 속성
    public double? X { get; set; }               // 오류 위치 X 좌표
    public double? Y { get; set; }               // 오류 위치 Y 좌표
    public string? GeometryWKT { get; set; }     // Point WKT: "POINT (X Y)"
    
    // ★ 메타데이터 (상세 정보)
    public Dictionary<string, object> Metadata { get; set; }  // ["X"], ["Y"], ["GeometryWkt"] 등
}
```

### 4.4 QcError 생성 - CreatePointWKT()

파일: `Models/QcError.cs` (라인 255-258)

```csharp
public static string CreatePointWKT(double x, double y)
{
    return $"POINT ({x:F6} {y:F6})";
}
```

**정밀도**: 6자리 소수점 (F6 형식)

### 4.5 GeometryErrorDetail 모델

파일: `Models/GeometryValidationItem.cs` (라인 263-366)

```csharp
public class GeometryErrorDetail
{
    public string ObjectId { get; set; }           // 오류 객체 ID
    public string ErrorType { get; set; }          // "중복 지오메트리", "스파이크" 등
    public string ErrorValue { get; set; }         // 측정된 값
    public string ThresholdValue { get; set; }     // 임계값
    
    // ★ 위치 정보
    public string Location { get; set; }           // "X, Y" 문자열
    public double X { get; set; }                  // X 좌표
    public double Y { get; set; }                  // Y 좌표
    public string? GeometryWkt { get; set; }       // 원본 지오메트리 또는 Point WKT
    
    public string DetailMessage { get; set; }      // "스파이크 검출: 정점 5번 각도 8.5도"
}
```

## 5. 위치 특정 오류 vs 비위치 특정 오류

### 5.1 위치 특정 오류 (Location-Specific)

✓ **정확한 좌표를 지정할 수 있음**

| 오류 코드 | 오류 이름 | 좌표 지정 방식 | 
|----------|---------|--------------|
| GEOM_SPIKE | 스파이크(돌기) | **정점의 정확한 좌표** |
| GEOM_SLIVER | 슬리버 폴리곤 | 외부 링의 중점 |
| GEOM_SELF_INTERSECTION | 자기 교차 | ValidationError 좌표 |
| GEOM_DUPLICATE | 중복 지오메트리 | Envelope 중심 |
| GEOM_OVERLAP | 겹침 | 교차 영역 중심 |

### 5.2 비위치 특정 오류 (Non-Location-Specific)

✗ **좌표를 지정할 수 없음**

| 오류 코드 | 오류 이름 | 특징 |
|----------|---------|-----|
| TABLE_MISSING | 테이블 누락 | 테이블 수준 오류 |
| SCHEMA_* | 스키마 오류 | 필드/열 수준 오류 |
| REL_* | 공간 관계 오류 | 테이블 간 관계 오류 |
| ATTR_REL_* | 속성 관계 오류 | 속성값 기반 오류 |

**오류 포인트가 없음**: X=0, Y=0, GeometryWKT=NULL

## 6. 에러 포인트 생성 흐름

```
┌─────────────────────────────────────┐
│  GeometryCheckProcessor.ProcessAsync│
└─────────────────┬───────────────────┘
                  │
                  ▼
      ┌───────────────────────┐
      │ CheckGeometryInSingle │
      │        PassAsync      │
      └───────────┬───────────┘
                  │
         ┌────────┴────────┐
         ▼                 ▼
    ┌─────────┐      ┌──────────┐
    │ HasSpike│      │  IsSliver│
    │ (X,Y가 │      │ (중심 좌표)
    │  정확함)│      └──────────┘
    └────┬────┘
         │
         ▼
    ┌─────────────────────┐
    │  ValidationError    │
    │  X = spikeX         │
    │  Y = spikeY         │
    │  GeometryWKT =      │
    │   CreatePointWKT()  │
    └────┬────────────────┘
         │
         ▼
    ┌──────────────────────┐
    │ValidationResultConv- │
    │erter.ConvertValidat- │
    │ionResultToQcErrors   │
    └────┬─────────────────┘
         │
         ▼
    ┌──────────────────────┐
    │  GeometryErrorDetail │
    │  X, Y 좌표           │
    │  GeometryWkt         │
    └────┬─────────────────┘
         │
         ▼
    ┌──────────────────────┐
    │  QcError             │
    │  X, Y                │
    │  GeometryWKT =       │
    │  "POINT (X Y)"       │
    └──────────────────────┘
```

## 7. 에러 포인트가 생성되지 않는 경우

### 7.1 원인 분석

#### 1) **초기값 미갱신 (0, 0)**
```csharp
spikeX = 0;
spikeY = 0;
// HasSpike가 false를 반환하면 X, Y는 (0, 0)으로 유지
```

**해결책**: `HasSpike()` 메서드가 true를 반환해야 좌표가 갱신됨

#### 2) **스파이크 임계값 설정 오류**
```csharp
var threshold = _criteria.SpikeAngleThresholdDegrees > 0 
    ? _criteria.SpikeAngleThresholdDegrees 
    : 10.0;  // 기본값: 10도
```

**검사 항목:**
- GeometryCriteria에서 `SpikeAngleThresholdDegrees` 값 확인
- 임계값이 너무 작으면 스파이크를 감지하지 못할 수 있음

#### 3) **좌표 추출 실패**
```csharp
if (spikeCandidates.Any())
{
    spikeX = best.x;  // 좌표가 유효해야 함
    spikeY = best.y;
    return true;
}
```

**원인:**
- Ring의 정점 접근 실패: `ring.GetX(i)` 반환값이 유효하지 않음
- 지오메트리 타입이 Ring/LineString이 아님

#### 4) **ValidationError 매핑 실패**
`ValidationResultConverter.ConvertGeometryValidationItem()` (라인 275-464)

```csharp
double outX = errorDetail.X;  // 기본값 사용
double outY = errorDetail.Y;

if ((outX == 0 && outY == 0) && geometry != null)
{
    // 지오메트리에서 좌표 보완
    // ...
}
```

**문제**: 좌표 보완 로직이 실패하면 (0, 0) 유지

### 7.2 디버깅 체크리스트

1. **로그 확인**
   ```csharp
   _logger.LogInformation("단일 순회 통합 검사 완료: {ErrorCount}개 오류",
       errors.Count);
   ```

2. **검사 설정 확인**
   ```csharp
   if (config.ShouldCheckSpikes)  // true인가?
   ```

3. **GeometryCriteria 값 확인**
   ```csharp
   var threshold = _criteria.SpikeAngleThresholdDegrees;
   ```

4. **Metadata 검사**
   ```csharp
   Metadata = {
       ["X"] = spikeX.ToString(),    // 정상값인가?
       ["Y"] = spikeY.ToString(),    // 정상값인가?
       ["GeometryWkt"] = wkt
   }
   ```

## 8. 에러 포인트 변환 과정의 키 메서드들

### 8.1 ValidationError → GeometryErrorDetail 변환
없음 (직접 QcError로 변환)

### 8.2 ValidationError → QcError 변환
파일: `Services/ValidationResultConverter.cs` (라인 275-464)

```csharp
private List<QcError> ConvertGeometryValidationItem(
    GeometryValidationItem geometryResult, 
    Guid runId)
{
    // GeometryErrorDetail 순회
    foreach (var errorDetail in geometryResult.ErrorDetails)
    {
        // 좌표 보완
        double outX = errorDetail.X;
        double outY = errorDetail.Y;
        
        if ((outX == 0 && outY == 0) && geometry != null)
        {
            // 지오메트리 타입별로 대표 좌표 추출
            // Point: GetPoint(0)
            // LineString: GetPoint(0)
            // Polygon: PointOnSurface() 또는 첫 링의 첫 점
        }
        
        // QcError 생성
        var qcError = new QcError
        {
            X = outX,
            Y = outY,
            GeometryWKT = QcError.CreatePointWKT(outX, outY)
        };
    }
}
```

## 9. 3단계 오류 검수 과정 요약

### 검수 항목 (라인 279-579)

1. **GEOS 유효성** (라인 279-379)
   - IsValid() 검사
   - IsSimple() 검사

2. **기본 기하 속성** (라인 382-461)
   - 짧은 선 (라인 400-431)
   - 작은 면적 (라인 434-461)
   - 최소 정점 (라인 464-505)

3. **고급 기하 특징** (라인 507-579)
   - **슬리버 검사** (라인 512-552)
   - **스파이크 검사** (라인 555-579) ★

### 스파이크 오류 속성

```csharp
new ValidationError
{
    ErrorCode = "GEOM_SPIKE",
    Message = spikeMessage,           // "스파이크 검출: 정점 5번 각도 8.5도"
    TableName = config.TableId,       // 테이블 ID
    FeatureId = fid.ToString(),       // 객체 FID
    Severity = ErrorSeverity.Warning, // 경고 수준
    X = spikeX,                       // 정점의 X 좌표
    Y = spikeY,                       // 정점의 Y 좌표
    GeometryWKT = QcError.CreatePointWKT(spikeX, spikeY),
    Metadata = {
        ["X"] = spikeX.ToString(),
        ["Y"] = spikeY.ToString(),
        ["GeometryWkt"] = wkt,                    // 원본 지오메트리
        ["OriginalGeometryWKT"] = wkt
    }
}
```

## 10. 중요한 인사이트

### 10.1 좌표의 정확성

**스파이크 좌표는 매우 정확함:**
- 정점의 정확한 X, Y 좌표를 직접 추출
- 생성되는 Point WKT는 정밀도 F6 (6자리 소수점)

```csharp
public static string CreatePointWKT(double x, double y)
{
    return $"POINT ({x:F6} {y:F6})";
}
```

### 10.2 위치 vs 메타데이터

**2가지 저장 방식:**

1. **위치 필드 (Primary)**
   - X, Y: 검색/필터링용 (쿼리 성능)
   - GeometryWKT: Point 지오메트리 (GIS 시각화용)

2. **메타데이터 (Secondary)**
   - Metadata["X"], Metadata["Y"]: 참고용
   - Metadata["GeometryWkt"]: 원본 지오메트리 저장 (분석용)

### 10.3 에러 카운팅

파일: `Models/GeometryValidationItem.cs` (라인 76)

```csharp
public int SpikeCount { get; set; }

public int TotalErrorCount => DuplicateCount + OverlapCount + 
    SelfIntersectionCount + SliverCount + ShortObjectCount + 
    SmallAreaCount + PolygonInPolygonCount + BasicValidationErrorCount +
    MinPointCount + SpikeCount + SelfOverlapCount + 
    UndershootCount + OvershootCount;
```

### 10.4 성능 최적화

**Phase 1.3: 단일 순회 최적화**
- 개별 메서드 호출 제거 → O(1) 오버헤드 감소
- 메모리 누적 방지 (스트리밍 모드)

**Phase 2 Item #7: 대용량 Geometry 스트리밍**
- 메모리 누적 방지
- 배치 단위 처리 (1000개 단위)

## 11. 결론

### 3단계 지오메트리 오류 감지의 핵심:

1. **스파이크는 위치 특정 오류**
   - 정점의 정확한 좌표를 추출
   - (0, 0) 이외의 유효한 좌표를 가짐

2. **에러 포인트는 자동 생성**
   - `CreatePointWKT(X, Y)` 메서드로 Point WKT 생성
   - X, Y 값이 0이 아닌 경우에만 유효한 위치 정보 제공

3. **메타데이터로 원본 정보 보존**
   - 원본 지오메트리는 Metadata["GeometryWkt"]에 저장
   - 상세 오류 메시지는 Message 필드에 저장

4. **비위치 특정 오류와 구분**
   - 테이블/스키마/관계 오류는 X=0, Y=0, GeometryWKT=NULL
   - 지오메트리 오류만 유효한 위치 정보 포함
