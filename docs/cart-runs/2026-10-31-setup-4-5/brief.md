# Run Brief: setup-4-5

**Ask, verbatim:** "subagent, but orchestrated with vibe-cartographer" (on the plan handoff), then "unattended. taking kids to knock on strangers doors for candy".

**Source:** `docs/superpowers/plans/2026-10-31-setup.md` (12 tasks), implementing `docs/superpowers/specs/2026-10-31-setup-design.md`. Branch `docs/setup-spec`, base `52a97067`.

**Intent:** Set up Sanduhr for Windows 4.5.0. One screen of free choices (a nicer desktop, tidy my files, track my AI usage) on first run and from the tray and Settings, replacing the one-way Just the Desk mode, plus Design it with an AI chat.

**Done when:** every plan task's tests pass on the integration branch `cart/setup-4-5`, the full Core, App and Mcp suites pass in Release, and the harness shots exist. The owner's live check (plan Task 12, step 2) waits for the owner.

**Out of scope:** drawers styling and placement (its own spec, next), plan 3's MCP drawer tools and auto-tidy, the agent layer and rename (5.0), the Mac, pushing or opening a PR, the version bump.

**Inferences:**
- Execution is subagent-driven, as the owner chose: a fresh implementer per item, then a fresh reviewer per item before it merges, then a whole-branch review at close. (Owner, plan handoff.)
- Each plan task is one item, in plan order, each depending on the one before (the plan's interfaces chain). (Plan, Interfaces blocks.)
- Every item is `go`. Task 4 changes what follows a sign-in (adding an account turns usage on) but adds no auth, gating or permission surface; Task 6 turns drawers off through the existing, tested turn-off. See `decisions.md` D1.
- Workers run App tests in Release, so the owner's Store Sanduhr and any dev build never lock the Debug output. (Plan, Global Constraints.)

**Mode:** unattended, by the owner's word at 17:31 CT. Auto mode is on (permission preflight passed).
