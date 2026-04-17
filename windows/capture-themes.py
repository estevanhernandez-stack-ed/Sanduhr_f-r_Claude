"""Capture one screenshot per theme into docs/screenshots/windows/.

For each built-in theme, writes the theme into %APPDATA%\\Sanduhr\\settings.json,
launches the widget, waits for it to render, grabs the window region via
Win32 BitBlt (captures Mica + translucent glass correctly), writes the PNG,
kills the widget, moves on.

Usage:
    .venv\\Scripts\\python.exe capture-themes.py
"""

import ctypes
import ctypes.wintypes
import json
import os
import pathlib
import subprocess
import sys
import time

import win32con
import win32gui
import win32ui
from PIL import Image

THEMES = ["obsidian", "aurora", "ember", "mint", "matrix"]
TITLE = "Sanduhr für Claude"

REPO = pathlib.Path(__file__).resolve().parent.parent
SHOTS_DIR = REPO / "docs" / "screenshots" / "windows"
SHOTS_DIR.mkdir(parents=True, exist_ok=True)

SETTINGS = pathlib.Path(os.environ["APPDATA"]) / "Sanduhr" / "settings.json"
USER_THEMES_DIR = pathlib.Path(os.environ["APPDATA"]) / "Sanduhr" / "themes"
PYTHON = REPO / "windows" / ".venv" / "Scripts" / "python.exe"


def stash_user_themes():
    """Move user theme JSONs out of the way so screenshots show just built-ins.
    Returns the stash path to be restored after capture."""
    if not USER_THEMES_DIR.exists():
        return None
    files = list(USER_THEMES_DIR.glob("*.json"))
    if not files:
        return None
    stash = USER_THEMES_DIR.parent / "themes.capture-stash"
    stash.mkdir(exist_ok=True)
    for f in files:
        f.rename(stash / f.name)
    return stash


def restore_user_themes(stash):
    if stash is None or not stash.exists():
        return
    for f in stash.glob("*.json"):
        f.rename(USER_THEMES_DIR / f.name)
    stash.rmdir()


def write_theme(theme: str) -> None:
    SETTINGS.parent.mkdir(parents=True, exist_ok=True)
    existing = {}
    if SETTINGS.exists():
        try:
            existing = json.loads(SETTINGS.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            existing = {}
    existing["theme"] = theme
    # Force a known window size + position so every screenshot has identical
    # dimensions regardless of what the user's last session had.
    existing["window"] = {"x": 100, "y": 100, "w": 520, "h": 560}
    SETTINGS.write_text(json.dumps(existing), encoding="utf-8")


def find_window(retries: int = 30) -> int:
    for _ in range(retries):
        hwnd = win32gui.FindWindow(None, TITLE)
        if hwnd:
            return hwnd
        time.sleep(0.4)
    raise RuntimeError(f"Could not find window with title {TITLE!r}")


def grab_hwnd(hwnd: int) -> Image.Image:
    """Screen-grab the widget including Mica backdrop via PrintWindow."""
    left, top, right, bottom = win32gui.GetWindowRect(hwnd)
    w, h = right - left, bottom - top

    hwnd_dc = win32gui.GetWindowDC(hwnd)
    mfc_dc = win32ui.CreateDCFromHandle(hwnd_dc)
    save_dc = mfc_dc.CreateCompatibleDC()

    bmp = win32ui.CreateBitmap()
    bmp.CreateCompatibleBitmap(mfc_dc, w, h)
    save_dc.SelectObject(bmp)

    # PW_RENDERFULLCONTENT = 2 (Win10 1607+) captures DWM-composited content
    # including Mica backdrop. Without this flag, translucent areas come out
    # black.
    ctypes.windll.user32.PrintWindow(hwnd, save_dc.GetSafeHdc(), 2)

    bmp_info = bmp.GetInfo()
    bmp_bits = bmp.GetBitmapBits(True)
    img = Image.frombuffer(
        "RGB",
        (bmp_info["bmWidth"], bmp_info["bmHeight"]),
        bmp_bits, "raw", "BGRX", 0, 1,
    )

    win32gui.DeleteObject(bmp.GetHandle())
    save_dc.DeleteDC()
    mfc_dc.DeleteDC()
    win32gui.ReleaseDC(hwnd, hwnd_dc)
    return img


def kill_widget() -> None:
    subprocess.run(
        ["powershell", "-Command",
         "Get-Process | Where-Object { $_.ProcessName -eq 'python' -and "
         "$_.MainWindowTitle -match 'Sanduhr' } | Stop-Process -Force"],
        capture_output=True, text=True,
    )
    time.sleep(0.5)


def capture_theme(theme: str) -> pathlib.Path:
    print(f"-> {theme}...")
    write_theme(theme)
    proc = subprocess.Popen(
        [str(PYTHON), "-m", "sanduhr"],
        cwd=str(REPO / "windows"),
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
    try:
        hwnd = find_window()
        # Wait extra for Mica to apply + cards to render
        time.sleep(3.0)
        img = grab_hwnd(hwnd)
        out = SHOTS_DIR / f"{theme}.png"
        img.save(out, "PNG")
        print(f"   wrote {out} ({img.size[0]}x{img.size[1]})")
        return out
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            proc.kill()
        kill_widget()


def main() -> int:
    kill_widget()  # any lingering instance
    stash = stash_user_themes()
    try:
        for theme in THEMES:
            capture_theme(theme)
    finally:
        restore_user_themes(stash)
    print(f"\nDone. {len(THEMES)} screenshots in {SHOTS_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
