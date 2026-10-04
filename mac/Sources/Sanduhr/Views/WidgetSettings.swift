import SwiftUI
import AppKit

/// The widget's sections of the Settings window (SettingsWindow.swift): Pacing & Focus,
/// Themes, Widget Look and Alerts. They were the tabs of the widget's settings sheet;
/// SettingsRoot picks one with `section`. Accounts has its own view (AccountsSettings.swift).
struct WidgetSettings: View {
    @Bindable var vm: UsageViewModel
    let section: SettingsSection

    @AppStorage("remindSessionEnd") private var remindSessionEnd = false
    @AppStorage("alertsEnabled") private var alertsEnabled = false
    @AppStorage("alertSessionPct") private var alertSessionPct = 80.0
    @AppStorage("alertWeeklyPct") private var alertWeeklyPct = 80.0
    @AppStorage("alertSessionReset") private var alertSessionReset = false
    @AppStorage(Notifier.Key.weeklyReset) private var alertWeeklyReset = false
    @AppStorage(Notifier.Key.pace) private var alertPace = false
    @AppStorage(Notifier.Key.delivery) private var alertDelivery = AlertDelivery.banner.rawValue
    @AppStorage(Notifier.Key.sound) private var alertSound = AlertSound.standard
    @AppStorage(Notifier.Key.quietEnabled) private var alertQuietEnabled = false
    @AppStorage(Notifier.Key.quietStart) private var alertQuietStart = 22 * 60
    @AppStorage(Notifier.Key.quietEnd) private var alertQuietEnd = 7 * 60
    @State private var systemSounds = AlertSound.systemNames()
    @State private var alertsNote: String?

    // Font state. Bound straight to the shared settings, so the widget
    // re-renders in the new font as you pick.
    @Bindable var fonts = FontSettings.shared
    @Bindable var display = DisplaySettings.shared
    @State private var fontFamilies: [String] = []

    // Themes state
    @State private var themePaste: String = ""
    @State private var themeFilename: String = ""
    @State private var themeError: String?
    @State private var themeStatus: String?
    @State private var installedThemes: [URL] = []
    @State private var selectedInstalled: URL?
    @AppStorage(ThemeProposalHandoff.directKey) private var themeClaudeDirect = false
    var themeHandoff = ThemeProposalHandoff.shared

    var body: some View {
        let t = vm.theme.palette
        Group {
            switch section {
            case .pacing: pacingTab(t: t)
            case .themes: themesTab(t: t)
            case .widgetLook: fontTab(t: t)
            case .alerts: ScrollView { alertsTab(t: t) }
            default: EmptyView()
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    // MARK: - Themes tab

    @ViewBuilder
    private func themesTab(t: Theme.Palette) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                if let proposal = themeHandoff.pending {
                    ThemeSuggestionBanner(proposal: proposal, placement: themeHandoff.placement(proposal),
                                          save: { themeHandoff.approve(apply: false) },
                                          saveAndApply: { themeHandoff.approve(apply: true) },
                                          dismiss: { themeHandoff.dismiss() })
                }
                Text("Click a theme to use it on the widget.")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                ThemeGalleryView(vm: vm)
                Divider().padding(.vertical, 6)
                Text("Your own themes")
                    .font(.headline)
                themeTools(t: t)
                claudeThemeSwitch(t: t)
            }
            .padding(.trailing, 4)
        }
    }

    @ViewBuilder
    private func themeTools(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Drop a theme JSON below — or paste what an AI agent returned.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            TextEditor(text: $themePaste)
                .font(.system(size: 11, design: .monospaced))
                .frame(height: 140)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .strokeBorder(t.border.opacity(0.4), lineWidth: 0.5))

            HStack(spacing: 8) {
                Text("Filename:")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                TextField("auto-filled from JSON \"name\"", text: $themeFilename)
                    .textFieldStyle(.roundedBorder)
                Text(".json")
                    .font(.caption)
                    .foregroundStyle(t.textDim)
            }

            HStack(spacing: 8) {
                Button("Save & Apply", action: saveAndApplyTheme)
                    .keyboardShortcut(.defaultAction)
                    .disabled(themePaste.trimmingCharacters(in: .whitespaces).isEmpty)
                Button("Copy Agent Prompt", action: copyAgentPrompt)
                Button("Open Themes Folder", action: openThemesFolder)
                Spacer()
            }

            if let err = themeError {
                Text(err)
                    .font(.caption)
                    .foregroundStyle(Color.hex("f87171"))
            } else if let ok = themeStatus {
                Text(ok)
                    .font(.caption)
                    .foregroundStyle(Color.hex("4ade80"))
            }

            Divider()

            HStack {
                Text("Installed user themes")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                Spacer()
                Button("Reload", action: reloadInstalled)
                Button("Delete Selected", action: deleteSelected)
                    .disabled(selectedInstalled == nil)
            }

            List(installedThemes, id: \.self, selection: $selectedInstalled) { url in
                Text(url.lastPathComponent)
                    .font(.system(size: 11, design: .monospaced))
                    .tag(url)
            }
            // A fixed height: inside the scroll view a min/max range collapses to the minimum.
            .frame(height: 110)
            .onAppear { installedThemes = UserThemes.listFiles() }
            // A file added or deleted outside the app (the folder watch bumps the tick).
            .onChange(of: vm.userThemesTick) { _, _ in installedThemes = UserThemes.listFiles() }
        }
        .onChange(of: themePaste) { _, _ in autofillFilename() }
    }

    /// Item 55: whether a theme Claude proposes (propose_theme) waits for Save or lands at once.
    private func claudeThemeSwitch(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle("Let Claude change themes directly", isOn: $themeClaudeDirect)
            Text(themeClaudeDirect
                 ? "Themes Claude proposes with the Sanduhr MCP server are saved to your themes at once, and applied when Claude asks. Your own themes are never overwritten."
                 : "Themes Claude proposes with the Sanduhr MCP server wait at the top of this page for you to save, apply or dismiss.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.top, 6)
    }

    private func autofillFilename() {
        guard themeFilename.isEmpty,
              let data = themePaste.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let name = obj["name"] as? String, !name.isEmpty
        else { return }
        themeFilename = UserThemes.slugify(name)
    }

    private func saveAndApplyTheme() {
        themeError = nil
        themeStatus = nil
        var fn = themeFilename.trimmingCharacters(in: .whitespaces)
        if fn.isEmpty,
           let data = themePaste.data(using: .utf8),
           let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let name = obj["name"] as? String {
            fn = UserThemes.slugify(name)
        }
        guard !fn.isEmpty else {
            themeError = "Filename is required (or include a \"name\" field in the JSON)."
            return
        }
        do {
            let url = try UserThemes.writeTheme(json: themePaste, filename: fn)
            vm.userThemesTick += 1
            installedThemes = UserThemes.listFiles()
            themePaste = ""
            themeFilename = ""
            themeStatus = "Saved: \(url.lastPathComponent)"
            // Auto-apply the newly-saved theme if it parsed cleanly.
            let id = url.deletingPathExtension().lastPathComponent.lowercased()
            vm.selectTheme(id: id)
        } catch {
            themeError = "Could not save theme: \(error.localizedDescription)"
        }
    }

    private func copyAgentPrompt() {
        themeError = nil
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(AgentPrompt.text, forType: .string)
        themeStatus = "Agent prompt copied — paste it into any chat agent with a reference image."
    }

    private func openThemesFolder() {
        themeError = nil
        let dir = UserThemes.appSupportThemesDir()
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        NSWorkspace.shared.open(dir)
    }

    private func reloadInstalled() {
        UserThemes.reload()
        installedThemes = UserThemes.listFiles()
        vm.userThemesTick += 1
        themeError = nil
        themeStatus = "Reloaded \(installedThemes.count) user theme\(installedThemes.count == 1 ? "" : "s")."
    }

    private func deleteSelected() {
        guard let url = selectedInstalled else { return }
        do {
            try UserThemes.deleteTheme(filename: url.lastPathComponent)
            vm.userThemesTick += 1
            installedThemes = UserThemes.listFiles()
            selectedInstalled = nil
            themeError = nil
            themeStatus = "Deleted \(url.lastPathComponent)."
            // If the deleted theme was currently applied, fall back to default.
            let id = url.deletingPathExtension().lastPathComponent.lowercased()
            if vm.theme.id == id {
                vm.theme = ThemeRegistry.default
            }
        } catch {
            themeError = "Could not delete: \(error.localizedDescription)"
        }
    }

    // MARK: - Font tab

    private func fontTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Pick any font installed on this Mac, your own handwriting font included. The widget changes as you choose. Monospaced readouts keep the system font so the digits stay aligned.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Picker("Font", selection: $fonts.family) {
                Text("System (SF Pro)").tag("")
                Divider()
                ForEach(fontFamilies, id: \.self) { fam in
                    Text(fam).tag(fam)
                }
            }

            Toggle("Subtle mode: no background, just the numbers over your desktop", isOn: $display.subtle)

            if vm.theme.palette.ink != nil {
                Text("The Match Desk theme is in use: the widget draws in Desk's font, without the glass. Your font and subtle mode come back with any other theme.")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Text("Sanduhr 0:42 left, 68%")
                .font(.app(size: 18, weight: .semibold))
                .foregroundStyle(t.text)

            HStack(spacing: 8) {
                Button("Use System Font") { fonts.family = "" }
                    .disabled(fonts.family.isEmpty)
                Button("Open Font Book") {
                    if let url = NSWorkspace.shared.urlForApplication(
                        withBundleIdentifier: "com.apple.FontBook") {
                        NSWorkspace.shared.openApplication(
                            at: url, configuration: .init())
                    }
                }
                Spacer()
            }
            Spacer()
        }
        .onAppear { fontFamilies = FontSettings.installedFamilies() }
    }

    // MARK: - Alerts tab

    private func alertsTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("A heads-up before you hit a limit. Each alert comes once per reset window. Focus and Do Not Disturb still apply.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Toggle("Notifications", isOn: $alertsEnabled)
                .onChange(of: alertsEnabled) { _, on in
                    guard on else { return }
                    Notifier.shared.requestPermission { granted in
                        alertsNote = granted ? nil
                            : "macOS has notifications off for Sanduhr. Turn them on in System Settings, Notifications."
                    }
                }

            Group {
                HStack {
                    Text("Session (5 hr) at")
                    Slider(value: $alertSessionPct, in: 50...95, step: 5)
                    Text("\(Int(alertSessionPct))%").monospacedDigit().frame(width: 40, alignment: .trailing)
                }
                HStack {
                    Text("Weekly limits at")
                    Slider(value: $alertWeeklyPct, in: 50...95, step: 5)
                    Text("\(Int(alertWeeklyPct))%").monospacedDigit().frame(width: 40, alignment: .trailing)
                }
                Toggle("Also when the session reaches 100%", isOn: $remindSessionEnd)
                Toggle("When the session resets", isOn: $alertSessionReset)
                Toggle("When a weekly limit resets", isOn: $alertWeeklyReset)
                Toggle("Warn when I'm on pace to run out before the reset", isOn: $alertPace)

                Divider()

                Picker("Where alerts show", selection: $alertDelivery) {
                    ForEach(AlertDelivery.allCases) { Text($0.title).tag($0.rawValue) }
                }
                Text("A Desk pulse glows the limit's meter on the desktop and the notch island. With Desk off, alerts show as banners.")
                    .font(.caption)
                    .foregroundStyle(t.textDim)
                    .fixedSize(horizontal: false, vertical: true)

                HStack {
                    Picker("Sound", selection: $alertSound) {
                        Text("Default").tag(AlertSound.standard)
                        Text("None").tag(AlertSound.none)
                        Divider()
                        ForEach(systemSounds, id: \.self) { Text($0).tag($0) }
                    }
                    Button("Preview") { Notifier.preview(sound: alertSound) }
                        .disabled(alertSound == AlertSound.standard || alertSound == AlertSound.none)
                }

                Toggle("Quiet hours", isOn: $alertQuietEnabled)
                HStack {
                    DatePicker("From", selection: quietTime($alertQuietStart), displayedComponents: .hourAndMinute)
                    DatePicker("to", selection: quietTime($alertQuietEnd), displayedComponents: .hourAndMinute)
                    Spacer()
                }
                .disabled(!alertQuietEnabled)
                Text("No banners or sounds in these hours, and they are not saved for later. Desk pulses still show.")
                    .font(.caption)
                    .foregroundStyle(t.textDim)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .disabled(!alertsEnabled)

            HStack(spacing: 8) {
                Button("Send a Test") { Notifier.shared.sendTest() }
                    .disabled(!alertsEnabled)
                Button("Notification Settings") {
                    if let url = URL(string: "x-apple.systempreferences:com.apple.Notifications-Settings.extension") {
                        NSWorkspace.shared.open(url)
                    }
                }
                Spacer()
            }
            if let note = alertsNote {
                Text(note).font(.caption).foregroundStyle(Color.hex("f87171"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
        }
    }

    /// A time picker over minutes since midnight, today's date standing in for the day.
    private func quietTime(_ minutes: Binding<Int>) -> Binding<Date> {
        Binding(
            get: { Calendar.current.startOfDay(for: Date()).addingTimeInterval(TimeInterval(minutes.wrappedValue * 60)) },
            set: {
                let c = Calendar.current.dateComponents([.hour, .minute], from: $0)
                minutes.wrappedValue = (c.hour ?? 0) * 60 + (c.minute ?? 0)
            })
    }

    // MARK: - Pacing tab

    private func pacingTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("The pacing calculators (cool down and surplus) show on a card under the pointer. Pin them to keep them on every card; Tools, Pacing Calculators in any Sanduhr menu does the same. Deep Work and Cooldown Snake open from Tools too.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Toggle("Pin the pacing calculators on every card", isOn: $vm.pacingPinned)
            Text("Until Sanduhr quits.")
                .font(.caption)
                .foregroundStyle(t.textDim)
            Spacer()
        }
    }
}
