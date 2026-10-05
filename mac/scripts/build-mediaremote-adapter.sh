#!/usr/bin/env bash
# Build the vendored mediaremote-adapter (mac/Vendor/mediaremote-adapter, item 53) for Sanduhr's
# now playing: MediaRemoteAdapter.framework and MediaRemoteAdapterTestClient, universal
# (arm64 and x86_64), with plain clang, so neither CMake nor Xcode is needed.
#
# Usage: scripts/build-mediaremote-adapter.sh OUT_DIR
# Leaves OUT_DIR/MediaRemoteAdapter.framework and OUT_DIR/MediaRemoteAdapterTestClient, unsigned
# (build.sh signs them inside Sanduhr.app). The sources and flags mirror the upstream
# CMakeLists.txt; `-w` keeps upstream's warnings out of Sanduhr's build log.
set -euo pipefail

OUT="${1:?usage: build-mediaremote-adapter.sh OUT_DIR}"
SRC="$(cd "$(dirname "$0")/../Vendor/mediaremote-adapter" && pwd)"
NAME="MediaRemoteAdapter"
MIN="14.0"
ARCHS=(-arch arm64 -arch x86_64)

SOURCES=(
    src/adapter/env.m src/adapter/get.m src/adapter/globals.m src/adapter/keys.m
    src/adapter/now_playing.m src/adapter/repeat.m src/adapter/seek.m src/adapter/send.m
    src/adapter/shuffle.m src/adapter/speed.m src/adapter/stream.m src/adapter/test.m
    src/private/MediaRemote.m src/utility/Debounce.m src/utility/helpers.m
)

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
FW="$OUT/$NAME.framework"
rm -rf "$FW" "$OUT/${NAME}TestClient"
mkdir -p "$FW/Versions/A/Resources"

cd "$SRC"
# The framework: a versioned bundle, so codesign seals it like any other framework. Symbols stay
# visible: the Perl script looks them up by name.
clang -dynamiclib -O2 -w -fobjc-arc -fvisibility=default "${ARCHS[@]}" -mmacosx-version-min="$MIN" \
    -I include -I src "${SOURCES[@]}" \
    -framework Foundation -framework AppKit -framework UniformTypeIdentifiers \
    -install_name "@rpath/$NAME.framework/Versions/A/$NAME" \
    -current_version 0.1.0 -compatibility_version 0.1.0 \
    -o "$FW/Versions/A/$NAME"

cat > "$FW/Versions/A/Resources/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>English</string>
    <key>CFBundleExecutable</key>
    <string>$NAME</string>
    <key>CFBundleIdentifier</key>
    <string>com.vandenbe.$NAME</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>$NAME</string>
    <key>CFBundlePackageType</key>
    <string>FMWK</string>
    <key>CFBundleShortVersionString</key>
    <string>0.1</string>
    <key>CFBundleVersion</key>
    <string>0.1.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>$MIN</string>
</dict>
</plist>
PLIST
ln -s A "$FW/Versions/Current"
ln -s "Versions/Current/$NAME" "$FW/$NAME"
ln -s Versions/Current/Resources "$FW/Resources"

# The test client publishes a silent fake track for the adapter's `test` when nothing plays.
clang -O2 -w -fobjc-arc "${ARCHS[@]}" -mmacosx-version-min="$MIN" \
    -I src/test src/test/main.m src/test/NowPlayingTest.m \
    -framework Foundation -framework MediaPlayer \
    -o "$OUT/${NAME}TestClient"

for bin in "$FW/Versions/A/$NAME" "$OUT/${NAME}TestClient"; do
    archs="$(lipo -archs "$bin")"
    [[ "$archs" == *arm64* && "$archs" == *x86_64* ]] || { echo "✗ $bin is '$archs', expected arm64 and x86_64" >&2; exit 1; }
done
echo "→ Built $NAME.framework and ${NAME}TestClient (arm64, x86_64)"
