namespace Sanduhr.Core;

/// <summary>
/// The Claude Code statusline script, embedded as source so the widget is the
/// script's updater (the script has no update channel of its own — WS-E). The
/// installer writes this to <see cref="Paths.StatuslineScriptFile"/> with a
/// UTF-8 BOM (Windows PowerShell 5.1 mis-decodes BOM-less UTF-8; the body is
/// ASCII-only today, the BOM guards any future non-ASCII edit).
///
/// The script mirrors <see cref="SnapshotContract"/>'s staleness bands and
/// schema guard — a Core test pins the embedded constants to the contract so
/// they can't drift apart silently.
/// </summary>
public static class StatuslineScript
{
    /// <summary>Bumped when the script body changes; the installer re-writes the
    /// script whenever the installed copy differs from this source.</summary>
    public const int Version = 2;

    /// <summary>PowerShell 5.1-compatible script body. Reads the snapshot per the
    /// reader contract (one-shot read, any parse failure = missing), renders per
    /// the staleness table: fresh = numbers; stale = numbers + age suffix; dead =
    /// an explicit "start widget" line (never blank — blank is the UNINSTALLED
    /// look); missing/malformed = empty output.</summary>
    public const string Content = """
# Sanduhr statusline for Claude Code (snapshot schema v1).
# Installed and kept up to date by the Sanduhr widget. Do not edit in place.
# Reads %APPDATA%\Sanduhr\snapshot.json (raw facts only); age and reset times
# are derived here at render time so they can never go stale on disk.
$ErrorActionPreference = 'SilentlyContinue'
$snapPath = Join-Path $env:APPDATA 'Sanduhr\snapshot.json'
if (-not (Test-Path -LiteralPath $snapPath)) { exit 0 }  # missing = invisible (the uninstalled look)
try { $snap = Get-Content -LiteralPath $snapPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop } catch { exit 0 }
if ($null -eq $snap) { exit 0 }
if ([int]$snap.schema_version -gt 1) { Write-Output 'sanduhr: update statusline'; exit 0 }

$now = [DateTimeOffset]::UtcNow
try { $captured = ([DateTimeOffset]::Parse($snap.captured_at, [System.Globalization.CultureInfo]::InvariantCulture)).ToUniversalTime() } catch { exit 0 }
$age = [Math]::Max(0, [int]($now - $captured).TotalSeconds)

# Dead band (two missed polls): the widget is not polling. Say so - numbers
# this old rendered plainly would be a silent lie.
if ($age -gt 900) {
  Write-Output ('sanduhr: stale {0}m - start widget' -f [int][Math]::Floor($age / 60))
  exit 0
}

$labels = @{ five_hour = '5h'; seven_day = 'wk' }
$parts = @()
$resetSuffix = $null
foreach ($tier in @($snap.tiers)) {
  $key = [string]$tier.key
  $label = $labels[$key]
  if (-not $label) {
    # Per-model weeklies join the line only when hot (>= 80%) - keeps it short.
    if ($key -notlike 'seven_day_*') { continue }
    if ($null -eq $tier.utilization -or [int]$tier.utilization -lt 80) { continue }
    $label = ($key.Substring(10) -replace '_', ' ')
  }
  if ($null -eq $tier.utilization) { continue }
  $crossed = $false
  if ($tier.resets_at) {
    try {
      $resetAt = [DateTimeOffset]::Parse($tier.resets_at, [System.Globalization.CultureInfo]::InvariantCulture)
      if ($resetAt.ToUniversalTime() -le $now) {
        $crossed = $true   # reset boundary crossed: the stored % is arbitrarily wrong - suppress the tier
      } elseif ($key -eq 'seven_day') {
        $local = $resetAt.ToLocalTime()
        $h = if ($local.Minute -eq 0) { $local.ToString('%h') } else { $local.ToString('h:mm') }
        $ampm = if ($local.Hour -lt 12) { 'a' } else { 'p' }
        $resetSuffix = ('wk resets {0} {1}{2}' -f $local.ToString('ddd'), $h, $ampm)
      }
    } catch { }
  }
  if ($crossed) { continue }
  $parts += ('{0} {1}%' -f $label, [int]$tier.utilization)
}

# Output stays ASCII-only: PowerShell encodes redirected stdout per the legacy
# console codepage while Claude Code decodes it as UTF-8 - non-ASCII mojibakes.
$line = $parts -join ' | '
if ($resetSuffix -and $line) { $line = $line + ' | ' + $resetSuffix }

if ([string]$snap.status -eq 'error') {
  # Fetch is failing: stale becomes actionable, with last-good numbers when we have them.
  $kind = switch ([string]$snap.error_kind) {
    'session_expired' { 'reauth needed' }
    'cloudflare'      { 'blocked - reauth' }
    default           { 'offline' }
  }
  if ($line) { Write-Output ('sanduhr: {0} | last {1}' -f $kind, $line) } else { Write-Output ('sanduhr: {0}' -f $kind) }
  exit 0
}

if (-not $line) { exit 0 }
if ($age -ge 450) { $line = $line + (' ({0}m ago)' -f [int][Math]::Floor($age / 60)) }
Write-Output $line
""";
}
