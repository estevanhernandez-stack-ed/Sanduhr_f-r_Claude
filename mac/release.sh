#!/usr/bin/env bash
# Cut a signed, notarized, stapled Sanduhr release and update the appcast.
#
# Usage: ./release.sh <version>     # e.g. ./release.sh 1.1.0
#
# Before running:
#   1. Bump CFBundleShortVersionString + CFBundleVersion in Info.plist.
#   2. Have these in place from earlier setup:
#      - Developer ID Application cert in login keychain
#      - `sanduhr-notary` notarytool profile (see notarize.sh header)
#      - Sparkle EdDSA private key in login keychain (from generate_keys)
#
# What it does:
#   1. Builds Sanduhr.app with Dev ID + hardened runtime + timestamp.
#   2. Submits Sanduhr.app to Apple notary, waits, staples the ticket.
#   3. Rolls it into a signed DMG.
#   4. Submits the DMG to notary, staples.
#   5. Copies the DMG to ../releases/Sanduhr-<version>.dmg so Sparkle's
#      generate_appcast can index it alongside past releases.
#   6. Regenerates docs/appcast.xml using generate_appcast, which pulls
#      the EdDSA signature from your keychain automatically.
#
# After it finishes:
#   a. Create a GitHub Release tagged v<version> and attach the DMG.
#   b. Commit + push docs/appcast.xml so Sparkle clients pick up the
#      update on their next scheduled check (default: 24h).
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
    echo "Usage: $0 <version>   # e.g. $0 1.1.0" >&2
    echo "Bump Info.plist's CFBundleShortVersionString first." >&2
    exit 2
fi

REPO_ROOT="$(cd .. && pwd)"
RELEASES_DIR="$REPO_ROOT/releases"
APPCAST_DIR="$REPO_ROOT/docs"
DOWNLOAD_URL_PREFIX="https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v${VERSION}/"

echo "━━━ Release $VERSION ━━━"

# 1. Build signed app.
./build.sh

# 2. Notarize + staple the .app.
./notarize.sh Sanduhr.app

# 3. Build + sign the DMG (reusing the already-signed .app).
./make-dmg.sh --skip-build

# 4. Notarize + staple the DMG.
./notarize.sh Sanduhr.dmg

# 5. Stage versioned DMG into the releases dir (gitignored — uploaded
# manually to GitHub Releases).
mkdir -p "$RELEASES_DIR"
DMG_OUT="$RELEASES_DIR/Sanduhr-$VERSION.dmg"
cp Sanduhr.dmg "$DMG_OUT"
echo "→ DMG staged at $DMG_OUT"

# 6. Regenerate appcast.xml against the releases dir.
GENERATE_APPCAST=".build/artifacts/sparkle/Sparkle/bin/generate_appcast"
if [[ ! -x "$GENERATE_APPCAST" ]]; then
    echo "✗ generate_appcast not found at $GENERATE_APPCAST" >&2
    echo "  Run 'swift package resolve' from mac/ first." >&2
    exit 1
fi

mkdir -p "$APPCAST_DIR"
echo "→ Generating appcast..."
# Sparkle's generate_appcast takes the archives dir positionally and uses
# `-o <file>` for an explicit output path (not --output-dir, which it
# doesn't support).
"$GENERATE_APPCAST" "$RELEASES_DIR" \
    --download-url-prefix "$DOWNLOAD_URL_PREFIX" \
    -o "$APPCAST_DIR/appcast.xml"

cat <<DONE

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✓ Release $VERSION built, signed, notarized, stapled
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Next (do these manually — one-time steps per release):

1. Tag + upload the DMG to GitHub Releases:
     cd "$REPO_ROOT"
     gh release create "v$VERSION" "$DMG_OUT" --title "Sanduhr v$VERSION" --generate-notes

2. Commit + push the updated appcast:
     git add docs/appcast.xml
     git commit -m "Release v$VERSION"
     git push

Sparkle clients pick up the update on their next scheduled check
(24h default). Users can force via menu → Check for Updates…
DONE
