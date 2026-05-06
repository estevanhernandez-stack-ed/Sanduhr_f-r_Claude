"""Probe candidate Routines endpoints to discover where claude.ai
returns the daily-routine quota. Reads creds from keyring, fetches
org_id via the existing ClaudeAPI, then tries a list of likely paths
and dumps any that respond 200.

Usage:
    .venv/Scripts/python.exe scripts/sniff_routines.py

If every candidate returns 404/401, fall back to DevTools — open
claude.ai/settings/usage, copy the request URL that populates the
'0/15' indicator from the Network tab, and we'll add it to the
candidate list.
"""

import json
import sys

from sanduhr import api, credentials


CANDIDATE_PATHS = [
    "/organizations/{org_id}/routines",
    "/organizations/{org_id}/routines/usage",
    "/organizations/{org_id}/usage/routines",
    "/organizations/{org_id}/usage_routines",
    "/organizations/{org_id}/routines_usage",
    "/users/me/routines",
    "/users/me/routines/usage",
    "/routines",
    "/routines/usage",
    "/account/routines",
    "/account/usage/routines",
]


def main() -> int:
    creds = credentials.load()
    if not creds.get("session_key"):
        print("No session_key stored in keyring. Sign in via the app first.", file=sys.stderr)
        return 1

    client = api.ClaudeAPI(creds["session_key"], creds.get("cf_clearance"))
    try:
        org_id = client._get_org_id()
    except api.APIError as e:
        print(f"Org discovery failed: {e}", file=sys.stderr)
        return 2

    print(f"Probing {len(CANDIDATE_PATHS)} candidate endpoints under "
          f"https://claude.ai/api for org {org_id}\n")

    hits = []
    for path_tmpl in CANDIDATE_PATHS:
        path = path_tmpl.format(org_id=org_id)
        url = f"{api._API_BASE}{path}"
        try:
            resp = client._get(url)
        except Exception as e:
            print(f"  [ERR] {path:50s} {type(e).__name__}: {e}")
            continue
        status = resp.status_code
        if status == 200:
            try:
                body = resp.json()
                snippet = json.dumps(body)[:200]
            except Exception:
                snippet = resp.text[:200]
            print(f"  [200] {path:50s} {snippet}")
            hits.append((path, body if isinstance(body, (dict, list)) else None))
        else:
            print(f"  [{status}] {path:50s}")

    if not hits:
        print("\nNo candidate returned 200. Open DevTools on "
              "claude.ai/settings/usage, find the request that populates "
              "the daily-routine quota indicator, and share the URL.")
        return 3

    print(f"\nFound {len(hits)} hits. Full bodies:\n")
    for path, body in hits:
        print(f"=== {path} ===")
        print(json.dumps(body, indent=2, default=str))
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
