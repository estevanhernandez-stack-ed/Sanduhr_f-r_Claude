# Microsoft Store submission — what we learned

Written after Sanduhr für Claude passed certification on 2026-04-20 following
two rejections under policy **10.1.4.4 (App Quality — Content)**. Intended as
a reusable playbook for the next 626 Labs app submission, not a marketing
piece. Claims here are grounded in actual review feedback, actual PR diffs,
and the actual Notes-to-Publisher text that got us through.

---

## The three sub-clauses of 10.1.4.4

Every rejection we got cited at least two of these. Treat them as separate
acceptance tests. Losing on any one kicks the submission back.

### (a) Content

**What the reviewer is checking:** trademark attribution, clarity of who made
the product, that you're not passing off someone else's IP as your own.

**What passes:**

- Explicit nominative-use disclaimer anywhere user-visible text refers to a
  third-party product. For Sanduhr: *"'Claude' and 'claude.ai' are trademarks
  of Anthropic PBC, used nominatively to describe integration. Sanduhr für
  Claude is an independent third-party tool."* Landed in Store Description,
  Copyright field, Privacy Policy, SECURITY.md, README footer.
- Publisher identity on Partner Center matches the git commit authorship and
  the copyright line. Mismatch is a red flag to reviewers even if technically
  allowed.

**What fails:**

- Referring to the target product by trademark without a disclaimer.
- Using the target product's logo or marks in your store assets.
- Vague copyright (e.g. just "© 2026" with no entity).

### (b) Unique lasting value

**What the reviewer is checking:** whether the app does enough to justify
taking up a slot in the Store, or whether it's a one-screen utility that
could be a browser bookmark.

**What passes:** multiple interactive features across discovery → engagement
→ return. For Sanduhr v2.0.4 specifically:

- **Core value** — burn-rate projection, advanced pacing metrics
  (cooldown / surplus on hover), pace ghost overlay
- **Engagement** — deep-work focus timer with digitised hourglass physics,
  cooldown snake game for the wait state
- **Personalization** — five hand-tuned themes, user-authored JSON themes,
  AI-agent prompt to generate themes from reference images
- **Platform craft** — Win11 Mica glass, native vibrancy, OS-native
  credential storage, edge-drag resize, breathing glass animation

**What fails:** a single view with static data. The v2.0.1 submission was
essentially "a widget that shows numbers" and got rejected here.

### (c) Navigation

**What the reviewer is checking:** can they find every feature the product
claims, within a reasonable number of clicks, using standard interaction
patterns.

**What passes:**

- **Persistent tool strip** with dedicated buttons for every top-level
  action. For Sanduhr: 🎨 Themes, ⚙ Settings, 📊 Graph, ↕ Compact,
  ⏳ Focus, 🐍 Snake. Hidden right-click menus fail here — the reviewer
  might not know to right-click.
- **`setAccessibleName` / `accessibilityLabel` on every button.** Screen
  readers AND the MS Store review tooling use these. Emoji-only button
  labels without accessible names read as "picture" or literal codepoints.
- **Tooltips on non-obvious controls.** Keep them terse; don't tooltip-spam
  every internal element.
- **Keyboard shortcuts** for the high-traffic actions. Document them in an
  in-app Settings → Help tab.
- **Windows-native close button.** Use the Unicode heavy multiplication sign
  (×), width 46px, red hover (`#c42b1c`), darker pressed state. Matching
  Explorer / Settings chrome signals "this is a native Windows app" to the
  reviewer instinctively.
- **Dialogs must render legibly on light-mode Windows.** This one bit us
  hard — QSS scoped to the main widget doesn't cascade to QDialog on a
  light-mode host. Test every dialog on a fresh Windows install set to
  light mode before submitting.

**What fails:**

- Features accessible only via right-click menu.
- Buttons labeled with emoji only (no accessible name).
- Dialogs that render as light-gray text on white (invisible).
- Any affordance that requires the user to read docs to discover.

---

## Submission sequence

### Before first submit

1. **Reserve the publisher name** in Partner Center. The name must match the
   MSIX manifest's `Publisher` field exactly — wrong-cased characters fail
   ingestion.
2. **Build the MSIX unsigned.** Store ingestion signs with Microsoft's
   publisher cert. If you sign it yourself you'll fail ingestion.
3. **Stage the Store listing copy** before uploading the package:
   - Description — lead with unique value. Not "what it is," "why it's
     worth your slot."
   - Screenshots — show actual features, not just the splash. Minimum three,
     ideally six, covering different states (first-run, active usage,
     settings, a theme change).
   - Keywords — what users would search for.
   - Privacy Policy URL — must be live and crawlable.
   - Trademark disclaimer in the Copyright field.
4. **Test the MSIX install locally** via `Add-AppxPackage` with the
   unsigned bypass — catches broken manifest references, missing assets,
   and DLL bundling issues that the reviewer would also hit.

### After rejection

**Don't argue. Don't patch.** Treat each rejection as a diagnostic:

1. **Read the feedback literally.** Reviewers cite specific clause
   numbers and specific findings. Write each finding down.
2. **Find the root cause, not the surface.** v2.0.1's rejection said
   "navigation is poor." The surface fix would have been adding more
   tooltips. The root cause was that most features lived in a hidden
   right-click menu. We moved them into a visible tool strip. Surface
   fixes fail the next review.
3. **Add regression tests for the specific bug class.** Our dialog-
   legibility rejection drove 55 parameterized tests (every dialog-chrome
   widget × every theme). Future theme changes can't silently regress.
4. **Update the version** — Partner Center rejects duplicate version
   numbers across submissions.
5. **Write a Notes-to-Publisher response** (template below).
6. **Resubmit.**

### Notes-to-Publisher template

Copy this structure for any resubmission:

```
Thanks for the detailed feedback on the previous submission. 
[One sentence framing — this is substantive, not cosmetic.]

**On "[specific finding quoted]":**

[Describe root cause in one paragraph. What was actually broken, 
not what looked broken.]

**Fixed in v[X.Y.Z]:**

- [Specific code change, one bullet per dimension of the fix.]
- [Regression test coverage count.]
- [PR or commit reference so the reviewer can spot-check.]

**To verify:**

1. [Concrete user-facing step.]
2. [Another step.]
3. [Optional: expected outcome.]

Full changelog: [link to CHANGELOG]

Thank you for the careful review — the specific feedback made this a 
one-commit root-cause fix instead of a guessing game.
```

The cultural thing that made this work: we treated the reviewer as an
engineering peer. Not an adversary to outsmart, not a bureaucrat to placate.
They gave us precise signal; we gave them precise response.

---

## Common gotchas

These bit us on Sanduhr specifically. Inoculate against them up front.

1. **Code-signed .exe ≠ Store-signed MSIX.** The Store approval applies to
   the MSIX install path only. The raw .exe you host on GitHub Releases
   stays unsigned unless you buy a separate code-signing cert ($300+/yr
   standard, ~$700/yr EV). Budget for this or direct users to the Store
   listing as primary and keep the .exe as "advanced install."
2. **PyInstaller one-folder packages need exactly the right Qt plugin
   paths** or the MSIX ingestion errors in opaque ways. Build from a
   clean venv, test-install on a VM, don't trust the "works on my
   machine" MSIX.
3. **Inno Setup's `#define MyAppVersion` is idempotent with `/D` only if
   you `#ifndef` the define first.** Otherwise CI-passed versions get
   shadowed by whatever's in the `.iss` file. Wrap it.
4. **Qt's `QDialog` doesn't inherit stylesheet from its parent widget**
   on Windows. If you're using QSS to theme and the parent's stylesheet
   has selectors scoped to the parent class, dialogs fall through to
   the system palette. Write explicit `QDialog { ... }` rules, apply
   the root stylesheet to every dialog before `exec_()`.
5. **Windows Credential Manager keys don't auto-clear on MSIX uninstall
   the way App Data does.** If you want true on-uninstall credential
   wipe, add an explicit cleanup path. Document either way in the
   privacy policy.
6. **Partner Center's age-rating questionnaire asks about data
   collection.** Answer honestly. If you claim "no data collected,"
   your privacy policy must match (Sanduhr's SECURITY.md explicitly
   leads with "no data comes back to us" for this reason).
7. **Store listing Description has a character limit (~10k) but the
   "Short description" limit is ~200.** The short description is what
   shows up in search results — make it earn its slot.

---

## Reference artifacts from this project

- **CHANGELOG** structure with rejection errata — [CHANGELOG.md](../CHANGELOG.md)
  notes both v2.0.3 (what actually shipped) and v2.0.4 (what people
  expected to ship) honestly.
- **SECURITY.md** template — [../SECURITY.md](../SECURITY.md) leads with
  the no-telemetry contract before any responsible-disclosure boilerplate.
- **Accessible-name audit** — see `windows/src/sanduhr/widget.py` for
  every `setAccessibleName` call. Missing any one is a reviewer's red flag.
- **Dialog-legibility test suite** — `windows/tests/test_dialog_styling.py`
  — 55 tests that would've caught the v2.0.2 rejection.
- **Submission response text** — preserved in the 626 Labs decision log
  under project `1tvLeMQmwjQCosm5Ym55`.

---

## For the next app

Paste this in whichever Claude session is prepping the submission:

> Reference: `estevanhernandez-stack-ed/Sanduhr_f-r_Claude` at `v2.0.4-windows`.
> Playbook: `docs/ms-store-submission-playbook.md` in that repo. Apply each
> section to the current app before first submit. Don't ship until all
> three sub-clauses of 10.1.4.4 are clean in the codebase — not
> "addressable," clean. Rejections cost 3-7 days each.
