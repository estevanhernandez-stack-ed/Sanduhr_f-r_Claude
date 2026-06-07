# Sanduhr -- generate MSIX/Store logo assets.
#
# PLACEHOLDER GENERATOR. Renders the bundled Assets/Sanduhr.ico onto navy-field tiles with a
# 626 cyan->magenta accent rule and the "Sanduhr für Claude" wordmark on the wide/splash
# surfaces. This produces a VALID, packable asset set so the manifest references resolve and
# `makeappx` succeeds -- but these are placeholders.
#
#   *** FINAL Store tile graphics MUST go through the 626labs-design skill before submission. ***
#   Pattern (x): never ship programmatic placeholders to the Store. See Package/Logos/README.md.
#
# Run from repo root (Sanduhr/):
#   powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/generate-store-assets.ps1
#
# Output: windows-dotnet/src/Sanduhr.App/Package/Logos/*.png -- bare names + scale-{100,125,150,
# 200,400} variants for every tile, plus Square44x44 targetsize-{16,24,32,48,256} (plated +
# altform-unplated) used both by Windows list rendering and by the Velopack AppIcon.ico builder.

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$appDir   = Join-Path $RepoRoot 'windows-dotnet\src\Sanduhr.App'
$icoPath  = Join-Path $appDir 'Assets\Sanduhr.ico'
$logosDir = Join-Path $appDir 'Package\Logos'

if (-not (Test-Path $icoPath)) { throw "Source icon not found: $icoPath" }
if (-not (Test-Path $logosDir)) { New-Item -ItemType Directory -Path $logosDir -Force | Out-Null }

# ----- Brand tokens -----------------------------------------------------------
$navy    = [System.Drawing.Color]::FromArgb(255, 15, 24, 43)    # #0f182b (manifest BackgroundColor)
$cyan    = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)  # #17d4fa
$magenta = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)  # #f22f89
$muted   = [System.Drawing.Color]::FromArgb(255, 154, 168, 184) # #9aa8b8

# ----- Load the largest available icon frame as a bitmap ----------------------
# Decode via the Bitmap path (not Icon.ToBitmap): the .ico's large frames are PNG-encoded,
# which trips a known GDI+ bug in Icon.ToBitmap but decodes cleanly through Bitmap.
$srcBmp = [System.Drawing.Bitmap]::new($icoPath)

function New-Canvas {
    param([int]$w, [int]$h)
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $b = New-Object System.Drawing.SolidBrush($navy)
    try { $g.FillRectangle($b, 0, 0, $w, $h) } finally { $b.Dispose() }
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Draw-AccentRule {
    # Thin cyan->magenta bar -- the only brand cue beyond the glyph on placeholder tiles.
    param([System.Drawing.Graphics]$g, [single]$x, [single]$y, [single]$w, [single]$h)
    $p1 = New-Object System.Drawing.PointF($x, $y)
    $p2 = New-Object System.Drawing.PointF(($x + $w), $y)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p1, $p2, $cyan, $magenta)
    try { $g.FillRectangle($grad, $x, $y, $w, $h) } finally { $grad.Dispose() }
}

function Draw-Glyph {
    # Icon scaled to a target box centered at (cx, cy).
    param([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$box)
    $dst = New-Object System.Drawing.RectangleF(($cx - $box / 2), ($cy - $box / 2), $box, $box)
    $g.DrawImage($srcBmp, $dst)
}

function Draw-Text {
    param([System.Drawing.Graphics]$g, [string]$text, [single]$x, [single]$y, [single]$px,
          [System.Drawing.Color]$color, [bool]$bold = $true)
    $style = if ($bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    $font  = New-Object System.Drawing.Font('Segoe UI', $px, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush($color)
    try { $g.DrawString($text, $font, $brush, $x, $y) } finally { $brush.Dispose(); $font.Dispose() }
}

function Save-Png {
    param([System.Drawing.Bitmap]$bmp, [string]$path)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# ----- Renderers --------------------------------------------------------------
function Render-Square {
    param([int]$size)
    $c = New-Canvas $size $size
    Draw-Glyph $c.Graphics ($size / 2.0) ($size / 2.0) ([single]($size * 0.62))
    return $c
}

function Render-Wide {
    param([int]$w, [int]$h)
    $c = New-Canvas $w $h
    Draw-Glyph $c.Graphics ($w * 0.18) ($h * 0.50) ([single]($h * 0.74))
    $tx = $w * 0.36
    Draw-Text $c.Graphics 'Sanduhr' $tx ($h * 0.28) ([single]($h * 0.20)) ([System.Drawing.Color]::White) $true
    Draw-Text $c.Graphics 'für Claude' $tx ($h * 0.52) ([single]($h * 0.13)) $muted $false
    Draw-AccentRule $c.Graphics $tx ($h * 0.74) ([single]($w * 0.30)) ([single]([Math]::Max(2, $h * 0.025)))
    return $c
}

function Render-Splash {
    param([int]$w, [int]$h)
    $c = New-Canvas $w $h
    Draw-Glyph $c.Graphics ($w * 0.22) ($h * 0.50) ([single]($h * 0.80))
    $tx = $w * 0.40
    Draw-Text $c.Graphics 'Sanduhr' $tx ($h * 0.30) ([single]($h * 0.20)) ([System.Drawing.Color]::White) $true
    Draw-Text $c.Graphics 'für Claude' $tx ($h * 0.54) ([single]($h * 0.12)) $muted $false
    Draw-AccentRule $c.Graphics $tx ($h * 0.72) ([single]($w * 0.28)) ([single]([Math]::Max(2, $h * 0.022)))
    return $c
}

# ----- Asset matrix -----------------------------------------------------------
$scales = [ordered]@{ 'scale-100' = 1.00; 'scale-125' = 1.25; 'scale-150' = 1.50; 'scale-200' = 2.00; 'scale-400' = 4.00 }

# Clear stale variants so a size change doesn't leave orphans.
Get-ChildItem $logosDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

function Emit-Square {
    param([string]$name, [int]$base)
    foreach ($s in $scales.GetEnumerator()) {
        $size = [int][Math]::Round($base * $s.Value)
        $c = Render-Square $size
        Save-Png $c.Bitmap (Join-Path $logosDir "$name.$($s.Key).png")
        if ($s.Key -eq 'scale-100') { Save-Png $c.Bitmap (Join-Path $logosDir "$name.png") }
        $c.Graphics.Dispose(); $c.Bitmap.Dispose()
    }
}

Emit-Square 'Square44x44Logo'   44
Emit-Square 'Square71x71Logo'   71
Emit-Square 'Square150x150Logo' 150
Emit-Square 'Square310x310Logo' 310
Emit-Square 'StoreLogo'         50

# Square44x44 targetsize variants (Win list rendering + Velopack ICO source).
foreach ($t in @(16, 24, 32, 48, 256)) {
    $c = Render-Square $t
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-$t.png")
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-${t}_altform-unplated.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# Wide tile (with wordmark).
foreach ($s in $scales.GetEnumerator()) {
    $w = [int][Math]::Round(310 * $s.Value); $h = [int][Math]::Round(150 * $s.Value)
    $c = Render-Wide $w $h
    Save-Png $c.Bitmap (Join-Path $logosDir "Wide310x150Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Save-Png $c.Bitmap (Join-Path $logosDir 'Wide310x150Logo.png') }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# Splash screen (620x300 base).
foreach ($s in $scales.GetEnumerator()) {
    $w = [int][Math]::Round(620 * $s.Value); $h = [int][Math]::Round(300 * $s.Value)
    $c = Render-Splash $w $h
    Save-Png $c.Bitmap (Join-Path $logosDir "SplashScreen.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Save-Png $c.Bitmap (Join-Path $logosDir 'SplashScreen.png') }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

$srcBmp.Dispose()

$count = (Get-ChildItem $logosDir -Filter '*.png').Count
Write-Host "[done] $count placeholder PNGs written to $logosDir" -ForegroundColor Green
Write-Host "[flag] PLACEHOLDERS -- run the 626labs-design skill before any Store submission." -ForegroundColor Yellow
