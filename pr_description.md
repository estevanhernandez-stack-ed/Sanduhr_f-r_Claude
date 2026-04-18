## 🕰️ Sanduhr Advanced Pacing Tools

This PR introduces the completed feature scope for Phase 1 - 4 pacing tools, deep-work focus modules, and the wait-state snake game for both the Windows PySide6 and macOS SwiftUI clients. **The legacy `sanduhr.py` fallback is explicitly untouched.**

### ✨ Features Integrated
1. **Configuration Overlay**
   - **Windows:** Added custom 3-tab architecture in `settings_dialog.py` bridging config flags via `settingsSaved`.
   - **macOS:** Ported `SettingsSheet.swift` over to a `TabView` relying purely on native `@AppStorage`.
2. **Deep Math Pacing Calculators**
   - Click-to-toggle logic attached directly to UI cards.
   - Tells power-users dynamically how long they need to reach absolute zero usage to get back to pace (`Cool down`).
   - Tells light-users their burn-rate `Surplus`. 
3. **Deep Work Timer (Cmd/Ctrl + P)**
   - Drops the user right into a single `FocusView.swift` overlay or `FocusTimerWidget` via QStackedLayout.
   - Hand-built circular neon progress UI drawn via SwiftUI `Canvas` and `QPainter` vectors respecting user custom color `.json` themes.
4. **Wait-State Vector Game (Snake Overlay)**
   - A perfectly integrated pure-python (PySide6) and pure-swift Snake game triggered from Context menus. 
   - Uses zero gaming frameworks.
   - Added automatic native high-score tracking hooked straight into the parent configurations.

### 🧪 Verification
- Tested native performance scaling of custom themes.
- Validated Focus Stacked Layout bounds.
- Validated Snake high score persistence across app loads.
