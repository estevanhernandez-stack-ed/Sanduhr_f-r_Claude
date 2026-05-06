"""Tests for the local Claude Code session-log reader."""

import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest


@pytest.fixture
def _fake_home(monkeypatch, tmp_path):
    """Pretend Path.home() returns tmp_path so cc_logs.search_roots
    resolves against a sandboxed dir tree we control."""
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: tmp_path))
    return tmp_path


def _write_session(
    home: Path, root_name: str, project_name: str, session_uuid: str, lines: list[dict]
) -> Path:
    """Helper — write a synthetic session JSONL under a fake CC root."""
    project_dir = home / root_name / "projects" / project_name
    project_dir.mkdir(parents=True, exist_ok=True)
    f = project_dir / f"{session_uuid}.jsonl"
    f.write_text(
        "\n".join(json.dumps(L) for L in lines) + "\n", encoding="utf-8"
    )
    return f


def _assistant_event(ts_iso: str, model: str, in_tokens: int, out_tokens: int) -> dict:
    return {
        "type": "assistant",
        "timestamp": ts_iso,
        "message": {
            "model": model,
            "usage": {
                "input_tokens": in_tokens,
                "output_tokens": out_tokens,
            },
        },
    }


def test_search_roots_finds_existing_dirs(_fake_home):
    from sanduhr import cc_logs
    (_fake_home / ".claude").mkdir()
    # .claude-personal does NOT exist
    assert cc_logs.search_roots() == [_fake_home / ".claude"]


def test_search_roots_finds_both_when_present(_fake_home):
    from sanduhr import cc_logs
    (_fake_home / ".claude").mkdir()
    (_fake_home / ".claude-personal").mkdir()
    roots = cc_logs.search_roots()
    assert _fake_home / ".claude" in roots
    assert _fake_home / ".claude-personal" in roots


def test_discover_log_files_handles_missing_projects_dir(_fake_home):
    from sanduhr import cc_logs
    (_fake_home / ".claude").mkdir()  # no projects/ subdir
    assert cc_logs.discover_log_files() == []


def test_discover_log_files_finds_session_jsonl(_fake_home):
    from sanduhr import cc_logs
    f = _write_session(_fake_home, ".claude", "C--proj", "abc", [
        _assistant_event("2026-05-05T10:00:00Z", "claude-opus-4-7", 100, 200),
    ])
    assert cc_logs.discover_log_files() == [f]


def test_discover_log_files_spans_both_roots(_fake_home):
    from sanduhr import cc_logs
    f1 = _write_session(_fake_home, ".claude", "P1", "s1", [])
    f2 = _write_session(_fake_home, ".claude-personal", "P2", "s2", [])
    files = cc_logs.discover_log_files()
    assert set(files) == {f1, f2}


def test_iter_usage_events_yields_only_assistant(_fake_home):
    from sanduhr import cc_logs
    f = _write_session(_fake_home, ".claude", "P", "s", [
        {"type": "user", "timestamp": "2026-05-05T10:00:00Z", "message": {"role": "user"}},
        _assistant_event("2026-05-05T10:01:00Z", "claude-opus-4-7", 100, 200),
        {"type": "system", "timestamp": "2026-05-05T10:02:00Z"},
    ])
    events = list(cc_logs.iter_usage_events(f))
    assert len(events) == 1
    ts, model, usage = events[0]
    assert model == "claude-opus-4-7"
    assert usage["input_tokens"] == 100
    assert usage["output_tokens"] == 200


def test_iter_usage_events_skips_malformed_lines(_fake_home):
    from sanduhr import cc_logs
    f = _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event("2026-05-05T10:00:00Z", "claude-opus-4-7", 50, 50),
    ])
    # Append a corrupt line and one without required fields.
    with open(f, "a", encoding="utf-8") as fh:
        fh.write("{not valid json\n")
        fh.write(json.dumps({"type": "assistant"}) + "\n")  # no message
        fh.write(json.dumps({"type": "assistant", "message": {}}) + "\n")  # no usage
    events = list(cc_logs.iter_usage_events(f))
    assert len(events) == 1


def test_tokens_since_filters_by_cutoff(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event((base - timedelta(hours=1)).isoformat(), "claude-opus-4-7", 100, 100),
        _assistant_event((base + timedelta(minutes=5)).isoformat(), "claude-opus-4-7", 50, 50),
        _assistant_event((base + timedelta(minutes=10)).isoformat(), "claude-sonnet-4-6", 30, 70),
    ])
    totals = cc_logs.tokens_since(base)
    # Only the two events at/after `base` count.
    assert totals == {"claude-opus-4-7": 100, "claude-sonnet-4-6": 100}


def test_tokens_since_aggregates_across_sessions(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s1", [
        _assistant_event((base + timedelta(minutes=1)).isoformat(), "claude-opus-4-7", 100, 200),
    ])
    _write_session(_fake_home, ".claude-personal", "Q", "s2", [
        _assistant_event((base + timedelta(minutes=2)).isoformat(), "claude-opus-4-7", 50, 150),
    ])
    totals = cc_logs.tokens_since(base)
    assert totals == {"claude-opus-4-7": 500}


def test_tokens_since_skips_zero_token_events(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event((base + timedelta(minutes=1)).isoformat(), "claude-opus-4-7", 0, 0),
    ])
    assert cc_logs.tokens_since(base) == {}


def test_model_to_tier_key_maps_known_prefixes():
    from sanduhr import cc_logs
    assert cc_logs.model_to_tier_key("claude-opus-4-7") == "seven_day_opus"
    assert cc_logs.model_to_tier_key("claude-opus-4-6") == "seven_day_opus"
    assert cc_logs.model_to_tier_key("claude-sonnet-4-6") == "seven_day_sonnet"
    assert cc_logs.model_to_tier_key("claude-haiku-4-5") == "seven_day"


def test_model_to_tier_key_returns_none_for_unknown():
    from sanduhr import cc_logs
    assert cc_logs.model_to_tier_key("gpt-4") is None
    assert cc_logs.model_to_tier_key(None) is None
    assert cc_logs.model_to_tier_key("") is None


def test_tokens_since_by_tier_groups_by_sanduhr_tier(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event((base + timedelta(minutes=1)).isoformat(), "claude-opus-4-7", 100, 200),
        _assistant_event((base + timedelta(minutes=2)).isoformat(), "claude-sonnet-4-6", 50, 50),
        _assistant_event((base + timedelta(minutes=3)).isoformat(), "gpt-4o", 999, 999),
    ])
    totals = cc_logs.tokens_since_by_tier(base)
    assert totals == {"seven_day_opus": 300, "seven_day_sonnet": 100}
