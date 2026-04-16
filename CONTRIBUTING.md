# Contributing to Sanduhr für Claude

## Getting Started

```bash
git clone https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude.git
cd Sanduhr_f-r_Claude
python sanduhr.py
```

Python 3.8+. No build step, no virtual environment required. `cloudscraper` auto-installs on first run.

## Architecture

The entire app is one file: `sanduhr.py` (~615 lines). This is intentional — see [ADR-001](docs/adr.md#adr-001-single-file-python-architecture). Don't split it into modules unless the codebase exceeds ~1,500 lines.

Key sections in `sanduhr.py`:
- **Config & constants** (lines 1–90) — themes, tier labels, dimensions
- **History** (lines 112–130) — sparkline data persistence
- **ClaudeAPI** (lines 135–152) — HTTP client for claude.ai
- **Helpers** (lines 156–228) — time formatting, pacing, burn rate
- **Sparkline** (lines 232–255) — canvas rendering
- **UsageWidget** (lines 259–615) — UI, controls, refresh loop

## Adding a Theme

Easiest contribution. Add a dict to `THEMES`:

```python
"mytheme": {
    "name": "My Theme", "bg": "#...", "glass": "#...",
    "title_bg": "#...", "border": "#...", "text": "#...",
    "text_secondary": "#...", "text_dim": "#...", "text_muted": "#...",
    "accent": "#...", "bar_bg": "#...", "footer_bg": "#...",
    "pace_marker": "#...", "sparkline": "#...",
},
```

All 14 color keys are required. Test it by running the widget and cycling to your theme.

## Adding a Tier

If Claude adds new usage tiers, add the API key to `TIER_LABELS`:

```python
TIER_LABELS = {
    "new_tier_key": "Display Name",
    ...
}
```

The widget auto-discovers tiers from the API response — if the key exists in `TIER_LABELS` and the API returns data for it, it renders automatically.

## Pull Request Guidelines

- Keep changes in `sanduhr.py` unless you're adding docs
- Test with a real session key before submitting (UI correctness > unit tests for a widget)
- One feature per PR
- Update `CHANGELOG.md` with your change under an `## Unreleased` section

## What We're Looking For

Check the [roadmap in README.md](README.md#roadmap):
- System tray mode
- Auto-start on boot
- Historical usage dashboard
- New themes

## Code Style

- No type hints (keeping it lightweight for a single-file tool)
- Minimal comments — code should be self-explanatory
- Follow existing naming: `snake_case` for functions, `UPPER_CASE` for constants
- tkinter patterns: create widgets once, update in-place via `configure()` — no destroy/recreate on refresh

## License

By contributing, you agree your code is released under the [MIT License](README.md#license).
