#!/usr/bin/env bash
# Build Sanduhr.app from the Swift package.
# Usage: ./build.sh               # release build, always universal
#        ./build.sh --debug       # debug build, native arch (fastest iteration)
#        ./build.sh --universal   # force universal (Apple silicon + Intel)
# Release builds are always universal. With full Xcode, SwiftPM builds both slices in one
# go; with only the command line tools, each slice is built on its own and joined with lipo.
set -euo pipefail
cd "$(dirname "$0")"

CONFIG="release"
UNIVERSAL=false
case "${1:-}" in
    --debug)     CONFIG="debug" ;;
    --universal) UNIVERSAL=true ;;
    "")          ;;
    *) echo "Unknown flag: $1" >&2; exit 2 ;;
esac

# Release builds ship to other Macs, so they are always universal: v2.0.4 went out Intel-only
# and ran under Rosetta on Apple silicon.
[[ "$CONFIG" == "release" ]] && UNIVERSAL=true

XCODE_PATH="$(xcode-select -p 2>/dev/null || true)"
HAVE_XCBUILD=false
[[ -n "$XCODE_PATH" && -x "$XCODE_PATH/../SharedFrameworks/XCBuild.framework/Versions/A/Support/xcbuild" ]] && HAVE_XCBUILD=true

# Without full Xcode, the newest SDK lacks the SwiftUI macro plugins; use a 26.x SDK.
if ! $HAVE_XCBUILD && [[ -z "${SDKROOT:-}" ]]; then
    for sdk in /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.sdk; do
        [[ -d "$sdk" ]] && { export SDKROOT="$sdk"; echo "→ Using $(basename "$sdk")"; break; }
    done
fi

if $UNIVERSAL && $HAVE_XCBUILD; then
    echo "→ Building ($CONFIG, universal via Xcode)..."
    swift build -c "$CONFIG" --arch arm64 --arch x86_64
    # Ask for the path: it moved from .build/apple to .build/out between SwiftPM versions.
    BIN="$(swift build -c "$CONFIG" --arch arm64 --arch x86_64 --show-bin-path)/Sanduhr"
elif $UNIVERSAL; then
    echo "→ Building ($CONFIG, universal: arm64 then x86_64, joined with lipo)..."
    # Newer SwiftPM gives both triples the same bin path (.build/out/Products/Release), so the
    # second build overwrites the first: copy each slice out as soon as it is built.
    mkdir -p .build/universal
    for arch in arm64 x86_64; do
        swift build -c "$CONFIG" --triple "$arch-apple-macosx14.0"
        SLICE=".build/universal/Sanduhr-$arch"
        cp "$(swift build -c "$CONFIG" --triple "$arch-apple-macosx14.0" --show-bin-path)/Sanduhr" "$SLICE"
        [[ "$(lipo -archs "$SLICE")" == "$arch" ]] || { echo "✗ $SLICE is $(lipo -archs "$SLICE"), not $arch" >&2; exit 1; }
    done
    BIN=".build/universal/Sanduhr"
    lipo -create .build/universal/Sanduhr-arm64 .build/universal/Sanduhr-x86_64 -output "$BIN"
else
    echo "→ Building ($CONFIG, native arch)..."
    swift build -c "$CONFIG"
    BIN="$(swift build -c "$CONFIG" --show-bin-path)/Sanduhr"
fi

if [[ ! -f "$BIN" ]]; then
    echo "✗ Built binary not found at $BIN" >&2
    exit 1
fi

APP="Sanduhr.app"
echo "→ Assembling $APP..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Sanduhr"
cp Info.plist "$APP/Contents/Info.plist"

# Icon: generate if missing, then copy into Resources.
if [[ ! -f icon/Sanduhr.icns ]]; then
    echo "→ Generating icon..."
    (cd icon && ./make-icon.sh >/dev/null)
fi
cp icon/Sanduhr.icns "$APP/Contents/Resources/Sanduhr.icns"

# The Claude Code integration scripts (item 49). Settings, Integrations copies them to
# ~/Library/Application Support/Sanduhr/integrations/<stamp>/, so an app update refreshes them.
# Plain files (not executable): python3 runs them, and the signature seals them as resources.
echo "→ Bundling the integration scripts..."
mkdir -p "$APP/Contents/Resources/integrations"
for script in sanduhr_mcp.py sanduhr_statusline.py; do
    install -m 0644 "integrations/$script" "$APP/Contents/Resources/integrations/$script"
done
# The meters mod for Claude Code (item 50), copied beside the scripts into the same stamped
# folder. Its tests and the types Claude Code writes into a mod folder it loads stay behind.
rm -rf "$APP/Contents/Resources/integrations/mods"
MOD="integrations/mods/sanduhr-meters"
while IFS= read -r file; do
    install -d "$APP/Contents/Resources/$(dirname "$MOD/$file")"
    install -m 0644 "$MOD/$file" "$APP/Contents/Resources/$MOD/$file"
done < <(cd "$MOD" && find . -type f ! -name '*.test.ts' ! -name '*.test.tsx' ! -name '.DS_Store' \
    ! -name '.gitignore' ! -path './.claude-plugin/types/*' | sed 's|^\./||' | sort)

# Embed Sparkle.framework so the app can self-update.
SPARKLE_FRAMEWORK=".build/artifacts/sparkle/Sparkle/Sparkle.xcframework/macos-arm64_x86_64/Sparkle.framework"
if [[ ! -d "$SPARKLE_FRAMEWORK" ]]; then
    echo "✗ Sparkle.framework not found — run 'swift package resolve' first" >&2
    exit 1
fi
echo "→ Embedding Sparkle.framework..."
mkdir -p "$APP/Contents/Frameworks"
rm -rf "$APP/Contents/Frameworks/Sparkle.framework"
cp -R "$SPARKLE_FRAMEWORK" "$APP/Contents/Frameworks/"

# Now playing (item 53): the vendored mediaremote-adapter (BSD-3). Sanduhr runs its Perl script
# with /usr/bin/perl, which loads the framework: MediaRemote answers Apple's own binaries only.
# Framework in Frameworks/, the test client (a tiny executable the adapter's `test` starts) in
# Helpers/, the script as a sealed resource. Always universal: it is small and builds in seconds.
echo "→ Building the now playing adapter..."
ADAPTER_OUT=".build/mediaremote-adapter"
./scripts/build-mediaremote-adapter.sh "$ADAPTER_OUT"
rm -rf "$APP/Contents/Frameworks/MediaRemoteAdapter.framework"
cp -R "$ADAPTER_OUT/MediaRemoteAdapter.framework" "$APP/Contents/Frameworks/"
mkdir -p "$APP/Contents/Helpers" "$APP/Contents/Resources/NowPlaying"
install -m 0755 "$ADAPTER_OUT/MediaRemoteAdapterTestClient" "$APP/Contents/Helpers/MediaRemoteAdapterTestClient"
install -m 0644 Vendor/mediaremote-adapter/bin/mediaremote-adapter.pl "$APP/Contents/Resources/NowPlaying/mediaremote-adapter.pl"
# The third-party notices (Sparkle, mediaremote-adapter), opened from Settings, About.
install -m 0644 THIRD-PARTY-NOTICES.txt "$APP/Contents/Resources/THIRD-PARTY-NOTICES.txt"

# Release builds get a Developer ID signature + hardened runtime so they can
# be notarized and run anywhere. Debug builds get an ad-hoc signature for
# local iteration only. Override with SIGN_IDENTITY=<id> or SIGN_IDENTITY=-
# (ad-hoc).
if [[ "$CONFIG" == "debug" ]]; then
    SIGN_IDENTITY="${SIGN_IDENTITY:--}"
else
    SIGN_IDENTITY="${SIGN_IDENTITY:-Developer ID Application: Estevan Hernandez (82BSR56X5J)}"
fi

if [[ "$SIGN_IDENTITY" == "-" ]]; then
    echo "→ Ad-hoc signing (dev build — not distributable)..."
    codesign --force --deep --sign - "$APP" >/dev/null
else
    # Sparkle embeds helpers (Autoupdate, Updater.app, XPC services) that each
    # need their own hardened-runtime signature. --deep gets the order wrong
    # for notarization — sign inside-out explicitly.
    echo "→ Signing Sparkle internals..."
    SP="$APP/Contents/Frameworks/Sparkle.framework/Versions/B"
    for helper in \
        "$SP/XPCServices/Downloader.xpc" \
        "$SP/XPCServices/Installer.xpc" \
        "$SP/Autoupdate" \
        "$SP/Updater.app"; do
        [[ -e "$helper" ]] && codesign --force \
            --sign "$SIGN_IDENTITY" \
            --options runtime \
            --timestamp \
            "$helper"
    done
    codesign --force \
        --sign "$SIGN_IDENTITY" \
        --options runtime \
        --timestamp \
        "$APP/Contents/Frameworks/Sparkle.framework"

    # The now playing adapter, inside-out like Sparkle: the helper, then the framework.
    echo "→ Signing the now playing adapter..."
    for nested in \
        "$APP/Contents/Helpers/MediaRemoteAdapterTestClient" \
        "$APP/Contents/Frameworks/MediaRemoteAdapter.framework"; do
        codesign --force \
            --sign "$SIGN_IDENTITY" \
            --options runtime \
            --timestamp \
            "$nested"
    done

    # The one entitlement: Apple Events, for Settings, Desk, Now Playing's "Ask Music and Spotify
    # directly" (hardened runtime refuses them without it; macOS still asks the user first).
    echo "→ Signing main app: $SIGN_IDENTITY"
    codesign --force \
        --sign "$SIGN_IDENTITY" \
        --options runtime \
        --timestamp \
        --entitlements Sanduhr.entitlements \
        "$APP"
fi
codesign --verify --strict --verbose=2 "$APP"

if $UNIVERSAL; then
    ARCHS="$(lipo -archs "$APP/Contents/MacOS/Sanduhr")"
    [[ "$ARCHS" == *arm64* && "$ARCHS" == *x86_64* ]] || { echo "✗ Expected arm64 and x86_64, got: $ARCHS" >&2; exit 1; }
    echo "→ Architectures: $ARCHS"
fi
# The adapter is universal in every build (the script checks it too); check what got bundled.
for bin in "$APP/Contents/Frameworks/MediaRemoteAdapter.framework/Versions/A/MediaRemoteAdapter" \
           "$APP/Contents/Helpers/MediaRemoteAdapterTestClient"; do
    ARCHS="$(lipo -archs "$bin")"
    [[ "$ARCHS" == *arm64* && "$ARCHS" == *x86_64* ]] || { echo "✗ $bin is $ARCHS, expected arm64 and x86_64" >&2; exit 1; }
done
echo "✓ Built $APP"
echo "  Run:      open $APP"
echo "  Install:  mv $APP /Applications/"
