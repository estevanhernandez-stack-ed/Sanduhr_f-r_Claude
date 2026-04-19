"""Pure pacing and projection calculations.

Ported verbatim from v1 sanduhr.py. The math is correct and well-tested;
this module is a clean home for it with zero Qt / tkinter dependencies.
"""

from datetime import datetime, timezone

_FIVE_HOUR_SECS = 5 * 3600
_SEVEN_DAY_SECS = 7 * 86400


def _parse(iso_str):
    try:
        return datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
    except (AttributeError, ValueError):
        return None


def time_until(iso_str):
    """Human countdown like '3d 5h 30m' / '45m' / 'now'."""
    if not iso_str:
        return "--"
    rd = _parse(iso_str)
    if rd is None:
        return "--"
    secs = max(0, int((rd - datetime.now(timezone.utc)).total_seconds()))
    if secs <= 0:
        return "now"
    d, r = divmod(secs, 86400)
    h, r = divmod(r, 3600)
    m = r // 60
    parts = []
    if d:
        parts.append(f"{d}d")
    if h:
        parts.append(f"{h}h")
    if m or not parts:
        parts.append(f"{m}m")
    return " ".join(parts)


def _tier_total_secs(tier_key):
    return _FIVE_HOUR_SECS if tier_key == "five_hour" else _SEVEN_DAY_SECS


def pace_frac(resets_at, tier_key):
    """How far through the period we are, in [0.0, 1.0]. None on bad input."""
    if not resets_at:
        return None
    rd = _parse(resets_at)
    if rd is None:
        return None
    rem = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    total = _tier_total_secs(tier_key)
    if total <= 0:
        return None
    return min(1.0, max(0.0, (total - rem) / total))


def pace_info(util, resets_at, tier_key):
    """Return (label, color) tuple describing on / ahead / under pace."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None:
        return None
    diff = util - frac * 100
    if abs(diff) < 5:
        return ("On pace", "#4ade80")
    if diff > 0:
        return (f"{round(abs(diff))}% ahead", "#fb923c")
    return (f"{round(abs(diff))}% under", "#60a5fa")


def calculate_cooldown(util, resets_at, tier_key):
    """Return '45m' if ahead of pace, else None."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None:
        return None
    wait_frac = (util / 100.0) - frac
    if wait_frac <= 0:
        return None
    total = _tier_total_secs(tier_key)
    secs = int(wait_frac * total)
    d, r = divmod(secs, 86400)
    h, r = divmod(r, 3600)
    m = r // 60
    parts = []
    if d:
        parts.append(f"{d}d")
    if h:
        parts.append(f"{h}h")
    if m or not parts:
        parts.append(f"{m}m")
    return " ".join(parts)


def calculate_surplus(util, resets_at, tier_key):
    """Return surplus integer percentage if under pace, else None."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None:
        return None
    surplus = (frac * 100.0) - util
    if surplus <= 0:
        return None
    return int(surplus)


def burn_projection(util, resets_at, tier_key):
    """Return (message, color) when current pace will hit 100% before reset, else None."""
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None or util <= 0 or frac <= 0:
        return None
    rate_per_frac = util / frac  # projected total% over the full period
    if rate_per_frac <= 100:
        return None  # won't hit 100% before reset

    rd = _parse(resets_at)
    if rd is None:
        return None
    secs_until_reset = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    total = _tier_total_secs(tier_key)

    frac_at_100 = 100 / rate_per_frac
    secs_until_100 = max(0, (frac_at_100 - frac) * total)

    if secs_until_100 <= 0:
        return ("Limit reached", "#f87171")
    if secs_until_100 >= secs_until_reset:
        return None  # resets first

    d, r = divmod(int(secs_until_100), 86400)
    h, r = divmod(r, 3600)
    m = r // 60
    parts = []
    if d:
        parts.append(f"{d}d")
    if h:
        parts.append(f"{h}h")
    if m or not parts:
        parts.append(f"{m}m")
    return (f"At current pace, expires in {' '.join(parts)}", "#f87171")


def velocity_projection(util, resets_at, tier_key):
    """Project final utilization at reset if current momentum continues.

    Returns a float in [0, 200] (capped at 200 to avoid absurd bars),
    or None when the data is insufficient.
    """
    frac = pace_frac(resets_at, tier_key)
    if frac is None or util is None or util <= 0 or frac <= 0.01:
        return None
    projected = util / frac  # simple linear extrapolation
    return min(200.0, projected)


def reset_datetime_str(iso_str):
    """Friendly 'Today 1:00 AM' / 'Tomorrow 1:00 AM' / 'Sun 1:00 AM' / 'Wed Apr 22 1:00 AM'."""
    if not iso_str:
        return ""
    rd = _parse(iso_str)
    if rd is None:
        return ""
    loc = rd.astimezone()
    now = datetime.now().astimezone()
    days = (loc.date() - now.date()).days
    t = loc.strftime("%I:%M %p").lstrip("0")
    if days <= 0:
        return f"Today {t}"
    if days == 1:
        return f"Tomorrow {t}"
    if days < 7:
        return f"{loc.strftime('%a')} {t}"
    return f"{loc.strftime('%a %b %d')} {t}"
