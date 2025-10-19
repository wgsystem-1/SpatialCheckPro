# SpatialCheckPro 기술 세부사항

## 1. GDAL/OGR 통합

### 1.1 GDAL 초기화
```csharp
// App.xaml.cs의 PROJ 환경 설정
private static void SetupProjEnvironment()
{
    // PostgreSQL PostGIS와의 충돌 방지
    var filteredPaths = paths.Where(p => 
        !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
        !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
        !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase)
    );
    
    // PROJ 환경 변수 설정
    Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath);
    Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath);
    Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
}
```

### 1.2 DataSource 풀링
- **IDataSourcePool**: GDAL DataSource 재사용으로 성능 향상
- 동일 파일에 대한 중복 Open 방지
- 메모리 효율적 관리

## 2. 동적 검수 규칙 시스템

### 2.1 CSV 기반 설정
```
Config/
├── 1_table_check.csv      # 테이블 목록, 좌표계, 지오메트리 타입
├── 2_schema_check.csv     # 컬럼 구조, 데이터 타입, PK/FK
├── 3_geometry_check.csv   # 중복, 겹침, 꼬임, 슬리버 검사
├── 4_attribute_check.csv  # 속성 관계 검사 (93개 규칙)
├── 5_relation_check.csv   # 공간 관계 검사 (20개 규칙)
├── codelist.csv          # 코드 목록
└── geometry_criteria.csv # 지오메트리 임계값 설정
```

### 2.2 동적 규칙 로딩 및 실행 엔진
- **CsvConfigService**: 런타임에 CSV 설정 파일을 읽어 메모리에 로드합니다.
- **ConditionalRuleEngine**: 단순 CSV 규칙을 넘어, 조건에 따라 동적으로 검수 로직을 실행하는 엔진입니다.
- **ExpressionEngine**: 복잡한 속성 관계 검사를 위해 표현식 기반의 규칙을 해석하고 실행합니다.
- 사용자 정의 규칙 추가 및 UI를 통한 실시간 편집을 지원합니다.

## 3. 지오메트리 검증 알고리즘

### 3.1 NetTopologySuite 및 고성능 검증기 활용
- **NetTopologySuite**: 기본적인 지오메트리 연산 및 유효성 검증에 사용됩니다.
- **HighPerformanceGeometryValidator**: 대용량 데이터 처리를 위해 최적화된 별도의 고성능 검증 로직을 구현하여 특정 시나리오에서 NetTopologySuite를 보완하거나 대체합니다.
- **세분화된 검사기**:
    - `PolygonTopologyChecker`: 폴리곤의 링 방향성, 닫힘 여부 등 위상 규칙을 정밀하게 검사합니다.
    - `LineIntersectionChecker`: 라인 객체 간의 교차 또는 자체 교차(self-intersection)를 효율적으로 탐지합니다.
    - `PointInPolygonChecker`: 점-폴리곤 포함 관계를 빠르게 검사하는 데 사용됩니다.

```csharp
// 슬리버 폴리곤 검출 예시
var compactness = 4 * Math.PI * area / (perimeter * perimeter);
if (compactness < threshold)
{
    // 슬리버 폴리곤으로 분류
}
```

### 3.2 토폴로지 검사
- 자체 교차 (Self-intersection)
- 무효한 지오메트리 (Invalid geometry)
- 닫히지 않은 폴리곤
- 중복 정점 (Duplicate vertices)

## 4. QC_ERRORS 시스템

### 4.1 오류 분류 체계
```
ErrType (오류 유형)
├── GEOM   : 지오메트리 오류
├── SCHEMA : 스키마 오류
├── REL    : 관계 오류
└── ATTR   : 속성 오류

Severity (심각도)
├── CRIT   : 치명적 (레벨 4)
├── MAJOR  : 주요 (레벨 3)
├── MINOR  : 경미 (레벨 2)
└── INFO   : 정보 (레벨 1)

Status (상태)
├── OPEN      : 열림
├── FIXED     : 수정됨
├── IGNORED   : 무시됨
└── FALSE_POS : 오탐
```

### 4.2 오류 코드 체계
- **DUP001**: 중복 지오메트리
- **OVL001**: 겹치는 객체
- **SLF001**: 자체 교차
- **SLV001**: 슬리버 폴리곤
- **NUL001**: NULL 지오메트리
- **INV001**: 무효한 지오메트리

## 5. 성능 최적화 기법

### 5.1 고성능 모드 (GDB to SQLite)
- 대용량 FileGDB(.gdb)를 검수 전에 임시 SQLite 파일로 변환합니다.
- GDAL의 FileGDB 드라이버보다 월등히 빠른 SQLite의 네이티브 쿼리 성능을 활용하여, 특히 속성 및 관계 검사에서 속도를 극대화합니다.
```csharp
// GdbToSqliteConverter.cs
public async Task<string> ConvertAsync(string gdbPath)
{
    // ... GDB 스키마 읽기 및 SQLite 테이블 생성 ...
    // ... 데이터 이관 로직 ...
    return tempSqlitePath;
}
```

### 5.2 다차원 병렬 처리 및 모니터링
- **단계 병렬 처리**: `StageParallelProcessingManager`를 사용하여 상호 의존성이 없는 검수 단계(예: 테이블 검사, 속성 관계 검사)를 동시에 실행합니다.
- **데이터 병렬 처리**: `AdvancedParallelProcessingManager`를 사용하여 단일 검수 작업 내에서 테이블 또는 피처(Feature) 청크를 여러 스레드에 분배하여 처리합니다.
- **병렬 처리 모니터링**: `ParallelPerformanceMonitor`와 `ParallelErrorHandler`를 통해 병렬 작업의 성능을 추적하고 발생하는 오류를 안정적으로 관리합니다.

### 5.3 동적 리소스 모니터링 및 최적화
- `CentralizedResourceMonitor` 서비스가 애플리케이션 시작 및 검수 실행 시 시스템의 CPU 코어 수, 가용 메모리, 현재 시스템 부하를 분석합니다.
- `SystemResourceAnalyzer`를 통해 분석된 결과를 바탕으로 최적의 병렬 스레드 수, 데이터 처리 배치 사이즈, 메모리 사용 한도를 동적으로 결정하여 시스템 안정성을 유지하면서 성능을 최대화합니다.
- `AdvancedMemoryManager`는 검수 과정에서 메모리 사용량을 추적하고, 필요 시 가비지 컬렉션을 유도하거나 캐시를 비워 메모리 부족 문제를 예방합니다.
```csharp
// SimpleValidationService.cs
private void ApplyOptimalSettings(SystemResourceInfo resourceInfo)
{
    _performanceSettings.MaxDegreeOfParallelism = resourceInfo.RecommendedMaxParallelism;
    _performanceSettings.BatchSize = resourceInfo.RecommendedBatchSize;
    // ...
}
```

### 5.4 데이터 스트리밍 및 캐싱
- 모든 파일 I/O 및 검수 로직은 `async/await` 패턴을 사용하여 UI 스레드의 블로킹을 방지합니다.
- **스트리밍 처리**: `StreamingDataProcessor`를 사용하여 대용량 데이터를 전체 메모리에 로드하는 대신 작은 배치 단위로 처리하여 메모리 사용량을 최소화합니다.
- **지능형 캐싱**: `DataCacheService`와 `LruCache`를 구현하여 자주 접근하는 데이터(예: 도메인 코드, 설정값)를 메모리에 캐싱함으로써 디스크 I/O를 줄이고 응답 속도를 향상시킵니다.

### 5.5 다중 전략 공간 인덱싱 및 DataSource 풀링
- **다중 공간 인덱스**: `SpatialIndexManager`를 통해 데이터의 특성과 검수 유형에 따라 최적의 공간 인덱싱 전략(R-Tree, Quad-Tree, Grid-based)을 선택적으로 사용합니다. 이는 공간 쿼리(겹침, 포함 등)의 성능을 극대화합니다.
- `DataSourcePool`을 통해 GDAL의 `DataSource` 객체를 재사용하여 반복적인 파일 열기/닫기 오버헤드를 줄입니다.

## 6. 보고서 생성

### 6.1 PDF 보고서 (iTextSharp)
```csharp
// 표지
document.Add(new Paragraph("공간데이터 검수 보고서"));

// 요약 정보
PdfPTable summaryTable = new PdfPTable(2);
summaryTable.AddCell("검수 대상");
summaryTable.AddCell(targetFile);

// 단계별 결과
foreach (var stage in stages)
{
    chapter.Add(new Section($"{stage.Name} 검수 결과"));
    // 오류 목록 추가
}
```

### 6.2 Excel 보고서 (EPPlus)
- 요약 시트
- 단계별 상세 시트
- 오류 목록 시트
- 통계 차트

### 6.3 HTML 보고서
- Chart.js와 같은 라이브러리를 활용하여 동적이고 상호작용이 가능한 웹 기반 보고서를 생성합니다.
- 사용자는 브라우저에서 필터링, 정렬, 확대/축소 등의 기능을 통해 검수 결과를 심층적으로 분석할 수 있습니다.

## 7. 오류 시각화 및 편집 (GUI)

### 7.1 지도 기반 오류 시각화
- **ErrorLayerService**: 검수 과정에서 발생한 지오메트리 관련 오류들을 WPF UI의 지도 위에 시각적으로 렌더링합니다.
- **ErrorRenderingService**: 오류의 심각도(Severity)나 상태(Status)에 따라 각기 다른 심볼이나 색상으로 오류를 표현하여 직관성을 높입니다.
- **ErrorClusteringService**: 특정 지역에 오류가 밀집된 경우, 이를 클러스터링하여 지도의 가독성을 향상시킵니다.

### 7.2 대화형 오류 분석 및 편집
- **MapInteractionService**: 사용자가 지도를 확대/축소/이동하며 오류를 탐색할 수 있도록 지원합니다.
- **ErrorSelectionService**: 지도에 표시된 오류를 클릭하여 상세 정보를 확인하고, 관련 피처를 하이라이트합니다.
- **GeometryEditToolService**: 지도 위에서 직접 오류가 발생한 지오메트리의 정점을 추가, 삭제, 이동하는 등 간단한 편집 및 수정 기능을 제공하여 즉각적인 데이터 교정을 가능하게 합니다.

## 8. 로깅 및 감사 시스템

### 8.1 Serilog 기반 구조화된 로깅
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("Logs/app-.log", 
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();
```
```csharp
_logger.LogInformation("검수 시작: {FilePath}, 단계: {Stage}", 
    filePath, stageName);

_logger.LogError(ex, "검수 실패: {ErrorCode}", errorCode);
```

### 8.2 감사 추적
- **AuditLogService**: 사용자의 주요 행위(예: 검수 시작, 설정 변경, 오류 수정) 및 시스템의 중요 이벤트(예: 리소스 한계 도달)를 별도의 로그 파일이나 데이터베이스에 기록하여 추적 및 분석이 가능하도록 합니다.

## 9. 데이터베이스 스키마

### 9.1 Entity Framework Core 마이그레이션
```csharp
// ValidationDbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // 복합 인덱스
    modelBuilder.Entity<ValidationErrorEntity>()
        .HasIndex(e => new { e.TableName, e.Severity })
        .HasDatabaseName("IX_ValidationErrors_TableName_Severity");
        
    // JSON 변환
    entity.Property(e => e.MetadataJson)
        .HasConversion(
            v => JsonSerializer.Serialize(v, null),
            v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, null)
        );
}
```

## 10. UI/UX 기술

### 10.1 MVVM 바인딩
```xml
<!-- DataGrid 바인딩 예시 -->
<DataGrid ItemsSource="{Binding ValidationResults}"
          SelectedItem="{Binding SelectedResult}">
    <DataGrid.Columns>
        <DataGridTextColumn Header="오류 코드" 
                           Binding="{Binding ErrorCode}"/>
    </DataGrid.Columns>
</DataGrid>
```

### 10.2 값 변환기
- **StageStatusToColorConverter**: 상태별 색상
- **StageStatusToIconConverter**: 상태별 아이콘
- **RowIndexConverter**: 행 번호 표시

## 11. 보안 고려사항

### 11.1 파일 경로 및 데이터 보호
- **FileSecurityService**: 파일 경로의 유효성을 검증하고(Path Traversal 방지), 허용된 확장자(`.gdb`, `.shp`, `.gpkg`)만 처리하도록 제한합니다.
- **DataProtectionService**: 필요한 경우, 보고서나 임시 파일에 포함된 민감 정보를 암호화하는 기능을 제공합니다.
- **SecurityMonitoringService**: 파일 접근, 권한 변경 등 보안 관련 이벤트를 감지하고 로깅합니다.

### 11.2 SQL 인젝션 방지
- Entity Framework 파라미터화 쿼리 사용
- 사용자 입력 검증

## 12. 확장성 설계

### 12.1 플러그인 아키텍처
```csharp
public interface IValidationPlugin
{
    string Name { get; }
    string Version { get; }
    Task<ValidationResult> ValidateAsync(string filePath);
}
```

### 12.2 커스텀 검수 규칙
```csharp
public interface ICustomRule
{
    string RuleId { get; }
    string Description { get; }
    Task<RuleResult> EvaluateAsync(Feature feature);
}
```

### 12.3 데이터 프로바이더 추상화
- 데이터 소스에 대한 접근을 `IValidationDataProvider` 인터페이스로 추상화했습니다.
- 이를 통해 FileGDB를 직접 읽는 `GdbDataProvider`와 SQLite로 변환하여 읽는 `SqliteDataProvider`를 유연하게 교체할 수 있습니다.
- 향후 PostGIS, Oracle Spatial 등 다른 데이터 소스를 지원하기 위한 확장이 용이합니다.
```csharp
public interface IValidationDataProvider
{
    Task InitializeAsync(string dataSourcePath);
    Task<List<string>> GetTableNamesAsync();
    Task<TableInfo> GetTableInfoAsync(string tableName);
    // ...
    void Close();
}
```

### 12.4 일관된 프로세서 인터페이스
모든 검수 프로세서는 일관성을 위해 인터페이스를 구현합니다:
```csharp
public interface IAttributeCheckProcessor
{
    void LoadCodelist(string? codelistPath);
    Task<List<ValidationError>> ValidateAsync(
        string gdbPath, 
        List<AttributeCheckConfig> rules, 
        CancellationToken token = default);
}
```

## 13. 테스트 전략

### 13.1 단위 테스트
- 각 Processor별 테스트
- 서비스 로직 테스트
- 유틸리티 함수 테스트

### 13.2 통합 테스트
- 전체 검수 프로세스 테스트
- 데이터베이스 연동 테스트
- 보고서 생성 테스트

### 13.3 성능 테스트
- 대용량 파일 처리
- 메모리 사용량 모니터링
- 처리 시간 측정
