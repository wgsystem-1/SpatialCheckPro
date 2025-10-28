# QcErrorIntegrationService.cs 수정 권장사항

## 문제 요약

이전 세션에서 작성한 코드가 ValidationError 모델의 실제 속성명과 불일치하여 빌드 오류 발생.

## 필요한 수정

### 1. RelatedTable → TargetTable (line 116, 144)

**오류 코드:**
```csharp
TargetLayer = error.RelatedTable ?? string.Empty,           // ❌
RelatedTableName = error.RelatedTable ?? string.Empty,      // ❌
```

**올바른 코드:**
```csharp
TargetLayer = error.TargetTable ?? string.Empty,            // ✅
RelatedTableName = error.TargetTable ?? string.Empty,       // ✅
```

**영향:** ✅ 기능 유지 (TargetTable이 관련 테이블 정보를 올바르게 전달)

---

### 2. RelatedObjectId → TargetObjectId (line 117, 145)

**오류 코드:**
```csharp
TargetObjectId = error.RelatedObjectId,                     // ❌
RelatedObjectId = error.RelatedObjectId,                    // ❌
```

**올바른 코드:**
```csharp
TargetObjectId = error.TargetObjectId,                      // ✅
RelatedObjectId = error.TargetObjectId,                     // ✅
```

**영향:** ✅ 기능 유지 (TargetObjectId가 관련 객체 ID를 올바르게 전달)

---

### 3. SpatialRelationType.Custom → 적절한 기본값 (line 123)

**오류 코드:**
```csharp
RelationType = Models.Enums.SpatialRelationType.Custom      // ❌ Custom 없음
```

**권장 수정 방안:**

#### 옵션 A: ErrorCode 기반 동적 매핑 (권장)
```csharp
RelationType = MapErrorCodeToRelationType(error.ErrorCode ?? "REL_UNKNOWN")

// 헬퍼 메서드 추가
private SpatialRelationType MapErrorCodeToRelationType(string errorCode)
{
    // ErrorCode에 따라 적절한 RelationType 반환
    if (errorCode.Contains("CONTAIN", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Contains;
    if (errorCode.Contains("WITHIN", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Within;
    if (errorCode.Contains("INTERSECT", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Intersects;
    if (errorCode.Contains("OVERLAP", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Overlaps;
    if (errorCode.Contains("TOUCH", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Touches;
    if (errorCode.Contains("CROSS", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Crosses;
    if (errorCode.Contains("DISJOINT", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Disjoint;

    // 기본값
    return SpatialRelationType.Intersects;
}
```

#### 옵션 B: 기본값 사용 (간단하지만 정보 손실)
```csharp
RelationType = SpatialRelationType.Intersects               // ✅ 기본값
```

**영향:**
- ⚠️ 옵션 A: 기능 유지 + ErrorCode에서 관계 타입 추론
- ⚠️ 옵션 B: 기능 축소 (모든 관계를 Intersects로 표시, 정보 손실)

**권장:** 옵션 A (ErrorCode 기반 매핑)

---

### 4. Description → Message 또는 null (line 142)

**오류 코드:**
```csharp
Details = error.Description ?? string.Empty,                // ❌ Description 없음
```

**권장 수정 방안:**

#### 옵션 A: Message 사용 (권장)
```csharp
Details = error.Message ?? string.Empty,                    // ✅
```

#### 옵션 B: Metadata에서 추출
```csharp
Details = error.Metadata.TryGetValue("Description", out var desc)
    ? desc?.ToString() ?? string.Empty
    : string.Empty,                                         // ✅
```

#### 옵션 C: Details 딕셔너리를 JSON으로 변환
```csharp
Details = error.Details != null
    ? string.Join("; ", error.Details.Select(kvp => $"{kvp.Key}={kvp.Value}"))
    : string.Empty,                                         // ✅
```

**영향:**
- ✅ 옵션 A: Message 내용을 Details에 저장 (가장 간단)
- ✅ 옵션 B: Metadata에 Description이 있다면 사용
- ✅ 옵션 C: Details 딕셔너리 전체를 문자열로 변환

**권장:** 옵션 A (Message 사용)

---

## ✅ 전체 수정 버전

```csharp
// === Stage 4: 공간 관계 오류 변환 (REL) ===
if (hasStage4)
{
    foreach (var error in validationResult.RelationCheckResult!.Errors)
    {
        var spatialError = new SpatialRelationError
        {
            SourceObjectId = error.SourceObjectId ?? 0,
            SourceLayer = error.SourceTable ?? string.Empty,
            TargetLayer = error.TargetTable ?? string.Empty,              // ✅ 수정
            TargetObjectId = error.TargetObjectId,                        // ✅ 수정
            ErrorType = error.ErrorCode ?? "REL_UNKNOWN",
            Severity = error.Severity,
            Message = error.Message ?? string.Empty,
            GeometryWKT = error.GeometryWKT ?? string.Empty,
            DetectedAt = DateTime.UtcNow,
            RelationType = MapErrorCodeToRelationType(error.ErrorCode ?? "REL_UNKNOWN")  // ✅ 수정
        };

        rel.SpatialErrors.Add(spatialError);
    }
}

// === Stage 5: 속성 관계 오류 변환 (ATTR_REL) ===
if (hasStage5)
{
    foreach (var error in validationResult.AttributeRelationCheckResult!.Errors)
    {
        var attrError = new AttributeRelationError
        {
            ObjectId = error.SourceObjectId ?? 0,
            TableName = error.SourceTable ?? string.Empty,
            FieldName = error.FieldName ?? string.Empty,
            RuleName = error.ErrorCode ?? "ATTR_UNKNOWN",
            Message = error.Message ?? string.Empty,
            Details = error.Message ?? string.Empty,                      // ✅ 수정 (Description → Message)
            Severity = error.Severity,
            RelatedTableName = error.TargetTable ?? string.Empty,         // ✅ 수정
            RelatedObjectId = error.TargetObjectId,                       // ✅ 수정
            DetectedAt = DateTime.UtcNow
        };

        rel.AttributeErrors.Add(attrError);
    }
}

// 헬퍼 메서드 추가 (클래스 내부)
private SpatialRelationType MapErrorCodeToRelationType(string errorCode)
{
    if (errorCode.Contains("CONTAIN", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Contains;
    if (errorCode.Contains("WITHIN", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Within;
    if (errorCode.Contains("INTERSECT", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Intersects;
    if (errorCode.Contains("OVERLAP", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Overlaps;
    if (errorCode.Contains("TOUCH", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Touches;
    if (errorCode.Contains("CROSS", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Crosses;
    if (errorCode.Contains("DISJOINT", StringComparison.OrdinalIgnoreCase))
        return SpatialRelationType.Disjoint;

    return SpatialRelationType.Intersects; // 기본값
}
```

---

## 📊 의도한 기능에 미치는 영향

### ✅ 기능 유지되는 항목
1. **TargetTable/TargetObjectId**: 관련 테이블/객체 정보가 올바르게 저장됨
2. **Message**: 오류 메시지 정상 저장

### ⚠️ 주의 필요 항목
1. **RelationType**:
   - 단순 기본값 사용 시 → 모든 관계가 동일 타입으로 표시 (정보 손실)
   - ErrorCode 매핑 사용 시 → 관계 타입 정보 유지

2. **Details**:
   - Message 사용 시 → 중복 정보 (Message와 Details 동일)
   - Metadata 추출 시 → 추가 정보 보존

### ❌ 기능 손실 위험
- RelationType을 잘못 설정하면 **관계 타입 분석이 불가능**

---

## 🎯 최종 권장사항

**커서가 다음과 같이 수정했다면 ✅ OK:**
1. RelatedTable → TargetTable
2. RelatedObjectId → TargetObjectId
3. Description → Message
4. SpatialRelationType.Custom → SpatialRelationType.Intersects (또는 다른 적절한 값)

**커서가 다르게 수정했다면 ⚠️ 확인 필요:**
- 위 권장 수정 버전대로 재수정 권장
- 특히 RelationType 매핑 로직 추가 권장

---

## 🔧 재수정이 필요한 경우

로컬에서 `QcErrorIntegrationService.cs`의 line 110-150 부분을 위 "전체 수정 버전"으로 교체하고 재빌드하세요.
