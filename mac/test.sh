#!/usr/bin/env bash
# Run the Swift tests. Usage: ./test.sh [swift test arguments]
# With full Xcode this is plain `swift test`. With only the command line tools it needs the same
# 26.x SDK as build.sh (the newest SDK lacks the SwiftUI macro plugins), and swift-testing's own
# macro plugin passed by path, which SwiftPM does not find by itself there.
set -euo pipefail
cd "$(dirname "$0")"

XCODE_PATH="$(xcode-select -p 2>/dev/null || true)"
EXTRA=()
if [[ ! -x "$XCODE_PATH/../SharedFrameworks/XCBuild.framework/Versions/A/Support/xcbuild" ]]; then
    if [[ -z "${SDKROOT:-}" ]]; then
        for sdk in /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.sdk; do
            [[ -d "$sdk" ]] && { export SDKROOT="$sdk"; echo "→ Using $(basename "$sdk")"; break; }
        done
    fi
    PLUGINS="$XCODE_PATH/usr/lib/swift/host/plugins/testing"
    [[ -d "$PLUGINS" ]] && EXTRA=(-Xswiftc -plugin-path -Xswiftc "$PLUGINS")
fi

swift test ${EXTRA[@]+"${EXTRA[@]}"} "$@"
