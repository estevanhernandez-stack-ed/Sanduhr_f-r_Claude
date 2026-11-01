# Run List: drawers-look-4-5

Every item's full steps and code are in its plan task (`docs/superpowers/plans/2026-11-01-drawers-look-placement.md`); its Global Constraints and Review Focus apply to every item.

- [ ] **1. Spike: blur and icon positions (throwaway)**
  Spec ref: `plan > Task 0`
  What to build: `spikes/drawer-look/` probe, verified COM declarations, a checklist verdict line. S.
  Acceptance: Done when both questions have PASS or FAIL with evidence in `windows-dotnet/docs/checklist.md`.
  Verify: the checklist line and the spike's screenshots.
  Stop class: go

- [ ] **2. The new drawer settings (Core)**
  Spec ref: `plan > Task 1`
  What to build: `DrawerStyle.cs`, DrawerSettings additions, `DrawerStyleTests`. S.
  Acceptance: Done when DrawerStyleTests, DrawerSettingsTests and DrawerPlacementTests pass.
  Verify: plan Task 1 Step 5.
  Stop class: go

- [ ] **3. Snapping (Core)**
  Spec ref: `plan > Task 2`
  What to build: `DrawerSnap.cs`, `DrawerSnapTests`. S.
  Acceptance: Done when DrawerSnapTests pass.
  Verify: plan Task 2 Step 4.
  Stop class: go

- [ ] **4. Icon area, nearest free spot, Tidy slots, arrival (Core)**
  Spec ref: `plan > Task 3`
  What to build: `DrawerIconArea.cs`, `DrawerTidyPlan.cs`, `Arrive` through Tidy, tests. M.
  Acceptance: Done when the four Core drawer test classes pass, existing placement tests unchanged.
  Verify: plan Task 3 Step 6.
  Stop class: go

- [ ] **5. The four looks on the Desk**
  Spec ref: `plan > Task 4`
  What to build: DrawerLook extended, DrawerPiece per look, WithDrawers from settings, Glass blur per the spike. M.
  Acceptance: Done when DrawerLookTests and the drawer piece, free panel and Desk controller suites pass.
  Verify: plan Task 4 Step 6.
  Stop class: go

- [ ] **6. Snapping while dragging, keep clear of icons**
  Spec ref: `plan > Task 5`; Review Focus 1, 5
  What to build: `DesktopIcons.cs`, the snap callback, controller placement, piece rects capture, arrival with icons. L.
  Acceptance: Done when DrawerPlacementLiveTests and the drawer and Desk suites pass.
  Verify: plan Task 5 Step 6.
  Stop class: go

- [ ] **7. Tidy with Undo**
  Spec ref: `plan > Task 6`; Review Focus 2, 4
  What to build: `DeskController.Tidy`, `RestorePlaces`, `DrawerReviewService.ShowResult`, `App.TidyDrawers`. M.
  Acceptance: Done when DrawerTidyTests and the drawer review suites pass.
  Verify: plan Task 6 Step 6.
  Stop class: go

- [ ] **8. Drawers page and Look menu**
  Spec ref: `plan > Task 7`
  What to build: page section, view model, ShellMenu checked submenu, drawer Look menu, copy and index. M.
  Acceptance: Done when DrawersLookPageTests, ShellMenu, names and index suites pass.
  Verify: plan Task 7 Step 6.
  Stop class: go

- [ ] **9. AI chat: look, opacity, arrange**
  Spec ref: `plan > Task 8`; Review Focus 4
  What to build: PastedSetup drawers members, prompt lines, ChatDesign button and App wiring. M.
  Acceptance: Done when PastedSetup, SetupPrompt, ChatDesign and TrustBoundary tests pass.
  Verify: plan Task 8 Step 6.
  Stop class: go

- [ ] **10. Harness shots of the four looks**
  Spec ref: `plan > Task 9`
  What to build: `DrawerLooksScenario.cs` and its wiring. S.
  Acceptance: Done when 16 PNGs are written and read cleanly.
  Verify: plan Task 9 Step 2.
  Stop class: go

- [ ] **11. The whole suite**
  Spec ref: `plan > Task 10 Step 1`
  What to build: no new code. S.
  Acceptance: Done when the Release build and all three suites report 0 failures.
  Verify: plan Task 10 Step 1.
  Stop class: go
