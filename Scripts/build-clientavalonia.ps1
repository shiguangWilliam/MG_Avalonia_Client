#!/usr/bin/env powershell
#Requires -Version 5.1

#####################################################################
#
# Builds the CnCNet Avalonia UI Client.
#
# Release publish: self-contained single-file ClientAvalonia.exe
# (Avalonia / SkiaSharp / ClientCore DLLs embedded in the exe).
# Policy: note/clientavalonia-build.md — Release packaging is ALWAYS single-file.
#
# Default outputs (Release):
#   1. CompiledAvalonia/          — project pack directory (+ staged resources)
#   2. ClientAvalonia/publish/    — workspace mirror of the same bundle
#
# Debug (-IsDebug): multi-file bin output for local dev ONLY — not for deploy.
#
#####################################################################

<#
.SYNOPSIS
  Builds the Avalonia-based CnCNet Client.
.DESCRIPTION
  Restores/builds shared dependencies (ClientCore), publishes ClientAvalonia
  as a self-contained single-file exe, stages DTA + core INI resources, and
  mirrors the bundle to ClientAvalonia/publish/.
.PARAMETER IsDebug
  Local development only: framework-dependent multi-file publish (NOT for packaging/deploy).
.PARAMETER Log
  Enable diagnostic verbosity for the build output.
.PARAMETER NoClean
  Skip cleaning output folders before building.
.PARAMETER SkipValidate
  Skip headless MainMenu.ini validation after publish.
.PARAMETER SkipWorkspaceMirror
  Skip copying the bundle to ClientAvalonia/publish/.
.PARAMETER DeployTo
  Optional third deploy target (e.g. MG mod test folder). Only runtime exe
  is copied — game Resources/INI on the target are not overwritten.
.EXAMPLE
  build-clientavalonia.ps1
  Release single-file build → CompiledAvalonia + ClientAvalonia/publish
.EXAMPLE
  build-clientavalonia.ps1 -DeployTo "D:\MG\MG-Avalonia测试区3"
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
  [switch]
  $SkipWorkspaceMirror,

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
$Script:WorkspacePackRoot = Join-Path (Join-Path $RepoRoot 'ClientAvalonia') 'publish'
$Script:Configuration = if ($IsDebug) { 'Debug' } else { 'Release' }
$Script:IsSingleFile = -not $IsDebug
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

  Write-Host "Staging client resources into $DestinationRoot ..."

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

function Copy-AvaloniaPublishBundle {
  param(
    [Parameter(Mandatory = $true)]
    [string]
    $SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]
    $DestinationRoot,

    [Parameter()]
    [switch]
    $RuntimeOnly
  )

  if (!(Test-Path -LiteralPath $DestinationRoot)) {
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
  }

  Write-Host "Copying publish bundle → $DestinationRoot ..."

  if ($RuntimeOnly) {
    Copy-AvaloniaRuntimeOnly -SourceRoot $SourceRoot -DestinationRoot $DestinationRoot
    return
  }

  # Full mirror: exe + staged resources (skip loose Avalonia DLLs in single-file mode).
  $items = @('ClientAvalonia.exe', 'Resources', 'SUN.ini', 'Maps', 'INI', 'MIX')
  foreach ($name in $items) {
    $src = Join-Path $SourceRoot $name
    if (!(Test-Path -LiteralPath $src)) {
      continue
    }

    $dest = Join-Path $DestinationRoot $name
    if (Test-Path -LiteralPath $src -PathType Container) {
      if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Recurse -Force
      }
      Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    }
    else {
      Copy-Item -LiteralPath $src -Destination $DestinationRoot -Force
    }
  }

  if (-not $Script:IsSingleFile) {
    Copy-AvaloniaRuntimeOnly -SourceRoot $SourceRoot -DestinationRoot $DestinationRoot
  }
}

function Copy-AvaloniaRuntimeOnly {
  param(
    [Parameter(Mandatory = $true)]
    [string]
    $SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]
    $DestinationRoot
  )

  if ($Script:IsSingleFile) {
    $exe = Join-Path $SourceRoot 'ClientAvalonia.exe'
    if (!(Test-Path -LiteralPath $exe)) {
      throw "Single-file publish missing: $exe"
    }

    Remove-LooseSingleFileArtifacts -PackRoot $DestinationRoot
    $destExe = Join-Path $DestinationRoot 'ClientAvalonia.exe'
    if (Test-Path -LiteralPath $destExe) {
      $backupExe = Join-Path $DestinationRoot 'ClientAvalonia.exe.old'
      if (Test-Path -LiteralPath $backupExe) {
        Remove-Item -LiteralPath $backupExe -Force
      }
      try {
        Rename-Item -LiteralPath $destExe -NewName 'ClientAvalonia.exe.old' -Force
      }
      catch {
        Write-Warning "Could not rename locked ClientAvalonia.exe — close the running client and redeploy."
        throw
      }
    }
    Copy-Item -LiteralPath $exe -Destination $DestinationRoot -Force
    Write-Host "Deployed single-file exe (removed legacy loose DLLs in target)."

    # Deploy default WAF rules without clobbering operator overrides (Client/WafRules.json).
    $wafSrc = Join-Path $SourceRoot 'Client\WafRules.default.json'
    if (!(Test-Path -LiteralPath $wafSrc)) {
      $wafSrc = Join-Path $PSScriptRoot '..\ClientAvalonia\CnCNet\Waf\rules.default.json'
    }
    if (Test-Path -LiteralPath $wafSrc) {
      $wafDestDir = Join-Path $DestinationRoot 'Client'
      New-Item -ItemType Directory -Force -Path $wafDestDir | Out-Null
      Copy-Item -LiteralPath $wafSrc -Destination (Join-Path $wafDestDir 'WafRules.default.json') -Force
    }
    return
  }

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

  Get-ChildItem -LiteralPath $SourceRoot -Filter 'Avalonia*.dll' -ErrorAction SilentlyContinue | ForEach-Object {
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

function Remove-LooseSingleFileArtifacts {
  param(
    [Parameter(Mandatory = $true)]
    [string]
    $PackRoot
  )

  if (-not $Script:IsSingleFile) {
    return
  }

  $patterns = @(
    'Avalonia*.dll',
    'ClientAvalonia.dll',
    'ClientCore.dll',
    'ClientUpdater.dll',
    'Rampastring.Tools.dll',
    'HarfBuzzSharp.dll',
    'SkiaSharp.dll',
    'MicroCom.Runtime.dll',
    'System.IO.Pipelines.dll',
    'Tmds.DBus.Protocol.dll',
    'Newtonsoft.Json*.dll',
    'System.Net.Http.Formatting.dll',
    'Ude.NetStandard.dll',
    '*.deps.json',
    '*.runtimeconfig.json',
    '*.pdb'
  )

  foreach ($pattern in $patterns) {
    Get-ChildItem -LiteralPath $PackRoot -Filter $pattern -File -ErrorAction SilentlyContinue |
      ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
  }

  $runtimesDir = Join-Path $PackRoot 'runtimes'
  if (Test-Path -LiteralPath $runtimesDir) {
    Remove-Item -LiteralPath $runtimesDir -Recurse -Force
  }
}

function Clear-PackDirectory {
  param([string]$Path)
  if (Test-Path -LiteralPath $Path) {
    Write-Host "Cleaning $Path ..."
    Remove-Item -Recurse -Force -LiteralPath $Path
  }
}

# -----------------------------------------------------------------
# Clean previous output
# -----------------------------------------------------------------
if (!$NoClean) {
  Clear-PackDirectory -Path $Script:CompiledRoot
  if (!$SkipWorkspaceMirror) {
    Clear-PackDirectory -Path $Script:WorkspacePackRoot
  }
}

# -----------------------------------------------------------------
# Build shared dependencies
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
Write-Host "Publishing ClientAvalonia ($Script:Configuration$(if ($Script:IsSingleFile) { ', single-file win-x64' } else { ''})) ..."

$publishArgs = @(
  'publish', $Script:ProjectPath,
  "--configuration:$Script:Configuration",
  '--framework:net8.0',
  "--output:$Script:CompiledRoot",
  $Script:ConfigurationsProperty,
  '-p:DisableGitVersionTask=true',
  '-p:GitVersion_MsBuildTask_Disabled=true',
  '-p:LangVersion=latest'
)

if ($Script:IsSingleFile) {
  $publishArgs += @(
    '-p:PublishProfile=win-x64-singlefile'
  )
}
else {
  $publishArgs += '--self-contained:false'
  Write-Warning 'Debug multi-file publish is for local dev only — do not deploy.'
}

Invoke-DotNet @publishArgs

Remove-LooseSingleFileArtifacts -PackRoot $Script:CompiledRoot

Copy-AvaloniaClientResources -DestinationRoot $Script:CompiledRoot

# Ship editable WAF rule pack next to the client (operators may override with WafRules.json).
$WafRulesSrc = Join-Path $RepoRoot 'ClientAvalonia\CnCNet\Waf\rules.default.json'
$WafClientDir = Join-Path $Script:CompiledRoot 'Client'
if (Test-Path -LiteralPath $WafRulesSrc) {
  New-Item -ItemType Directory -Force -Path $WafClientDir | Out-Null
  Copy-Item -LiteralPath $WafRulesSrc -Destination (Join-Path $WafClientDir 'WafRules.default.json') -Force
}

# -----------------------------------------------------------------
# Mirror to workspace pack directory (ClientAvalonia/publish)
# -----------------------------------------------------------------
if (!$SkipWorkspaceMirror) {
  Copy-AvaloniaPublishBundle -SourceRoot $Script:CompiledRoot -DestinationRoot $Script:WorkspacePackRoot
}

# -----------------------------------------------------------------
# Headless smoke test
# -----------------------------------------------------------------
if (!$SkipValidate) {
  $MainMenuIni = Join-Path $Script:CompiledRoot 'Resources\DTA\MainMenu.ini'
  if (Test-Path -LiteralPath $MainMenuIni) {
    Write-Host 'Validating MainMenu.ini load ...'
    Push-Location $Script:CompiledRoot
    try {
      if ($Script:IsSingleFile -and (Test-Path -LiteralPath (Join-Path $Script:CompiledRoot 'ClientAvalonia.exe'))) {
        & (Join-Path $Script:CompiledRoot 'ClientAvalonia.exe') --validate-ini $MainMenuIni
        if ($LASTEXITCODE -ne 0) { throw "Validation failed (exit $LASTEXITCODE)" }
      }
      else {
        Invoke-DotNet ClientAvalonia.dll --validate-ini $MainMenuIni
      }
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
Write-Host 'Build succeeded.'
Write-Host "  Project pack:  $Script:CompiledRoot"
if (!$SkipWorkspaceMirror) {
  Write-Host "  Workspace:     $Script:WorkspacePackRoot"
}
if ($Script:IsSingleFile) {
  $exe = Join-Path $Script:CompiledRoot 'ClientAvalonia.exe'
  if (Test-Path -LiteralPath $exe) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
    Write-Host "  Single-file:   ClientAvalonia.exe (${sizeMb} MB)"
  }
  Write-Host 'Run:  cd CompiledAvalonia && .\ClientAvalonia.exe'
}
else {
  Write-Host 'Run:  cd CompiledAvalonia && dotnet ClientAvalonia.dll'
}

if ($DeployTo) {
  if (!(Test-Path -LiteralPath $DeployTo)) {
    throw "Deploy target not found: $DeployTo"
  }
  Copy-AvaloniaRuntimeOnly -SourceRoot $Script:CompiledRoot -DestinationRoot $DeployTo
  Write-Host "  Deployed exe:  $DeployTo"
}
