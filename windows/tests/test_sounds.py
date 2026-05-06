"""Tests for the in-memory chime WAV generator.

Don't actually play — that would be flaky and noisy on CI. Just
verify the WAV blob is structurally valid and the convenience
play_save_confirmation() helper soft-fails when winsound isn't
available."""

import struct


def test_build_wav_emits_valid_riff_header():
    from sanduhr.sounds import _build_wav

    blob = _build_wav([(440.0, 0.05)])  # 50ms A4
    # RIFF / WAVE header sanity
    assert blob[:4] == b"RIFF"
    assert blob[8:12] == b"WAVE"
    assert blob[12:16] == b"fmt "
    # data chunk
    assert b"data" in blob[36:]


def test_build_wav_data_size_matches_header():
    from sanduhr.sounds import _build_wav

    blob = _build_wav([(440.0, 0.05)])
    # data chunk size at offset 40-44
    declared_data = struct.unpack("<I", blob[40:44])[0]
    actual_data = len(blob) - 44
    assert declared_data == actual_data


def test_wav_path_cache_returns_same_path_on_second_call(tmp_path, monkeypatch):
    """File-based playback writes the WAV once on first call;
    subsequent calls reuse the same path."""
    from sanduhr import sounds

    monkeypatch.setattr(sounds, "_sounds_dir", lambda: tmp_path)
    sounds._FILE_CACHE.clear()
    a = sounds._wav_path_for(sounds._NOTES_SUCCESS, "success_test")
    b = sounds._wav_path_for(sounds._NOTES_SUCCESS, "success_test")
    assert a == b
    assert a.exists()
    assert a.read_bytes()[:4] == b"RIFF"


def test_play_error_and_play_info_do_not_raise(monkeypatch):
    """The expanded tone palette (error / info) shares the same
    soft-fail wrapper as play_save_confirmation — verify both new
    helpers stay quiet when winsound is absent."""
    from sanduhr import sounds
    monkeypatch.setattr(sounds, "winsound", None)
    sounds.play_error()
    sounds.play_info()


def test_play_save_confirmation_does_not_raise(monkeypatch):
    """Cover the soft-fail path — even with winsound missing or
    raising, play_save_confirmation must not propagate."""
    from sanduhr import sounds

    monkeypatch.setattr(sounds, "winsound", None)
    sounds.play_save_confirmation()  # should be a no-op, no raise


def test_play_save_confirmation_swallows_winsound_errors(monkeypatch):
    from sanduhr import sounds

    class _FakeWinsound:
        SND_MEMORY = 1
        SND_ASYNC = 2
        SND_NODEFAULT = 4

        def PlaySound(self, *args, **kwargs):
            raise RuntimeError("sound subsystem dead")

    monkeypatch.setattr(sounds, "winsound", _FakeWinsound())
    # Must not propagate the RuntimeError.
    sounds.play_save_confirmation()
