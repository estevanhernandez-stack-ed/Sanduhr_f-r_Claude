<#
  Builds and runs the under-icons spike for 3 minutes (cart run mac-parity-3-6-0, item 1), then prints
  the click log. Never touches Explorer, never moves or covers windows, sends no input.

  Usage (from anywhere):
    pwsh docs/cart-runs/2026-10-21-mac-parity-3-6-0/spike/run-spike.ps1
    pwsh .../run-spike.ps1 -Seconds 60 -X 3700 -Y 700 -Mode attr -SimulateRestart

  -Mode attr (default) | ulw | plain   how the WorkerW child is painted; attr is the one that showed pixels on Neb
  -X / -Y   piece position in screen pixels (default: top-left of the primary work area, where icons start)
#>
param(
    [int]$Seconds = 180,
    [ValidateSet('attr', 'ulw', 'plain')][string]$Mode = 'attr',
    [Nullable[int]]$X = $null,
    [Nullable[int]]$Y = $null,
    [switch]$SimulateRestart
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$project = Join-Path $repo 'windows-dotnet\spikes\under-icons'
$log = Join-Path $here 'under-icons-run.log'
$shot = Join-Path $here 'screen-run.png'

Write-Host "Building $project"
dotnet build $project -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$argsList = @('--seconds', $Seconds, '--piece-mode', $Mode, '--log', $log, '--shot', $shot)
if ($null -ne $X) { $argsList += @('--x', $X) }
if ($null -ne $Y) { $argsList += @('--y', $Y) }
if ($SimulateRestart) { $argsList += '--simulate-restart' }

Write-Host "Running the spike for $Seconds s (Ctrl+C ends it early and cleans up). Log: $log"
dotnet run --project $project -c Debug --no-build -- @argsList
$code = $LASTEXITCODE

Write-Host ''
Write-Host '=== Click log (CLICK lines) ==='
$clicks = Select-String -Path $log -CaseSensitive -Pattern 'CLICK [A-Z]+ on click layer|PIECE received' | ForEach-Object { $_.Line }
if ($clicks) { $clicks } else { Write-Host '(no clicks reached the click layer)' }
Write-Host ''
Write-Host '=== Rebuilds and results ==='
Select-String -Path $log -Pattern 'TaskbarCreated|rebuild ok|rebuild gave up|screenshot:|exit:' | ForEach-Object { $_.Line }
Write-Host ''
Write-Host "Full log: $log"
exit $code
