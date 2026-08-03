# 构建 YierPet Windows 版（需 Windows 10/11 + .NET 8 SDK）
param(
    # 打 Release 安装包时建议开启：内置 .NET 运行时，用户无需另装
    [switch]$SelfContained,
    # 将 build\ 打成 dist\YierPet4Win-win-x64.zip
    [switch]$Package
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\spritesheet.webp"))) {
    Write-Error "缺少 Assets\spritesheet.webp，请确认素材已包含在本项目中。"
}
if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\Packs"))) {
    Write-Error "缺少 Assets\Packs，请确认表情包素材已包含在本项目中。"
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
    Write-Error "dotnet publish 失败，退出码 $LASTEXITCODE"
}
Pop-Location

if (-not (Test-Path (Join-Path $buildDir "YierPet.exe"))) {
    Write-Error "未生成 YierPet.exe，请检查上方编译错误。"
}

Write-Host "构建完成：$buildDir\YierPet.exe"

if ($Package) {
    $distDir = Join-Path $PSScriptRoot "dist"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zipPath = Join-Path $distDir "YierPet4Win-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $buildDir "*") -DestinationPath $zipPath
    Write-Host "安装包：$zipPath"
    if ($SelfContained) {
        Write-Host "（自包含版，解压后双击 YierPet.exe 即可，无需安装 .NET 运行时）"
    } else {
        Write-Host "（框架依赖版，运行前需安装 .NET 8 桌面运行时）"
    }
} else {
    Write-Host "启动：.\build\YierPet.exe"
    Write-Host "打 zip 包：.\build.ps1 -Package -SelfContained"
}
