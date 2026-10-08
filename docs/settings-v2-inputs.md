# Settings v2 inputs

Collected while writing `docs/setup-guide.md` against 2.10.0 (2026-10-07). Each is a place where a person following the guide would stumble. The fresh-agent pass adds to this list; Settings v2 answers it.

1. **The notch island's click** says it opens "these settings" (Notch), but opens Settings wherever it was left (`Desk/NotchView.swift`, `showSettings()` with no page).
2. **One switch, two labels, two places.** The notch: "Notch: the island around the camera (needs Desk)" in General vs "Extend the camera notch" on Notch. The Desk: only switchable in General; Layout only says "Turn on Desk to arrange it on the desktop."
3. **The menu bar choice has two names:** "Menu Bar Shows" in the menu vs "Percent beside the hourglass" in General.
4. **"Desk Layout Settings…" opens a page titled "Layout."**
5. **Watchers live on three pages:** switches in Integrations ("Watcher Settings…" opens it), placement on Notch and Layout.
6. **Integrations' order contradicts its captions:** "install it for a folder below" sits below the folders; Add Folder… is at the very bottom, away from the folder list.
7. **The meters mod installs from two pages:** Integrations' "Meters above the prompt" row and the Mods page's switch, Update and Remove.
8. **One glow, several names:** "Notch glow when Claude needs you", "the notch glow hooks", "Notch, Glow" vs the section "Glow for Claude Code".
9. **The Message editor's "Look"** is named in a tooltip, but the controls are Color, Glow, Size, Letters and Motion.
10. **Edit-mode buttons are uneven:** "Edit as text…" (opens no window) vs "Edit as a list".
11. **Pinning differs by mode:** a pin per row in the list vs a separate field in text mode.
12. **The `Mon:` reference ignores the Mix switch** ("instead of every-day lines").
13. **Same command, two spellings:** "Save & Apply" vs "Save and Apply"; "Check Now" vs "Check for Updates…".
14. **Two Look pages share an icon** (`textformat`); fonts are split across Desk Look and Widget Look.
15. **Meters sits under Desk** but its warnings change the widget too.
16. **Claude meters naming is spread out:** General's "Show the Claude meters on the desktop" vs Layout's "Claude line" and "Claude meters (bars)".
17. **Camera and mic placement has two controls:** the wing's "Camera and mic" choice vs "Beside the camera".
18. **Option+S only works while the Desk is on**, yet it's the advertised way into Settings.
19. **The Watchers caption says "a Desk corner"**; Layout has eight places now.
20. **No in-app reset:** a full reset needs Remove Account, deleting a folder and two `defaults delete` commands.
