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
- `지오메트리타입`: 지오메트리 유형
- `중복`: 중복 지오메트리 검사 여부 (Y/N)
- `겹침`: 겹침 지오메트리 검사 여부 (Y/N)
- `꼬임`: 꼬임 지오메트리 검사 여부 (Y/N)
- `슬리버`: 슬리버 폴리곤 검사 여부 (Y/N)

**예시:**
```csv
테이블ID,테이블명칭,중복,겹침,꼬임,슬리버
TBL001,건물,Y,Y,Y,Y
TBL002,도로,Y,Y,Y,N
```

### 4. relation_check.csv
관계 검수를 위한 설정 파일입니다.

**컬럼 구조:**
- `주테이블ID`: 주 테이블의 영문명
- `주테이블명칭`: 주 테이블의 한글명
- `관계테이블ID`: 관계 테이블의 영문명
- `관계테이블명칭`: 관계 테이블의 한글명
- `선면포함`: 선이 면에 포함되는지 검사 여부 (Y/N)
- `점면포함`: 점이 면에 포함되는지 검사 여부 (Y/N)
- `면면포함`: 면이 면에 포함되는지 검사 여부 (Y/N)

**예시:**
```csv
주테이블ID,주테이블명칭,관계테이블ID,관계테이블명칭,선면포함,점면포함,면면포함
TBL002,도로,TBL004,행정구역,Y,N,N
TBL003,지점,TBL004,행정구역,N,Y,N
```

## 설정 파일 수정 방법

1. **Excel이나 텍스트 에디터로 편집**: CSV 파일은 Excel이나 메모장 등으로 편집할 수 있습니다.
2. **인코딩 주의**: 파일을 저장할 때 UTF-8 인코딩을 사용하세요.
3. **컬럼 순서 유지**: 컬럼의 순서를 변경하지 마세요.
4. **필수 컬럼**: 모든 컬럼이 필수이므로 빈 값을 두지 마세요.

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