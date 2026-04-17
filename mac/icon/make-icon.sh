#!/usr/bin/env bash
# Generate Sanduhr.icns from icon/generate.swift.
# Produces: icon/Sanduhr.icns (the artifact build.sh copies into Sanduhr.app)
set -euo pipefail
cd "$(dirname "$0")"

MASTER="master-1024.png"
ICONSET="Sanduhr.iconset"
OUTPUT="Sanduhr.icns"

echo "→ Rendering master @ 1024×1024..."
swift generate.swift "$MASTER" >/dev/null

echo "→ Downsampling to iconset..."
rm -rf "$ICONSET" "$OUTPUT"
mkdir -p "$ICONSET"

# Apple's expected filenames + sizes. `sips` ships with macOS.
declare -a SIZES=(16 32 32 64 128 256 256 512 512 1024)
declare -a NAMES=(
    "icon_16x16.png"    "icon_16x16@2x.png"
    "icon_32x32.png"    "icon_32x32@2x.png"
    "icon_128x128.png"  "icon_128x128@2x.png"
    "icon_256x256.png"  "icon_256x256@2x.png"
    "icon_512x512.png"  "icon_512x512@2x.png"
)

for i in "${!SIZES[@]}"; do
    sips -z "${SIZES[$i]}" "${SIZES[$i]}" "$MASTER" \
        --out "$ICONSET/${NAMES[$i]}" >/dev/null
done

echo "→ Packing $OUTPUT..."
iconutil -c icns "$ICONSET" -o "$OUTPUT"

# Cleanup: keep the master and icns, drop the iconset.
rm -rf "$ICONSET" "$MASTER"

echo "✓ $OUTPUT"
