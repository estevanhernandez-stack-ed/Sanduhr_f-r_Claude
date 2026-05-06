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


# C5 → E5 → G5 ascending arpeggio. Bright, short, doesn't outstay
# its welcome — about 350 ms total.
_SAVE_CONFIRM_NOTES = [
    (523.25, 0.10),  # C5
    (659.25, 0.10),  # E5
    (783.99, 0.16),  # G5 (held a hair longer for the landing)
]
_SAVE_CONFIRM_WAV: Optional[bytes] = None


def _save_confirm_wav() -> bytes:
    """Cached lazy-build of the save chime so the cost is paid once,
    not on every call (and not at module import which would fire even
    on a fresh import of the package for tests that don't play
    anything)."""
    global _SAVE_CONFIRM_WAV
    if _SAVE_CONFIRM_WAV is None:
        _SAVE_CONFIRM_WAV = _build_wav(_SAVE_CONFIRM_NOTES)
    return _SAVE_CONFIRM_WAV


def play_save_confirmation() -> None:
    """Async playback of the save chime. No-op on platforms without
    winsound, and silently swallows any audio-subsystem failure —
    a successful save shouldn't be undone by a sound-card hiccup."""
    if winsound is None:
        return
    try:
        winsound.PlaySound(
            _save_confirm_wav(),
            winsound.SND_MEMORY | winsound.SND_ASYNC | winsound.SND_NODEFAULT,
        )
    except Exception:
        _log.debug("Save-confirmation sound failed", exc_info=True)
