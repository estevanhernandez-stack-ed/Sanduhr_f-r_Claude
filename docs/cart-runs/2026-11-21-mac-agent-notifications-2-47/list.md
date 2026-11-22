# Run List: mac-agent-notifications-2-47

Each item is the plan's task of the same number: `docs/superpowers/plans/2026-11-21-mac-agent-notifications.md`.
Its Global Constraints and Review Focus apply to every item. The design is the contract.

- [ ] **1. Turn events, the Claude Code signal file, and the rules (pure)**
  Spec ref: plan Task 1; design
  What to build: plan Task 1, every step.
  Acceptance: Done when plan Task 1's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. Codex and Antigravity turn readers (pure)**
  Spec ref: plan Task 2; design
  What to build: plan Task 2, every step.
  Acceptance: Done when plan Task 2's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. The activity watcher**
  Spec ref: plan Task 3; design
  What to build: plan Task 3, every step.
  Acceptance: Done when plan Task 3's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. The Claude Code hook**
  Spec ref: plan Task 4; design
  What to build: plan Task 4, every step.
  Acceptance: Done when plan Task 4's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. The surfaces: notification, glow color, turn cards**
  Spec ref: plan Task 5; design
  What to build: plan Task 5, every step.
  Acceptance: Done when plan Task 5's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **6. Wiring**
  Spec ref: plan Task 6; design
  What to build: plan Task 6, every step.
  Acceptance: Done when plan Task 6's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **7. Settings, Notifications**
  Spec ref: plan Task 7; design
  What to build: plan Task 7, every step.
  Acceptance: Done when plan Task 7's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **8. Smoke hooks and idle CPU**
  Spec ref: plan Task 8; design
  What to build: plan Task 8, every step.
  Acceptance: Done when plan Task 8's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **9. The 2.47.0 rows**
  Spec ref: plan Task 9; design
  What to build: plan Task 9, every step.
  Acceptance: Done when plan Task 9's tests exist and pass, and its Review Focus tests (if it owns any) pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`
  Stop class: go

## Not this run
- The real Claude Code stdin check and the real Codex check happen in smoke section 81 (Task 9 writes it), live, after the run.
- The release.
