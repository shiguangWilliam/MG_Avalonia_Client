#!/usr/bin/env powershell
#Requires -Version 5.1

#####################################################################
#
# Builds the CnCNet Avalonia UI Client.
#
# Unlike the main build.ps1 which handles multiple rendering engines
# (XNA/DX/GL) and .NET Framework 4.8, this script focuses solely on
# the cross-platform Avalonia client targeting .NET 8.0.
#
# Post-publish it stages DXMainClient example resources into
# CompiledAvalonia/ so the output is runnable without a separate copy step.
#
#####################################################################

<#
.SYNOPSIS
  Builds the Avalonia-based CnCNet Client.
.DESCRIPTION
  Restores/builds shared dependencies (ClientCore), publishes ClientAvalonia,
  and copies DTA + core INI resources into CompiledAvalonia/.
.PARAMETER IsDebug
  Build in Debug configuration instead of Release.
.PARAMETER Log
  Enable diagnostic verbosity for the build output.
.PARAMETER NoClean
  Skip cleaning the CompiledAvalonia output folder before building.
.PARAMETER BuildDependencies
  Also build ClientCore (requires Rampastring.Tools submodule). Off by default until ClientAvalonia references ClientCore.
.PARAMETER SkipValidate
  Skip headless MainMenu.ini validation after publish.
.EXAMPLE
  build-clientavalonia.ps1
  Build the Avalonia client in Release mode.
.EXAMPLE
  build-clientavalonia.ps1 -IsDebug
  Build the Avalonia client in Debug mode.
#>
param(
  [Parameter()]
  [switch]
  $IsDebug,

  [Parameter()]
  [switch]
  $Log,

  [Parameter()]
  [switch]
  $NoClean,

  [Parameter()]
  [switch]
  $SkipValidate,

  [Parameter()]
  [string]
  $DeployTo
)

$ErrorActionPreference = 'Stop'

$Script:RepoRoot = Split-Path $PSScriptRoot -Parent
$Script:ProjectPath = Join-Path (Join-Path $RepoRoot 'ClientAvalonia') 'ClientAvalonia.csproj'
$Script:ClientCorePath = Join-Path (Join-Path $RepoRoot 'ClientCore') 'ClientCore.csproj'
$Script:ClientUpdaterPath = Join-Path (Join-Path $RepoRoot 'ClientUpdater') 'ClientUpdater.csproj'
$Script:CompiledRoot = Join-Path $RepoRoot 'CompiledAvalonia'
$Script:Configuration = if ($IsDebug) { 'Debug' } else { 'Release' }
# PowerShell 5.1 splits bare semicolons in -property values; MSBuild accepts %3B.
$Script:ConfigurationsProperty = '-p:Configurations=Debug%3BRelease'

function Invoke-DotNet {
  param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]
    $ArgumentList
  )

  if ($Log) {
    $ArgumentList = $ArgumentList + @('--verbosity:diagnostic')
  }

  Write-Host "> dotnet $($ArgumentList -join ' ')"
  & dotnet.exe @ArgumentList
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet failed (exit code $LASTEXITCODE): $($ArgumentList -join ' ')"
  }
}

function Copy-AvaloniaClientResources {
  param(
    [Parameter(Mandatory = $true)]
    [string]
    $DestinationRoot
  )

  $ResourcesSrc = Join-Path $RepoRoot 'DXMainClient\Resources'
  if (!(Test-Path -LiteralPath $ResourcesSrc)) {
    Write-Warning "Resources source not found: $ResourcesSrc"
    return
  }

  Write-Host 'Staging client resources into CompiledAvalonia ...'

  $DtaSrc = Join-Path $ResourcesSrc 'DTA'
  $DtaDest = Join-Path $DestinationRoot 'Resources\DTA'
  if (Test-Path -LiteralPath $DtaSrc) {
    New-Item -ItemType Directory -Force -Path $DtaDest | Out-Null
    Copy-Item -Path (Join-Path $DtaSrc '*') -Destination $DtaDest -Recurse -Force
  }

  $ClientDefinitions = Join-Path $ResourcesSrc 'ClientDefinitions.ini'
  if (Test-Path -LiteralPath $ClientDefinitions) {
    $ResourcesDest = Join-Path $DestinationRoot 'Resources'
    New-Item -ItemType Directory -Force -Path $ResourcesDest | Out-Null
    Copy-Item -LiteralPath $ClientDefinitions -Destination $ResourcesDest -Force
  }

  $SunIni = Join-Path $ResourcesSrc 'SUN.ini'
  if (Test-Path -LiteralPath $SunIni) {
    Copy-Item -LiteralPath $SunIni -Destination $DestinationRoot -Force
  }

  foreach ($Folder in @('Maps', 'INI', 'MIX')) {
    $Src = Join-Path $ResourcesSrc $Folder
    if (Test-Path -LiteralPath $Src) {
      $Dest = Join-Path $DestinationRoot $Folder
      New-Item -ItemType Directory -Force -Path $Dest | Out-Null
      Copy-Item -Path (Join-Path $Src '*') -Destination $Dest -Recurse -Force
    }
  }
}

function Copy-AvaloniaClientToDeployTarget {
  param(
    [Parameter(Mandatory = $true)]
    [string]
    $SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]
    $DestinationRoot
  )

  if (!(Test-Path -LiteralPath $DestinationRoot)) {
    throw "Deploy target not found: $DestinationRoot"
  }

  Write-Host "Deploying ClientAvalonia runtime to $DestinationRoot ..."

  $RuntimeFiles = @(
    'ClientAvalonia.exe',
    'ClientAvalonia.dll',
    'ClientAvalonia.deps.json',
    'ClientAvalonia.runtimeconfig.json',
    'ClientCore.dll',
    'ClientUpdater.dll',
    'Rampastring.Tools.dll',
    'System.Net.Http.Formatting.dll',
    'Newtonsoft.Json.dll',
    'Newtonsoft.Json.Bson.dll',
    'Ude.NetStandard.dll'
  )

  foreach ($file in $RuntimeFiles) {
    $src = Join-Path $SourceRoot $file
    if (Test-Path -LiteralPath $src) {
      Copy-Item -LiteralPath $src -Destination $DestinationRoot -Force
    }
    else {
      Write-Warning "Missing publish artifact: $file"
    }
  }

  Get-ChildItem -LiteralPath $SourceRoot -Filter 'Avalonia*.dll' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $DestinationRoot -Force
  }

  foreach ($pattern in @('HarfBuzzSharp.dll', 'MicroCom.Runtime.dll', 'SkiaSharp.dll', 'System.IO.Pipelines.dll', 'Tmds.DBus.Protocol.dll')) {
    $src = Join-Path $SourceRoot $pattern
    if (Test-Path -LiteralPath $src) {
      Copy-Item -LiteralPath $src -Destination $DestinationRoot -Force
    }
  }

  $runtimesSrc = Join-Path $SourceRoot 'runtimes'
  if (Test-Path -LiteralPath $runtimesSrc) {
    Copy-Item -LiteralPath $runtimesSrc -Destination $DestinationRoot -Recurse -Force
  }
}

# -----------------------------------------------------------------
# Clean previous output
# -----------------------------------------------------------------
if (!$NoClean -and (Test-Path -LiteralPath $Script:CompiledRoot)) {
  Write-Host "Cleaning $Script:CompiledRoot ..."
  Remove-Item -Recurse -Force -LiteralPath $Script:CompiledRoot
}

# -----------------------------------------------------------------
# Build shared dependencies (ClientCore)
# -----------------------------------------------------------------
if (Test-Path -LiteralPath $Script:ClientCorePath) {
  Write-Host "Building ClientCore ($Script:Configuration) ..."
  Invoke-DotNet build $Script:ClientCorePath `
    "--configuration:$Script:Configuration" `
    '--framework:net8.0' `
    $Script:ConfigurationsProperty `
    '-p:DisableGitVersionTask=true' `
    '-p:LangVersion=latest'
}

if (Test-Path -LiteralPath $Script:ClientUpdaterPath) {
  Write-Host "Building ClientUpdater ($Script:Configuration) ..."
  Invoke-DotNet build $Script:ClientUpdaterPath `
    "--configuration:$Script:Configuration" `
    '--framework:net8.0' `
    $Script:ConfigurationsProperty `
    '-p:DisableGitVersionTask=true' `
    '-p:GitVersion_MsBuildTask_Disabled=true' `
    '-p:LangVersion=latest'
}

# -----------------------------------------------------------------
# Publish ClientAvalonia
# -----------------------------------------------------------------
Write-Host "Publishing ClientAvalonia ($Script:Configuration) ..."
Invoke-DotNet publish $Script:ProjectPath `
  "--configuration:$Script:Configuration" `
  '--framework:net8.0' `
  "--output:$Script:CompiledRoot" `
  '--self-contained:false' `
  $Script:ConfigurationsProperty `
  '-p:DisableGitVersionTask=true' `
  '-p:GitVersion_MsBuildTask_Disabled=true' `
  '-p:LangVersion=latest'

Copy-AvaloniaClientResources -DestinationRoot $Script:CompiledRoot

# -----------------------------------------------------------------
# Headless smoke test
# -----------------------------------------------------------------
if (!$SkipValidate) {
  $MainMenuIni = Join-Path $Script:CompiledRoot 'Resources\DTA\MainMenu.ini'
  if (Test-Path -LiteralPath $MainMenuIni) {
    Write-Host 'Validating MainMenu.ini load ...'
    Push-Location $Script:CompiledRoot
    try {
      Invoke-DotNet ClientAvalonia.dll --validate-ini $MainMenuIni
    }
    finally {
      Pop-Location
    }
  }
  else {
    Write-Warning "MainMenu.ini not found at $MainMenuIni — skipped validation."
  }
}

Write-Host ''
Write-Host "Build succeeded. Output: $Script:CompiledRoot"
Write-Host 'Run:  cd CompiledAvalonia && dotnet ClientAvalonia.dll'

if ($DeployTo) {
  Copy-AvaloniaClientToDeployTarget -SourceRoot $Script:CompiledRoot -DestinationRoot $DeployTo
  Write-Host "Deployed to: $DeployTo"
}
