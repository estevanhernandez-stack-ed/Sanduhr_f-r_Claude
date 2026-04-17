#!/usr/bin/env bash
# Package Sanduhr.app into a drag-to-Applications disk image.
# Produces Sanduhr.dmg in the project root.
#
# Usage:
#   ./make-dmg.sh              # build app if needed, then bundle the DMG
#   ./make-dmg.sh --skip-build # reuse an existing Sanduhr.app
set -euo pipefail
cd "$(dirname "$0")"

APP="Sanduhr.app"
VOL_NAME="Sanduhr"
DMG_NAME="Sanduhr.dmg"
STAGE="dmg-stage"
TMP_DMG="dmg-rw.dmg"
SKIP_BUILD=false

if [[ "${1:-}" == "--skip-build" ]]; then SKIP_BUILD=true; fi

if [[ "$SKIP_BUILD" == false || ! -d "$APP" ]]; then
    echo "→ Building $APP..."
    ./build.sh
fi

if [[ ! -d "$APP" ]]; then
    echo "✗ $APP not found after build" >&2
    exit 1
fi

echo "→ Staging DMG contents..."
rm -rf "$STAGE" "$TMP_DMG" "$DMG_NAME"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
# The symlink is what makes "drag to install" work — dragging onto it is
# equivalent to dropping the app into /Applications.
ln -s /Applications "$STAGE/Applications"

# Size the read-write image with plenty of headroom (app is ~1 MB; 32 MB is safe).
echo "→ Creating read-write image..."
hdiutil create -volname "$VOL_NAME" \
    -srcfolder "$STAGE" \
    -fs HFS+ \
    -format UDRW \
    -size 32m \
    "$TMP_DMG" >/dev/null

echo "→ Mounting to arrange icons..."
# Attach without -nobrowse: Finder needs the volume visible in its world so
# AppleScript can address `disk "$VOL_NAME"`. We'll unmount cleanly at the end.
ATTACH_OUTPUT="$(hdiutil attach "$TMP_DMG" -readwrite -noautoopen)"
MOUNT_DIR="$(echo "$ATTACH_OUTPUT" | grep -Eo '/Volumes/[^ ]+' | head -1)"

if [[ -z "$MOUNT_DIR" || ! -d "$MOUNT_DIR" ]]; then
    echo "✗ Could not determine mount point" >&2
    exit 1
fi

# Always unmount — even if AppleScript fails — so the temp DMG isn't left attached.
cleanup_mount() { hdiutil detach "$MOUNT_DIR" -quiet 2>/dev/null || hdiutil detach "$MOUNT_DIR" -force >/dev/null 2>&1 || true; }
trap cleanup_mount EXIT

# Give Finder a beat to notice the mount.
sleep 2

osascript <<APPLESCRIPT || echo "⚠ AppleScript layout step failed — DMG will still work, just without custom icon positions."
tell application "Finder"
    tell disk "$VOL_NAME"
        open
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set the bounds of container window to {200, 160, 760, 500}
        set viewOptions to the icon view options of container window
        set arrangement of viewOptions to not arranged
        set icon size of viewOptions to 128
        set text size of viewOptions to 12
        set label position of viewOptions to bottom
        set position of item "$APP" of container window to {150, 170}
        set position of item "Applications" of container window to {410, 170}
        close
        open
        update without registering applications
        delay 1
        close
    end tell
end tell
APPLESCRIPT

# Finder needs a moment to flush the view prefs (.DS_Store) to disk.
sync
sleep 2

echo "→ Unmounting..."
cleanup_mount
trap - EXIT

echo "→ Compressing final DMG..."
hdiutil convert "$TMP_DMG" \
    -format UDZO \
    -imagekey zlib-level=9 \
    -o "$DMG_NAME" >/dev/null

# Sign the DMG with Developer ID so it can be notarized as its own artifact.
# (The app inside is signed separately by build.sh.) Skip if SIGN_IDENTITY=-.
SIGN_IDENTITY="${SIGN_IDENTITY:-Developer ID Application: Estevan Hernandez (82BSR56X5J)}"
if [[ "$SIGN_IDENTITY" != "-" ]]; then
    echo "→ Signing DMG: $SIGN_IDENTITY"
    codesign --force --sign "$SIGN_IDENTITY" --timestamp "$DMG_NAME"
fi

echo "→ Cleaning up..."
rm -rf "$STAGE" "$TMP_DMG"

SIZE=$(du -h "$DMG_NAME" | awk '{print $1}')
echo "✓ $DMG_NAME ($SIZE)"
echo "  Test: open $DMG_NAME"
