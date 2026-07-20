# Release runbook — Sanduhr für Claude (.NET 10 / WPF)

> **Recurring per-release runbook** for the .NET rebuild. Merges RORORO's
> `docs/store/release-playbook.md` (the dual-channel Velopack + MSIX loop, the 4th-component-`.0`
> rule, draft-release discipline, the "What's new" Store-listing step) with Sanduhr's
> [`ms-store-submission-playbook.md`](ms-store-submission-playbook.md) (the 10.1.4.4 hard-won
> lessons: trademark disclaimer, unique-value, navigation/accessibility). Read both before a
> first submission; this doc is the loop you run every release.

> **Two distribution channels per release, one version:**
> 1. **Microsoft Store** — unsigned MSIX (`windows-dotnet/dist/Sanduhr-Store-v<v>.msix`), Partner
>    Center re-signs after upload. Reuses the existing listing under identity
>    `626LabsLLC.SanduhrfrClaude`.
> 2. **GitHub Release** — Velopack `sanduhr-win-Setup.exe` + delta `.nupkg` + `releases.win.json`
>    (the in-app `UpdateChecker` auto-update channel).
>
> Both ship from the same tag. The .NET rebuild's first Store submission (**v3.0.0.0**) replaces
> the Python build in the SAME listing — same Identity Name and Publisher CN, so it's an update,
> not a new app.

---

## Preconditions

- **Working tree clean on `feat/dotnet-rebuild`** (or `main` once merged); CI green.
- **No `Sanduhr.exe` running.** A running instance holds locks on the DLLs; `dotnet publish` fails
  with "file is being used by another process." Quit from the tray (or
  `Get-Process Sanduhr | Stop-Process -Force`) first.
- **Windows 10/11 SDK installed** (`makeappx.exe` + `signtool.exe`):
  `winget install Microsoft.WindowsSDK.10.0.26100`.
- **`vpk` installed:** `dotnet tool install -g vpk`.
- **`gh` CLI authenticated.** On this box it's at `C:\Program Files\GitHub CLI\gh.exe` (not on the
  harness PATH — invoke via full path). Verify: `& "C:\Program Files\GitHub CLI\gh.exe" auth status`.
- **Branded Store tiles in place.** `src/Sanduhr.App/Package/Logos/` currently holds
  **placeholders** generated from the app icon. **Before any Store submission, replace them with
  branded art via the `626labs-design` skill** (Square150x150 / Square44x44 / Wide310x150 / splash
  on the `#0f182b` navy field, 626 cyan→magenta). See `Package/Logos/README.md`. Pattern (x):
  never ship programmatic placeholders to the Store.

---

## Phase 1 — Pick the version

**Format:** `MAJOR.MINOR.BUILD.0` — four components, **the 4th MUST be `0`** for the Store. A
non-zero revision is rejected at upload validation:

> "Apps are not allowed to have a Version with a revision number other than zero specified in the
> app manifest."

`build-msix.ps1` and `build-velopack-release.ps1` both hard-fail on a non-`.0` version, but it's on
you to pick correctly.

| Change type | Bump | Example |
|---|---|---|
| Platform shift / breaking, user-visible feature | MINOR (3rd→0, 4th=0) | 3.0.0.0 → 3.1.0.0 |
| Bug fix, small UX, schema-compatible | BUILD (3rd, 4th=0) | 3.1.0.0 → 3.1.1.0 |
| **Resubmission after Store rejection** | BUILD (3rd, 4th=0) | 3.1.1.0 → 3.1.2.0 (never `.1` revision) |

**The .NET rebuild's first release is `3.0.0.0`** — 3.x signals the Python→.NET platform shift over
the shipped 2.x Python line.

**Where the version lives — keep in lockstep:**

1. `windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj` — `<Version>`, `<AssemblyVersion>`,
   `<FileVersion>` (About box + assembly).
2. `windows-dotnet/src/Sanduhr.App/Package.appxmanifest` — `<Identity Version>` (the committed
   source of truth; `build-msix.ps1` patches only the staged copy at pack time, so bump this here
   too).
3. `docs/store/reviewer-letter-<v>.md` — the per-version Notes-for-certification letter (you write
   this, modeled on `reviewer-letter-3.0.0.0.md`).

---

## Phase 2 — Write the reviewer letter + release notes

1. **Reviewer letter** — copy `docs/store/reviewer-letter-3.0.0.0.md` to
   `reviewer-letter-<v>.md`. Lead with whatever this release changes on the **disclosure surface**
   (new outbound endpoint, new at-rest data, new capability). If nothing changed there, say so
   explicitly. Always carry the trademark disclaimer and the no-telemetry contract. This text is
   reviewer-only (Partner Center → Submission options → Notes for certification).
2. **Release notes** (GitHub + clan-facing) — short, second person, sentence case, no jargon. Lead
   with the download link, then "What changed" as outcomes the user feels. Verify each carried-over
   "known issue" still applies (`git log <prev-tag>..HEAD` + a 60-second smoke) before re-pasting —
   stale known-issues are a recurring miss.

---

## Phase 3 — Build the Store MSIX (unsigned)

One command. Self-contained (bundles the .NET 10 Desktop Runtime → larger MSIX, installs on a
fresh Win11 box without a separate runtime install).

```powershell
powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Store -Version 3.0.0.0
```

- Runs the **logo gate** (fails fast on missing/stub tiles; warns that present tiles may still be
  placeholders — confirm branded art first).
- Publishes → stages publish output + version-patched manifest + logos → `makeappx pack`.
- Output: `windows-dotnet/dist/Sanduhr-Store-v3.0.0.0.msix` (**unsigned** — Partner Center signs).
- Leaves the committed manifest untouched (only the staged copy is version-patched).

Optional local install test (sideload): build a self-signed flavor whose cert subject CN matches
the manifest Publisher, import the cert into Trusted People, then `Add-AppxPackage`:

```powershell
powershell -ExecutionPolicy Bypass -File windows-dotnet/scripts/build-msix.ps1 -Sideload -Version 3.0.0.0 -CertPath dev-cert.pfx -CertPassword '<pwd>'
```

---

## Phase 4 — Build the Velopack release (GitHub channel)

```powershell
pwsh windows-dotnet/scripts/build-velopack-release.ps1 -Version 3.0.0.0
```

For release #2 and later, pull the prior release's `*-full.nupkg` into `dist/release/` first
(`vpk download github --repo <url> --outputDir windows-dotnet/dist/release`) and pass `-NoClean`
so `vpk` can compute a delta.

Output (`windows-dotnet/dist/release/`): `626Labs.Sanduhr-win-Setup.exe`,
`626Labs.Sanduhr-<v>-full.nupkg`, `626Labs.Sanduhr-<v>-delta.nupkg` (#2+),
`626Labs.Sanduhr-win-Portable.zip`, `releases.win.json`. (The `626Labs.` prefix is the
Velopack packId — see the gotcha log before ever touching it.)

---

## Phase 5 — Commit, tag, push

```powershell
git add windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj `
        windows-dotnet/src/Sanduhr.App/Package.appxmanifest `
        docs/store/reviewer-letter-<v>.md
git commit -m "chore(release): bump version to <v>"
git tag v<v>
git push origin <branch>
git push origin v<v>
```

If CI builds the Velopack release on tag push (a `release.yml` like RORORO's), let it draft the
GitHub Release. **Draft, not published** — a botched build can't go live silently. Otherwise build
locally (Phase 4) and draft manually.

---

## Phase 6 — Finalize the GitHub Release

```powershell
$gh = "C:\Program Files\GitHub CLI\gh.exe"
$repo = 'estevanhernandez-stack-ed/Sanduhr_f-r_Claude'

# Upload EVERY file from dist/release/ (the auto-updater needs releases.win.json + *-full.nupkg,
# not just Setup.exe).
& $gh release upload v<v> (Get-ChildItem windows-dotnet/dist/release/* | ForEach-Object FullName) --repo $repo

& $gh release edit v<v> --notes-file docs/store/release-notes-<v>.md --repo $repo
& $gh release edit v<v> --draft=false --latest --repo $repo
```

Auto-update rolls out within ~24h of publishing — existing installs poll `releases.win.json` and
pick up the delta.

---

## Phase 7 — Microsoft Store submission

1. Partner Center → Apps → **Sanduhr für Claude** → **Packages**.
2. Drag `windows-dotnet/dist/Sanduhr-Store-v<v>.msix` into the Packages slot. Wait for upload +
   validation. If validation rejects on version (4th component non-zero), bump and re-run Phase 3.
3. **Notes for certification** — paste the `---` block from `docs/store/reviewer-letter-<v>.md`
   (reviewer-only; leads with the disclosure-surface change + the trademark disclaimer).
4. **Store listing → "What's new in this version"** — paste the public update note.
   **DO NOT SKIP — this is the public field, separate from Notes for certification (step 3).** Both
   must be filled every release.
   Also sync **Store listing → Product features** from
   [`store/product-features.md`](store/product-features.md) (the tracked source of truth —
   20-bullet cap, plain text only; update the file first, then paste).
5. Confirm the 10.1.4.4 surfaces are still clean (the three sub-clauses, below).
6. **Submit.** Status: *In submission* → *Certification* → *Publishing* (success) or *Failed*.
   Turnaround: typically 2-3 hours (observed across 3.1-3.3 submissions), occasionally overnight depending on submission time; plan for 24-72h worst case. A pending submission is replaced by the new one (no duplicates).

### The 10.1.4.4 acceptance tests (from the Python build's two rejections)

Treat each as a separate gate — losing any one kicks the submission back. Full detail in
[`ms-store-submission-playbook.md`](ms-store-submission-playbook.md).

- **(a) Content** — trademark disclaimer on every user-visible surface that names Claude/claude.ai
  (Store description, Copyright field, manifest `<Description>` ✓, privacy policy, README, About
  box). No Anthropic logos/marks in Store assets. Specific copyright entity, not bare "© 2026".
- **(b) Unique lasting value** — the cert-load-bearing features must be live: burn-rate projection
  + advanced pacing, the **deep-work focus hourglass** (rebuilt branded-glass view, item 10), the
  **cooldown snake game**, five themes + user-JSON themes + the AI theme-prompt, Win11 Mica glass,
  OS-native credential storage. A single static view fails here (the Python v2.0.1 rejection).
- **(c) Navigation / accessibility** — every top-level action on a visible tool strip (not
  hidden right-click only); accessible names on every button; tooltips on non-obvious controls;
  keyboard shortcuts documented in-app; native close button; **every dialog legible on light-mode
  Windows** (the QSS-cascade bug that bit the Python build — re-verify the WPF dialogs render on a
  light-mode host).

---

## Phase 8 — Announce

After the GitHub Release is live (post-`--draft=false` only): post highlights + the `Setup.exe`
link where the audience lives. Store users update automatically — no announcement needed.

---

## Deferred — winget

A winget manifest pointing at the GitHub `Setup.exe` is a future channel (gives
`winget install Sanduhr`). Not in scope for v3.0.0.0; revisit once the Store + GitHub channels are
both proven on the .NET build.

---

## Gotcha log

| Symptom | Root cause | Fix |
|---|---|---|
| `dotnet publish` errors on a locked DLL | `Sanduhr.exe` is running | Quit from tray, re-run |
| Store validation: "revision number other than zero" | 4th version component ≠ 0 | Bump the 3rd component; the scripts hard-fail on this |
| `signtool` `0x8007000b` on sideload | Manifest Publisher CN ≠ the dev cert's subject | Temp-patch the manifest Publisher to the dev cert CN, build, restore (or use a cert whose CN matches) |
| MSIX launch fails "framework missing" on a fresh box | Published framework-dependent, not self-contained | `build-msix.ps1` already passes `--self-contained true`; don't flip it off |
| Store rejects with trademark complaint | Disclaimer missing on a required surface | Re-check all six surfaces in 10.1.4.4(a) above |
| `vpk` "not found" | Global tool not installed | `dotnet tool install -g vpk` |
| Auto-updater never picks up a release | Only `Setup.exe` uploaded | Upload EVERY file from `dist/release/` (esp. `releases.win.json` + `*-full.nupkg`) |
| Store tiles look generic / off-brand | Shipping the placeholder logos | Run `626labs-design` for the tile set before submit (Package/Logos/README.md) |
| Velopack install DESTROYS `%LOCALAPPDATA%\Sanduhr` contents (the vault) | packId `Sanduhr` made the install dir collide with the app's data dir — the installer rollback-renames a pre-existing dir and deletes it on success (verified live 2026-07-13) | packId is `626Labs.Sanduhr` (install tree disjoint from data tree). NEVER revert — the packId froze when v3.2.0.0 published; changing it orphans every installed updater |

---

## Reference

- [`ms-store-submission-playbook.md`](ms-store-submission-playbook.md) — the 10.1.4.4 deep dive +
  Notes-to-Publisher template (the original Python-build playbook).
- [`store/reviewer-letter-3.0.0.0.md`](store/reviewer-letter-3.0.0.0.md) — the v3.0.0.0
  Notes-for-certification letter (model new ones after this).
- [`scripts/build-msix.ps1`](../windows-dotnet/scripts/build-msix.ps1) — Phase 3.
- [`scripts/build-velopack-release.ps1`](../windows-dotnet/scripts/build-velopack-release.ps1) —
  Phase 4.
- [`scripts/generate-store-assets.ps1`](../windows-dotnet/scripts/generate-store-assets.ps1) —
  placeholder tile generator (replace output via `626labs-design` before submit).
- `src/Sanduhr.App/Package/Logos/README.md` — the design-skill handoff for branded tiles.
