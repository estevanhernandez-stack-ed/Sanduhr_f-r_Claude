import SwiftUI

/// Settings, Alerts: Codex's alerts switch (agent sources, item 4). Off until turned on, and shown
/// only while Codex is ticked in Set up Sanduhr, so a Mac with only Claude on sees nothing new.
/// It uses the lines, resets, pace and delivery chosen above; only a fresh Codex limit alerts.
struct CodexAlertsToggle: View {
    let t: Theme.Palette
    @AppStorage(Notifier.Key.codexAlerts) private var codexAlerts = false
    @State private var codexOn = Self.isCodexOn()

    /// Whether Codex is on in the stored setup.
    static func isCodexOn() -> Bool {
        AgentSourceSettings.load().isOn(.codex, setup: SetupStore.load())
    }

    var body: some View {
        Group {
            if codexOn {
                VStack(alignment: .leading, spacing: 4) {
                    Toggle(Self.title, isOn: $codexAlerts)
                        .accessibilityLabel(Self.title)
                        .accessibilityHint(Self.hint)
                    Text(Self.hint)
                        .font(.caption)
                        .foregroundStyle(t.textDim)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: .sanduhrSetupDidChange)) { _ in
            codexOn = Self.isCodexOn()
        }
    }

    static let title = "Codex alerts"
    static let hint = "Codex 5-hour and weekly limits alert with the lines and options above. Only a current reading alerts, never an old log."
}
