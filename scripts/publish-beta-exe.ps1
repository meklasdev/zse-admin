#requires -Version 5.1
<#
.SYNOPSIS
    Publikuje istniejący klient WPF jako samodzielne demo beta z lokalnym API w tym samym procesie.

.EXAMPLE
    ./scripts/publish-beta-exe.ps1 -Runtime win-x64
#>
param(
    [string]$Output = "$PSScriptRoot\..\artifacts\beta-exe",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Runtime -notin @("win-x64", "win-arm64")) {
    throw "Obsługiwane identyfikatory Windows: win-x64 albo win-arm64."
}

Write-Host "Publikuję klienta WPF + lokalne API mock w jednym procesie..." -ForegroundColor Cyan
& dotnet publish src/ZseAdmin.Client `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:ZseAdminClientBundle=true `
    -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish zakończył się kodem $LASTEXITCODE." }

$settingsPath = Join-Path $Output "appsettings.json"
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
if ($settings.UseLocalDemoServer -ne $true) {
    throw "Artefakt beta musi mieć UseLocalDemoServer = true."
}
if ($settings.UseWindowsAuth -eq $true) {
    throw "Demo beta nie może uruchamiać uwierzytelniania domenowego."
}

$executable = Join-Path $Output "ZseAdmin.Client.exe"
if (-not (Test-Path $executable)) {
    throw "Nie znaleziono $executable."
}

Write-Host "Gotowe: $executable" -ForegroundColor Green
Write-Host "Uruchom EXE na Windows. API mock uruchomi się w procesie aplikacji na loopback; AD nie jest używane."
