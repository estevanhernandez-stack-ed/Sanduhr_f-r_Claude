# Windows Release Test Plan

Run this checklist on a clean Windows 11 VM before every tagged release.
Estimated time: 20 minutes.

## Prerequisites

- Windows 11 22H2+ VM with no prior Sanduhr install
- A valid `claude.ai` sessionKey
- Network access to claude.ai

## 1. Fresh install

- [ ] Double-click `Sanduhr-Setup-vX.Y.Z.exe`
- [ ] SmartScreen warns -> click "More info" -> "Run anyway"
- [ ] Installer wizard appears with Sanduhr banner
- [ ] "Start Menu shortcut" is checked by default; "Desktop" and "Launch at login" are unchecked
- [ ] Install completes without errors
- [ ] Sanduhr launches automatically (post-install task)
- [ ] First-run setup dialog appears, walks through the F12 -> Cookies flow
- [ ] Paste sessionKey -> "OK" -> widget shows usage bars within ~5 seconds

## 2. v1 upgrade

- [ ] On a VM with v1 `sanduhr.py` previously run: confirm `~/.claude-usage-widget/config.json` exists
- [ ] Install v2 over the top
- [ ] Launch -- widget shows data immediately (no re-auth)
- [ ] Theme preference from v1 is preserved
- [ ] `%APPDATA%\Sanduhr\history.json` exists and contains the v1 data
- [ ] Old `~/.claude-usage-widget/config.json` was deleted (history kept)

## 3. Theme rendering

For each theme (Obsidian, Aurora, Ember, Mint, Matrix):

- [ ] Click the theme button in the title-bar strip
- [ ] Compare to reference screenshot in `docs/screenshots/windows/<theme>.png`
- [ ] Glass themes (Obsidian/Aurora/Ember/Mint): desktop wallpaper visible through Mica backdrop
- [ ] Matrix: solid dark green-black background (no Mica bleed-through)
- [ ] Matrix: percentages in Cascadia Code with soft green glow

## 4. Mica / Win10 fallback

- [ ] On Win11 22H2+ with a colorful desktop wallpaper: translucent backdrop visible
- [ ] On Win10 or unsupported build: solid theme `bg` color, no crash, no visual glitches

## 5. Controls

- [ ] Click **Pin** in title bar -- always-on-top toggles
- [ ] Double-click title bar -- compact mode toggles
- [ ] Click **Key** -- credentials dialog opens, current sessionKey + cf_clearance pre-populated
- [ ] Click **Refresh** -- status shows "Refreshing..." briefly, then updates
- [ ] Right-click anywhere on widget -- context menu shows Refresh / Compact / Credentials / Quit
- [ ] Click and drag anywhere on widget -- widget moves
- [ ] Close, reopen -- widget appears at last-saved position

## 6. Session expiry

- [ ] Paste a deliberately wrong sessionKey -> status becomes "Session expired -- click Key."
- [ ] Paste a valid key -> widget recovers within 5 seconds

## 7. Cloudflare block

- [ ] Temporarily block claude.ai via `%SystemRoot%\System32\drivers\etc\hosts`: `127.0.0.1 claude.ai`
- [ ] Trigger Refresh -> status becomes "Cloudflare -- add cf_clearance (click Key)."
- [ ] Click Key -> credentials dialog opens, cf_clearance field focused
- [ ] Restore hosts; refresh -- widget recovers

## 8. Autostart

- [ ] Uninstall. Reinstall with "Launch at login" checked.
- [ ] Sign out, sign back in -- Sanduhr launches automatically
- [ ] Uninstall with autostart previously enabled -- Run registry entry is removed

## 9. Uninstall

- [ ] Start -> Apps -> Sanduhr für Claude -> Uninstall
- [ ] "Also remove my settings and history" checkbox is unchecked
- [ ] Uninstall with checkbox UNCHECKED -> install dir gone, `%APPDATA%\Sanduhr\` preserved
- [ ] Uninstall again (after reinstalling) with checkbox CHECKED -> `%APPDATA%\Sanduhr\` also gone
- [ ] Credential Manager entries (service `com.626labs.sanduhr`) removed in both cases
- [ ] Start Menu entry gone; Run registry entry (if ticked) gone

## 10. Exit criteria

All boxes above must be checked before tagging a release.
