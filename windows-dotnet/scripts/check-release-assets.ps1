<#
.SYNOPSIS
  Fail when a folder of release files is missing something a Windows release must carry.

.DESCRIPTION
  A Windows release ships two Store packages, two installers, two full update packages, two
  portable zips and two update feeds. An architecture whose feed is missing never updates, and
  nothing else notices. This checks that every file is there and that each feed lists its own
  architecture's full package at this version. Delta packages are optional and not checked.

  The tag may be three-part (v5.0.0) or four-part (v5.0.0.0). Velopack's file names carry the
  three-part version; the Store packages carry the four-part one.

.EXAMPLE
  pwsh windows-dotnet/scripts/check-release-assets.ps1 -Dir draft-files -Tag v5.0.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Dir,
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+(\.\d+)?$')][string]$Tag
)

$ErrorActionPreference = 'Stop'
$Dir = [System.IO.Path]::GetFullPath($Dir)
if (-not (Test-Path -LiteralPath $Dir -PathType Container)) { throw "No folder at $Dir." }

$parts = ($Tag -replace '^v', '').Split('.')
$pack = $parts[0..2] -join '.'
if ($parts.Count -eq 4) { $store = $parts -join '.' } else { $store = "$pack.0" }

$x64Full = "626Labs.Sanduhr-${pack}-full.nupkg"
$armFull = "626Labs.Sanduhr-${pack}-win-arm64-full.nupkg"
$expected = @(
    "Sanduhr-Store-x64-v${store}.msix"
    "Sanduhr-Store-arm64-v${store}.msix"
    '626Labs.Sanduhr-win-Setup.exe'
    '626Labs.Sanduhr-win-arm64-Setup.exe'
    $x64Full
    $armFull
    '626Labs.Sanduhr-win-Portable.zip'
    '626Labs.Sanduhr-win-arm64-Portable.zip'
    'releases.win.json'
    'releases.win-arm64.json'
)

$problems = @()
foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $Dir $name))) { $problems += "$name is missing" }
}

# Each feed must list its own architecture's full package at this version.
foreach ($feed in @(@('releases.win.json', $x64Full), @('releases.win-arm64.json', $armFull))) {
    $path = Join-Path $Dir $feed[0]
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $names = @()
    try { $names = @((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).Assets | ForEach-Object { $_.FileName }) }
    catch { $problems += "$($feed[0]) is not readable JSON"; continue }
    if ($names -notcontains $feed[1]) { $problems += "$($feed[0]) does not list $($feed[1])" }
}

if ($problems.Count -gt 0) {
    Write-Output "[assets] FAIL for $Tag in ${Dir}:"
    $problems | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "[assets] OK for ${Tag}: $($expected.Count) files, both feeds list their packages."
exit 0
