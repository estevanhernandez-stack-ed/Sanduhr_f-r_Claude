# Mac release

Mac releases are built, signed and notarized on GitHub (`.github/workflows/mac-release.yml`).
The signing material lives in two places: the repo's `mac-release` environment, where only this
workflow can read it, and 1Password, which is the master copy. A Mac's Keychain is a working copy
at most; a reinstall wipes it.

| 1Password item | Holds |
|---|---|
| `Sanduhr Mac / Sparkle Private Key` | Sparkle EdDSA private key (base64), the one that **cannot** be replaced |
| `Sanduhr Mac / Developer ID p12` | the exported .p12 and its password (same cert as RORORO) |
| `Sanduhr Mac / Notary app-specific password` | Apple ID email and the `Sanduhr GitHub notarization` app-specific password |

The workflow never publishes. It leaves a **draft** release and a **pull request**; you publish
the draft, then merge the pull request, and only that merge offers the update to users.

| Secret (environment `mac-release`) | What it is |
|---|---|
| `MAC_DEVELOPER_ID_P12` | Developer ID Application certificate and private key, base64 |
| `MAC_DEVELOPER_ID_P12_PASSWORD` | the password set when exporting it |
| `MAC_NOTARY_APPLE_ID` | Apple ID of the developer account |
| `MAC_NOTARY_PASSWORD` | an app-specific password for notarization |
| `MAC_SPARKLE_ED_KEY` | Sparkle's EdDSA private key; its public half is `SUPublicEDKey` in `mac/Info.plist` |

## Part A. One-time setup, on the old Mac

About 20 minutes. Use the same macOS user account that built 2.0.4: the Sparkle key and the
certificate are in that user's login Keychain.

### A1. Get the repo onto the old Mac

The script ships on `feat/mac-desk` (after it is pushed) and later on `main`.

If the old Mac already has a clone:

```
cd ~/path/to/Sanduhr_f-r_Claude
git fetch origin
git checkout feat/mac-desk        # or main, once merged
git pull
```

If not:

```
cd ~/Projects
git clone https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude.git
cd Sanduhr_f-r_Claude
git checkout feat/mac-desk        # or main, once merged
```

Every command below runs from the repo root.

### A2. Tools

```
xcrun notarytool --version        # if this fails: xcode-select --install, then retry
brew install gh                   # skip if `gh --version` works
gh auth login                     # GitHub.com, HTTPS, log in with a browser
gh repo view estevanhernandez-stack-ed/Sanduhr_f-r_Claude   # must print the repo
```

The GitHub account needs admin rights on the repo (yours does) to create the environment and its
secrets.

### A3. Confirm this is the right Mac

The certificate:

```
security find-identity -v -p codesigning
```

One line must read `Developer ID Application: Estevan Hernandez (82BSR56X5J)`. If it is
missing, the certificate is on another Mac, or it has expired. A new one can be made at
developer.apple.com, Certificates, "+", Developer ID Application. Any valid Developer ID
certificate for team 82BSR56X5J works; it does not have to be the one 2.0.4 was signed with.

The Sparkle key, which **cannot** be replaced:

```
cd mac
swift package resolve
.build/artifacts/sparkle/Sparkle/bin/generate_keys -p
cd ..
```

macOS asks whether `generate_keys` may use a Keychain item: click **Allow**. It must print
exactly:

```
b5hOsbZVS61d3/wAN8hxf70HvfH3SOpuNVZES3QvhpI=
```

If it prints something else, or "No existing signing key found", **stop here**. Every 2.0.4
install only accepts updates signed with the key behind that public key. Without it, 2.0.4 users
cannot auto-update to 2.1.0 and need a manual download. Look on any other Mac you used before
deciding.

### A4. Export the certificate as a .p12

Use Keychain Access, not `openssl`: macOS's `security import` cannot read the format current
OpenSSL writes.

1. Open **Keychain Access** (Spotlight: "Keychain Access").
2. In the sidebar pick the **login** keychain, then the **My Certificates** tab at the top.
3. Find **Developer ID Application: Estevan Hernandez (82BSR56X5J)**. Click its disclosure
   triangle: a private key must be nested under it. (No triangle, no key: this Mac has only
   the certificate. Go back to A3.)
4. Right-click the **certificate** row (not the key), then **Export "Developer ID Application…"**.
5. File Format: **Personal Information Exchange (.p12)**. Save as `DeveloperID.p12` on the Desktop.
6. Set a strong password when asked. You type it once more in A6, then never again.
7. macOS asks for your login password to allow the export. Enter it and click **Allow**.

### A5. Make an app-specific password for notarization

1. Go to https://account.apple.com (appleid.apple.com), sign in with the developer Apple ID.
2. **Sign-In and Security**, **App-Specific Passwords**, **+**.
3. Name it `Sanduhr GitHub notarization`. Copy the `xxxx-xxxx-xxxx-xxxx` it shows.

This is a fresh password on purpose. The old one is sealed in the `sanduhr-notary` Keychain
profile, and a new one can be revoked on its own later without breaking local releases.

### A6. Dry run, then upload

The dry run checks everything and uploads nothing:

```
mac/scripts/export-release-secrets.sh --dry-run ~/Desktop/DeveloperID.p12
```

It asks for the .p12 password, the Apple ID and the app-specific password (typing is hidden for
both passwords), and prints a ✓ for each of:

1. gh signed in and sees the repo, notarytool present
2. Sparkle public key matches `Info.plist` (click **Allow** again if the Keychain asks)
3. the .p12 opens and holds the Developer ID identity with its private key
4. Apple accepts the notarization login

Any ✗ names the problem; fix it and run the dry run again. When all four pass:

```
mac/scripts/export-release-secrets.sh ~/Desktop/DeveloperID.p12
```

Same prompts, then it creates the `mac-release` environment and lists the five secrets.

### A7. Clean up the old Mac

```
rm ~/Desktop/DeveloperID.p12
```

The script already deleted its own temp copies. Make sure all three 1Password items above exist;
they, not any Mac's Keychain, are the master copy.

### After a reinstall (or on a new Mac)

macOS keeps an old login keychain it can no longer open as
`~/Library/Keychains/login_renamed_N.keychain-db`. In October 2026 the Sparkle key and the
Developer ID identity were recovered from one of these. Unlock it with the Mac password from
before the reset (`security unlock-keychain <path>`), then:

- **Sparkle key:** `security find-generic-password -s "https://sparkle-project.org" -a ed25519 -w <path> > /tmp/sk`,
  `generate_keys -f /tmp/sk`, check `generate_keys -p` prints the `SUPublicEDKey` above, `rm /tmp/sk`.
  Or import straight from the 1Password copy the same way.
- **Certificate:** Keychain Access, File, Add Keychain, pick the renamed file, then export as in A4.

### A8. Two GitHub settings (from any browser)

1. **Settings, Actions, General, Workflow permissions**: tick **Allow GitHub Actions to create
   and approve pull requests**. Without it the run still works, but you open the appcast pull
   request by hand from the pushed `release/mac-<version>` branch.
2. Recommended: **Settings, Environments, mac-release**: under Deployment protection rules add
   yourself as a **Required reviewer**, and under Deployment branches choose **Selected
   branches**, adding `main`. Each release run then waits for your click before it can touch the
   secrets.

## Part B. Cutting a release (any Mac, or the browser)

1. **Merge to main.** The workflow refuses any other branch. Mac CI must be green.
2. **Versions** in `mac/Info.plist`: `CFBundleShortVersionString` is the release (2.1.0), and
   `CFBundleVersion` must be above every `sparkle:version` in `docs/appcast.xml` (2.0.4 is 2,
   so 2.1.0 is 3). Sparkle orders updates by the build number; the workflow checks it.
3. **Run it.** Actions, **Mac Release**, Run workflow, branch `main`, version `2.1.0`. Or:
   `gh workflow run mac-release.yml --ref main -f version=2.1.0`.
   About 10 to 20 minutes, most of it Apple's notary.
4. **What it leaves:** a draft release `v2.1.0-mac` with `Sanduhr-2.1.0.dmg` and the
   CHANGELOG's "Unreleased (mac)" section as notes; a pull request `release/mac-2.1.0` with the
   appcast entry and the cask's version and SHA-256; the DMG as a run artifact.
5. **Check the DMG** (download from the draft or the run): it opens with no Gatekeeper warning,
   and `lipo -archs /Volumes/Sanduhr/Sanduhr.app/Contents/MacOS/Sanduhr` prints `x86_64 arm64`.
6. **Publish the draft** (edit the notes first if you like). This creates the tag and makes the
   DMG URL live. Then confirm the release's tag is `v2.1.0-mac`: a publish from the GitHub UI
   can land on a placeholder (`untagged-…`) if the tag field was touched, and the appcast and the
   cask point at `releases/download/v2.1.0-mac/`. `gh release view v2.1.0-mac` must answer; if it
   does not, set the tag on the release (`gh api -X PATCH repos/<owner>/<repo>/releases/<id>
   -f tag_name=v2.1.0-mac -f target_commitish=<the release commit>`) before step 7.
7. **Merge the pull request.** GitHub Pages republishes `docs/appcast.xml`; Sparkle clients see
   2.1.0 on their next daily check.
8. **Accept:** on a Mac running 2.0.4, menu, Check for Updates offers 2.1.0, installs and
   relaunches; `lipo -archs /Applications/Sanduhr.app/Contents/MacOS/Sanduhr` prints
   `x86_64 arm64`.
9. **Homebrew:** copy `docs/distribution/sanduhr.rb` to `Casks/sanduhr.rb` in
   [estevanhernandez-stack-ed/homebrew-tap](https://github.com/estevanhernandez-stack-ed/homebrew-tap)
   (the two files are the same apart from version and sha256, which the workflow sets), run
   `brew style Casks/sanduhr.rb`, commit `sanduhr <version>` and push. Check with
   `brew tap estevanhernandez-stack-ed/tap && brew fetch --cask estevanhernandez-stack-ed/tap/sanduhr`.
10. **CHANGELOG:** rename "Unreleased (mac)" to `v2.1.0-mac — <date>` and start a new empty one.

Order matters: merging the pull request before publishing the draft points users at a DMG URL
that is still a 404.

## Troubleshooting

- **Notarization rejected:** the run log prints the submission ID. On a Mac with the
  credentials: `xcrun notarytool log <id> --apple-id <id> --team-id 82BSR56X5J --password <pw>`.
- **"Release v… already exists":** delete the draft (and the tag if one was made), then rerun.
- **"build N is not above the feed's highest build":** bump `CFBundleVersion`.
- **DMG layout warning ("AppleScript layout step failed"):** harmless; the DMG works without
  the icon positions.
- **Rotating a secret:** run A4 to A7 again; the script overwrites the five secrets. Update the
  matching 1Password item in the same sitting.
- **Local fallback:** `mac/release.sh <version>` on the old Mac does the same with its Keychain
  (see the script header), then the release and appcast steps by hand.

## Rolling back a release

Rollback means something different on each channel, and none of them moves a Mac backwards:

- **Sparkle** orders updates by build number and never offers a lower one (`mac/scripts/appcast_add.py`
  refuses a lower build for the same reason). A Mac that installed 2.26.0 stays on 2.26.0 until a
  higher build ships. For those users the only rollback is a patch release (next section).
- **The DMG** is one file at one URL, named by the appcast's `<enclosure>` and by the cask's `url`.
  Remove it and both channels stop installing at once.
- **Homebrew** installs whatever `Casks/sanduhr.rb` in the tap says at install time, so the tap
  needs its own revert.

Steps, in the order that stops new installs fastest. Example: pulling 2.26.0 (build 36), the
previous release being 2.25.1.

1. **Pull the DMG off the release.** Instant. A Sparkle client that is offered 2.26.0 now fails
   at download instead of installing it; `brew install` fails on the 404 the same way. The run's
   artifact keeps a copy of the DMG for the investigation (`gh run download <run id>`).

   ```
   gh release delete-asset v2.26.0-mac Sanduhr-2.26.0.dmg --yes
   ```

2. **Mark the release.** Pre-release keeps the page and the notes and takes the release out of
   the Latest slot and out of the cask's `livecheck` (`strategy :github_latest`):

   ```
   gh release edit v2.26.0-mac --prerelease
   gh release edit v2.25.1-mac --latest
   ```

   Or delete it, without `--cleanup-tag`:

   ```
   gh release delete v2.26.0-mac --yes
   gh release edit v2.25.1-mac --latest
   ```

   Re-mark the previous Mac release Latest either way. Left to itself GitHub picks Latest by
   version number, and a Windows `v3.x` release takes the slot (`docs/release-runbook.md`,
   gotcha log).

3. **Take the item out of the appcast.** By hand: delete the whole `<item>` block for 2.26.0 from
   `docs/appcast.xml` (nothing in the repo removes an item; `mac/scripts/appcast_add.py` only
   adds), then a pull request to `main`:

   ```
   git checkout -b release/mac-pull-2.26.0
   git add docs/appcast.xml
   git commit -m "release(mac): pull 2.26.0 from the appcast"
   git push -u origin release/mac-pull-2.26.0
   gh pr create --base main --title "release(mac): pull 2.26.0 from the appcast" --body "Removes the 2.26.0 item. The DMG is already off the release."
   ```

   GitHub Pages republishes the feed after the merge. Sparkle checks once a day
   (`SUScheduledCheckInterval` is 86400 in `mac/Info.plist`), so a Mac that checked just before
   the merge can still be offered 2.26.0 for up to 24 hours; with step 1 done that offer fails at
   download. Check for Updates… reads the feed at once.

   The pulled build number stays burnt: the next release's `CFBundleVersion` must be above 36,
   not above the feed's new highest (35). The workflow only checks against the feed, so it would
   let 36 through, and Sparkle on every 2.26.0 Mac would never offer it.

4. **Revert the tap.** In a clone of `estevanhernandez-stack-ed/homebrew-tap` (the tap repo, not
   this one):

   ```
   git log --oneline -3                    # find the "sanduhr 2.26.0" commit
   git revert <that sha>
   brew style Casks/sanduhr.rb
   git push
   brew update
   brew fetch --cask estevanhernandez-stack-ed/tap/sanduhr    # downloads 2.25.1 again
   ```

5. **Leave the tag.** `v2.26.0-mac` names the commit the bad DMG was built from (the workflow's
   `--target`); the investigation and the patch start there, and the Macs still running 2.26.0
   report that version. Never force-push or delete a published tag, and never reuse the version:
   a second 2.26.0 would be two different builds under one number, and build 36 is gone from the
   Macs that have it anyway. The fix is 2.26.1.

6. **Write it down.** A line under "Unreleased (mac)" in `CHANGELOG.md` saying 2.26.0 was pulled
   and why; it ships in the patch's notes.

## A bad update in the wild

**Who notices.** Nothing phones home: the app sends no telemetry and Sparkle reports nothing
back, so there is no crash count and no install count. The first signal is one of three: your
own Macs (they take the same update at their next daily check, so keep one on the normal
cadence), a GitHub issue, or someone telling you. For the day after a release:

```
gh issue list --state open
```

**The first ten minutes.**

1. **Which build.** About shows the version and build; confirm it is the release build, not a
   local one (by hand). A Sparkle-updated app, a dragged DMG and a `brew` install all end up as
   `/Applications/Sanduhr.app`.
2. **Reproduce with what users got.** The DMG on the release page, not a local build:

   ```
   gh release download v2.26.0-mac --pattern 'Sanduhr-2.26.0.dmg' --dir <an empty folder>
   open <that folder>/Sanduhr-2.26.0.dmg
   ```

   If the asset is already pulled, the run artifact has it: `gh run download <run id>`.
3. **Read the crash.** A crash of a Sparkle-updated app lands where every crash lands:

   ```
   open ~/Library/Logs/DiagnosticReports
   ```

   The file is `Sanduhr-<date>.ips` (Console, Crash Reports, shows the same). For a misbehaviour
   without a crash, `open -a Console` and filter on subsystem `com.626labs.sanduhr`, as
   `docs/mac-smoke-test.md` does. From a reporter, ask for the `.ips` file, their macOS version
   and whether the update came from Sparkle, the DMG or `brew`.
4. **Decide: pull or hotfix.** Pull (previous section) when the app does not launch, loses or
   corrupts the vault or settings, or the fix is not obvious within the hour. Hotfix when the
   bug is contained and you can ship today. Pulling first and hotfixing after is the usual
   shape: pulling costs nothing (nobody is downgraded), the hotfix takes the notary's 10 to 20
   minutes plus your review.

**The hotfix path** is the normal release with a patch version.

1. Fix on a branch off `main`; `mac/test.sh` passes.
2. `CHANGELOG.md`, under "Unreleased (mac)": a `**Fixed: …**` line that names the bug and the
   version that introduced it.
3. `mac/Info.plist`: `CFBundleShortVersionString` 2.26.1, `CFBundleVersion` 37 (above the
   pulled build, see the previous section).
4. Merge to `main`, then Part B steps 3 to 10 with the patch version:

   ```
   gh workflow run mac-release.yml --ref main -f version=2.26.1
   ```

5. Release notes: the workflow drafts them from the CHANGELOG section. Before publishing, edit
   the draft so the fix is the first line and names the bad version ("2.26.0 crashed at launch
   when …").
6. Macs on 2.26.0 get 2.26.1 at their next daily check, or at once from Check for Updates….

**What to tell users.**

- The patch's release notes: first line, the bug, the version it was in.
- The GitHub issue: reply when it reproduces, reply again with the patch version when the draft
  is published and the appcast is merged, then close it.
- If 2.26.0's page stays up as a pre-release, its notes say so (this replaces the notes, so
  keep the old text in the command):

  ```
  gh release edit v2.26.0-mac --notes "Pulled: <the bug>. Update to 2.26.1."
  ```

## After a release

Nothing in the workflow runs after the draft and the pull request; every check here is by hand.
Example: 2.26.0.

- [ ] **The run is green** (by hand):

  ```
  gh run list --workflow mac-release.yml --limit 1
  gh run view <run id>
  ```

- [ ] **The draft is published** and holds the Latest slot (by hand):

  ```
  gh release view v2.26.0-mac --json isDraft,isPrerelease,isLatest,assets
  ```

  `isDraft` false, `isPrerelease` false, `isLatest` true, one asset `Sanduhr-2.26.0.dmg`. Note
  its `size`.

- [ ] **The appcast pull request is merged** and `docs/appcast.xml` on `main` carries the version
  (by hand):

  ```
  gh pr list --state merged --head release/mac-2.26.0
  git fetch origin
  git grep -n -A 6 '<title>2.26.0</title>' origin/main -- docs/appcast.xml
  ```

  The item is first in the feed, `sparkle:version` is the build from `mac/Info.plist`,
  `sparkle:edSignature` is non-empty and `length` equals the asset `size` above. Nothing prints
  the signature for comparison; only Sparkle checks it, two items down.

- [ ] **The tap commit `sanduhr 2.26.0` is pushed** (by hand, in the tap clone: `git log -1`),
  and Homebrew serves the new DMG:

  ```
  brew update
  brew fetch --cask estevanhernandez-stack-ed/tap/sanduhr
  ```

  `brew fetch` prints the path of the downloaded `Sanduhr-2.26.0.dmg` and verifies the cask's
  `sha256` itself; a mismatch fails here.

- [ ] **The DMG's SHA-256 equals the cask's** (by hand), for the DMG from the release page as well
  as the one `brew fetch` downloaded:

  ```
  gh release download v2.26.0-mac --pattern 'Sanduhr-2.26.0.dmg' --dir <an empty folder>
  shasum -a 256 <that folder>/Sanduhr-2.26.0.dmg
  ```

  The hash is the `sha256` line of `docs/distribution/sanduhr.rb` on `main` and of
  `Casks/sanduhr.rb` in the tap, and the pull request body's "DMG SHA-256".

- [ ] **A Sparkle-updated Mac shows What's New** (by hand): on a Mac running the previous
  version, Check for Updates… offers 2.26.0, installs and relaunches (Sparkle verified the
  EdDSA signature, or it would have refused). After the relaunch the What's New window opens
  with "New in 2.26.0" when the release has a card in
  `mac/Sources/Sanduhr/Models/WhatsNewCards.swift`; a release without a card of its own shows
  nothing automatically, and About's What's New… lists every card.

- [ ] **CHANGELOG.md** has "Unreleased (mac)" renamed to `v2.26.0-mac — <date>` and a new empty
  one above it (by hand, Part B step 10). Skipping this is the common miss: the next run's draft
  notes are cut from "Unreleased (mac)" by the workflow, so a stale heading ships the previous
  release's notes again.
