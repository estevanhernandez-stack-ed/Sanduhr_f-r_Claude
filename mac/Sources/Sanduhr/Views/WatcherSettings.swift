import SwiftUI

/// Settings, Integrations, Watchers (item 66): the two switches, both off by default, and where
/// watchers are placed (the Notch page's places and the Desk Layout page's Watchers element).
/// Its own view so the Integrations page's body stays small enough for Swift 6.0 and 6.1.
struct WatcherSettings: View {
    let openNotch: () -> Void
    let openLayout: () -> Void

    @AppStorage(WatcherStore.agentsKey) private var agents = false
    @AppStorage(WatcherStore.backgroundKey) private var background = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Watchers").font(.headline)
            Text("Live cards for work in flight, on the notch or the Desk: a title, a state dot (running, waiting on you, passed, failed), the time so far, the progress when there is a total and a one-line note. Waiting on you pulses and glows the notch once; a click opens the watcher's link. Kept in memory only: quitting Sanduhr drops them.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Let agents show watchers", isOn: $agents)
            Text("Agents use the MCP server's watch_start, watch_update and watch_end. Off, the server refuses them.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.leading, 20)
            Toggle("Show Claude Code's background work", isOn: $background)
            Text("Background shells, monitors, subagents and workflows show as watchers on their own and end when they leave the session's list. Needs the notch glow hooks above (Install updates an older copy). Claude Code hands over each task's kind, status and short description, never a command or anything from the conversation; Sanduhr reads it and deletes it at once, and never logs it.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.leading, 20)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("Watchers show where you place them: a notch wing or the strip under the camera, or a Desk corner. Watchers from a work account's folder hide in demo mode.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 8)
                Button("Notch…", action: openNotch)
                Button("Desk Layout…", action: openLayout)
            }
        }
    }
}
