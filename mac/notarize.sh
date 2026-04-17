#!/usr/bin/env bash
# Submit Sanduhr.app or Sanduhr.dmg to Apple for notarization and staple
# the resulting ticket so Gatekeeper accepts it offline.
#
# One-time setup (stores the app-specific password in the login Keychain):
#   xcrun notarytool store-credentials sanduhr-notary \
#       --apple-id <your-apple-id> \
#       --team-id 82BSR56X5J \
#       --password <app-specific-password>
#
# Generate the app-specific password at https://appleid.apple.com →
# Sign-In and Security → App-Specific Passwords.
#
# Usage:
#   ./notarize.sh              # notarize Sanduhr.app
#   ./notarize.sh Sanduhr.dmg  # notarize a DMG
set -euo pipefail
cd "$(dirname "$0")"

PROFILE="${NOTARY_PROFILE:-sanduhr-notary}"
TARGET="${1:-Sanduhr.app}"

if [[ ! -e "$TARGET" ]]; then
    echo "✗ $TARGET not found" >&2
    exit 1
fi

# notarytool accepts .zip/.dmg/.pkg. Wrap .app bundles in a zip first.
CLEANUP_ZIP=""
if [[ "$TARGET" == *.app ]]; then
    UPLOAD="${TARGET%.app}-notarize.zip"
    echo "→ Zipping $TARGET → $UPLOAD"
    /usr/bin/ditto -c -k --sequesterRsrc --keepParent "$TARGET" "$UPLOAD"
    CLEANUP_ZIP="$UPLOAD"
else
    UPLOAD="$TARGET"
fi

echo "→ Submitting $UPLOAD (typically 1–5 minutes)..."
xcrun notarytool submit "$UPLOAD" \
    --keychain-profile "$PROFILE" \
    --wait

echo "→ Stapling ticket to $TARGET..."
xcrun stapler staple "$TARGET"
xcrun stapler validate "$TARGET"

if [[ -n "$CLEANUP_ZIP" ]]; then
    rm -f "$CLEANUP_ZIP"
fi

echo "✓ Notarized + stapled $TARGET"
