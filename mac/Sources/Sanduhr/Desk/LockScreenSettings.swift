import SwiftUI

/// Settings, Lock Screen (#137): the line Sanduhr keeps ready for the lock screen. The root
/// writer that shows it there is a later build (docs/lock-screen-line-spec.md); for now the
/// page keeps `lock-line.txt` current and previews it.
struct LockScreenSettings: View {
    let vm: UsageViewModel
    @AppStorage(LockLineKeys.on, store: .desk) private var enabled = false
    @AppStorage(LockLineKeys.prefix, store: .desk) private var prefix = ""
    @AppStorage(LockLineKeys.usage, store: .desk) private var showUsage = true
    @AppStorage(LockLineKeys.reset, store: .desk) private var showReset = true
    @AppStorage(LockLineKeys.waiting, store: .desk) private var showWaiting = true

    static let switchTitle = "Show a line on the lock screen"
    static let laterBuild = "The lock screen writer comes in a later build; for now Sanduhr keeps the line ready."
    static let readable = "Anyone at the screen can read this line. Sanduhr only puts counts, percents and times in it."

    var body: some View {
        Form {
            Section {
                Toggle(Self.switchTitle, isOn: $enabled)
                    .onChange(of: enabled) { _, _ in LockLineController.shared.refresh() }
                    .settingsAnchor(SettingsAnchor.lockLine)
                Text(Self.laterBuild).font(.caption).foregroundStyle(.secondary)
            }
            LockLinePrefixSection(prefix: $prefix)
            Section("Show") {
                Toggle("Claude usage", isOn: $showUsage)
                Toggle("Reset time", isOn: $showReset)
                Toggle("Watchers waiting for you", isOn: $showWaiting)
            }
            .onChange(of: showUsage) { _, _ in LockLineController.shared.refresh() }
            .onChange(of: showReset) { _, _ in LockLineController.shared.refresh() }
            .onChange(of: showWaiting) { _, _ in LockLineController.shared.refresh() }
            LockLinePreviewSection(vm: vm, signature: [prefix, "\(showUsage)\(showReset)\(showWaiting)"])
        }
        .formStyle(.grouped)
    }
}

private struct LockLinePrefixSection: View {
    @Binding var prefix: String

    var body: some View {
        Section("Prefix") {
            TextField("Prefix", text: $prefix, prompt: Text("Optional text before the numbers"))
                .onChange(of: prefix) { _, _ in LockLineController.shared.refresh() }
            if let reason = LockLine.reason(forPrefix: prefix) {
                Text(reason)
                    .font(.caption).foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct LockLinePreviewSection: View {
    let vm: UsageViewModel
    /// Changes whenever a setting does, so the line redraws with it.
    let signature: [String]

    var body: some View {
        Section("Preview") {
            let line = LockLineController.line(vm: vm, now: Date())
            Text(line.isEmpty ? " " : line)
                .font(.system(.callout, design: .monospaced))
                .textSelection(.enabled)
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 6))
            Text("\(line.count) of \(LockLine.maxLength) characters")
                .font(.caption).foregroundStyle(.secondary)
            Text(LockScreenSettings.readable)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
