# SpatialCheckPro 배포 스크립트
# Self-contained 배포 버전 생성

param(
    [string]$OutputDir = ".\publish",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SpatialCheckPro 배포 버전 생성" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 프로젝트 경로
$ProjectPath = "SpatialCheckPro.GUI\SpatialCheckPro.GUI.csproj"

# 출력 디렉토리 정리
if (Test-Path $OutputDir) {
    Write-Host "기존 배포 디렉토리 삭제 중..." -ForegroundColor Yellow
    Remove-Item $OutputDir -Recurse -Force
}

Write-Host "배포 디렉토리 생성: $OutputDir" -ForegroundColor Green
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host ""
Write-Host "Self-contained 배포 빌드 시작..." -ForegroundColor Green
Write-Host "  - Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  - Runtime: $Runtime" -ForegroundColor Gray
Write-Host "  - Output: $OutputDir" -ForegroundColor Gray
Write-Host ""

# dotnet publish 실행
# 주의: PublishSingleFile=false로 설정 (GDAL Native DLL 호환성 문제로 인해)
# GDAL Native DLL은 네이티브 라이브러리이므로 단일 파일로 압축 시 로드 문제 발생 가능
$publishArgs = @(
    "publish",
    $ProjectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:PublishReadyToRun=true",        # 성능 최적화: 시작 시간 20~40% 단축
    "-p:TieredCompilation=true",        # 성능 최적화: 계층적 컴파일
    "-o", $OutputDir
)

$result = & dotnet $publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ 빌드 실패!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ 빌드 완료!" -ForegroundColor Green
Write-Host ""

# GDAL Native DLL 수동 복사
Write-Host "GDAL Native DLL 복사 중..." -ForegroundColor Yellow
$gdalNativePath = "$env:USERPROFILE\.nuget\packages\gdal.native\3.10.3\build\gdal\x64"
if (Test-Path $gdalNativePath) {
    # GDAL DLL 파일들 복사
    $gdalDlls = Get-ChildItem $gdalNativePath -Filter "*.dll" -File
    foreach ($dll in $gdalDlls) {
        Copy-Item $dll.FullName -Destination $OutputDir -Force -ErrorAction SilentlyContinue
        Write-Host "  - 복사: $($dll.Name)" -ForegroundColor Gray
    }
    
    # GDAL 데이터 디렉토리 복사
    $gdalDataPath = Join-Path $gdalNativePath "gdal"
    if (Test-Path $gdalDataPath) {
        $gdalDestPath = Join-Path $OutputDir "gdal"
        if (Test-Path $gdalDestPath) {
            Remove-Item $gdalDestPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        Copy-Item $gdalDataPath -Destination $gdalDestPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  - GDAL 데이터 디렉토리 복사 완료" -ForegroundColor Gray
    }
    
    Write-Host "GDAL Native DLL 복사 완료" -ForegroundColor Green
} else {
    Write-Host "⚠️ GDAL Native 경로를 찾을 수 없습니다: $gdalNativePath" -ForegroundColor Yellow
}

# GDAL 데이터 디렉토리 복사 (빌드 디렉토리에서)
Write-Host "GDAL 데이터 디렉토리 복사 중..." -ForegroundColor Yellow
$buildGdalPath = "SpatialCheckPro.GUI\bin\Release\net8.0-windows\gdal"
if (Test-Path $buildGdalPath) {
    $gdalDestPath = Join-Path $OutputDir "gdal"
    if (Test-Path $gdalDestPath) {
        Remove-Item $gdalDestPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-Item $buildGdalPath -Destination $gdalDestPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  - 빌드 디렉토리에서 GDAL 데이터 복사 완료" -ForegroundColor Gray
    
    # PROJ 데이터베이스 파일 확인
    $projDbPath = Join-Path $gdalDestPath "share\proj.db"
    if (Test-Path $projDbPath) {
        Write-Host "  - PROJ 데이터베이스 파일 확인: proj.db" -ForegroundColor Gray
    } else {
        Write-Host "  ⚠️ PROJ 데이터베이스 파일을 찾을 수 없습니다: $projDbPath" -ForegroundColor Yellow
    }
    
    Write-Host "GDAL 데이터 디렉토리 복사 완료" -ForegroundColor Green
} else {
    Write-Host "⚠️ 빌드 디렉토리에 GDAL 데이터를 찾을 수 없습니다: $buildGdalPath" -ForegroundColor Yellow
}

# 배포 디렉토리에서 PROJ 데이터베이스 파일 최종 확인
Write-Host ""
Write-Host "PROJ 데이터베이스 파일 최종 확인 중..." -ForegroundColor Yellow
$finalProjDbPath = Join-Path $OutputDir "gdal\share\proj.db"
if (Test-Path $finalProjDbPath) {
    $fileInfo = Get-Item $finalProjDbPath
    Write-Host "  ✅ PROJ 데이터베이스 파일 확인됨: $($fileInfo.Name) ($([math]::Round($fileInfo.Length/1KB, 2)) KB)" -ForegroundColor Green
} else {
    Write-Host "  ❌ PROJ 데이터베이스 파일이 없습니다: $finalProjDbPath" -ForegroundColor Red
    Write-Host "     이 파일이 없으면 좌표계 변환이 실패할 수 있습니다!" -ForegroundColor Red
}

# 라이선스 번들 구성
Write-Host ""
Write-Host "라이선스 패키지 구성 중..." -ForegroundColor Yellow
$licenseSource = "LEGAL"
if (Test-Path $licenseSource) {
    $licenseDest = Join-Path $OutputDir "LICENSES"
    New-Item -ItemType Directory -Path $licenseDest -Force | Out-Null

    # 자체 라이선스
    $productLicense = Join-Path $licenseSource "SpatialCheckPro_LICENSE.txt"
    if (Test-Path $productLicense) {
        Copy-Item $productLicense -Destination (Join-Path $OutputDir "LICENSE.txt") -Force
        Write-Host "  - LICENSE.txt 작성 완료" -ForegroundColor Gray
    } else {
        Write-Host "  ⚠️ SpatialCheckPro_LICENSE.txt를 찾을 수 없습니다." -ForegroundColor Yellow
    }

    # 기본 라이선스 파일 복사
    $baseLicenseFiles = @(
        "MIT.txt",
        "Apache-2.0.txt",
        "BSD-3-Clause.txt",
        "LGPL-2.1.txt",
        "NOTICE_Apache2.txt",
        "THIRD_PARTY_LIST.md"
    )
    foreach ($fileName in $baseLicenseFiles) {
        $sourcePath = Join-Path $licenseSource $fileName
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath -Destination (Join-Path $licenseDest $fileName) -Force
            Write-Host "  - 복사: $fileName" -ForegroundColor Gray
        } else {
            Write-Host "  ⚠️ 누락된 라이선스 파일: $fileName" -ForegroundColor Yellow
        }
    }

    # GDAL / PROJ 라이선스는 빌드 산출물에서 최신본을 가져오고, 없으면 템플릿 사용
    $gdalLicenseSource = "SpatialCheckPro.GUI\bin\Release\net8.0-windows\gdal\license.txt"
    if (-not (Test-Path $gdalLicenseSource)) {
        $gdalLicenseSource = Join-Path $licenseSource "GDAL_LICENSE_template.txt"
    }
    if (Test-Path $gdalLicenseSource) {
        Copy-Item $gdalLicenseSource -Destination (Join-Path $licenseDest "GDAL_LICENSE.txt") -Force
        Write-Host "  - GDAL 라이선스 복사" -ForegroundColor Gray
    } else {
        Write-Host "  ⚠️ GDAL 라이선스 소스를 찾을 수 없습니다." -ForegroundColor Yellow
    }

    $projLicenseSource = "SpatialCheckPro.GUI\bin\Release\net8.0-windows\gdal\share\proj\COPYING"
    if (-not (Test-Path $projLicenseSource)) {
        $projLicenseSource = Join-Path $licenseSource "PROJ_COPYING_template.txt"
    }
    if (Test-Path $projLicenseSource) {
        Copy-Item $projLicenseSource -Destination (Join-Path $licenseDest "PROJ_COPYING.txt") -Force
        Write-Host "  - PROJ 라이선스 복사" -ForegroundColor Gray
    } else {
        Write-Host "  ⚠️ PROJ 라이선스 소스를 찾을 수 없습니다." -ForegroundColor Yellow
    }

    Write-Host "라이선스 패키지 구성 완료" -ForegroundColor Green
} else {
    Write-Host "⚠️ LEGAL 디렉토리를 찾을 수 없어 라이선스 복사를 건너뜁니다." -ForegroundColor Yellow
}

Write-Host ""

# 출력 파일 확인
$exePath = Join-Path $OutputDir "SpatialCheckPro.GUI.exe"
if (Test-Path $exePath) {
    $fileInfo = Get-Item $exePath
    $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host "생성된 파일:" -ForegroundColor Cyan
    Write-Host "  - 파일명: $($fileInfo.Name)" -ForegroundColor Gray
    Write-Host "  - 크기: $fileSizeMB MB" -ForegroundColor Gray
    Write-Host "  - 경로: $($fileInfo.FullName)" -ForegroundColor Gray
} else {
    Write-Host "⚠️ 실행 파일을 찾을 수 없습니다: $exePath" -ForegroundColor Yellow
}

# Config 디렉토리 확인
$configPath = Join-Path $OutputDir "Config"
if (Test-Path $configPath) {
    $configFiles = Get-ChildItem $configPath -Filter "*.csv" | Measure-Object
    Write-Host ""
    Write-Host "Config 디렉토리 확인:" -ForegroundColor Cyan
    Write-Host "  - CSV 파일 개수: $($configFiles.Count)" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "⚠️ Config 디렉토리를 찾을 수 없습니다!" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "배포 준비 완료!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "다음 단계:" -ForegroundColor Yellow
Write-Host "1. $OutputDir 디렉토리에서 실행 파일 테스트" -ForegroundColor Gray
Write-Host "2. 설치 프로그램 제작 (Inno Setup, WiX 등)" -ForegroundColor Gray
Write-Host "3. 깨끗한 PC에서 테스트" -ForegroundColor Gray
Write-Host ""

