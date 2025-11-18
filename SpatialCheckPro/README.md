# 국가기본도 FileGDB 검수 시스템 (SpatialCheckPro)

## 개요
SpatialCheckPro는 국가기본도 File Geodatabase(.gdb)를 ISO 19157 품질 요소 기준으로 자동 검수하는 .NET 8 WPF 애플리케이션입니다. GDAL/PROJ 네이티브 번들을 포함하여 오프라인 환경에서도 0~5단계 검수를 단일 실행 파일로 수행할 수 있으며, RuleID·Severity 구조화, QC 오류 GDB 생성, 검수 메트릭 수집까지 지원합니다.

## 주요 기능
- **6단계(0~5) 검수**: 테이블/스키마/지오메트리/관계/속성/종합 단계까지 CSV 기반 규칙을 순차 실행
- **ISO RuleID 관리**: `측정ID (RuleId)`를 ISO 19157 요소와 연결해 Config CSV에 통합 저장
- **GDAL/PROJ 통합**: GDAL 3.10.3 + PROJ 데이터 번들을 포함하고, 한글 경로에서도 `proj.db`를 자동 복사/보정
- **QC 오류 리포트**: QC_ERRORS FileGDB, PDF/CSV 보고서, 배치 처리 중 실시간 진행률 예측
- **배포 자동화**: `배포_스크립트.ps1`로 self-contained publish, GDAL 복사, LICENSE 번들을 한번에 구성
- **사용자 문서**: `사용자_매뉴얼*.md`, `빠른_시작_가이드.md`, RuleID 관리 가이드 등 내장

## 프로젝트 구조
```
SpatialCheckPro/
├── SpatialCheckPro/                # 코어 라이브러리 (Services, Processors, Models, Config)
│   ├── Config/                     # 0~5단계 규칙 CSV, codelist, geometry_criteria
│   ├── Processors/                 # Table/Schema/Geometry/Relation/Attribute 처리기
│   ├── Services/                   # GDAL 스키마, RuleID 관리, 메트릭 수집, 보고서
│   └── Models/                     # 검수 결과/설정/Rule/메트릭 도메인 객체
└── SpatialCheckPro.GUI/            # WPF 클라이언트
    ├── Views/, ViewModels/         # 메인 대시보드·결과 탭·설정 UI
    ├── Services/                   # UI 진입용 Validation Service, GDAL 초기화, DI 구성
    ├── Assets/Icons, Images        # 애플리케이션/회사 로고
    └── App.xaml(.cs)               # 프로그램 진입점, PROJ 환경 변수 세팅
```

## 검수 단계 요약
| 단계 | 설명 | 주요 규칙 파일 |
|------|------|----------------|
| 0단계 | FileGDB 분석, 좌표계/용량/테이블 수 등 메타데이터 추출 | 내부 로직 |
| 1단계 | 필수 테이블·좌표계·지오메트리 타입·FeatureClass 상태 검사 | `Config/1_table_check.csv` |
| 2단계 | 필드 존재 여부, 자료형, 길이, 도메인, PK/FK 일관성 | `Config/2_schema_check.csv` |
| 3단계 | 지오메트리 유효성, Self-intersection, 중복/슬리버, geometry_criteria 룰 | `Config/3_geometry_check.csv`, `geometry_criteria.csv` |
| 4단계 | 테이블 간 공간/속성 관계 검사 (Contains/Within/Overlap 등) | `Config/5_relation_check.csv` |
| 5단계 | 속성 값, 코드 리스트, 필수 표준 속성 및 요약 | `Config/4_attribute_check.csv`, `codelist.csv` |

## 기술 스택
### 프레임워크
- .NET 8.0, WPF, MVVM (CommunityToolkit.Mvvm 8.4)
- Entity Framework Core Sqlite 9.0.9 (로컬 캐시/메트릭 저장)
- Microsoft.Extensions DI/Logging/Hosting

### 핵심 라이브러리
- **GDAL / GDAL.Native 3.10.3** – FileGDB IO, PROJ 9 데이터 포함
- **NetTopologySuite 2.6.0 / GeoJSON 4.0.0** – 지오메트리 연산, JSON 인터페이스
- **Serilog 4.2.0** – 구조화 로깅 및 로그 파일 관리
- **PdfSharp-MigraDoc 6.2.1, iTextSharp.LGPLv2.Core 3.4.21** – PDF/보고서 생성
- **Grpc.Net.Client 2.57.0, Google.Protobuf 3.25.1** – (옵션) gRPC 통신
- **Microsoft.Xaml.Behaviors.Wpf 1.1.122** – UI 인터랙션 확장

## 빌드 & 테스트
```powershell
# Release 빌드
dotnet build SpatialCheckPro.sln -c Release

# (선택) 테스트 실행
dotnet test SpatialCheckPro.sln -c Release
```

## 배포
```powershell
# Self-contained publish + GDAL + LICENSE 번들
pwsh .\배포_스크립트.ps1 -OutputDir .\publish -Configuration Release -Runtime win-x64
```
- 스크립트는 기존 publish 폴더를 삭제 후 재생성하고, `gdal\` 디렉터리, Config CSV, 실행 파일, `LICENSE.txt`, `LICENSES\` 폴더를 포함한 배포 패키지를 생성합니다.

## 라이선스 및 고지
- 배포본에는 `LICENSE.txt`(자체 사용권 고지)와 `LICENSES/` 폴더가 포함되며, MIT/Apache-2.0/BSD-3-Clause/LGPL-2.1/GDAL/PROJ 라이선스 전문과 `THIRD_PARTY_LIST.md`, `NOTICE_Apache2.txt`를 제공합니다.
- iTextSharp(LGPL) 소스는 https://github.com/VahidN/iTextSharp.LGPLv2.Core 에서 입수할 수 있으며, 사용자 교체가 가능합니다.
- 자세한 라이선스 절차는 `LEGAL/` 디렉터리를 참고하세요.

## 지원
- 기술 문의: support@wgsystem.co.kr  
- 라이선스 문의: legal@wgsystem.co.kr  
- 버그/기능 개선 요청은 프로젝트 이슈 트래커 또는 배포 로그를 통해 접수해 주세요.