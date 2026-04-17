# Regenerate source.png + Sanduhr.ico from make-icon.py.
# Requires the windows/.venv with Pillow installed.
#
# Usage (from repo root OR windows/icon/):
#   ./make-icon.ps1

$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

$python = if (Test-Path "../.venv/Scripts/python.exe") { "../.venv/Scripts/python.exe" } else { "python" }
& $python make-icon.py
