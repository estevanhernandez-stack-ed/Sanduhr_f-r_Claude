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
    # First-pass org-scoped (all 404'd in the first run, kept for completeness)
    "/organizations/{org_id}/routines",
    "/organizations/{org_id}/routines/usage",
    "/organizations/{org_id}/usage/routines",
    "/organizations/{org_id}/usage_routines",
    "/organizations/{org_id}/routines_usage",
    "/organizations/{org_id}/usage_limits",
    "/organizations/{org_id}/limits",
    "/organizations/{org_id}/quotas",
    "/organizations/{org_id}/quota",
    "/organizations/{org_id}/routine_runs",
    "/organizations/{org_id}/scheduled_tasks",
    "/organizations/{org_id}/scheduled_runs",
    "/organizations/{org_id}/account",
    "/organizations/{org_id}/account/usage",
    "/organizations/{org_id}/account/limits",
    "/organizations/{org_id}/usage_v2",
    "/organizations/{org_id}/usage/limits",
    "/organizations/{org_id}/usage/quotas",
    # User-scoped
    "/users/me/routines",
    "/users/me/routines/usage",
    "/users/me/usage",
    "/users/me/usage_limits",
    "/users/me/limits",
    "/users/me/quotas",
    "/users/me/quota",
    # Account-prefixed
    "/account",
    "/account/usage",
    "/account/usage_limits",
    "/account/limits",
    "/account/quotas",
    "/account/routines",
    "/account/usage/routines",
    # Code surface
    "/code/usage",
    "/code/limits",
    "/code/routines",
    "/code/routines/usage",
    # Bare
    "/routines",
    "/routines/usage",
    "/usage_limits",
    "/limits",
    "/quotas",
    # CCR / remote-control variants — internal_has_used_remote_control = true
    # and a forest of ccr_* feature flags in /account suggest Routines lives
    # under one of these namespaces.
    "/organizations/{org_id}/ccr_routines",
    "/organizations/{org_id}/ccr/routines",
    "/organizations/{org_id}/ccr/routines/usage",
    "/organizations/{org_id}/ccr_routine_runs",
    "/organizations/{org_id}/ccr/runs",
    "/organizations/{org_id}/ccr/usage",
    "/organizations/{org_id}/ccr/limits",
    "/organizations/{org_id}/ccr",
    "/organizations/{org_id}/scheduled_routines",
    "/organizations/{org_id}/scheduled",
    "/organizations/{org_id}/cron",
    "/organizations/{org_id}/cron_jobs",
    "/organizations/{org_id}/agents",
    "/organizations/{org_id}/agents/runs",
    "/organizations/{org_id}/agents/usage",
    "/organizations/{org_id}/remote_control",
    "/organizations/{org_id}/remote_control/runs",
    # Subscription / billing surfaces
    "/organizations/{org_id}/subscription",
    "/organizations/{org_id}/subscription/usage",
    "/organizations/{org_id}/subscription/limits",
    "/organizations/{org_id}/billing",
    "/organizations/{org_id}/billing/usage",
    "/organizations/{org_id}/billing_summary",
    "/organizations/{org_id}/plan",
    "/organizations/{org_id}/plan/usage",
    "/organizations/{org_id}/plan/limits",
    "/organizations/{org_id}/usage_overview",
    "/organizations/{org_id}/usage_summary",
    # Bare CCR / agents / cron
    "/ccr",
    "/ccr/routines",
    "/ccr/routines/usage",
    "/ccr/runs",
    "/agents",
    "/agents/usage",
    "/cron",
    "/scheduled",
    "/remote_control",
    "/remote_control/runs",
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
