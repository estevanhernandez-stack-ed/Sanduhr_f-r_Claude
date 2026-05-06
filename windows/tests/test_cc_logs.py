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


def _assistant_event(
    ts_iso: str,
    model: str,
    in_tokens: int,
    out_tokens: int,
    cwd: str = None,
    attribution_skill: str = None,
) -> dict:
    event = {
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
    if cwd is not None:
        event["cwd"] = cwd
    if attribution_skill is not None:
        event["attributionSkill"] = attribution_skill
    return event


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
        _assistant_event(
            "2026-05-05T10:01:00Z", "claude-opus-4-7", 100, 200,
            cwd="C:/proj/Sanduhr", attribution_skill="my-skill",
        ),
        {"type": "system", "timestamp": "2026-05-05T10:02:00Z"},
    ])
    events = list(cc_logs.iter_usage_events(f))
    assert len(events) == 1
    event = events[0]
    assert event.model == "claude-opus-4-7"
    assert event.usage["input_tokens"] == 100
    assert event.usage["output_tokens"] == 200
    assert event.cwd == "C:/proj/Sanduhr"
    assert event.attribution_skill == "my-skill"


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


def test_tokens_by_day_groups_by_local_date(_fake_home):
    from sanduhr import cc_logs
    # Use relative timestamps so the 30-day window always holds
    # regardless of when the test runs.
    now = datetime.now(timezone.utc)
    d1 = now - timedelta(days=2)
    d2 = now - timedelta(days=1)
    d3 = now
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event(d1.isoformat(), "claude-opus-4-7", 50, 50),
        _assistant_event(
            (d1 + timedelta(hours=5)).isoformat(), "claude-opus-4-7", 100, 100,
        ),
        _assistant_event(d2.isoformat(), "claude-opus-4-7", 75, 25),
        _assistant_event(d3.isoformat(), "claude-opus-4-7", 200, 300),
    ])
    by_day = cc_logs.tokens_by_day(days_back=30)
    # 300 (d1 — two events) + 100 (d2) + 500 (d3) = 900.
    # Dates are LOCAL, not UTC, so day grouping depends on the test
    # runner's TZ; sum is the stable invariant.
    assert sum(by_day.values()) == 900
    # Bucket count is 1-3 depending on whether the +5h event crosses a
    # local midnight on the runner's TZ. Either way it's bounded.
    assert 1 <= len(by_day) <= 3


def test_tokens_by_day_drops_events_outside_window(_fake_home):
    from sanduhr import cc_logs
    base = datetime.now(timezone.utc)
    # 1 event 5 days ago (in window), 1 event 60 days ago (out of window).
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event(
            (base - timedelta(days=5)).isoformat(), "claude-opus-4-7", 100, 100,
        ),
        _assistant_event(
            (base - timedelta(days=60)).isoformat(), "claude-opus-4-7", 999, 999,
        ),
    ])
    by_day = cc_logs.tokens_by_day(days_back=30)
    assert sum(by_day.values()) == 200


def test_tokens_by_project_groups_by_cwd(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event(
            (base + timedelta(minutes=1)).isoformat(), "claude-opus-4-7", 100, 100,
            cwd="C:/Users/dev/Projects/Sanduhr",
        ),
        _assistant_event(
            (base + timedelta(minutes=2)).isoformat(), "claude-opus-4-7", 50, 50,
            cwd="C:/Users/dev/Projects/Sanduhr",
        ),
        _assistant_event(
            (base + timedelta(minutes=3)).isoformat(), "claude-opus-4-7", 70, 30,
            cwd="C:/Users/dev/Projects/Other",
        ),
        # No cwd — should be skipped
        _assistant_event(
            (base + timedelta(minutes=4)).isoformat(), "claude-opus-4-7", 999, 999,
        ),
    ])
    by_proj = cc_logs.tokens_by_project(base)
    assert by_proj == {
        "C:/Users/dev/Projects/Sanduhr": 300,
        "C:/Users/dev/Projects/Other": 100,
    }


def test_tokens_by_skill_groups_by_attribution(_fake_home):
    from sanduhr import cc_logs
    base = datetime(2026, 5, 5, 10, 0, 0, tzinfo=timezone.utc)
    _write_session(_fake_home, ".claude", "P", "s", [
        _assistant_event(
            (base + timedelta(minutes=1)).isoformat(), "claude-opus-4-7", 100, 100,
            attribution_skill="superpowers:debugging",
        ),
        _assistant_event(
            (base + timedelta(minutes=2)).isoformat(), "claude-opus-4-7", 50, 50,
            attribution_skill="superpowers:debugging",
        ),
        _assistant_event(
            (base + timedelta(minutes=3)).isoformat(), "claude-opus-4-7", 70, 30,
            attribution_skill="vibe-doc:scan",
        ),
        # No attribution_skill — should be skipped
        _assistant_event(
            (base + timedelta(minutes=4)).isoformat(), "claude-opus-4-7", 999, 999,
        ),
    ])
    by_skill = cc_logs.tokens_by_skill(base)
    assert by_skill == {
        "superpowers:debugging": 300,
        "vibe-doc:scan": 100,
    }


def test_project_display_name_extracts_basename():
    from sanduhr import cc_logs
    assert cc_logs.project_display_name("C:\\Users\\estev\\Projects\\Sanduhr") == "Sanduhr"
    assert cc_logs.project_display_name("C:/Users/estev/Projects/Sanduhr") == "Sanduhr"
    assert cc_logs.project_display_name("/home/dev/work/foo") == "foo"
    assert cc_logs.project_display_name("/home/dev/work/foo/") == "foo"
    assert cc_logs.project_display_name("") == ""
