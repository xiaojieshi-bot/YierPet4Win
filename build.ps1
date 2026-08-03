# YierPet Windows build (Windows 10/11 + .NET 8 SDK)
# ASCII-only output: safe for Windows PowerShell 5.x (no UTF-8 BOM issues)
param(
    [switch]$SelfContained,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\spritesheet.webp"))) {
    Write-Error "Missing Assets\spritesheet.webp"
}
if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\Packs"))) {
    Write-Error "Missing Assets\Packs"
}

$buildDir = Join-Path $PSScriptRoot "build"
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }

Push-Location (Join-Path $PSScriptRoot "YierPet")
$publishArgs = @(
    "publish",
    "-c", "Release",
    "-r", "win-x64",
    "-o", "..\build"
)
if ($SelfContained) {
    $publishArgs += @("--self-contained", "true", "-p:PublishReadyToRun=true")
} else {
    $publishArgs += @("--self-contained", "false")
}
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
}
Pop-Location

if (-not (Test-Path (Join-Path $buildDir "YierPet.exe"))) {
    Write-Error "YierPet.exe was not produced. See errors above."
}

Write-Host "Build OK: $buildDir\YierPet.exe"

if ($Package) {
    $distDir = Join-Path $PSScriptRoot "dist"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zipPath = Join-Path $distDir "YierPet4Win-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $buildDir "*") -DestinationPath $zipPath
    Write-Host "Package: $zipPath"
    if ($SelfContained) {
        Write-Host "Self-contained: unzip and run YierPet.exe (no .NET runtime needed)"
    } else {
        Write-Host "Framework-dependent: install .NET 8 Desktop Runtime first"
    }
} else {
    Write-Host "Run: .\build\YierPet.exe"
    Write-Host "Zip: .\build.ps1 -Package -SelfContained"
}
