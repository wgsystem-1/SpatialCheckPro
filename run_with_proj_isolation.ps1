# PROJ 라이브러리 격리 실행 스크립트

# 현재 PATH 백업
$originalPath = $env:PATH

# PostgreSQL 경로 제거
$newPath = ($env:PATH -split ';' | Where-Object { 
    $_ -notmatch 'PostgreSQL' -and 
    $_ -notmatch 'postgis' -and
    $_ -notmatch 'OSGeo4W'
}) -join ';'

# 새로운 환경 변수 설정
$env:PATH = $newPath

# PROJ 관련 환경 변수 설정
$appPath = "G:\QC_pro\GeoSpatialValidationSystem.GUI\bin\Release\net8.0-windows"
$projPath = "$appPath\gdal\share"

$env:PROJ_LIB = $projPath
$env:PROJ_DATA = $projPath
$env:PROJ_SEARCH_PATH = $projPath
$env:PROJ_NETWORK = "OFF"
$env:PROJ_DEBUG = "3"
$env:PROJ_USER_WRITABLE_DIRECTORY = $projPath
$env:PROJ_CACHE_DIR = $projPath
$env:GDAL_DATA = "$appPath\gdal\data"

# PostgreSQL proj.db가 있는지 확인하고 임시로 이름 변경
$postgresProj = "C:\Program Files\PostgreSQL\15\share\contrib\postgis-3.5\proj\proj.db"
if (Test-Path $postgresProj) {
    Write-Host "PostgreSQL proj.db 발견 - 임시로 이름 변경" -ForegroundColor Yellow
    try {
        Rename-Item $postgresProj "$postgresProj.backup" -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Host "proj.db 이름 변경 실패 (권한 부족)" -ForegroundColor Red
    }
}

Write-Host "환경 설정 완료:" -ForegroundColor Green
Write-Host "PROJ_LIB: $env:PROJ_LIB"
Write-Host "PROJ_DATA: $env:PROJ_DATA"
Write-Host "GDAL_DATA: $env:GDAL_DATA"
Write-Host ""
Write-Host "애플리케이션 실행..." -ForegroundColor Cyan

# 애플리케이션 실행
& "$appPath\GeoSpatialValidationSystem.GUI.exe"

# 원래 환경 복원
$env:PATH = $originalPath

# PostgreSQL proj.db 복원
if (Test-Path "$postgresProj.backup") {
    Write-Host "`nPostgreSQL proj.db 복원 중..." -ForegroundColor Yellow
    try {
        Rename-Item "$postgresProj.backup" $postgresProj -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Host "proj.db 복원 실패" -ForegroundColor Red
    }
}

Write-Host "`n실행 완료" -ForegroundColor Green
