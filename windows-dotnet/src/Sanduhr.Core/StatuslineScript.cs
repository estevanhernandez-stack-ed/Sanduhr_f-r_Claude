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
///
/// Stage 4 (4.4, 4.6): the script also picks the snapshot file by the session's
/// <c>CLAUDE_CONFIG_DIR</c> (the same rule as <see cref="StatuslineSeatLookup"/>),
/// prints only the segments <c>-Mine</c> names, and with <c>-Chain</c> runs the
/// user's own statusline command first and prints both (<c>-Join line|same</c>).
/// </summary>
public static class StatuslineScript
{
    /// <summary>Bumped when the script body changes; the installer re-writes the
    /// script whenever the installed copy differs from this source.</summary>
    public const int Version = 6;

    /// <summary>PowerShell 5.1-compatible script body. Reads the snapshot per the
    /// reader contract (one-shot read, any parse failure = missing), renders per
    /// the staleness table: fresh = numbers; stale = numbers + age suffix; dead =
    /// an explicit "start widget" line (never blank — blank is the UNINSTALLED
    /// look); missing/malformed = empty output. Output is written as UTF-8 bytes
    /// without a BOM so the user's own non-ASCII text survives a combine; Sanduhr's
    /// own text stays ASCII.</summary>
    public const string Content = """
# Sanduhr statusline for Claude Code (snapshot schema v1).
# Installed and kept up to date by the Sanduhr widget. Do not edit in place.
# Reads %USERPROFILE%\.sanduhr\snapshot.json (raw facts only), or the per-seat
# snapshot.<ref>.json that seats.json maps this session's CLAUDE_CONFIG_DIR to.
# Age and reset times are derived here at render time so they can never go
# stale on disk.
#   -Chain <base64>   the user's own statusline command (UTF-8): run it first, print both
#   -Join line|same   Sanduhr's line on its own row, or appended to their last row
#   -Mine a,b,c       Sanduhr's segments: account, session, weekly, resets (default: all four)
# The account segment leads the line with the Claude Code folder's label. It is worked out
# here, when the line renders, and written nowhere: SANDUHR_LABEL from the session's
# environment (the folder's own settings.json env block), else the CLAUDE_CONFIG_DIR folder's
# name (.claude-personal reads personal; plain .claude has none), else no label.
# Streamer mode (Settings, Privacy) puts the folder's codename in its place, from
# %USERPROFILE%\.sanduhr\streamer.json (folder ref -> codename, never a real name). It never adds
# a label where none would print, and a file it can't trust prints no label at all.
param(
  [string]$Chain = '',
  [string]$Join = 'line',
  [string]$Mine = ''
)
$ErrorActionPreference = 'SilentlyContinue'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Emit([string]$text) {
  $bytes = $utf8.GetBytes($text)
  $out = [Console]::OpenStandardOutput()
  $out.Write($bytes, 0, $bytes.Length)
  $out.Flush()
}

# ---- seat lookup (the same rule as StatuslineSeatLookup in Core) ---------------
function Get-FolderKey([string]$path, [string]$profileDir) {
  if ([string]::IsNullOrEmpty($path)) { return $null }
  if ($path -eq '~' -or $path.StartsWith('~\') -or $path.StartsWith('~/')) { $path = $profileDir + $path.Substring(1) }
  if ($path -notmatch '^([A-Za-z]:[\\/]|\\\\)') { return $null }   # relative reads as unlinked
  try { $full = [System.IO.Path]::GetFullPath($path) } catch { return $null }
  $root = [System.IO.Path]::GetPathRoot($full)
  while ($full.Length -gt $root.Length -and ($full.EndsWith('\') -or $full.EndsWith('/'))) { $full = $full.Substring(0, $full.Length - 1) }
  return $full
}

function Get-SnapshotName {
  $fallback = 'snapshot.json'
  try {
    $seatsPath = Join-Path $env:USERPROFILE '.sanduhr\seats.json'
    $info = New-Object System.IO.FileInfo($seatsPath)
    if (-not $info.Exists -or $info.Length -gt 65536) { return $fallback }
    $profileDir = $env:USERPROFILE
    $config = $env:CLAUDE_CONFIG_DIR
    if ([string]::IsNullOrEmpty($config)) { $config = Join-Path $profileDir '.claude' }
    $folder = Get-FolderKey $config $profileDir
    if ($null -eq $folder) { return $fallback }
    $seats = [System.IO.File]::ReadAllText($seatsPath) | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $seats -or $null -eq $seats.schema_version) { return $fallback }
    if (-not ($seats.schema_version -is [int] -or $seats.schema_version -is [long] -or $seats.schema_version -is [double])) { return $fallback }
    if ([double]$seats.schema_version -ne 1) { return $fallback }
    foreach ($row in @($seats.seats)) {
      if ($null -eq $row) { continue }
      if (-not ($row.folder -is [string]) -or -not ($row.account_ref -is [string])) { continue }
      if ($row.account_ref -cnotmatch '^[0-9a-f]{8}$') { continue }
      $key = Get-FolderKey $row.folder $profileDir
      if ($null -ne $key -and [string]::Equals($key, $folder, [System.StringComparison]::OrdinalIgnoreCase)) {
        return ('snapshot.{0}.json' -f $row.account_ref)
      }
    }
  } catch { }
  return $fallback
}

# ---- the folder's label (computed here, never stored anywhere) ------------------
# Control characters out, trimmed, at most 24 characters; $null when nothing is left.
function Clean-Label([string]$text) {
  if ([string]::IsNullOrEmpty($text)) { return $null }
  $text = ([regex]::Replace($text, '[\x00-\x1F\x7F-\x9F]', '')).Trim()
  if ($text.Length -gt 24) {
    $cut = 24
    if ([char]::IsHighSurrogate($text[23])) { $cut = 23 }
    $text = $text.Substring(0, $cut).TrimEnd()
  }
  if ($text) { return $text }
  return $null
}

function Get-FolderLabel {
  $own = Clean-Label $env:SANDUHR_LABEL
  if ($own) { return $own }
  $config = $env:CLAUDE_CONFIG_DIR
  if ([string]::IsNullOrWhiteSpace($config)) { return $null }
  $leaf = @($config.Trim().TrimEnd('\', '/').Split(@('\', '/')))[-1]
  if ([string]::IsNullOrEmpty($leaf)) { return $null }
  if ($leaf.StartsWith('.claude-', [System.StringComparison]::OrdinalIgnoreCase)) { return (Clean-Label $leaf.Substring(8)) }
  if ($leaf -ieq '.claude') { return $null }
  return (Clean-Label $leaf.TrimStart('.'))
}

# ---- streamer mode: the folder's codename in place of its label -----------------
# streamer.json is written by the widget once streamer mode has been on. Missing: as before.
# On: the codename for this folder's ref, the label itself for a shown ref, else nothing.
# The ref is the first 4 bytes of SHA-256 over the folder's full path, lower-cased (StreamerFile.FolderRef).
function Get-StreamerLabel([string]$label) {
  $path = Join-Path $env:USERPROFILE '.sanduhr\streamer.json'
  $info = New-Object System.IO.FileInfo($path)
  if (-not $info.Exists) { return $label }
  if ($info.Length -gt 65536) { return $null }
  try { $s = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
  if ($null -eq $s -or $null -eq $s.schema_version) { return $null }
  if (-not ($s.schema_version -is [int] -or $s.schema_version -is [long] -or $s.schema_version -is [double])) { return $null }
  if ([double]$s.schema_version -ne 1) { return $null }
  if (-not ($s.on -is [bool])) { return $null }
  if (-not $s.on) { return $label }
  $profileDir = $env:USERPROFILE
  $config = $env:CLAUDE_CONFIG_DIR
  if ([string]::IsNullOrEmpty($config)) { $config = Join-Path $profileDir '.claude' }
  $folder = Get-FolderKey $config $profileDir
  if ($null -eq $folder) { return $null }
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try { $hash = $sha.ComputeHash($utf8.GetBytes($folder.ToLowerInvariant())) } finally { $sha.Dispose() }
  $ref = (($hash[0..3] | ForEach-Object { $_.ToString('x2') }) -join '')
  if (@($s.shown) -contains $ref) { return $label }
  if ($null -eq $s.folders) { return $null }
  $code = $s.folders.$ref
  if ($code -is [string]) { return (Clean-Label $code) }
  return $null
}

# ---- Sanduhr's own line (null = nothing of Sanduhr's to print) -----------------
function Get-OurLine {
  $segments = @('account', 'session', 'weekly', 'resets')
  if ($Mine) {
    $picked = @($Mine.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { @('account', 'session', 'weekly', 'resets') -contains $_ })
    if ($picked.Count -gt 0) { $segments = $picked }
  }
  $showAccount = $segments -contains 'account'
  $showSession = $segments -contains 'session'
  $showWeekly = $segments -contains 'weekly'
  $showResets = $segments -contains 'resets'

  $snapPath = Join-Path (Join-Path $env:USERPROFILE '.sanduhr') (Get-SnapshotName)
  if (-not (Test-Path -LiteralPath $snapPath)) { return $null }  # missing = invisible (the uninstalled look)
  try { $snap = Get-Content -LiteralPath $snapPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
  if ($null -eq $snap) { return $null }
  if ([int]$snap.schema_version -gt 1) { return 'sanduhr: update statusline' }

  $now = [DateTimeOffset]::UtcNow
  try { $captured = ([DateTimeOffset]::Parse($snap.captured_at, [System.Globalization.CultureInfo]::InvariantCulture)).ToUniversalTime() } catch { return $null }
  $age = [Math]::Max(0, [int]($now - $captured).TotalSeconds)

  # Dead band (two missed polls): the widget is not polling. Say so - numbers
  # this old rendered plainly would be a silent lie.
  if ($age -gt 900) {
    return ('sanduhr: stale {0}m - start widget' -f [int][Math]::Floor($age / 60))
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
      if (-not $showWeekly) { continue }
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
    if ($key -eq 'five_hour' -and -not $showSession) { continue }
    if ($key -eq 'seven_day' -and -not $showWeekly) { continue }
    $parts += ('{0} {1}%' -f $label, [int]$tier.utilization)
  }

  # Sanduhr's own text stays ASCII.
  $line = $parts -join ' | '
  if ($showResets -and $resetSuffix) {
    if ($line) { $line = $line + ' | ' + $resetSuffix } else { $line = $resetSuffix }
  }

  if ([string]$snap.status -eq 'error') {
    # Fetch is failing: stale becomes actionable, with last-good numbers when we have them.
    $kind = switch ([string]$snap.error_kind) {
      'session_expired' { 'reauth needed' }
      'cloudflare'      { 'blocked - reauth' }
      default           { 'offline' }
    }
    if ($line) { return ('sanduhr: {0} | last {1}' -f $kind, $line) }
    return ('sanduhr: {0}' -f $kind)
  }

  if (-not $line) { return $null }   # no numbers: the label never prints on its own
  if ($showAccount) {
    $folderLabel = Get-FolderLabel
    if ($folderLabel) { $folderLabel = Get-StreamerLabel $folderLabel }   # streamer mode: the codename, or nothing
    if ($folderLabel) { $line = $folderLabel + ' | ' + $line }   # the label may be non-ASCII; Emit writes UTF-8
  }
  if ($age -ge 450) { $line = $line + (' ({0}m ago)' -f [int][Math]::Floor($age / 60)) }
  return $line
}

# ---- the user's own command (combine) ------------------------------------------
function Quote-Arg([string]$s) {
  # Windows command-line quoting: backslashes before a quote double, quotes escape.
  $q = $s -replace '(\\*)"', '$1$1\"'
  $q = $q -replace '(\\+)$', '$1$1'
  return '"' + $q + '"'
}

# Claude Code runs a statusline command through Git Bash when it has one, so the
# user's command is written for it. CLAUDE_CODE_GIT_BASH_PATH, when set, is
# authoritative (a path that does not exist means no Git Bash: fall back to
# PowerShell). Else bin\bash.exe in the git install root found from git.exe.
# Never C:\Windows\System32\bash.exe (that is WSL).
function Find-GitBash {
  $set = $env:CLAUDE_CODE_GIT_BASH_PATH
  if (-not [string]::IsNullOrEmpty($set)) {
    if (Test-Path -LiteralPath $set -PathType Leaf) { return $set }
    return $null
  }
  try {
    $git = Get-Command git -ErrorAction Stop | Select-Object -First 1
    $dir = Split-Path -Parent $git.Source
    for ($i = 0; $i -lt 3 -and $dir; $i++) {
      $candidate = Join-Path $dir 'bin\bash.exe'
      if ((Test-Path -LiteralPath $candidate -PathType Leaf) -and ($candidate -notlike '*\System32\*')) { return $candidate }
      $dir = Split-Path -Parent $dir
    }
  } catch { }
  return $null
}

# Their stdout as text, or $null when it failed (could not start, 2 s timeout,
# non-zero exit): a failed command prints nothing of theirs.
function Invoke-Theirs([string]$command, [byte[]]$stdinBytes) {
  # Their command gets 2 s (SANDUHR_CHAIN_MS overrides it, for the test suite).
  $budgetMs = 2000
  if ($env:SANDUHR_CHAIN_MS -match '^[0-9]{3,5}$') { $budgetMs = [int]$env:SANDUHR_CHAIN_MS }
  $bash = Find-GitBash
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  if ($bash) {
    $psi.FileName = $bash
    $psi.Arguments = '-c ' + (Quote-Arg $command)
  } else {
    $psi.FileName = 'powershell.exe'
    $psi.Arguments = '-NoProfile -Command ' + (Quote-Arg $command)
  }
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardInput = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.EnvironmentVariables['SANDUHR_CHAIN_DEPTH'] = '1'
  $proc = $null
  try {
    $proc = [System.Diagnostics.Process]::Start($psi)
    $buffer = New-Object System.IO.MemoryStream
    $outTask = $proc.StandardOutput.BaseStream.CopyToAsync($buffer)
    $null = $proc.StandardError.BaseStream.CopyToAsync([System.IO.Stream]::Null)
    try {
      $proc.StandardInput.BaseStream.Write($stdinBytes, 0, $stdinBytes.Length)
      $proc.StandardInput.BaseStream.Flush()
    } catch { }
    try { $proc.StandardInput.Close() } catch { }
    if (-not $proc.WaitForExit($budgetMs)) {
      # Kill the whole tree: a grandchild still holding a pipe would keep our caller waiting.
      try {
        $kill = New-Object System.Diagnostics.ProcessStartInfo('taskkill.exe', ('/T /F /PID {0}' -f $proc.Id))
        $kill.UseShellExecute = $false
        $kill.CreateNoWindow = $true
        $kill.RedirectStandardOutput = $true
        $kill.RedirectStandardError = $true
        $k = [System.Diagnostics.Process]::Start($kill)
        $null = $k.WaitForExit(1500)
      } catch { }
      try { $proc.Kill() } catch { }
      return $null
    }
    if (-not $outTask.Wait(1000)) { return $null }
    if ($proc.ExitCode -ne 0) { return $null }
    $bytes = $buffer.ToArray()
    $start = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $start = 3 }
    return $utf8.GetString($bytes, $start, $bytes.Length - $start)
  } catch {
    return $null
  } finally {
    if ($proc) { try { $proc.Dispose() } catch { } }
  }
}

function Read-Stdin {
  try {
    $in = [Console]::OpenStandardInput()
    $ms = New-Object System.IO.MemoryStream
    $in.CopyTo($ms)
    return $ms.ToArray()
  } catch {
    return (New-Object byte[] 0)
  }
}

# ---- main ----------------------------------------------------------------------
$ours = Get-OurLine
$theirs = $null
if ($Chain -and -not $env:SANDUHR_CHAIN_DEPTH) {
  try { $command = $utf8.GetString([Convert]::FromBase64String($Chain)) } catch { $command = $null }
  if ($command -and $command.Trim()) { $theirs = Invoke-Theirs $command (Read-Stdin) }
}

if ($null -eq $theirs) {
  if ($ours) { Emit ($ours + "`n") }
  exit 0
}

$rows = $theirs.TrimEnd("`r", "`n")
if (-not $ours) {
  if ($rows.Trim()) { Emit ($rows + "`n") }
  exit 0
}
if (-not $rows.Trim()) { Emit ($ours + "`n"); exit 0 }
if ($Join -eq 'same') {
  $lines = $rows -split "`n"
  $lines[$lines.Count - 1] = $lines[$lines.Count - 1].TrimEnd("`r") + ([string][char]27) + "[0m" + ' | ' + $ours
  Emit (($lines -join "`n") + "`n")
} else {
  Emit ($rows + "`n" + $ours + "`n")
}
""";
}
