# Cursor AI 수정 지시서: 13가지 지오메트리 오류 위치 정확도 개선

## 📋 수정 개요

**목적**: 13가지 지오메트리 오류 타입에서 정확한 X, Y 좌표 추출
**영향 범위**: 3개 파일 수정 + 1개 유틸리티 클래스 신규 생성
**우선순위**: Phase 1 (긴급) → Phase 2 (높음) → Phase 3 (중간/낮음)

---

## 🎯 Phase 1: 공통 유틸리티 클래스 생성 (필수 선행 작업)

### 📁 신규 파일 생성
**파일 경로**: `/SpatialCheckPro/Utils/GeometryCoordinateExtractor.cs`

```csharp
using OSGeo.OGR;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace SpatialCheckPro.Utils
{
    /// <summary>
    /// 지오메트리 오류 위치 추출을 위한 공통 유틸리티 클래스
    /// </summary>
    public static class GeometryCoordinateExtractor
    {
        /// <summary>
        /// GDAL Geometry의 Envelope 중심점 추출
        /// </summary>
        public static (double X, double Y) GetEnvelopeCenter(OSGeo.OGR.Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty())
                return (0, 0);

            var envelope = new OSGeo.OGR.Envelope();
            geometry.GetEnvelope(envelope);
            double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
            double centerY = (envelope.MinY + envelope.MaxY) / 2.0;
            return (centerX, centerY);
        }

        /// <summary>
        /// LineString의 중점 추출
        /// </summary>
        public static (double X, double Y) GetLineStringMidpoint(OSGeo.OGR.Geometry lineString)
        {
            if (lineString == null || lineString.IsEmpty())
                return (0, 0);

            if (lineString.GetGeometryType() != wkbGeometryType.wkbLineString)
                return GetEnvelopeCenter(lineString);

            int pointCount = lineString.GetPointCount();
            if (pointCount == 0) return (0, 0);

            int midIndex = pointCount / 2;
            return (lineString.GetX(midIndex), lineString.GetY(midIndex));
        }

        /// <summary>
        /// Polygon 외부 링의 중점 추출
        /// </summary>
        public static (double X, double Y) GetPolygonRingMidpoint(OSGeo.OGR.Geometry polygon)
        {
            if (polygon == null || polygon.IsEmpty())
                return (0, 0);

            if (polygon.GetGeometryCount() > 0)
            {
                var exteriorRing = polygon.GetGeometryRef(0);
                if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                {
                    int pointCount = exteriorRing.GetPointCount();
                    int midIndex = pointCount / 2;
                    return (exteriorRing.GetX(midIndex), exteriorRing.GetY(midIndex));
                }
            }

            return GetEnvelopeCenter(polygon);
        }

        /// <summary>
        /// 첫 번째 정점 추출
        /// </summary>
        public static (double X, double Y) GetFirstVertex(OSGeo.OGR.Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty())
                return (0, 0);

            if (geometry.GetPointCount() > 0)
            {
                return (geometry.GetX(0), geometry.GetY(0));
            }

            return GetEnvelopeCenter(geometry);
        }

        /// <summary>
        /// NTS ValidationError에서 좌표 추출, 없으면 Envelope 중심 반환
        /// </summary>
        public static (double X, double Y) GetValidationErrorLocation(
            NetTopologySuite.Geometries.Geometry ntsGeometry,
            TopologyValidationError? validationError)
        {
            if (validationError?.Coordinate != null)
            {
                return (validationError.Coordinate.X, validationError.Coordinate.Y);
            }
            else if (ntsGeometry != null)
            {
                var envelope = ntsGeometry.EnvelopeInternal;
                return (envelope.Centre.X, envelope.Centre.Y);
            }

            return (0, 0);
        }

        /// <summary>
        /// 두 점 사이의 간격 선분 WKT 생성 (언더슛/오버슛용)
        /// </summary>
        public static string CreateGapLineWkt(
            NetTopologySuite.Geometries.Point startPoint,
            NetTopologySuite.Geometries.Point endPoint)
        {
            var lineString = new NetTopologySuite.Geometries.LineString(
                new[] { startPoint.Coordinate, endPoint.Coordinate });
            return lineString.ToText();
        }

        /// <summary>
        /// NTS ValidationError 타입을 한글 오류명으로 변환
        /// </summary>
        public static string GetKoreanErrorType(int errorType)
        {
            return errorType switch
            {
                0 => "자체 꼬임", // SelfIntersection
                1 => "링이 닫히지 않음", // RingNotClosed
                2 => "홀이 쉘 외부에 위치", // HoleOutsideShell
                3 => "중첩된 홀", // NestedHoles
                4 => "쉘과 홀 연결 해제", // DisconnectedInterior
                5 => "링 자체 교차", // RingSelfIntersection
                6 => "중첩된 링", // NestedShells
                7 => "중복된 링", // DuplicateRings
                8 => "너무 적은 점", // TooFewPoints
                9 => "유효하지 않은 좌표", // InvalidCoordinate
                10 => "링 자체 교차", // RingSelfIntersection (중복)
                _ => "지오메트리 유효성 오류"
            };
        }
    }
}
```

---

## 🎯 Phase 2: HIGH Priority - 정확한 위치가 중요한 오류 (7가지)

### 1️⃣ 스파이크 (GEOM_SPIKE)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 537-566번 라인

#### ❌ 수정 전 코드
```csharp
private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message)
{
    message = string.Empty;
    var pointCount = geometry.GetPointCount();

    if (pointCount < 3) return false;

    var threshold = _criteria.SpikeAngleThreshold;

    for (int i = 1; i < pointCount - 1; i++)
    {
        var x1 = geometry.GetX(i - 1);
        var y1 = geometry.GetY(i - 1);
        var x2 = geometry.GetX(i);
        var y2 = geometry.GetY(i);
        var x3 = geometry.GetX(i + 1);
        var y3 = geometry.GetY(i + 1);

        var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

        if (angle < threshold)
        {
            message = $"스파이크 검출: 정점 {i}번 각도 {angle:F1}도 (임계값: {threshold}도)";
            return true;  // ❌ x2, y2를 반환하지 않음
        }
    }

    return false;
}
```

#### ✅ 수정 후 코드
```csharp
private bool CheckSpikeInSingleGeometry(Geometry geometry, out string message, out double spikeX, out double spikeY)
{
    message = string.Empty;
    spikeX = 0;
    spikeY = 0;

    var pointCount = geometry.GetPointCount();

    if (pointCount < 3) return false;

    var threshold = _criteria.SpikeAngleThreshold;

    for (int i = 1; i < pointCount - 1; i++)
    {
        var x1 = geometry.GetX(i - 1);
        var y1 = geometry.GetY(i - 1);
        var x2 = geometry.GetX(i);
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

#### 🔍 수정 위치: 502-532번 라인 (HasSpike 메서드)

#### ❌ 수정 전 코드
```csharp
private bool HasSpike(Geometry geometry, out string message)
{
    message = string.Empty;

    try
    {
        int geomCount = geometry.GetGeometryCount();
        if (geomCount > 0)
        {
            for (int g = 0; g < geomCount; g++)
            {
                var part = geometry.GetGeometryRef(g);
                if (part != null && CheckSpikeInSingleGeometry(part, out message))
                {
                    return true;
                }
            }
        }
        else
        {
            return CheckSpikeInSingleGeometry(geometry, out message);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "스파이크 검사 중 오류");
    }

    return false;
}
```

#### ✅ 수정 후 코드
```csharp
private bool HasSpike(Geometry geometry, out string message, out double spikeX, out double spikeY)
{
    message = string.Empty;
    spikeX = 0;
    spikeY = 0;

    try
    {
        int geomCount = geometry.GetGeometryCount();
        if (geomCount > 0)
        {
            for (int g = 0; g < geomCount; g++)
            {
                var part = geometry.GetGeometryRef(g);
                if (part != null && CheckSpikeInSingleGeometry(part, out message, out spikeX, out spikeY))
                {
                    return true;
                }
            }
        }
        else
        {
            return CheckSpikeInSingleGeometry(geometry, out message, out spikeX, out spikeY);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "스파이크 검사 중 오류");
    }

    return false;
}
```

#### 🔍 수정 위치: 425-440번 라인 (HasSpike 호출 부분)

#### ❌ 수정 전 코드
```csharp
if (config.ShouldCheckSpikes && GeometryRepresentsPolygon(workingGeometry))
{
    if (HasSpike(workingGeometry, out spikeMessage))
    {
        errors.Add(new ValidationError
        {
            ErrorCode = "GEOM_SPIKE",
            Message = spikeMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning
        });
    }
}
```

#### ✅ 수정 후 코드
```csharp
if (config.ShouldCheckSpikes && GeometryRepresentsPolygon(workingGeometry))
{
    if (HasSpike(workingGeometry, out spikeMessage, out double spikeX, out double spikeY))
    {
        string wkt;
        workingGeometry.ExportToWkt(out wkt);

        errors.Add(new ValidationError
        {
            ErrorCode = "GEOM_SPIKE",
            Message = spikeMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning,
            Metadata =
            {
                ["X"] = spikeX.ToString(),  // ✅ 스파이크 정점 X
                ["Y"] = spikeY.ToString(),  // ✅ 스파이크 정점 Y
                ["GeometryWkt"] = wkt
            }
        });
    }
}
```

---

### 2️⃣ 자체 꼬임/교차 (GEOM_INVALID)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 213-223번 라인

#### ❌ 수정 전 코드
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
    });
}
```

#### ✅ 수정 후 코드
```csharp
if (!geometry.IsValid())
{
    // NTS를 사용하여 정확한 오류 위치 추출
    geometry.ExportToWkt(out string wkt);
    var reader = new NetTopologySuite.IO.WKTReader();
    var ntsGeom = reader.Read(wkt);

    var validator = new NetTopologySuite.Operation.Valid.IsValidOp(ntsGeom);
    var validationError = validator.ValidationError;

    double errorX = 0, errorY = 0;
    string errorTypeName = "지오메트리 유효성 오류";

    if (validationError != null)
    {
        // 오류 타입 한글 변환
        errorTypeName = Utils.GeometryCoordinateExtractor.GetKoreanErrorType(validationError.ErrorType);

        // 오류 위치 추출
        (errorX, errorY) = Utils.GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
    }
    else
    {
        // Fallback: Envelope 중심
        (errorX, errorY) = Utils.GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
    }

    errors.Add(new ValidationError
    {
        ErrorCode = "GEOM_INVALID",
        Message = validationError != null
            ? $"{errorTypeName}: {validationError.Message}"
            : "지오메트리 유효성 오류 (자체꼬임, 자기중첩, 홀폴리곤, 링방향 등)",
        TableName = config.TableId,
        FeatureId = fid.ToString(),
        Severity = Models.Enums.ErrorSeverity.Error,
        Metadata =
        {
            ["X"] = errorX.ToString(),  // ✅ 오류 위치 X
            ["Y"] = errorY.ToString(),  // ✅ 오류 위치 Y
            ["GeometryWkt"] = wkt,
            ["ErrorType"] = errorTypeName
        }
    });
}
```

#### 📦 필요한 using 추가
파일 상단에 다음 using 추가:
```csharp
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Valid;
```

---

### 3️⃣ 자기 중첩 (Self-Overlap)

#### 📁 수정 파일
`/SpatialCheckPro.GUI/Services/GeometryValidationService.cs`

#### 🔍 수정 위치: 373-404번 라인

#### ❌ 수정 전 코드
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
                if (!nts.IsValid)
                {
                    details.Add(new GeometryErrorDetail
                    {
                        ObjectId = GetObjectId(feature),
                        ErrorType = "자기중첩",
                        DetailMessage = "NTS 유효성 검사에서 위상 오류 감지"
                    });
                }
            }
            finally { feature.Dispose(); }
        }
    });
    return details;
}
```

#### ✅ 수정 후 코드
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

                // ✅ NTS ValidationError로 정확한 위치 추출
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

#### 📦 필요한 using 추가
```csharp
using NetTopologySuite.Operation.Valid;
```

---

### 4️⃣ 언더슛/오버슛 (Undershoot/Overshoot)

#### 📁 수정 파일
`/SpatialCheckPro.GUI/Services/GeometryValidationService.cs`

#### 🔍 수정 위치: 477-495번 라인

#### ❌ 수정 전 코드
```csharp
if (!isConnected && minDistance < searchDistance && closestLine != null)
{
    var coord = new NetTopologySuite.Operation.Distance.DistanceOp(p, closestLine).NearestPoints()[1];
    var closestPointOnTarget = new NetTopologySuite.Geometries.Point(coord);

    var targetStart = closestLine.StartPoint;
    var targetEnd = closestLine.EndPoint;

    bool isEndpoint = closestPointOnTarget.Distance(targetStart) < 1e-9 ||
                      closestPointOnTarget.Distance(targetEnd) < 1e-9;

    details.Add(new GeometryErrorDetail
    {
        ObjectId = objectId,
        ErrorType = isEndpoint ? "오버슛" : "언더슛",
        DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)"
    });
    goto NextLine;
}
```

#### ✅ 수정 후 코드
```csharp
if (!isConnected && minDistance < searchDistance && closestLine != null)
{
    var coord = new NetTopologySuite.Operation.Distance.DistanceOp(p, closestLine).NearestPoints()[1];
    var closestPointOnTarget = new NetTopologySuite.Geometries.Point(coord);

    var targetStart = closestLine.StartPoint;
    var targetEnd = closestLine.EndPoint;

    bool isEndpoint = closestPointOnTarget.Distance(targetStart) < 1e-9 ||
                      closestPointOnTarget.Distance(targetEnd) < 1e-9;

    // ✅ 간격 선분 WKT 생성
    var gapLineString = new NetTopologySuite.Geometries.LineString(
        new[] { p.Coordinate, closestPointOnTarget.Coordinate });
    string gapLineWkt = gapLineString.ToText();

    details.Add(new GeometryErrorDetail
    {
        ObjectId = objectId,
        ErrorType = isEndpoint ? "오버슛" : "언더슛",
        DetailMessage = $"선 끝점 비연결 (최소 이격 {minDistance:F3}m)",
        X = p.X,  // ✅ 끝점 X
        Y = p.Y,  // ✅ 끝점 Y
        GeometryWkt = gapLineWkt  // ✅ 간격 선분
    });
    goto NextLine;
}
```

---

### 5️⃣ 겹침 지오메트리 (Overlap)

#### 📁 수정 파일
`/SpatialCheckPro/Services/HighPerformanceGeometryValidator.cs`

#### ⚠️ 복잡도: 높음
이 수정은 `SpatialIndexService.FindOverlaps()` 메서드가 교차 지오메트리를 반환하도록 수정해야 합니다.

#### Step 1: OverlapInfo 클래스 수정

**위치**: OverlapInfo 클래스 정의 (파일 내 검색 필요)

#### ❌ 수정 전
```csharp
public class OverlapInfo
{
    public long ObjectId { get; set; }
    public long OtherObjectId { get; set; }
    public double OverlapArea { get; set; }
}
```

#### ✅ 수정 후
```csharp
public class OverlapInfo
{
    public long ObjectId { get; set; }
    public long OtherObjectId { get; set; }
    public double OverlapArea { get; set; }
    public OSGeo.OGR.Geometry? IntersectionGeometry { get; set; }  // ✅ 추가
}
```

#### Step 2: SpatialIndexService.FindOverlaps() 수정

**위치**: `SpatialIndexService` 클래스의 `FindOverlaps()` 메서드

겹침을 검출하는 부분에서 교차 지오메트리를 계산하고 OverlapInfo에 포함:

```csharp
// 교차 영역 계산
var intersection = geom1.Intersection(geom2);
if (intersection != null && !intersection.IsEmpty())
{
    overlaps.Add(new OverlapInfo
    {
        ObjectId = objectId1,
        OtherObjectId = objectId2,
        OverlapArea = intersection.GetArea(),
        IntersectionGeometry = intersection.Clone()  // ✅ 교차 지오메트리 저장
    });
}
```

#### Step 3: HighPerformanceGeometryValidator 수정

#### 🔍 수정 위치: 122-132번 라인

#### ❌ 수정 전 코드
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
    });
}
```

#### ✅ 수정 후 코드
```csharp
foreach (var overlap in overlaps)
{
    double centerX = 0, centerY = 0;
    string? intersectionWkt = null;

    // ✅ 교차 영역의 중심점 추출
    if (overlap.IntersectionGeometry != null && !overlap.IntersectionGeometry.IsEmpty())
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

---

## 🎯 Phase 3: MEDIUM Priority - 중복, 슬리버 (2가지)

### 6️⃣ 중복 지오메트리 (Duplicate)

#### 📁 수정 파일
`/SpatialCheckPro/Services/HighPerformanceGeometryValidator.cs`

#### 🔍 수정 위치: 76-85번 라인

#### ❌ 수정 전 코드
```csharp
for (int i = 1; i < group.Count; i++)
{
    var (objectId, geometry) = group[i];
    errorDetails.Add(new GeometryErrorDetail
    {
        ObjectId = objectId.ToString(),
        ErrorType = "중복 지오메트리",
        ErrorValue = $"정확히 동일한 지오메트리 (그룹 크기: {group.Count})",
        ThresholdValue = coordinateTolerance > 0 ? $"좌표 허용오차 {coordinateTolerance}m" : "Exact match",
        DetailMessage = coordinateTolerance > 0
            ? $"OBJECTID {objectId}: 좌표 허용오차 {coordinateTolerance}m 이내 동일한 지오메트리"
            : $"OBJECTID {objectId}: 완전히 동일한 지오메트리"
    });
    duplicateCount++;
}
```

#### ✅ 수정 후 코드
```csharp
for (int i = 1; i < group.Count; i++)
{
    var (objectId, geometry) = group[i];

    // ✅ Envelope 중심점 추출
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
    duplicateCount++;
}
```

---

### 7️⃣ 슬리버 폴리곤 (Sliver)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 410-425번 라인 (IsSliverPolygon 호출 부분)

#### ❌ 수정 전 코드
```csharp
if (config.ShouldCheckSlivers && GeometryRepresentsPolygon(workingGeometry))
{
    if (IsSliverPolygon(workingGeometry, out sliverMessage))
    {
        errors.Add(new ValidationError
        {
            ErrorCode = "GEOM_SLIVER",
            Message = sliverMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning
        });
    }
}
```

#### ✅ 수정 후 코드
```csharp
if (config.ShouldCheckSlivers && GeometryRepresentsPolygon(workingGeometry))
{
    if (IsSliverPolygon(workingGeometry, out sliverMessage))
    {
        // ✅ 슬리버 폴리곤의 외부 링 중점 추출
        double centerX = 0, centerY = 0;
        if (workingGeometry.GetGeometryCount() > 0)
        {
            var exteriorRing = workingGeometry.GetGeometryRef(0);
            if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
            {
                int pointCount = exteriorRing.GetPointCount();
                int midIndex = pointCount / 2;
                centerX = exteriorRing.GetX(midIndex);
                centerY = exteriorRing.GetY(midIndex);
            }
        }

        // Fallback: Envelope 중심
        if (centerX == 0 && centerY == 0)
        {
            var envelope = new OSGeo.OGR.Envelope();
            workingGeometry.GetEnvelope(envelope);
            centerX = (envelope.MinX + envelope.MaxX) / 2.0;
            centerY = (envelope.MinY + envelope.MaxY) / 2.0;
        }

        string wkt;
        workingGeometry.ExportToWkt(out wkt);

        errors.Add(new ValidationError
        {
            ErrorCode = "GEOM_SLIVER",
            Message = sliverMessage,
            TableName = config.TableId,
            FeatureId = fid.ToString(),
            Severity = Models.Enums.ErrorSeverity.Warning,
            Metadata =
            {
                ["X"] = centerX.ToString(),  // ✅ 외부 링 중점 X
                ["Y"] = centerY.ToString(),  // ✅ 외부 링 중점 Y
                ["GeometryWkt"] = wkt
            }
        });
    }
}
```

---

## 🎯 Phase 4: LOW Priority - 짧은 선, 작은 면적, 최소 정점 (3가지)

### 8️⃣ 짧은 선 객체 (Short Line)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 304-318번 라인

#### ❌ 수정 전 코드
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
    });
}
```

#### ✅ 수정 후 코드
```csharp
if (length < _criteria.MinLineLength && length > 0)
{
    // ✅ 선의 중점 계산
    int pointCount = workingGeometry.GetPointCount();
    double midX = 0, midY = 0;

    if (pointCount > 0)
    {
        int midIndex = pointCount / 2;
        midX = workingGeometry.GetX(midIndex);
        midY = workingGeometry.GetY(midIndex);
    }

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
            ["X"] = midX.ToString(),  // ✅ 선 중점 X
            ["Y"] = midY.ToString(),  // ✅ 선 중점 Y
            ["GeometryWkt"] = wkt
        }
    });
}
```

---

### 9️⃣ 작은 면적 (Small Area)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 320-334번 라인

#### ❌ 수정 전 코드
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
    });
}
```

#### ✅ 수정 후 코드
```csharp
if (area > 0 && area < _criteria.MinPolygonArea)
{
    // ✅ Envelope 중심점 계산
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

---

### 🔟 최소 정점 부족 (Minimum Vertex)

#### 📁 수정 파일
`/SpatialCheckPro/Processors/GeometryCheckProcessor.cs`

#### 🔍 수정 위치: 336-358번 라인

#### ❌ 수정 전 코드
```csharp
if (!minVertexCheck.IsValid)
{
    var detail = string.IsNullOrWhiteSpace(minVertexCheck.Detail)
        ? string.Empty
        : $" ({minVertexCheck.Detail})";

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
    });
}
```

#### ✅ 수정 후 코드
```csharp
if (!minVertexCheck.IsValid)
{
    var detail = string.IsNullOrWhiteSpace(minVertexCheck.Detail)
        ? string.Empty
        : $" ({minVertexCheck.Detail})";

    // ✅ 첫 번째 정점 추출
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
            ["X"] = x.ToString(),  // ✅ 첫 정점 또는 중심점 X
            ["Y"] = y.ToString(),  // ✅ 첫 정점 또는 중심점 Y
            ["GeometryWkt"] = wkt
        }
    });
}
```

---

## 🎯 Phase 5: ValidationError Metadata → GeometryErrorDetail 변환

### 중요: QcErrorService 수정 필요

ValidationError의 Metadata에 저장한 X, Y, GeometryWkt를 GeometryErrorDetail로 변환할 때 읽어와야 합니다.

#### 📁 수정 파일
`/SpatialCheckPro/Services/QcErrorService.cs`

ValidationError를 GeometryErrorDetail로 변환하는 메서드를 찾아서 다음과 같이 수정:

```csharp
private GeometryErrorDetail ConvertValidationErrorToGeometryErrorDetail(ValidationError error)
{
    var detail = new GeometryErrorDetail
    {
        ObjectId = error.FeatureId,
        ErrorType = error.ErrorCode,
        ErrorValue = error.Message,
        DetailMessage = error.Message
    };

    // ✅ Metadata에서 좌표 추출
    if (error.Metadata != null)
    {
        if (error.Metadata.TryGetValue("X", out var xValue) &&
            double.TryParse(xValue, out var x))
        {
            detail.X = x;
        }

        if (error.Metadata.TryGetValue("Y", out var yValue) &&
            double.TryParse(yValue, out var y))
        {
            detail.Y = y;
        }

        if (error.Metadata.TryGetValue("GeometryWkt", out var wkt))
        {
            detail.GeometryWkt = wkt;
        }
    }

    return detail;
}
```

---

## ✅ 검증 체크리스트

수정 완료 후 다음을 확인하세요:

### Phase 1: 유틸리티 클래스
- [ ] `GeometryCoordinateExtractor.cs` 파일 생성 확인
- [ ] 빌드 오류 없이 컴파일되는지 확인

### Phase 2: High Priority (7가지)
- [ ] 스파이크: `CheckSpikeInSingleGeometry` 시그니처 변경 확인
- [ ] 스파이크: `HasSpike` 시그니처 변경 확인
- [ ] 스파이크: 호출 부분에서 spikeX, spikeY 사용 확인
- [ ] 자체 꼬임: NTS ValidationError 사용 확인
- [ ] 자기 중첩: CheckSelfOverlapAsync에 X, Y 설정 확인
- [ ] 언더슛/오버슛: X, Y 및 GeometryWkt 설정 확인
- [ ] 겹침: OverlapInfo에 IntersectionGeometry 추가 확인
- [ ] 겹침: FindOverlaps에서 교차 지오메트리 계산 확인

### Phase 3: Medium Priority (2가지)
- [ ] 중복: X, Y 및 GeometryWkt 설정 확인
- [ ] 슬리버: 외부 링 중점 또는 Envelope 중심 설정 확인

### Phase 4: Low Priority (3가지)
- [ ] 짧은 선: 선 중점 Metadata 저장 확인
- [ ] 작은 면적: Envelope 중심 Metadata 저장 확인
- [ ] 최소 정점: 첫 정점 Metadata 저장 확인

### Phase 5: 변환 로직
- [ ] QcErrorService에서 Metadata 읽기 로직 추가 확인

### 통합 테스트
- [ ] 모든 오류 타입이 QC_Errors_Point에 저장되는지 확인
- [ ] X, Y 좌표가 0이 아닌 실제 좌표인지 확인
- [ ] GeometryWkt가 올바르게 저장되는지 확인

---

## 🚨 주의사항

1. **NetTopologySuite using 추가**: 여러 파일에서 NTS를 사용하므로 using 문 누락 방지
2. **Null 체크**: 모든 지오메트리 접근 시 null 체크 필수
3. **WKT Export**: `ExportToWkt(out string wkt)` 호출 후 반드시 wkt 변수 사용
4. **Metadata 딕셔너리**: ValidationError.Metadata가 이미 존재하는지 확인 후 사용

---

## 📊 예상 결과

수정 완료 후:
- **13가지 오류 타입** 중 **12가지**가 정확한 X, Y 좌표와 함께 저장
- **Stage 3 지오메트리 검수** 결과가 모두 QC_Errors_Point에 저장
- **사용자가 ArcGIS/QGIS에서 오류 위치를 정확히 확인 가능**

---

## 🔗 참고 문서

- 종합 분석 문서: `ANALYSIS_전체_지오메트리_오류_위치정확도_종합개선방안.md`
- Stage 4, 5 수정 지시서: `CURSOR_수정지시서_관계검수_지오메트리추출.md`
