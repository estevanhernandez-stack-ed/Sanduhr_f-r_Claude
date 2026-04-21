import SwiftUI
import AppKit

/// Paste sheet for the session key (and optional cf_clearance fallback).
struct SettingsSheet: View {
    @Bindable var vm: UsageViewModel
    var onDismiss: () -> Void

    // Write-only sheet: we never read the existing key back from the
    // Keychain (skips a Touch ID prompt just to open settings, and avoids
    // ever displaying the secret on screen). Leave blank to keep the
    // existing value; any non-empty value replaces it.
    @State private var sessionKey: String = ""
    @State private var cfClearance: String = ""
    private var hasExistingKey: Bool {
        KeychainStore.exists(account: KeychainAccount.sessionKey)
    }

    @AppStorage("pacingToolsEnabled") private var pacingToolsEnabled = true
    @AppStorage("remindSessionEnd") private var remindSessionEnd = false

    // Themes tab state
    @State private var themePaste: String = ""
    @State private var themeFilename: String = ""
    @State private var themeError: String?
    @State private var themeStatus: String?
    @State private var installedThemes: [URL] = []
    @State private var selectedInstalled: URL?

    var body: some View {
        let t = vm.theme.palette
        VStack(spacing: 0) {
            TabView {
                pacingTab(t: t)
                    .tabItem { Text("Pacing & Focus") }

                themesTab(t: t)
                    .tabItem { Text("Themes") }

                credentialsTab(t: t)
                    .tabItem { Text("Credentials") }
            }
            .padding(.bottom, 16)



            HStack {
                Spacer()
                Button("Cancel", action: onDismiss)
                    .keyboardShortcut(.cancelAction)
                Button("Save") {
                    let trimmedKey = sessionKey.trimmingCharacters(in: .whitespacesAndNewlines)
                    let trimmedCF  = cfClearance.trimmingCharacters(in: .whitespacesAndNewlines)
                    // Only write fields that were filled in — blank means
                    // "keep what's there". First-time users are forced to
                    // provide sessionKey via the `disabled` check below.
                    if !trimmedKey.isEmpty {
                        KeychainStore.set(trimmedKey,
                                          account: KeychainAccount.sessionKey)
                    }
                    if !trimmedCF.isEmpty {
                        KeychainStore.set(trimmedCF,
                                          account: KeychainAccount.cfClearance)
                    }
                    vm.credentialsChanged()
                    onDismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(
                    // Require a new key on first setup; otherwise allow
                    // saving blank (keeps current values).
                    !hasExistingKey &&
                    sessionKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                )
            }
        }
        .padding(20)
        .frame(width: 420)
    }

    private func credentialsTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 6) {
                Text(hasExistingKey ? "sessionKey (replace)" : "sessionKey (required)")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                Text("claude.ai → DevTools (⌥⌘I) → Application → Cookies → sessionKey")
                    .font(.caption2)
                    .foregroundStyle(t.textDim)
                SecureField(
                    hasExistingKey ? "Leave blank to keep existing key" : "sessionKey",
                    text: $sessionKey)
                    .textFieldStyle(.roundedBorder)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("cf_clearance (optional)")
                    .font(.caption)
                    .foregroundStyle(t.textSecondary)
                Text("Only needed if you see a Cloudflare challenge error.")
                    .font(.caption2)
                    .foregroundStyle(t.textDim)
                SecureField(
                    hasExistingKey ? "Leave blank to keep existing" : "cf_clearance",
                    text: $cfClearance)
                    .textFieldStyle(.roundedBorder)
            }
            Spacer()
        }
        .padding(.top, 12)
    }

    // MARK: - Themes tab

    @ViewBuilder
    private func themesTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Drop a theme JSON below — or paste what an AI agent returned.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            TextEditor(text: $themePaste)
                .font(.system(size: 11, design: .monospaced))
                .frame(minHeight: 120, maxHeight: 160)
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
            .frame(minHeight: 80, maxHeight: 140)
            .onAppear { installedThemes = UserThemes.listFiles() }
        }
        .padding(.top, 12)
        .onChange(of: themePaste) { _, _ in autofillFilename() }
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
            if let saved = ThemeRegistry.theme(id: id) {
                vm.theme = saved
            }
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

    // MARK: - Pacing tab

    private func pacingTab(t: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Configure the advanced pacing and focus tools. These overlays appear directly on top of the widget UI when activated.")
                .font(.caption)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Toggle("Enable Pacing Calculators", isOn: $pacingToolsEnabled)
            Toggle("Show reminder at 100% of session", isOn: $remindSessionEnd)
            Spacer()
        }
        .padding(.top, 12)
    }
}

private struct NumericOnly: ViewModifier {
    func body(content: Content) -> some View {
        #if os(macOS)
        content.onChange(of: "") { _, _ in } // placeholder to silence Swift 5.9 warning if needed
        #else
        content.keyboardType(.numberPad)
        #endif
        return content
    }
}
