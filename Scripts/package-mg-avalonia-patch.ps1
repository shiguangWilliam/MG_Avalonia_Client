#!/usr/bin/env powershell
#Requires -Version 5.1
# MG patch: ClientAvalonia.exe + Resources\ClientDefinitions.ini (ClientGameType=YR)

param(
  [switch]$SkipBuild,
  [switch]$NoClean,
  [Parameter(Mandatory = $true)]
  [string]$MgBaselineRoot
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$BuildScript = Join-Path $PSScriptRoot "build-clientavalonia.ps1"
$CompiledRoot = Join-Path $RepoRoot "CompiledAvalonia"
$DistRoot = Join-Path $RepoRoot "Dist"
$Stamp = Get-Date -Format "yyyyMMdd-HHmm"
$PatchName = "MG-Avalonia-Patch-$Stamp"
$PatchDir = Join-Path $DistRoot $PatchName
$ZipPath = Join-Path $DistRoot "$PatchName.zip"
$TestAreaIni = Join-Path (Split-Path (Split-Path $RepoRoot -Parent) -Parent) "MG-Avalonia测试区2\Resources\ClientDefinitions.ini"

if (-not $SkipBuild) {
  if ($NoClean) { & $BuildScript -SkipValidate -NoClean }
  else { & $BuildScript -SkipValidate }
}

if (-not (Test-Path -LiteralPath $MgBaselineRoot)) {
  throw "MG baseline not found: $MgBaselineRoot"
}

$exe = Join-Path $CompiledRoot "ClientAvalonia.exe"
if (-not (Test-Path -LiteralPath $exe)) {
  throw "Missing: $exe"
}

$iniSource = $TestAreaIni
if (-not (Test-Path -LiteralPath $iniSource)) {
  throw "Missing patched ClientDefinitions.ini: $iniSource"
}

if (Test-Path -LiteralPath $PatchDir) { Remove-Item -LiteralPath $PatchDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $PatchDir "Resources") | Out-Null
Copy-Item -LiteralPath $exe -Destination $PatchDir -Force
Copy-Item -LiteralPath $iniSource -Destination (Join-Path $PatchDir "Resources\ClientDefinitions.ini") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\Dist\MG-Avalonia-Patch-20260605-2021\PATCH_README.txt") -Destination (Join-Path $PatchDir "PATCH_README.txt") -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PatchDir, $ZipPath)

Write-Host "Patch: $ZipPath"
