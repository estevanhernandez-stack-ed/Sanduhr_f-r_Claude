## Sanduhr für Claude 3.9.1 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.9.1/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.9, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. A 3.7.x Setup.exe install needs this Setup.exe once. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's fixed

- **Widget packs now do what they said.** Themes, palettes and fonts from the packs you install show in the theme list, the Desk's Ink presets and the Desk and message fonts. They installed before, but nothing used them. A palette shows as its name and the pack's; the font lists update when Settings reopens.
- **Removing the theme in use switches to Obsidian at once**, including when you delete the theme file you have selected. Your saved choice is not rewritten, so a pack that can't be read for a moment doesn't change it.
- **A pack font Sanduhr can't read is left out** of the font lists instead of failing when the Desk draws with it.
- **Theme Studio saves a copy of a pack's theme.** Starting from one offers a file name that works, and Save no longer replaces one of your own themes you didn't start from.

Effects packs install and preview, but nothing draws them yet.

This patch changes nothing about what Sanduhr reads or sends. The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v391--2026-10-24)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
