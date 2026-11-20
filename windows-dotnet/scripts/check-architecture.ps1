<#
.SYNOPSIS
  Fail when a publish folder holds a program file built for the wrong processor.

.DESCRIPTION
  Sanduhr ships for win-x64 and win-arm64. A build machine can run only one of them, so a
  wrong binary would otherwise be found by a user. This reads the CPU field in the header of
  every .dll and .exe under -Dir and fails when:
    - Sanduhr.exe or mcp\sanduhr-mcp.exe is missing or is not built for -Runtime, or
    - any file is built for the other 64-bit architecture.
  Managed, architecture-neutral files (the field reads i386) pass, and so does a .dll that is
  not a program file at all.

.EXAMPLE
  pwsh windows-dotnet/scripts/check-architecture.ps1 -Dir windows-dotnet/dist/publish-arm64 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Dir,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime
)

$ErrorActionPreference = 'Stop'
$Dir = [System.IO.Path]::GetFullPath($Dir).TrimEnd('\')
if (-not (Test-Path -LiteralPath $Dir -PathType Container)) { throw "No folder at $Dir." }

if ($Runtime -eq 'win-arm64') { $want = 'arm64'; $other = 'x64' } else { $want = 'x64'; $other = 'arm64' }

function Get-Machine {
    param([string]$Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        if ($fs.Length -lt 0x40) { return 'none' }
        $br = New-Object System.IO.BinaryReader($fs)
        if ($br.ReadUInt16() -ne 0x5A4D) { return 'none' }          # "MZ"
        $fs.Position = 0x3C
        $pe = $br.ReadInt32()
        if ($pe -le 0 -or ($pe + 6) -gt $fs.Length) { return 'none' }
        $fs.Position = $pe
        if ($br.ReadUInt32() -ne 0x00004550) { return 'none' }      # "PE\0\0"
        $machine = [int]$br.ReadUInt16()
        if ($machine -eq 0x8664) { return 'x64' }
        if ($machine -eq 0xAA64) { return 'arm64' }
        if ($machine -eq 0x014C) { return 'neutral' }
        return 'other'
    } finally { $fs.Dispose() }
}

$problems = @()
$counts = @{}
$files = @(Get-ChildItem -LiteralPath $Dir -Recurse -File | Where-Object { $_.Extension -eq '.dll' -or $_.Extension -eq '.exe' })
foreach ($f in $files) {
    $m = Get-Machine $f.FullName
    $counts[$m] = 1 + [int]$counts[$m]
    if ($m -eq $other) { $problems += ('{0} is built for {1}' -f $f.FullName.Substring($Dir.Length + 1), $other) }
}
foreach ($must in @('Sanduhr.exe', 'mcp\sanduhr-mcp.exe')) {
    $p = Join-Path $Dir $must
    if (-not (Test-Path -LiteralPath $p)) { $problems += "$must is missing"; continue }
    $m = Get-Machine $p
    if ($m -ne $want) { $problems += "$must is $m, not $want" }
}

$summary = ($counts.GetEnumerator() | Sort-Object Name | ForEach-Object { '{0} {1}' -f $_.Value, $_.Name }) -join ', '
if ($problems.Count -gt 0) {
    Write-Output "[architecture] FAIL for $Runtime in $Dir ($summary):"
    $problems | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "[architecture] OK for $Runtime ($summary)."
exit 0
