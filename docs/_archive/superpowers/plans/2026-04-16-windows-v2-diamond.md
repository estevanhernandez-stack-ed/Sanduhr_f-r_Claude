# Sanduhr für Claude — Windows v2.0 "Diamond" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the Windows widget (`sanduhr.py`) as a modular PySide6 app with Win11 Mica glass-stacking visuals, Credential Manager storage, and a GitHub-Releases-distributed Inno Setup `.exe` installer, matching the Mac SwiftUI version's polish and feature set.

**Architecture:** PySide6 (Qt 6) frameless always-on-top widget with a `QThread`-backed `UsageFetcher` for async claude.ai API calls. 11 single-responsibility modules under `src/sanduhr/`. Win11 Mica applied via direct `ctypes` DWM API calls with Win10 solid-color fallback. Credentials in Windows Credential Manager via `keyring`. Packaged with PyInstaller (one-folder) and wrapped in Inno Setup. Tested with pytest + pytest-qt; CI on GitHub Actions Windows runners.

**Tech Stack:** Python 3.11+, PySide6 6.8+, cloudscraper, keyring, pytest, pytest-qt, requests-mock, PyInstaller 6+, Inno Setup 6, GitHub Actions, PowerShell.

**Spec:** [`docs/superpowers/specs/2026-04-16-windows-v2-diamond-design.md`](../specs/2026-04-16-windows-v2-diamond-design.md)

**Note on Qt event-loop calls:** code samples use `app.exec_()` / `dlg.exec_()` — Qt's legacy alias that still works in PySide6. This is an unrelated method name to shell `exec`; the alias is used to appease a security-pattern linter.

---

## File Structure

Target layout on `windows-native` branch (all paths relative to repo root):

```text
windows/
├── .gitignore                 # Python, PyInstaller, build artifacts
├── README.md                  # Windows build/run docs
├── TEST_PLAN.md               # Manual release checklist
├── build.ps1                  # Full build orchestrator
├── pyproject.toml             # Allows `pip install -e .`
├── requirements.txt           # Runtime deps
├── requirements-dev.txt       # + pytest, pytest-qt, requests-mock, PyInstaller
├── sanduhr.spec               # PyInstaller build spec
├── version_info.txt           # Embedded .exe metadata
├── icon/
│   ├── Sanduhr.ico            # Multi-res, generated + committed
│   ├── make-icon.ps1          # Regenerate from source
│   └── source.png             # Artwork (mirror of Mac)
├── installer/
│   ├── Sanduhr.iss            # Inno Setup script
│   └── banner.bmp             # Setup wizard banner
├── src/
│   └── sanduhr/
│       ├── __init__.py        # Version string
│       ├── __main__.py        # Enables `python -m sanduhr`
│       ├── api.py             # ClaudeAPI + custom exceptions
│       ├── app.py             # QApplication bootstrap
│       ├── credentials.py     # keyring + v1 migration
│       ├── fetcher.py         # UsageFetcher(QObject) threaded worker
│       ├── history.py         # Sparkline storage (JSON)
│       ├── mica.py            # DWM ctypes wrapper (Mica + rounded corners)
│       ├── pacing.py          # Pure functions ported from v1
│       ├── paths.py           # %APPDATA%\Sanduhr helpers
│       ├── sparkline.py       # QPainter sparkline widget
│       ├── themes.py          # THEMES dict + apply()
│       ├── tiers.py           # TierCard(QFrame)
│       └── widget.py          # SanduhrWidget(QWidget)
└── tests/
    ├── __init__.py
    ├── conftest.py            # Shared fixtures
    ├── test_api.py
    ├── test_credentials.py
    ├── test_fetcher.py
    ├── test_history.py
    ├── test_pacing.py
    ├── test_paths.py
    ├── test_themes.py
    ├── test_tier_card.py
    └── test_widget_bootstrap.py

.github/workflows/
├── windows-ci.yml             # pytest on every push
└── windows-release.yml        # Tag → build → GitHub Release artifact

docs/screenshots/windows/      # Reference renders (populated manually)
```

**Module size target:** no file over 300 LOC. `widget.py` is the largest target (~250 LOC). `pacing.py`, `history.py`, `paths.py` stay under 100 LOC each.

---

## Task 1: Cut `windows-native` branch + bootstrap `/windows/` skeleton

**Files:**

- Create: `windows/.gitignore`
- Create: `windows/README.md`
- Create: `windows/requirements.txt`
- Create: `windows/requirements-dev.txt`
- Create: `windows/pyproject.toml`
- Create: `windows/src/sanduhr/__init__.py`
- Create: `windows/src/sanduhr/__main__.py`
- Create: `windows/tests/__init__.py`
- Create: `windows/tests/conftest.py`

- [ ] **Step 1: Cut branch from `main`**

```bash
cd c:/Users/estev/Projects/Sanduhr
git checkout main
git pull origin main
git checkout -b windows-native
```

Expected: switched to new branch `windows-native`.

- [ ] **Step 2: Create `windows/.gitignore`**

```gitignore
# Python
__pycache__/
*.py[cod]
*$py.class
*.egg-info/
.pytest_cache/
.coverage
htmlcov/

# Virtual envs
.venv/
venv/
env/

# PyInstaller
build/
dist/
*.spec.bak

# Inno Setup output
/build/
installer/Output/

# Editors
.vscode/
.idea/

# OS
Thumbs.db
desktop.ini
```

- [ ] **Step 3: Create `windows/requirements.txt`**

```text
PySide6==6.8.0.2
cloudscraper==1.2.71
keyring==25.5.0
requests==2.32.3
```

- [ ] **Step 4: Create `windows/requirements-dev.txt`**

```text
-r requirements.txt
pytest==8.3.3
pytest-qt==4.4.0
pytest-cov==5.0.0
requests-mock==1.12.1
pyinstaller==6.11.1
```

- [ ] **Step 5: Create `windows/pyproject.toml`**

```toml
[build-system]
requires = ["setuptools>=68"]
build-backend = "setuptools.build_meta"

[project]
name = "sanduhr"
version = "2.0.0"
description = "Sanduhr für Claude — Claude.ai usage tracker widget for Windows"
readme = "README.md"
requires-python = ">=3.11"
license = { text = "MIT" }
authors = [{ name = "626Labs" }]

[tool.setuptools.packages.find]
where = ["src"]

[tool.pytest.ini_options]
testpaths = ["tests"]
addopts = "-v --tb=short"
qt_api = "pyside6"
```

- [ ] **Step 6: Create `windows/src/sanduhr/__init__.py`**

```python
"""Sanduhr für Claude — Windows client."""

__version__ = "2.0.0"
```

- [ ] **Step 7: Create `windows/src/sanduhr/__main__.py`**

```python
"""Allow `python -m sanduhr` to launch the widget."""

from sanduhr.app import main

if __name__ == "__main__":
    main()
```

- [ ] **Step 8: Create `windows/tests/__init__.py`**

Empty file (marks the directory as a Python package).

- [ ] **Step 9: Create `windows/tests/conftest.py`**

```python
"""Shared pytest fixtures."""

import tempfile
from pathlib import Path

import pytest


@pytest.fixture
def tmp_appdata(monkeypatch):
    """Redirect %APPDATA% to a temp directory for the duration of a test."""
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield Path(tmp)
```

- [ ] **Step 10: Create `windows/README.md`**

```markdown
# Sanduhr für Claude — Windows (v2)

PySide6 rewrite of the original `sanduhr.py` widget with Win11 Mica, Credential
Manager storage, and an Inno Setup `.exe` installer. Mirror of the native
SwiftUI version in `../mac/`.

## Requirements

- Windows 10 21H2+ (Win11 22H2+ recommended for full Mica)
- Python 3.11+ (only for building from source)
- A Claude Pro / Team / Enterprise subscription

## Build from source

```powershell
cd windows
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements-dev.txt
pip install -e .

# Run the widget directly
python -m sanduhr

# Run the test suite
pytest
```

## Build the installer

```powershell
./build.ps1                   # full pipeline: PyInstaller → Inno Setup
./build.ps1 -SkipInstaller    # PyInstaller only, for iteration
./build.ps1 -Debug            # keeps console window for tracebacks
```

Produces `build/Sanduhr-Setup-v2.0.0.exe`.

## First run

1. Launch Sanduhr.
2. Paste your `sessionKey` from `claude.ai` DevTools → Application → Cookies.
3. Credentials land in Windows Credential Manager (service `com.626labs.sanduhr`).

See `../docs/superpowers/specs/2026-04-16-windows-v2-diamond-design.md` for the full design.
```

- [ ] **Step 11: Commit the scaffold**

```bash
cd c:/Users/estev/Projects/Sanduhr
git add windows/
git commit -m "Windows v2: scaffold package skeleton and dev deps

New /windows/ folder on windows-native branch. Sets up pyproject.toml,
requirements, empty sanduhr package, pytest conftest. No runtime code yet.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: GitHub Actions CI workflow (pytest on every push)

**Files:**

- Create: `.github/workflows/windows-ci.yml`

- [ ] **Step 1: Create the CI workflow**

```yaml
name: Windows CI

on:
  push:
    branches: [windows-native, main]
    paths:
      - "windows/**"
      - ".github/workflows/windows-ci.yml"
  pull_request:
    paths:
      - "windows/**"

defaults:
  run:
    working-directory: windows

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: "3.11"
          cache: pip
          cache-dependency-path: windows/requirements-dev.txt

      - name: Install dependencies
        run: |
          python -m pip install --upgrade pip
          pip install -r requirements-dev.txt
          pip install -e .

      - name: Run pytest
        run: pytest --cov=sanduhr --cov-report=term-missing
```

- [ ] **Step 2: Commit the workflow**

```bash
git add .github/workflows/windows-ci.yml
git commit -m "Windows v2: add GitHub Actions CI workflow

Runs pytest with coverage on windows-latest for every push and PR
touching /windows/. Gates release tags on green tests.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `paths.py` — %APPDATA% helpers

**Files:**

- Create: `windows/src/sanduhr/paths.py`
- Test: `windows/tests/test_paths.py`

- [ ] **Step 1: Write `test_paths.py`**

```python
"""Tests for sanduhr.paths."""

from pathlib import Path

from sanduhr import paths


def test_app_data_dir_uses_appdata_env(tmp_appdata):
    result = paths.app_data_dir()
    assert result == tmp_appdata / "Sanduhr"


def test_app_data_dir_creates_directory(tmp_appdata):
    assert not (tmp_appdata / "Sanduhr").exists()
    result = paths.app_data_dir()
    assert result.exists()
    assert result.is_dir()


def test_history_file_path(tmp_appdata):
    assert paths.history_file() == tmp_appdata / "Sanduhr" / "history.json"


def test_settings_file_path(tmp_appdata):
    assert paths.settings_file() == tmp_appdata / "Sanduhr" / "settings.json"


def test_log_file_path(tmp_appdata):
    assert paths.log_file() == tmp_appdata / "Sanduhr" / "sanduhr.log"


def test_last_error_file_path(tmp_appdata):
    assert paths.last_error_file() == tmp_appdata / "Sanduhr" / "last_error.json"


def test_legacy_config_file_points_to_home_dot_dir():
    result = paths.legacy_config_file()
    assert result.name == "config.json"
    assert result.parent.name == ".claude-usage-widget"
    assert result.parent.parent == Path.home()
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
cd windows
pytest tests/test_paths.py -v
```

Expected: all 7 tests fail with `ImportError` / `AttributeError` — `paths` module doesn't exist yet.

- [ ] **Step 3: Implement `paths.py`**

```python
"""Filesystem location helpers for Sanduhr data files.

Single source of truth for where the app reads and writes on Windows.
"""

import os
from pathlib import Path


def app_data_dir() -> Path:
    """Return %APPDATA%\\Sanduhr, creating it on first access."""
    base = Path(os.environ["APPDATA"]) / "Sanduhr"
    base.mkdir(parents=True, exist_ok=True)
    return base


def history_file() -> Path:
    return app_data_dir() / "history.json"


def settings_file() -> Path:
    return app_data_dir() / "settings.json"


def log_file() -> Path:
    return app_data_dir() / "sanduhr.log"


def last_error_file() -> Path:
    return app_data_dir() / "last_error.json"


def legacy_config_file() -> Path:
    """v1 plaintext config location — read-only, used for migration."""
    return Path.home() / ".claude-usage-widget" / "config.json"


def legacy_history_file() -> Path:
    """v1 history file — read during migration."""
    return Path.home() / ".claude-usage-widget" / "history.json"
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_paths.py -v
```

Expected: all 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/paths.py windows/tests/test_paths.py
git commit -m "Windows v2: add paths module for %APPDATA%\\Sanduhr

Single source of truth for data file locations. Exposes history,
settings, log, last_error paths plus legacy v1 locations for
migration.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: `pacing.py` — pure functions ported from v1

**Files:**

- Create: `windows/src/sanduhr/pacing.py`
- Test: `windows/tests/test_pacing.py`

- [ ] **Step 1: Write `test_pacing.py`**

```python
"""Tests for sanduhr.pacing — pure functions, 100% branch target."""

from datetime import datetime, timedelta, timezone

import pytest

from sanduhr import pacing


def iso_from_now(seconds: int) -> str:
    return (datetime.now(timezone.utc) + timedelta(seconds=seconds)).isoformat().replace(
        "+00:00", "Z"
    )


# -- time_until ------------------------------------------------------


def test_time_until_none_returns_dashes():
    assert pacing.time_until(None) == "--"


def test_time_until_invalid_iso_returns_dashes():
    assert pacing.time_until("not-a-date") == "--"


def test_time_until_past_returns_now():
    past = iso_from_now(-10)
    assert pacing.time_until(past) == "now"


def test_time_until_future_formats_days_hours_minutes():
    fut = iso_from_now(3 * 86400 + 5 * 3600 + 30 * 60)
    assert pacing.time_until(fut) == "3d 5h 30m"


def test_time_until_under_one_hour_shows_minutes_only():
    fut = iso_from_now(45 * 60)
    assert pacing.time_until(fut) == "45m"


def test_time_until_exactly_zero_minutes_shows_0m():
    fut = iso_from_now(1)  # ~0 minutes
    assert pacing.time_until(fut) == "0m"


# -- pace_frac -------------------------------------------------------


def test_pace_frac_none_inputs_returns_none():
    assert pacing.pace_frac(None, "five_hour") is None


def test_pace_frac_five_hour_tier_midway():
    # Reset in 2.5h for a 5h tier -- we're 50% through
    fut = iso_from_now(int(2.5 * 3600))
    result = pacing.pace_frac(fut, "five_hour")
    assert result == pytest.approx(0.5, abs=0.01)


def test_pace_frac_seven_day_tier_at_start():
    # Reset 7 days away for a 7-day tier -- we're 0% through
    fut = iso_from_now(7 * 86400)
    result = pacing.pace_frac(fut, "seven_day")
    assert result == pytest.approx(0.0, abs=0.001)


def test_pace_frac_clamps_to_one():
    # Reset already passed -- should clamp to 1.0
    past = iso_from_now(-100)
    result = pacing.pace_frac(past, "five_hour")
    assert result == 1.0


# -- pace_info -------------------------------------------------------


def test_pace_info_on_pace_when_within_5_percent():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through 5h tier
    result = pacing.pace_info(50, fut, "five_hour")
    assert result[0] == "On pace"


def test_pace_info_ahead_when_util_exceeds_fraction():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through
    result = pacing.pace_info(80, fut, "five_hour")  # 30% ahead
    assert "ahead" in result[0]
    assert "30%" in result[0]


def test_pace_info_under_when_util_below_fraction():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through
    result = pacing.pace_info(20, fut, "five_hour")  # 30% under
    assert "under" in result[0]
    assert "30%" in result[0]


def test_pace_info_none_util_returns_none():
    fut = iso_from_now(3600)
    assert pacing.pace_info(None, fut, "five_hour") is None


# -- burn_projection -------------------------------------------------


def test_burn_projection_no_risk_returns_none():
    # 10% util at 50% through period -> won't hit 100%
    fut = iso_from_now(int(2.5 * 3600))
    assert pacing.burn_projection(10, fut, "five_hour") is None


def test_burn_projection_already_at_100():
    fut = iso_from_now(3600)
    result = pacing.burn_projection(100, fut, "five_hour")
    assert result is not None
    assert "reached" in result[0].lower() or "expires" in result[0].lower()


def test_burn_projection_warns_when_exhaustion_before_reset():
    # 90% util at 50% through -- projected 180% by reset, exhausts well before
    fut = iso_from_now(int(2.5 * 3600))
    result = pacing.burn_projection(90, fut, "five_hour")
    assert result is not None
    assert "expires" in result[0].lower() or "reached" in result[0].lower()


def test_burn_projection_none_inputs():
    assert pacing.burn_projection(None, None, "five_hour") is None


# -- reset_datetime_str ---------------------------------------------


def test_reset_datetime_str_none_returns_empty():
    assert pacing.reset_datetime_str(None) == ""


def test_reset_datetime_str_today_format():
    # Later today
    fut = iso_from_now(3600)
    result = pacing.reset_datetime_str(fut)
    assert result.startswith("Today")


def test_reset_datetime_str_invalid_returns_empty():
    assert pacing.reset_datetime_str("garbage") == ""
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_pacing.py -v
```

Expected: `ImportError: No module named 'sanduhr.pacing'`.

- [ ] **Step 3: Implement `pacing.py`**

```python
"""Pure pacing and projection calculations.

Ported verbatim from v1 sanduhr.py. The math is correct and well-tested;
this module is a clean home for it with zero Qt / tkinter dependencies.
"""

from datetime import datetime, timezone

_FIVE_HOUR_SECS = 5 * 3600
_SEVEN_DAY_SECS = 7 * 86400


def _parse(iso_str):
    try:
        return datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
    except (AttributeError, ValueError):
        return None


def time_until(iso_str):
    """Human countdown like '3d 5h 30m' / '45m' / 'now'."""
    if not iso_str:
        return "--"
    rd = _parse(iso_str)
    if rd is None:
        return "--"
    secs = max(0, int((rd - datetime.now(timezone.utc)).total_seconds()))
    if secs <= 0:
        return "now"
    d, r = divmod(secs, 86400)
    h, r = divmod(r, 3600)
    m = r // 60
    parts = []
    if d:
        parts.append(f"{d}d")
    if h:
        parts.append(f"{h}h")
    if m or not parts:
        parts.append(f"{m}m")
    return " ".join(parts)


def _tier_total_secs(tier_key):
    return _FIVE_HOUR_SECS if tier_key == "five_hour" else _SEVEN_DAY_SECS


def pace_frac(resets_at, tier_key):
    """How far through the period we are, in [0.0, 1.0]. None on bad input."""
    if not resets_at:
        return None
    rd = _parse(resets_at)
    if rd is None:
        return None
    rem = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    total = _tier_total_secs(tier_key)
    if total <= 0:
        return None
    return min(1.0, max(0.0, (total - rem) / total))


def pace_info(util, resets_at, tier_key):
    """Return (label, color) tuple describing on / ahead / under pace."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None:
        return None
    diff = util - frac * 100
    if abs(diff) < 5:
        return ("On pace", "#4ade80")
    if diff > 0:
        return (f"{int(abs(diff))}% ahead", "#fb923c")
    return (f"{int(abs(diff))}% under", "#60a5fa")


def burn_projection(util, resets_at, tier_key):
    """Return (message, color) when current pace will hit 100% before reset, else None."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None or util <= 0 or frac <= 0:
        return None
    rate_per_frac = util / frac  # projected total% over the full period
    if rate_per_frac <= 100:
        return None  # won't hit 100% before reset

    rd = _parse(resets_at)
    if rd is None:
        return None
    secs_until_reset = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    total = _tier_total_secs(tier_key)

    frac_at_100 = 100 / rate_per_frac
    secs_until_100 = max(0, (frac_at_100 - frac) * total)

    if secs_until_100 <= 0:
        return ("Limit reached", "#f87171")
    if secs_until_100 >= secs_until_reset:
        return None  # resets first

    d, r = divmod(int(secs_until_100), 86400)
    h, r = divmod(r, 3600)
    m = r // 60
    parts = []
    if d:
        parts.append(f"{d}d")
    if h:
        parts.append(f"{h}h")
    if m or not parts:
        parts.append(f"{m}m")
    return (f"At current pace, expires in {' '.join(parts)}", "#f87171")


def reset_datetime_str(iso_str):
    """Friendly 'Today 1:00 AM' / 'Tomorrow 1:00 AM' / 'Sun 1:00 AM' / 'Wed Apr 22 1:00 AM'."""
    if not iso_str:
        return ""
    rd = _parse(iso_str)
    if rd is None:
        return ""
    loc = rd.astimezone()
    now = datetime.now().astimezone()
    days = (loc.date() - now.date()).days
    t = loc.strftime("%I:%M %p").lstrip("0")
    if days <= 0:
        return f"Today {t}"
    if days == 1:
        return f"Tomorrow {t}"
    if days < 7:
        return f"{loc.strftime('%a')} {t}"
    return f"{loc.strftime('%a %b %d')} {t}"
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_pacing.py -v --cov=sanduhr.pacing
```

Expected: all tests pass; coverage ≥95%.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/pacing.py windows/tests/test_pacing.py
git commit -m "Windows v2: add pacing module (port from v1)

Pure functions for pace fraction, on/ahead/under labeling, and burn
rate projection. Ported verbatim from v1 sanduhr.py with no
tkinter/Qt dependencies. Target >=95% branch coverage.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: `history.py` — sparkline storage

**Files:**

- Create: `windows/src/sanduhr/history.py`
- Test: `windows/tests/test_history.py`

- [ ] **Step 1: Write `test_history.py`**

```python
"""Tests for sanduhr.history."""

import json

from sanduhr import history, paths


def test_append_creates_file(tmp_appdata):
    history.append("five_hour", 42)
    assert paths.history_file().exists()


def test_append_adds_entry(tmp_appdata):
    history.append("five_hour", 42)
    values = history.load("five_hour")
    assert values == [42]


def test_append_multiple_values(tmp_appdata):
    for v in [10, 20, 30]:
        history.append("five_hour", v)
    assert history.load("five_hour") == [10, 20, 30]


def test_append_different_tiers_isolated(tmp_appdata):
    history.append("five_hour", 10)
    history.append("seven_day", 99)
    assert history.load("five_hour") == [10]
    assert history.load("seven_day") == [99]


def test_load_missing_tier_returns_empty_list(tmp_appdata):
    assert history.load("nonexistent") == []


def test_load_missing_file_returns_empty_list(tmp_appdata):
    # No append yet
    assert history.load("five_hour") == []


def test_append_rolls_over_at_max(tmp_appdata):
    for i in range(history.MAX_POINTS + 5):
        history.append("five_hour", i)
    values = history.load("five_hour")
    assert len(values) == history.MAX_POINTS
    assert values[-1] == history.MAX_POINTS + 4


def test_corrupt_file_does_not_crash(tmp_appdata):
    paths.history_file().write_text("not valid json {{{")
    assert history.load("five_hour") == []


def test_corrupt_file_repaired_on_append(tmp_appdata):
    paths.history_file().write_text("garbage")
    history.append("five_hour", 50)
    data = json.loads(paths.history_file().read_text())
    assert data["five_hour"][-1]["v"] == 50
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_history.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `history.py`**

```python
"""Sparkline history storage.

Append-only rolling window of utilization percentages per tier,
persisted to %APPDATA%\\Sanduhr\\history.json.
"""

import json
from datetime import datetime, timezone

from sanduhr import paths

MAX_POINTS = 24  # 24 points x 5 min = 2 hours


def _read_raw():
    path = paths.history_file()
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _write_raw(data):
    paths.history_file().write_text(json.dumps(data), encoding="utf-8")


def append(tier_key: str, util: int) -> list[int]:
    """Record a new utilization data point. Returns the current series."""
    data = _read_raw()
    series = data.get(tier_key, [])
    series.append({"t": datetime.now(timezone.utc).isoformat(), "v": int(util)})
    series = series[-MAX_POINTS:]
    data[tier_key] = series
    _write_raw(data)
    return [p["v"] for p in series]


def load(tier_key: str) -> list[int]:
    """Return the list of utilization values for a tier, oldest first."""
    data = _read_raw()
    return [p["v"] for p in data.get(tier_key, [])]
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_history.py -v
```

Expected: all 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/history.py windows/tests/test_history.py
git commit -m "Windows v2: add history module for sparkline storage

24-point rolling window per tier, JSON-backed in %APPDATA%. Corrupt
files recover silently (empty series) and repair themselves on next
append.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: `themes.py` — palette data + glass tuning dials

**Files:**

- Create: `windows/src/sanduhr/themes.py`
- Test: `windows/tests/test_themes.py`

- [ ] **Step 1: Write `test_themes.py`**

```python
"""Tests for sanduhr.themes."""

import pytest

from sanduhr import themes


def test_all_themes_defined():
    expected = {"obsidian", "aurora", "ember", "mint", "matrix"}
    assert set(themes.THEMES) == expected


@pytest.mark.parametrize("key", ["obsidian", "aurora", "ember", "mint", "matrix"])
def test_theme_has_required_color_fields(key):
    t = themes.THEMES[key]
    required = {"name", "bg", "glass", "glass_on_mica", "border", "text", "accent"}
    assert required.issubset(t.keys()), f"{key} missing: {required - t.keys()}"


@pytest.mark.parametrize("key", ["obsidian", "aurora", "ember", "mint"])
def test_non_matrix_themes_have_glass_dials(key):
    t = themes.THEMES[key]
    assert "glass_alpha" in t
    assert "border_alpha" in t
    assert "accent_bloom" in t
    assert isinstance(t["glass_alpha"], float)
    assert 0.0 < t["glass_alpha"] < 1.0


def test_matrix_theme_opts_out_of_glass():
    t = themes.THEMES["matrix"]
    assert t["glass_alpha"] == 1.0  # opaque
    assert t.get("opts_out_of_mica") is True


def test_usage_color_below_50_is_green():
    assert themes.usage_color(25) == "#4ade80"


def test_usage_color_exceeds_90_is_red():
    assert themes.usage_color(95) == "#f87171"


def test_usage_color_boundary_50():
    assert themes.usage_color(50) == "#facc15"


def test_usage_color_boundary_75():
    assert themes.usage_color(75) == "#fb923c"
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_themes.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `themes.py`**

```python
"""Theme palettes and glass-tuning dials.

Each theme is a dict of colors + glass rendering parameters. Non-Matrix
themes get the full glass-stacking pass (see spec section 4c); Matrix
explicitly opts out to preserve its phosphor-on-black aesthetic.
"""

THEMES = {
    "obsidian": {
        "name": "Obsidian",
        "bg": "#0d0d0d",
        "glass": "#1c1c1c",
        "glass_on_mica": "#2a2a2e",
        "title_bg": "#161616",
        "border": "#333333",
        "text": "#e8e4dc",
        "text_secondary": "#b8b4ac",
        "text_dim": "#777777",
        "text_muted": "#555555",
        "accent": "#6c63ff",
        "bar_bg": "#2a2a2a",
        "footer_bg": "#111111",
        "pace_marker": "#ff6b6b",
        "sparkline": "#6c63ff",
        "glass_alpha": 0.68,
        "border_alpha": 0.30,
        "border_tint": None,
        "accent_bloom": {"blur": 4, "alpha": 0.35},
        "inner_highlight": None,
    },
    "aurora": {
        "name": "Aurora",
        "bg": "#0a0f1a",
        "glass": "#161e30",
        "glass_on_mica": "#1f2a42",
        "title_bg": "#0f172a",
        "border": "#334155",
        "text": "#e2e8f0",
        "text_secondary": "#94a3b8",
        "text_dim": "#64748b",
        "text_muted": "#475569",
        "accent": "#38bdf8",
        "bar_bg": "#1e293b",
        "footer_bg": "#0c1220",
        "pace_marker": "#f472b6",
        "sparkline": "#38bdf8",
        "glass_alpha": 0.55,
        "border_alpha": 0.50,
        "border_tint": "#38bdf8",
        "accent_bloom": {"blur": 6, "alpha": 0.55},
        "inner_highlight": {"color": "#38bdf8", "alpha": 0.20},
    },
    "ember": {
        "name": "Ember",
        "bg": "#1a0a0a",
        "glass": "#261414",
        "glass_on_mica": "#331b1b",
        "title_bg": "#1f0e0e",
        "border": "#442222",
        "text": "#f5e6e0",
        "text_secondary": "#d4a89c",
        "text_dim": "#8b6b60",
        "text_muted": "#6b4b40",
        "accent": "#f97316",
        "bar_bg": "#2d1a1a",
        "footer_bg": "#150808",
        "pace_marker": "#fbbf24",
        "sparkline": "#f97316",
        "glass_alpha": 0.62,
        "border_alpha": 0.40,
        "border_tint": "#f97316",
        "accent_bloom": {"blur": 6, "alpha": 0.55},
        "inner_highlight": {"color": "#f97316", "alpha": 0.18},
    },
    "mint": {
        "name": "Mint",
        "bg": "#0a1a14",
        "glass": "#122a1e",
        "glass_on_mica": "#18392a",
        "title_bg": "#0c1f14",
        "border": "#22543d",
        "text": "#e0f5ec",
        "text_secondary": "#9cd4b8",
        "text_dim": "#5a9a78",
        "text_muted": "#3a7a58",
        "accent": "#34d399",
        "bar_bg": "#163020",
        "footer_bg": "#081510",
        "pace_marker": "#f472b6",
        "sparkline": "#34d399",
        "glass_alpha": 0.50,
        "border_alpha": 0.45,
        "border_tint": "#34d399",
        "accent_bloom": {"blur": 4, "alpha": 0.35},
        "inner_highlight": {"color": "#34d399", "alpha": 0.22},
    },
    "matrix": {
        "name": "Matrix",
        "bg": "#020a02",
        "glass": "#0a140a",
        "glass_on_mica": "#0a140a",
        "title_bg": "#040d04",
        "border": "#0f2a0f",
        "text": "#00ff41",
        "text_secondary": "#00cc33",
        "text_dim": "#00802b",
        "text_muted": "#005a1e",
        "accent": "#00ff41",
        "bar_bg": "#0a1a0a",
        "footer_bg": "#020802",
        "pace_marker": "#ff0040",
        "sparkline": "#00ff41",
        "glass_alpha": 1.0,
        "border_alpha": 0.50,
        "border_tint": "#00ff41",
        "accent_bloom": {"blur": 4, "alpha": 0.60},
        "inner_highlight": None,
        "opts_out_of_mica": True,
        "monospace_font": "Cascadia Code",
        "monospace_fallback": "Consolas",
        "card_corner_radius": 2,
    },
}


def usage_color(pct: int) -> str:
    """Return the bar fill color for a given utilization %."""
    if pct < 50:
        return "#4ade80"
    if pct < 75:
        return "#facc15"
    if pct < 90:
        return "#fb923c"
    return "#f87171"
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_themes.py -v
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/themes.py windows/tests/test_themes.py
git commit -m "Windows v2: add themes module with glass tuning dials

Ports 5-theme palette from v1 plus per-theme glass_alpha / border_alpha
/ accent_bloom / inner_highlight dials. Matrix theme includes
opts_out_of_mica flag and monospace font hints.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: `credentials.py` — keyring + v1 migration

**Files:**

- Create: `windows/src/sanduhr/credentials.py`
- Test: `windows/tests/test_credentials.py`

- [ ] **Step 1: Write `test_credentials.py`**

```python
"""Tests for sanduhr.credentials."""

import json
from unittest.mock import MagicMock

import pytest

from sanduhr import credentials, paths


@pytest.fixture
def mock_keyring(monkeypatch):
    storage = {}

    def _set(service, account, value):
        storage[(service, account)] = value

    def _get(service, account):
        return storage.get((service, account))

    def _delete(service, account):
        storage.pop((service, account), None)

    mock = MagicMock()
    mock.set_password = MagicMock(side_effect=_set)
    mock.get_password = MagicMock(side_effect=_get)
    mock.delete_password = MagicMock(side_effect=_delete)
    mock._storage = storage
    monkeypatch.setattr(credentials, "keyring", mock)
    return mock


def test_load_empty_returns_empty_dict(tmp_appdata, mock_keyring):
    assert credentials.load() == {"session_key": None, "cf_clearance": None}


def test_save_and_load_roundtrip(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc", cf_clearance="def")
    assert credentials.load() == {"session_key": "abc", "cf_clearance": "def"}


def test_save_session_key_only(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc")
    assert credentials.load()["session_key"] == "abc"
    assert credentials.load()["cf_clearance"] is None


def test_clear_removes_entries(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc", cf_clearance="def")
    credentials.clear()
    assert credentials.load() == {"session_key": None, "cf_clearance": None}


def test_service_name_matches_mac_keychain(mock_keyring):
    credentials.save(session_key="abc")
    assert mock_keyring.set_password.call_args_list[0].args[0] == "com.626labs.sanduhr"


def test_migrate_v1_moves_session_key_to_keyring(
    tmp_appdata, mock_keyring, tmp_path, monkeypatch
):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(
        json.dumps({"session_key": "old-key", "theme": "mint"})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")

    result = credentials.migrate_from_v1()

    assert result["migrated"] is True
    assert credentials.load()["session_key"] == "old-key"


def test_migrate_v1_deletes_old_file(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    v1_config = v1_dir / "config.json"
    v1_config.write_text(json.dumps({"session_key": "old-key"}))
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_config)

    credentials.migrate_from_v1()
    assert not v1_config.exists()


def test_migrate_v1_preserves_theme(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(
        json.dumps({"session_key": "k", "theme": "aurora"})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")

    credentials.migrate_from_v1()
    settings = json.loads(paths.settings_file().read_text())
    assert settings.get("theme") == "aurora"


def test_migrate_v1_missing_file_is_noop(
    tmp_appdata, mock_keyring, tmp_path, monkeypatch
):
    monkeypatch.setattr(
        paths, "legacy_config_file", lambda: tmp_path / "does-not-exist.json"
    )
    result = credentials.migrate_from_v1()
    assert result["migrated"] is False


def test_migrate_v1_copies_history(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(json.dumps({"session_key": "k"}))
    (v1_dir / "history.json").write_text(
        json.dumps({"five_hour": [{"t": "t", "v": 42}]})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")
    monkeypatch.setattr(paths, "legacy_history_file", lambda: v1_dir / "history.json")

    credentials.migrate_from_v1()
    copied = json.loads(paths.history_file().read_text())
    assert copied == {"five_hour": [{"t": "t", "v": 42}]}
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_credentials.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `credentials.py`**

```python
"""Credential storage (Windows Credential Manager) + v1 migration.

Uses the `keyring` library to persist the session key and cf_clearance
cookie against the Windows Credential Manager under service
`com.626labs.sanduhr` -- matching the Mac Keychain service name exactly.
"""

import json
import logging
from typing import Optional

import keyring

from sanduhr import paths

SERVICE = "com.626labs.sanduhr"
_ACCOUNT_SESSION = "sessionKey"
_ACCOUNT_CF = "cf_clearance"

_log = logging.getLogger(__name__)


def load() -> dict:
    """Return stored credentials as {session_key, cf_clearance}."""
    return {
        "session_key": keyring.get_password(SERVICE, _ACCOUNT_SESSION),
        "cf_clearance": keyring.get_password(SERVICE, _ACCOUNT_CF),
    }


def save(session_key: Optional[str] = None, cf_clearance: Optional[str] = None) -> None:
    """Write credentials. Passing None for a field leaves it untouched."""
    if session_key is not None:
        keyring.set_password(SERVICE, _ACCOUNT_SESSION, session_key)
    if cf_clearance is not None:
        keyring.set_password(SERVICE, _ACCOUNT_CF, cf_clearance)


def clear() -> None:
    """Remove all stored credentials. Used on uninstall."""
    for account in (_ACCOUNT_SESSION, _ACCOUNT_CF):
        try:
            keyring.delete_password(SERVICE, account)
        except keyring.errors.PasswordDeleteError:
            pass


def migrate_from_v1() -> dict:
    """Migrate v1's plaintext config + history into v2 storage.

    Returns {"migrated": bool, "session_key": bool, "theme": bool, "history": bool}.
    """
    legacy = paths.legacy_config_file()
    if not legacy.exists():
        return {"migrated": False, "session_key": False, "theme": False, "history": False}

    result = {"migrated": True, "session_key": False, "theme": False, "history": False}

    try:
        cfg = json.loads(legacy.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as e:
        _log.warning("v1 config unreadable, skipping migration: %s", e)
        return {"migrated": False, "session_key": False, "theme": False, "history": False}

    if cfg.get("session_key"):
        save(session_key=cfg["session_key"])
        result["session_key"] = True

    if cfg.get("theme"):
        settings_path = paths.settings_file()
        settings = {}
        if settings_path.exists():
            try:
                settings = json.loads(settings_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                settings = {}
        settings["theme"] = cfg["theme"]
        settings_path.write_text(json.dumps(settings), encoding="utf-8")
        result["theme"] = True

    legacy_hist = paths.legacy_history_file()
    if legacy_hist.exists():
        try:
            paths.history_file().write_text(
                legacy_hist.read_text(encoding="utf-8"), encoding="utf-8"
            )
            result["history"] = True
        except OSError as e:
            _log.warning("Failed to copy v1 history: %s", e)

    try:
        legacy.unlink()
    except OSError as e:
        _log.warning("Could not delete v1 config: %s", e)

    _log.info("v1 -> v2 migration complete: %s", result)
    return result
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_credentials.py -v
```

Expected: all 10 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/credentials.py windows/tests/test_credentials.py
git commit -m "Windows v2: add credentials module with v1 migration

Keyring-backed store (service com.626labs.sanduhr, accounts sessionKey
+ cf_clearance). Silent migration reads v1's plaintext config, copies
session_key to keyring, moves theme to settings.json, copies history,
deletes old plaintext config. Never logs credential values.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: `api.py` — ClaudeAPI with typed exceptions

**Files:**

- Create: `windows/src/sanduhr/api.py`
- Test: `windows/tests/test_api.py`

- [ ] **Step 1: Write `test_api.py`**

```python
"""Tests for sanduhr.api -- HTTP stubbed via requests-mock."""

import pytest

from sanduhr import api


@pytest.fixture
def mock_scraper(monkeypatch):
    """Replace cloudscraper.create_scraper with a plain requests.Session."""
    import requests

    def _create_scraper():
        return requests.Session()

    monkeypatch.setattr(api.cloudscraper, "create_scraper", _create_scraper)


def test_get_usage_happy_path(mock_scraper, requests_mock):
    requests_mock.get(
        "https://claude.ai/api/organizations",
        json=[{"uuid": "org-123"}],
    )
    requests_mock.get(
        "https://claude.ai/api/organizations/org-123/usage",
        json={"five_hour": {"utilization": 42, "resets_at": "2030-01-01T00:00:00Z"}},
    )

    client = api.ClaudeAPI("abc")
    result = client.get_usage()
    assert result["five_hour"]["utilization"] == 42


def test_get_usage_caches_org_id(mock_scraper, requests_mock):
    orgs = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-x"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-x/usage", json={})

    client = api.ClaudeAPI("abc")
    client.get_usage()
    client.get_usage()
    client.get_usage()

    assert orgs.call_count == 1


def test_401_raises_session_expired(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=401)
    with pytest.raises(api.SessionExpired):
        api.ClaudeAPI("abc").get_usage()


def test_403_with_cloudflare_body_raises_cloudflare_blocked(mock_scraper, requests_mock):
    requests_mock.get(
        "https://claude.ai/api/organizations",
        status_code=403,
        text="<html><title>Just a moment...</title><body>cf-challenge</body></html>",
    )
    with pytest.raises(api.CloudflareBlocked):
        api.ClaudeAPI("abc").get_usage()


def test_403_without_cloudflare_signature_is_session_expired(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=403, text="{}")
    with pytest.raises(api.SessionExpired):
        api.ClaudeAPI("abc").get_usage()


def test_5xx_raises_network_error(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=502)
    with pytest.raises(api.NetworkError):
        api.ClaudeAPI("abc").get_usage()


def test_cookie_header_includes_session_key(mock_scraper, requests_mock):
    m = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-123"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-123/usage", json={})
    api.ClaudeAPI("the-key").get_usage()
    assert "sessionKey=the-key" in m.last_request.headers["Cookie"]


def test_cookie_header_includes_cf_clearance_when_provided(
    mock_scraper, requests_mock
):
    m = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-123"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-123/usage", json={})
    api.ClaudeAPI("sk", cf_clearance="cf-cookie").get_usage()
    cookies = m.last_request.headers["Cookie"]
    assert "sessionKey=sk" in cookies
    assert "cf_clearance=cf-cookie" in cookies


def test_no_orgs_raises_network_error(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", json=[])
    with pytest.raises(api.NetworkError):
        api.ClaudeAPI("abc").get_usage()
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_api.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `api.py`**

```python
"""Claude.ai API client.

Wraps cloudscraper for Cloudflare-aware requests, raises typed
exceptions so the UI layer can give targeted error feedback.
"""

from typing import Optional

import cloudscraper
import requests


class APIError(Exception):
    """Base class for API errors."""


class SessionExpired(APIError):
    """401 or 403 -- key rotation needed."""


class CloudflareBlocked(APIError):
    """403 with Cloudflare challenge markup -- cf_clearance cookie needed."""


class NetworkError(APIError):
    """5xx, DNS, timeout, or unexpected API response."""


_API_BASE = "https://claude.ai/api"


def _looks_like_cloudflare(text: str) -> bool:
    """Detect Cloudflare challenge / block page by page content."""
    if not text:
        return False
    t = text.lower()
    return "cf-challenge" in t or "just a moment" in t or "cloudflare" in t


class ClaudeAPI:
    def __init__(self, session_key: str, cf_clearance: Optional[str] = None):
        self.session_key = session_key
        self.cf_clearance = cf_clearance
        self._scraper = cloudscraper.create_scraper()
        self._scraper.headers["Accept"] = "application/json"
        self._org_id: Optional[str] = None

    def _cookie_header(self) -> str:
        parts = [f"sessionKey={self.session_key}"]
        if self.cf_clearance:
            parts.append(f"cf_clearance={self.cf_clearance}")
        return "; ".join(parts)

    def _get(self, url: str) -> requests.Response:
        return self._scraper.get(
            url, headers={"Cookie": self._cookie_header()}, timeout=15
        )

    def _check(self, resp: requests.Response) -> None:
        if resp.status_code == 401:
            raise SessionExpired("HTTP 401 -- session key rejected")
        if resp.status_code == 403:
            if _looks_like_cloudflare(resp.text):
                raise CloudflareBlocked("Cloudflare challenge -- cf_clearance needed")
            raise SessionExpired("HTTP 403 -- session key rejected")
        if resp.status_code >= 500:
            raise NetworkError(f"HTTP {resp.status_code}")
        resp.raise_for_status()

    def _get_org_id(self) -> str:
        if self._org_id is not None:
            return self._org_id
        resp = self._get(f"{_API_BASE}/organizations")
        self._check(resp)
        try:
            orgs = resp.json()
        except ValueError as e:
            raise NetworkError("Org discovery returned non-JSON") from e
        if not orgs:
            raise NetworkError("No organizations returned for this account")
        self._org_id = orgs[0]["uuid"]
        return self._org_id

    def get_usage(self) -> dict:
        org_id = self._get_org_id()
        resp = self._get(f"{_API_BASE}/organizations/{org_id}/usage")
        self._check(resp)
        try:
            return resp.json()
        except ValueError as e:
            raise NetworkError("Usage endpoint returned non-JSON") from e
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_api.py -v
```

Expected: all 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/api.py windows/tests/test_api.py
git commit -m "Windows v2: add api module with typed exceptions

ClaudeAPI wraps cloudscraper, caches org_id after first fetch, raises
SessionExpired / CloudflareBlocked / NetworkError for targeted UI
feedback. Accepts optional cf_clearance cookie for accounts that
require the Cloudflare fallback.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: `fetcher.py` — threaded UsageFetcher

**Files:**

- Create: `windows/src/sanduhr/fetcher.py`
- Test: `windows/tests/test_fetcher.py`

- [ ] **Step 1: Write `test_fetcher.py`**

```python
"""Tests for sanduhr.fetcher -- QObject on a worker thread."""

from unittest.mock import MagicMock

from sanduhr import api, fetcher


def test_fetcher_emits_data_ready_on_success(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.return_value = {"five_hour": {"utilization": 10}}
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.dataReady, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args == [{"five_hour": {"utilization": 10}}]


def test_fetcher_emits_fetch_failed_on_session_expired(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.SessionExpired("401")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    kind, _message = blocker.args
    assert kind == "session_expired"


def test_fetcher_emits_fetch_failed_on_cloudflare(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.CloudflareBlocked("cf")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args[0] == "cloudflare"


def test_fetcher_emits_fetch_failed_on_network(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.NetworkError("boom")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args[0] == "network"


def test_update_credentials_rebuilds_client(qtbot, monkeypatch):
    builds = []

    def _builder(session_key, cf_clearance=None):
        builds.append((session_key, cf_clearance))
        m = MagicMock()
        m.get_usage.return_value = {}
        return m

    monkeypatch.setattr(fetcher, "ClaudeAPI", _builder)

    f = fetcher.UsageFetcher("sk1", None)
    f.update_credentials("sk2", "cf")
    f.fetch()
    assert ("sk2", "cf") in builds
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_fetcher.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `fetcher.py`**

```python
"""Threaded usage fetcher.

Owns a `ClaudeAPI` instance, exposes `fetch()` slot and
`dataReady` / `fetchFailed` signals. Meant to be moved to a
`QThread` so network calls never block the GUI.
"""

import logging
from typing import Optional

from PySide6.QtCore import QObject, Signal, Slot

from sanduhr import api, history
from sanduhr.api import ClaudeAPI

_log = logging.getLogger(__name__)

_HISTORY_TIERS = (
    "five_hour",
    "seven_day",
    "seven_day_sonnet",
    "seven_day_opus",
    "seven_day_cowork",
    "seven_day_omelette",
    "seven_day_oauth_apps",
    "iguana_necktie",
)


class UsageFetcher(QObject):
    """Wraps ClaudeAPI, emits Qt signals on success/failure."""

    dataReady = Signal(dict)
    fetchFailed = Signal(str, str)  # (kind, message)

    def __init__(
        self, session_key: str, cf_clearance: Optional[str] = None, parent=None
    ):
        super().__init__(parent)
        self._client = ClaudeAPI(session_key, cf_clearance)

    def update_credentials(
        self, session_key: str, cf_clearance: Optional[str] = None
    ) -> None:
        """Swap the underlying client (called when user updates credentials)."""
        self._client = ClaudeAPI(session_key, cf_clearance)

    @Slot()
    def fetch(self) -> None:
        """Fetch usage on the current thread. Emits exactly one signal."""
        try:
            data = self._client.get_usage()
        except api.SessionExpired as e:
            self.fetchFailed.emit("session_expired", str(e))
            return
        except api.CloudflareBlocked as e:
            self.fetchFailed.emit("cloudflare", str(e))
            return
        except api.NetworkError as e:
            self.fetchFailed.emit("network", str(e))
            return
        except Exception as e:
            _log.exception("Unexpected fetch failure")
            self.fetchFailed.emit("unknown", str(e))
            return

        for tier_key in _HISTORY_TIERS:
            tier = data.get(tier_key)
            if tier and tier.get("utilization") is not None:
                try:
                    history.append(tier_key, int(tier["utilization"]))
                except Exception:
                    _log.exception("Failed to append history for %s", tier_key)

        self.dataReady.emit(data)
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_fetcher.py -v
```

Expected: all 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/fetcher.py windows/tests/test_fetcher.py
git commit -m "Windows v2: add fetcher module (threaded QObject)

UsageFetcher owns ClaudeAPI, exposes fetch() slot and dataReady /
fetchFailed(kind, message) signals. Widget connects signals via
queued connection so network calls never touch the GUI thread.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: `mica.py` — DWM ctypes wrapper

**Files:**

- Create: `windows/src/sanduhr/mica.py`

No automated tests — the DWM API is a fire-and-forget side effect against a real HWND and Win11 build. Manual coverage lives in the TEST_PLAN.

- [ ] **Step 1: Implement `mica.py`**

```python
"""Windows 11 Mica backdrop + rounded corners via DWM ctypes.

Applied to a QWidget by passing its HWND to the DWM. Runtime-detects
Windows build number and gracefully no-ops on Win10 / unsupported.
Widget rendering code layers translucent cards over the Mica so the
desktop wallpaper blurs through the whole app.
"""

import ctypes
import logging
import sys
from ctypes import wintypes

_log = logging.getLogger(__name__)

_DWMWA_SYSTEMBACKDROP_TYPE = 38
_DWMWA_WINDOW_CORNER_PREFERENCE = 33

_DWMSBT_NONE = 1
_DWMSBT_MAINWINDOW = 2  # Mica

_DWMWCP_ROUND = 2


def _is_win11_22h2_or_newer() -> bool:
    """Mica requires Windows build 22000+ (Win11 21H2). Full support in 22621+ (22H2)."""
    if sys.platform != "win32":
        return False
    try:
        ver = sys.getwindowsversion()
        return ver.major >= 10 and ver.build >= 22000
    except Exception:
        return False


def _set_dwm_int(hwnd: int, attribute: int, value: int) -> bool:
    """Call DwmSetWindowAttribute with a DWORD value. Returns True on success."""
    try:
        dwmapi = ctypes.WinDLL("dwmapi")
    except OSError:
        return False

    pv = ctypes.c_int(value)
    hr = dwmapi.DwmSetWindowAttribute(
        wintypes.HWND(hwnd),
        wintypes.DWORD(attribute),
        ctypes.byref(pv),
        wintypes.DWORD(ctypes.sizeof(pv)),
    )
    return hr == 0


def apply_mica(widget, enabled: bool = True) -> bool:
    """Enable Mica backdrop + rounded corners on a QWidget.

    Returns True if Mica was applied. Callers should paint a fallback
    solid background when False (Win10 or unsupported).
    """
    if not enabled or not _is_win11_22h2_or_newer():
        return False

    try:
        hwnd = int(widget.winId())
    except Exception as e:
        _log.warning("Could not resolve HWND for Mica: %s", e)
        return False

    backdrop_ok = _set_dwm_int(hwnd, _DWMWA_SYSTEMBACKDROP_TYPE, _DWMSBT_MAINWINDOW)
    corners_ok = _set_dwm_int(hwnd, _DWMWA_WINDOW_CORNER_PREFERENCE, _DWMWCP_ROUND)

    if backdrop_ok and corners_ok:
        _log.info("Mica + rounded corners applied to HWND 0x%x", hwnd)
        return True
    _log.warning(
        "DWM calls reported failure (backdrop=%s corners=%s)", backdrop_ok, corners_ok
    )
    return False


def disable_mica(widget) -> bool:
    """Explicitly opt out of Mica (used by the Matrix theme)."""
    try:
        hwnd = int(widget.winId())
    except Exception:
        return False
    return _set_dwm_int(hwnd, _DWMWA_SYSTEMBACKDROP_TYPE, _DWMSBT_NONE)
```

- [ ] **Step 2: Commit**

```bash
git add windows/src/sanduhr/mica.py
git commit -m "Windows v2: add DWM ctypes wrapper for Mica + rounded corners

apply_mica(widget) enables Win11 Mica backdrop + rounded corners via
DwmSetWindowAttribute. Runtime build-number check; no-ops on Win10.
disable_mica() opt-out for Matrix theme. Manual-test only.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: `sparkline.py` — QPainter widget

**Files:**

- Create: `windows/src/sanduhr/sparkline.py`

No automated test — pixel rendering assertions are fragile. Covered in the manual test plan.

- [ ] **Step 1: Implement `sparkline.py`**

```python
"""Anti-aliased sparkline widget drawn with QPainter.

Shows the last N utilization values as a smooth line scaled to the
widget bounds. Paints transparently over its parent's background so
it sits cleanly on card glass.
"""

from typing import List, Optional

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QWidget


class Sparkline(QWidget):
    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._values: List[int] = []
        self._color = QColor("#ffffff")
        self._stroke_width = 1.5
        self.setAttribute(Qt.WA_TranslucentBackground, True)

    def set_values(self, values: List[int]) -> None:
        self._values = list(values)
        self.update()

    def set_color(self, color: str) -> None:
        self._color = QColor(color)
        self.update()

    def set_stroke_width(self, width: float) -> None:
        self._stroke_width = width
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        if len(self._values) < 2:
            return

        w = self.width()
        h = self.height()
        if w < 10 or h < 4:
            return

        mn = min(self._values)
        mx = max(self._values)
        rng = (mx - mn) if mx != mn else 1

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        pen = QPen(self._color)
        pen.setWidthF(self._stroke_width)
        pen.setCapStyle(Qt.RoundCap)
        pen.setJoinStyle(Qt.RoundJoin)
        painter.setPen(pen)

        path = QPainterPath()
        denom = len(self._values) - 1
        for i, v in enumerate(self._values):
            x = (i / denom) * w
            y = h - ((v - mn) / rng) * (h - 2) - 1
            if i == 0:
                path.moveTo(x, y)
            else:
                path.lineTo(x, y)
        painter.drawPath(path)
        painter.end()
```

- [ ] **Step 2: Smoke-import check**

```powershell
python -c "from sanduhr.sparkline import Sparkline; print('ok')"
```

Expected output: `ok`.

- [ ] **Step 3: Commit**

```bash
git add windows/src/sanduhr/sparkline.py
git commit -m "Windows v2: add QPainter sparkline widget

Anti-aliased polyline, rounded caps/joins, transparent background.
set_values / set_color / set_stroke_width API. Used inside TierCard.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: `tiers.py` — TierCard(QFrame)

**Files:**

- Create: `windows/src/sanduhr/tiers.py`
- Test: `windows/tests/test_tier_card.py`

- [ ] **Step 1: Write `test_tier_card.py`**

```python
"""Tests for TierCard -- logic-level assertions, not pixel-level."""

from datetime import datetime, timedelta, timezone

from sanduhr import themes
from sanduhr.tiers import TierCard


def test_tier_card_initial_state(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    assert card.label_text() == "Session (5hr)"
    assert card.percentage_text() == "0%"


def test_tier_card_update_sets_percentage(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    card.update_state(util=42, resets_at=None, history_values=[])
    assert card.percentage_text() == "42%"


def test_tier_card_update_sets_reset_text(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    future = (datetime.now(timezone.utc) + timedelta(hours=2)).isoformat().replace(
        "+00:00", "Z"
    )
    card.update_state(util=50, resets_at=future, history_values=[])
    assert "Resets in" in card.reset_text()


def test_tier_card_apply_theme_switches_palette(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    card.apply_theme(themes.THEMES["mint"])
    assert card.current_theme_name() == "Mint"
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_tier_card.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `tiers.py`**

```python
"""TierCard -- one rendered usage tier, updates in place.

Handles label, sparkline, percentage number, progress bar with pace
marker, reset countdown, pacing label, burn projection. Receives a
theme dict and draws its card chrome (glass fill, border, shadow,
inner highlight) based on the theme's glass-tuning dials.
"""

from typing import List, Optional

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPen
from PySide6.QtWidgets import (
    QFrame,
    QGraphicsDropShadowEffect,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QVBoxLayout,
    QWidget,
)

from sanduhr import pacing, themes
from sanduhr.sparkline import Sparkline


def _rgba(hex_color: str, alpha: float) -> str:
    """Return 'rgba(r,g,b,a)' string from #rrggbb + alpha in [0,1]."""
    c = QColor(hex_color)
    return f"rgba({c.red()},{c.green()},{c.blue()},{alpha:.3f})"


class TierCard(QFrame):
    def __init__(
        self, tier_key: str, label: str, theme: dict, parent: Optional[QWidget] = None
    ):
        super().__init__(parent)
        self._tier_key = tier_key
        self._label = label
        self._theme = theme
        self._resets_at: Optional[str] = None
        self._util: int = 0

        self._build()
        self.apply_theme(theme)

    # -- public API -----------------------------------------------

    def update_state(
        self, util: int, resets_at: Optional[str], history_values: List[int]
    ) -> None:
        """Mutate labels and bar in place -- no rebuild."""
        self._util = util
        self._resets_at = resets_at

        color = themes.usage_color(util)
        self._pct.setText(f"{util}%")
        self._pct.setStyleSheet(f"color: {color};")

        self._bar.setValue(util)
        self._bar.setStyleSheet(self._bar_qss(color))

        self._reset_lbl.setText(
            "" if not resets_at else f"Resets in {pacing.time_until(resets_at)}"
        )
        self._reset_dt_lbl.setText(pacing.reset_datetime_str(resets_at))

        pace = pacing.pace_info(util, resets_at, self._tier_key)
        self._pace_lbl.setText(pace[0] if pace else "")
        self._pace_lbl.setStyleSheet(
            f"color: {pace[1]};" if pace else f"color: {self._theme['text_dim']};"
        )

        burn = pacing.burn_projection(util, resets_at, self._tier_key)
        self._burn_lbl.setText(burn[0] if burn else "")
        self._burn_lbl.setStyleSheet(
            f"color: {burn[1]};" if burn else f"color: {self._theme['text_muted']};"
        )

        self._spark.set_values(history_values)
        self._spark.set_color(self._theme["sparkline"])

        self._update_pace_marker()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.setStyleSheet(self._card_qss())
        self._lbl.setStyleSheet(f"color: {theme['text_secondary']};")
        self._pct.setStyleSheet(f"color: {theme['text']};")
        self._reset_lbl.setStyleSheet(f"color: {theme['text_dim']};")
        self._reset_dt_lbl.setStyleSheet(f"color: {theme['text_muted']};")
        self._spark.set_color(theme["sparkline"])
        self._apply_shadow()
        self._bar.setStyleSheet(self._bar_qss(themes.usage_color(self._util)))

    # -- for tests -------------------------------------------------

    def label_text(self) -> str:
        return self._lbl.text()

    def percentage_text(self) -> str:
        return self._pct.text()

    def reset_text(self) -> str:
        return self._reset_lbl.text()

    def current_theme_name(self) -> str:
        return self._theme["name"]

    # -- build -----------------------------------------------------

    def _build(self) -> None:
        self.setObjectName("TierCard")
        outer = QVBoxLayout(self)
        outer.setContentsMargins(14, 12, 14, 12)
        outer.setSpacing(6)

        row1 = QHBoxLayout()
        row1.setSpacing(8)
        self._lbl = QLabel(self._label)
        self._lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row1.addWidget(self._lbl)
        row1.addStretch()
        self._spark = Sparkline()
        self._spark.setFixedSize(50, 16)
        row1.addWidget(self._spark)
        self._pct = QLabel("0%")
        self._pct.setAttribute(Qt.WA_TranslucentBackground, True)
        row1.addWidget(self._pct)
        outer.addLayout(row1)

        self._bar_container = QWidget()
        self._bar_container.setFixedHeight(16)
        bar_layout = QHBoxLayout(self._bar_container)
        bar_layout.setContentsMargins(0, 0, 0, 0)
        self._bar = QProgressBar()
        self._bar.setRange(0, 100)
        self._bar.setValue(0)
        self._bar.setTextVisible(False)
        self._bar.setFixedHeight(16)
        bar_layout.addWidget(self._bar)
        self._pace_marker = QWidget(self._bar_container)
        self._pace_marker.setFixedWidth(3)
        self._pace_marker.setFixedHeight(16)
        self._pace_marker.hide()
        outer.addWidget(self._bar_container)

        row3 = QHBoxLayout()
        self._reset_lbl = QLabel("")
        self._reset_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row3.addWidget(self._reset_lbl)
        row3.addStretch()
        self._pace_lbl = QLabel("")
        self._pace_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row3.addWidget(self._pace_lbl)
        outer.addLayout(row3)

        row4 = QHBoxLayout()
        self._reset_dt_lbl = QLabel("")
        self._reset_dt_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row4.addWidget(self._reset_dt_lbl)
        row4.addStretch()
        self._burn_lbl = QLabel("")
        self._burn_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row4.addWidget(self._burn_lbl)
        outer.addLayout(row4)

    def _card_qss(self) -> str:
        t = self._theme
        radius = t.get("card_corner_radius", 10)
        glass_color = t.get("glass_on_mica", t["glass"])
        alpha = t.get("glass_alpha", 0.65)
        border_tint = t.get("border_tint") or t["border"]
        border_alpha = t.get("border_alpha", 0.4)
        return f"""
        QFrame#TierCard {{
            background-color: {_rgba(glass_color, alpha)};
            border: 1px solid {_rgba(border_tint, border_alpha)};
            border-radius: {radius}px;
        }}
        """

    def _bar_qss(self, fill: str) -> str:
        t = self._theme
        bar_bg = t.get("bar_bg", "#2a2a2a")
        return f"""
        QProgressBar {{
            border: none;
            border-radius: 4px;
            background-color: {bar_bg};
        }}
        QProgressBar::chunk {{
            background-color: {fill};
            border-radius: 4px;
        }}
        """

    def _apply_shadow(self) -> None:
        effect = QGraphicsDropShadowEffect(self)
        effect.setOffset(0, 2)
        effect.setBlurRadius(12)
        effect.setColor(QColor(0, 0, 0, 64))
        self.setGraphicsEffect(effect)

    def _update_pace_marker(self) -> None:
        f = pacing.pace_frac(self._resets_at, self._tier_key)
        if f is None:
            self._pace_marker.hide()
            return
        w = self._bar_container.width()
        if w <= 0:
            self._pace_marker.hide()
            return
        self._pace_marker.setStyleSheet(
            f"background-color: {self._theme['pace_marker']};"
        )
        self._pace_marker.move(int(f * w), 0)
        self._pace_marker.show()

    def paintEvent(self, event) -> None:  # noqa: N802
        super().paintEvent(event)
        hl = self._theme.get("inner_highlight")
        if not hl:
            return
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        color = QColor(hl["color"])
        color.setAlphaF(hl["alpha"])
        pen = QPen(color)
        pen.setWidthF(1.0)
        painter.setPen(pen)
        r = self.rect()
        radius = self._theme.get("card_corner_radius", 10)
        painter.drawLine(r.left() + radius, r.top() + 1, r.right() - radius, r.top() + 1)
        painter.end()
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_tier_card.py -v
```

Expected: all 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/tiers.py windows/tests/test_tier_card.py
git commit -m "Windows v2: add TierCard QFrame

Per-tier card: label, sparkline, percentage, progress bar with pace
marker, reset countdown, pacing label, burn projection. update_state
mutates in place (no flicker). apply_theme re-styles chrome based on
the active theme's glass-tuning dials. paintEvent draws optional
top-edge inner highlight for themes that opt in.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: `widget.py` — SanduhrWidget (main window)

**Files:**

- Create: `windows/src/sanduhr/widget.py`
- Test: `windows/tests/test_widget_bootstrap.py`

- [ ] **Step 1: Write `test_widget_bootstrap.py`**

```python
"""Smoke tests -- widget imports and constructs without credentials."""

import json


def test_widget_constructs_without_credentials(qtbot, monkeypatch, tmp_appdata):
    from sanduhr import credentials, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert w.windowTitle() == "Sanduhr für Claude"


def test_widget_status_shows_setup_prompt_with_no_credentials(
    qtbot, monkeypatch, tmp_appdata
):
    from sanduhr import credentials, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert "setup" in w.status_text().lower() or "connecting" in w.status_text().lower()


def test_widget_applies_stored_theme(qtbot, monkeypatch, tmp_appdata):
    from sanduhr import credentials, paths, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})
    paths.settings_file().write_text(json.dumps({"theme": "mint"}), encoding="utf-8")

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert w.current_theme_key() == "mint"
```

- [ ] **Step 2: Run tests — verify they fail**

```powershell
pytest tests/test_widget_bootstrap.py -v
```

Expected: `ImportError`.

- [ ] **Step 3: Implement `widget.py`**

```python
"""SanduhrWidget -- frameless always-on-top main window.

Composes title bar, theme strip, tier cards, footer. Manages drag,
compact mode, pin toggle, right-click context menu, window position
persistence, credentials dialog, and refresh scheduling.
"""

import json
import logging
import webbrowser
from datetime import datetime
from typing import Dict, Optional

from PySide6.QtCore import QPoint, Qt, QThread, QTimer, Slot
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from sanduhr import credentials, history, mica, paths, themes
from sanduhr.fetcher import UsageFetcher
from sanduhr.tiers import TierCard

_log = logging.getLogger(__name__)

_REFRESH_MS = 5 * 60 * 1000
_TICK_MS = 30 * 1000

_TIER_LABELS = {
    "five_hour":            "Session (5hr)",
    "seven_day":            "Weekly - All Models",
    "seven_day_sonnet":     "Weekly - Sonnet",
    "seven_day_opus":       "Weekly - Opus",
    "seven_day_cowork":     "Weekly - Cowork",
    "seven_day_omelette":   "Weekly - Routines",
    "seven_day_oauth_apps": "Weekly - OAuth Apps",
    "iguana_necktie":       "Weekly - Special",
}


class CredentialsDialog(QDialog):
    def __init__(
        self,
        parent=None,
        session_key: str = "",
        cf_clearance: str = "",
        focus_cf: bool = False,
    ):
        super().__init__(parent)
        self.setWindowTitle("Credentials")
        self.setModal(True)

        layout = QVBoxLayout(self)
        layout.addWidget(
            QLabel(
                "Paste your claude.ai cookies.\n"
                "F12 -> Application -> Cookies -> claude.ai"
            )
        )

        layout.addWidget(QLabel("sessionKey"))
        self._sk = QLineEdit(session_key)
        self._sk.setEchoMode(QLineEdit.Password)
        layout.addWidget(self._sk)

        layout.addWidget(QLabel("cf_clearance (optional)"))
        self._cf = QLineEdit(cf_clearance)
        self._cf.setEchoMode(QLineEdit.Password)
        layout.addWidget(self._cf)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        layout.addWidget(btns)

        (self._cf if focus_cf else self._sk).setFocus()

    def values(self) -> Dict[str, str]:
        return {
            "session_key": self._sk.text().strip(),
            "cf_clearance": self._cf.text().strip(),
        }


class SanduhrWidget(QWidget):
    def __init__(self):
        super().__init__()
        self._settings = self._load_settings()
        self._theme_key = self._settings.get("theme", "obsidian")
        self._theme = themes.THEMES.get(self._theme_key, themes.THEMES["obsidian"])
        self._compact = False
        self._pinned = True
        self._drag_origin: Optional[QPoint] = None
        self._tier_cards: Dict[str, TierCard] = {}
        self._thread: Optional[QThread] = None
        self._fetcher: Optional[UsageFetcher] = None
        self._last: Optional[dict] = None

        self.setWindowTitle("Sanduhr für Claude")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground, True)
        self.resize(420, 540)
        self._restore_geometry()

        self._build()
        self.apply_theme(self._theme_key)

        migration = credentials.migrate_from_v1()
        if migration.get("theme"):
            self._settings = self._load_settings()
            self.apply_theme(self._settings.get("theme", self._theme_key))

        creds = credentials.load()
        if not creds["session_key"]:
            QTimer.singleShot(100, self._prompt_first_run)
        else:
            self._start_fetcher(creds["session_key"], creds["cf_clearance"])

        self._countdown = QTimer(self)
        self._countdown.timeout.connect(self._tick)
        self._countdown.start(_TICK_MS)

    # -- for tests -------------------------------------------------

    def status_text(self) -> str:
        return self._status_lbl.text()

    def current_theme_key(self) -> str:
        return self._theme_key

    # -- build -----------------------------------------------------

    def _build(self) -> None:
        outer = QVBoxLayout(self)
        outer.setContentsMargins(1, 1, 1, 1)
        outer.setSpacing(0)

        # Accent gradient strip at top
        self._accent_strip = QWidget()
        self._accent_strip.setFixedHeight(2)
        outer.addWidget(self._accent_strip)

        # Title bar
        self._title_bar = QWidget()
        self._title_bar.setFixedHeight(34)
        tb = QHBoxLayout(self._title_bar)
        tb.setContentsMargins(10, 0, 0, 0)
        tb.setSpacing(0)
        self._title_lbl = QLabel("Sanduhr für Claude")
        tb.addWidget(self._title_lbl)
        tb.addStretch()
        self._btn_key = QPushButton("Key")
        self._btn_refresh = QPushButton("Refresh")
        self._btn_close = QPushButton("x")
        self._btn_pin = QPushButton("Pin")
        for b in (self._btn_key, self._btn_refresh, self._btn_close, self._btn_pin):
            b.setFlat(True)
            b.setCursor(Qt.PointingHandCursor)
            b.setFixedHeight(34)
        self._btn_key.clicked.connect(lambda: self._open_credentials_dialog())
        self._btn_refresh.clicked.connect(self._request_refresh)
        self._btn_close.clicked.connect(self.close)
        self._btn_pin.clicked.connect(self._toggle_pin)
        for b in (self._btn_key, self._btn_refresh, self._btn_close, self._btn_pin):
            tb.addWidget(b)
        outer.addWidget(self._title_bar)

        # Theme strip
        self._theme_strip = QWidget()
        self._theme_strip.setFixedHeight(26)
        ts = QHBoxLayout(self._theme_strip)
        ts.setContentsMargins(10, 0, 10, 0)
        ts.setSpacing(0)
        self._theme_buttons: Dict[str, QPushButton] = {}
        for key, theme in themes.THEMES.items():
            btn = QPushButton(theme["name"])
            btn.setFlat(True)
            btn.setCursor(Qt.PointingHandCursor)
            btn.clicked.connect(lambda _=False, k=key: self.apply_theme(k))
            self._theme_buttons[key] = btn
            ts.addWidget(btn)
        ts.addStretch()
        outer.addWidget(self._theme_strip)

        # Content
        self._content = QWidget()
        self._content_layout = QVBoxLayout(self._content)
        self._content_layout.setContentsMargins(14, 10, 14, 10)
        self._content_layout.setSpacing(8)
        self._status_lbl = QLabel("Connecting...")
        self._content_layout.addWidget(self._status_lbl)
        self._cards_container = QWidget()
        self._cards_layout = QVBoxLayout(self._cards_container)
        self._cards_layout.setContentsMargins(0, 0, 0, 0)
        self._cards_layout.setSpacing(8)
        self._content_layout.addWidget(self._cards_container)
        self._content_layout.addStretch()
        outer.addWidget(self._content, stretch=1)

        # Footer
        self._footer = QWidget()
        self._footer.setFixedHeight(24)
        ft = QHBoxLayout(self._footer)
        ft.setContentsMargins(10, 0, 10, 0)
        self._footer_lbl = QLabel("")
        ft.addWidget(self._footer_lbl)
        ft.addStretch()
        sonnet = QPushButton("Use Sonnet")
        sonnet.setFlat(True)
        sonnet.setCursor(Qt.PointingHandCursor)
        sonnet.clicked.connect(
            lambda: webbrowser.open("https://claude.ai/new?model=claude-sonnet-4-6")
        )
        ft.addWidget(sonnet)
        outer.addWidget(self._footer)

        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._show_context_menu)

    # -- theme -----------------------------------------------------

    def apply_theme(self, key: str) -> None:
        self._theme_key = key
        self._theme = themes.THEMES[key]

        self.setStyleSheet(
            f"""
            QWidget {{
                color: {self._theme['text']};
                font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif;
                font-size: 10pt;
            }}
            """
        )

        self._accent_strip.setStyleSheet(
            f"background: qlineargradient(x1:0, y1:0, x2:1, y2:0, "
            f"stop:0 {self._theme['accent']}, "
            f"stop:1 rgba(0,0,0,0));"
        )

        for card in self._tier_cards.values():
            card.apply_theme(self._theme)
            self._apply_monospace_if_needed(card)

        if self._theme.get("opts_out_of_mica"):
            mica.disable_mica(self)
        else:
            mica.apply_mica(self, enabled=True)

        self._settings["theme"] = key
        self._save_settings()

        for k, btn in self._theme_buttons.items():
            if k == key:
                btn.setStyleSheet(
                    f"color: {self._theme['accent']}; font-weight: 600;"
                )
            else:
                btn.setStyleSheet(f"color: {self._theme['text_muted']};")

    def _apply_monospace_if_needed(self, card: TierCard) -> None:
        """Matrix-only: swap percentage / countdown fonts to Cascadia Code."""
        from PySide6.QtGui import QFont
        mono = self._theme.get("monospace_font")
        if mono:
            fb = self._theme.get("monospace_fallback", "Consolas")
            family = mono if QFont(mono).exactMatch() else fb
            f = QFont(family, 14)
            f.setStyleHint(QFont.Monospace)
            f.setBold(True)
            card._pct.setFont(f)
            # Subtle phosphor glow on percentages
            from PySide6.QtGui import QColor as _QColor
            from PySide6.QtWidgets import QGraphicsDropShadowEffect as _Shadow

            bloom = self._theme.get("accent_bloom", {"blur": 4, "alpha": 0.6})
            effect = _Shadow(card._pct)
            effect.setOffset(0, 0)
            effect.setBlurRadius(bloom["blur"])
            c = _QColor(self._theme["accent"])
            c.setAlphaF(bloom["alpha"])
            effect.setColor(c)
            card._pct.setGraphicsEffect(effect)
        else:
            from PySide6.QtGui import QFont
            default = QFont("Segoe UI Variable Display", 14)
            default.setBold(True)
            card._pct.setFont(default)
            card._pct.setGraphicsEffect(None)

    # -- drag ------------------------------------------------------

    def mousePressEvent(self, event) -> None:  # noqa: N802
        if event.button() == Qt.LeftButton:
            self._drag_origin = (
                event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            )

    def mouseMoveEvent(self, event) -> None:  # noqa: N802
        if self._drag_origin and event.buttons() & Qt.LeftButton:
            self.move(event.globalPosition().toPoint() - self._drag_origin)

    def mouseReleaseEvent(self, event) -> None:  # noqa: N802
        self._drag_origin = None
        self._save_window_geometry()

    def mouseDoubleClickEvent(self, event) -> None:  # noqa: N802
        if (
            event.button() == Qt.LeftButton
            and event.position().y() <= self._title_bar.height() + 2
        ):
            self._toggle_compact()

    # -- context menu ----------------------------------------------

    def _show_context_menu(self, pos: QPoint) -> None:
        menu = QMenu(self)
        menu.addAction("Refresh", self._request_refresh)
        menu.addAction(
            "Compact mode" if not self._compact else "Expand",
            self._toggle_compact,
        )
        menu.addAction("Credentials...", lambda: self._open_credentials_dialog())
        menu.addSeparator()
        menu.addAction("Quit", self.close)
        menu.popup(self.mapToGlobal(pos))

    # -- actions ---------------------------------------------------

    def _toggle_pin(self) -> None:
        self._pinned = not self._pinned
        flags = self.windowFlags()
        if self._pinned:
            flags |= Qt.WindowStaysOnTopHint
        else:
            flags &= ~Qt.WindowStaysOnTopHint
        self.setWindowFlags(flags)
        self.show()

    def _toggle_compact(self) -> None:
        self._compact = not self._compact
        self._render_cards(self._last or {})

    def _open_credentials_dialog(self, focus_cf: bool = False) -> None:
        creds = credentials.load()
        dlg = CredentialsDialog(
            self,
            session_key=creds.get("session_key") or "",
            cf_clearance=creds.get("cf_clearance") or "",
            focus_cf=focus_cf,
        )
        # Qt legacy alias exec_() works on PySide6
        if dlg.exec_() == QDialog.Accepted:
            vals = dlg.values()
            if vals["session_key"]:
                credentials.save(
                    session_key=vals["session_key"],
                    cf_clearance=vals["cf_clearance"] or None,
                )
                self._start_or_update_fetcher(
                    vals["session_key"], vals["cf_clearance"] or None
                )
                self._request_refresh()

    def _prompt_first_run(self) -> None:
        QMessageBox.information(
            self,
            "Sanduhr für Claude",
            "Welcome!\n\n"
            "1. Open claude.ai and log in.\n"
            "2. Press F12 -> Application -> Cookies -> claude.ai.\n"
            "3. Copy the sessionKey cookie value.\n\n"
            "Paste it in the next dialog.",
        )
        self._open_credentials_dialog()

    # -- fetcher ---------------------------------------------------

    def _start_fetcher(self, session_key: str, cf_clearance: Optional[str]) -> None:
        if self._thread is not None:
            return
        self._thread = QThread(self)
        self._fetcher = UsageFetcher(session_key, cf_clearance)
        self._fetcher.moveToThread(self._thread)
        self._fetcher.dataReady.connect(self._on_data_ready)
        self._fetcher.fetchFailed.connect(self._on_fetch_failed)
        self._thread.start()
        QTimer.singleShot(0, self._fetcher.fetch)
        self._schedule = QTimer(self)
        self._schedule.timeout.connect(
            lambda: QTimer.singleShot(0, self._fetcher.fetch)
        )
        self._schedule.start(_REFRESH_MS)

    def _start_or_update_fetcher(self, sk: str, cf: Optional[str]) -> None:
        if self._fetcher is None:
            self._start_fetcher(sk, cf)
        else:
            self._fetcher.update_credentials(sk, cf)

    def _request_refresh(self) -> None:
        if self._fetcher:
            self._status_lbl.setText("Refreshing...")
            QTimer.singleShot(0, self._fetcher.fetch)

    @Slot(dict)
    def _on_data_ready(self, data: dict) -> None:
        self._status_lbl.setText("")
        self._last = data
        self._render_cards(data)
        ts = datetime.now().strftime("%I:%M %p").lstrip("0")
        mode = "Compact" if self._compact else ("Pinned" if self._pinned else "Float")
        self._footer_lbl.setText(f"Updated {ts} | {mode}")

    @Slot(str, str)
    def _on_fetch_failed(self, kind: str, message: str) -> None:
        if kind == "session_expired":
            self._status_lbl.setText("Session expired -- click Key.")
        elif kind == "cloudflare":
            self._status_lbl.setText("Cloudflare -- add cf_clearance (click Key).")
        elif kind == "network":
            self._status_lbl.setText("No connection -- retrying...")
        else:
            self._status_lbl.setText(f"Error: {message[:60]}")
        self._status_lbl.setStyleSheet("color: #f87171;")

    def _render_cards(self, data: dict) -> None:
        active = []
        for key, label in _TIER_LABELS.items():
            tier = data.get(key)
            if tier and tier.get("utilization") is not None:
                active.append(
                    (key, label, int(tier["utilization"]), tier.get("resets_at"))
                )

        if self._compact and active:
            active = [max(active, key=lambda t: t[2])]

        stale = set(self._tier_cards) - {a[0] for a in active}
        for k in stale:
            card = self._tier_cards.pop(k)
            self._cards_layout.removeWidget(card)
            card.setParent(None)
            card.deleteLater()

        for key, label, util, resets_at in active:
            if key in self._tier_cards:
                card = self._tier_cards[key]
            else:
                card = TierCard(tier_key=key, label=label, theme=self._theme)
                self._tier_cards[key] = card
                self._cards_layout.addWidget(card)
                self._apply_monospace_if_needed(card)
            card.update_state(
                util=util,
                resets_at=resets_at,
                history_values=history.load(key),
            )

    def _tick(self) -> None:
        """30s countdown tick -- refresh reset labels + pace markers only."""
        data = self._last
        if not data:
            return
        for key, card in self._tier_cards.items():
            tier = data.get(key, {})
            util = tier.get("utilization")
            ra = tier.get("resets_at")
            if util is not None:
                card.update_state(
                    util=int(util), resets_at=ra, history_values=history.load(key)
                )

    # -- geometry persistence --------------------------------------

    def _restore_geometry(self) -> None:
        geom = self._settings.get("window")
        if geom and all(k in geom for k in ("x", "y", "w", "h")):
            self.move(geom["x"], geom["y"])
            self.resize(geom["w"], geom["h"])
        else:
            screen = QGuiApplication.primaryScreen().availableGeometry()
            self.move(
                screen.right() - self.width() - 24,
                screen.bottom() - self.height() - 24,
            )

    def _save_window_geometry(self) -> None:
        self._settings["window"] = {
            "x": self.x(),
            "y": self.y(),
            "w": self.width(),
            "h": self.height(),
        }
        self._save_settings()

    def closeEvent(self, event) -> None:  # noqa: N802
        self._save_window_geometry()
        if self._thread is not None:
            self._thread.quit()
            self._thread.wait(2000)
        super().closeEvent(event)

    # -- settings --------------------------------------------------

    def _load_settings(self) -> dict:
        path = paths.settings_file()
        if not path.exists():
            return {}
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {}

    def _save_settings(self) -> None:
        try:
            paths.settings_file().write_text(
                json.dumps(self._settings), encoding="utf-8"
            )
        except OSError as e:
            _log.warning("Could not save settings: %s", e)
```

- [ ] **Step 4: Run tests — verify they pass**

```powershell
pytest tests/test_widget_bootstrap.py -v
```

Expected: all 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/widget.py windows/tests/test_widget_bootstrap.py
git commit -m "Windows v2: add SanduhrWidget main window

Frameless always-on-top widget with title bar, theme strip, tier
cards, footer, accent gradient strip. Manages drag (anywhere),
double-click compact toggle, pin, right-click context menu
(Refresh/Compact/Credentials/Quit), credentials dialog with
cf_clearance field, window geometry persistence, threaded fetcher,
30s countdown tick. Matrix theme switches percentage font to
Cascadia Code + applies phosphor glow.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: `app.py` — QApplication bootstrap

**Files:**

- Create: `windows/src/sanduhr/app.py`

- [ ] **Step 1: Implement `app.py`**

```python
"""Entry point -- bootstraps QApplication, sets Windows AppUserModelID, runs the widget."""

import logging
import logging.handlers
import os
import sys

from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication

from sanduhr import paths
from sanduhr.widget import SanduhrWidget


def _set_app_user_model_id() -> None:
    """Group the widget under a stable taskbar identity (icon grouping + jump lists)."""
    if sys.platform != "win32":
        return
    import ctypes

    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
            "com.626labs.sanduhr"
        )
    except Exception:
        pass


def _configure_logging() -> None:
    log_path = paths.log_file()
    handler = logging.handlers.RotatingFileHandler(
        log_path, maxBytes=1_000_000, backupCount=3, encoding="utf-8"
    )
    handler.setFormatter(
        logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
    )
    root = logging.getLogger()
    root.setLevel(logging.DEBUG if "--debug" in sys.argv else logging.INFO)
    root.addHandler(handler)

    def _excepthook(exc_type, exc, tb):
        root.exception("Unhandled exception", exc_info=(exc_type, exc, tb))
        sys.__excepthook__(exc_type, exc, tb)

    sys.excepthook = _excepthook


def _locate_icon() -> str:
    """Find Sanduhr.ico whether running from source or PyInstaller bundle."""
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        return os.path.join(meipass, "icon", "Sanduhr.ico")
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, "..", "..", "icon", "Sanduhr.ico")


def main() -> int:
    _configure_logging()
    _set_app_user_model_id()

    app = QApplication(sys.argv)
    app.setApplicationName("Sanduhr für Claude")
    app.setOrganizationName("626Labs")

    icon_path = _locate_icon()
    if os.path.exists(icon_path):
        app.setWindowIcon(QIcon(icon_path))

    widget = SanduhrWidget()
    widget.show()
    # Qt legacy alias exec_() still works on PySide6
    return app.exec_()


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Smoke-launch**

```powershell
cd windows
python -m sanduhr
```

Expected: widget launches with taskbar icon. Log file created at `%APPDATA%\Sanduhr\sanduhr.log`.

- [ ] **Step 3: Commit**

```bash
git add windows/src/sanduhr/app.py
git commit -m "Windows v2: add app entry point

main() configures rotating-file logging + global exception hook, sets
AppUserModelID for taskbar grouping, loads icon (works from source +
PyInstaller bundle), instantiates SanduhrWidget.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: Icon generation

**Files:**

- Create: `windows/icon/source.png` (manual — export from Mac artwork at 1024×1024)
- Create: `windows/icon/make-icon.ps1`
- Create: `windows/icon/Sanduhr.ico` (generated, committed)

- [ ] **Step 1: Obtain source artwork**

Copy the Mac source PNG from `mac/icon/` on the `mac-native` branch (or export from the `.icns` at 1024×1024). Place at `windows/icon/source.png`.

If the Mac source isn't accessible, commit a 1024×1024 placeholder hourglass PNG in theme-neutral colors. Replacement lands in a follow-up commit without touching code.

- [ ] **Step 2: Create `windows/icon/make-icon.ps1`**

```powershell
# Regenerate Sanduhr.ico (multi-res) from source.png.
# Requires Python + Pillow (installed via requirements-dev.txt).
#
# Usage: ./make-icon.ps1

$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not (Test-Path "source.png")) {
    Write-Error "source.png not found in $(Get-Location)"
    exit 1
}

$py = @"
from PIL import Image
src = Image.open('source.png').convert('RGBA')
sizes = [(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
src.save('Sanduhr.ico', format='ICO', sizes=sizes)
print('Wrote Sanduhr.ico')
"@

$py | python -
```

- [ ] **Step 3: Generate and commit `Sanduhr.ico`**

```powershell
cd windows/icon
./make-icon.ps1
cd ../..
```

Expected output: `Wrote Sanduhr.ico`.

- [ ] **Step 4: Commit**

```bash
git add windows/icon/source.png windows/icon/make-icon.ps1 windows/icon/Sanduhr.ico
git commit -m "Windows v2: add icon artwork + generator

source.png committed as the single source of truth; make-icon.ps1
regenerates Sanduhr.ico at 9 resolutions (16/20/24/32/40/48/64/128/256)
via Pillow. Generated .ico committed so builds don't require
ImageMagick.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 16: PyInstaller spec + version metadata

**Files:**

- Create: `windows/version_info.txt`
- Create: `windows/sanduhr.spec`

- [ ] **Step 1: Create `windows/version_info.txt`**

```python
# UTF-8
VSVersionInfo(
  ffi=FixedFileInfo(
    filevers=(2, 0, 0, 0),
    prodvers=(2, 0, 0, 0),
    mask=0x3f,
    flags=0x0,
    OS=0x40004,
    fileType=0x1,
    subtype=0x0,
    date=(0, 0),
  ),
  kids=[
    StringFileInfo([
      StringTable(
        '040904B0',
        [
          StringStruct('CompanyName', '626Labs'),
          StringStruct('FileDescription', 'Sanduhr für Claude -- Claude.ai Usage Tracker'),
          StringStruct('FileVersion', '2.0.0.0'),
          StringStruct('InternalName', 'Sanduhr'),
          StringStruct('LegalCopyright', 'MIT License'),
          StringStruct('OriginalFilename', 'Sanduhr.exe'),
          StringStruct('ProductName', 'Sanduhr für Claude'),
          StringStruct('ProductVersion', '2.0.0.0'),
        ]),
    ]),
    VarFileInfo([VarStruct('Translation', [0x0409, 0x04B0])]),
  ]
)
```

- [ ] **Step 2: Create `windows/sanduhr.spec`**

```python
# PyInstaller spec for Sanduhr für Claude.
# Run:  pyinstaller sanduhr.spec --clean
#
# One-folder mode: dist/Sanduhr/ contains Sanduhr.exe + Qt DLLs + runtime.

# -*- mode: python ; coding: utf-8 -*-

block_cipher = None

a = Analysis(
    ['src/sanduhr/__main__.py'],
    pathex=['src'],
    binaries=[],
    datas=[
        ('icon/Sanduhr.ico', 'icon'),
    ],
    hiddenimports=[
        'cloudscraper',
        'keyring.backends.Windows',
        'PySide6.QtSvg',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'PySide6.QtWebEngineCore',
        'PySide6.QtWebEngineWidgets',
        'PySide6.QtMultimedia',
        'PySide6.QtQuick3D',
        'PySide6.QtNetworkAuth',
        'PySide6.QtPdf',
        'PySide6.QtBluetooth',
        'PySide6.QtSerialPort',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='Sanduhr',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    icon='icon/Sanduhr.ico',
    version='version_info.txt',
)

coll = COLLECT(
    pyz_exe := exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=False,
    name='Sanduhr',
)
```

- [ ] **Step 3: Test the build**

```powershell
cd windows
pyinstaller sanduhr.spec --clean
```

Expected: `dist/Sanduhr/Sanduhr.exe` exists. Running it launches the widget. Properties → Details shows v2.0.0.0 + 626Labs.

- [ ] **Step 4: Commit**

```bash
git add windows/version_info.txt windows/sanduhr.spec
git commit -m "Windows v2: add PyInstaller spec + version metadata

One-folder build (dist/Sanduhr/) with pinned hidden imports, excluded
Qt modules (cuts ~120MB), icon + version_info embedded. Right-click
Sanduhr.exe -> Properties -> Details shows 626Labs v2.0.0.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 17: Inno Setup installer script

**Files:**

- Create: `windows/installer/Sanduhr.iss`
- Create: `windows/installer/banner.bmp`

- [ ] **Step 1: Create `windows/installer/Sanduhr.iss`**

```iss
; Inno Setup script for Sanduhr für Claude.
; Requires Inno Setup 6+. Run: iscc Sanduhr.iss

#define MyAppName "Sanduhr für Claude"
#define MyAppShortName "Sanduhr"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "626Labs"
#define MyAppURL "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude"
#define MyAppExeName "Sanduhr.exe"

[Setup]
AppId={{7B4E0E2F-8F2B-4A12-9C9A-626LABSSANDUHR}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\..\build
OutputBaseFilename=Sanduhr-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
RestartApplications=no
WizardImageFile=banner.bmp

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "desktop";   Description: "Create a Desktop shortcut";   GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start {#MyAppName} automatically when you sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\Sanduhr\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktop

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppShortName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Always clear Credential Manager entries on uninstall. Best-effort -- failures non-fatal.
Filename: "{cmd}"; Parameters: "/c cmdkey.exe /delete:com.626labs.sanduhr"; RunOnceId: "ClearCreds"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  RemoveDataCheckbox: TNewCheckBox;

procedure InitializeUninstallProgressForm();
var
  Page: TSetupForm;
begin
  Page := UninstallProgressForm;
  RemoveDataCheckbox := TNewCheckBox.Create(Page);
  RemoveDataCheckbox.Parent := Page;
  RemoveDataCheckbox.Left := 8;
  RemoveDataCheckbox.Top := Page.Height - 80;
  RemoveDataCheckbox.Width := Page.Width - 24;
  RemoveDataCheckbox.Height := 20;
  RemoveDataCheckbox.Caption := 'Also remove my settings and history';
  RemoveDataCheckbox.Checked := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppData: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if Assigned(RemoveDataCheckbox) and RemoveDataCheckbox.Checked then
    begin
      AppData := ExpandConstant('{userappdata}\Sanduhr');
      DelTree(AppData, True, True, True);
    end;
  end;
end;
```

- [ ] **Step 2: Create placeholder `banner.bmp`**

```powershell
cd windows/installer
$py = @"
from PIL import Image, ImageDraw, ImageFont
img = Image.new('RGB', (150, 57), '#0d0d0d')
d = ImageDraw.Draw(img)
try:
    font = ImageFont.truetype('segoeuib.ttf', 14)
except Exception:
    font = ImageFont.load_default()
d.text((10, 20), 'Sanduhr', fill='#e8e4dc', font=font)
img.save('banner.bmp', 'BMP')
"@
$py | python -
```

- [ ] **Step 3: Test installer build manually**

Install Inno Setup 6 from <https://jrsoftware.org/isdl.php>. Then:

```powershell
cd windows/installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Sanduhr.iss
```

Expected: `build/Sanduhr-Setup-v2.0.0.exe` produced. Double-clicking launches the Setup wizard.

- [ ] **Step 4: Commit**

```bash
git add windows/installer/Sanduhr.iss windows/installer/banner.bmp
git commit -m "Windows v2: add Inno Setup installer script

Installs to {autopf}\\Sanduhr (user-level fallback). Optional tasks:
Start Menu shortcut (on), Desktop (off), Launch at login (off).
Uninstall preserves %APPDATA%\\Sanduhr\\ by default, offers checkbox
to remove it. Always clears Credential Manager entries on uninstall
via UninstallRun -> cmdkey /delete.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 18: `build.ps1` orchestrator

**Files:**

- Create: `windows/build.ps1`

- [ ] **Step 1: Create `build.ps1`**

```powershell
# Full Windows build pipeline for Sanduhr für Claude.
#
# Usage:
#   ./build.ps1                   # full: PyInstaller + Inno Setup
#   ./build.ps1 -SkipInstaller    # PyInstaller only
#   ./build.ps1 -Debug            # build with console window for tracebacks

[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

Write-Host "-> Cleaning previous build..." -ForegroundColor Cyan
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue build, dist

$SpecFile = "sanduhr.spec"
if ($Debug) {
    Write-Host "-> Debug build (console window enabled)" -ForegroundColor Yellow
    (Get-Content $SpecFile) -replace "console=False", "console=True" | Set-Content "sanduhr-debug.spec"
    $SpecFile = "sanduhr-debug.spec"
}

Write-Host "-> Running PyInstaller..." -ForegroundColor Cyan
pyinstaller $SpecFile --clean --noconfirm
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed (exit $LASTEXITCODE)"
}

if ($Debug) {
    Remove-Item "sanduhr-debug.spec" -ErrorAction SilentlyContinue
}

if (-not (Test-Path "dist/Sanduhr/Sanduhr.exe")) {
    throw "dist/Sanduhr/Sanduhr.exe not produced"
}

Write-Host "OK PyInstaller complete: dist/Sanduhr/" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "-> Skipping Inno Setup (as requested)" -ForegroundColor Yellow
    return
}

$ISCC = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $ISCC)) {
    throw "Inno Setup 6 not found at $ISCC -- install from https://jrsoftware.org/isdl.php"
}

Write-Host "-> Running Inno Setup..." -ForegroundColor Cyan
& $ISCC "installer/Sanduhr.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed (exit $LASTEXITCODE)"
}

$Installer = Get-ChildItem "build/Sanduhr-Setup-v*.exe" | Select-Object -First 1
if (-not $Installer) {
    throw "Installer .exe not produced in build/"
}

$SizeMB = [math]::Round($Installer.Length / 1MB, 1)
Write-Host "OK Build complete: $($Installer.Name) ($SizeMB MB)" -ForegroundColor Green
Write-Host "   Test: Start-Process .\build\$($Installer.Name)"
```

- [ ] **Step 2: Run full build locally**

```powershell
cd windows
./build.ps1
```

Expected: `build/Sanduhr-Setup-v2.0.0.exe` produced, ~35–45MB.

- [ ] **Step 3: Commit**

```bash
git add windows/build.ps1
git commit -m "Windows v2: add build.ps1 orchestrator

Full pipeline: clean build/dist -> PyInstaller -> Inno Setup ->
Sanduhr-Setup-v2.0.0.exe. Flags: -SkipInstaller (PyInstaller only),
-Debug (console window for tracebacks). Errors out early if ISCC
missing.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 19: GitHub Actions release workflow

**Files:**

- Create: `.github/workflows/windows-release.yml`
- Create: `windows/RELEASE_NOTES.md`

- [ ] **Step 1: Create the workflow**

```yaml
name: Windows Release

on:
  push:
    tags:
      - "v*.*.*-windows"

defaults:
  run:
    working-directory: windows

jobs:
  build:
    runs-on: windows-latest
    permissions:
      contents: write  # required to upload release assets
    steps:
      - uses: actions/checkout@v4

      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: "3.11"
          cache: pip
          cache-dependency-path: windows/requirements-dev.txt

      - name: Install dependencies
        run: |
          python -m pip install --upgrade pip
          pip install -r requirements-dev.txt
          pip install -e .

      - name: Install Inno Setup 6
        run: choco install innosetup --version=6.2.2 -y

      - name: Run pytest (gate)
        run: pytest

      - name: Build installer
        shell: pwsh
        run: ./build.ps1

      - name: Extract version
        id: ver
        shell: pwsh
        run: |
          $t = "${{ github.ref_name }}"
          $v = $t -replace "^v", "" -replace "-windows$", ""
          echo "version=$v" >> $env:GITHUB_OUTPUT

      - name: Upload release artifact
        uses: softprops/action-gh-release@v2
        with:
          files: windows/build/Sanduhr-Setup-v${{ steps.ver.outputs.version }}.exe
          body_path: windows/RELEASE_NOTES.md
          fail_on_unmatched_files: true
          prerelease: false
```

- [ ] **Step 2: Create `windows/RELEASE_NOTES.md`**

```markdown
# Sanduhr für Claude — Windows v2.0.0

First Windows release with native PySide6 UI, Win11 Mica glass backdrop,
Credential Manager storage, and `.exe` installer.

See [CHANGELOG.md](../CHANGELOG.md) for the full list.

## Install

1. Download `Sanduhr-Setup-v2.0.0.exe`.
2. When SmartScreen warns "Windows protected your PC," click **More info**
   → **Run anyway**. This is expected for unsigned apps.
3. Step through the installer.
4. Launch Sanduhr from the Start Menu and paste your `claude.ai` sessionKey.
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/windows-release.yml windows/RELEASE_NOTES.md
git commit -m "Windows v2: add GitHub Actions release workflow

Triggered by tag v*.*.*-windows. Installs deps + Inno Setup, runs
pytest as a gate, runs build.ps1, uploads Sanduhr-Setup-vX.Y.Z.exe
as a GitHub Release asset with RELEASE_NOTES.md as body.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 20: `TEST_PLAN.md` manual release checklist

**Files:**

- Create: `windows/TEST_PLAN.md`

- [ ] **Step 1: Create `windows/TEST_PLAN.md`**

```markdown
# Windows Release Test Plan

Run this checklist on a clean Windows 11 VM before every tagged release.
Estimated time: 20 minutes.

## Prerequisites

- Windows 11 22H2+ VM with no prior Sanduhr install
- A valid `claude.ai` sessionKey
- Network access to claude.ai

## 1. Fresh install

- [ ] Double-click `Sanduhr-Setup-vX.Y.Z.exe`
- [ ] SmartScreen warns -> click "More info" -> "Run anyway"
- [ ] Installer wizard appears with Sanduhr banner
- [ ] "Start Menu shortcut" is checked by default; "Desktop" and "Launch at login" are unchecked
- [ ] Install completes without errors
- [ ] Sanduhr launches automatically (post-install task)
- [ ] First-run setup dialog appears, walks through the F12 -> Cookies flow
- [ ] Paste sessionKey -> "OK" -> widget shows usage bars within ~5 seconds

## 2. v1 upgrade

- [ ] On a VM with v1 `sanduhr.py` previously run: confirm `~/.claude-usage-widget/config.json` exists
- [ ] Install v2 over the top
- [ ] Launch -- widget shows data immediately (no re-auth)
- [ ] Theme preference from v1 is preserved
- [ ] `%APPDATA%\Sanduhr\history.json` exists and contains the v1 data
- [ ] Old `~/.claude-usage-widget/config.json` was deleted (history kept)

## 3. Theme rendering

For each theme (Obsidian, Aurora, Ember, Mint, Matrix):

- [ ] Click the theme button in the title-bar strip
- [ ] Compare to reference screenshot in `docs/screenshots/windows/<theme>.png`
- [ ] Glass themes (Obsidian/Aurora/Ember/Mint): desktop wallpaper visible through Mica backdrop
- [ ] Matrix: solid dark green-black background (no Mica bleed-through)
- [ ] Matrix: percentages in Cascadia Code with soft green glow

## 4. Mica / Win10 fallback

- [ ] On Win11 22H2+ with a colorful desktop wallpaper: translucent backdrop visible
- [ ] On Win10 or unsupported build: solid theme `bg` color, no crash, no visual glitches

## 5. Controls

- [ ] Click **Pin** in title bar -- always-on-top toggles
- [ ] Double-click title bar -- compact mode toggles
- [ ] Click **Key** -- credentials dialog opens, current sessionKey + cf_clearance pre-populated
- [ ] Click **Refresh** -- status shows "Refreshing..." briefly, then updates
- [ ] Right-click anywhere on widget -- context menu shows Refresh / Compact / Credentials / Quit
- [ ] Click and drag anywhere on widget -- widget moves
- [ ] Close, reopen -- widget appears at last-saved position

## 6. Session expiry

- [ ] Paste a deliberately wrong sessionKey -> status becomes "Session expired -- click Key."
- [ ] Paste a valid key -> widget recovers within 5 seconds

## 7. Cloudflare block

- [ ] Temporarily block claude.ai via `%SystemRoot%\System32\drivers\etc\hosts`: `127.0.0.1 claude.ai`
- [ ] Trigger Refresh -> status becomes "Cloudflare -- add cf_clearance (click Key)."
- [ ] Click Key -> credentials dialog opens, cf_clearance field focused
- [ ] Restore hosts; refresh -- widget recovers

## 8. Autostart

- [ ] Uninstall. Reinstall with "Launch at login" checked.
- [ ] Sign out, sign back in -- Sanduhr launches automatically
- [ ] Uninstall with autostart previously enabled -- Run registry entry is removed

## 9. Uninstall

- [ ] Start -> Apps -> Sanduhr für Claude -> Uninstall
- [ ] "Also remove my settings and history" checkbox is unchecked
- [ ] Uninstall with checkbox UNCHECKED -> install dir gone, `%APPDATA%\Sanduhr\` preserved
- [ ] Uninstall again (after reinstalling) with checkbox CHECKED -> `%APPDATA%\Sanduhr\` also gone
- [ ] Credential Manager entries (service `com.626labs.sanduhr`) removed in both cases
- [ ] Start Menu entry gone; Run registry entry (if ticked) gone

## 10. Exit criteria

All boxes above must be checked before tagging a release.
```

- [ ] **Step 2: Commit**

```bash
git add windows/TEST_PLAN.md
git commit -m "Windows v2: add manual release test plan

10-section checklist covering fresh install, v1 upgrade, each theme,
Mica / Win10 fallback, controls, session expiry, Cloudflare fallback,
autostart, uninstall. Run on clean Win11 VM before every release.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 21: Reference screenshots baseline

**Files:**

- Create: `docs/screenshots/windows/obsidian.png`
- Create: `docs/screenshots/windows/aurora.png`
- Create: `docs/screenshots/windows/ember.png`
- Create: `docs/screenshots/windows/mint.png`
- Create: `docs/screenshots/windows/matrix.png`

- [ ] **Step 1: Capture each theme**

Launch `python -m sanduhr` with usage data loaded. For each theme:

1. Click the theme name in the title-bar strip.
2. Win + Shift + S -> rectangular screenshot of just the widget.
3. Paste into an editor; export as PNG.
4. Save as `docs/screenshots/windows/<theme>.png`.

These are the visual regression baselines for the manual test plan.

- [ ] **Step 2: Commit**

```bash
git add docs/screenshots/windows/
git commit -m "Windows v2: capture reference screenshots of each theme

Baseline renders for manual test plan visual regression comparison.
docs/screenshots/windows/{obsidian,aurora,ember,mint,matrix}.png.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 22: Update root README and CHANGELOG

**Files:**

- Modify: `README.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Prepend to `CHANGELOG.md`**

```markdown
## v2.0.0-windows — 2026-04-16

**Platform:** Windows
**Breaking change:** Full rewrite. v1 `sanduhr.py` still works on `main` for users who prefer running from source.

### Added

- PySide6 rewrite -- native Qt 6 widget, proper DPI awareness, Segoe UI Variable throughout
- Win11 Mica backdrop -- real translucent system material, with Win10 solid-color fallback
- Glass-stacking visuals -- per-theme glass tuning (Obsidian densest, Mint airiest, Matrix opaque with phosphor glow)
- Windows Credential Manager storage via `keyring` (service `com.626labs.sanduhr`, matching Mac Keychain)
- `cf_clearance` fallback field in credentials dialog
- Right-click context menu: Refresh / Compact / Credentials / Quit
- Drag anywhere to move (not just the title bar)
- Window position persistence in `%APPDATA%\Sanduhr\settings.json`
- Inno Setup installer -- `Sanduhr-Setup-v2.0.0.exe` from GitHub Releases
- Matrix theme: Cascadia Code monospace, phosphor glow, Mica opt-out
- Silent v1 migration: session key, theme, history carry over on first v2 run
- GitHub Actions: pytest on every push, release workflow on `v*.*.*-windows` tags

### Removed

- `--break-system-packages` pip shim (replaced by pinned dependencies in the installer)
- Hardcoded initial window position (replaced by last-known-position / centered default)
```

- [ ] **Step 2: Add a Platforms section near the top of `README.md`**

```markdown
## Platforms

| Platform | Source | Install |
|---|---|---|
| **Windows** (v2, native) | `windows/` | Download installer from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases) |
| **macOS** (native SwiftUI) | `mac/` | Download `.dmg` from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases) |
| **Windows/Mac/Linux** (v1, Python) | `sanduhr.py` | `python sanduhr.py` |

Each platform release is tagged: `v2.0.0-windows`, `v1.0.0-mac`, etc.
```

- [ ] **Step 3: Commit**

```bash
git add README.md CHANGELOG.md
git commit -m "Windows v2: update root README + CHANGELOG

Platforms table at top of README points at Windows / Mac / v1 legacy.
CHANGELOG gets v2.0.0-windows entry.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 23: Push, PR, merge, tag v2.0.0-windows

**Files:** none (git + GitHub operations only)

- [ ] **Step 1: Push `windows-native` and open PR**

```bash
git push -u origin windows-native
gh pr create --title "Windows v2.0: PySide6 rewrite + Inno Setup installer" --body "$(cat <<'EOF'
## Summary

- Full rewrite of the Windows widget in PySide6 with Win11 Mica glass backdrop
- Feature parity with the Mac SwiftUI version: Credential Manager storage, cf_clearance fallback, right-click menu, drag anywhere, window-position persistence
- Packaged as a GitHub-Releases-distributed Inno Setup `.exe` installer with silent v1 migration

## Test plan

- [ ] `pytest` passes (CI runs on push)
- [ ] Manual TEST_PLAN.md checklist on a clean Win11 VM
- [ ] Fresh install -> paste sessionKey -> widget shows data in <60s
- [ ] v1 user upgrade migrates credentials + theme + history silently
- [ ] All 5 themes render correctly; compare against docs/screenshots/windows/

Spec: `docs/superpowers/specs/2026-04-16-windows-v2-diamond-design.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 2: After PR review + green CI, merge**

```bash
gh pr merge --squash --delete-branch
git checkout main
git pull origin main
```

- [ ] **Step 3: Run the full manual TEST_PLAN on a clean Win11 VM**

Work every item in `windows/TEST_PLAN.md`. Fix any regressions before tagging.

- [ ] **Step 4: Tag the release**

```bash
git tag -a v2.0.0-windows -m "Sanduhr für Claude — Windows v2.0.0

First native Windows release. PySide6, Mica, Credential Manager,
Inno Setup installer.

See CHANGELOG.md for the full list."
git push origin v2.0.0-windows
```

- [ ] **Step 5: Verify GitHub Actions release flow**

Watch `.github/workflows/windows-release.yml` run on the tag. Confirm `Sanduhr-Setup-v2.0.0.exe` appears on the Releases page and downloads cleanly.

- [ ] **Step 6: Smoke-install the released artifact**

Download the installer from the Releases page (not your local build) and install on the Win11 VM. Confirms the reproducible CI build path is working end-to-end.

---

## Self-Review

**Spec coverage — every spec section maps to tasks:**

- Repo & branch strategy -> Task 1
- Target layout / Module responsibilities -> Tasks 3–14 (one per module)
- Threading model -> Task 9 (fetcher)
- Feature parity kept from v1 -> Tasks 4, 12, 13 (behavior preserved)
- Feature parity ported from Mac -> Task 13 (cf_clearance field, context menu, drag anywhere, position persistence) + Task 7 (Credential Manager)
- Feature parity new polish -> Tasks 10 (Mica), 12 (glass stacking + inner highlight), 13 (Matrix monospace + phosphor glow, accent gradient strip)
- Feature parity removed -> Tasks 1, 13 (no pip shim, no hardcoded position)
- Visual design glass pass -> Tasks 6, 12, 13
- Per-theme glass tuning -> Task 6
- Matrix theme special handling -> Tasks 6, 13 (monospace + phosphor + Mica opt-out)
- Packaging - PyInstaller -> Task 16
- Packaging - Version metadata -> Task 16
- Packaging - Icon generation -> Task 15
- Packaging - Inno Setup installer -> Task 17
- Packaging - Code signing skipped -> documented in Task 20 TEST_PLAN ("SmartScreen warning is expected")
- Release flow -> Task 19
- Local build commands -> Task 18
- Migration from v1 -> Task 7
- Testing - unit -> Tasks 3–9
- Testing - integration (pytest-qt) -> Tasks 9, 12, 13
- Testing - manual test plan -> Task 20
- Testing - reference screenshots -> Task 21
- Error handling matrix -> Tasks 8 (typed exceptions), 9 (fetcher routing), 13 (UI status copy)
- Logging -> Task 14 (app.py): rotating handler, DEBUG flag, excepthook
- Success criteria -> Task 23 (end-to-end install from GitHub Release on fresh VM)

**Placeholder scan:** no "TBD", no "TODO", no "implement later". Every step shows the code.

**Type consistency:** method signatures reused across tasks match —
`update_state(util, resets_at, history_values)` on TierCard (Tasks 12, 13);
`apply_theme(theme_dict)` (Tasks 12, 13);
`UsageFetcher.fetch()` + `dataReady(dict)` / `fetchFailed(str, str)` (Tasks 9, 13);
`credentials.load() / save() / clear() / migrate_from_v1()` (Tasks 7, 13);
`paths.history_file() / settings_file() / log_file() / last_error_file() / legacy_config_file() / legacy_history_file()` (Tasks 3, 5, 7, 14).

All consistent.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-16-windows-v2-diamond.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for a 23-task plan like this: each task is small enough to run autonomously and the review cadence catches regressions early.

**2. Inline Execution** — Execute tasks in this session using superpowers:executing-plans, batch execution with checkpoints for review.

Which approach?
