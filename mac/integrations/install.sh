#!/usr/bin/env bash
# Install Sanduhr's Claude Code integrations on this Mac:
#   - the statusline segment  (5h 42% | wk 18% | wk resets Thu 3p)
#   - the sanduhr MCP server  (get_usage, ping) for Claude Code
# Both only read ~/Library/Application Support/Sanduhr/snapshot.json, which Sanduhr
# writes after every fetch. Neither touches claude.ai or the session key.
#   bash mac/integrations/install.sh            install or update
#   bash mac/integrations/install.sh --remove   unregister the MCP server, delete the copies
set -eu
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="$HOME/Library/Application Support/Sanduhr/integrations"
PY="$(command -v python3 || true)"

if [ "${1:-}" = "--remove" ]; then
  command -v claude >/dev/null && claude mcp remove sanduhr --scope user >/dev/null 2>&1 && echo "Removed the sanduhr MCP server" || true
  rm -f "$DEST/sanduhr_statusline.py" "$DEST/sanduhr_mcp.py"
  echo "Removed the scripts. If a statusLine in your Claude settings calls sanduhr_statusline.py, it now prints nothing."
  exit 0
fi

[ -n "$PY" ] || { echo "Needs python3 (xcode-select --install)."; exit 1; }
mkdir -p "$DEST"
cp "$HERE/sanduhr_statusline.py" "$HERE/sanduhr_mcp.py" "$DEST/"
chmod +x "$DEST"/*.py
echo "Installed to $DEST"

if command -v claude >/dev/null; then
  if claude mcp get sanduhr >/dev/null 2>&1; then
    echo "MCP server 'sanduhr' already registered (the script was updated in place)"
  else
    claude mcp add sanduhr --scope user -- "$PY" "$DEST/sanduhr_mcp.py" && echo "Registered MCP server 'sanduhr' for Claude Code (user scope)"
  fi
else
  echo "Claude Code not found; register later with:"
  echo "  claude mcp add sanduhr --scope user -- $PY \"$DEST/sanduhr_mcp.py\""
fi

echo
echo "Statusline now:  $("$PY" "$DEST/sanduhr_statusline.py" || true)"
echo "To show it in Claude Code, call it from your statusLine command, or use it alone:"
echo "  \"statusLine\": {\"type\": \"command\", \"command\": \"$PY '$DEST/sanduhr_statusline.py'\"}"
