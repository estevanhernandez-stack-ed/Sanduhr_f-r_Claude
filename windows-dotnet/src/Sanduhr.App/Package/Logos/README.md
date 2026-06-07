# Store / MSIX logo assets

> **These are PLACEHOLDERS.** Generated programmatically by
> [`windows-dotnet/scripts/generate-store-assets.ps1`](../../../../scripts/generate-store-assets.ps1)
> from `Assets/Sanduhr.ico` so the manifest references resolve and `makeappx` packs a valid
> package. **Final Store tile graphics MUST go through the `626labs-design` skill before any
> Partner Center submission.** Pattern (x): never ship programmatic placeholders to the Store —
> the reviewers and the 626 brand both notice.

## Required files (bare names — the build-msix logo gate checks these)

| File | Base size | Used for |
|---|---|---|
| `StoreLogo.png` | 50×50 | Store listing |
| `Square44x44Logo.png` | 44×44 | taskbar / alt-tab / list views |
| `Square71x71Logo.png` | 71×71 | small tile |
| `Square150x150Logo.png` | 150×150 | **medium tile (required)** |
| `Square310x310Logo.png` | 310×310 | large tile |
| `Wide310x150Logo.png` | 310×150 | wide tile (wordmark) |
| `SplashScreen.png` | 620×300 | launch splash |

Each ships `scale-{100,125,150,200,400}` variants; `Square44x44Logo` additionally ships
`targetsize-{16,24,32,48,256}` (plated + `altform-unplated`). Windows auto-resolves the right
variant at runtime; the manifest references the bare names.

## Regenerate

```powershell
powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/generate-store-assets.ps1
```

## Replace with branded art (before Store submission)

Invoke the `626labs-design` skill for the Square150x150 / Square44x44 / Wide310x150 / splash set,
using the 626 cyan→magenta on the `#0f182b` navy field. Drop the produced PNGs here at the same
bare names + scale variants, then re-run `scripts/build-msix.ps1 -Verify`.
