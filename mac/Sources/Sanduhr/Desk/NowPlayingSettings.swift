import SwiftUI
import AppKit

/// Settings, Desk, Now Playing (item 53): the switch (off by default), where it shows, which apps,
/// the source the self-test chose, the AppleScript fallback and the adapter's credit.
struct NowPlayingSection: View {
    @AppStorage(NowPlayingPrefs.enabledKey, store: .desk) private var enabled = false
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false

    var body: some View {
        Form {
            Section {
                Toggle("Show what's playing", isOn: $enabled)
                    .onChange(of: enabled) { _, _ in NowPlayingController.shared.apply() }
                NowPlayingSourceRow(enabled: enabled, deskEnabled: deskEnabled)
                Text("Shows the song or video playing in any app, browsers included: on the notch (pick Now playing for a wing or the strip in Settings, Notch) and under the Desk meters. Click it to play or pause; two-finger click for Previous and Next. Nothing leaves your Mac, and titles are never saved. Runs only while Desk is on.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            NowPlayingShowSection()
                .disabled(!enabled)
            NowPlayingAppsSection()
                .disabled(!enabled)
            NowPlayingAskSection()
                .disabled(!enabled)
            Section {
                Text("Now playing uses mediaremote-adapter by Jonas van den Berg (BSD-3-Clause).")
                    .font(.caption).foregroundStyle(.secondary)
                Link("mediaremote-adapter on GitHub", destination: AboutLinks.mediaRemoteAdapter)
                    .font(.caption)
            }
        }
        .formStyle(.grouped)
    }
}

/// "Source: Adapter working", "Fallback (Music and Spotify only)", "Off".
private struct NowPlayingSourceRow: View {
    let enabled: Bool
    let deskEnabled: Bool

    var body: some View {
        let c = NowPlayingController.shared
        LabeledContent("Source", value: NowPlayingStatus.text(
            source: c.source, enabled: enabled, deskRunning: deskEnabled,
            checking: c.checking, adapterFailed: c.adapterFailed))
    }
}

private struct NowPlayingShowSection: View {
    @AppStorage(NowPlayingPrefs.deskLineKey, store: .desk) private var deskLine = true
    @AppStorage(NowPlayingPrefs.hidePausedKey, store: .desk) private var hidePaused = false

    var body: some View {
        Section("Show") {
            Toggle("A line under the Desk meters", isOn: $deskLine)
            Toggle("Hide while paused", isOn: $hidePaused)
            Text("Nothing shows when nothing plays. With the meters off the desktop, the line sits under the Claude line.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}

/// Every app seen playing since Sanduhr started, each with a switch; an excluded app stays on the
/// list so it can be switched back on.
private struct NowPlayingAppsSection: View {
    @State private var excluded: Set<String> = []

    var body: some View {
        let rows = NowPlayingApps.rows(seen: NowPlayingController.shared.seenApps, excluded: excluded)
        Section("Apps") {
            if rows.isEmpty {
                Text("Apps show here once they play something.")
                    .foregroundStyle(.secondary)
            }
            ForEach(rows, id: \.self) { app in
                Toggle(Self.name(app), isOn: Binding(
                    get: { !excluded.contains(app) },
                    set: { show in
                        if show { excluded.remove(app) } else { excluded.insert(app) }
                        UserDefaults.desk.set(excluded.sorted(), forKey: NowPlayingPrefs.excludedKey)
                    }))
            }
            Text("Switch an app off and its playback never shows. All apps show by default.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .onAppear {
            excluded = Set(UserDefaults.desk.stringArray(forKey: NowPlayingPrefs.excludedKey) ?? [])
        }
    }

    /// The app's name from its bundle, else the bundle id.
    static func name(_ bundleID: String) -> String {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else { return bundleID }
        let name = FileManager.default.displayName(atPath: url.path)
        return name.hasSuffix(".app") ? String(name.dropLast(4)) : name
    }
}

private struct NowPlayingAskSection: View {
    @AppStorage(NowPlayingPrefs.askAppsKey, store: .desk) private var askApps = false

    var body: some View {
        Section("When the system now playing is unavailable") {
            Toggle("Ask Music and Spotify directly", isOn: $askApps)
            Text("If macOS stops sharing what plays, Sanduhr falls back to the notices Music and Spotify send on each change (no browsers, no permission needed). This switch also asks them for their track when the fallback starts, so it shows before the next change. macOS asks once per app whether Sanduhr may control Music or Spotify; Sanduhr only reads what's playing, and only from an app that is already open.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
