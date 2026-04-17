#!/usr/bin/env bash
# Build Sanduhr.app from the Swift package.
# Usage: ./build.sh               # release build, auto-detects universal vs native
#        ./build.sh --debug       # debug build, native arch (fastest iteration)
#        ./build.sh --universal   # force universal (requires full Xcode)
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

# Universal builds need full Xcode (provides xcbuild). Auto-detect.
if [[ "$CONFIG" == "release" ]] && [[ "$UNIVERSAL" == false ]]; then
    XCODE_PATH="$(xcode-select -p 2>/dev/null || true)"
    if [[ -n "$XCODE_PATH" && -x "$XCODE_PATH/../SharedFrameworks/XCBuild.framework/Versions/A/Support/xcbuild" ]]; then
        UNIVERSAL=true
    fi
fi

ARCH_FLAGS=()
if $UNIVERSAL; then
    ARCH_FLAGS=(--arch arm64 --arch x86_64)
    echo "→ Building ($CONFIG, universal)..."
else
    echo "→ Building ($CONFIG, native arch)..."
fi

swift build -c "$CONFIG" "${ARCH_FLAGS[@]}"

# Universal builds land in .build/apple/Products/<Config>;
# single-arch builds land in .build/<triple>/<config>.
if $UNIVERSAL && [[ "$CONFIG" == "release" ]]; then
    BIN=".build/apple/Products/Release/Sanduhr"
else
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

    echo "→ Signing main app: $SIGN_IDENTITY"
    codesign --force \
        --sign "$SIGN_IDENTITY" \
        --options runtime \
        --timestamp \
        "$APP"
fi
codesign --verify --strict --verbose=2 "$APP"

echo "✓ Built $APP"
echo "  Run:      open $APP"
echo "  Install:  mv $APP /Applications/"
