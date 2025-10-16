현재 개발중인 프로젝트에서 오류로 검출된 객체 및 사항들을 저장하는 방식에 대한 설명입니다.

QC_ERRORS` 시스템의 테이블들(`QC_errors_Nogeom`, `QC_errors_point`, `QC_errors_line`, `QC_errors_polygon`)과 검수 이력을 관리하는 `QC_Runs` 테이블에 대한 스키마, C# 모델, 그리고 데이터 저장 방식에 대해 상세히 정리해 드리겠습니다.

`QC_ERRORS` 시스템을 직접 관리하는 `QcErrorService.cs` 파일의 코드를 분석하겠습니다.

[1 tool called]

`QcErrorService.cs`를 분석해보니, 실제 데이터베이스 상호작용(테이블 생성, 데이터 CRUD)은 `QcErrorDataService`에 위임되어 있습니다. `QcErrorService`는 비즈니스 로직과 데이터 변환에 집중하고 있네요.

따라서 `QcErrorDataService.cs` 파일을 열어 실제 스키마 정의와 저장 방식을 확인해야 합니다. 이 파일에 테이블을 생성하는 SQL 문이나 OGR 레이어 생성 코드가 포함되어 있을 것입니다.

[1 tool called]

`QcErrorDataService.cs`를 확인하니, 스키마 생성 로직은 또 다른 서비스인 `FgdbSchemaService`로 분리되어 있네요. 이제 `FgdbSchemaService.cs`를 열어 최종적으로 스키마 정의를 확인하겠습니다.

[1 tool called]

`FgdbSchemaService.cs`에서 모든 테이블의 스키마 정의를 찾았습니다. 이 정보를 바탕으로 요청하신 내용을 완벽하게 정리해 드리겠습니다.

### **QC\_Errors 및 QC\_Runs 시스템 분석**

#### **1. 저장 방식**

*   **저장 위치**: 검수 대상 File Geodatabase(`.gdb`) 파일 내부에 직접 생성됩니다. 별도의 SQLite 데이터베이스나 외부 파일에 저장되는 것이 아니라, 원본 데이터와 함께 관리됩니다.
*   **생성 시점**: `SimpleValidationService`가 검수를 시작할 때 `InitializeQcErrorsDatabaseAsync` 메서드를 호출하여 `FgdbSchemaService`를 통해 관련 테이블과 피처 클래스(Feature Class)가 있는지 확인하고, **없으면 자동으로 생성**합니다.
*   **데이터 추가**: 검수 과정에서 오류가 발견되면, `QcErrorService`가 해당 오류 정보를 `QcError` 모델 객체로 변환한 뒤, `QcErrorDataService`를 통해 지오메트리 유형에 맞는 테이블(Point, Line, Polygon, NoGeom)에 새로운 피처(레코드)로 추가합니다.

#### **2. 데이터 모델**

C# 코드에서는 모든 오류 정보를 `SpatialCheckPro.Models.QcError` 클래스 객체로 우선 처리합니다. 이 모델은 데이터베이스 스키마와 거의 1:1로 대응되며, 데이터를 저장하거나 조회할 때 사용됩니다. `QC_Runs` 정보는 `SpatialCheckPro.Models.QcRun` 클래스를 사용합니다.

#### **3. 테이블 및 피처 클래스 상세**

##### **3.1 `QC_Runs` (Table)**

검수 실행 이력을 관리하는 일반 테이블입니다.

*   **스키마 (Fields)**
    | 필드명           | 데이터 타입 | 길이/설명                                   |
    | :--------------- | :---------- | :------------------------------------------ |
    | `GlobalID`       | `String`    | 38 (검수 실행의 고유 ID, GUID)              |
    | `RunName`        | `String`    | 256 (실행 이름)                             |
    | `TargetFilePath` | `String`    | 512 (검수한 GDB 파일 경로)                  |
    | `RulesetVersion` | `String`    | 32 (적용된 검수 규칙 버전)                  |
    | `StartTimeUTC`   | `DateTime`  | 검수 시작 시각 (UTC)                        |
    | `EndTimeUTC`     | `DateTime`  | 검수 종료 시각 (UTC)                        |
    | `ExecutedBy`     | `String`    | 64 (실행한 사용자)                          |
    | `Status`         | `String`    | 16 (상태: `Running`, `Completed`, `Failed`) |
    | `TotalErrors`    | `Integer`   | 총 발견된 오류 수                           |
    | `TotalWarnings`  | `Integer`   | 총 발견된 경고 수                           |
    | `ResultSummary`  | `String`    | 4096 (검수 결과 요약)                       |
    | `ConfigInfo`     | `String`    | 2048 (사용된 설정 정보, JSON)               |
    | `CreatedUTC`     | `DateTime`  | 레코드 생성 시각 (UTC)                      |
    | `UpdatedUTC`     | `DateTime`  | 레코드 마지막 수정 시각 (UTC)               |

##### **3.2 공통 오류 스키마**

`QC_Errors_`로 시작하는 모든 피처 클래스(`Point`, `Line`, `Polygon`)와 테이블(`NoGeom`)은 아래의 공통 필드 구조를 가집니다.

*   **스키마 (Fields)**
    | 필드명           | 데이터 타입 | 길이/설명                                       |
    | :--------------- | :---------- | :---------------------------------------------- |
    | `GlobalID`       | `String`    | 38 (오류의 고유 ID, GUID)                       |
    | `ErrType`        | `String`    | 16 (오류 유형: `GEOM`, `ATTR`, `REL`, `SCHEMA`) |
    | `ErrCode`        | `String`    | 32 (세부 오류 코드: `DUP001`, `OVL001` 등)      |
    | `Severity`       | `String`    | 16 (심각도: `CRIT`, `MAJOR`, `MINOR`, `INFO`)   |
    | `Status`         | `String`    | 16 (상태: `OPEN`, `FIXED`, `IGNORED`)           |
    | `RuleId`         | `String`    | 64 (적용된 검수 규칙 ID)                        |
    | `SourceClass`    | `String`    | 128 (오류가 발생한 피처 클래스/테이블명)        |
    | `SourceOID`      | `Integer64` | 오류가 발생한 피처의 고유 ID (Object ID)        |
    | `SourceGlobalID` | `String`    | 38 (오류 피처의 GlobalID, 있는 경우)            |
    | `X`              | `Real`      | 오류 위치의 대표 X 좌표 (EPSG:5179)             |
    | `Y`              | `Real`      | 오류 위치의 대표 Y 좌표 (EPSG:5179)             |
    | `GeometryWKT`    | `String`    | 8192 (오류 지오메트리의 WKT 표현)               |
    | `GeometryType`   | `String`    | 32 (지오메트리 타입: `POINT`, `LINESTRING` 등)  |
    | `ErrorValue`     | `String`    | 256 (측정된 실제 오류 값)                       |
    | `ThresholdValue` | `String`    | 256 (판단 기준이 된 임계값)                     |
    | `Message`        | `String`    | 1024 (오류 상세 메시지)                         |
    | `DetailsJSON`    | `String`    | 4096 (추가 정보, JSON 형식)                     |
    | `RunID`          | `String`    | 38 (`QC_Runs`의 `GlobalID`와 연결되는 외래 키)  |
    | `CreatedUTC`     | `DateTime`  | 레코드 생성 시각 (UTC)                          |
    | `UpdatedUTC`     | `DateTime`  | 레코드 마지막 수정 시각 (UTC)                   |
    | `Assignee`       | `String`    | 64 (오류 처리 담당자)                           |

##### **3.3 `QC_Errors_Point` (Feature Class)**

점(Point) 지오메트리를 가진 오류를 저장합니다.
*   **지오메트리 타입**: `wkbPoint`
*   **좌표계**: EPSG:5179
*   **스키마**: 위의 **공통 오류 스키마**와 동일합니다.

##### **3.4 `QC_Errors_Line` (Feature Class)**

선(Line) 지오메트리를 가진 오류를 저장합니다.
*   **지오메트리 타입**: `wkbLineString`
*   **좌표계**: EPSG:5179
*   **스키마**: 위의 **공통 오류 스키마**와 동일합니다.

##### **3.5 `QC_Errors_Polygon` (Feature Class)**

면(Polygon) 지오메트리를 가진 오류를 저장합니다.
*   **지오메트리 타입**: `wkbPolygon`
*   **좌표계**: EPSG:5179
*   **스키마**: 위의 **공통 오류 스키마**와 동일합니다.

##### **3.6 `QC_Errors_NoGeom` (Table)**

지오메트리가 없는 오류(예: 속성 오류, 스키마 오류)나 지오메트리 정보를 특정할 수 없는 오류를 저장하는 일반 테이블입니다.
*   **지오메트리 타입**: 없음 (`wkbNone`)
*   **스키마**: 위의 **공통 오류 스키마**와 동일합니다.

이것으로 QC 관련 시스템의 스키마, 모델, 저장 방식에 대한 정리를 마칩니다. 추가로 궁금한 점이 있으시면 언제든지 질문해 주세요.