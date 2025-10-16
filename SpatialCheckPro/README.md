# 공간정보 검수 시스템 (GeoSpatialValidationSystem)

## 개요
Windows 데스크톱에서 단독으로 실행되는 공간정보 검수 애플리케이션입니다. 
오픈소스 기반으로 개발되며, FileGDB, SHP file, Geopackage 파일을 대상으로 4단계 체계적인 검수를 수행합니다.

## 기술 스택

### 프레임워크
- .NET 8.0
- WPF (Windows Presentation Foundation)
- MVVM 패턴

### 주요 라이브러리
- **CommunityToolkit.Mvvm** (8.4.0) - MVVM 패턴 구현
- **NetTopologySuite** (2.6.0) - 지오메트리 연산 및 토폴로지 분석
- **CsvHelper** (33.1.0) - CSV 파일 처리
- **GDAL** (3.11.3) - 공간정보 파일 읽기/쓰기
- **Microsoft.EntityFrameworkCore.Sqlite** (9.0.8) - 로컬 데이터베이스
- **Microsoft.Extensions.DependencyInjection** (9.0.8) - 의존성 주입

## 프로젝트 구조

```
GeoSpatialValidationSystem/
├── Views/              # WPF 뷰 파일들
├── ViewModels/         # MVVM 뷰모델 클래스들
├── Services/           # 비즈니스 로직 서비스들
├── Models/             # 데이터 모델 클래스들
├── Processors/         # 검수 프로세서 클래스들
├── App.xaml           # 애플리케이션 정의
├── App.xaml.cs        # 애플리케이션 진입점 및 DI 설정
└── MainWindow.xaml    # 메인 윈도우
```

## 4단계 검수 프로세스

1. **1단계: 테이블 검수** - table_check.csv 기반 테이블 리스트, 좌표계, 지오메트리 타입 검증
2. **2단계: 스키마 검수** - schema_check.csv 기반 컬럼 구조, 데이터 타입, PK/FK 검증
3. **3단계: 지오메트리 검수** - geometry_check.csv 기반 중복, 겹침, 꼬임, 슬리버 검사
4. **4단계: 관계 검수** - relation_check.csv 기반 테이블 간 공간 관계 검증

## 빌드 및 실행

### 빌드
```bash
dotnet build
```

### 실행
```bash
dotnet run
```

## 라이선스
이 프로젝트는 오픈소스 라이브러리들을 기반으로 개발되었습니다.
- MIT 라이선스: CommunityToolkit.Mvvm, NetTopologySuite, CsvHelper, Entity Framework Core
- X/MIT 라이선스: GDAL