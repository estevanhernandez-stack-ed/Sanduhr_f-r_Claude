<#
.SYNOPSIS
  Cold-run a published sanduhr-mcp.exe the way Claude Code spawns it and check the answers.

.DESCRIPTION
  The Store and Velopack app carries the MCP server as a self-contained, trimmed,
  single-file mcp\sanduhr-mcp.exe. Trimming removes code the linker cannot see a use
  for, so the only proof the shipped exe works is running it: this script feeds it the
  JSON-RPC lines Claude Code would (initialize, tools/list, ping, get_usage,
  get_local_burn_by_project, get_model_usage, get_usage_history, and a propose_theme that must be
  rejected without touching the widget) over stdin, with no environment overrides, and fails on
  a crash, a missing tool, or a malformed reply. It reads this machine's real
  %APPDATA%\Sanduhr paths and writes nothing.

.PARAMETER Exe
  Path to the published exe. Defaults to windows-dotnet\dist\publish\mcp\sanduhr-mcp.exe
  (what build-msix.ps1 leaves behind).

.EXAMPLE
  pwsh windows-dotnet/scripts/smoke-mcp.ps1
  pwsh windows-dotnet/scripts/smoke-mcp.ps1 -Exe windows-dotnet/dist/publish-velopack/mcp/sanduhr-mcp.exe
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\dist\publish\mcp\sanduhr-mcp.exe')
)

$ErrorActionPreference = 'Stop'
$Exe = [System.IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $Exe)) { throw "No server at $Exe. Publish first (build-msix.ps1 or build-velopack-release.ps1)." }

$requests = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-mcp","version":"0"}}}'
    '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
    '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping","arguments":{}}}'
    '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_usage","arguments":{}}}'
    '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"get_local_burn_by_project","arguments":{"window_days":1}}}'
    '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"get_model_usage","arguments":{"window_days":1}}}'
    '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"get_usage_history","arguments":{"window_days":7}}}'
    '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"propose_theme","arguments":{"apply":false,"theme":{"name":"Smoke","bg":"#0d0d0d","glass":"#1c1c1c","glass_on_mica":"#1a1a1c","title_bg":"#161616","border":"#333333","footer_bg":"#111111","bar_bg":"#2a2a2a","text":"#e8e4dc","text_secondary":"#b8b4ac","text_dim":"#777777","text_muted":"#555555","accent":"not-a-color","pace_marker":"#ff6b6b","sparkline":"#6c63ff"}}}}'
)

$psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
# StandardInputEncoding exists on .NET (pwsh) only; the requests are ASCII, so 5.1's default is fine.
if ($psi.PSObject.Properties['StandardInputEncoding']) { $psi.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false) }
$psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$psi.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)

$p = [System.Diagnostics.Process]::Start($psi)
foreach ($r in $requests) { $p.StandardInput.WriteLine($r) }
$p.StandardInput.Close()
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'sanduhr-mcp did not exit within 30s after stdin closed.' }

Write-Host "[smoke-mcp] $Exe ($([math]::Round((Get-Item $Exe).Length / 1MB, 1)) MB) exit $($p.ExitCode)"
if ($p.ExitCode -ne 0) {
    Write-Host $stderr -ForegroundColor Red
    throw "sanduhr-mcp crashed (exit $($p.ExitCode)). A trimmed-away code path is the usual cause; see the linker rule in ToolCatalog.cs."
}

$replies = @{}
foreach ($line in ($stdout -split "`n")) {
    if (-not $line.Trim()) { continue }
    $msg = $line | ConvertFrom-Json
    if ($null -ne $msg.id) { $replies[[int]$msg.id] = $msg }
}
foreach ($id in 1..8) {
    if (-not $replies.ContainsKey($id)) { throw "No reply for request id $id." }
    if ($replies[$id].PSObject.Properties['error']) { throw "Request $id returned an error: $($replies[$id].error | ConvertTo-Json -Compress)" }
}

$server = $replies[1].result.serverInfo
Write-Host "[smoke-mcp] initialize: $($server.name) $($server.version)"

$tools = @($replies[2].result.tools | ForEach-Object name)
$expected = @('get_usage', 'get_local_burn_by_project', 'get_model_usage', 'get_usage_history', 'ping', 'publish_usage', 'propose_theme')
$missing = @($expected | Where-Object { $tools -notcontains $_ })
if ($missing.Count) { throw "tools/list is missing: $($missing -join ', ') (got: $($tools -join ', '))" }
Write-Host "[smoke-mcp] tools/list: $($tools -join ', ')"

$ping = $replies[3].result.content[0].text | ConvertFrom-Json
Write-Host "[smoke-mcp] ping: server $($ping.server_version), snapshot writer $($ping.snapshot_writer_version), floor $($ping.required_widget_version), meets_floor=$($ping.snapshot_writer_meets_floor), usage_status=$($ping.usage_status)"
if ($ping.server_version -ne $server.version) { throw "ping.server_version ($($ping.server_version)) differs from initialize ($($server.version))." }

$usage = $replies[4].result.content[0].text | ConvertFrom-Json
Write-Host "[smoke-mcp] get_usage: status=$($usage.status) reason=$($usage.reason)"
if (-not $usage.status) { throw 'get_usage returned no status.' }

$burn = $replies[5].result.content[0].text | ConvertFrom-Json
Write-Host "[smoke-mcp] get_local_burn_by_project: status=$($burn.status) roots_scanned=$(@($burn.roots_scanned).Count) window_days=$($burn.window_days)"
if (-not $burn.status) { throw 'get_local_burn_by_project returned no status.' }

$models = $replies[6].result.content[0].text | ConvertFrom-Json
Write-Host "[smoke-mcp] get_model_usage: status=$($models.status) models=$(@($models.models).Count) meter_source=$(if ($models.meter_source) { $models.meter_source.status } else { 'none' })"
if (-not $models.status) { throw 'get_model_usage returned no status.' }

$history = $replies[7].result.content[0].text | ConvertFrom-Json
Write-Host "[smoke-mcp] get_usage_history: status=$($history.status) reason=$($history.reason) days_recorded=$($history.days_recorded)"
if (-not $history.status) { throw 'get_usage_history returned no status.' }

# A broken palette must be refused by the server's own lint: no request file, no widget involved.
$theme = $replies[8].result.content[0].text | ConvertFrom-Json
$fields = @($theme.findings | ForEach-Object field)
Write-Host "[smoke-mcp] propose_theme (broken accent): status=$($theme.status) reason=$($theme.reason) findings=$($fields -join ',')"
if ($theme.status -ne 'rejected' -or $fields -notcontains 'accent') { throw 'propose_theme did not reject the broken palette on the accent field.' }
if (Test-Path (Join-Path $env:APPDATA 'Sanduhr\theme-request.json')) { throw 'propose_theme wrote a request for a palette it should have rejected.' }

Write-Host '[smoke-mcp] OK' -ForegroundColor Green
