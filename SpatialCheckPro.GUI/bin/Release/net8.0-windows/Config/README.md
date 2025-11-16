# 검수 설정 파일 가이드

이 디렉토리에는 공간정보 검수를 위한 CSV 설정 파일들이 포함되어 있습니다. 각 파일은 검수 단계별로 필요한 설정 정보를 정의합니다.

## 목차

1. [1_table_check.csv](#1-table_checkcsv) - 테이블 검수 설정
2. [2_schema_check.csv](#2-schema_checkcsv) - 스키마 검수 설정
3. [3_geometry_check.csv](#3-geometry_checkcsv) - 지오메트리 검수 설정
4. [4_attribute_check.csv](#4-attribute_checkcsv) - 속성 검수 설정
5. [5_relation_check.csv](#5-relation_checkcsv) - 공간 관계 검수 설정
6. [geometry_criteria.csv](#geometry_criteriacsv) - 지오메트리 검수 기준값
7. [codelist.csv](#codelistcsv) - 코드값 목록

---

## 1. table_check.csv

### 개요

1단계 테이블 검수에서 사용되는 설정 파일입니다. 검수 대상 테이블(피처클래스)의 기본 정보를 정의합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `TableId` | ✅ | 테이블의 영문명 (피처클래스명) | 영문, 숫자, 언더스코어 조합 (예: `tn_buld`) |
| `TableName` | ✅ | 테이블의 한글명 | 한글명 (예: `건물`) |
| `GeometryType` | ✅ | 지오메트리 타입 | `POINT`, `LINESTRING`, `POLYGON`, `MULTIPOINT`, `MULTILINESTRING`, `MULTIPOLYGON` |
| `CRS` | ✅ | 좌표계 정보 | EPSG 코드 형식 (예: `EPSG:5179`) |

### 작성 방법

```csv
TableId,TableName,GeometryType,CRS
tn_buld,건물,POLYGON,EPSG:5179
tn_rodway_ctln,도로중심선,LINESTRING,EPSG:5179
tn_alpt,표고점,POINT,EPSG:5179
```

### 유효값 설명

#### GeometryType

- **POINT**: 점 지오메트리 (단일 좌표)
- **LINESTRING**: 선 지오메트리 (연속된 점들의 집합)
- **POLYGON**: 면 지오메트리 (닫힌 링)
- **MULTIPOINT**: 다중 점 지오메트리
- **MULTILINESTRING**: 다중 선 지오메트리
- **MULTIPOLYGON**: 다중 면 지오메트리

**주의사항:**

- 실제 GDB 파일의 지오메트리 타입과 일치해야 합니다
- 대소문자는 구분하지 않지만, 표준 형식 사용 권장

#### CRS (좌표계)

- **EPSG:5179**: UTM-K (Korea 2000 / Unified CS) - 국내 표준 좌표계
- **EPSG:5185**: Korea 2000 / West Belt 2010 (서부원점)
- **EPSG:5186**: Korea 2000 / Central Belt 2010 (중부원점)
- **EPSG:5187**: Korea 2000 / East Belt 2010 (동부원점)
- **EPSG:5188**: Korea 2000 / East Sea Belt 2010 (동해(울릉)원점)
- **EPSG:4326**: WGS 84 (경위도)
- **EPSG:3857**: Web Mercator (WGS 84 / Pseudo-Mercator)

### 검수 동작

- 테이블 존재 여부 확인
- 지오메트리 타입 일치 여부 확인
- 좌표계 메타데이터 일치 여부 확인
- 피처 개수 확인

### 주의사항

- `TableId`는 실제 GDB 파일의 피처클래스명과 정확히 일치해야 합니다 (대소문자 구분)
- 이 파일에 정의되지 않은 테이블은 검수 대상에서 제외됩니다
- 실제 GDB에 존재하지 않는 테이블을 정의하면 경고가 발생합니다

---

## 2. schema_check.csv

### 개요
2단계 스키마 검수에서 사용되는 설정 파일입니다. 각 테이블의 필드(컬럼) 구조와 제약조건을 정의합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `TableId` | ✅ | 테이블 ID | `1_table_check.csv`에 정의된 `TableId`와 일치 |
| `FieldName` | ✅ | 필드명 (영문) | 실제 데이터베이스 필드명 |
| `FieldAlias` | | 필드 한글명 | 한글 설명 (선택) |
| `DataType` | ✅ | 데이터 타입 | `Integer`, `String`, `NUMERIC`, `CHAR`, `DateTime` |
| `Length` | | 필드 길이 | 문자열: 숫자 (예: `20`)<br>NUMERIC: `"길이,소수점"` (예: `"7,2"`) | |
| `UK` | | Unique Key 여부 | `UK` 또는 공백 |
| `FK` | | Foreign Key 여부 | `FK` 또는 공백 |
| `NN` | | Not Null 여부 | `Y` 또는 공백 |
| `RefTable` | | 참조 테이블명 (FK인 경우) | 참조할 테이블의 `TableId` |
| `RefColumn` | | 참조 컬럼명 (FK인 경우) | 참조할 테이블의 `FieldName` |

### 작성 방법

```csv
TableId,FieldName,FieldAlias,DataType,Length,UK,FK,NN,RefTable,RefColumn
tn_buld,objectid,시스템고유아이디,Integer,,,,Y,,
tn_buld,nf_id,국가기본도고유식별자아이디,String,17,UK,,,Y,,
tn_buld,bldg_se,건물구분,String,6,,,Y,,
tn_buld,bldg_nofl,건물층수,Integer,,,,Y,,
tn_buld,alpt_hgt,표고점높이,NUMERIC,"7,2",,,Y,,
tn_buld,rfrnc_nfid,참조NFID,String,17,,FK,Y,tn_fclty_zone_bndry,nf_id
```

### 유효값 설명

#### DataType

- **Integer**: 정수형 (32비트)
- **String**: 가변 길이 문자열
- **NUMERIC**: 실수형 (소수점 포함)
- **CHAR**: 고정 길이 문자열
- **DateTime**: 날짜/시간 형식

#### Length

- **문자열 타입 (String, CHAR)**: 최대 길이 (예: `20`, `255`)
- **NUMERIC 타입**: `"전체자릿수,소수점자릿수"` 형식 (예: `"7,2"` = 전체 7자리, 소수점 2자리)
- **Integer, DateTime**: 공백 가능

#### UK (Unique Key)

- **UK**: 해당 필드가 고유값을 가져야 함 (중복값 검사 수행)
- **공백**: 고유값 제약 없음

#### FK (Foreign Key)

- **FK**: 외래키 제약조건 (참조 무결성 검사 수행)
- **공백**: 외래키 제약 없음
- **주의**: FK인 경우 `RefTable`과 `RefColumn`이 필수입니다

#### NN (Not Null)

- **Y**: NULL 값 허용 안 함 (필수 필드)
- **공백**: NULL 값 허용

### 검수 동작

- 필드 존재 여부 확인
- 데이터 타입 일치 여부 확인
- 필드 길이/정밀도 일치 여부 확인
- Not Null 제약조건 확인
- Unique Key 중복값 검사
- Foreign Key 참조 무결성 검사

### 주의사항

- FK 설정 시 `RefTable`과 `RefColumn`을 반드시 지정해야 합니다
- `RefTable`은 `1_table_check.csv`에 정의된 `TableId`와 일치해야 합니다
- NUMERIC 타입의 Length는 반드시 `"길이,소수점"` 형식으로 작성해야 합니다 (따옴표 포함)

---

## 3. geometry_check.csv

### 개요

3단계 지오메트리 검수에서 사용되는 설정 파일입니다. 각 테이블에 대해 수행할 지오메트리 검사 항목을 정의합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `TableId` | ✅ | 테이블 ID | `1_table_check.csv`에 정의된 `TableId`와 일치 |
| `TableName` | ✅ | 테이블 한글명 | 한글명 |
| `GeometryType` | ✅ | 지오메트리 타입 | `POINT`, `MULTILINESTRING`, `MULTIPOLYGON` 등 |
| `객체중복` | ✅ | 중복 지오메트리 검사 | `Y` 또는 `N` |
| `객체간겹침` | ✅ | 객체 간 겹침 검사 | `Y` 또는 `N` |
| `자체꼬임` | ✅ | 자체 교차 검사 | `Y` 또는 `N` |
| `슬리버` | ✅ | 슬리버 폴리곤 검사 | `Y` 또는 `N` |
| `짧은객체` | ✅ | 짧은 선분 검사 | `Y` 또는 `N` |
| `작은면적객체` | ✅ | 작은 면적 폴리곤 검사 | `Y` 또는 `N` |
| `홀 폴리곤 오류` | ✅ | 폴리곤 내부 홀 검사 | `Y` 또는 `N` |
| `최소정점개수` | ✅ | 최소 정점 개수 검사 | `Y` 또는 `N` |
| `스파이크` | ✅ | 스파이크 검출 | `Y` 또는 `N` |
| `자기중첩` | ✅ | 자기 중첩 검사 | `Y` 또는 `N` |
| `언더슛` | ✅ | 언더슛 검사 | `Y` 또는 `N` |
| `오버슛` | ✅ | 오버슛 검사 | `Y` 또는 `N` |

### 작성 방법

```csv
TableId,TableName,GeometryType,객체중복,객체간겹침,자체꼬임,슬리버,짧은객체,작은면적객체,홀 폴리곤 오류,최소정점개수,스파이크,자기중첩,언더슛,오버슛
tn_buld,건물,MULTIPOLYGON,Y,Y,Y,Y,N,Y,Y,Y,Y,Y,N,N
tn_rodway_ctln,도로중심선,MULTILINESTRING,Y,Y,Y,N,Y,N,N,Y,N,Y,Y,Y
tn_alpt,표고점,POINT,Y,N,N,N,N,N,N,Y,N,N,N,N
```

### 검사 항목 상세 설명

| 검사 항목 | 적용 지오메트리 | 설명 | 기준값 파일 |
|----------|----------------|------|-------------|
| **객체중복** | 전체 | 동일한 좌표를 가진 지오메트리가 중복되는지 검사 | `중복검사허용오차` |
| **객체간겹침** | LINE, POLYGON | 서로 다른 객체 간 겹침(overlap) 발생 여부 검사 | `겹침허용면적` |
| **자체꼬임** | LINE, POLYGON | 단일 객체가 자기 자신과 교차하는지 검사 | `자체꼬임허용각도` |
| **슬리버** | POLYGON | 가늘고 긴 슬리버 폴리곤 검출 (면적 대비 둘레 비율) | `슬리버면적`, `슬리버형태지수`, `슬리버신장률` |
| **짧은객체** | LINE | 지정된 길이보다 짧은 선분 검출 | `최소선길이` |
| **작은면적객체** | POLYGON | 지정된 면적보다 작은 폴리곤 검출 | `최소폴리곤면적` |
| **홀 폴리곤 오류** | POLYGON | 폴리곤 내부 홀의 유효성 검사 (폴리곤 내 폴리곤) | `폴리곤내폴리곤최소거리` |
| **최소정점개수** | LINE, POLYGON | 지오메트리의 최소 정점 개수 검사 (일반적으로 2개 이상) | - |
| **스파이크** | LINE, POLYGON | 급격한 각도 변화(spike) 검출 | `스파이크각도임계값` |
| **자기중첩** | LINE, POLYGON | 선분이 자기 자신 위에 중첩되는지 검사 | - |
| **언더슛** | LINE | 선의 끝점이 연결되어야 하는데 짧게 끊긴 경우 | `네트워크탐색거리` |
| **오버슛** | LINE | 선의 끝점이 교차점을 넘어 튀어나온 경우 | `네트워크탐색거리` |

### 지오메트리 타입별 권장 설정

#### POINT 타입

- 대부분 `객체중복`과 `최소정점개수`만 `Y`로 설정
- 나머지는 `N`으로 설정

#### LINESTRING/MULTILINESTRING 타입

- `객체중복`, `객체간겹침`, `자체꼬임`, `짧은객체`, `최소정점개수`, `스파이크`, `자기중첩`, `언더슛`, `오버슛` 권장
- `슬리버`, `작은면적객체`, `홀 폴리곤 오류`는 `N`

#### POLYGON/MULTIPOLYGON 타입

- 대부분의 검사 항목을 `Y`로 설정 가능
- `짧은객체`, `언더슛`, `오버슛`은 일반적으로 `N`

### 주의사항

- 실제 GDB에 존재하지 않는 테이블을 정의하면 검수에서 자동으로 스킵됩니다
- 각 검사 항목의 기준값은 `geometry_criteria.csv`에서 설정됩니다
- 지오메트리 타입에 따라 적용 가능한 검사 항목이 다르므로, 타입에 맞게 설정해야 합니다

---

## 4. attribute_check.csv

### 개요

4단계 속성 검수에서 사용되는 설정 파일입니다. 필드값의 유효성을 검사하는 규칙을 정의합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `RuleId` | ✅ | 규칙 고유 ID | 영문, 숫자, 언더스코어 조합 |
| `Enabled` | ✅ | 규칙 활성화 여부 | `Y` 또는 `N` |
| `TableId` | ✅ | 테이블 ID | `1_table_check.csv`의 `TableId` 또는 `*` (모든 테이블) |
| `TableName` | ✅ | 테이블 한글명 | 한글명 또는 `전체` |
| `FieldName` | ✅ | 검수할 필드명 | 실제 필드명 |
| `CheckType` | ✅ | 검수 타입 | `CodeList`, `Range`, `Regex`, `NotNull`, `NotZero`, `MultipleOf`, `IfCodeThen*` 등 |
| `Parameters` | | 검수 파라미터 | CheckType에 따라 형식 다름 (아래 참조) |
| `Severity` | | 오류 심각도 | `INFO`, `MINOR`, `MAJOR`, `CRIT` (기본: `MAJOR`) |
| `Note` | | 비고/설명 | 규칙 설명 |

### 작성 방법

```csv
RuleId,Enabled,TableId,TableName,FieldName,CheckType,Parameters,Severity,Note
ATTR_CODE_BULD,Y,tn_buld,건물,bldg_se,CodeList,건물구분V6,MAJOR,건물 용도 코드리스트
ATTR_NOTZERO_FLOOR,Y,tn_buld,건물,bldg_nofl,NotZero,,MAJOR,층수 0 금지
ATTR_PNU_17LEN,Y,tn_buld,건물,pnu,Regex,^[0-9]{17}$,MAJOR,PNU 17자리
ATTR_ALPT_LT2000,Y,tn_alpt,표고점,alpt_hgt,Range,0.0000001..1999.9999,MAJOR,표고점높이 0 금지 및 2000 미만
CTRLN_HGT_MUL5,Y,tn_ctrln,등고선,ctrln_hgt,MultipleOf,5,MAJOR,등고선높이 5의 배수
```

### CheckType별 Parameters 형식

#### CodeList

- **형식**: 코드셋 ID (예: `건물구분V6`)
- **설명**: `codelist.csv`에서 해당 코드셋의 모든 코드값을 참조하여 검사
- **예시**: `CodeList,건물구분V6` → `codelist.csv`의 `CodeSetId="건물구분V6"`인 모든 `CodeValue`와 비교

#### Range

- **형식**: `최소값..최대값` (예: `0.0000001..1999.9999`)
- **설명**: 숫자값이 지정된 범위 내에 있는지 검사
- **예시**: 
  - `0.0000001..1999.9999`: 0보다 크고 2000 미만
  - `..100`: 100 이하 (최소값 없음)
  - `10..`: 10 이상 (최대값 없음)

#### Regex

- **형식**: 정규식 패턴 (예: `^[0-9]{17}$`)
- **설명**: 문자열이 정규식 패턴과 일치하는지 검사
- **예시**: 
  - `^[0-9]{17}$`: 정확히 17자리 숫자
  - `^[A-Z]{3}$`: 정확히 3자리 대문자

#### NotNull

- **형식**: 파라미터 없음 (공백)
- **설명**: 필드값이 NULL이 아닌지 검사

#### NotZero

- **형식**: 파라미터 없음 (공백)
- **설명**: 숫자값이 0이 아닌지 검사

#### MultipleOf

- **형식**: 배수 값 (예: `5`, `25`)
- **설명**: 숫자값이 지정된 배수인지 검사
- **예시**: `MultipleOf,5` → 값이 5의 배수여야 함

#### IfCodeThenMultipleOf

- **형식**: `코드필드명;코드값목록;수치필드명;배수값`
- **설명**: 특정 코드값인 경우 수치 필드가 지정 배수여야 함
- **예시**: `ctrln_se;CTS001|CTS101;CTRLN_HGT;25`
  - `ctrln_se` 필드가 `CTS001` 또는 `CTS101`인 경우
  - `CTRLN_HGT` 필드가 25의 배수여야 함

#### IfCodeThenNumericEquals

- **형식**: `코드필드명;코드값;수치필드명;고정값`
- **설명**: 특정 코드값인 경우 수치 필드가 고정값이어야 함
- **예시**: `road_se;RDS014;ROAD_BT;1.5`
  - `road_se` 필드가 `RDS014`인 경우
  - `ROAD_BT` 필드가 정확히 1.5여야 함

#### IfCodeThenBetweenExclusive

- **형식**: `코드필드명;코드값;수치필드명;최소값..최대값`
- **설명**: 특정 코드값인 경우 수치 필드가 범위 내에 있어야 함 (경계 제외)
- **예시**: `road_se;RDS010;ROAD_BT;1.5..3.0`
  - `road_se` 필드가 `RDS010`인 경우
  - `ROAD_BT` 필드가 1.5 초과 3.0 미만이어야 함

#### IfCodeThenNotNullAll

- **형식**: `코드필드명;코드값목록;필드명목록`
- **설명**: 특정 코드값인 경우 지정된 모든 필드가 NULL이 아니어야 함
- **예시**: `road_se;RDS001|RDS002|RDS003;FEAT_NM|ROAD_NO`
  - `road_se` 필드가 `RDS001`, `RDS002`, `RDS003` 중 하나인 경우
  - `FEAT_NM`과 `ROAD_NO` 필드가 모두 NULL이 아니어야 함

#### IfCodeThenNull

- **형식**: `코드필드명;코드값;필드명`
- **설명**: 특정 코드값인 경우 지정 필드가 NULL이어야 함
- **예시**: `bldg_se;BDG001;FEAT_NM`
  - `bldg_se` 필드가 `BDG001`인 경우
  - `FEAT_NM` 필드가 NULL이어야 함

#### KoreanTypo

- **형식**: 파라미터 없음 (공백)
- **설명**: 한글명의 오타 검사 (자동 오타 검출)

#### buld_height_base_vs_max

- **형식**: 파라미터 없음 (공백)
- **설명**: 건물 기본높이가 최고높이보다 큰지 검사 (특수 규칙)

#### buld_height_max_vs_facility

- **형식**: 파라미터 없음 (공백)
- **설명**: 건물 최고높이가 시설물높이보다 큰지 검사 (특수 규칙)

### TableId 특수값

- **`*`**: 모든 테이블에 적용 (예: `*,전체,objfltn_se,CodeList,객체변동구분V6`)

### Severity 레벨

- **INFO**: 정보성 오류 (경고 수준)
- **MINOR**: 경미한 오류
- **MAJOR**: 주요 오류 (기본값)
- **CRIT**: 치명적 오류

### 주의사항
- `CodeList` 타입의 경우 `Parameters`에 지정된 코드셋 ID가 `codelist.csv`에 존재해야 합니다
- `IfCodeThen*` 타입의 경우 `Parameters` 형식을 정확히 따라야 합니다 (세미콜론 구분)
- `TableId`가 `*`인 경우 모든 테이블에 적용되므로 주의가 필요합니다

---

## 5. relation_check.csv

### 개요

5단계 공간 관계 검수에서 사용되는 설정 파일입니다. 테이블 간 공간적 관계를 검사하는 규칙을 정의합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `RuleId` | ✅ | 규칙 고유 ID | 영문, 숫자, 언더스코어, 한글 조합 |
| `Enabled` | ✅ | 규칙 활성화 여부 | `Y` 또는 `N` |
| `CaseType` | ✅ | 공간 관계 유형 | `PointInsidePolygon`, `LineWithinPolygon`, `PolygonNotWithinPolygon`, `PolygonNotOverlap`, `PolygonNotIntersectLine`, `PolygonNotContainPoint`, `LineConnectivity`, `PolygonMissingLine` |
| `MainTableId` | ✅ | 주 테이블 ID | `1_table_check.csv`의 `TableId` |
| `MainTableName` | ✅ | 주 테이블 한글명 | 한글명 |
| `RelatedTableId` | ✅ | 관련 테이블 ID | `1_table_check.csv`의 `TableId` |
| `RelatedTableName` | ✅ | 관련 테이블 한글명 | 한글명 |
| `FieldFilter` | | 필드 필터 조건 | SQL WHERE 절 형식 (예: `road_se IN (RDS001|RDS002)`) |
| `Tolerance` | | 허용 오차 (미터) | 숫자 (예: `0.001`, `1.0`) 또는 공백 (기본값 사용) |
| `Note` | | 비고/설명 | 규칙 설명 |

### 작성 방법

```csv
RuleId,Enabled,CaseType,MainTableId,MainTableName,RelatedTableId,RelatedTableName,FieldFilter,Tolerance,Note
건물중심점_관계,Y,PointInsidePolygon,tn_buld,건물,tn_buld_ctpt,건물중심점,,,건물중심점은 건물을 벗어나면 안 됨
도로경계면_관계,Y,LineWithinPolygon,tn_rodway_bndry,도로경계면,tn_rodway_ctln,도로중심선,road_se IN (RDS001|RDS002|RDS003),0.001,중심선은 경계면을 벗어나면 안 됨
건물_도로경계면_침범,Y,PolygonNotOverlap,tn_buld,건물,tn_rodway_bndry,도로경계면,,0.1,겹침 면적 허용 0.1
도로중심선_연결성,Y,LineConnectivity,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,,1,끝점 1m 이내 연결 확인
```

### CaseType별 설명

| CaseType | 설명 | 적용 지오메트리 | Tolerance 의미 |
|----------|------|----------------|----------------|
| **PointInsidePolygon** | 점이 폴리곤 내부에 포함되어야 함 | POINT ↔ POLYGON | - |
| **LineWithinPolygon** | 선이 폴리곤 내부에 완전히 포함되어야 함 | LINESTRING ↔ POLYGON | 선-면 포함 관계 허용 오차 (기본: 0.001m) |
| **PolygonNotWithinPolygon** | 폴리곤이 다른 폴리곤 내부에 포함되면 안 됨 | POLYGON ↔ POLYGON | 면-면 포함 관계 허용 오차 (기본: 0.001m) |
| **PolygonNotOverlap** | 폴리곤 간 겹침이 허용되지 않음 (또는 허용 면적 이내) | POLYGON ↔ POLYGON | 허용 겹침 면적 (m²) |
| **PolygonNotIntersectLine** | 폴리곤과 선이 교차하면 안 됨 | POLYGON ↔ LINESTRING | - |
| **PolygonNotContainPoint** | 폴리곤 내부에 점이 포함되면 안 됨 | POLYGON ↔ POINT | - |
| **LineConnectivity** | 선의 끝점이 연결되어야 함 (언더슛/오버슛 검출) | LINESTRING ↔ LINESTRING | 연결 탐색 거리 (기본: 1.0m) |
| **PolygonMissingLine** | 폴리곤 내부에 선이 전혀 없으면 오류 | POLYGON ↔ LINESTRING | 선 존재 여부 판정 거리 (기본: 0.1m) |

### FieldFilter 형식

SQL WHERE 절과 유사한 형식으로 작성합니다.

**지원 연산자:**

- `IN`: 값 목록 포함 (예: `road_se IN (RDS001|RDS002|RDS003)`)
- `=`: 동등 비교
- `!=`, `<>`: 부등 비교
- `>`, `<`, `>=`, `<=`: 크기 비교

**예시:**

- `road_se IN (RDS001|RDS002|RDS003)`: `road_se` 필드가 RDS001, RDS002, RDS003 중 하나
- `pg_rdfc_se IN (PRC002|PRC003|PRC004|PRC005)`: `pg_rdfc_se` 필드가 지정된 코드 중 하나

### Tolerance 설정

- **비어있음**: `geometry_criteria.csv`의 기본값 사용
- **숫자 지정**: 해당 규칙에만 적용되는 커스텀 허용 오차

**CaseType별 기본값:**

- `LineWithinPolygon`: `선면포함관계허용오차` (기본: 0.001m)
- `PolygonNotWithinPolygon`: `면면포함관계허용오차` (기본: 0.001m)
- `LineConnectivity`: `선연결성탐색거리` (기본: 1.0m)
- `PolygonMissingLine`: `선면포함관계허용오차` (기본: 0.001m)

### 주의사항

- `MainTableId`와 `RelatedTableId`는 모두 `1_table_check.csv`에 정의되어 있어야 합니다
- `CaseType`에 따라 적용 가능한 지오메트리 타입이 다릅니다
- `FieldFilter`는 선택 사항이지만, 특정 조건의 피처만 검사하려면 사용하는 것이 좋습니다

---

## 6. geometry_criteria.csv

### 개요
지오메트리 검수와 공간 관계 검수에서 사용되는 기준값을 정의하는 파일입니다. 모든 지오메트리 검수와 관계 검수의 허용 오차 및 임계값을 중앙에서 관리합니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `항목명` | ✅ | 기준값 항목명 | 시스템에서 사용하는 고정된 항목명 |
| `값` | ✅ | 기준값 | 숫자 (정수 또는 실수) |
| `단위` | ✅ | 단위 | `미터`, `제곱미터`, `도`, `무차원` |
| `설명` | ✅ | 항목 설명 | 한글 설명 |

### 작성 방법

```csv
항목명,값,단위,설명
겹침허용면적,0.01,제곱미터,폴리곤 겹침 허용 면적
최소선길이,0.01,미터,짧은 선 객체 판정 기준
최소폴리곤면적,1.0,제곱미터,작은 면적 객체 판정 기준
```

### 항목명별 상세 설명

| 항목명 | 기본값 | 단위 | 용도 | 영향 범위 |
|--------|--------|------|------|-----------|
| **겹침허용면적** | 0.01 | 제곱미터 | 폴리곤 간 겹침 검수 시 허용 면적 | 지오메트리 검수 (객체간겹침) |
| **최소선길이** | 0.01 | 미터 | 짧은 선분 판정 기준 (이보다 짧으면 오류) | 지오메트리 검수 (짧은객체) |
| **최소폴리곤면적** | 1.0 | 제곱미터 | 작은 폴리곤 판정 기준 (이보다 작으면 오류) | 지오메트리 검수 (작은면적객체) |
| **자체꼬임허용각도** | 1.0 | 도 | 자체 교차 허용 각도 | 지오메트리 검수 (자체꼬임) |
| **폴리곤내폴리곤최소거리** | 0.1 | 미터 | 폴리곤 내부 폴리곤 최소 거리 | 지오메트리 검수 (홀 폴리곤 오류) |
| **슬리버면적** | 2.0 | 제곱미터 | 슬리버폴리곤 면적 기준 | 지오메트리 검수 (슬리버) |
| **슬리버형태지수** | 0.1 | 무차원 | 슬리버폴리곤 형태지수 기준 (4π×면적/둘레²) | 지오메트리 검수 (슬리버) |
| **슬리버신장률** | 10.0 | 무차원 | 슬리버폴리곤 신장률 기준 (가로/세로 비율) | 지오메트리 검수 (슬리버) |
| **스파이크각도임계값** | 8.0 | 도 | 스파이크 검출 각도 임계값 | 지오메트리 검수 (스파이크) |
| **링폐합오차** | 1e-8 | 미터 | 폴리곤 링 폐합 허용 오차 | 지오메트리 검수 (기본 검사) |
| **네트워크탐색거리** | 0.1 | 미터 | 언더슛/오버슛 탐색 반경 | 지오메트리 검수 (언더슛, 오버슛) |
| **중복검사허용오차** | 0.001 | 미터 | 중복 지오메트리 판정 거리 | 지오메트리 검수 (객체중복) |
| **선면포함관계허용오차** | 0.01 | 미터 | 선이 면에 포함되는지 검사 시 허용 오차 | 관계 검수 (LineWithinPolygon) |
| **면면포함관계허용오차** | 0.01 | 미터 | 면이 면에 포함되는지 검사 시 허용 오차 | 관계 검수 (PolygonNotWithinPolygon) |
| **선연결성탐색거리** | 1.0 | 미터 | 선 연결성 검사 시 탐색 거리 | 관계 검수 (LineConnectivity) |
| **스파이크모두저장** | 1 | - | 0=최솟값 1개만 저장, 1=모든 스파이크 저장 | 지오메트리 검수 (스파이크) |

### 기준값 수정 방법

1. Excel이나 텍스트 에디터로 `geometry_criteria.csv` 파일을 엽니다
2. '값' 컬럼의 숫자를 원하는 기준값으로 수정합니다
3. **UTF-8 인코딩**으로 저장합니다
4. 애플리케이션을 재시작하면 새로운 기준값이 적용됩니다

### 주의사항

- **항목명을 변경하면 안 됩니다** (시스템에서 항목명으로 값을 읽음)
- 값은 반드시 숫자여야 합니다
- 과도하게 큰 값이나 작은 값(0 이하)은 검수 결과에 영향을 줄 수 있습니다
- 변경 후에는 테스트 데이터로 검증하는 것을 권장합니다
- `스파이크모두저장`은 0 또는 1만 유효합니다

---

## 7. codelist.csv

### 개요
속성 검수(`4_attribute_check.csv`)에서 `CodeList` 타입 규칙이 참조하는 코드값 목록을 정의하는 파일입니다.

### 파일 구조

| 컬럼명 | 필수 | 설명 | 유효값 |
|--------|------|------|--------|
| `CodeSetId` | ✅ | 코드셋 식별자 | 코드셋 고유 ID (예: `건물구분V6`) |
| `CodeValue` | ✅ | 코드 값 | 실제 데이터에 사용되는 코드값 (예: `BDG001`) |
| `Label` | ✅ | 코드 설명 | 코드의 한글 설명 (예: `단독주택`) |

### 작성 방법

```csv
CodeSetId,CodeValue,Label
건물구분V6,BDG001,단독주택
건물구분V6,BDG002,공동주택
건물구분V6,BDG003,상업시설
도로구분V6,RDS001,고속국도
도로구분V6,RDS002,일반국도
```

### 사용 방법

`4_attribute_check.csv`에서 `CheckType`이 `CodeList`인 경우:

```csv
RuleId,Enabled,TableId,TableName,FieldName,CheckType,Parameters,Severity,Note
건물구분_Code,Y,tn_buld,건물,bldg_se,CodeList,건물구분V6,MAJOR,건물구분V6_코드리스트 점검
```

위 예시에서:

- `Parameters`에 지정된 `건물구분V6`가 `CodeSetId`로 사용됩니다
- `codelist.csv`에서 `CodeSetId="건물구분V6"`인 모든 `CodeValue`를 조회합니다
- 실제 데이터의 `bldg_se` 필드값이 조회된 `CodeValue` 목록에 포함되어 있는지 검사합니다

### 주의사항

- `CodeSetId`는 `4_attribute_check.csv`의 `Parameters`와 정확히 일치해야 합니다
- 같은 `CodeSetId`에 여러 `CodeValue`가 정의될 수 있습니다
- `CodeValue`는 대소문자를 구분합니다
- 코드값이 변경되면 `codelist.csv`를 업데이트해야 합니다

---

## 공통 작성 규칙

### 파일 인코딩

- **모든 CSV 파일은 UTF-8 인코딩으로 저장해야 합니다**
- Excel에서 저장 시 "CSV UTF-8(쉼표로 분리)(*.csv)" 형식 선택

### 컬럼 순서

- **컬럼의 순서를 변경하지 마세요** (시스템에서 컬럼 순서로 파싱)
- 헤더 행은 반드시 첫 번째 행에 있어야 합니다

### 필수/선택 컬럼

- 필수 컬럼(✅ 표시)은 반드시 값을 입력해야 합니다
- 선택 컬럼은 필요에 따라 공백으로 둘 수 있습니다

### 값 형식

- **Y/N 값**: 대문자 `Y` 또는 `N` 사용 권장
- **숫자값**: 소수점은 마침표(.) 사용
- **문자열**: 특수문자 포함 시 주의 (쉼표, 따옴표 등)

### 파일명

- **파일명을 변경하지 마세요** (시스템에서 고정된 파일명으로 읽음)
- 파일명은 대소문자를 구분합니다

---

## 검수 단계별 파일 매핑

| 검수 단계 | 검수 내용 | 사용 설정 파일 |
|----------|----------|---------------|
| **1단계** | 테이블 검수 | `1_table_check.csv` |
| **2단계** | 스키마 검수 | `2_schema_check.csv` |
| **3단계** | 지오메트리 검수 | `3_geometry_check.csv`, `geometry_criteria.csv` |
| **4단계** | 속성 검수 | `4_attribute_check.csv`, `codelist.csv` |
| **5단계** | 공간 관계 검수 | `5_relation_check.csv`, `geometry_criteria.csv` |

---

## 문제 해결

### 설정 파일 오류

설정 파일에 오류가 있는 경우 애플리케이션에서 오류 메시지를 표시합니다. 오류 메시지를 참고하여 해당 파일을 수정하세요.

### 일반적인 오류

1. **인코딩 오류**: UTF-8로 저장하지 않은 경우 한글이 깨질 수 있습니다
2. **컬럼 불일치**: 헤더 행의 컬럼명이 정확하지 않은 경우
3. **값 형식 오류**: 숫자 필드에 문자가 입력된 경우
4. **참조 오류**: FK의 `RefTable`이 `1_table_check.csv`에 없는 경우

### 검증 방법

1. 애플리케이션의 "검수 설정" 화면에서 각 단계별 설정을 미리 확인할 수 있습니다
2. 설정 파일을 수정한 후 애플리케이션을 재시작하면 자동으로 새 설정이 로드됩니다

---

## 추가 참고사항

- 각 설정 파일은 독립적으로 수정 가능합니다
- 설정 파일을 수정한 후에는 반드시 애플리케이션을 재시작해야 합니다
- 대용량 데이터 검수 시 성능을 위해 불필요한 검사 항목은 `N`으로 설정하는 것을 권장합니다
- 자세한 사용법은 사용자 매뉴얼을 참고하세요
