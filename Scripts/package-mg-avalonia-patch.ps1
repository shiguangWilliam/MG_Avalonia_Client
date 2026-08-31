#!/usr/bin/env powershell
#Requires -Version 5.1

<#
.SYNOPSIS
  Builds ClientAvalonia and packages an MG (Moment of Genesis) full-resource bundle.
.DESCRIPTION
  Output: Dist/MG-Avalonia-Patch-<timestamp>.zip

  Contains:
    ClientAvalonia.exe
    Resources\  (entire local snapshot: Binaries, BinariesNET8, Compatibility,
                 DTA, ThemeDefault, ThemeMG, Translations, ClientDefinitions.ini, ...)

  The snapshot lives at Packaging\MG-Avalonia\Resources\ and is NOT tracked by git —
  edit INIs there for local packaging; the script packs whatever the snapshot holds.
  Snapshot must contain ClientDefinitions.ini (the -ClientDefinitionsIni override
  is no longer applied; keep the file in the snapshot instead).

  Does NOT touch game MIX / INI (Battle.ini, rules, maps) — those stay in the game root.
.EXAMPLE
  .\Scripts\package-mg-avalonia-patch.ps1
.EXAMPLE
  .\Scripts\package-mg-avalonia-patch.ps1 -SkipBuild
#>
param(
  [switch]$SkipBuild,
  [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$BuildScript = Join-Path $PSScriptRoot 'build-clientavalonia.ps1'
$CompiledRoot = Join-Path $RepoRoot 'CompiledAvalonia'
$DistRoot = Join-Path $RepoRoot 'Dist'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$PatchName = "MG-Avalonia-Patch-$Stamp"
$PatchDir = Join-Path $DistRoot $PatchName
$ZipPath = Join-Path $DistRoot "$PatchName.zip"

function Write-MgPatchReadme {
  param([string]$Path, [string]$BuildTime, [long]$ExeBytes, [int]$ResourceFileCount, [double]$ResourceSizeMb)

  $sizeMb = [math]::Round($ExeBytes / 1MB, 1)
  @"
MG ClientAvalonia 资源整包
==========================

构建时间: $BuildTime
启动器:   ClientAvalonia.exe (${sizeMb} MB, .NET 8 单文件自包含)
资源快照: Packaging\MG-Avalonia\Resources（git 不追踪，本地改动直接生效）

包含文件
--------
  ClientAvalonia.exe
  Resources\  完整快照（$ResourceFileCount 个文件，约 $ResourceSizeMb MB）
              Binaries / BinariesNET8 / Compatibility / DTA / ThemeDefault /
              ThemeMG / Translations / ClientDefinitions.ini 等

资源快照要点
------------
  快照内容以测试区当前使用的 Resources 为准；改 INI 请直接改快照目录，
  重新打包即生效。快照内 ClientDefinitions.ini 即最终生效版本。

安装
----
1. 关闭正在运行的 ClientAvalonia.exe / MGLauncher.exe
2. 将本 zip 内所有文件解压到 MG 游戏根目录（与 gamemd.exe、MGLauncher.exe 同级）
3. 确认出现:
     ClientAvalonia.exe
     Resources\ClientDefinitions.ini 等完整资源
4. 双击 ClientAvalonia.exe 启动

不会覆盖
--------
  INI\、MIX\、地图等游戏本体文件（本包只动 Resources\ 与启动器）

说明
----
  - 本包为 Resources 全量覆盖包：解压后 Resources 内同名文件会被快照版本替换。
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

# 工作区资源快照（git 不追踪）：以测试区当前使用的 Resources 为准，改 INI 直接在这里改。
$ResourceSnapshot = Join-Path $RepoRoot 'Packaging\MG-Avalonia\Resources'
if (-not (Test-Path -LiteralPath $ResourceSnapshot)) {
  throw "Missing resource snapshot: $ResourceSnapshot — copy your MG game-root Resources here first."
}

# 快照必须自带 ClientDefinitions.ini（本地打包以快照为准，忽略 -ClientDefinitionsIni 覆盖）。
if (-not (Test-Path -LiteralPath (Join-Path $ResourceSnapshot 'ClientDefinitions.ini'))) {
  throw "Snapshot is missing ClientDefinitions.ini: $ResourceSnapshot"
}

if (Test-Path -LiteralPath $PatchDir) {
  Remove-Item -LiteralPath $PatchDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PatchDir | Out-Null
Copy-Item -LiteralPath $exe -Destination $PatchDir -Force

# 整包复制快照 Resources（Binaries/BinariesNET8/Compatibility/DTA/ThemeDefault/ThemeMG/Translations 等）。
robocopy $ResourceSnapshot (Join-Path $PatchDir 'Resources') /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
  throw "robocopy failed with exit code $LASTEXITCODE while staging Resources."
}
$LASTEXITCODE = 0

$exeInfo = Get-Item -LiteralPath (Join-Path $PatchDir 'ClientAvalonia.exe')
$resourceStats = Get-ChildItem -LiteralPath (Join-Path $PatchDir 'Resources') -Recurse -File
Write-MgPatchReadme -Path (Join-Path $PatchDir 'PATCH_README.txt') -BuildTime (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -ExeBytes $exeInfo.Length -ResourceFileCount $resourceStats.Count -ResourceSizeMb ([math]::Round(($resourceStats | Measure-Object -Property Length -Sum).Sum / 1MB, 1))

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
