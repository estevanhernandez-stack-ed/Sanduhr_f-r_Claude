import SwiftUI
import AppKit

/// Settings, Desk, Now Playing (item 53): the source the self-test chose (or that it is placed
/// nowhere), Hide while paused, which apps, the AppleScript fallback and the adapter's credit.
/// Where it shows is arranged with everything else (item 53b): the wings and the strip in Notch,
/// the element in Layout; the two buttons go there.
struct NowPlayingSection: View {
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false

    var body: some View {
        Form {
            Section {
                NowPlayingSourceRow(deskEnabled: deskEnabled)
                Text("Shows the song or video playing in any app, browsers included, wherever you place it: a notch wing or the strip under the camera (Notch), or the Desk (Layout). Click it to play or pause; two-finger click for Previous and Next. Nothing leaves your Mac, and titles are never saved (only the songs of looks you save from Claude, below). Runs only while it is placed somewhere and Desk is on.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                NowPlayingArrangeRow()
            }
            NowPlayingShowSection()
            NowPlayingLooksSection()
            NowPlayingIdleSection()
            NowPlayingAppsSection()
            NowPlayingAskSection()
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

/// "Source: Adapter working", "Fallback (Music and Spotify only)", "Not placed anywhere".
private struct NowPlayingSourceRow: View {
    let deskEnabled: Bool

    var body: some View {
        let c = NowPlayingController.shared
        LabeledContent("Source", value: NowPlayingStatus.text(
            source: c.source, placed: !c.placed.isEmpty, deskRunning: deskEnabled,
            checking: c.checking, adapterFailed: c.adapterFailed))
    }
}

/// The two ways to place it: the Notch page (wings, strip) and the Layout page (the Desk element).
private struct NowPlayingArrangeRow: View {
    var body: some View {
        HStack {
            SettingsLinkButton(.notch)
            SettingsLinkButton(.deskLayout)
            Spacer()
        }
    }
}

private struct NowPlayingShowSection: View {
    @AppStorage(NowPlayingPrefs.hidePausedKey, store: .desk) private var hidePaused = false

    var body: some View {
        Section("Show") {
            Toggle("Hide while paused", isOn: $hidePaused)
            Text("While paused, a wing shows a Next button at its outer edge (the strip at its end): click the title to play, the button to skip.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}

/// When nothing is playing: what a notch wing or the strip on Now playing shows in its stead
/// (NowPlayingIdle, NotchContent.effective).
private struct NowPlayingIdleSection: View {
    @AppStorage(NowPlayingIdle.key, store: .desk) private var idle = NowPlayingIdle.automatic

    var body: some View {
        Section {
            Picker("When nothing is playing", selection: $idle) {
                ForEach(NowPlayingIdle.allCases) { Text($0.label).tag($0) }
            }
            Text("Applies to the notch wings and the strip under the camera: when nothing plays, while paused with Hide while paused on, or when the app playing is switched off below, that spot shows this instead, and now playing comes back with the next track. By default each spot shows what it shows when Now playing isn't picked there. The Desk line simply hides.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
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
