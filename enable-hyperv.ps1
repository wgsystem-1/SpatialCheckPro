# Hyper-V 활성화 스크립트 (관리자 권한 필요)

# 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")

if (-not $isAdmin) {
    Write-Host "이 스크립트는 관리자 권한이 필요합니다!" -ForegroundColor Red
    Write-Host "관리자 권한으로 다시 실행합니다..." -ForegroundColor Yellow
    
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

Write-Host "Hyper-V 및 관련 기능 활성화 중..." -ForegroundColor Cyan

# Hyper-V 활성화
Write-Host "`n1. Hyper-V 활성화 중..." -ForegroundColor Yellow
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All -NoRestart

# Virtual Machine Platform 활성화
Write-Host "`n2. Virtual Machine Platform 활성화 중..." -ForegroundColor Yellow
Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform -All -NoRestart

# WSL 활성화 (Docker Desktop에 필요)
Write-Host "`n3. Windows Subsystem for Linux 활성화 중..." -ForegroundColor Yellow
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Windows-Subsystem-Linux -All -NoRestart

# 하드웨어 가상화 확인
Write-Host "`n4. 하드웨어 가상화 지원 확인 중..." -ForegroundColor Yellow
$vmx = Get-WmiObject -Class Win32_Processor | Select-Object -Property Name, VirtualizationFirmwareEnabled
if ($vmx.VirtualizationFirmwareEnabled -eq $true) {
    Write-Host "✓ 하드웨어 가상화 활성화됨" -ForegroundColor Green
} else {
    Write-Host "⚠ 하드웨어 가상화가 BIOS/UEFI에서 비활성화되어 있을 수 있습니다." -ForegroundColor Yellow
    Write-Host "  컴퓨터를 재시작하고 BIOS/UEFI 설정에서 다음을 활성화하세요:" -ForegroundColor Yellow
    Write-Host "  - Intel: Intel VT-x 또는 Intel Virtualization Technology" -ForegroundColor White
    Write-Host "  - AMD: AMD-V 또는 SVM Mode" -ForegroundColor White
}

Write-Host "`n모든 기능이 활성화되었습니다!" -ForegroundColor Green
Write-Host "`n중요: 시스템을 재시작해야 변경사항이 적용됩니다." -ForegroundColor Yellow

Write-Host "`n지금 재시작하시겠습니까? (Y/N): " -ForegroundColor Cyan -NoNewline
$restart = Read-Host

if ($restart -eq 'Y' -or $restart -eq 'y') {
    Write-Host "시스템을 재시작합니다..." -ForegroundColor Yellow
    Restart-Computer -Force
} else {
    Write-Host "`n수동으로 재시작하세요. 재시작 후 Docker Desktop을 다시 실행하세요." -ForegroundColor Yellow
}


