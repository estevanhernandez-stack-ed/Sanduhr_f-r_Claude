# Sanduhr fur Claude - Technical Specification

## Architecture
Single-file Python application using tkinter (stdlib) for GUI. No framework, no build system, no server. Runs as a desktop process with threading for async API calls.

## Stack
- **Language**: Python 3.8+
- **GUI**: tkinter (stdlib)
- **HTTP**: cloudscraper (handles Cloudflare JS challenges)
- **Data**: JSON files in ~/.claude-usage-widget/

## File Structure
```
sanduhr.py          # The entire application (~575 lines)
README.md           # Distribution docs
.gitignore          # Excludes config, docs, Python cache
docs/               # VC planning artifacts (not distributed)
```

## Data Flow
```
[Claude.ai API] --cloudscraper--> [ClaudeAPI class] --thread--> [UI update on main thread]
                                                         |
                                                    [history.json] --> [sparkline rendering]
```

## API Integration

### Endpoints (reverse-engineered from claude.ai settings page)
1. `GET https://claude.ai/api/organizations` - Returns org list with UUIDs
2. `GET https://claude.ai/api/organizations/{orgId}/usage` - Returns usage data

### Response Shape (usage endpoint)
```json
{
  "five_hour": {"utilization": 27, "resets_at": "2026-04-16T03:00:00+00:00"},
  "seven_day": {"utilization": 56, "resets_at": "2026-04-19T06:00:00+00:00"},
  "seven_day_sonnet": {"utilization": 2, "resets_at": "2026-04-21T20:00:00+00:00"},
  "seven_day_omelette": {"utilization": 0, "resets_at": null},
  "extra_usage": {"is_enabled": false, "monthly_limit": null, "used_credits": null}
}
```
Null tiers are hidden. Utilization is an integer percentage.

### Auth
- Session cookie (`sessionKey`) extracted from browser
- Sent via `Cookie` header on every request
- cloudscraper handles Cloudflare challenge transparently

## Key Algorithms

### Pacing
Linear pace: `expected_usage = (elapsed_time / total_period) * 100`
- On pace: |actual - expected| < 5%
- Ahead: actual > expected + 5%
- Under: actual < expected - 5%

### Burn Rate Projection
```
rate_per_period = utilization / elapsed_fraction
if rate_per_period > 100:
    time_to_100 = (100 / rate_per_period - elapsed_fraction) * total_period
    if time_to_100 < time_to_reset:
        show warning: "At current pace, expires in {time_to_100}"
```
Only warns if projected exhaustion occurs BEFORE the reset — no false alarms.

### Sparklines
- 24-point rolling window stored in ~/.claude-usage-widget/history.json
- Rendered on tk.Canvas with line segments
- Min/max normalization for vertical scaling
- Updated on each API refresh

## UI Architecture

### Graceful Updates
Tier widgets are created once and updated in-place via `configure()` and `place()`. No `destroy()`/recreate cycle on refresh. This eliminates flicker.

### Theme System
5 themes stored as dicts with 14 color keys each. Theme selection persisted in config.json. Theme strip renders all options as clickable buttons with active state highlighting.

### Compact Mode
Double-click title bar toggles. In compact mode, `active` list is filtered to `max(active, key=utilization)` before rendering. Stale widgets are destroyed.

### Window Management
- `overrideredirect(True)` removes native chrome
- Custom title bar with drag bindings
- Always-on-top via `attributes("-topmost")`
- Positioned bottom-right of primary monitor on launch

## Security
- Session key stored in ~/.claude-usage-widget/config.json (user home, outside repo)
- .gitignore excludes all config and secret files
- No key ever transmitted except to claude.ai API via HTTPS
- cloudscraper handles TLS and Cloudflare — no custom cert handling

## Limitations
- Session key expires on logout/inactivity (user must re-paste)
- Cloudflare may change challenge method (cloudscraper updates independently)
- No support for Antigravity/Gemini quotas yet (gRPC-Web + protobuf auth wall)
- tkinter rendering is basic — no true glassmorphism blur, simulated via color
