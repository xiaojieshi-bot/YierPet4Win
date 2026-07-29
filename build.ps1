# 构建 YierPet Windows 版（需 Windows 10/11 + .NET 8 SDK）
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\spritesheet.webp"))) {
    Write-Error "缺少 Assets\spritesheet.webp，请确认素材已包含在本项目中。"
}
if (-not (Test-Path (Join-Path $PSScriptRoot "Assets\Packs"))) {
    Write-Error "缺少 Assets\Packs，请确认表情包素材已包含在本项目中。"
}

Push-Location (Join-Path $PSScriptRoot "YierPet")
dotnet publish -c Release -r win-x64 --self-contained false -o ..\build
Pop-Location

Write-Host "构建完成：$PSScriptRoot\build\YierPet.exe"
Write-Host "启动：.\build\YierPet.exe"
