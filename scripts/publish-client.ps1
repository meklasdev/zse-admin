#requires -Version 5.1
<#
.SYNOPSIS
    Publikuje klienta WPF i nadpisuje appsettings.json konfiguracją produkcyjną (M3).

.DESCRIPTION
    Zwykły `dotnet publish` kopiuje deweloperski token z appsettings.json. Ten skrypt
    po publikacji w Release podmienia go na wss + UseWindowsAuth = true, bez tokenu.
    Dzięki temu artefakt nie zawiera sekretu, a klient używa Negotiate/Kerberos.

.EXAMPLE
    ./scripts/publish-client.ps1 -Output C:\Apps\ZseAdminClient
.EXAMPLE
    ./scripts/publish-client.ps1 -Output C:\Apps\ZseAdminClient -Runtime win-x64 -SingleFile
#>
param(
    [string]$Output = "$PSScriptRoot\..\artifacts\client",
    [string]$Configuration = "Release",
    [string]$Runtime = "",
    [switch]$SingleFile,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($SingleFile -and -not $Runtime) {
    throw "-SingleFile wymaga podania -Runtime (np. win-x64)."
}

$publishArgs = @("publish", "src/ZseAdmin.Client", "-c", $Configuration, "-o", $Output)
if ($NoBuild) { $publishArgs += "--no-build" }
if ($Runtime) { $publishArgs += @("-r", $Runtime, "--self-contained", "false") }
if ($SingleFile) { $publishArgs += "-p:PublishSingleFile=true" }

Write-Host "Publikacja klienta do $Output..." -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish zakończył się kodem $LASTEXITCODE." }

if ($Configuration -ieq "Release") {
    $production = Join-Path $root "src/ZseAdmin.Client/appsettings.Production.json"
    $destination = Join-Path $Output "appsettings.json"
    if (-not (Test-Path $production)) {
        throw "Brak $production — nie można przygotować bezpiecznego artefaktu."
    }

    Copy-Item -Path $production -Destination $destination -Force
    $settings = Get-Content $production -Raw | ConvertFrom-Json
    if ($settings.UseWindowsAuth -ne $true -or $settings.Token -or $settings.ServerUrl -notlike "wss://*" -or $settings.UseLocalDemoServer -eq $true) {
        throw "appsettings.Production.json musi mieć wss://, UseWindowsAuth = true, pusty Token i wyłączone lokalne demo."
    }

    Write-Host "Artefakt używa wss + Windows Auth i nie zawiera tokenu." -ForegroundColor Green
}

Write-Host ""
Write-Host "Gotowe. Przed wdrożeniem ustaw poprawny ServerUrl w $Output\appsettings.json" -ForegroundColor Green
Write-Host "oraz certyfikat serwera zaufany na stacjach administratorów."
