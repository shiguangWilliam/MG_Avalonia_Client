# Crop bottom watermark band only — no pixel inpainting.
Add-Type -AssemblyName System.Drawing
$src = (Resolve-Path "Tools\GlmImage\output\world_map_raw.png").Path
$outDir = Join-Path (Get-Location) "Tools\GlmImage\output\processed"
$asset = Join-Path (Get-Location) "ClientAvalonia\Assets\Glm\world_map.png"
New-Item -ItemType Directory -Path $outDir,(Split-Path $asset) -Force | Out-Null
$img = [System.Drawing.Image]::FromFile($src)
$bmp = New-Object System.Drawing.Bitmap($img)
$drop = 110
$h = [Math]::Max(1, $bmp.Height - $drop)
$rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $h)
$cropped = $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$cropped.Save((Join-Path $outDir "world_map.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$cropped.Save($asset, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host ("published {0}x{1}" -f $cropped.Width, $cropped.Height)
$cropped.Dispose(); $bmp.Dispose(); $img.Dispose()
