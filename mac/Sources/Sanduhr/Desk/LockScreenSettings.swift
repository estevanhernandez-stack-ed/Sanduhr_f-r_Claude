import SwiftUI

/// Settings, Lock Screen (#137, docs/lock-screen-line-spec.md): the line Sanduhr puts on the lock
/// screen. The switch keeps `lock-line.txt` current and installs the root helper
/// (LockLineDaemon) that writes it to the lock screen; turning it off takes the line away and
/// removes the helper.
struct LockScreenSettings: View {
    let vm: UsageViewModel
    @AppStorage(LockLineKeys.on, store: .desk) private var enabled = false
    @AppStorage(LockLineKeys.prefix, store: .desk) private var prefix = ""
    @AppStorage(LockLineKeys.usage, store: .desk) private var showUsage = true
    @AppStorage(LockLineKeys.reset, store: .desk) private var showReset = true
    @AppStorage(LockLineKeys.waiting, store: .desk) private var showWaiting = true

    static let switchTitle = "Show a line on the lock screen"
    static let readable = "Anyone at the screen can read this line. Sanduhr only puts counts, percents and times in it."

    var body: some View {
        Form {
            LockLineSwitchSection(enabled: $enabled)
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

/// The switch and the helper's state under it: installed, waiting for macOS's OK, or off, and
/// what macOS said when it refused.
private struct LockLineSwitchSection: View {
    @Binding var enabled: Bool
    @State private var state = LockLineDaemon.state
    @State private var problem: String?
    @State private var removing = false

    var body: some View {
        Section {
            Toggle(LockScreenSettings.switchTitle, isOn: $enabled)
                .onChange(of: enabled) { _, on in apply(on) }
                .settingsAnchor(SettingsAnchor.lockLine)
            Text(removing ? LockLineDaemon.removingCaption : LockLineDaemon.caption(state))
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if state == .needsApproval {
                Button("Open Login Items…") { LockLineDaemon.openLoginItems() }
            }
            if let problem {
                Text(problem)
                    .font(.caption).foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .onAppear { state = LockLineDaemon.state }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            state = LockLineDaemon.state
        }
    }

    private func apply(_ on: Bool) {
        problem = nil
        if on {
            LockLineController.shared.refresh(force: true)
            do { try LockLineDaemon.install() } catch {
                // register() also throws when macOS wants the Login Items OK first; the state is
                // the truth, so only a helper that is neither on nor waiting for an OK failed.
                problem = LockLineDaemon.installProblem(error, after: LockLineDaemon.state)
            }
            state = LockLineDaemon.state
        } else {
            let shown = LockLineDaemon.currentLoginText() ?? ""
            let wasOn = state == .on
            LockLineController.shared.refresh()
            guard wasOn else {
                Task { @MainActor in
                    await LockLineDaemon.remove(lastLine: "")
                    state = LockLineDaemon.state
                }
                return
            }
            removing = true
            Task { @MainActor in
                let removed = await LockLineDaemon.remove(lastLine: shown)
                removing = false
                state = LockLineDaemon.state
                if !removed { problem = LockLineDaemon.stillUp }
            }
        }
    }
}
