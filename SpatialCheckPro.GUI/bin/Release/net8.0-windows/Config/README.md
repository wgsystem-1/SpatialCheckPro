# 검수 설정 파일 가이드

이 디렉토리에는 공간정보 검수를 위한 CSV 설정 파일들이 포함되어 있습니다.

## 설정 파일 목록

### 1. table_check.csv

테이블 검수를 위한 설정 파일입니다.

**컬럼 구조:**

- `테이블ID`: 테이블의 영문명
- `테이블명칭`: 테이블의 한글명
- `지오메트리타입`: 지오메트리 유형 (POINT, LINESTRING, POLYGON)
- `좌표계`: 좌표계 정보 (예: EPSG:5179)

**예시:**

```csv
테이블ID,테이블명칭,지오메트리타입,좌표계
TN_CTPRVN_BNDRY,시도구역경계, POLYGON ,EPSG:5179
TN_SIGNGU_BNDRY,시군구구역경계, POLYGON ,EPSG:5179
TN_EMD_BNDRY,읍면동구역경계, POLYGON ,EPSG:5179
TN_FCLTY_ZONE_BNDRY,시설구역경계, POLYGON ,EPSG:5179
```

### 2. schema_check.csv

스키마 검수를 위한 설정 파일입니다.

**컬럼 구조:**

- `테이블ID`: 테이블의 영문명
- `컬럼명칭`: 컬럼 이름
- `컬럼한글명`: 컬럼 한글 이름
- `타입`: 데이터 타입 (VARCHAR, INTEGER, DOUBLE 등)
- `길이`: 문자열 타입의 최대 길이
- `UK`: Unique Key 여부 (UK)
- `FK`: Foreign Key 여부 (FK)
- `NN`: Not Null 여부 (Y/N)
- `참조테이블`: 참조하는 테이블명, FK인 경우 참조테이블의 참조컬럼을 조회
- `참조컬럼`: 참조하는 컬럼명

**예시:**

```csv
테이블ID,컬럼명칭,컬럼한글명,타입,길이,PK,UK,FK,NN,참조테이블,참조컬럼
tn_tn_buld,objectid,시스템고유아이디,INTEGER,,,,,Y,,
tn_buld,ncid,변동정보관리아이디,TEXT,20,,,,,,
tn_buld,nf_id,국가기본도고유식별자아이디,TEXT,17,PK,,,Y,,
tn_buld,rfrnc_nfid,참조NFID,TEXT,17,,,FK,,TN_FCLTY_ZONE_BNDRY,NF_ID
tn_buld,molit_ufid,국토교통부UFID,TEXT,17,,UK,,,,
tn_buld,bdgusg_se,건물용도구분,TEXT,6,,,,Y,,
tn_buld,bdgsymb_se,건물기호구분,TEXT,6,,,,Y,,
tn_buld,feat_nm,지형지물명칭,TEXT,200,,,,,,
tn_buld,batc_nm,건물부명칭,TEXT,200,,,,,,
tn_buld,bldg_se,건물구분,TEXT,6,,,,Y,,
tn_buld,mnbldg_se,주건물구분,TEXT,6,,,,Y,,
tn_buld,bldg_nofl,건물층수,INTEGER,,,,,Y,,
tn_buld,pnu,PNU,TEXT,19,,,,Y,,
tn_buld,bd_mgt_sn,도로명주소건물관리번호,CHAR,25,,,,,,
tn_buld,bldrgst_pk,건축물대장일련번호,CHAR,33,,,,,,
tn_buld,usecon_day,사용승인일,TEXT,8,,,,,,
tn_buld,sig_cd,시군구코드,TEXT,5,,,,,,
tn_buld,road_nm_cd,도로명코드,TEXT,7,,,,,,
tn_buld,road_nm,도로명,TEXT,80,,,,,,
tn_buld,bno_mno,건물번호본번,INTEGER,,,,,,,
tn_buld,bno_sno,건물번호부번,INTEGER,,,,,,,
tn_buld,bldlwt_hgt,건물최저높이,NUMERIC,"7,2",,,,,,
tn_buld,bldhgt_hgt,건물최고높이,NUMERIC,"7,2",,,,,,
tn_buld,bldbsc_hgt,건물기본높이,NUMERIC,"7,2",,,,,,
tn_buld,blfcht_hgt,건물시설물최고높이,NUMERIC,"7,2",,,,,,
tn_buld,rdscl_se,축척구분,TEXT,6,,,,Y,,
tn_buld,mdsvymt_se,수정측량방법구분,TEXT,1,,,,Y,,
tn_buld,objfltn_se,객체변동구분,TEXT,6,,,,Y,,
tn_buld,fbcbzet_nm,제작업체명,TEXT,100,,,,Y,,
tn_buld,globalid,시스템운영용고유객체관리번호,TEXT,38,,,,Y,,
tn_buld,created_user,생성자계정명,TEXT,255,,,,,,
tn_buld,created_date,최초생성일,DATE,,,,,,,
tn_buld,last_edited_user,마지막편집계정명,TEXT,255,,,,,,
tn_buld,last_edited_date,마지막편집일,DATE,,,,,,,
tn_buld,gdb_branch_id,버전실행자(편집자)의고유식별번호,INTEGER,,,,,Y,,
tn_buld,gdb_from_date,객체생성/수정일자,DATE,,,,,Y,,
tn_buld,gdb_is_delete,객체삭제여부,INTEGER,,,,,Y,,
tn_buld,gdb_deleted_at,객체삭제일자,DATE,,,,,,,
tn_buld,gdb_deleted_by,객체삭제실행자,TEXT,255,,,,,,
tn_buld,gdb_archive_oid,객체아카이브이력이참조하는ObjectID,INTEGER,,,,,Y,,
```

### 3. geometry_check.csv

지오메트리 검수를 위한 설정 파일입니다.

**컬럼 구조:**

- `테이블ID`: 테이블의 영문명
- `테이블명칭`: 테이블의 한글명
- `지오메트리타입`: 지오메트리 유형 (POINT, MULTILINESTRING, MULTIPOLYGON)
- `객체중복`: 중복 지오메트리 검사 여부 (Y/N)
- `객체간겹침`: 객체 간 겹침 지오메트리 검사 여부 (Y/N)
- `자체꼬임`: 자체 꼬임(self-intersection) 검사 여부 (Y/N)
- `슬리버`: 슬리버 폴리곤 검사 여부 (Y/N)
- `짧은객체`: 짧은 선 객체 검사 여부 (Y/N)
- `작은면적객체`: 작은 면적 폴리곤 검사 여부 (Y/N)
- `홀 폴리곤 오류`: 폴리곤 내부 홀(hole) 검사 여부 (Y/N)
- `최소정점개수`: 최소 정점 개수 검사 여부 (Y/N)
- `스파이크`: 스파이크(spike) 검출 여부 (Y/N)
- `자기중첩`: 자기 중첩 검사 여부 (Y/N)
- `언더슛`: 언더슛(undershoot) 검사 여부 (Y/N)
- `오버슛`: 오버슛(overshoot) 검사 여부 (Y/N)

**예시:**

```csv
테이블ID,테이블명칭,지오메트리타입,객체중복,객체간겹침,자체꼬임,슬리버,짧은객체,작은면적객체,홀 폴리곤 오류,최소정점개수,스파이크,자기중첩,언더슛,오버슛
tn_buld,건물,MULTIPOLYGON,Y,Y,Y,Y,N,Y,Y,Y,Y,Y,N,N
tn_rodway_ctln,도로중심선,MULTILINESTRING,Y,Y,Y,N,Y,N,N,Y,N,Y,Y,Y
tn_ptrfc,점형도로시설,POINT,Y,N,N,N,N,N,N,Y,N,N,N,N
```

**검사 항목 설명:**

| 검사 항목 | 적용 지오메트리 | 설명 | 관련 기준값 (geometry_criteria.csv) |
|----------|----------------|------|-------------------------------------|
| **객체중복** | 전체 | 동일한 지오메트리가 중복되는지 검사 | 중복검사허용오차 |
| **객체간겹침** | LINE, POLYGON | 객체 간 겹침(overlap) 발생 여부 검사 | 겹침허용면적 |
| **자체꼬임** | LINE, POLYGON | 단일 객체가 자기 자신과 교차하는지 검사 | 자체꼬임허용각도 |
| **슬리버** | POLYGON | 가늘고 긴 슬리버 폴리곤 검출 | 슬리버면적, 슬리버형태지수, 슬리버신장률 |
| **짧은객체** | LINE | 지정된 길이보다 짧은 선분 검출 | 최소선길이 |
| **작은면적객체** | POLYGON | 지정된 면적보다 작은 폴리곤 검출 | 최소폴리곤면적 |
| **홀 폴리곤 오류** | POLYGON | 폴리곤 내부 홀의 유효성 검사 | 폴리곤내폴리곤최소거리 |
| **최소정점개수** | LINE, POLYGON | 지오메트리의 최소 정점 개수 검사 | - |
| **스파이크** | LINE, POLYGON | 급격한 각도 변화(spike) 검출 | 스파이크각도임계값 |
| **자기중첩** | LINE, POLYGON | 선분이 자기 자신 위에 중첩되는지 검사 | - |
| **언더슛** | LINE | 선의 끝점이 연결되어야 하는데 짧게 끊긴 경우 | 네트워크탐색거리 |
| **오버슛** | LINE | 선의 끝점이 교차점을 넘어 튀어나온 경우 | 네트워크탐색거리 |

**주의사항:**

- 지오메트리 타입에 따라 적용 가능한 검사 항목이 다릅니다
- POINT 타입은 대부분 객체중복과 최소정점개수 검사만 적용됩니다
- LINE 타입은 짧은객체, 언더슛, 오버슛 검사가 주로 적용됩니다
- POLYGON 타입은 슬리버, 작은면적객체, 홀 폴리곤 오류 검사가 추가 적용됩니다
- 각 검사 항목의 기준값은 `geometry_criteria.csv`에서 설정됩니다

### 4. attribute_check.csv

속성 관계 검수를 위한 설정 파일입니다.

**컬럼 구조:**

- `테이블ID`: 테이블의 영문명
- `필드명`: 검수할 필드명
- `검수타입`: 검수 유형 (codelist, range, pattern 등)
- `파라미터`: 검수 파라미터

### 5. relation_check.csv

공간 관계 검수를 위한 설정 파일입니다.

**컬럼 구조:**

- `RuleId`: 규칙 ID
- `Enabled`: 활성화 여부 (Y/N)
- `CaseType`: 케이스 유형 (PointInsidePolygon, LineWithinPolygon, PolygonNotWithinPolygon, LineConnectivity 등)
- `MainTableId`: 주 테이블 ID
- `MainTableName`: 주 테이블명
- `RelatedTableId`: 관련 테이블 ID
- `RelatedTableName`: 관련 테이블명
- `FieldFilter`: 필드 필터 조건 (선택)
- `Tolerance`: 허용 오차 (미터, 선택)
- `Note`: 비고

**예시:**

```csv
RuleId,Enabled,CaseType,MainTableId,MainTableName,RelatedTableId,RelatedTableName,FieldFilter,Tolerance,Note
도로경계면_관계,Y,LineWithinPolygon,TN_RODWAY_BNDRY,도로경계면,TN_RODWAY_CTLN,도로중심선,road_se IN (RDS001|RDS002),0.001,중심선은 경계면을 벗어나면 안 됨
도로중심선_연결성,Y,LineConnectivity,TN_RODWAY_CTLN,도로중심선,TN_RODWAY_CTLN,도로중심선,,1,끝점 1m 이내 연결 확인
```

**Tolerance 설정 안내:**

- `Tolerance` 값이 비어있으면 `geometry_criteria.csv`의 기본값을 사용합니다
- CaseType별 기본값:
  - `LineWithinPolygon`: 선면포함관계허용오차 (기본: 0.001m)
  - `PolygonNotWithinPolygon`: 면면포함관계허용오차 (기본: 0.001m)
  - `LineConnectivity`: 선연결성탐색거리 (기본: 1.0m)

### 6. geometry_criteria.csv

지오메트리 검수 기준값 설정 파일입니다. 모든 지오메트리 검수와 관계 검수에서 사용되는 허용 오차 및 기준값을 정의합니다.

**파일 구조:**

```csv
항목명,값,단위,설명
겹침허용면적,0.001,제곱미터,폴리곤 겹침 허용 면적
최소선길이,0.01,미터,짧은 선 객체 판정 기준
최소폴리곤면적,1.0,제곱미터,작은 면적 객체 판정 기준
자체꼬임허용각도,1.0,도,자체 교차 허용 각도
폴리곤내폴리곤최소거리,0.1,미터,폴리곤 내부 폴리곤 최소 거리
슬리버면적,2.0,제곱미터,슬리버폴리곤 면적 기준
슬리버형태지수,0.1,무차원,슬리버폴리곤 형태지수 기준
슬리버신장률,10.0,무차원,슬리버폴리곤 신장률 기준
스파이크각도임계값,10.0,도,스파이크 검출 각도 임계값
링폐합오차,1e-8,미터,링 폐합 허용 오차
네트워크탐색거리,0.1,미터,언더슛/오버슛 탐색 거리
중복검사허용오차,0.001,미터,중복 지오메트리 판정 허용 오차
선면포함관계허용오차,0.001,미터,선-면 포함 관계 검수 허용 오차
면면포함관계허용오차,0.001,미터,면-면 포함 관계 검수 허용 오차
선연결성탐색거리,1.0,미터,선 연결성 검수 탐색 거리
```

**주요 항목 설명:**

| 항목명 | 기본값 | 용도 | 영향 범위 |
|--------|--------|------|-----------|
| **겹침허용면적** | 0.001m² | 폴리곤 겹침 검수 시 허용 면적 | 지오메트리 검수 |
| **최소선길이** | 0.01m | 짧은 선분 판정 기준 | 지오메트리 검수 |
| **최소폴리곤면적** | 1.0m² | 작은 폴리곤 판정 기준 | 지오메트리 검수 |
| **슬리버면적** | 2.0m² | 슬리버폴리곤 면적 임계값 | 지오메트리 검수 |
| **슬리버형태지수** | 0.1 | 슬리버폴리곤 형태 판정 (4π×면적/둘레²) | 지오메트리 검수 |
| **슬리버신장률** | 10.0 | 슬리버폴리곤 신장률 판정 (가로/세로 비율) | 지오메트리 검수 |
| **스파이크각도임계값** | 10.0° | 스파이크 검출 각도 | 지오메트리 검수 |
| **링폐합오차** | 1e-8m | 폴리곤 링 폐합 허용 오차 | 지오메트리 검수 |
| **네트워크탐색거리** | 0.1m | 언더슛/오버슛 탐색 반경 | 지오메트리 검수 |
| **중복검사허용오차** | 0.001m | 중복 지오메트리 판정 거리 | 지오메트리 검수 |
| **선면포함관계허용오차** | 0.001m | 선이 면에 포함되는지 검사 시 허용 오차 | 관계 검수 (LineWithinPolygon) |
| **면면포함관계허용오차** | 0.001m | 면이 면에 포함되는지 검사 시 허용 오차 | 관계 검수 (PolygonNotWithinPolygon) |
| **선연결성탐색거리** | 1.0m | 선 연결성 검사 시 탐색 거리 | 관계 검수 (LineConnectivity) |

**기준값 수정 방법:**

1. Excel이나 텍스트 에디터로 `geometry_criteria.csv` 파일을 엽니다
2. '값' 컬럼의 숫자를 원하는 기준값으로 수정합니다
3. UTF-8 인코딩으로 저장합니다
4. 애플리케이션을 재시작하면 새로운 기준값이 적용됩니다

**주의사항:**

- 항목명을 변경하면 안 됩니다 (시스템에서 항목명으로 값을 읽음)
- 값은 반드시 숫자여야 합니다
- 과도하게 큰 값이나 작은 값(0 이하)은 검수 결과에 영향을 줄 수 있습니다
- 변경 후에는 테스트 데이터로 검증하는 것을 권장합니다

### 7. codelist.csv

속성 검수에서 사용되는 코드값 목록 파일입니다.

**컬럼 구조:**

- `CodeSetId`: 코드셋 식별자
- `CodeValue`: 코드 값
- `Label`: 코드 설명

**예시:**

```csv
CodeSetId,CodeValue,Label
시설구역경계구분V6,FZB001,중앙행정기관(부)
시설구역경계구분V6,FZB002,중앙행정기관(처)
도로구분,RDS001,고속국도
도로구분,RDS002,일반국도
```

## 설정 파일 수정 방법

1. **Excel이나 텍스트 에디터로 편집**: CSV 파일은 Excel이나 메모장 등으로 편집할 수 있습니다.
2. **인코딩 주의**: 파일을 저장할 때 UTF-8 인코딩을 사용하세요.
3. **컬럼 순서 유지**: 컬럼의 순서를 변경하지 마세요.
4. **필수 컬럼**: 모든 컬럼이 필수이므로 빈 값을 두지 마세요 (Tolerance 등 일부 선택 항목 제외).

## 주의사항

- CSV 파일의 첫 번째 행은 헤더이므로 삭제하지 마세요.
- 테이블ID와 컬럼ID는 테이블의 영문명과 컬럼 영문명으로  고유해야 합니다.
- Y/N 값은 대문자로 입력하세요.
- 파일명을 변경하지 마세요.
- 검수 실행 전에 설정 파일의 유효성을 확인하세요.

## 문제 해결

설정 파일에 오류가 있는 경우 애플리케이션에서 오류 메시지를 표시합니다.
오류 메시지를 참고하여 해당 파일을 수정하세요.

자세한 사용법은 사용자 매뉴얼을 참고하세요.
