<p align="center">
  <img src="docs/images/icon-512.png" width="160" alt="Sanduhr icon">
</p>

<h1 align="center">Sanduhr für Claude</h1>

<p align="center"><em>Hourglass for Claude — pace yourself on claude.ai.</em></p>

<p align="center">
A native desktop widget that turns your Claude.ai subscription usage into
something you can actually pace yourself by — burn-rate projection, pace
markers, sparkline trends, and five hand-tuned glass themes.
</p>

<p align="center">
  <a href="https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/estevanhernandez-stack-ed/Sanduhr_f-r_Claude?label=release"></a>
  <a href="https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/estevanhernandez-stack-ed/Sanduhr_f-r_Claude?style=flat"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-4ade80"></a>
  <a href="https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/"><img alt="Landing page" src="https://img.shields.io/badge/landing-page-3bb4d9"></a>
</p>

<p align="center">
  <strong><a href="https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/">🌐 Landing page</a></strong> ·
  <strong>macOS</strong> · <strong>Windows 11</strong> · <strong>Python (any OS)</strong>
</p>

<p align="center">
<sub>Independent third-party tool. Not affiliated with Anthropic. Requires an active Claude Pro / Team / Enterprise subscription.</sub>
</p>

---

## Install

### macOS — native SwiftUI

Download the latest **`Sanduhr.dmg`** from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases) and drag to Applications.

- Developer ID signed + Apple-notarized → no Gatekeeper warnings.
- NSVisualEffectView vibrancy, Keychain-backed credentials.
- Auto-updates via [Sparkle](https://sparkle-project.org) (24h check interval).
- Homebrew cask manifest at [`docs/distribution/sanduhr.rb`](docs/distribution/sanduhr.rb); submission to `homebrew-cask` pending a tagged Mac release.
- Requires **macOS 11 (Big Sur)** or newer.

### Windows 11 — native PySide6

Download **`Sanduhr-Setup-vX.Y.Z.exe`** from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases), click through the SmartScreen "unknown publisher" warning (signing deferred per [threat model](docs/generated/threat-model.md)), and run the installer.

- Win11 Mica glass backdrop (Win10 falls back to solid theme color).
- Windows Credential Manager storage (service `com.626labs.sanduhr`, matching the macOS Keychain).
- Full source under [`windows/`](windows/).
- **MSIX submission to Microsoft Store is in review** — Store approval eliminates the SmartScreen prompt and unlocks `winget install sanduhr`.

### Cross-platform — Python / tkinter (v1)

```bash
python sanduhr.py
```

Single-file tkinter app with auto-installing `cloudscraper` dep. Works on macOS, Windows, and Linux. Preserved at repo root for users who prefer running from source. Requires Python 3.8+.

---

## Features

### Pacing

- **Burn-rate projection** — "At current pace, expires in 3d 21h" warns before you run dry.
- **Always-on pace ghost** — a vertical tick on every bar showing where pace says usage should be right now. Real fill sits to the left (under pace), at (on pace), or to the right (ahead). No math required.
- **Advanced pacing metrics** — hover any tier card to reveal **Cooldown required** (how long at zero usage to get back on pace) and **Surplus** (burn-rate delta when under pace).
- **Horizon sparkline** — classic Heer/Tufte 4-band horizon over the last 2 hours. Peaks stack into dense dark regions, lulls wash soft. More information per pixel than a line chart, toggle via the 📊 button.
- **Breathing glass** — bars pulse softly toward each theme's accent color. Subliminal, not flickery.

### Focus

- **Deep-work focus timer** — swap the tier cards for a digitised 31×31 pixel hourglass that drains in real time. Inline minute picker, zero external deps.
- **Cooldown snake game** — pure-Qt/pure-SwiftUI snake for when you've burned through your budget and need to kill a few minutes. Persistent high score.

### Chrome & themes

- **Five hand-tuned themes** — Obsidian, Aurora, Ember, Mint, Matrix — plus unlimited **user-authored JSON themes** via Settings → Themes or by dropping a `.json` into your platform's themes folder.
- **AI-agent theme prompt** ([`docs/themes/AGENT_PROMPT.md`](docs/themes/AGENT_PROMPT.md)) — hand any chat agent a reference image or vibe description and get back a drop-in theme JSON.
- **Win11 Mica glass / macOS NSVisualEffectView** — real native vibrancy, no Electron, no WebView.
- **Edge-drag resize** — hover any edge or corner, cursor changes, click-drag. Minimum bounds track your font metrics so text never clips. New geometry persists across launches.

### Privacy & control

- **OS-native credential storage** — Windows Credential Manager / macOS Keychain. Cleared on uninstall. Never plaintext.
- **One-click sign-out** — Settings → Credentials → save with an empty sessionKey. Confirmation dialog, then credentials are wiped from the OS store.
- **Drag-anywhere, pin/unpin, compact mode, full keyboard shortcuts** (`Ctrl+R`, `Ctrl+,`, `Ctrl+D`, `Ctrl+H`).
- **No telemetry, no analytics, no ads.** One network destination: `claude.ai`, using your own session cookie. See [SECURITY.md](SECURITY.md) for why no data ever comes back to us.

---

## Themes

<table>
  <tr>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-obsidian.png" width="260"><br><strong>Obsidian</strong><br><sub>deep black · purple accent</sub></td>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-aurora.png" width="260"><br><strong>Aurora</strong><br><sub>dark blue · cyan glow</sub></td>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-ember.png" width="260"><br><strong>Ember</strong><br><sub>dark red · orange warmth</sub></td>
  </tr>
  <tr>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-mint.png" width="260"><br><strong>Mint</strong><br><sub>dark green · teal glass</sub></td>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-626-labs.png" width="260"><br><strong>626 Labs</strong><br><sub>navy · cyan · magenta</sub></td>
    <td align="center" width="33%"><img src="docs/images/screenshots/theme-matrix.png" width="260"><br><strong>Matrix</strong><br><sub>phosphor · CRT corners</sub></td>
  </tr>
</table>

Drop a custom theme JSON into `~/Library/Application Support/Sanduhr/themes/` (macOS) or `%APPDATA%\Sanduhr\themes\` (Windows) and it appears in the theme strip on next launch. Template + prompt at [`docs/themes/`](docs/themes/).

---

## First-run setup

1. Go to [claude.ai](https://claude.ai) and sign in.
2. Open DevTools (`⌥⌘I` on macOS, `F12` on Windows).
3. Navigate to **Application → Cookies → claude.ai**.
4. Copy the value of the `sessionKey` cookie.
5. Paste it into Sanduhr's first-launch dialog.

Sanduhr hits two `claude.ai` endpoints — the same ones the settings page uses — to read your usage, and stores the cookie in your platform's native secure credential store (Keychain / Credential Manager). Nothing else leaves your machine.

**Key expired?** `sessionKey` cookies expire when you log out or after extended inactivity. Paste a fresh one in Settings → Credentials.

---

## Controls

| Action | Effect |
|--------|--------|
| 🎨 Theme | Open theme picker menu |
| ⚙ Settings | Credentials · Themes · Pacing · Help tabs |
| 📊 Graph | Cycle sparkline: Classic / Horizon |
| ↕ Compact | Toggle compact mode (`Ctrl+D`) |
| ⏳ Focus | Swap tier cards for the deep-work hourglass |
| 🐍 Snake | Play the cooldown snake game |
| Refresh | Force a data refresh (`Ctrl+R`) |
| Pin / Unpin | Toggle always-on-top |
| Drag anywhere | Reposition the widget |
| Drag any edge or corner | Resize the widget |
| Double-click anywhere | Toggle compact mode |
| Right-click | Refresh / Compact / Settings / Quit |
| × | Close Sanduhr |

Full keybindings documented in the in-app **Settings → Help** tab.

---

## Docs

- [Landing page](https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/)
- [Privacy policy](docs/PRIVACY.md)
- [Runbook](docs/generated/runbook.md) — release, hotfix, rollback
- [ADRs](docs/generated/adr.md) — nine architecture decision records
- [Threat model](docs/generated/threat-model.md) — assets, controls, ratings
- [Deployment procedure](docs/generated/deployment-procedure.md)
- [Changelog](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md) — how to report vulnerabilities, and why no data ever comes back to us

---

## Roadmap

### Shipped in v2.0.4

- [x] Pace ghost (always-on pace position tick on every bar)
- [x] Horizon sparkline (replaces pulse histogram)
- [x] Breathing glass (subliminal accent pulse)
- [x] Edge-drag resize with dynamic minimum bounds
- [x] Deep-work focus timer with digitised hourglass
- [x] Cooldown snake game
- [x] Advanced pacing metrics (Cooldown required, Surplus)
- [x] One-click sign-out from Settings

### Up next

- [ ] Microsoft Store listing live (in review)
- [ ] Homebrew cask submission (pending first tagged Mac release)
- [ ] winget manifest (pending MS Store cert)
- [ ] Historical usage dashboard with CSV export
- [ ] Auto-start on boot (native builds)
- [ ] Antigravity (Google Gemini IDE) quota tracking
- [ ] Official Anthropic read-only usage endpoint support (pending Anthropic response)

---

## Why "Sanduhr für Claude"?

*Sanduhr* (ZAHND-oor) is German for "hourglass" — Sand + Uhr (sand clock). *Für* = "for." You're watching the sand drain on your Claude usage, pacing yourself so you don't run out before the reset.

---

## License

MIT — do whatever you want with it. Built by [626 Labs LLC](https://626labs.dev) ([@626Labs-LLC on GitHub](https://github.com/626Labs-LLC)).

<p align="center">
  <sub>
    "Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively to describe integration. Sanduhr für Claude is an independent third-party tool.
  </sub>
</p>
