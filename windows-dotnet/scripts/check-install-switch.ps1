<#
.SYNOPSIS
  On an Arm runner: install the x64 Sanduhr, then the arm64 one over it, and check the switch.

.DESCRIPTION
  An Arm PC that installed Sanduhr from GitHub before 5.0 has the x64 build. This is what that
  person does to get the native one, run for real:
    1. Install the x64 installer. On an Arm PC that Sanduhr runs through emulation, so it must
       record that the Arm build notice was due.
    2. Put a marker file in each of Sanduhr's data folders.
    3. Install the arm64 installer (in place, or after an uninstall with -UninstallFirst).
       The installed app must be arm64, its package's channel must be win-arm64 (the feed it
       will update from), the markers must still be there, the app must start, and it must not
       record the notice as due.

  CI RUNNERS ONLY, and enforced. It installs, launches and replaces Sanduhr, kills running copies,
  writes markers into the real data folders and deletes the real log. So before it stops, installs,
  writes or deletes anything, it refuses to run unless GITHUB_ACTIONS is 'true' (a GitHub Actions
  runner), and then refuses again if a Velopack install is already present. Neither refusal can
  see a Store or portable copy, which is why the first one does not depend on finding an install.

.EXAMPLE
  pwsh windows-dotnet/scripts/check-install-switch.ps1 -X64Setup dist/release/626Labs.Sanduhr-win-Setup.exe -Arm64Setup dist/release/626Labs.Sanduhr-win-arm64-Setup.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$X64Setup,
    [Parameter(Mandatory)][string]$Arm64Setup,
    [switch]$UninstallFirst
)

$ErrorActionPreference = 'Stop'
$X64Setup = (Resolve-Path -LiteralPath $X64Setup).Path
$Arm64Setup = (Resolve-Path -LiteralPath $Arm64Setup).Path
if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'This script installs, launches and uninstalls Sanduhr and writes into its data folders. It runs on CI runners only (GITHUB_ACTIONS is not true here); nothing was stopped, installed or changed.'
}

$installRoot = Join-Path $env:LOCALAPPDATA '626Labs.Sanduhr'
$current = Join-Path $installRoot 'current'
$exe = Join-Path $current 'Sanduhr.exe'
$dataDirs = @((Join-Path $env:APPDATA 'Sanduhr'), (Join-Path $env:LOCALAPPDATA 'Sanduhr'), (Join-Path $env:USERPROFILE '.sanduhr'))
$logFile = Join-Path $env:APPDATA 'Sanduhr\sanduhr.log'
$dueLine = 'arm build notice: due'
$onArm = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') -or ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64')

function Stop-Sanduhr {
    foreach ($proc in @(Get-Process -Name Sanduhr -ErrorAction SilentlyContinue)) { & taskkill.exe /PID $proc.Id /T /F | Out-Null }
    [System.Threading.Thread]::Sleep(2000)
}

function Wait-ForExit {
    param($Process, [int]$Seconds, [string]$What)
    $null = $Process.Handle   # hold the handle, or ExitCode is empty once the process has gone
    if (-not $Process.WaitForExit($Seconds * 1000)) {
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
        throw "$What did not finish in $Seconds seconds."
    }
    if ($Process.ExitCode -ne 0) { throw "$What exited with code $($Process.ExitCode)." }
}

function Install-Setup {
    param([string]$Setup)
    $name = [System.IO.Path]::GetFileName($Setup)
    Write-Output "[switch] installing $name"
    Wait-ForExit (Start-Process -FilePath $Setup -ArgumentList '--silent' -PassThru) 300 $name
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $exe) -and ((Get-Date) -lt $deadline)) { [System.Threading.Thread]::Sleep(1000) }
    if (-not (Test-Path -LiteralPath $exe)) { throw "No $exe after $name." }
    # Setup may start the app when it finishes; the documentation does not say. Give a started app time to
    # settle and say so, then stop it, so every launch below is this script's own.
    [System.Threading.Thread]::Sleep(10000)
    if (Get-Process -Name Sanduhr -ErrorAction SilentlyContinue) { Write-Output "[switch] note: $name started the app after a silent install" }
    else { Write-Output "[switch] note: $name did not start the app after a silent install" }
    Stop-Sanduhr
}

function Assert-Installed {
    param([string]$Runtime, [string]$Channel)
    & (Join-Path $PSScriptRoot 'check-architecture.ps1') -Dir $current -Runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw "The installed copy is not $Runtime." }
    [xml]$record = Get-Content -LiteralPath (Join-Path $current 'sq.version') -Raw
    $meta = $record.package.metadata
    if ($meta.channel -ne $Channel) { throw "The installed package's channel is '$($meta.channel)', not '$Channel'." }
    Write-Output "[switch] installed: version $($meta.version), channel $($meta.channel), rid $($meta.rid), machineArchitecture $($meta.machineArchitecture)"
}

function Test-NoticeDue {
    return (Test-Path -LiteralPath $logFile) -and [bool](Select-String -LiteralPath $logFile -SimpleMatch $dueLine -Quiet)
}

if (Test-Path -LiteralPath $installRoot) { throw "Sanduhr is already installed at $installRoot. This check is for a clean runner." }
Stop-Sanduhr

# 1. The x64 build. On an Arm PC it runs through emulation and must say the notice was due.
Install-Setup $X64Setup
Assert-Installed -Runtime 'win-x64' -Channel 'win'
& (Join-Path $PSScriptRoot 'smoke-launch.ps1') -Exe $exe
if ($onArm) {
    if (-not (Test-NoticeDue)) { throw 'The x64 Sanduhr on an Arm PC did not record that the Arm build notice was due.' }
    Write-Output '[switch] the x64 build on Arm recorded the notice as due'
} else {
    Write-Output '[switch] not an Arm machine: the notice is not checked'
}

# 2. Markers in every data folder, and a fresh log for the arm64 build.
foreach ($d in $dataDirs) {
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $d 'arm-check-marker.txt') -Value 'marker'
}
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

# 3. The arm64 build over it.
if ($UninstallFirst) {
    Write-Output '[switch] uninstalling the x64 build first'
    Wait-ForExit (Start-Process -FilePath (Join-Path $installRoot 'Update.exe') -ArgumentList 'uninstall', '--silent' -PassThru) 120 'Update.exe uninstall'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Test-Path -LiteralPath $current) -and ((Get-Date) -lt $deadline)) { [System.Threading.Thread]::Sleep(1000) }
    if (Test-Path -LiteralPath $current) { throw 'The x64 build is still installed after the uninstall.' }
}
Install-Setup $Arm64Setup
Assert-Installed -Runtime 'win-arm64' -Channel 'win-arm64'
foreach ($d in $dataDirs) {
    if (-not (Test-Path -LiteralPath (Join-Path $d 'arm-check-marker.txt'))) { throw "The marker in $d did not survive the switch." }
}
Write-Output '[switch] every data folder kept its marker'
& (Join-Path $PSScriptRoot 'smoke-launch.ps1') -Exe $exe
if (Test-NoticeDue) { throw 'The arm64 build recorded the Arm build notice as due. It must stay silent on a native build.' }

if ($UninstallFirst) { $how = 'after an uninstall' } else { $how = 'in place' }
Write-Output "[switch] OK: x64 to arm64 $how."
