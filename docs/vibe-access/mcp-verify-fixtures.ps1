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

Write-Output "fixtures written under $Root at $(Iso $now)"
