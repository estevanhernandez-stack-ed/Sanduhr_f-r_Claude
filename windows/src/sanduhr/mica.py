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
