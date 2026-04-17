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
./build.ps1 -DebugBuild       # keeps console window for tracebacks
```

Produces `build/Sanduhr-Setup-v2.0.0.exe`.

## First run

1. Launch Sanduhr.
2. Paste your `sessionKey` from `claude.ai` DevTools → Application → Cookies.
3. Credentials land in Windows Credential Manager (service `com.626labs.sanduhr`).

See `../docs/superpowers/specs/2026-04-16-windows-v2-diamond-design.md` for the full design.
