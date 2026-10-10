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
   DMG URL live.
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
