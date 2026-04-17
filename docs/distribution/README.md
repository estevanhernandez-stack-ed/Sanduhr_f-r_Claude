# Distribution manifests

Canonical package manifests for getting Sanduhr into the package managers
people actually use.

## Homebrew (macOS)

File: [`sanduhr.rb`](./sanduhr.rb)

### When to submit

Submit after you cut a **Mac release tag** with a notarized DMG attached.
Rough sequence:

1. From `mac/`, run `./release.sh <version>` (e.g. `./release.sh 2.0.0`). This
   signs, notarizes, staples, and produces a DMG in `releases/`.
2. Tag the commit: `git tag v2.0.0-mac && git push origin v2.0.0-mac`.
3. Create a GitHub release for the tag and attach the DMG.
4. Compute SHA-256: `shasum -a 256 releases/Sanduhr-2.0.0.dmg`.
5. Update `sanduhr.rb` — change `version` and the `sha256` line.
6. Submit to homebrew-cask (below).

### Submission flow

```bash
# Fork + clone Homebrew/homebrew-cask
gh repo fork Homebrew/homebrew-cask --clone
cd homebrew-cask

# Copy the formula in
cp /path/to/Sanduhr_f-r_Claude/docs/distribution/sanduhr.rb Casks/s/sanduhr.rb

# Lint + audit
brew style --fix Casks/s/sanduhr.rb
brew audit --new --cask Casks/s/sanduhr.rb

# Commit, push, PR
git checkout -b add-sanduhr
git add Casks/s/sanduhr.rb
git commit -m "Add Sanduhr v2.0.0"
git push origin add-sanduhr
gh pr create --repo Homebrew/homebrew-cask
```

Maintainers typically merge new casks within a few days as long as the DMG
is signed + notarized (it is) and the formula passes `brew audit`.

### After approval

Users install with:

```
brew install --cask sanduhr
```

Updates flow through Sparkle inside the app (the `auto_updates true` line
tells Homebrew not to manage upgrades itself — Sanduhr's built-in Sparkle
handles them).

## Windows (winget / Microsoft Store)

**winget:** submission to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
requires a signed MSIX. Deferred until the MS Store submission clears (Store
ingestion signs the MSIX with MS's publisher cert), at which point a winget
manifest pointing at the Store listing is trivial.

**Microsoft Store:** in review as of 2026-04-17. Partner Center product
identity: `626LabsLLC.SanduhrfrClaude`.

## Scoop / Chocolatey (Windows)

Same constraint as winget — defer until signing is in place. Scoop's bucket
submission process is lower-friction than winget and may be worth doing in
parallel once the MSIX is signed.
