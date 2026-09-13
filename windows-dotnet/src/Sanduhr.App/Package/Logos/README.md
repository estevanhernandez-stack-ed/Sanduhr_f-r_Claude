# Store / MSIX logo assets

**Branded set, generated 2026-09-13 (3.4.0 release prep)** by
[`windows-dotnet/scripts/generate-store-assets.ps1`](../../../../scripts/generate-store-assets.ps1)
from two committed brand sources. No rendered text, no programmatic art: every tile is a crop and
composite of the real identity.

| Source | Feeds | How |
| --- | --- | --- |
| `docs/store-assets/logo-square-1080x1080.png` (the Sanduhr lockup: hourglass glyph + SANDUHR / FÜR CLAUDE wordmark on navy) | `StoreLogo`, `Square71x71Logo`, `Square150x150Logo`, `Square310x310Logo`, `Wide310x150Logo`, `SplashScreen` | Glyph and wordmark lines cropped by fixed pixel rectangles (measured 2026-09-13, listed in the script), the source navy `#0f1f31` color-keyed to transparency, placed on the manifest `BackgroundColor` `#0f182b`. Square tiles carry the glyph at 64% height; wide + splash carry the horizontal lockup with the cyan→magenta accent rule. |
| `docs/images/icon-512.png` (the app icon: rounded square, transparent corners, same identity as `Assets/Sanduhr.ico`) | the whole `Square44x44Logo` family (`scale-*`, `targetsize-*`, `altform-unplated`) | Transparent canvas, 8% padding at 32px and up. Windows plates the plated variants itself; `build-velopack-release.ps1` stitches the `targetsize-*` PNGs into the Velopack `AppIcon.ico`. |

The pre-3.4.0 committed set was a programmatic placeholder (icon on a navy field with Segoe UI
text, and a mojibake "fÃ¼r" on the wide/splash tiles from the script's UTF-8 text read as ANSI).
From 3.4.0 the committed set is the shipping set.

## Required files (bare names — the build-msix logo gate checks these)

| File | Base size | Used for |
| --- | --- | --- |
| `StoreLogo.png` | 50×50 | Store listing |
| `Square44x44Logo.png` | 44×44 | taskbar / alt-tab / list views |
| `Square71x71Logo.png` | 71×71 | small tile |
| `Square150x150Logo.png` | 150×150 | **medium tile (required)** |
| `Square310x310Logo.png` | 310×310 | large tile |
| `Wide310x150Logo.png` | 310×150 | wide tile (lockup) |
| `SplashScreen.png` | 620×300 | launch splash |

Each ships `scale-{100,125,150,200,400}` variants; `Square44x44Logo` additionally ships
`targetsize-{16,24,32,48,256}` (plated + `altform-unplated`). Windows auto-resolves the right
variant at runtime; the manifest references the bare names. 52 PNGs in total.

## Regenerate

Only needed when the brand art changes. The script clears every `*.png` here first, so a size
change leaves no orphans.

```powershell
powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/generate-store-assets.ps1
powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Verify
```

If `logo-square-1080x1080.png` is ever redrawn, re-measure the three crop rectangles at the top of
the script (glyph, "SANDUHR", "FÜR CLAUDE") before regenerating; they are pixel coordinates, not
detected. `AppIcon.ico` in this folder is a build output of `build-velopack-release.ps1` and is
git-ignored.
