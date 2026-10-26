<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: lock-line-unattended

## Ask
"merge it and start the unattended half", after the spec PR for #137 (`docs/lock-screen-line-spec.md`) and its build order: "Items 1 and 2 can run unattended. Item 3 is attended: it is the first root code in the app".

## Source
- **Kind:** a reviewed spec's build order
- **Location:** `docs/lock-screen-line-spec.md`, "Build order" items 1 and 2, plus the parts of item 4 that need no daemon (the smoke keys and scenario)

## Intent
Everything of the lock screen line that runs without root is built and tested, so the attended session only adds the daemon, its signing and its install and uninstall.

## Done when
- `LockLine` composes `prefix · usage · reset · waiting` from plain inputs, and its sanitizer drops paths, emails, URLs, secret shapes and long tokens; tests feed it a task title, a file path, an email, a URL, a token, a bidi override and a long prefix, and none reaches the line.
- Settings, Desktop, Lock Screen: the switch, the prefix with the sanitizer's reason, the slot checkboxes, the live preview with its character count, and the caption; Search finds it; with the switch on the app writes `lock-line.txt` atomically on a 5-minute timer (only when the line changed) and at session resign or screen sleep; with it off the file is empty.
- `state.yaml` carries `lock_line` and a scenario drives the composer with fake watchers and demo usage.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.

## Out of scope
- The daemon, `SMAppService.daemon`, CI signing, install and uninstall, saving and restoring the owner's message: attended (spec item 3).
- A release: nothing ships until the daemon lands, since the switch would write a file nothing reads.
- What's New and the README section (spec item 5), with the release.

## Inferred
- Inside the app the usage and reset slots read the view model (`UsageViewModel.usage`, `lastUpdated`), the same numbers `snapshot.json` is written from; the composer takes plain values so it stays pure (D2).
- The sanitizer reuses `ConfigPack.secretLine(in:)` and `looksRandom` for secret shapes rather than a second copy, as the spec says (D3).
- Until the daemon lands, the switch's row says the lock screen writer comes with a later build; the preview and the file work now (D4).

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("start the unattended half").
