"""Short audio cues for save / success confirmations.

Generates a small in-memory PCM-WAV blob at import time and plays it
asynchronously via winsound.PlaySound — no shipped asset, no Qt
audio backend dependency, no main-thread block. Soft-fails on
non-Windows platforms or if winsound is unavailable.
"""

import logging
import math
import struct
from typing import Optional

_log = logging.getLogger(__name__)

try:
    import winsound  # Windows-only; soft-fail elsewhere.
except ImportError:
    winsound = None  # type: ignore[assignment]


def _build_wav(notes: list[tuple[float, float]], sample_rate: int = 44100) -> bytes:
    """Build a 16-bit mono PCM-WAV blob from `notes` — list of
    `(frequency_hz, duration_s)` tuples played sequentially.

    Each note has a short linear attack (5 ms) and release (30 ms)
    envelope so they don't click at the boundaries. Amplitude headroom
    leaves the file from going hot enough to clip on cheap speakers.
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
            sample = int(28000 * env * math.sin(2 * math.pi * freq * t))
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


# Tone palette. All ascending shapes for success / info, descending
# for error so the user can recognize severity by ear without seeing
# the dialog. Both deliberately short (~300 ms) and softer than the
# OS defaults — pleasant rather than punishing.
_NOTES_SUCCESS = [
    (523.25, 0.10),  # C5
    (659.25, 0.10),  # E5
    (783.99, 0.16),  # G5 (slightly longer landing)
]
_NOTES_ERROR = [
    (587.33, 0.10),  # D5
    (493.88, 0.10),  # B4
    (392.00, 0.18),  # G4 (lower landing, signals 'something didn't land')
]
_NOTES_INFO = [
    (659.25, 0.08),  # E5
    (659.25, 0.12),  # E5 (same pitch, two-beat tap — neutral 'note')
]
_NOTES_TOGGLE = [
    (880.00, 0.04),  # A5 — single brief tap, ~50 ms total with envelope
]

# Cached lazy builds — pay the math cost once per tone shape.
_WAV_CACHE: dict[str, bytes] = {}


def _wav_for(notes: list[tuple[float, float]], cache_key: str) -> bytes:
    if cache_key not in _WAV_CACHE:
        _WAV_CACHE[cache_key] = _build_wav(notes)
    return _WAV_CACHE[cache_key]


def _play(wav: bytes) -> None:
    """Soft-fail async play. Sound failures must never propagate —
    a bad sound card shouldn't crash the widget over a save dialog.

    Flags: SND_MEMORY tells PlaySound to read the bytes directly
    (no temp file); SND_ASYNC returns immediately so the UI doesn't
    block. SND_NODEFAULT was previously included to suppress the OS
    default sound on failure, but it interacts badly with SND_MEMORY
    on some Windows configs — silently drops the playback. Removed."""
    if winsound is None:
        return
    try:
        winsound.PlaySound(
            wav,
            winsound.SND_MEMORY | winsound.SND_ASYNC,
        )
    except Exception:
        _log.debug("Sound playback failed", exc_info=True)


def play_save_confirmation() -> None:
    """Async ascending C-E-G arpeggio for save / action success."""
    _play(_wav_for(_NOTES_SUCCESS, "success"))


def play_error() -> None:
    """Async descending D-B-G arpeggio for failures / invalid input.
    Replaces the Windows system error beep on validation dialogs."""
    _play(_wav_for(_NOTES_ERROR, "error"))


def play_info() -> None:
    """Async two-beat E5 tap for neutral / informational dialogs.
    Lighter than the success chime — used when the dialog is just
    surfacing info, not confirming a write."""
    _play(_wav_for(_NOTES_INFO, "info"))


def play_toggle() -> None:
    """Async brief A5 tap for checkbox / toggle interactions. Much
    softer than the save/error chimes — meant to be felt rather than
    heard, like a mechanical click."""
    _play(_wav_for(_NOTES_TOGGLE, "toggle"))
