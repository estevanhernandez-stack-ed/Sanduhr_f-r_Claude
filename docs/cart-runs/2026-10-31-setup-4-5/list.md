# Run List: setup-4-5

Every item's full steps, code and test code are in its plan task (`docs/superpowers/plans/2026-10-31-setup.md`); the plan's Global Constraints and Review Focus apply to every item.

- [ ] **1. The stored setup, its migration and the launch rule (Core)**
  Spec ref: `plan > Task 1`; spec section 1
  What to build: `SetupSettings.cs` (SetupSettings, SetupChoices, SetupMigration, SetupLaunch) and `SetupSettingsTests`. S.
  Acceptance: Done when the 11 SetupSettingsTests cases pass and the four migration cases map as the spec's table says.
  Verify: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter SetupSettingsTests`
  Stop class: go

- [ ] **2. Setup in settings.json, migrated once**
  Spec ref: `plan > Task 2`; spec section 1, Migration
  What to build: `LoadSetup`, `SaveSetup`, `MigrateSetup` on SettingsStore and `SetupStoreTests`. S.
  Acceptance: Done when SetupStoreTests pass: absent loads null, Just the Desk migrates with the old keys kept, migration runs once and never on a first run.
  Verify: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter SetupStoreTests`
  Stop class: go

- [ ] **3. The setup copy and each change's line (Core)**
  Spec ref: `plan > Task 3`; spec sections 2 and 4
  What to build: `SetupCopy.cs` (SetupCopy, SetupChange) and `SetupCopyTests`. S.
  Acceptance: Done when SetupCopyTests pass, including no em dash and no product name but Claude.
  Verify: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter SetupCopyTests`
  Stop class: go

- [ ] **4. Usage turns off without signing out; launch and sign-in follow the setup**
  Spec ref: `plan > Task 4`; spec sections 1, 2
  What to build: UsageOn and SetUsageOn in WidgetViewModel; the Just the Desk button, mode and dead members removed; the Widget page's usage-off section; App launch, migration call, first-run route stub and sign-in turning usage on. L.
  Acceptance: Done when WidgetUsageTests, WidgetPageTests, WidgetLimitsTests, SetupStoreTests and the touched Core tests pass, the solution builds with 0 errors, and nothing in src calls a removed JustTheDesk member.
  Verify: the two test commands in plan Task 4 Step 8, then `dotnet build windows-dotnet/Sanduhr.slnx -c Release`
  Stop class: go

- [ ] **5. Desktop off leaves only the drawers**
  Spec ref: `plan > Task 5`; spec section 1
  What to build: `SetupDesk.cs`, the DeskController filter in LayoutFor and Placed, Arrange blocked and hidden while the desktop is off. M.
  Acceptance: Done when SetupDeskTests pass, the Desk App tests pass, and the saved layout is never written filtered.
  Verify: plan Task 5 Steps 4 and 7
  Stop class: go

- [ ] **6. The setup service applies a change**
  Spec ref: `plan > Task 6`; spec sections 2 and 4; Review Focus 2
  What to build: `SetupService.cs` (ISetupHost, SetupService, AppSetupHost) and `SetupServiceTests`. M.
  Acceptance: Done when the 6 SetupServiceTests pass, including a refused turn-off that keeps drawers ticked while the rest applies.
  Verify: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter SetupServiceTests`
  Stop class: go

- [ ] **7. The Set up Sanduhr window, first-run route and tour**
  Spec ref: `plan > Task 7`; spec section 2; Review Focus 5
  What to build: SetupViewModel, SetupWindow, App.OpenSetup and AfterSetup, WelcomeTour renames. M.
  Acceptance: Done when SetupWindowTests (names, blocked, X saves nothing, reopened lines, Continue) and WelcomeTourTests pass.
  Verify: plan Task 7 Step 6
  Stop class: go

- [ ] **8. Tray, Settings General and the 4.5.0 card**
  Spec ref: `plan > Task 8`; spec section 4
  What to build: the tray item, General's Set up Sanduhr button and anchor, the `choose-what-shows` card, ShowMe opening the window. S.
  Acceptance: Done when WhatsNewCardsTests (with the new id first and Release450), SettingsIndex/Names tests and the new SetupWindowTests case pass.
  Verify: plan Task 8 Step 6
  Stop class: go

- [ ] **9. The AI chat prompt and the pasted-answer reader (Core)**
  Spec ref: `plan > Task 9`; spec section 3; Review Focus 4
  What to build: `SetupPrompt.cs`, `PastedSetup.cs` and their tests. M.
  Acceptance: Done when SetupPromptTests and PastedSetupTests pass and the Mcp TrustBoundary tests still pass unchanged.
  Verify: plan Task 9 Step 5
  Stop class: go

- [ ] **10. Design it with an AI chat in the app**
  Spec ref: `plan > Task 10`; spec section 3
  What to build: ChatDesignViewModel and window, DeskDesignService.ProposePasted with the neutral heading and no result file, UseChatRules, the Desk page button. L.
  Acceptance: Done when ChatDesignTests and the new DeskDesignService paste test pass, plus the DeskDesign, SettingsIndex and names suites.
  Verify: plan Task 10 Step 6
  Stop class: go

- [ ] **11. Harness shots of the setup screen**
  Spec ref: `plan > Task 11`
  What to build: `SetupScenario.cs` and its wiring in the harness. S.
  Acceptance: Done when the harness writes the four setup PNGs and each is legible and unclipped.
  Verify: plan Task 11 Step 2
  Stop class: go

- [ ] **12. The whole suite**
  Spec ref: `plan > Task 12 Step 1`
  What to build: no new code; fix any test still naming Just the Desk. S.
  Acceptance: Done when `dotnet test windows-dotnet/Sanduhr.slnx -c Release` reports 0 failures. The live check (Step 2) is the owner's and is listed in the report.
  Verify: `dotnet build windows-dotnet/Sanduhr.slnx -c Release` then `dotnet test windows-dotnet/Sanduhr.slnx -c Release --no-build`
  Stop class: go

- [ ] **13. Whole-branch review fixes**
  Spec ref: `decisions.md > D18`; whole-branch review of cart/setup-4-5
  What to build: I1 the upgrade migration keeps `desktop: true` for account users, I2 a usage-off row on the widget, I3 the trademark line shown whenever the screen names Claude, plus Minors M1, M2, M4 and M6, the D9 bar line, and the D5 test rows. M.
  Acceptance: Done when the fixes have tests, the full suite passes in Release, and a reviewer approves.
  Verify: `dotnet test windows-dotnet/Sanduhr.slnx -c Release`
  Stop class: go
