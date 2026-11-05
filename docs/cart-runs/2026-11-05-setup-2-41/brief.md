# Run Brief: setup-2-41

## Ask
"lets do it now", answering "Up next is 2.41.0, Set up Sanduhr ... It'll be an overnight run whenever you want to start it." Started 2026-11-05 08:15, the morning after 2.40.0 (Drawers) shipped.

## Source
- **Kind:** spec
- **Location:** `docs/mac-drawers-setup-spec.md`, sections 10 to 12 and the 2.41.0 rows of section 13, with the owner's decisions at its end.
- **Windows reference, read only:** 4.5.0 in `windows-dotnet/src/`:
  - `Sanduhr.Core/`: `SetupSettings.cs`, `SetupCopy.cs`, `SetupDesk.cs`, `SetupPrompt.cs`, `PastedSetup.cs`;
  - `Sanduhr.App/Services/SetupService.cs`, `ViewModels/SetupViewModel.cs`, `Views/SetupWindow.xaml(.cs)`;
  - its run `docs/cart-runs/2026-10-31-setup-4-5/` (list, decisions D1 to D18, whole-branch review fixes).
- **House style:** the Mac's onboarding and Just the Desk code (`Models/JustTheDesk.swift`, `Views/OnboardingSheet.swift`, `Models/WelcomeTour.swift`, `AppDelegate.swift`), `Services/UsageViewModel.swift` (sign-out, `stopFetching`, `SnapshotWriter`), `Models/SanduhrMenu.swift`, `Models/WhatsNewCards.swift`, the Desk's `DeskDesign`/`DeskDesignProposalHandoff` (the `propose_desk` validator and review), and `Desk/Drawers/` (2.40.0).

## Intent
One screen of free choices replaces the welcome sheet: **A nicer desktop**, **Tidy my files**, **Track my AI usage**.
- Usage can be off with the account kept: no fetching, `snapshot.json` deleted, the account and its Keychain item untouched.
- **Design it with an AI chat:** a prompt for any chat and a paste-back block in Windows' shape, so one answer works on either platform.
- The prompt never holds a file name, a path or a usage number. One bad field rejects the whole paste and names the field.

## Done when
- Items 1 to 9 are merged on `cart/setup-2-41`.
- `cd mac && ./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.
- No upgrade path turns usage off for someone signed in (migration tests prove it).

## Out of scope
- The release (Info.plist version, appcast, cask), any push or PR, and anything under `windows-dotnet/`.
- **Shipping:** the owner's call 1 holds 2.41.0 until 2.40.0 has run on real Desktops for a few days. This run builds; it does not ship.
- Other usage sources besides Claude. The stored shape keeps a `usage` map as Windows does, so one can be added later.
- Reading or moving anything in the real `~/Desktop` during the run, signing in, or touching the Keychain. Tests use temp folders and in-memory stores.

## Inferred
- **Base:** `main` at `d1686977` (2.40.0 shipped, CHANGELOG titled). (from: run start)
- **Storage:** the Mac keeps settings in UserDefaults, not settings.json, so `setup` is a standard-defaults key holding `{desktop, usage: {claude}}` in the same shape as Windows. (from: `JustTheDesk.swift`, `SetupSettings.cs`)
- **Desktop off, Mac form:** on the Mac, drawers are their own panels, not Desk layout words, so "desktop off" hides the Desk's pieces while the drawer panels keep showing. The saved layout is never rewritten. (from: `DrawerPanelController`, `SetupDesk.cs`)
- **Upgrade rule:** someone with an account gets desktop on with usage on, and their Desk switch untouched. Someone who chose Just the Desk gets desktop on and usage off. Only someone with neither sees the screen. (from: spec 10, `SetupMigration`, Windows review fix I1)
- **No `hold` items.** Usage off stops fetching and deletes `snapshot.json`, as sign-out already does. It changes no credential, no auth flow and no permission, and the Keychain item stays. Drawers' Desktop permission already shipped in 2.40.0. Logged as D1.

## Mode
- **Mode:** Unattended
- **Triggered by:** It follows the Drawers run, which the owner ran unattended. Offered as "an overnight run", the owner said "lets do it now". Auto mode is on, so the permission preflight passed.
