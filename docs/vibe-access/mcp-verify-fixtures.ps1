# Fixture generator for the sanduhr-mcp cold verify. Re-run before each pass so
# age-dependent states are stamped relative to NOW. Creates, under the kit root:
#   state-fresh/    snapshot.json captured 2 minutes ago  (expect status ok)
#   state-stale/    captured 10 minutes ago               (expect status stale)
#   state-dead/     captured 43 minutes ago               (expect stale + widget_not_polling)
#   state-error/    captured 2m ago, status=error/session_expired (expect stale + fetch_error)
#   state-missing/  no snapshot file                      (expect no_data/missing)
#   state-malformed/ snapshot is not JSON                 (expect no_data/malformed)
#   state-schema2/  schema_version 2                      (expect no_data/schema_unsupported)
#   roots/personal-fixture/  CC-home-shaped tree, 2 projects (one outside the 1-day window)
param([string]$Root = $PSScriptRoot)

$ErrorActionPreference = 'Stop'

function Iso([DateTimeOffset]$t) { $t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz") }

function Snapshot([DateTimeOffset]$captured, [string]$status, [string]$errorKind) {
  $ek = if ($errorKind) { '"' + $errorKind + '"' } else { 'null' }
  @"
{"schema_version":1,"writer_version":"3.4.0","captured_at":"$(Iso $captured)","account_ref":"c0ffee42","plan":"Max 20x","status":"$status","error_kind":$ek,"tiers":[
{"key":"five_hour","utilization":42,"resets_at":"$(Iso $captured.AddHours(3))","used":null,"limit":null},
{"key":"seven_day","utilization":62,"resets_at":"$(Iso $captured.AddDays(5))","used":null,"limit":null},
{"key":"routines","utilization":20,"resets_at":null,"used":3,"limit":15}]}
"@
}

$now = [DateTimeOffset]::UtcNow
foreach ($s in @('state-fresh','state-stale','state-dead','state-error','state-missing','state-malformed','state-schema2')) {
  New-Item -ItemType Directory -Force (Join-Path $Root $s) | Out-Null
}

Set-Content (Join-Path $Root 'state-fresh\snapshot.json')  (Snapshot $now.AddMinutes(-2)  'ok'    $null) -Encoding utf8
Set-Content (Join-Path $Root 'state-stale\snapshot.json')  (Snapshot $now.AddMinutes(-10) 'ok'    $null) -Encoding utf8
Set-Content (Join-Path $Root 'state-dead\snapshot.json')   (Snapshot $now.AddMinutes(-43) 'ok'    $null) -Encoding utf8
Set-Content (Join-Path $Root 'state-error\snapshot.json')  (Snapshot $now.AddMinutes(-2)  'error' 'session_expired') -Encoding utf8
Remove-Item (Join-Path $Root 'state-missing\snapshot.json') -ErrorAction SilentlyContinue
Set-Content (Join-Path $Root 'state-malformed\snapshot.json') '{ this is not json' -Encoding utf8
Set-Content (Join-Path $Root 'state-schema2\snapshot.json') ('{"schema_version":2,"captured_at":"' + (Iso $now) + '","status":"ok","tiers":[]}') -Encoding utf8

# CC-home fixture: personal-fixture/projects/{c--proj-alpha, c--proj-beta}
$rootA = Join-Path $Root 'roots\personal-fixture'
foreach ($p in @('c--proj-alpha','c--proj-beta')) {
  New-Item -ItemType Directory -Force (Join-Path $rootA "projects\$p") | Out-Null
}
function EventLine([DateTimeOffset]$ts, [string]$model, [long]$in_, [long]$out_, [string]$cwd) {
  '{"type":"assistant","timestamp":"' + $ts.ToString('o') + '","cwd":"' + $cwd + '","message":{"model":"' + $model + '","usage":{"input_tokens":' + $in_ + ',"output_tokens":' + $out_ + '}}}'
}
# alpha: 3000 tokens fable + 100 unmapped, stamped AFTER state-fresh's
# captured_at (-2m) so get_usage's "burn since snapshot" sees them. The first
# cold verify caught the original -30m/-20m stamps predating the anchor —
# "since X" fixtures need events that deliberately straddle X.
Set-Content (Join-Path $rootA 'projects\c--proj-alpha\s1.jsonl') @(
  (EventLine $now.AddMinutes(-1) 'claude-fable-5' 1000 2000 'C:/fixture/proj-alpha'),
  (EventLine $now.AddSeconds(-30) 'mystery-model-9' 60 40 'C:/fixture/proj-alpha')
) -Encoding utf8
# beta: 500 tokens sonnet, 3 days old (outside a 1-day window, inside 7)
Set-Content (Join-Path $rootA 'projects\c--proj-beta\s1.jsonl') @(
  (EventLine $now.AddDays(-3) 'claude-sonnet-5' 300 200 'C:/fixture/proj-beta')
) -Encoding utf8

# Pacing-band snapshots (abilities wave): ahead (80% with 4h of 5h left) and
# under (10% with 2.5h left) - drive cooldown_seconds / surplus_pct /
# projected_final_pct assertions.
foreach ($s in @('state-ahead','state-under')) {
  New-Item -ItemType Directory -Force (Join-Path $Root $s) | Out-Null
}
function PaceSnapshot([DateTimeOffset]$captured, [int]$util, [double]$hoursLeft) {
  @"
{"schema_version":1,"writer_version":"3.4.0","captured_at":"$(Iso $captured)","account_ref":"c0ffee42","plan":"Max 20x","status":"ok","error_kind":null,"tiers":[
{"key":"five_hour","utilization":$util,"resets_at":"$(Iso $captured.AddHours($hoursLeft))","used":null,"limit":null}]}
"@
}
Set-Content (Join-Path $Root 'state-ahead\snapshot.json') (PaceSnapshot $now.AddMinutes(-1) 80 4.0) -Encoding utf8
Set-Content (Join-Path $Root 'state-under\snapshot.json') (PaceSnapshot $now.AddMinutes(-1) 10 2.5) -Encoding utf8
# Note (run-2 finding): pace derivations anchor at CALL time - expected
# cooldown/surplus values drift by age_seconds (cooldown = ideal - age).

# Fable-meter state (run-2 kit gap): get_model_usage's meter join needs a
# snapshot carrying the seven_day_fable tier.
New-Item -ItemType Directory -Force (Join-Path $Root 'state-fresh-fable') | Out-Null
Set-Content (Join-Path $Root 'state-fresh-fable\snapshot.json') @"
{"schema_version":1,"writer_version":"3.4.0","captured_at":"$(Iso $now.AddMinutes(-2))","account_ref":"c0ffee42","plan":"Max 20x","status":"ok","error_kind":null,"tiers":[
{"key":"five_hour","utilization":42,"resets_at":"$(Iso $now.AddHours(3))","used":null,"limit":null},
{"key":"seven_day","utilization":62,"resets_at":"$(Iso $now.AddDays(5))","used":null,"limit":null},
{"key":"seven_day_fable","utilization":57,"resets_at":"$(Iso $now.AddDays(5))","used":null,"limit":null}]}
"@ -Encoding utf8

# Vault fixture (abilities wave, get_usage_history): rollups-YYYY-MM.json under
# vault\<rootName>\ - two recorded days (one with sent/received split + projects,
# keyed name~hash), one deliberate gap day. Env: SANDUHR_VAULT_DIR=<Root>\vault,
# consented root name must be 'personal-fixture' (matches SANDUHR_CC_ROOTS).
$vaultRoot = Join-Path $Root 'vault\personal-fixture'
New-Item -ItemType Directory -Force $vaultRoot | Out-Null
$d1 = $now.AddDays(-2).ToString('yyyy-MM-dd')
$d2 = $now.AddDays(-1).ToString('yyyy-MM-dd')
$months = @($now.AddDays(-2).ToString('yyyy-MM'), $now.AddDays(-1).ToString('yyyy-MM')) | Select-Object -Unique
foreach ($m in $months) {
  $days = @{}
  if ($d1.StartsWith($m)) { $days[$d1] = @{ total = 500000; input = 100000; output = 400000; by_model = @{}; by_project = @{ 'Sanduhr~abc123' = 300000; 'vibe-plugins~def456' = 200000 }; by_skill = @{}; sessions = 3 } }
  if ($d2.StartsWith($m)) { $days[$d2] = @{ total = 250000; input = 50000; output = 200000; by_model = @{}; by_project = @{}; by_skill = @{}; sessions = 1 } }
  @{ schema_version = 1; days = $days } | ConvertTo-Json -Depth 6 -Compress |
    Set-Content (Join-Path $vaultRoot "rollups-$m.json") -Encoding utf8
}

Write-Output "fixtures written under $Root at $(Iso $now)"
