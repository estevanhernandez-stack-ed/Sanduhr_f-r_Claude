import SwiftUI

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

    var body: some View {
        let t = vm.theme.palette
        VStack(spacing: 0) {
            TabView {
                pacingTab(t: t)
                    .tabItem { Text("Pacing & Focus") }
                
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
