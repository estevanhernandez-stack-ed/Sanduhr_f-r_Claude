"""One-off API sniff. Dumps the raw /usage response so we can see Routines tier shape.

Run from the project venv:
    .venv/Scripts/python.exe scripts/sniff_usage.py

Reads credentials from keyring (same as the running app). Prints JSON to stdout.
Not committed; gitignored under scripts/ in this dir.
"""

import json
import sys

from sanduhr import api, credentials


def main() -> int:
    creds = credentials.load()
    if not creds.get("session_key"):
        print("No session_key stored in keyring. Sign in via the app first.", file=sys.stderr)
        return 1

    client = api.ClaudeAPI(creds["session_key"], creds.get("cf_clearance"))
    try:
        data = client.get_usage()
    except api.SessionExpired as e:
        print(f"Session expired: {e}", file=sys.stderr)
        return 2
    except api.CloudflareBlocked as e:
        print(f"Cloudflare blocked: {e}", file=sys.stderr)
        return 3
    except api.NetworkError as e:
        print(f"Network error: {e}", file=sys.stderr)
        return 4

    print(json.dumps(data, indent=2, default=str))
    return 0


if __name__ == "__main__":
    sys.exit(main())
