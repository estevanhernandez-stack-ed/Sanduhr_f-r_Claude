param(
  [Parameter(Mandatory = $true)][string]$Marker,
  [Parameter(Mandatory = $true)][string]$Out
)
# Captures the main window of the VS Code instance whose command line carries $Marker
# (its unique --user-data-dir), and nothing else. Uses PrintWindow, so overlapping windows
# do not matter.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinCap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@

$procs = Get-CimInstance Win32_Process -Filter "Name='Code.exe'" |
  Where-Object { $_.CommandLine -and $_.CommandLine.Contains($Marker) -and $_.CommandLine -notmatch '--type=' }
if (-not $procs) { Write-Error "no test VS Code process found for the marker"; exit 2 }

$hwnd = [IntPtr]::Zero
foreach ($p in $procs) {
  $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
  if ($gp -and $gp.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $gp.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "test VS Code has no main window"; exit 3 }

$r = New-Object WinCap+RECT
[void][WinCap]::GetWindowRect($hwnd, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinCap]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
if (-not $ok) { Write-Error "PrintWindow failed"; exit 4 }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "captured ${w}x${h} -> $Out"
