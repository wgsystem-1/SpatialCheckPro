# ISO 19157 Geometry Validity Rules with Specific Values (구체적 값을 포함한 지오메트리 유효성 규칙)

## 1. Topological Consistency Measures (위상 일관성 측정항목)

| Measure ID | Rule Name (규칙명)                                           | Value Type (값 타입) | Valid Range (유효 범위)                                  | Parameter Values (매개변수 값)                               | Error Condition (오류 조건)                                  |
| ---------- | ------------------------------------------------------------ | -------------------- | -------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| 21         | Number of faulty point-curve connections<br>(잘못된 점-곡선 연결 개수) | Integer<br>(정수)    | ≥ 0                                                      | None<br>(없음)                                               | Count > 0 indicates faulty connections<br>(개수 > 0은 잘못된 연결을 나타냄) |
| 23         | Number of missing connections due to undershoots<br>(언더슛 누락 연결 개수) | Integer<br>(정수)    | ≥ 0                                                      | **Search distance**: 3m (example)<br>(탐색 거리: 3m (예시))<br>Application dependent<br>(응용프로그램에 따라 다름) | Line ends within search distance but not connected<br>(탐색 거리 내 선 끝점이 있으나 연결되지 않음) |
| 24         | Number of missing connections due to overshoots<br>(오버슛 누락 연결 개수) | Integer<br>(정수)    | ≥ 0                                                      | **Search tolerance**: 3m (example)<br>(탐색 허용오차: 3m (예시))<br>Minimum allowable length<br>(최소 허용 길이) | Line extends beyond connection point within tolerance<br>(선이 허용오차 내에서 연결점을 넘어 연장됨) |
| 25         | Number of invalid slivers<br>(유효하지 않은 슬리버 개수)     | Integer<br>(정수)    | ≥ 0                                                      | **Parameter 1 - Max area**:<br>(매개변수 1 - 최대 면적)<br>Application dependent<br>(응용프로그램에 따라 다름)<br><br>**Parameter 2 - Thickness quotient**:<br>(매개변수 2 - 두께 계수)<br>T = 4π × [area] / [perimeter]²<br>**Range**: 0 ≤ T ≤ 1<br>(범위: 0 ≤ T ≤ 1)<br>• T = 1: Perfect circle<br>(완전한 원)<br>• T = 0: Line<br>(선)<br>• T → 0: Thinner sliver<br>(더 얇은 슬리버) | Area < Max area AND<br>T < threshold (closer to 0)<br>(면적 < 최대 면적 그리고<br>T < 임계값 (0에 가까움)) |
| 26         | Number of invalid self-intersect errors<br>(유효하지 않은 자기교차 오류 개수) | Integer<br>(정수)    | ≥ 0                                                      | None<br>(없음)                                               | Geometry intersects itself (creates loops)<br>(기하가 자기 자신과 교차 (루프 생성)) |
| 27         | Number of invalid self-overlap errors<br>(유효하지 않은 자기중첩 오류 개수) | Integer<br>(정수)    | ≥ 0                                                      | None<br>(없음)                                               | Geometry overlaps itself (vertices trace back)<br>(기하가 자기중첩 (정점이 역추적)) |
| 11         | Number of invalid overlaps of surfaces<br>(표면의 유효하지 않은 중첩 개수) | Integer<br>(정수)    | ≥ 0                                                      | Feature class types that cannot overlap<br>(중첩될 수 없는 피처 클래스 타입) | Surfaces overlap when prohibited by schema<br>(스키마에서 금지된 표면 중첩) |
| 22         | Rate of faulty point-curve connections<br>(잘못된 점-곡선 연결 비율) | Real<br>(실수)       | 0.0 to 1.0<br>or 0% to 100%<br>(0.0~1.0 또는<br>0%~100%) | None<br>(없음)                                               | Rate = (# faulty connections) / (# total supposed connections)<br>(비율 = (잘못된 연결 수) / (전체 예상 연결 수)) |

## 2. Completeness Measures (완전성 측정항목)

| Measure ID | Rule Name (규칙명)                                           | Value Type (값 타입) | Valid Range (유효 범위)                                  | Calculation Formula (계산 공식)                              | Quality Indicator (품질 지표)                                |
| ---------- | ------------------------------------------------------------ | -------------------- | -------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| 1          | Excess item<br>(과다 항목)                                   | Boolean<br>(불린)    | True / False<br>(참 / 거짓)                              | True = item incorrectly present<br>(참 = 항목이 잘못 존재함) | True indicates commission error<br>(참은 과다 오류를 나타냄) |
| 2          | Number of excess items<br>(과다 항목 개수)                   | Integer<br>(정수)    | ≥ 0                                                      | Count of items that should not be present<br>(존재하지 말아야 할 항목의 개수) | Higher count = lower quality<br>(개수가 높을수록 품질 낮음)  |
| 3          | Rate of excess items<br>(과다 항목 비율)                     | Real<br>(실수)       | 0.0 to ∞<br>(0.0 ~ 무한)                                 | Rate = (# excess items) / (# items that should be present) × 100%<br>(비율 = (과다 항목 수) / (존재해야 할 항목 수) × 100%) | Example: 10% (10% more houses than universe of discourse)<br>(예: 10% (실세계보다 10% 더 많은 주택)) |
| 4          | Number of duplicate feature instances<br>(중복 피처 인스턴스 개수) | Integer<br>(정수)    | ≥ 0                                                      | Count of exact duplications:<br>• Identical attribution<br>• Identical coordinates<br>(정확한 중복 개수:<br>• 동일 속성<br>• 동일 좌표) | Count > 0 indicates duplicates exist<br>(개수 > 0은 중복 존재를 나타냄) |
| 5          | Missing item<br>(누락 항목)                                  | Boolean<br>(불린)    | True / False<br>(참 / 거짓)                              | True = item is missing<br>(참 = 항목이 누락됨)               | True indicates omission error<br>(참은 누락 오류를 나타냄)   |
| 6          | Number of missing items<br>(누락 항목 개수)                  | Integer<br>(정수)    | ≥ 0                                                      | Count of items absent from data set<br>(데이터셋에서 누락된 항목의 개수) | Higher count = lower quality<br>(개수가 높을수록 품질 낮음)  |
| 7          | Rate of missing items<br>(누락 항목 비율)                    | Real<br>(실수)       | 0.0 to 1.0<br>or 0% to 100%<br>(0.0~1.0 또는<br>0%~100%) | Rate = (# missing items) / (# items that should be present) × 100%<br>(비율 = (누락 항목 수) / (존재해야 할 항목 수) × 100%) | Example: 10% (10% less houses than universe of discourse)<br>(예: 10% (실세계보다 10% 적은 주택)) |

## 3. Conceptual Consistency Measures (개념 일관성 측정항목)

| Measure ID | Rule Name (규칙명)                                           | Value Type (값 타입) | Valid Range (유효 범위)                                  | Calculation Formula (계산 공식)                              | Acceptance Criteria (수용 기준)                              |
| ---------- | ------------------------------------------------------------ | -------------------- | -------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| 8          | Conceptual schema non-compliance<br>(개념 스키마 비준수)     | Boolean<br>(불린)    | True / False<br>(참 / 거짓)                              | True = not compliant with conceptual schema rules<br>(참 = 개념 스키마 규칙 비준수) | False is desired (compliant)<br>(거짓이 바람직함 (준수))     |
| 9          | Conceptual schema compliance<br>(개념 스키마 준수)           | Boolean<br>(불린)    | True / False<br>(참 / 거짓)                              | True = complies with conceptual schema rules<br>(참 = 개념 스키마 규칙 준수) | True is desired (compliant)<br>(참이 바람직함 (준수))        |
| 10         | Number of items not compliant<br>(비준수 항목 개수)          | Integer<br>(정수)    | ≥ 0                                                      | Count of non-compliant items:<br>• Invalid placement (within tolerance)<br>• Duplication<br>• Invalid overlap<br>(비준수 항목 개수:<br>• 유효하지 않은 배치 (허용오차 내)<br>• 중복<br>• 유효하지 않은 중첩) | 0 is ideal<br>(0이 이상적)                                   |
| 12         | Non-compliance rate with conceptual schema<br>(개념 스키마 비준수 비율) | Real<br>(실수)       | 0.0 to 1.0<br>or 0% to 100%<br>(0.0~1.0 또는<br>0%~100%) | Rate = (# non-compliant items) / (# total items) × 100%<br>(비율 = (비준수 항목 수) / (전체 항목 수) × 100%) | Example: 2%<br>(예: 2%)                                      |
| 13         | Compliance rate with conceptual schema<br>(개념 스키마 준수 비율) | Real<br>(실수)       | 0.0 to 1.0<br>or 0% to 100%<br>(0.0~1.0 또는<br>0%~100%) | Rate = (# compliant items) / (# total items) × 100%<br>(비율 = (준수 항목 수) / (전체 항목 수) × 100%) | Example: 90%<br>(예: 90%)<br>Higher is better<br>(높을수록 좋음) |

## 4. Practical Implementation Examples (실무 구현 예시)

### Example 1: Search Tolerance Values (탐색 허용오차 값 예시)

| Context (상황)                                     | Typical Values (일반적 값)                             | Application (적용)                                       |
| -------------------------------------------------- | ------------------------------------------------------ | -------------------------------------------------------- |
| Urban high-precision mapping<br>(도시 고정밀 매핑) | 0.1m - 1m                                              | Undershoot/overshoot detection<br>(언더슛/오버슛 탐지)   |
| Rural/general mapping<br>(농촌/일반 매핑)          | 3m - 10m<br>(Document examples:<br>문서 예시: 3m, 10m) | Point-curve connection validation<br>(점-곡선 연결 검증) |
| Large-scale regional<br>(대규모 지역)              | 10m - 50m                                              | Network connectivity<br>(네트워크 연결성)                |

### Example 2: Sliver Detection Parameters (슬리버 탐지 매개변수)

| Parameter (매개변수)              | Formula (공식)                                      | Typical Threshold (일반적 임계값)                            | Interpretation (해석)                                        |
| --------------------------------- | --------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| Thickness Quotient<br>(두께 계수) | T = 4π × Area / Perimeter²<br>T = 4π × 면적 / 둘레² | **T < 0.1**: Likely sliver<br>(슬리버 가능성 높음)<br>**0.1 ≤ T < 0.3**: Investigate<br>(조사 필요)<br>**T ≥ 0.3**: Normal polygon<br>(정상 폴리곤) | Lower values indicate elongated/thin polygons<br>(낮은 값은 길쭉한/얇은 폴리곤을 나타냄) |
| Maximum Area<br>(최대 면적)       | User-defined<br>(사용자 정의)                       | Depends on map scale:<br>(지도 축척에 따라 다름)<br>• 1:1,000 → 1-5 m²<br>• 1:10,000 → 10-50 m²<br>• 1:50,000 → 100-500 m² | Prevents large valid polygons from being flagged<br>(큰 유효 폴리곤이 표시되는 것을 방지) |

### Example 3: Acceptance Quality Limits (AQL) from Document (문서의 수용 품질 한계)

| Feature Type (피처 타입) | AQL Threshold (AQL 임계값)             | Measure (측정항목)                  | Example from Doc (문서 예시)                                 |
| ------------------------ | -------------------------------------- | ----------------------------------- | ------------------------------------------------------------ |
| Path (경로)              | Max 2 missing<br>(최대 2개 누락)       | Omission<br>(누락)                  | "Max two items can be missing for each feature type"<br>("각 피처 타입당 최대 2개 항목 누락 가능") |
| Road (도로)              | Max 2 excess<br>(최대 2개 과다)        | Commission<br>(과다)                | "Max two items can be in excess for each feature type"<br>("각 피처 타입당 최대 2개 항목 과다 가능") |
| Tree (나무)              | Max 10% missing<br>(최대 10% 누락)     | Omission rate<br>(누락 비율)        | "Max 10 % missing trees"<br>("최대 10% 나무 누락")           |
| Tree (나무)              | Max 10% excess<br>(최대 10% 과다)      | Commission rate<br>(과다 비율)      | "Max 10 % trees in excess"<br>("최대 10% 나무 과다")         |
| Tree height (나무 높이)  | Max 20% incorrect<br>(최대 20% 부정확) | Attribute accuracy<br>(속성 정확도) | "Max 20 % of the trees can have wrong height"<br>("최대 20% 나무가 잘못된 높이 가능") |

## 5. Statistical Sampling Values (통계적 샘플링 값)

### Sample Size Table (표본 크기 표) - From Annex F

| Population Size (모집단 크기) | Sample Size (n)<br>(표본 크기) | AQL 0.5% Rejection Limit<br>(AQL 0.5% 기각 한계) | AQL 1.0% Rejection Limit<br>(AQL 1.0% 기각 한계) | AQL 2.0% Rejection Limit<br>(AQL 2.0% 기각 한계) |
| ----------------------------- | ------------------------------ | ------------------------------------------------ | ------------------------------------------------ | ------------------------------------------------ |
| 1 - 8                         | All (전체)                     | 1                                                | 1                                                | 1                                                |
| 9 - 50                        | 8                              | 1                                                | 1                                                | 1                                                |
| 51 - 90                       | 13                             | 1                                                | 1                                                | 2                                                |
| 91 - 150                      | 20                             | 1                                                | 2                                                | 2                                                |
| 151 - 280                     | 32                             | 1                                                | 2                                                | 3                                                |
| 281 - 400                     | 50                             | 2                                                | 3                                                | 3                                                |
| 501 - 1200                    | 80                             | 3                                                | 3                                                | 5                                                |
| 1201 - 3200                   | 125                            | 3                                                | 4                                                | 6                                                |
| 3201 - 10000                  | 200                            | 4                                                | 6                                                | 8                                                |
| > 500000                      | 1250                           | 12                                               | 20                                               | 34                                               |

**Usage Example (사용 예시):**

- Dataset with 2440 buildings, Sample size n = 125<br>(2440개 건물의 데이터셋, 표본 크기 n = 125)
- If AQL = 0.5%, rejection limit = 3<br>(AQL = 0.5%이면, 기각 한계 = 3)
- If 2 missing items found: PASS (2 < 3)<br>(2개 누락 항목 발견: 합격 (2 < 3))
- If 3+ missing items found: FAIL (≥ 3)<br>(3개 이상 누락 항목 발견: 불합격 (≥ 3))

**Note (참고):** All values are derived from ISO 19157:2013, Annex D (Standardized Data Quality Measures) and Annex F (Sampling Methods). Specific thresholds should be defined in the data product specification according to application requirements.<br> (모든 값은 ISO 19157:2013, 부록 D (표준화된 데이터 품질 측정항목) 및 부록 F (샘플링 방법)에서 도출되었습니다. 구체적인 임계값은 응용 요구사항에 따라 데이터 제품 사양서에 정의되어야 합니다.)