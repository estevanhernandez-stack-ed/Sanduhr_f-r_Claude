# Sanduhr -- MSIX build pipeline (makeappx, no wapproj -- spec > Release).
#
# Two flavors:
#   -Store    : unsigned (Partner Center signs after submission). The upload artifact.
#   -Sideload : self-signed via a dev cert whose subject CN matches the manifest Publisher.
#               For local sideload testing only -- never submitted to the Store.
#   -Verify   : run the logo gate + manifest sanity check only; build nothing.
#
# Run from repo root (Sanduhr/):
#   powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Store
#   powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword 'pwd'
#   powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Verify
#
# The Store flavor leaves the MSIX UNSIGNED on purpose -- Store ingestion re-signs with the
# Microsoft publisher cert. Self-signing a Store upload fails ingestion.
#
# Logo gate (pattern x): fails fast if any required asset is missing or looks like a stub.
# These are PLACEHOLDERS until the 626labs-design skill produces branded tiles -- see
# src/Sanduhr.App/Package/Logos/README.md. -AllowPlaceholders bypasses for early-dev smoke only.

[CmdletBinding(DefaultParameterSetName = 'Store')]
param(
    [Parameter(ParameterSetName = 'Store')]
    [switch]$Store,

    [Parameter(ParameterSetName = 'Sideload')]
    [switch]$Sideload,

    [Parameter(ParameterSetName = 'Sideload', Mandatory = $true)]
    [string]$CertPath,

    [Parameter(ParameterSetName = 'Sideload', Mandatory = $true)]
    [string]$CertPassword,

    [Parameter(ParameterSetName = 'Verify')]
    [switch]$Verify,

    # Optional override. Defaults to <Version> in Sanduhr.App.csproj (single source of truth).
    # Must be 4-part with the 4th component .0 (Partner Center rejects non-zero revisions).
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$AllowPlaceholders
)

$ErrorActionPreference = 'Stop'
$repoRoot     = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appDir       = Join-Path $repoRoot 'windows-dotnet\src\Sanduhr.App'
$appProject   = Join-Path $appDir 'Sanduhr.App.csproj'
$manifestPath = Join-Path $appDir 'Package.appxmanifest'
$logosDir     = Join-Path $appDir 'Package\Logos'
$outDir       = Join-Path $repoRoot 'windows-dotnet\dist'

$requiredLogos = @(
    'StoreLogo.png', 'Square44x44Logo.png', 'Square71x71Logo.png',
    'Square150x150Logo.png', 'Square310x310Logo.png', 'Wide310x150Logo.png', 'SplashScreen.png'
)

function Test-LogosPresent {
    param([switch]$AllowPlaceholders)
    $missing = @(); $suspicious = @()
    foreach ($name in $requiredLogos) {
        $path = Join-Path $logosDir $name
        if (-not (Test-Path $path)) { $missing += $name; continue }
        if ((Get-Item $path).Length -lt 200) { $suspicious += "$name (under 200 bytes -- stub)"; continue }
    }
    if ($missing.Count)    { Write-Host "[logos] MISSING:"    -ForegroundColor Red;    $missing    | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red } }
    if ($suspicious.Count) { Write-Host "[logos] SUSPICIOUS:" -ForegroundColor Yellow; $suspicious | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow } }
    if (($missing.Count -or $suspicious.Count) -and -not $AllowPlaceholders) {
        Write-Host ''
        Write-Host '[logos] FAIL -- run windows-dotnet/scripts/generate-store-assets.ps1, then replace with' -ForegroundColor Red
        Write-Host '[logos] branded art via the 626labs-design skill. See Package/Logos/README.md.' -ForegroundColor Red
        Write-Host '[logos] Early-dev bypass only: pass -AllowPlaceholders.' -ForegroundColor Red
        return $false
    }
    if ($AllowPlaceholders -and ($missing.Count -or $suspicious.Count)) {
        Write-Host '[logos] WARN -- building with -AllowPlaceholders. DO NOT submit this to the Store.' -ForegroundColor Yellow
    } else {
        Write-Host "[logos] OK -- all $($requiredLogos.Count) required assets present." -ForegroundColor Green
        Write-Host '[logos] NOTE -- these may still be PLACEHOLDERS. Confirm branded art before submit.' -ForegroundColor Yellow
    }
    return $true
}

function Get-SdkTool {
    param([string]$Tool)
    $root = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $root) {
        $hit = Get-ChildItem $root -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$Tool" } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    throw "$Tool not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK.10.0.26100)."
}

function Resolve-Version {
    if ($Version) { $v = $Version }
    else {
        $csproj = Get-Content $appProject -Raw
        if ($csproj -match '<Version>([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)</Version>') { $v = $Matches[1] }
        else { throw "Could not read a 4-part <Version> from $appProject -- pass -Version explicitly." }
    }
    if ($v -notmatch '^\d+\.\d+\.\d+\.0$') {
        throw "Version '$v' must be 4-part with the 4th component .0 (Store rejects non-zero revisions)."
    }
    return $v
}

# === Verify mode. ===
if ($Verify) {
    Write-Host '[verify] Logo gate...' -ForegroundColor Cyan
    if (-not (Test-LogosPresent)) { exit 1 }
    Write-Host '[verify] Manifest...' -ForegroundColor Cyan
    if (-not (Test-Path $manifestPath)) { Write-Host "[verify] FAIL -- $manifestPath missing." -ForegroundColor Red; exit 1 }
    $null = Resolve-Version
    Write-Host "[verify] OK -- version $(Resolve-Version)." -ForegroundColor Green
    exit 0
}

$ver = Resolve-Version
$flavor = if ($Sideload) { 'Sideload' } else { 'Store' }
Write-Host "[build-msix] Sanduhr v$ver ($flavor / $Configuration / $Runtime)" -ForegroundColor Cyan

# === Logo gate. ===
if (-not (Test-LogosPresent -AllowPlaceholders:$AllowPlaceholders)) { exit 1 }

# === Publish (self-contained: bundles .NET 10 Desktop Runtime so the MSIX installs on a fresh
#     Win11 box without a separate runtime install). ===
$publishDir = Join-Path $outDir 'publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "[build-msix] Publishing..." -ForegroundColor Cyan
& dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir `
    /p:Version=$ver `
    /p:PublishReadyToRun=true `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] dotnet publish FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }

# === Stage publish output + manifest (version-patched) + logos into one tree for makeappx. ===
$stage = Join-Path $outDir 'staging'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$publishDir\*" $stage -Recurse -Force

# Patch the Identity Version into the STAGED manifest copy (committed manifest stays untouched).
[xml]$m = Get-Content $manifestPath
$m.Package.Identity.Version = $ver
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$ws = New-Object System.Xml.XmlWriterSettings
$ws.Indent = $true; $ws.IndentChars = '  '; $ws.Encoding = $utf8NoBom
$w = [System.Xml.XmlWriter]::Create((Join-Path $stage 'AppxManifest.xml'), $ws)
try { $m.Save($w) } finally { $w.Close() }

$stageLogos = Join-Path $stage 'Package\Logos'
New-Item -ItemType Directory -Path $stageLogos -Force | Out-Null
Copy-Item "$logosDir\*.png" $stageLogos -Force

# === Pack. ===
$makeAppx = Get-SdkTool 'makeappx.exe'
$msixPath = Join-Path $outDir "Sanduhr-$flavor-v$ver.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }
Write-Host "[build-msix] Packing $msixPath..." -ForegroundColor Cyan
& $makeAppx pack /v /d $stage /p $msixPath /o
if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] makeappx pack FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }

# === Sign (sideload only). The Store flavor stays unsigned for Partner Center ingestion. ===
if ($Sideload) {
    if (-not (Test-Path $CertPath)) { Write-Host "[build-msix] Cert not found: $CertPath" -ForegroundColor Red; exit 1 }
    $signTool = Get-SdkTool 'signtool.exe'
    Write-Host "[build-msix] Signing (sideload)..." -ForegroundColor Cyan
    & $signTool sign /v /f $CertPath /p $CertPassword /td sha256 /fd sha256 $msixPath
    if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] signtool FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }
}

$sizeMB = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
Write-Host ''
Write-Host "[build-msix] DONE: $msixPath ($sizeMB MB)" -ForegroundColor Green
if ($Sideload) {
    Write-Host '[build-msix] Sideload: import the dev cert into Trusted People, then Add-AppxPackage.' -ForegroundColor Cyan
} else {
    Write-Host '[build-msix] Store: upload UNSIGNED to Partner Center -- ingestion re-signs.' -ForegroundColor Cyan
}
