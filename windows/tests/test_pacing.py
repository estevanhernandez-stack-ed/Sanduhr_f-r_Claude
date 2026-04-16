"""Tests for sanduhr.pacing -- pure functions, 100% branch target."""

from datetime import datetime, timedelta, timezone

import pytest

from sanduhr import pacing


def iso_from_now(seconds: int) -> str:
    # Add 1s buffer so elapsed time between construction and assertion doesn't
    # cause off-by-one failures in minute-resolution comparisons.
    return (datetime.now(timezone.utc) + timedelta(seconds=seconds + 1)).isoformat().replace(
        "+00:00", "Z"
    )


# -- time_until ------------------------------------------------------


def test_time_until_none_returns_dashes():
    assert pacing.time_until(None) == "--"


def test_time_until_invalid_iso_returns_dashes():
    assert pacing.time_until("not-a-date") == "--"


def test_time_until_past_returns_now():
    past = iso_from_now(-10)
    assert pacing.time_until(past) == "now"


def test_time_until_future_formats_days_hours_minutes():
    fut = iso_from_now(3 * 86400 + 5 * 3600 + 30 * 60)
    assert pacing.time_until(fut) == "3d 5h 30m"


def test_time_until_under_one_hour_shows_minutes_only():
    fut = iso_from_now(45 * 60)
    assert pacing.time_until(fut) == "45m"


def test_time_until_exactly_zero_minutes_shows_0m():
    fut = iso_from_now(1)  # ~0 minutes
    assert pacing.time_until(fut) == "0m"


# -- pace_frac -------------------------------------------------------


def test_pace_frac_none_inputs_returns_none():
    assert pacing.pace_frac(None, "five_hour") is None


def test_pace_frac_five_hour_tier_midway():
    # Reset in 2.5h for a 5h tier -- we're 50% through
    fut = iso_from_now(int(2.5 * 3600))
    result = pacing.pace_frac(fut, "five_hour")
    assert result == pytest.approx(0.5, abs=0.01)


def test_pace_frac_seven_day_tier_at_start():
    # Reset 7 days away for a 7-day tier -- we're 0% through
    fut = iso_from_now(7 * 86400)
    result = pacing.pace_frac(fut, "seven_day")
    assert result == pytest.approx(0.0, abs=0.001)


def test_pace_frac_clamps_to_one():
    # Reset already passed -- should clamp to 1.0
    past = iso_from_now(-100)
    result = pacing.pace_frac(past, "five_hour")
    assert result == 1.0


# -- pace_info -------------------------------------------------------


def test_pace_info_on_pace_when_within_5_percent():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through 5h tier
    result = pacing.pace_info(50, fut, "five_hour")
    assert result[0] == "On pace"


def test_pace_info_ahead_when_util_exceeds_fraction():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through
    result = pacing.pace_info(80, fut, "five_hour")  # 30% ahead
    assert "ahead" in result[0]
    assert "30%" in result[0]


def test_pace_info_under_when_util_below_fraction():
    fut = iso_from_now(int(2.5 * 3600))  # 50% through
    result = pacing.pace_info(20, fut, "five_hour")  # 30% under
    assert "under" in result[0]
    assert "30%" in result[0]


def test_pace_info_none_util_returns_none():
    fut = iso_from_now(3600)
    assert pacing.pace_info(None, fut, "five_hour") is None


# -- burn_projection -------------------------------------------------


def test_burn_projection_no_risk_returns_none():
    # 10% util at 50% through period -> won't hit 100%
    fut = iso_from_now(int(2.5 * 3600))
    assert pacing.burn_projection(10, fut, "five_hour") is None


def test_burn_projection_already_at_100():
    fut = iso_from_now(3600)
    result = pacing.burn_projection(100, fut, "five_hour")
    assert result is not None
    assert "reached" in result[0].lower() or "expires" in result[0].lower()


def test_burn_projection_warns_when_exhaustion_before_reset():
    # 90% util at 50% through -- projected 180% by reset, exhausts well before
    fut = iso_from_now(int(2.5 * 3600))
    result = pacing.burn_projection(90, fut, "five_hour")
    assert result is not None
    assert "expires" in result[0].lower() or "reached" in result[0].lower()


def test_burn_projection_none_inputs():
    assert pacing.burn_projection(None, None, "five_hour") is None


# -- reset_datetime_str ---------------------------------------------


def test_reset_datetime_str_none_returns_empty():
    assert pacing.reset_datetime_str(None) == ""


def test_reset_datetime_str_today_format():
    # Later today
    fut = iso_from_now(3600)
    result = pacing.reset_datetime_str(fut)
    assert result.startswith("Today")


def test_reset_datetime_str_invalid_returns_empty():
    assert pacing.reset_datetime_str("garbage") == ""


def test_reset_datetime_str_tomorrow_format():
    fut = iso_from_now(86400)
    result = pacing.reset_datetime_str(fut)
    assert result.startswith("Tomorrow")


def test_reset_datetime_str_short_weekday_format():
    fut = iso_from_now(3 * 86400)
    result = pacing.reset_datetime_str(fut)
    # Should be a 3-letter weekday, not "Today" or "Tomorrow"
    assert not result.startswith("Today")
    assert not result.startswith("Tomorrow")
    assert len(result.split()[0]) == 3


def test_reset_datetime_str_long_date_format():
    fut = iso_from_now(8 * 86400)
    result = pacing.reset_datetime_str(fut)
    # Should include month abbreviation (e.g. "Thu Apr 24 1:00 AM")
    assert not result.startswith("Today")
    assert not result.startswith("Tomorrow")
    parts = result.split()
    assert len(parts) >= 3  # weekday + month + day + time


# -- additional branch coverage ------------------------------------------


def test_pace_frac_invalid_iso_returns_none():
    assert pacing.pace_frac("not-a-date", "five_hour") is None


def test_burn_projection_exhausts_within_days():
    # 90% util at 10% through a 7-day tier -> exhausts in ~7h*0.1 from now
    # Reset 6.3 days away (10% through 7d tier)
    fut = iso_from_now(int(6.3 * 86400))
    result = pacing.burn_projection(90, fut, "seven_day")
    # rate_per_frac = 90/0.1 = 900 > 100, so projection fires
    # exhaustion at frac_at_100=100/900=0.111, secs_until_100=(0.111-0.1)*604800 ≈ 6654s
    # That's < secs_until_reset, so should return a result with days or hours
    assert result is not None
    assert "expires" in result[0].lower()


def test_burn_projection_resets_before_exhaustion():
    # 55% util at 90% through 5h tier -- rate=55/0.9=61.1% -> <= 100, returns None
    fut = iso_from_now(int(0.5 * 3600))  # 30m left = 90% through 5h
    assert pacing.burn_projection(55, fut, "five_hour") is None
