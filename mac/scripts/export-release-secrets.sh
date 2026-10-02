#!/usr/bin/env bash
# Run ONCE on the Mac that holds the signing material (Developer ID certificate, Sparkle key).
# Checks each secret, then uploads it to the repo's "mac-release" environment, where only
# .github/workflows/mac-release.yml can read it. Nothing is written outside a private temp
# folder, which is deleted on exit. Step by step: docs/mac-release.md.
#
# Usage: mac/scripts/export-release-secrets.sh [--dry-run] <path to DeveloperID.p12>
#   --dry-run   run every check, upload nothing
set -euo pipefail

REPO="estevanhernandez-stack-ed/Sanduhr_f-r_Claude"
ENVIRONMENT="mac-release"
TEAM_ID="82BSR56X5J"
IDENTITY="Developer ID Application: Estevan Hernandez ($TEAM_ID)"
SPARKLE_PUBLIC_KEY="b5hOsbZVS61d3/wAN8hxf70HvfH3SOpuNVZES3QvhpI="   # SUPublicEDKey in mac/Info.plist

DRY_RUN=false
[[ "${1:-}" == "--dry-run" ]] && { DRY_RUN=true; shift; }
P12="${1:-}"
[[ -n "$P12" && -f "$P12" ]] || { echo "Usage: $0 [--dry-run] <path to DeveloperID.p12>" >&2; exit 2; }

MAC_DIR="$(cd "$(dirname "$0")/.." && pwd)"
ok()   { echo "  ✓ $*"; }
fail() { echo "  ✗ $*" >&2; exit 1; }

TMP="$(mktemp -d)"
chmod 700 "$TMP"
CHECK_KC="$TMP/check.keychain-db"
cleanup() {
    security delete-keychain "$CHECK_KC" 2>/dev/null || true
    rm -rf "$TMP"
}
trap cleanup EXIT

echo "1/5 Tools"
command -v gh >/dev/null || fail "gh not installed: brew install gh"
gh auth status >/dev/null 2>&1 || fail "gh not signed in: gh auth login"
gh repo view "$REPO" --json name >/dev/null || fail "gh cannot see $REPO with this login"
xcrun notarytool --version >/dev/null 2>&1 || fail "notarytool missing: xcode-select --install"
ok "gh signed in, $REPO visible, notarytool present"

echo "2/5 Sparkle key"
GENERATE_KEYS="$MAC_DIR/.build/artifacts/sparkle/Sparkle/bin/generate_keys"
if [[ ! -x "$GENERATE_KEYS" ]]; then
    echo "  → fetching Sparkle's tools (swift package resolve)..."
    (cd "$MAC_DIR" && swift package resolve >/dev/null)
fi
[[ -x "$GENERATE_KEYS" ]] || fail "generate_keys not found at $GENERATE_KEYS"
echo "  (macOS may ask to let generate_keys use the Keychain: click Always Allow or Allow)"
PUBLIC="$("$GENERATE_KEYS" -p 2>/dev/null)" \
    || fail "no Sparkle key in this Mac's Keychain (or access was denied); this is not the Mac that signed 2.0.4"
PUBLIC="$(printf '%s' "$PUBLIC" | tail -1 | tr -d '[:space:]')"
[[ "$PUBLIC" == "$SPARKLE_PUBLIC_KEY" ]] \
    || fail "this Mac's Sparkle key ($PUBLIC) does not match Info.plist ($SPARKLE_PUBLIC_KEY); updates signed with it would be rejected"
"$GENERATE_KEYS" -x "$TMP/sparkle.key" >/dev/null
[[ -s "$TMP/sparkle.key" ]] || fail "generate_keys -x wrote nothing"
ok "public key matches Info.plist; private key exported to a temp file"

echo "3/5 Developer ID certificate"
read -r -s -p "  Password you set when exporting $(basename "$P12"): " P12_PASSWORD; echo
security create-keychain -p check "$CHECK_KC"
security import "$P12" -k "$CHECK_KC" -P "$P12_PASSWORD" >/dev/null \
    || fail "could not open the .p12 with that password"
security find-identity -p codesigning "$CHECK_KC" | grep -qF "$IDENTITY" \
    || fail "the .p12 has no '$IDENTITY' with its private key (export the certificate WITH its key, see docs/mac-release.md)"
security delete-keychain "$CHECK_KC"
ok "$IDENTITY, with private key"

echo "4/5 Notarization"
read -r -p "  Apple ID email for the developer account: " APPLE_ID
read -r -s -p "  App-specific password (xxxx-xxxx-xxxx-xxxx): " NOTARY_PASSWORD; echo
xcrun notarytool history --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$NOTARY_PASSWORD" >/dev/null 2>&1 \
    || fail "Apple rejected that Apple ID and app-specific password for team $TEAM_ID"
ok "Apple accepted the notarization login"

if $DRY_RUN; then
    echo "5/5 Upload: skipped (--dry-run). Everything checks out; run again without --dry-run."
    exit 0
fi

echo "5/5 Upload to $REPO, environment $ENVIRONMENT"
gh api -X PUT "repos/$REPO/environments/$ENVIRONMENT" >/dev/null
put() { gh secret set "$1" --repo "$REPO" --env "$ENVIRONMENT" >/dev/null && ok "$1"; }
base64 -i "$P12" | put MAC_DEVELOPER_ID_P12
printf '%s' "$P12_PASSWORD"   | put MAC_DEVELOPER_ID_P12_PASSWORD
printf '%s' "$APPLE_ID"       | put MAC_NOTARY_APPLE_ID
printf '%s' "$NOTARY_PASSWORD" | put MAC_NOTARY_PASSWORD
put MAC_SPARKLE_ED_KEY < "$TMP/sparkle.key"

echo
gh secret list --repo "$REPO" --env "$ENVIRONMENT"
echo
echo "Done. Delete the exported certificate now: rm \"$P12\""
