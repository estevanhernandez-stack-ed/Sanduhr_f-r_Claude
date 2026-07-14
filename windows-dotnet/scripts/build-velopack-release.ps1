# Sanduhr -- build a Velopack Setup.exe + delta package for GitHub Releases.
#
# Produces a self-contained, no-runtime-install installer users download from the Releases page.
# Without a code-sign cert, SmartScreen warns ("More info -> Run anyway") on first install --
# that's the trade for the free GitHub channel. The Store MSIX (build-msix.ps1) is the signed path.
#
# Output (under windows-dotnet/dist/release/):
#   - 626Labs.Sanduhr-win-Setup.exe        installer users download
#   - 626Labs.Sanduhr-<v>-full.nupkg       full package (auto-updater consumes this)
#   - 626Labs.Sanduhr-<v>-delta.nupkg      delta vs prior release (release #2+)
#   - 626Labs.Sanduhr-win-Portable.zip     no-install portable flavor
#   - releases.win.json                    manifest the in-app UpdateChecker pings
#
# Run from repo root (Sanduhr/):
#   pwsh windows-dotnet/scripts/build-velopack-release.ps1 -Version 3.0.0.0
#
# Requires:
#   - .NET 10 SDK
#   - vpk  (install: dotnet tool install -g vpk)
#   - Square44x44Logo.targetsize-{16,24,32,48,256}.png under src/Sanduhr.App/Package/Logos/
#     (run scripts/generate-store-assets.ps1 first if missing).
#
# Upload flow (manual): see docs/release-runbook.md -- the GitHub release MUST carry EVERY file
# from dist/release/, not just Setup.exe (the auto-updater needs releases.win.json + *-full.nupkg).

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.0)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ReleaseNotes,
    [switch]$SkipPublish,
    [switch]$SkipIco,
    # CI: keep dist/release/ contents (prior nupkgs from `vpk download github`) so vpk can delta.
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

# vpk --packVersion wants 3-part SemVer2; the assembly + tag use the 4-part .NET version. Strip
# the 4th segment for vpk only.
$packVersion = ($Version -split '\.')[0..2] -join '.'

$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appProject = Join-Path $repoRoot 'windows-dotnet\src\Sanduhr.App\Sanduhr.App.csproj'
$publishDir = Join-Path $repoRoot 'windows-dotnet\dist\publish-velopack'
$releaseDir = Join-Path $repoRoot 'windows-dotnet\dist\release'
$logosDir   = Join-Path $repoRoot 'windows-dotnet\src\Sanduhr.App\Package\Logos'
$icoOutPath = Join-Path $logosDir 'AppIcon.ico'

Write-Host "[velopack] Sanduhr v$Version ($Runtime, $Configuration) -- packVersion=$packVersion" -ForegroundColor Cyan

# ----- 1. Pre-flight ----------------------------------------------------------
if (-not (Get-Command vpk -ErrorAction SilentlyContinue))    { throw "vpk not found. Install: dotnet tool install -g vpk" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found on PATH." }
if (-not (Test-Path $appProject)) { throw "Project not found: $appProject" }

# ----- 2. AppIcon.ico (multi-size, PNG-encoded) from the targetsize PNGs -------
if (-not $SkipIco) {
    $sizes = @(16, 24, 32, 48, 256)
    $sources = $sizes | ForEach-Object { Join-Path $logosDir "Square44x44Logo.targetsize-$_.png" }
    foreach ($s in $sources) {
        if (-not (Test-Path $s)) { throw "Missing icon source: $s. Run scripts/generate-store-assets.ps1 first." }
    }
    $stream = [System.IO.File]::Open($icoOutPath, [System.IO.FileMode]::Create)
    try {
        $bw = New-Object System.IO.BinaryWriter($stream)
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)   # ICONDIR
        $pngBytes = @($sources | ForEach-Object { ,([System.IO.File]::ReadAllBytes($_)) })
        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]; $b = $pngBytes[$i]
            $bw.Write([byte]($sz -band 0xFF)); $bw.Write([byte]($sz -band 0xFF))       # W, H (256->0)
            $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$b.Length); $bw.Write([uint32]$offset); $offset += $b.Length
        }
        for ($i = 0; $i -lt $sizes.Count; $i++) { $bw.Write($pngBytes[$i]) }
        $bw.Flush()
    } finally { $stream.Dispose() }
    Write-Host "[ico] Wrote $icoOutPath ($($sizes -join ',') px)" -ForegroundColor Cyan
}

# ----- 3. dotnet publish (self-contained; single-file OFF so Velopack can delta per-file) ------
if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    Write-Host "[publish] dotnet publish ($Runtime, self-contained)..." -ForegroundColor Cyan
    & dotnet publish $appProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir `
        /p:Version=$Version `
        /p:PublishSingleFile=false `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}
# AssemblyName is 'Sanduhr', so the published exe is Sanduhr.exe (matches --mainExe + the manifest).
if (-not (Test-Path (Join-Path $publishDir 'Sanduhr.exe'))) {
    throw "Publish output missing Sanduhr.exe at $publishDir. Re-run without -SkipPublish."
}

# ----- 4. vpk pack ------------------------------------------------------------
if (-not $NoClean -and (Test-Path $releaseDir)) { Remove-Item -Recurse -Force $releaseDir }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# packId MUST NOT be 'Sanduhr': Velopack installs to %LOCALAPPDATA%\{packId}, and
# %LOCALAPPDATA%\Sanduhr is the app's DATA directory (the usage vault lives there).
# Velopack's installer renames a pre-existing install dir for rollback and deletes it
# on success — verified 2026-07-13: an install over existing data DESTROYS the vault.
# '626Labs.Sanduhr' keeps the install tree permanently disjoint from the data tree.
# Frozen once the first real install exists — changing it orphans updaters.
$vpkArgs = @(
    'pack'
    '--packId',      '626Labs.Sanduhr'
    '--packVersion', $packVersion
    '--packDir',     $publishDir
    '--mainExe',     'Sanduhr.exe'
    '--packTitle',   'Sanduhr für Claude'
    '--packAuthors', '626 Labs'
    '--icon',        $icoOutPath
    '--outputDir',   $releaseDir
    '--delta',       'BestSpeed'
)
if ($ReleaseNotes -and (Test-Path $ReleaseNotes)) { $vpkArgs += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) }

Write-Host "[vpk] vpk $($vpkArgs -join ' ')" -ForegroundColor Cyan
& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

# ----- 5. Summary -------------------------------------------------------------
Write-Host ""
Write-Host "Built v$Version -> $releaseDir" -ForegroundColor Green
Get-ChildItem $releaseDir -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-46} {1,8} MB" -f $_.Name, [math]::Round($_.Length / 1MB, 1))
}
Write-Host ""
Write-Host "Next: tag v$Version, draft a GitHub release, upload EVERY file from dist/release/." -ForegroundColor Yellow
Write-Host "      (releases.win.json + *-full.nupkg are what the in-app auto-updater pings.)" -ForegroundColor Yellow
