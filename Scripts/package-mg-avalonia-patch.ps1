#!/usr/bin/env powershell
#Requires -Version 5.1

<#
.SYNOPSIS
  Builds ClientAvalonia and packages an MG (Moment of Genesis) game-root patch.
.DESCRIPTION
  Output: Dist/MG-Avalonia-Patch-<timestamp>.zip

  Contains:
    ClientAvalonia.exe
    Resources\ClientDefinitions.ini  (MG / YR / CnCNet R10 settings)

  Does NOT overwrite ThemeMG, DTA INI, GameCollectionConfig.ini, or game MIX.
.EXAMPLE
  .\Scripts\package-mg-avalonia-patch.ps1
.EXAMPLE
  .\Scripts\package-mg-avalonia-patch.ps1 -SkipBuild
.EXAMPLE
  .\Scripts\package-mg-avalonia-patch.ps1 -ClientDefinitionsIni "D:\MG\my-test\ClientDefinitions.ini"
#>
param(
  [switch]$SkipBuild,
  [switch]$NoClean,
  [string]$ClientDefinitionsIni = ''
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$BuildScript = Join-Path $PSScriptRoot 'build-clientavalonia.ps1'
$CompiledRoot = Join-Path $RepoRoot 'CompiledAvalonia'
$DistRoot = Join-Path $RepoRoot 'Dist'
$DefaultIni = Join-Path $RepoRoot 'Packaging\MG-Avalonia\ClientDefinitions.ini'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$PatchName = "MG-Avalonia-Patch-$Stamp"
$PatchDir = Join-Path $DistRoot $PatchName
$ZipPath = Join-Path $DistRoot "$PatchName.zip"

function Write-MgPatchReadme {
  param([string]$Path, [string]$BuildTime, [long]$ExeBytes)

  $sizeMb = [math]::Round($ExeBytes / 1MB, 1)
  @"
MG ClientAvalonia 补丁包
========================

构建时间: $BuildTime
启动器:   ClientAvalonia.exe (${sizeMb} MB, .NET 8 单文件自包含)

适用基线
--------
  The Moment of Genesis (MG) 游戏根目录
  需已安装 ThemeMG 主题与 GameCollectionConfig.ini（本补丁不附带）

包含文件
--------
  ClientAvalonia.exe
  Resources\ClientDefinitions.ini

ClientDefinitions.ini 要点
--------------------------
  LocalGame=MG
  ClientGameType=YR          (gamemd.exe / YR 引擎)
  CnCNetProtocolRevision=R10 (MG CnCNet 协议)
  Theme: ThemeMG/

安装
----
1. 关闭正在运行的 ClientAvalonia.exe / MGLauncher.exe
2. 将本 zip 内所有文件解压到 MG 游戏根目录（与 gamemd.exe、MGLauncher.exe 同级）
3. 确认出现:
     ClientAvalonia.exe
     Resources\ClientDefinitions.ini
4. 双击 ClientAvalonia.exe 启动

不会覆盖
--------
  Resources\ThemeMG\
  Resources\DTA\
  Resources\GameCollectionConfig.ini
  INI\、MIX\、地图等游戏本体文件

说明
----
  - 仅替换启动器 exe 与 ClientDefinitions.ini；MG 主题 INI 仍使用 ThemeMG。
  - 若需联机大厅 DTA INI 回退文件，请另行使用 package-clientavalonia-patch.ps1。
  - 需要 Windows x64；首次运行可能稍慢（单文件解压缓存）。

"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

if (-not $SkipBuild) {
  if ($NoClean) {
    & $BuildScript -SkipValidate -NoClean
  }
  else {
    & $BuildScript -SkipValidate
  }
}

$exe = Join-Path $CompiledRoot 'ClientAvalonia.exe'
if (-not (Test-Path -LiteralPath $exe)) {
  throw "Missing build output: $exe — run build first."
}

$iniSource = $ClientDefinitionsIni
if ([string]::IsNullOrWhiteSpace($iniSource)) {
  $iniSource = $DefaultIni
}

if (-not (Test-Path -LiteralPath $iniSource)) {
  throw "Missing ClientDefinitions.ini: $iniSource"
}

if (Test-Path -LiteralPath $PatchDir) {
  Remove-Item -LiteralPath $PatchDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $PatchDir 'Resources') | Out-Null
Copy-Item -LiteralPath $exe -Destination $PatchDir -Force
Copy-Item -LiteralPath $iniSource -Destination (Join-Path $PatchDir 'Resources\ClientDefinitions.ini') -Force

$exeInfo = Get-Item -LiteralPath (Join-Path $PatchDir 'ClientAvalonia.exe')
Write-MgPatchReadme -Path (Join-Path $PatchDir 'PATCH_README.txt') -BuildTime (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -ExeBytes $exeInfo.Length

if (Test-Path -LiteralPath $ZipPath) {
  Remove-Item -LiteralPath $ZipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PatchDir, $ZipPath)

Write-Host ''
Write-Host 'MG patch package ready.'
Write-Host "  Folder:  $PatchDir"
Write-Host "  Zip:     $ZipPath"
Write-Host "  Size:    $([math]::Round((Get-Item $ZipPath).Length / 1MB, 2)) MB"
Write-Host ''
Write-Host 'Install: extract zip contents into your MG game root (folder with gamemd.exe).'
