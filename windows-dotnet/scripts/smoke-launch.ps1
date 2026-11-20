<#
.SYNOPSIS
  Start a published Sanduhr.exe, confirm it shows a window and stays up, then close it.

.DESCRIPTION
  The closest thing to "someone opened it" a runner can do. It starts the app in demo mode
  (sample numbers, nothing fetched), waits for a visible window, holds, and fails when the
  process exits early, never shows a window, or leaves a crash in the Application event log.

  CI RUNNERS ONLY. A Sanduhr started from a publish folder uses the same data folders as an
  installed Sanduhr. Never run this on a PC that has Sanduhr installed.

.PARAMETER NoWindowCheck
  For a runner that cannot show a window: pass on the process staying up alone.

.EXAMPLE
  pwsh windows-dotnet/scripts/smoke-launch.ps1 -Exe windows-dotnet/dist/publish-arm64/Sanduhr.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [int]$HoldSeconds = 15,
    [int]$WindowTimeoutSeconds = 60,
    [switch]$NoWindowCheck
)

$ErrorActionPreference = 'Stop'
$Exe = [System.IO.Path]::GetFullPath($Exe)
if (-not (Test-Path -LiteralPath $Exe)) { throw "No app at $Exe." }

# Sanduhr is single-instance: a second copy hands off to the first and exits, which would read as a crash here.
if (Get-Process -Name Sanduhr -ErrorAction SilentlyContinue) { throw 'A Sanduhr is already running. Close it first.' }

Add-Type -Namespace SmokeLaunch -Name Win -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
public static int VisibleWindows(uint pid) {
    int n = 0;
    EnumWindows(delegate(System.IntPtr h, System.IntPtr l) {
        uint p;
        GetWindowThreadProcessId(h, out p);
        if (p == pid && IsWindowVisible(h)) n++;
        return true;
    }, System.IntPtr.Zero);
    return n;
}
'@

$started = Get-Date
$p = Start-Process -FilePath $Exe -ArgumentList '--demo' -PassThru
try {
    $deadline = (Get-Date).AddSeconds($WindowTimeoutSeconds)
    $seen = $false
    while ((Get-Date) -lt $deadline) {
        $p.Refresh()
        if ($p.HasExited) { throw "Sanduhr exited during startup (code $($p.ExitCode))." }
        if ($NoWindowCheck -or ([SmokeLaunch.Win]::VisibleWindows([uint32]$p.Id) -gt 0)) { $seen = $true; break }
        [System.Threading.Thread]::Sleep(500)
    }
    if (-not $seen) { throw "Sanduhr showed no window within $WindowTimeoutSeconds seconds." }

    [System.Threading.Thread]::Sleep($HoldSeconds * 1000)
    $p.Refresh()
    if ($p.HasExited) { throw "Sanduhr exited within $HoldSeconds seconds of starting (code $($p.ExitCode))." }
    if (-not $NoWindowCheck -and ([SmokeLaunch.Win]::VisibleWindows([uint32]$p.Id) -eq 0)) { throw "Sanduhr's window went away." }
}
finally {
    $p.Refresh()
    if (-not $p.HasExited) { & taskkill.exe /PID $p.Id /T /F | Out-Null }
}

# A hard crash leaves an event even when a dialog kept the process alive.
$crashes = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = $started } -ErrorAction SilentlyContinue |
    Where-Object { ($_.ProviderName -eq '.NET Runtime' -or $_.ProviderName -eq 'Application Error') -and $_.Message -like '*Sanduhr.exe*' })
if ($crashes.Count -gt 0) {
    $first = ($crashes[0].Message -split "`n" | Select-Object -First 6) -join "`n"
    throw "Sanduhr left $($crashes.Count) crash event(s). First:`n$first"
}

Write-Output "[launch] OK: $Exe started, showed a window and stayed up $HoldSeconds seconds."
