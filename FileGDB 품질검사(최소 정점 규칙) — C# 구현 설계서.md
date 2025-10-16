# FileGDB 품질검사(최소 정점 규칙) — C# 구현 설계서

## 목표

- **규칙**:
  - Point → **정점 1개**
  - (Multi)LineString → **정점 ≥ 2개**
  - (Multi)Polygon → 각 링(외곽·홀) **폐합(첫=마지막) + 정점 ≥ 4개**
  - ※ Polygon 링 규칙은 **GeoJSON RFC 7946의 LinearRing 정의**(4개 이상, 첫·마지막 동일)와 일치하며, Esri Geometry 사양도 링 폐합을 요구합니다. [IETF Datatracker+2esri.github.io+2](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **입력**: Esri **File Geodatabase(.gdb)**
- **목표 품질**: 대용량 데이터에서도 **IO 병목 최소화, 빠른 스트리밍 검사, 정확한 예외 처리(곡선, Z/M, 빈 지오메트리, 무효 링 등)**

------

## 권장 아키텍처

### **GDAL/OGR + OpenFileGDB 드라이버 + C# 바인딩**

- **이유**
  - **OpenFileGDB**는 기본 내장, **스레드-세이프(데이터소스 병렬 처리 가능)**, 필드가 많은 FC에서 빠른 경향, 외부 상용 SDK 의존 없음. [gdal.org+1](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)
  - **C# 바인딩(SWIG)** 제공으로 .NET에서 바로 사용 가능. NuGet 패키지(예: `MaxRev.Gdal.Core` 등)로 손쉬운 배포. [gdal.org+1](https://gdal.org/en/stable/api/csharp/index.html?utm_source=chatgpt.com)
  - FileGDB 드라이버 비교: GDAL의 **FileGDB**(Esri API 연동) vs **OpenFileGDB**(내장). 우리 용도(읽기·검사)에는 OpenFileGDB가 적합. [gdal.org](https://gdal.org/en/stable/drivers/vector/filegdb.html?utm_source=chatgpt.com)
- **곡선(True Curves) 대응**
  - FileGDB는 폴리라인/폴리곤에 **곡선 세그먼트**를 가질 수 있음. 검사 일관성을 위해 **선형화(Linearize)** 후 최소정점 규칙을 적용. OGR은 **곡선 지오메트리를 ISO 곡선 타입으로 인식**하며, **`GetLinearGeometry()/OGR_G_GetLinearGeometry`**로 선형화 가능. [gdal.org+1](https://gdal.org/en/stable/development/rfc/rfc49_curve_geometries.html?utm_source=chatgpt.com)
  - 필요시 선형화 정밀도는 OGR 옵션(예: `OGR_ARC_STEPSIZE`)로 제어. [gdal.org](https://gdal.org/en/stable/api/ogrgeometry_cpp.html?utm_source=chatgpt.com)

------

## 검수 규칙의 표준 근거

- **LineString**: 좌표 **2개 이상**. (RFC 7946) [geojson.org](https://geojson.org/geojson-spec?utm_source=chatgpt.com)
- **LinearRing(Polygon 링)**: **폐합 + 4개 이상**(첫·마지막 동일). (RFC 7946) [IETF Datatracker](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **Esri Geometry 사양**: 링은 항상 **첫 점 = 마지막 점**. (Esri Geometry API 문서) [esri.github.io](https://esri.github.io/geometry-api-java/doc/Polygon.html?utm_source=chatgpt.com)

------

## 구현 개요(파이프라인)

1. **GDAL/OGR 초기화 및 드라이버 확인**
   - `Gdal.AllRegister(); Ogr.RegisterAll();`
   - `.gdb` 경로를 **OpenFileGDB**로 오픈(`read-only`)
   - **필드 무시**: 속성 필드를 읽지 않도록 `SetIgnoredFields()` → **지오메트리만** 스트리밍 (IO/디시리얼라이즈 비용 절감). [gdal.org](https://gdal.org/en/stable/development/rfc/rfc29_desired_fields.html?utm_source=chatgpt.com)
2. **레이어 순회**
   - `using`/`Dispose` 준수, 각 레이어에 대해 `GetNextFeature()`로 **스트리밍**.
3. **곡선 표준화(선형화)**
   - `Geometry gLinear = g.GetLinearGeometry(/*기본 옵션*/);`
   - 선형화 후 타입을 평탄화(`wkbFlatten`)하여 판정. [gdal.org](https://gdal.org/en/stable/api/vector_c_api.html?utm_source=chatgpt.com)
4. **최소정점 규칙 검사**
   - **Point**: `GetPointCount()==1 && !IsEmpty()`
   - **LineString**: `GetPointCount()>=2`
   - **MultiLineString**: 자식 라인 모두 `>=2`
   - **Polygon**: 각 링(Exterior+Interior) `IsRing()==true && GetPointCount()>=4`
   - **MultiPolygon**: 모든 폴리곤이 위 조건 충족
   - **기타 타입(GeometryCollection/CurvePolygon/…)**: 선형화 후 등가 타입으로 재검사 또는 **정책대로 제외/경고**.
5. **결과 수집**
   - (예시) `FeatureID, Layer, GeometryType, NumPoints(or per-ring), Pass/Fail, Message`
   - **빈 지오메트리**, **무효 링(비폐합)**, **곡선 포함 여부**(`HasCurveGeometry`) 등 메타도 기록. [gdal.org](https://gdal.org/en/stable/genindex.html?utm_source=chatgpt.com)
6. **병렬 처리 전략**
   - **OpenFileGDB는 “데이터소스 병렬 처리” 가능** → **레이어 단위** 또는 **파일 단위**로 **스레드 분할**, 각 스레드가 **자기 DataSource를 별도로 오픈**. (OGR 오브젝트는 스레드 간 공유 금지 권장) [gdal.org](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)

------

## C# 예시 코드 (핵심 로직)

> **메모**: 아래 코드는 **검수 핵심만** 담은 축약본입니다. 프로젝트에서는 **에러 처리/리소스 해제/로그/진단**을 추가하세요.

```
using OSGeo.OGR;
using OSGeo.GDAL;

public static class GdbMinVertexValidator
{
    public static void Run(string gdbPath)
    {
        Gdal.AllRegister();
        Ogr.RegisterAll();

        using var ds = Ogr.Open(gdbPath, 0); // read-only
        if (ds == null) throw new ApplicationException($"Open failed: {gdbPath}");

        for (int li = 0; li < ds.GetLayerCount(); li++)
        {
            using var layer = ds.GetLayerByIndex(li);
            // 성능: 속성 무시 (필요 시 필드명 나열)
            layer.SetIgnoredFields(new string[] { }); // 프로젝트에서 필드명 수집 후 전달 권장 (RFC 29)

            layer.ResetReading();
            Feature f;
            while ((f = layer.GetNextFeature()) != null)
            {
                using (f)
                {
                    using var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) { ReportFail(f, "Empty geometry"); continue; }

                    using var gl = g.GetLinearGeometry(); // 곡선 → 선형화
                    if (!Validate(gl)) ReportFail(f, "Min-vertex rule violated");
                }
            }
        }
    }

    static bool Validate(Geometry g)
    {
        var t = wkbGeometryType.wkbUnknown;
        try { t = (wkbGeometryType)((int)g.GetGeometryType() & 0xFF); } catch { }

        switch (t)
        {
            case wkbGeometryType.wkbPoint:
                return g.GetPointCount() == 1;

            case wkbGeometryType.wkbLineString:
                return g.GetPointCount() >= 2;

            case wkbGeometryType.wkbMultiLineString:
            {
                int n = g.GetGeometryCount();
                for (int i = 0; i < n; i++)
                    if (g.GetGeometryRef(i).GetPointCount() < 2) return false;
                return true;
            }

            case wkbGeometryType.wkbPolygon:
                return PolygonOk(g);

            case wkbGeometryType.wkbMultiPolygon:
            {
                int n = g.GetGeometryCount();
                for (int i = 0; i < n; i++)
                    if (!PolygonOk(g.GetGeometryRef(i))) return false;
                return true;
            }

            default:
                // CurvePolygon 등은 GetLinearGeometry() 후 wkbPolygon/LineString으로 들어오므로 도달 빈도 낮음
                return false;
        }
    }

    static bool PolygonOk(Geometry poly)
    {
        // exterior + interior rings
        int ringCount = poly.GetGeometryCount();
        for (int r = 0; r < ringCount; r++)
        {
            using var ring = poly.GetGeometryRef(r); // wkbLinearRing
            if (!ring.IsRing()) return false;        // 폐합 체크
            if (ring.GetPointCount() < 4) return false;
        }
        return true;
    }

    static void ReportFail(Feature f, string reason)
    {
        // TODO: 수집/로그/리포트
        // e.g., Console.WriteLine($"Layer={f.GetFID()} Reason={reason}");
    }
}
```

- **곡선 대응 근거**: GDAL은 **ISO 곡선 타입**(CurvePolygon 등)을 지원하며, **`GetLinearGeometry()`**로 **선형화** 가능. [gdal.org+1](https://gdal.org/en/stable/development/rfc/rfc49_curve_geometries.html?utm_source=chatgpt.com)
- **링 폐합/최소 정점 근거**: RFC 7946 LinearRing(4개 이상, 첫=마지막 동일), Esri 사양의 링 폐합. [IETF Datatracker+1](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **속성 무시 최적화**: **`SetIgnoredFields()`**로 **필드 역직렬화 회피 → IO/CPU 절감**. [gdal.org](https://gdal.org/en/stable/development/rfc/rfc29_desired_fields.html?utm_source=chatgpt.com)

> NuGet/런타임: `MaxRev.Gdal.Core` 등 패키지 사용 시 플랫폼별 네이티브 바이너리 세팅과 `GDAL_DATA/PROJ_LIB` 경로 구성이 필요합니다. (패키지 가이드 참고) [NuGet+1](https://www.nuget.org/packages/MaxRev.Gdal.Core?utm_source=chatgpt.com)

------

## 성능 최적화 체크리스트

1. **드라이버 선택**
   - **OpenFileGDB**: 내장, **병렬 처리 안전**, 대규모 필드에서 빠름. (읽기 검수에 적합) [gdal.org](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)
   - (비교) `FileGDB` 드라이버는 Esri FileGDB API 의존(쓰기 필요 시). 읽기 성능/의존성 측면에서 기본값으로는 OpenFileGDB 권장. [gdal.org](https://gdal.org/en/stable/drivers/vector/filegdb.html?utm_source=chatgpt.com)
2. **속성 무시**
   - `layer.SetIgnoredFields()`로 **지오메트리만** 가져오면 대폭 빨라짐. [gdal.org](https://gdal.org/en/stable/development/rfc/rfc29_desired_fields.html?utm_source=chatgpt.com)
3. **병렬화**
   - **레이어/파일 단위**로 **Task 병렬**. 각 Task는 **자기 DataSource를 독립 오픈**(드라이버는 thread-safe). [gdal.org](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)
4. **곡선 처리 비용 관리**
   - 기본은 `GetLinearGeometry()`로 선형화 후 판정. 정밀도 필요 시 **세그먼트 간격 옵션** 조절. [gdal.org+1](https://gdal.org/en/stable/api/vector_c_api.html?utm_source=chatgpt.com)

------

## 엣지 케이스 처리 정책

- **빈 지오메트리(IsEmpty)**: **Fail**로 기록. (점=0/선=0/면=0)
- **Polygon 링 비폐합**: `IsRing()==false` → **Fail**. (표준 요구) [IETF Datatracker](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **곡선 포함**: 선형화 후 검사. 필요 시 **곡선 존재 플래그**(`HasCurveGeometry`)를 결과에 기록. [gdal.org](https://gdal.org/en/stable/genindex.html?utm_source=chatgpt.com)
- **MultiSurface/CurvePolygon 등**: 선형화 후 등가 (Multi)Polygon/(Multi)LineString으로 판정. [gdal.org](https://gdal.org/en/stable/development/rfc/rfc49_curve_geometries.html?utm_source=chatgpt.com)
- **3D(Z)/측정값(M)**: 최소정점 규칙은 **평면 정점 개수** 기준(OGC/SFA/GeoJSON 모델). Z/M 존재와 무관. [mikejohnson51.github.io](https://mikejohnson51.github.io/geog178_2019/section_slides/week2_section_slides_1.pdf?utm_source=chatgpt.com)

------

## 결과 스키마(예시)

| 컬럼         | 타입   | 설명                                             |
| ------------ | ------ | ------------------------------------------------ |
| LayerName    | string | 레이어명                                         |
| FID          | long   | Feature ID                                       |
| GeometryType | string | (선형화 후) 지오메트리 타입                      |
| RingIndex    | int?   | 폴리곤 링 인덱스(외곽=0, 홀=1..). 라인/점은 null |
| NumPoints    | int    | 링 또는 지오메트리의 정점 수                     |
| IsClosed     | bool?  | 링 폐합 여부(폴리곤만)                           |
| HasCurves    | bool   | 원본에 곡선 세그먼트 존재                        |
| Pass         | bool   | 규칙 충족 여부                                   |
| Message      | string | 위반 사유(예: “ring not closed”, “<4 points”)    |

------

## 테스트 전략(샘플)

- **정상 케이스**:
  - Point(1), LineString(2), Polygon 삼각형(닫힘 포함 4) → **Pass**. (RFC 7946 준수) [IETF Datatracker](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **위반 케이스**:
  - LineString(1), Polygon 링 3점(닫힘 미충족) → **Fail**. [IETF Datatracker](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **곡선 케이스**:
  - Arc 세그먼트 포함 폴리곤 → `GetLinearGeometry()` 후 링 ≥4 확인 → **Pass**(정상). [desktop.arcgis.com+1](https://desktop.arcgis.com/en/arcmap/latest/manage-data/creating-new-features/creating-a-curve-with-arc.htm?utm_source=chatgpt.com)
- **무효 링**:
  - `IsRing()==false` → **Fail**. [gdal.org](https://gdal.org/en/stable/doxygen/classOGRGeometry.html?utm_source=chatgpt.com)

------

## 배포·운영 메모

- **NuGet**: `MaxRev.Gdal.Core` 등 최신 GDAL 바인딩 사용. 런타임별 네이티브 포함/초기화 필요. [NuGet+1](https://www.nuget.org/packages/MaxRev.Gdal.Core?utm_source=chatgpt.com)
- **대용량 처리**: 레이어/파일 단위 **병렬 파이프라인** 구성, **로그/리포트 분리**, 실패 샘플 자동 추출. (OpenFileGDB 병렬 처리 가능) [gdal.org](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)

## 참고 자료(근거)

- **GeoJSON RFC 7946** — LineString/LinearRing 정의(최소 정점, 링 폐합·순서). [IETF Datatracker+1](https://datatracker.ietf.org/doc/html/rfc7946?utm_source=chatgpt.com)
- **Esri Geometry 사양** — Polygon 링은 첫 점=마지막 점(폐합). [esri.github.io+1](https://esri.github.io/geometry-api-java/doc/Polygon.html?utm_source=chatgpt.com)
- **GDAL/OGR C# 바인딩** — 공식 문서/튜토리얼. [gdal.org+1](https://gdal.org/en/stable/api/csharp/index.html?utm_source=chatgpt.com)
- **OpenFileGDB 드라이버** — 내장, **스레드-세이프**, 성능 특성. [gdal.org+1](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)
- **FileGDB vs OpenFileGDB** — 드라이버 개요/차이. [gdal.org](https://gdal.org/en/stable/drivers/vector/filegdb.html?utm_source=chatgpt.com)
- **곡선 지오메트리 지원** — GDAL RFC 49(ISO 곡선 타입), `GetLinearGeometry()`로 선형화. [gdal.org+1](https://gdal.org/en/stable/development/rfc/rfc49_curve_geometries.html?utm_source=chatgpt.com)

## 결론

- **가장 실용적인 접근**: **GDAL/OGR + OpenFileGDB + C#**으로 **선형화 후 최소정점 규칙 검사**.
- **이유**: **표준 준수**, **배포 용이**, **대용량에 강한 스트리밍/병렬 처리**, **Esri 라이선스 비의존**. (필요 시 Esri 스택 병행) [gdal.org+1](https://gdal.org/en/stable/drivers/vector/openfilegdb.html?utm_source=chatgpt.com)