# SpatialCheckPro 시스템 아키텍처 분석

## 1. 시스템 개요

### 1.1 프로젝트 소개
**SpatialCheckPro**는 국가기본도 DB의 공간 데이터를 검수하는 Windows 데스크톱 애플리케이션입니다. File Geodatabase(FGDB), Shapefile, GeoPackage 형식의 공간 데이터에 대해 6단계의 체계적인 검수를 수행합니다.

### 1.2 주요 목적
- 공간 데이터의 품질 보증
- 데이터 무결성 검증
- 토폴로지 및 관계 검증
- 검수 결과의 체계적 관리

## 2. 기술 스택

### 2.1 프레임워크 및 런타임
- **.NET 8.0**: 최신 .NET 런타임 환경
- **WPF (Windows Presentation Foundation)**: UI 프레임워크
- **C# 12**: 프로그래밍 언어

### 2.2 핵심 라이브러리
| 라이브러리 | 버전 | 용도 |
|-----------|------|------|
| GDAL | 3.10.3 | 공간 데이터 읽기/쓰기 |
| NetTopologySuite | 2.6.0 | 지오메트리 연산 및 토폴로지 분석 |
| Entity Framework Core SQLite | 9.0.9 | 로컬 데이터베이스 관리 |
| CsvHelper | 33.1.0 | CSV 설정 파일 처리 |
| iTextSharp.LGPLv2.Core | 3.4.21 | PDF 보고서 생성 |
| Microsoft.Extensions.Logging + FileLoggerProvider | - | 구조화된 로깅(UTF-8 파일 로깅) |

### 2.3 GUI 관련 라이브러리
- **Xceed.Wpf.Toolkit**: 4.5.22477.12540 - 확장 WPF 컨트롤
- **Microsoft.VisualBasic**: 10.3.0 - InputBox 등 유틸리티

## 3. 시스템 아키텍처

### 3.1 계층 구조
```
┌─────────────────────────────────────────────────────┐
│                   Presentation Layer                 │
│              (SpatialCheckPro.GUI - WPF)            │
├─────────────────────────────────────────────────────┤
│                  Application Layer                   │
│         (Services, ViewModels, Controllers)         │
├─────────────────────────────────────────────────────┤
│                   Business Logic Layer               │
│      (SpatialCheckPro - Processors, Services)      │
├─────────────────────────────────────────────────────┤
│                    Data Access Layer                 │
│         (Entity Framework, GDAL, CSV Reader)        │
├─────────────────────────────────────────────────────┤
│                    Infrastructure                    │
│         (SQLite DB, File System, Logging)          │
└─────────────────────────────────────────────────────┘
```

### 3.2 프로젝트 구조
```
SpatialCheckPro/
├── SpatialCheckPro/              # 핵심 비즈니스 로직 라이브러리
│   ├── Config/                   # CSV 설정 파일
│   ├── Constants/                # 상수 정의
│   ├── Data/                     # 데이터베이스 컨텍스트
│   ├── Exceptions/               # 커스텀 예외
│   ├── Extensions/               # 확장 메서드
│   ├── Models/                   # 도메인 모델
│   │   ├── Config/              # 설정 모델
│   │   ├── Entities/            # 데이터베이스 엔티티
│   │   └── Enums/               # 열거형
│   ├── Processors/               # 검수 프로세서
│   └── Services/                 # 비즈니스 서비스
│
└── SpatialCheckPro.GUI/          # WPF GUI 애플리케이션
    ├── Views/                    # XAML 뷰
    ├── ViewModels/               # MVVM 뷰모델
    ├── Services/                 # UI 서비스
    ├── Converters/               # 값 변환기
    └── Styles/                   # UI 스타일
```

## 4. 핵심 설계 패턴

### 4.1 아키텍처 패턴
- **MVVM (Model-View-ViewModel)**: GUI 레이어에서 사용
- **Layered Architecture**: 전체 시스템 구조
- **Repository Pattern**: 데이터 접근 계층
- **Dependency Injection**: 의존성 관리
- **Strategy Pattern**: 검수 프로세서 구현

### 4.2 설계 원칙
- **SOLID 원칙** 준수
- **관심사의 분리** (Separation of Concerns)
- **DRY** (Don't Repeat Yourself)
- **KISS** (Keep It Simple, Stupid)

## 5. 핵심 컴포넌트

### 5.1 검수 프로세서 (Processors)
```
ITableCheckProcessor      → TableCheckProcessor
ISchemaCheckProcessor     → SchemaCheckProcessor  
IGeometryCheckProcessor   → GeometryCheckProcessor
IRelationCheckProcessor   → RelationCheckProcessor
IAttributeCheckProcessor  → AttributeCheckProcessor
```

### 5.2 주요 서비스
#### 5.2.1 검수 오케스트레이션 서비스
- **SimpleValidationService**: 전체 검수 프로세스를 총괄하는 핵심 서비스. 각 검수 단계를 조율하고 진행률을 보고합니다. (`SpatialCheckPro.GUI` 프로젝트에 위치)

#### 5.2.2 핵심 로직 및 데이터 서비스 (업데이트)
- **RelationErrorsIntegrator**: Stage 4(REL), Stage 5(ATTR_REL) 변환 시 원본 FGDB에서 지오메트리를 직접 추출(`ExtractGeometryFromSourceAsync`)하여 X/Y/GeometryWKT를 채워 `QC_Errors_Point` 저장률을 개선합니다.
- **SpatialIndexService**: 겹침 검사 시 교차 지오메트리를 `OverlapResult.IntersectionGeometry`로 반환합니다.
- **HighPerformanceGeometryValidator**: Overlap에서 교차 지오메트리 중심 좌표와 WKT를 사용해 오류 위치 정확도를 높입니다(없으면 대상 피처 중심으로 폴백).

#### 5.2.3 성능 및 자원 관리 서비스
- **StageParallelProcessingManager**: 독립적인 검수 단계를 병렬로 실행하여 전체 검수 시간을 단축합니다.
- **AdvancedParallelProcessingManager**: 파일/단계/테이블/규칙 레벨 병렬 처리와 IO/CPU 유형별 병렬도 산정, 상위에서 할당한 `operationId`를 사용해 일관된 진행률/성능 집계를 제공합니다.
- **CentralizedResourceMonitor / SystemResourceAnalyzer**: 시스템의 CPU 및 메모리 사용량을 모니터링하여 최적의 병렬 처리 수준과 배치 크기를 동적으로 결정합니다.
- **AdvancedMemoryManager**: 메모리 사용량을 추적하고 최적화하여 대용량 파일 처리 시 안정성을 확보합니다.
- **DataSourcePool**: GDAL DataSource 객체를 풀링하여 재사용함으로써 파일 I/O 오버헤드를 줄입니다.
- **SpatialIndexManager**: 데이터 특성에 맞춰 R-Tree, Quad-Tree 등 최적의 공간 인덱스를 선택 및 관리하여 공간 쿼리 성능을 극대화합니다.
- **DataCacheService / LruCache**: 자주 사용하는 데이터를 캐싱하여 디스크 접근을 최소화합니다.

#### 5.2.4 보고서 및 감사 서비스
- **ReportService**: 검수 결과를 종합하여 보고서 생성을 총괄합니다. (`PdfReportService`, `HtmlReportService` 포함. Excel 보고서는 공식적으로 지원하지 않습니다.)
- **AuditLogService**: 사용자의 주요 행위와 시스템 이벤트를 기록하여 추적 및 감사를 지원합니다.

#### 5.2.5 GUI 특화 서비스
현재 WPF GUI에는 지도 뷰가 포함되어 있지 않습니다. 오류 위치 확인은 외부 GIS(QGIS/ArcGIS) 연계로 진행하는 워크플로우를 권장합니다(원본 FGDB와 QC FGDB 동시 오픈, `SourceClass/SourceOID` 선택, `QC_Errors_Point` 참조 등).

### 5.3 데이터 모델
#### 5.3.1 검수 결과 모델
- **ValidationResult**: 전체 검수 결과
- **CheckResult**: 개별 검사 결과
- **QcError**: QC 오류 정보
- **ValidationError**: 검수 오류

#### 5.3.2 설정 모델
- **TableCheckConfig**: 테이블 검수 설정
- **SchemaCheckConfig**: 스키마 검수 설정
- **GeometryCheckConfig**: 지오메트리 검수 설정
- **RelationCheckConfig**: 관계 검수 설정
- **AttributeCheckConfig**: 속성 검수 설정

## 6. 검수 프로세스 흐름

### 6.1 6단계 검수 체계
```
0단계: FileGDB 완전성 검사
   ├─ 디렉터리/확장자 확인
   ├─ 코어 시스템 테이블 확인
   └─ .gdbtable/.gdbtablx 페어 확인

1단계: 테이블 검사 (1_table_check.csv)
   ├─ 테이블 존재 여부
   ├─ 좌표계 검증
   └─ 지오메트리 타입 검증

2단계: 스키마 검사 (2_schema_check.csv)
   ├─ 컬럼 구조 검증
   ├─ 데이터 타입 검증
   └─ PK/FK 관계 검증

3단계: 지오메트리 검사 (3_geometry_check.csv)
   ├─ 중복 지오메트리
   ├─ 겹침 검사
   ├─ 꼬임 검사
   └─ 슬리버 폴리곤

4단계: 속성 관계 검사 (4_attribute_check.csv)
   └─ 93개 규칙 기반 속성 검증

5단계: 공간 관계 검사 (5_relation_check.csv)
   └─ 20개 규칙 기반 공간 관계 검증
```

### 6.2 데이터 흐름
```
사용자 입력
    ↓
FileGDB 선택
    ↓
검수 설정 로드 (CSV)
    ↓
SimpleValidationService
    ↓
각 단계별 Processor 실행 (순차 또는 병렬)
    ↓
QcError 생성 및 저장
    ↓
검수 결과 집계
    ↓
보고서 생성 (PDF/HTML)
```

## 7. 데이터 관리

### 7.1 SQLite 데이터베이스
- **ValidationDbContext**: Entity Framework Core 컨텍스트
- 주요 테이블:
  - ValidationResults: 검수 결과
  - StageResults: 단계별 결과
  - CheckResults: 개별 검사 결과
  - ValidationErrors: 검수 오류
  - SpatialFiles: 공간 파일 정보

### 7.2 설정 관리
- CSV 파일 기반 검수 규칙 설정
- 동적 로드 및 갱신 지원
- 사용자 정의 규칙 추가 가능

## 8. 의존성 주입 구조

### 8.1 서비스 등록 (App.xaml.cs)
```csharp
// 중앙 DI 구성으로 이전됨: DependencyInjectionConfigurator.ConfigureServices(services)
// 핵심 서비스 예시
services.AddSingleton<CsvConfigService>();
services.AddSingleton<QcErrorService>();
services.AddTransient<GdbDataProvider>();
services.AddTransient<SqliteDataProvider>();
services.AddSingleton<PerformanceSettings>(sp =>
    sp.GetRequiredService<IConfigurationFactory>().CreateDefaultPerformanceSettings());
services.AddSingleton<SystemResourceAnalyzer>();
services.AddLogging(b =>
{
    b.AddConsole();
    b.AddProvider(new FileLoggerProvider()); // UTF-8 BOM, 공유 읽기
});
```

## 9. 보안 및 성능 고려사항

### 9.1 보안
- **FileSecurityService**: 파일 경로를 검증하고(Path Traversal 방지) 허용된 파일 확장자만 처리합니다.
- **SecurityMonitoringService**: 파일 접근, 권한 변경 등 보안 관련 활동을 모니터링하고 로깅합니다.
- **DataProtectionService**: 민감 데이터를 암호화하는 기능을 제공합니다.

### 9.2 성능 최적화
- **고성능 모드**: 대용량 FileGDB를 임시 SQLite 데이터베이스로 변환하여 읽기 및 공간 쿼리 성능을 극대화합니다.
- **다차원 병렬 처리**:
  - **단계 병렬 처리**: 서로 의존성이 없는 검수 단계(예: 테이블 검사, 속성 검사)를 동시에 실행하여 전체 검수 시간을 단축합니다.
  - **데이터 병렬 처리**: 단일 검수 단계 내에서 테이블 또는 피처 청크를 여러 스레드에서 병렬로 처리합니다.
- **동적 자원 할당**: `CentralizedResourceMonitor`가 시스템의 CPU 및 메모리 가용량을 실시간으로 분석하여, 병렬 처리 스레드 수와 데이터 처리 배치 크기를 동적으로 조절합니다.
- **다중 전략 공간 인덱싱**: `SpatialIndexManager`를 통해 데이터 특성에 따라 R-Tree, Quad-Tree 등 최적의 공간 인덱싱 전략을 동적으로 선택하여 공간 쿼리 성능을 최적화합니다.
- **지능형 캐싱**: `DataCacheService` (LRU 알고리즘 기반)를 통해 자주 사용하는 설정이나 도메인 코드 데이터를 메모리에 캐시하여 디스크 I/O를 최소화합니다.
- **데이터 프로바이더 추상화**: `IValidationDataProvider` 인터페이스를 통해 데이터 소스(FileGDB, SQLite 등)를 추상화하여, 데이터 접근 방식을 유연하게 전환합니다.
- **DataSource 풀링**: GDAL의 `DataSource` 객체를 재사용하여 파일 I/O 오버헤드를 최소화합니다.
- **비동기 처리 (async/await)**: UI 스레드가 차단되지 않도록 모든 검수 및 파일 처리 작업을 비동기로 수행합니다.

## 10. 확장성 및 유지보수성

### 10.1 확장 가능한 설계
- 인터페이스 기반 프로세서
- **동적 규칙 엔진**: `ConditionalRuleEngine`을 통해 코드 변경 없이 새로운 검수 규칙(표현식, 조건부 로직)을 추가할 수 있습니다.
- 플러그인 구조 지원 가능

### 10.2 유지보수성
- 명확한 계층 분리
- 의존성 주입으로 느슨한 결합
- 상세한 로깅 시스템
- 단위 테스트 가능한 구조

## 11. 주요 특징

1. **포괄적인 검수**: 6단계의 체계적 검수 프로세스
2. **유연한 설정**: CSV 기반 검수 규칙 설정
3. **성능 최적화**: GDAL 네이티브 성능 활용
4. **보고서**: PDF/HTML 지원
5. **오류 추적**: QC_ERRORS 시스템으로 체계적 관리
6. **확장 가능**: 새로운 검수 규칙 쉽게 추가

## 12. 향후 개선 방향

1. **웹 기반 버전** 개발 고려
2. **실시간 협업** 기능 추가
3. **AI 기반 오류 예측** 기능
4. **더 많은 공간 데이터 형식** 지원
5. **클라우드 저장소** 연동
