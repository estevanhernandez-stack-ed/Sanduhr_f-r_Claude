# Sanduhr für Claude — macOS (SwiftUI)

Native Mac rewrite of the [Python/tkinter widget](../sanduhr.py). Feature parity
plus real vibrancy, SF Pro, Keychain-backed credentials, and no dock icon.

## Build

Requires macOS 14+ and Xcode 15+ command-line tools (`xcode-select --install`).

```
./build.sh                 # release build → Sanduhr.app (universal if full Xcode present)
./build.sh --debug         # native-arch debug build (faster iteration)
./build.sh --universal     # force arm64+x86_64 (requires full Xcode)
```

Run the result:

```
open Sanduhr.app
```

## Package as a drag-install DMG

For the classic "drag the app onto Applications" experience:

```
./make-dmg.sh              # builds app if needed, produces Sanduhr.dmg
./make-dmg.sh --skip-build # reuse existing Sanduhr.app
```

`open Sanduhr.dmg` mounts a window showing the app next to an Applications
alias — drag across to install, eject the DMG, done. Uses only `hdiutil` +
`osascript`, no Homebrew or extra tools.

## First run

1. Go to <https://claude.ai>, sign in.
2. Open DevTools (⌥⌘I) → Application → Cookies → `claude.ai`.
3. Copy the `sessionKey` value.
4. In Sanduhr, click **Continue** on the onboarding sheet, then paste the key.

The key is stored in the macOS Keychain (service `com.626labs.sanduhr`,
account `sessionKey`) — not in a plaintext file like the Python version.

### Cloudflare fallback

If the widget shows "Cloudflare — add cf_clearance", copy the `cf_clearance`
cookie the same way and paste it into the second field in **Credentials…**.
Most accounts don't need this.

## Files

- `sessionKey` + `cf_clearance` → Keychain
- Selected theme → `UserDefaults` (`theme`)
- Sparkline history → `~/Library/Application Support/Sanduhr/history.json`
- Window position → `UserDefaults` (`windowFrame`)

## Controls

| Gesture                     | Action                       |
| --------------------------- | ---------------------------- |
| Drag anywhere               | Move the widget              |
| Double-click title          | Toggle compact mode          |
| Click theme name            | Switch theme                 |
| **Pin** button              | Toggle always-on-top         |
| **Refresh** button          | Fetch usage now              |
| **Key** button              | Open credentials sheet       |
| Right-click widget          | Refresh / Compact / Quit menu|
| **×**                       | Quit                         |

## License

MIT. Python original by [626 Labs LLC](https://626labs.dev).
