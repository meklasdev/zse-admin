#requires -Version 5.1
<#
.SYNOPSIS
    Uruchamia serwer ZSE-Admin w trybie deweloperskim (dane przykładowe).

.EXAMPLE
    ./scripts/run-server.ps1 -Url http://0.0.0.0:5080
#>
param(
    [string]$Url = "http://0.0.0.0:5080",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host "ZSE-Admin: start serwera na $Url (konfiguracja $Configuration)" -ForegroundColor Cyan
Write-Host "Konsola protokołu: $Url/debug.html" -ForegroundColor DarkGray

dotnet run --project src/ZseAdmin.Server -c $Configuration --urls $Url
