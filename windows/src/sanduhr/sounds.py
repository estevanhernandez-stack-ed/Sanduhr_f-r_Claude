"""Short audio cues for save / success confirmations.

Generates small PCM-WAV files in %APPDATA%\\Sanduhr\\sounds at first
use and plays them asynchronously via winsound.PlaySound with
SND_FILENAME. Tried SND_MEMORY first but it silently dropped
playback on multiple Windows configs — file-based is the reliable
winsound path. No shipped asset (we render the WAVs ourselves), no
Qt audio backend dependency, no main-thread block. Soft-fails on
non-Windows platforms or if winsound is unavailable.
"""

import logging
import math
import os
import struct
import tempfile
from pathlib import Path
from typing import Optional

_log = logging.getLogger(__name__)

try:
    import winsound  # Windows-only; soft-fail elsewhere.
except ImportError:
    winsound = None  # type: ignore[assignment]


def _sounds_dir() -> Path:
    """%APPDATA%\\Sanduhr\\sounds — created lazily on first use.
    Reuses the existing app data root so cleanup on uninstall takes
    the WAV cache with it."""
    base = Path(os.environ.get("APPDATA", tempfile.gettempdir()))
    d = base / "Sanduhr" / "sounds"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _build_wav(notes: list[tuple[float, float]], sample_rate: int = 44100) -> bytes:
    """Build a 16-bit mono PCM-WAV blob from `notes` — list of
    `(frequency_hz, duration_s)` tuples played sequentially.

    Each note has a short linear attack (5 ms) and release (30 ms)
    envelope so they don't click at the boundaries. Amplitude is
    deliberately conservative (~25% of full scale) — these are
    background-of-attention cues, not announcements.
    """
    samples = bytearray()
    for freq, dur in notes:
        n = int(sample_rate * dur)
        attack = 0.005 * sample_rate
        release = 0.030 * sample_rate
        for i in range(n):
            t = i / sample_rate
            attack_env = min(1.0, i / attack) if attack > 0 else 1.0
            release_env = (
                min(1.0, (n - i) / release) if release > 0 else 1.0
            )
            env = attack_env * release_env
            sample = int(8000 * env * math.sin(2 * math.pi * freq * t))
            samples.extend(struct.pack("<h", sample))

    data_size = len(samples)
    header = bytearray()
    header.extend(b"RIFF")
    header.extend(struct.pack("<I", 36 + data_size))
    header.extend(b"WAVE")
    header.extend(b"fmt ")
    header.extend(struct.pack("<I", 16))   # PCM fmt chunk size
    header.extend(struct.pack("<H", 1))    # PCM format
    header.extend(struct.pack("<H", 1))    # mono
    header.extend(struct.pack("<I", sample_rate))
    header.extend(struct.pack("<I", sample_rate * 2))  # byte rate
    header.extend(struct.pack("<H", 2))    # block align
    header.extend(struct.pack("<H", 16))   # bits per sample
    header.extend(b"data")
    header.extend(struct.pack("<I", data_size))
    return bytes(header) + bytes(samples)


# Tone palette. Pitched an octave below the obvious choice (lower =
# warmer, less attention-grabbing) and amplitude-attenuated to ~25%
# of full scale. Ascending shapes for success / info, descending
# for error so severity reads by ear. Each tone stays short (~300 ms
# total) and pleasant — background-of-attention, not announcements.
_NOTES_SUCCESS = [
    (261.63, 0.10),  # C4
    (329.63, 0.10),  # E4
    (392.00, 0.16),  # G4 (slightly longer landing)
]
_NOTES_ERROR = [
    (293.66, 0.10),  # D4
    (246.94, 0.10),  # B3
    (196.00, 0.18),  # G3 (low landing, signals 'something didn't land')
]
_NOTES_INFO = [
    (329.63, 0.08),  # E4
    (329.63, 0.12),  # E4 (same pitch, two-beat tap — neutral 'note')
]
_NOTES_TOGGLE = [
    (440.00, 0.04),  # A4 — single brief tap, ~50 ms total with envelope
]

# Cached file paths — pay the build + write cost once per tone.
_FILE_CACHE: dict[str, Path] = {}


def _wav_path_for(notes: list[tuple[float, float]], cache_key: str) -> Path:
    """Lazy-build the WAV file for a given tone shape and return its
    on-disk path. File is written to %APPDATA%\\Sanduhr\\sounds.

    Always (re)writes on first call per process — that way, when we
    update the tone palette in a release, users get the new sound on
    next launch without needing manual cache invalidation. In-process
    cache prevents redundant writes within a single session."""
    if cache_key in _FILE_CACHE and _FILE_CACHE[cache_key].exists():
        return _FILE_CACHE[cache_key]
    path = _sounds_dir() / f"{cache_key}.wav"
    try:
        path.write_bytes(_build_wav(notes))
    except OSError:
        # File might be locked from a previous async play — fall back
        # to the existing version if any.
        if not path.exists():
            raise
    _FILE_CACHE[cache_key] = path
    return path


def _play(notes: list[tuple[float, float]], cache_key: str) -> None:
    """Soft-fail async file-based play. Sound failures must never
    propagate — a bad sound card shouldn't crash the widget over a
    save dialog.

    Uses SND_FILENAME instead of SND_MEMORY: empirically more
    reliable on Windows when called from Qt's event loop. The temp
    file approach also dodges any GC issues with the bytes argument
    going out of scope mid-async-playback.

    Env var SANDUHR_SILENT_SOUNDS=1 disables playback entirely —
    set in conftest.py so test temp dirs can clean up without
    Windows holding WAV files open async."""
    if winsound is None or os.environ.get("SANDUHR_SILENT_SOUNDS"):
        return
    try:
        path = _wav_path_for(notes, cache_key)
        winsound.PlaySound(
            str(path),
            winsound.SND_FILENAME | winsound.SND_ASYNC,
        )
    except Exception:
        _log.debug("Sound playback failed", exc_info=True)


def play_save_confirmation() -> None:
    """Async ascending C-E-G arpeggio for save / action success."""
    _play(_NOTES_SUCCESS, "success")


def play_error() -> None:
    """Async descending D-B-G arpeggio for failures / invalid input.
    Replaces the Windows system error beep on validation dialogs."""
    _play(_NOTES_ERROR, "error")


def play_info() -> None:
    """Async two-beat E5 tap for neutral / informational dialogs.
    Lighter than the success chime — used when the dialog is just
    surfacing info, not confirming a write."""
    _play(_NOTES_INFO, "info")


def play_toggle() -> None:
    """Async brief A5 tap for checkbox / toggle interactions. Much
    softer than the save/error chimes — meant to be felt rather than
    heard, like a mechanical click."""
    _play(_NOTES_TOGGLE, "toggle")
