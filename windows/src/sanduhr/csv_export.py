"""Usage history CSV export.

Writes a flat CSV any LLM or spreadsheet can parse. Columns:
- Single-account export: timestamp, tier, util_pct
- All-accounts export:   timestamp, account, tier, util_pct

One row per (tier, timestamp) pair from the local per-account history
file(s). Never touches the network — CSV lives on the user's filesystem
at a path they chose via the file dialog."""

import csv
from pathlib import Path
from typing import Optional

from sanduhr import accounts, history


def export_to_csv(dest_path: str | Path, account: Optional[str] = None) -> int:
    """Export local usage history to `dest_path` as CSV.

    `account=None` exports ALL registered accounts and includes an
    'account' column. `account="Personal"` (or any specific label)
    exports just that account's data without the account column.
    Defaulting to None matches the History tab's 'All accounts' view.

    Returns the number of data rows written (excluding the header).
    Always writes the header row, even if history is empty."""
    dest_path = Path(dest_path)
    rows = []

    if account is None:
        labels = accounts.list_accounts()
        # If no accounts registered, fall back to the active account
        # (which may also be None — in which case nothing to export).
        if not labels:
            active = accounts.get_active()
            labels = [active] if active else []
        fieldnames = ["timestamp", "account", "tier", "util_pct"]
        for label in labels:
            account_history = history.load_history(account=label)
            for tier_key, points in account_history.items():
                for p in points:
                    rows.append({
                        "timestamp": p.get("t", ""),
                        "account":   label,
                        "tier":      tier_key,
                        "util_pct":  str(p.get("v", "")),
                    })
    else:
        fieldnames = ["timestamp", "tier", "util_pct"]
        account_history = history.load_history(account=account)
        for tier_key, points in account_history.items():
            for p in points:
                rows.append({
                    "timestamp": p.get("t", ""),
                    "tier":      tier_key,
                    "util_pct":  str(p.get("v", "")),
                })

    # Lexicographic sort on the ISO-8601 timestamp strings is
    # chronological because history.append() writes a fixed `+00:00`
    # UTC offset every time. If a future writer ever introduces mixed
    # timezones, parse before sorting.
    rows.sort(key=lambda r: r["timestamp"])

    with open(dest_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)
