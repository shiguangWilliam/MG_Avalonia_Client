<#
.SYNOPSIS
    Generate an image with the Z.ai GLM-Image API (local art tool).

.DESCRIPTION
    Reads the API key from config.json (or ZAI_API_KEY env var), posts the
    prompt to the GLM-Image endpoint and saves the result as a local file.
    Handles both url and b64_json response formats.

.PARAMETER Prompt
    Text prompt for the image. Required.

.PARAMETER Out
    Output file path. Defaults to Tools/GlmImage/output/glm_<timestamp>.png

.PARAMETER Size
    Image size, e.g. "1280x1280", "1440x720". Default 1280x1280.

.PARAMETER Model
    Model id. Default "glm-image".

.EXAMPLE
    .\generate.ps1 -Prompt "tactical starfield, dark, gold accents" -Out "..\..\ClientAvalonia\Assets\starfield.png" -Size "1440x720"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Prompt,

    [string]$Out = "",

    [string]$Size = "1280x1280",

    [string]$Model = "glm-image",

    [string]$ApiUrl = "https://open.bigmodel.cn/api/paas/v4/images/generations",

    # Optional reference image URL for image-to-image.
    [string]$ImageUrl = "",

    # Local reference image path — converted to a data-URI for image_url.
    [string]$ImagePath = "",

    # Platform watermark. Default $true (policy). Set $false only after signing
    # the disclaimer at bigmodel.cn → 个人中心 → 安全管理 → 去水印管理.
    [bool]$WatermarkEnabled = $true
)

$ErrorActionPreference = "Stop"
$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---- Resolve API key: config.json > env var ----
$apiKey = $null
$configPath = Join-Path $toolDir "config.json"
if (Test-Path $configPath) {
    try {
        $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
        if ($cfg.apiKey) { $apiKey = $cfg.apiKey }
    }
    catch {
        Write-Host "config.json is not valid JSON: $_" -ForegroundColor Red
        exit 1
    }
}
if (-not $apiKey) { $apiKey = $env:ZAI_API_KEY }
if (-not $apiKey) {
    Write-Host "API key not found." -ForegroundColor Red
    Write-Host "  Option 1: copy config.example.json to config.json and paste your token into 'apiKey'."
    Write-Host "  Option 2: set environment variable ZAI_API_KEY."
    exit 1
}

# ---- Output path ----
if (-not $Out) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $Out = Join-Path $toolDir "output\glm_$stamp.png"
}
$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# ---- Request ----
if ($ImagePath) {
    if (-not (Test-Path $ImagePath)) {
        Write-Host "ImagePath not found: $ImagePath" -ForegroundColor Red
        exit 1
    }
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $ImagePath).Path)
    $b64 = [Convert]::ToBase64String($bytes)
    $ext = [IO.Path]::GetExtension($ImagePath).TrimStart('.').ToLowerInvariant()
    if ($ext -eq "jpg") { $ext = "jpeg" }
    if ($ext -notin @("png","jpeg","webp")) { $ext = "png" }
    $ImageUrl = "data:image/$ext;base64,$b64"
    Write-Host "Attached local reference ($([math]::Round($bytes.Length/1KB,1)) KB) as data-URI" -ForegroundColor DarkCyan
}

$bodyObj = @{
    model             = $Model
    prompt            = $Prompt
    size              = $Size
    watermark_enabled = $WatermarkEnabled
}
if ($ImageUrl) { $bodyObj.image_url = $ImageUrl }

# Avoid ConvertTo-Json truncation on large data-URIs (Windows PowerShell).
if ($ImageUrl -and $ImageUrl.Length -gt 20000) {
    $promptJson = ($Prompt | ConvertTo-Json)
    $wm = if ($WatermarkEnabled) { "true" } else { "false" }
    # image_url is already base64/data-URI — no further escaping needed beyond quotes.
    $body = "{`"model`":`"$Model`",`"prompt`":$promptJson,`"size`":`"$Size`",`"watermark_enabled`":$wm,`"image_url`":`"$ImageUrl`"}"
}
else {
    $body = $bodyObj | ConvertTo-Json -Depth 4 -Compress
}

Write-Host "Requesting $Size image from $Model (watermark=$WatermarkEnabled$(if($ImageUrl){", with reference"}))..." -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri $ApiUrl -Method Post `
        -Headers @{ Authorization = "Bearer $apiKey" } `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 300
}
catch {
    Write-Host "API request failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
        Write-Host "Response body: $($_.ErrorDetails.Message)" -ForegroundColor DarkRed
    }
    # 429: brief backoff then a single retry (rate limits are often transient).
    if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 429) {
        Write-Host "Rate limited — retrying once in 20s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 20
        try {
            $response = Invoke-RestMethod -Uri $ApiUrl -Method Post `
                -Headers @{ Authorization = "Bearer $apiKey" } `
                -ContentType "application/json" `
                -Body $body `
                -TimeoutSec 300
        }
        catch {
            Write-Host "Retry failed: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
                Write-Host "Response body: $($_.ErrorDetails.Message)" -ForegroundColor DarkRed
            }
            exit 1
        }
    }
    else {
        exit 1
    }
}

# ---- Parse response: url or b64_json ----
$item = $response.data | Select-Object -First 1
if (-not $item) {
    Write-Host "Unexpected response (no data array):" -ForegroundColor Red
    $response | ConvertTo-Json -Depth 10 | Write-Host
    exit 1
}

if ($item.url) {
    Write-Host "Downloading $($item.url)..."
    try {
        Invoke-WebRequest -Uri $item.url -OutFile $Out -TimeoutSec 180
    }
    catch {
        Write-Host "Download failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "URL (manual download): $($item.url)"
        exit 1
    }
}
elseif ($item.b64_json) {
    [IO.File]::WriteAllBytes($Out, [Convert]::FromBase64String($item.b64_json))
}
else {
    Write-Host "Response has neither url nor b64_json:" -ForegroundColor Red
    $response | ConvertTo-Json -Depth 10 | Write-Host
    exit 1
}

$bytes = (Get-Item $Out).Length
Write-Host "Saved: $Out ($([math]::Round($bytes / 1KB, 1)) KB)" -ForegroundColor Green
