## #gdev

**Sanduhr für Claude** — v2.0 shipped. Native desktop widget for claude.ai subscription usage.

Started as a single Python file on Wednesday night to stop myself from refreshing claude.ai/settings every 20 minutes. Spent the next couple days rebuilding the whole thing native on both platforms:

- **Mac** — SwiftUI, NSVisualEffectView vibrancy, Keychain, Sparkle auto-updates, Developer ID signed + Apple-notarized (no Gatekeeper warning).
- **Windows** — PySide6, real Win11 Mica glass via DWM API, Windows Credential Manager, Inno Setup installer. MSIX submitted to Microsoft Store — currently in review.
- **Themes** — 5 hand-tuned + unlimited user-authored JSON themes. There's an agent prompt in the repo: hand Claude a reference image, get back a drop-in theme file.
- **Pacing engine** — burn-rate projection ("hits 100% in ~4h 22m at current pace") + pace markers + 2h sparklines per tier.

No Electron, no WebView, no telemetry. One network destination: `claude.ai`, using your own session cookie. Credentials in the OS-native store, cleared on uninstall.

🌐 https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/
📦 https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude

MIT. Built by 626 Labs. The v1 single-file Python version is still in the repo for folks who want to run from source.

— Este / 626Labs

---

## #devpost

**Sanduhr für Claude** — native desktop widget for pacing your claude.ai usage. v2.0 out now.

The pitch: if you pay for Claude Pro/Max, your limits reset on a rolling window. You don't find out you're running dry until you hit the wall mid-thought. Sanduhr puts that info on your desktop — burn-rate projection, pace markers, sparklines, countdown to reset.

Started as a 30-minute Python/tkinter hack on Wednesday night. Three days later it's a native Mac app (SwiftUI, notarized, Sparkle updates) and a native Windows app (PySide6, Win11 Mica glass, MSIX in Microsoft Store review right now). Credentials live in Keychain / Credential Manager — never plaintext. No telemetry, no analytics. One network destination: claude.ai.

Five hand-tuned glass themes plus a JSON theme system — drop a `.json` into your themes folder and it shows up in the picker. There's even an agent prompt so you can have Claude generate themes from reference images.

🌐 Landing: https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/
📦 Source: https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude

MIT license. Independent — not affiliated with Anthropic.

— Este / 626Labs
