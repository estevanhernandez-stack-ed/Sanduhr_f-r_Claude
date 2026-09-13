# Sanduhr -- generate the MSIX/Store logo asset set from the committed brand art.
#
# BRANDED GENERATOR (since the 3.4.0 release prep, 2026-09-13). No rendered text, no programmatic
# art: every tile is a crop-and-composite of two committed brand sources.
#
#   - docs/store-assets/logo-square-1080x1080.png -- the Sanduhr lockup (hourglass glyph plus the
#     SANDUHR / FUR CLAUDE wordmark on navy). The glyph and the two wordmark lines are cropped out by
#     fixed pixel rectangles (measured 2026-09-13; re-measure if the lockup is ever redrawn) with the
#     source navy color-keyed to transparency, then placed on the manifest BackgroundColor #0f182b.
#     Feeds StoreLogo, Square71/150/310, Wide310x150 and SplashScreen.
#   - docs/images/icon-512.png -- the app icon (rounded square, transparent corners; the same identity
#     as Assets/Sanduhr.ico). Feeds the whole Square44x44 family on a TRANSPARENT canvas: Windows
#     plates the scale-* and plain targetsize-* variants itself, leaves altform-unplated bare, and
#     build-velopack-release.ps1 stitches the targetsize-* PNGs into the Velopack AppIcon.ico.
#
# Run from repo root (Sanduhr/):
#   powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/generate-store-assets.ps1
#
# Output: windows-dotnet/src/Sanduhr.App/Package/Logos/*.png -- bare names + scale-{100,125,150,
# 200,400} variants for every tile, plus Square44x44 targetsize-{16,24,32,48,256} (plated +
# altform-unplated). Stale PNGs in the folder are cleared first so a size change leaves no orphans.
# Then: powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Verify

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$Lockup   = 'docs\store-assets\logo-square-1080x1080.png',
    [string]$AppIcon  = 'docs\images\icon-512.png'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$appDir     = Join-Path $RepoRoot 'windows-dotnet\src\Sanduhr.App'
$logosDir   = Join-Path $appDir 'Package\Logos'
$lockupPath = Join-Path $RepoRoot $Lockup
$iconPath   = Join-Path $RepoRoot $AppIcon

if (-not (Test-Path $lockupPath)) { throw "Brand lockup not found: $lockupPath" }
if (-not (Test-Path $iconPath))   { throw "App icon not found: $iconPath" }
if (-not (Test-Path $logosDir))   { New-Item -ItemType Directory -Path $logosDir -Force | Out-Null }

# ----- Brand tokens -----------------------------------------------------------
$navy    = [System.Drawing.Color]::FromArgb(255, 15, 24, 43)    # #0f182b (manifest BackgroundColor)
$cyan    = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)  # #17d4fa
$magenta = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)  # #f22f89

# ----- Sources ----------------------------------------------------------------
$lockupBmp = [System.Drawing.Bitmap]::new($lockupPath)   # 1080x1080, navy field #0f1f31
$iconBmp   = [System.Drawing.Bitmap]::new($iconPath)     # 512x512, transparent corners
if ($lockupBmp.Width -ne 1080 -or $lockupBmp.Height -ne 1080) {
    throw "Lockup is $($lockupBmp.Width)x$($lockupBmp.Height); the crop rectangles below assume 1080x1080."
}

# Crop rectangles inside the 1080x1080 lockup (x, y, w, h), each with a 2px margin around the
# measured bounding box so anti-aliased edges survive the key.
$glyphRect = @(353, 145, 374, 571)    # hourglass glyph (bbox 355,147 .. 724,713)
$wordRect  = @(261, 794, 558,  94)    # "SANDUHR"      (bbox 263,796 .. 816,885)
$subRect   = @(436, 899, 209,  38)    # "FUR CLAUDE"   (bbox 438,901 .. 642,934, umlaut included)

# Color key: the lockup's navy (#0f1f31 = 15,31,49) +/- 12 per channel becomes transparent, so the
# crops sit on the #0f182b canvas without a visible rectangle. TileFlipXY stops bicubic sampling
# from pulling a transparent halo in from outside the source rectangle.
$keyed = New-Object System.Drawing.Imaging.ImageAttributes
$keyed.SetColorKey([System.Drawing.Color]::FromArgb(255, 3, 19, 37), [System.Drawing.Color]::FromArgb(255, 27, 43, 61))
$keyed.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
$plain = New-Object System.Drawing.Imaging.ImageAttributes
$plain.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

function New-Canvas {
    param([int]$w, [int]$h, [bool]$transparent = $false)
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    if ($transparent) {
        $g.Clear([System.Drawing.Color]::Transparent)
    } else {
        $b = New-Object System.Drawing.SolidBrush($navy)
        try { $g.FillRectangle($b, 0, 0, $w, $h) } finally { $b.Dispose() }
    }
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Draw-Crop {
    # Draw source rectangle $src (x,y,w,h) of $bmp into a destination box of $dw x $dh whose
    # center is (cx, cy). Aspect is the caller's responsibility.
    param([System.Drawing.Graphics]$g, [System.Drawing.Bitmap]$bmp, [int[]]$src,
          [double]$cx, [double]$cy, [double]$dw, [double]$dh,
          [System.Drawing.Imaging.ImageAttributes]$attrs)
    $dst = New-Object System.Drawing.Rectangle([int][Math]::Round($cx - $dw / 2), [int][Math]::Round($cy - $dh / 2),
                                               [int][Math]::Max(1, [Math]::Round($dw)), [int][Math]::Max(1, [Math]::Round($dh)))
    $g.DrawImage($bmp, $dst, [int]$src[0], [int]$src[1], [int]$src[2], [int]$src[3],
                 [System.Drawing.GraphicsUnit]::Pixel, $attrs)
}

function Draw-AccentRule {
    # Thin cyan->magenta bar under the wordmark on the wide and splash surfaces.
    param([System.Drawing.Graphics]$g, [single]$x, [single]$y, [single]$w, [single]$h)
    $p1 = New-Object System.Drawing.PointF($x, $y)
    $p2 = New-Object System.Drawing.PointF(($x + $w), $y)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p1, $p2, $cyan, $magenta)
    try { $g.FillRectangle($grad, $x, $y, $w, $h) } finally { $grad.Dispose() }
}

function Save-Png {
    param([System.Drawing.Bitmap]$bmp, [string]$path)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# ----- Renderers --------------------------------------------------------------
function Render-Glyph-Tile {
    # Hourglass glyph centered on the navy field, 64% of the tile height (Windows overlays the
    # display name at the bottom of the 150/310 tiles, so the mark stays clear of that band).
    param([int]$size)
    $c = New-Canvas $size $size
    $h = $size * 0.64
    $w = $h * $glyphRect[2] / $glyphRect[3]
    Draw-Crop $c.Graphics $lockupBmp $glyphRect ($size / 2.0) ($size / 2.0) $w $h $keyed
    return $c
}

function Render-Icon-Tile {
    # App icon on a transparent canvas. 8% padding at 32px and up (the same breathing room the
    # shipped .ico frames carry); edge to edge below that so 16/24px stay legible.
    param([int]$size)
    $c = New-Canvas $size $size $true
    $pad = if ($size -ge 32) { 0.08 } else { 0.0 }
    $box = $size * (1 - 2 * $pad)
    Draw-Crop $c.Graphics $iconBmp @(0, 0, $iconBmp.Width, $iconBmp.Height) ($size / 2.0) ($size / 2.0) $box $box $plain
    return $c
}

function Render-Lockup {
    # Horizontal lockup for the wide tile and the splash: glyph left, SANDUHR / FUR CLAUDE right
    # (both cropped from the square lockup, scaled together), accent rule underneath.
    param([int]$w, [int]$h)
    $c = New-Canvas $w $h
    $g = $c.Graphics

    $gh = $h * 0.72
    $gw = $gh * $glyphRect[2] / $glyphRect[3]
    Draw-Crop $g $lockupBmp $glyphRect ($w * 0.20) ($h * 0.50) $gw $gh $keyed

    $x0    = $w * 0.36
    $ww    = $w * 0.46
    $scale = $ww / $wordRect[2]
    $wh    = $wordRect[3] * $scale
    $sw    = $subRect[2] * $scale
    $sh    = $subRect[3] * $scale
    $gap   = $h * 0.05
    $rule  = [Math]::Max(2, $h * 0.025)
    $block = $wh + $gap + $sh + $gap + $rule
    $top   = $h * 0.48 - $block / 2

    Draw-Crop $g $lockupBmp $wordRect ($x0 + $ww / 2) ($top + $wh / 2) $ww $wh $keyed
    Draw-Crop $g $lockupBmp $subRect  ($x0 + $sw / 2) ($top + $wh + $gap + $sh / 2) $sw $sh $keyed
    Draw-AccentRule $g ([single]$x0) ([single]($top + $wh + $gap + $sh + $gap)) ([single]($w * 0.30)) ([single]$rule)
    return $c
}

# ----- Asset matrix -----------------------------------------------------------
$scales = [ordered]@{ 'scale-100' = 1.00; 'scale-125' = 1.25; 'scale-150' = 1.50; 'scale-200' = 2.00; 'scale-400' = 4.00 }

# Clear stale variants so a size change doesn't leave orphans.
Get-ChildItem $logosDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

function Emit-Square {
    param([string]$name, [int]$base, [scriptblock]$renderer)
    foreach ($s in $scales.GetEnumerator()) {
        $size = [int][Math]::Round($base * $s.Value)
        $c = & $renderer $size
        Save-Png $c.Bitmap (Join-Path $logosDir "$name.$($s.Key).png")
        if ($s.Key -eq 'scale-100') { Save-Png $c.Bitmap (Join-Path $logosDir "$name.png") }
        $c.Graphics.Dispose(); $c.Bitmap.Dispose()
    }
}

Emit-Square 'Square44x44Logo'   44  { param($s) Render-Icon-Tile  $s }
Emit-Square 'Square71x71Logo'   71  { param($s) Render-Glyph-Tile $s }
Emit-Square 'Square150x150Logo' 150 { param($s) Render-Glyph-Tile $s }
Emit-Square 'Square310x310Logo' 310 { param($s) Render-Glyph-Tile $s }
Emit-Square 'StoreLogo'         50  { param($s) Render-Glyph-Tile $s }

# Square44x44 targetsize variants (Windows list rendering + the Velopack AppIcon.ico source).
foreach ($t in @(16, 24, 32, 48, 256)) {
    $c = Render-Icon-Tile $t
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-$t.png")
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-${t}_altform-unplated.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# Wide tile (310x150 base) and splash screen (620x300 base): the horizontal lockup.
foreach ($surface in @(@{ Name = 'Wide310x150Logo'; W = 310; H = 150 }, @{ Name = 'SplashScreen'; W = 620; H = 300 })) {
    foreach ($s in $scales.GetEnumerator()) {
        $w = [int][Math]::Round($surface.W * $s.Value); $h = [int][Math]::Round($surface.H * $s.Value)
        $c = Render-Lockup $w $h
        Save-Png $c.Bitmap (Join-Path $logosDir "$($surface.Name).$($s.Key).png")
        if ($s.Key -eq 'scale-100') { Save-Png $c.Bitmap (Join-Path $logosDir "$($surface.Name).png") }
        $c.Graphics.Dispose(); $c.Bitmap.Dispose()
    }
}

$lockupBmp.Dispose(); $iconBmp.Dispose(); $keyed.Dispose(); $plain.Dispose()

$count = (Get-ChildItem $logosDir -Filter '*.png').Count
Write-Host "[done] $count branded PNGs written to $logosDir" -ForegroundColor Green
Write-Host "[note] Sources: $Lockup (glyph + wordmark, navy keyed onto #0f182b) and $AppIcon (Square44x44 family, transparent)." -ForegroundColor Cyan
Write-Host "[next] powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Verify" -ForegroundColor Cyan
