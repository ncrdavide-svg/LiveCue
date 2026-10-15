# Builds artifacts\installer\LiveCue-Setup-<version>.exe
# Requires: .NET 9 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Usage: powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 1.0.1]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
# Published to a short temp path: VLC's plugin files sit deep in the tree, and from a long
# checkout path they exceed the 260-character limit Inno Setup can read.
$publishDir = Join-Path ([IO.Path]::GetTempPath()) "LiveCue-publish"

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 non trovato. Installalo con: winget install JRSoftware.InnoSetup" }

Write-Host "Pubblicazione LiveCue $Version (self-contained, win-x64)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root "src\LiveCue.App") -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:Version=$Version -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito" }

Write-Host "Creazione installer..."
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot "LiveCue.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup non riuscito" }

Get-Item (Join-Path $root "artifacts\installer\LiveCue-Setup-$Version.exe")
