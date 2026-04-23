"""Usage history CSV export.

Writes a flat CSV any LLM or spreadsheet can parse. Columns:
timestamp, tier, util_pct. One row per (tier, timestamp) pair from the
local history.json file. Never touches the network — CSV lives on the
user's filesystem at a path they chose via the file dialog."""

import csv
from pathlib import Path

from sanduhr import history


def export_to_csv(dest_path: str | Path) -> int:
    """Export all local usage history to `dest_path` as CSV.

    Returns the number of data rows written (excluding the header).
    Always writes the header row, even if history is empty."""
    dest_path = Path(dest_path)
    all_history = history.load_history()
    rows = []
    for tier_key, points in all_history.items():
        for p in points:
            rows.append({
                "timestamp": p.get("t", ""),
                "tier": tier_key,
                "util_pct": str(p.get("v", "")),
            })
    rows.sort(key=lambda r: r["timestamp"])

    with open(dest_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=["timestamp", "tier", "util_pct"])
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)
