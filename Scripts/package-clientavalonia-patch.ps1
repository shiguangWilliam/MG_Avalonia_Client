#!/usr/bin/env powershell
#Requires -Version 5.1

<#
.SYNOPSIS
  Builds ClientAvalonia and packages a game-root patch (exe + required DTA lobby INIs).
.DESCRIPTION
  Output: Dist/ClientAvalonia-Patch-<timestamp>.zip
  Extract the zip directly into your MG / mod game root (same folder as gamemd.exe).
  Does NOT overwrite ThemeMG, ClientDefinitions.ini, or GameCollectionConfig.ini.
.EXAMPLE
  .\Scripts\package-clientavalonia-patch.ps1
.EXAMPLE
  .\Scripts\package-clientavalonia-patch.ps1 -SkipBuild
#>
param(
  [switch]$SkipBuild,
  [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$BuildScript = Join-Path $PSScriptRoot 'build-clientavalonia.ps1'
$CompiledRoot = Join-Path $RepoRoot 'CompiledAvalonia'
$DtaSrc = Join-Path $RepoRoot 'DXMainClient\Resources\DTA'
$DistRoot = Join-Path $RepoRoot 'Dist'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$PatchName = "ClientAvalonia-Patch-$Stamp"
$PatchDir = Join-Path $DistRoot $PatchName
$ZipPath = Join-Path $DistRoot "$PatchName.zip"

# INI files required by ClientAvalonia (lobby UI chain + CnCNet multiplayer).
$LobbyIniFiles = @(
  'GenericWindow.ini',
  'GameLobbyBase.ini',
  'SkirmishLobby.ini',
  'MultiplayerGameLobby.ini',
  'CnCNetLobby.ini',
  'CnCNetGameLobby.ini',
  'LANLobby.ini',
  'LANGameLobby.ini'
)

function Write-PatchReadme {
  param([string]$Path, [string]$BuildTime, [long]$ExeBytes)

  $sizeMb = [math]::Round($ExeBytes / 1MB, 1)
  @"
ClientAvalonia 补丁包
=====================

构建时间: $BuildTime
启动器:   ClientAvalonia.exe (${sizeMb} MB, .NET 8 单文件自包含，无需额外 DLL)

安装方法
--------
1. 关闭正在运行的 ClientAvalonia.exe
2. 将本 zip 内所有文件解压到游戏根目录（与 gamemd.exe 同级）
3. 确认出现:
     ClientAvalonia.exe
     Resources\DTA\CnCNetGameLobby.ini  等 INI
     Resources\Translations\zh-CN\Translation.ini 等语言包
4. 双击 ClientAvalonia.exe 启动（不要覆盖 Resources\ThemeMG\ 下已有 MG 主题 INI）

本补丁包含的 INI
----------------
仅复制 Resources\DTA\ 下 ClientAvalonia 联机/遭遇战大厅所需的窗口定义:
  $($LobbyIniFiles -join ', ')

以及 Resources\Translations\（en / zh-CN / ru）客户端文案。

不会覆盖
--------
  Resources\ThemeMG\*
  Resources\ClientDefinitions.ini
  Resources\GameCollectionConfig.ini
  游戏本体 INI / MIX

说明
----
  - 创建游戏弹窗、颜色预览、CnCNet 频道/房间等功能依赖上述 DTA INI 作为回退。
  - 若 MG 主题缺少 CnCNetGameLobby.ini，解压后会自动补齐。
  - Translations 会合并进游戏根 Resources\Translations（同名文件会被更新）。
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

if (Test-Path -LiteralPath $PatchDir) {
  Remove-Item -LiteralPath $PatchDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PatchDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PatchDir 'Resources\DTA') | Out-Null

Copy-Item -LiteralPath $exe -Destination $PatchDir -Force

$missing = @()
foreach ($ini in $LobbyIniFiles) {
  $src = Join-Path $DtaSrc $ini
  if (-not (Test-Path -LiteralPath $src)) {
    $missing += $ini
    continue
  }
  Copy-Item -LiteralPath $src -Destination (Join-Path $PatchDir "Resources\DTA\$ini") -Force
}

if ($missing.Count -gt 0) {
  throw "Missing source INI(s): $($missing -join ', ')"
}

$TranslationsSrc = Join-Path $RepoRoot 'DXMainClient\Resources\Translations'
if (Test-Path -LiteralPath $TranslationsSrc) {
  $TranslationsDest = Join-Path $PatchDir 'Resources\Translations'
  New-Item -ItemType Directory -Force -Path $TranslationsDest | Out-Null
  Copy-Item -Path (Join-Path $TranslationsSrc '*') -Destination $TranslationsDest -Recurse -Force
}

$exeInfo = Get-Item -LiteralPath (Join-Path $PatchDir 'ClientAvalonia.exe')
Write-PatchReadme -Path (Join-Path $PatchDir 'PATCH_README.txt') -BuildTime (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -ExeBytes $exeInfo.Length

if (Test-Path -LiteralPath $ZipPath) {
  Remove-Item -LiteralPath $ZipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PatchDir, $ZipPath)

Write-Host ''
Write-Host 'Patch package ready.'
Write-Host "  Folder:  $PatchDir"
Write-Host "  Zip:     $ZipPath"
Write-Host "  Size:    $([math]::Round((Get-Item $ZipPath).Length / 1MB, 2)) MB"
Write-Host ''
Write-Host 'Install: extract zip contents into your game root (folder with gamemd.exe).'
