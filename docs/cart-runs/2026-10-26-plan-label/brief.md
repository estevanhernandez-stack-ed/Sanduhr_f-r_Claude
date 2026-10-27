# Run Brief: plan-label

**Asked (verbatim):** "merge and start", after: "unattended: docs/plan-label-spec.md, the Mac plan label for 2.34.0 (footer capsule + Accounts label, PlanLabel ported from Windows with riffs, snapshot.json plan filled, demo plan, PRIVACY/README/smoke docs, and the 2.34.0 What's New card + WhatsNewTests). Do not bump Info.plist or cut the release; the ship commit comes after review."

**Source:** `docs/plan-label-spec.md` (merged in #364). The owner's calls are in the dashboard decision of 2026-10-26 on project Sanduhr (macOS).

**Intent:** The Mac shows the account's subscription plan the way Windows does, plus per-account labels, and Claude Code sessions get it through `snapshot.json`.

**Done when:** All five items are merged on `cart/plan-label`, and `cd mac && ./test.sh` passes on the integration branch. The owner then reviews and does a live check on a Max ×20 account, and the 2.34.0 ship commit comes after.

**Out of scope:** Info.plist version bump, the release, any push or PR, anything under `windows-dotnet/`, the notch, the menu bar, the lock screen line.

**Inferences:**
- The plan for a non-active, non-followed account shows only once its org has been fetched. The spec says no new request (spec § Where it shows, 2).
- The What's New card is added now with version "2.34.0", before Info.plist moves. WhatsNewTests checks the table, not the bundle version (checked by item 5's worker).

**Mode:** unattended. The ask said so, and auto mode is on.
