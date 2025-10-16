OGC Simple Features 표준 문서의 지오메트리 유효성 규칙을 영어 원문과 한글로 작성하겠습니다.

## Geometry Validation Rules (지오메트리 유효성 규칙)

### 1. Polygon Validity Constraints (면 유효성 제약조건)

| Error Type (오류 유형)                  | Description (설명)                                           | Constraint (제약조건)                                        | Validation Method (검증 방법)              |
| --------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------ |
| **Non-closed (비폐합)**                 | Polygon is not topologically closed (면이 위상적으로 닫혀있지 않음) | Polygons are topologically closed (면은 위상적으로 폐합)     | startPoint() = endPoint()                  |
| **Crossing Rings (교차 링)**            | Exterior and interior rings cross (외부링과 내부링이 교차함) | No two rings cross; may touch at tangent point only (두 링이 교차하지 않음; 접선 접촉만 허용) | ∀ P ∈ Polygon, ∀ c1,c2∈P.Boundary(), c1≠c2 |
| **Cut Lines (단절선)**                  | Line cuts through polygon interior (면 내부를 가르는 선 존재) | Polygon may not have cut lines (면은 단절선을 가질 수 없음)  | ∀ P ∈ Polygon, P = P.Interior.Closure      |
| **Spikes (돌기)**                       | Sharp protrusions from polygon (면에서 뾰족하게 튀어나온 부분) | Polygon may not have spikes (면은 돌기를 가질 수 없음)       | P = P.Interior.Closure                     |
| **Punctures (관통점)**                  | Penetrating holes (관통된 구멍)                              | Polygon may not have punctures (면은 관통점을 가질 수 없음)  | P = P.Interior.Closure                     |
| **Disconnected Interior (비연결 내부)** | Interior is not connected (내부가 연결되지 않음)             | Interior of every Polygon is a connected point set (모든 면의 내부는 연결된 점집합) | Connectivity verification (연결성 검증)    |

### 2. Curve/LineString Validity Constraints (곡선/선스트링 유효성 제약조건)

| Error Type (오류 유형)                        | Description (설명)                                           | Constraint (제약조건)                                        | Validation Formula (검증 공식)                           |
| --------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ | -------------------------------------------------------- |
| **Self-intersection (자기교차)**              | Passes through same point twice except endpoints (시작점과 끝점을 제외하고 같은 점을 두 번 통과) | Curve is simple if it does not pass through the same Point twice (곡선은 같은 점을 두 번 통과하지 않으면 단순) | ∀ x1, x2 ∈ [a, b]: [f(x1)=f(x2) ∧ x1<x2] ⇒ [x1=a ∧ x2=b] |
| **Insufficient Points (최소점 미달)**         | LineString has too few points (선스트링의 점 개수 부족)      | numPoints ≥ 2 (점개수 ≥ 2)                                   | Point count validation (점개수 검증)                     |
| **LinearRing Not Closed (선형링 비폐합)**     | LinearRing is not closed (선형링이 닫혀있지 않음)            | startPoint() = endPoint() (시작점 = 종료점)                  | isClosed() = TRUE (폐합여부 = 참)                        |
| **LinearRing Minimum Points (선형링 최소점)** | LinearRing has insufficient points (선형링의 점 개수 부족)   | numPoints ≥ 4 (점개수 ≥ 4)                                   | Point count ≥ 4 (점개수 ≥ 4)                             |

### 3. MultiPolygon Validity Constraints (다중면 유효성 제약조건)

| Error Type (오류 유형)                              | Description (설명)                                        | Constraint (제약조건)                                        | Validation Formula (검증 공식)                           |
| --------------------------------------------------- | --------------------------------------------------------- | ------------------------------------------------------------ | -------------------------------------------------------- |
| **Overlap (중첩)**                                  | Interiors of two Polygons intersect (두 면의 내부가 교차) | Interiors of 2 Polygons must be disjoint (두 면의 내부는 서로소여야 함) | ∀M∈MultiPolygon, ∀Pi,Pj∈M: Interior(Pi)∩Interior(Pj) = ∅ |
| **Boundary Crossing (경계 교차)**                   | Boundaries of two Polygons cross (두 면의 경계가 교차)    | Boundaries may touch at only finite number of Points (경계는 유한개 점에서만 접촉) | ∀ci,cj∈Curve: ci∩cj = {p1,…,pk \| pm∈Point}              |
| **Cut Lines/Spikes/Punctures (단절선/돌기/관통점)** | Occur across MultiPolygon (다중면 전체에서 발생)          | MultiPolygon is a regular closed Point set (다중면은 정규 폐집합) | ∀ M ∈ MultiPolygon, M = Closure(Interior(M))             |

### 4. Point Validity Constraints (점 유효성 제약조건)

| Error Type (오류 유형)                         | Description (설명)                                           | Constraint (제약조건)                              | Note (비고)                       |
| ---------------------------------------------- | ------------------------------------------------------------ | -------------------------------------------------- | --------------------------------- |
| **Missing Coordinates (좌표 누락)**            | X or Y coordinate values missing (X 또는 Y 좌표값 누락)      | X, Y must exist (X, Y 필수 존재)                   | Required (필수)                   |
| **Coordinate Out of Range (좌표 범위 초과)**   | Coordinates exceed CRS valid range (좌표가 좌표참조체계 유효 범위 벗어남) | Within CRS valid range (좌표참조체계 유효 범위 내) | CRS dependent (좌표참조체계 의존) |
| **Z Coordinate Inconsistency (Z 좌표 불일치)** | Mixed presence of Z coordinates (Z 좌표 혼재)                | Consistent dimension (일관된 차원)                 | is3D() check (3차원여부 확인)     |

### 5. Common Geometry Properties (공통 지오메트리 속성)

| Property (속성)           | Valid Values (유효값) | Validation Method (검증 메소드) | Description (설명)                                |
| ------------------------- | --------------------- | ------------------------------- | ------------------------------------------------- |
| **Dimension (차원)**      | 0, 1, 2               | dimension()                     | Point=0, Curve=1, Surface=2 (점=0, 곡선=1, 면=2)  |
| **isEmpty (공집합여부)**  | TRUE/FALSE (참/거짓)  | isEmpty()                       | 1 = Empty geometry (∅) (1 = 공집합)               |
| **isSimple (단순여부)**   | TRUE/FALSE (참/거짓)  | isSimple()                      | 1 = No self-intersection (1 = 자기교차 없음)      |
| **SRID (공간참조식별자)** | Integer (정수)        | SRID()                          | Spatial Reference System ID (공간참조체계 식별자) |

### 6. Spatial Relationship Error Detection (공간 관계 오류 감지) - DE-9IM

| Relationship Operator (관계 연산자) | Return Value (반환값) | Meaning (의미)                            | Error Detection Use (오류 감지 활용)                |
| ----------------------------------- | --------------------- | ----------------------------------------- | --------------------------------------------------- |
| **Equals (동일)**                   | 1/0                   | Spatially equal (공간적으로 동일)         | Duplicate geometry detection (중복 지오메트리 검출) |
| **Disjoint (분리)**                 | 1/0                   | Do not intersect (교차하지 않음)          | Connectivity validation (연결성 검증)               |
| **Intersects (교차)**               | 1/0                   | Spatially intersect (공간적으로 교차)     | Intersection detection (교차 검출)                  |
| **Touches (접촉)**                  | 1/0                   | Boundaries touch only (경계만 접촉)       | Topological consistency (위상 일관성 검증)          |
| **Crosses (횡단)**                  | 1/0                   | Spatially cross (공간적으로 횡단)         | Crossing detection (횡단 검출)                      |
| **Within (포함됨)**                 | 1/0                   | Spatially within (공간적으로 내부에 포함) | Containment validation (포함관계 검증)              |
| **Contains (포함)**                 | 1/0                   | Spatially contains (공간적으로 포함)      | Containment validation (포함관계 검증)              |
| **Overlaps (중첩)**                 | 1/0                   | Spatially overlap (공간적으로 중첩)       | Overlap error detection (중첩 오류 검출)            |

### 7. PolyhedralSurface/TIN Constraints (다면체표면/삼각망 제약조건)

| Error Type (오류 유형)                                       | Description (설명)                                           | Constraint (제약조건)                                        | Note (비고)                                           |
| ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ | ----------------------------------------------------- |
| **Inconsistent Orientation (일관되지 않은 방향성)**          | Patches not consistently oriented (패치들의 방향이 일관되지 않음) | Consistent orientation required (일관된 방향성 필요)         | See Figure 14 (그림 14 참조)                          |
| **Not Closed Polyhedron (비폐합 다면체)**                    | Boundary not closed (경계가 닫혀있지 않음)                   | IsClosed() = TRUE (폐합여부 = 참)                            | Required for solid representation (입체 표현 시 필수) |
| **Common Boundary Mismatch (공통 경계 미일치)**              | Adjacent polygons do not share boundary (인접 면이 경계를 공유하지 않음) | Common boundary as LineStrings (선스트링으로 표현된 공통 경계) | BoundingPolygons() validation (경계면들 검증)         |
| **More Than 2 Polygons Share Edge (2개 이상 면이 모서리 공유)** | One edge shared by 3+ polygons (하나의 모서리를 3개 이상 면이 공유) | Each LineString in boundary of at most 2 Polygon patches (각 선스트링은 최대 2개 면의 경계) | Topological consistency (위상 일관성)                 |

### 8. GeometryCollection Constraints (지오메트리컬렉션 제약조건)

| Error Type (오류 유형)                     | Description (설명)                                           | Constraint (제약조건)                                        | Note (비고)                                              |
| ------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ | -------------------------------------------------------- |
| **Inconsistent CRS (좌표참조체계 불일치)** | Elements have different CRS (요소들의 좌표참조체계가 다름)   | All elements in same Spatial Reference System (모든 요소가 동일한 공간참조체계에 존재) | Same CRS required (동일 좌표참조체계 필수)               |
| **Empty Elements (공집합 요소)**           | Contains meaningless empty elements (의미없는 공집합 요소 포함) | Valid geometry elements (유효한 지오메트리 요소)             | numGeometries() validation (요소개수 검증)               |
| **Mixed Dimensions (혼합 차원)**           | Geometries of different dimensions mixed (서로 다른 차원의 지오메트리 혼재) | Allowed by specification (명세상 허용됨)                     | May be restricted by application (응용에 따라 제한 가능) |

### 9. Well-Known Text/Binary Representation Errors (텍스트/이진 표현 오류)

| Error Type (오류 유형)                                    | Description (설명)                                  | Constraint (제약조건)                                        | Format (형식)                             |
| --------------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| **Coordinate Precision Loss (좌표 정밀도 손실)**          | Exceeds double precision range (배정밀도 범위 초과) | IEEE 754 double precision format (IEEE 754 배정밀도 형식)    | 64-bit floating point (64비트 부동소수점) |
| **Byte Order Mismatch (바이트 순서 불일치)**              | Endianness inconsistency (엔디안 불일치)            | wkbXDR=0 (Big Endian), wkbNDR=1 (Little Endian)              | Explicit byte order (명시적 바이트 순서)  |
| **Geometry Type Mismatch (지오메트리 타입 불일치)**       | Invalid type code (타입 코드 오류)                  | Valid type codes (1-16, 1001-1016, 2001-2016, 3001-3016) (유효한 타입코드) | See Table 7 (표 7 참조)                   |
| **Coordinate Dimension Inconsistency (좌표 차원 불일치)** | 2D/3D/M/ZM mixed (2차원/3차원/측정값/혼합 혼재)     | Consistent coordinate dimension (일관된 좌표 차원)           | Z, M, ZM distinction (Z, M, ZM 구분)      |

### 10. Coordinate Constraints by Dimension (차원별 좌표 제약조건)

| Coordinate Type (좌표 유형) | Required Coordinates (필수 좌표) | Optional Coordinates (선택 좌표) | Usage (용도)                               |
| --------------------------- | -------------------------------- | -------------------------------- | ------------------------------------------ |
| **2D**                      | x, y                             | none (없음)                      | Planar map (평면 지도)                     |
| **3D Z**                    | x, y, z                          | none (없음)                      | Altitude information (고도 정보)           |
| **2D M**                    | x, y, m                          | none (없음)                      | Linear referencing system (선형 참조 체계) |
| **3D ZM**                   | x, y, z, m                       | none (없음)                      | Altitude and measure (고도와 측정값)       |

### 11. Boundary Definition Rules (경계 정의 규칙)

| Geometry Type (지오메트리 타입)    | Boundary Definition (경계 정의)                              | Rule (규칙)                                                 |
| ---------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------- |
| **Point (점)**                     | Empty set (공집합)                                           | No boundary (경계 없음)                                     |
| **MultiPoint (다중점)**            | Empty set (공집합)                                           | No boundary (경계 없음)                                     |
| **Non-closed Curve (비폐합 곡선)** | Start and end Points (시작점과 종료점)                       | Two endpoints (양 끝점)                                     |
| **Closed Curve (폐합 곡선)**       | Empty set (공집합)                                           | startPoint=endPoint, no boundary (시작점=종료점, 경계 없음) |
| **MultiCurve (다중곡선)**          | Points in boundaries of odd number of Curves ("mod 2" union rule) (홀수개 곡선의 경계에 속한 점, "모듈로 2" 합집합 규칙) | Mod 2 union (모듈로 2 합집합)                               |
| **Polygon (면)**                   | Set of exterior and interior Rings (외부링과 내부링 집합)    | All rings (모든 링)                                         |
| **MultiPolygon (다중면)**          | Union of boundaries of element Polygons (요소 면들의 경계 합집합) | Shared boundaries counted once (공유 경계는 한 번만)        |

### 12. Simplicity Determination Rules (단순성 판정 규칙)

| Geometry Type (지오메트리 타입) | Simplicity Condition (단순성 조건)                           | Description (설명)                                           |
| ------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **Point (점)**                  | Always simple (항상 단순)                                    | Cannot self-intersect (자기교차 불가능)                      |
| **MultiPoint (다중점)**         | No duplicate Points (중복 점 없음)                           | All points have unique coordinate values (모든 점이 고유한 좌표값) |
| **Curve (곡선)**                | No self-intersection (자기교차 없음)                         | Does not pass through same Point twice except endpoints (끝점 제외하고 같은 점 통과 안 함) |
| **Ring (링)**                   | Simple and closed (단순하고 폐합)                            | No self-intersection and startPoint=endPoint (자기교차 없고 시작점=종료점) |
| **MultiCurve (다중곡선)**       | Each Curve is simple, intersections only at endpoints (각 곡선이 단순, 교차점이 끝점에만 존재) | No interior intersections between Curves (곡선 간 내부 교차 없음) |
| **Polygon (면)**                | Always simple (항상 단순)                                    | Simple by definition (정의상 단순)                           |

### 13. Spatial Operation Result Constraints (공간 연산 결과 제약)

| Operation (연산)           | Input Constraint (입력 제약)   | Output Guarantee (출력 보장)                            | Note (비고)                                  |
| -------------------------- | ------------------------------ | ------------------------------------------------------- | -------------------------------------------- |
| **Union (합집합)**         | Same CRS (동일 좌표참조체계)   | Valid geometry (유효한 지오메트리)                      | Point set union (점집합 합)                  |
| **Intersection (교집합)**  | Same CRS (동일 좌표참조체계)   | Valid geometry or empty (유효한 지오메트리 또는 공집합) | Point set intersection (점집합 교)           |
| **Difference (차집합)**    | Same CRS (동일 좌표참조체계)   | Valid geometry or empty (유효한 지오메트리 또는 공집합) | Point set difference (점집합 차)             |
| **SymDifference (대칭차)** | Same CRS (동일 좌표참조체계)   | Valid geometry or empty (유효한 지오메트리 또는 공집합) | Exclusive union (배타적 합집합)              |
| **Buffer (버퍼)**          | Distance > 0 (거리 > 0)        | Valid Surface geometry (유효한 면 지오메트리)           | All Points within distance (거리 내 모든 점) |
| **ConvexHull (볼록껍질)**  | Any geometry (임의 지오메트리) | Convex Surface (볼록 면)                                | Minimum convex polygon (최소 볼록 다각형)    |

### 14. Assertions for Geometric Objects (지오메트리 객체 단언)

| Assertion (단언)                       | Description (설명)                                           | Mathematical Expression (수학적 표현)                      |
| -------------------------------------- | ------------------------------------------------------------ | ---------------------------------------------------------- |
| **Topologically Closed (위상적 폐합)** | All geometries include their boundary (모든 지오메트리는 경계 포함) | Geometry = Interior ∪ Boundary (지오메트리 = 내부 ∪ 경계)  |
| **Regular Closed (정규 폐합)**         | Geometry equals closure of its interior (지오메트리는 내부의 폐포와 같음) | Geometry = Closure(Interior) (지오메트리 = 폐포(내부))     |
| **Connected Interior (연결된 내부)**   | Interior is a connected set (내부는 연결된 집합)             | Interior(Polygon) is connected (내부(면)는 연결됨)         |
| **Disjoint Interiors (서로소 내부)**   | Interiors do not intersect (내부가 교차하지 않음)            | Interior(A) ∩ Interior(B) = ∅ (내부(A) ∩ 내부(B) = 공집합) |

이 표들은 OGC SFA 표준 (Simple Features Access, Version 1.2.1, 2011) 문서 Section 6에 정의된 지오메트리 유효성 규칙을 원문과 한글로 정리한 것입니다.