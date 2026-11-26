#requires -Version 5.1
<#
.SYNOPSIS
    Publikuje serwer ZSE-Admin i wypisuje komendy rejestracji jako usługa Windows.

.EXAMPLE
    ./scripts/publish-server.ps1 -Output C:\Apps\ZseAdmin
#>
param(
    [string]$Output = "$PSScriptRoot\..\artifacts\server",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "Publikacja serwera do $Output..." -ForegroundColor Cyan

dotnet publish src/ZseAdmin.Server -c $Configuration -r $Runtime --self-contained false -o $Output

Write-Host ""
Write-Host "Gotowe. Rejestracja jako usługa Windows (uruchom w PowerShell z uprawnieniami administratora):" -ForegroundColor Green
Write-Host "  sc.exe create ZseAdmin binPath= `"$env:ProgramFiles\dotnet\dotnet.exe $Output\ZseAdmin.Server.dll`" start= auto"
Write-Host "  sc.exe description ZseAdmin `"ZSE-Admin — serwer WebSocket dla pracowni`""
Write-Host "  sc.exe start ZseAdmin"
Write-Host ""
Write-Host "Pamiętaj o konfiguracji Security:Tokens oraz o tym, że tokeny nie są rozwiązaniem produkcyjnym."
