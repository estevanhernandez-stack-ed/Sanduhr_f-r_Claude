import SwiftUI
import AppKit

/// Settings, Desk, Now Playing (item 53): the source the self-test chose (or that it is placed
/// nowhere), Hide while paused, which apps, the AppleScript fallback and the adapter's credit.
/// Where it shows is arranged with everything else (item 53b): the wings and the strip in Notch,
/// the piece on Desk; the two buttons go there.
struct NowPlayingSection: View {
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false

    var body: some View {
        Form {
            NeedsDeskRow()
            Section {
                NowPlayingSourceRow(deskEnabled: deskEnabled)
                    .settingsAnchor(SettingsAnchor.source)
                Text("Shows the song or video playing in any app, browsers included, wherever you place it: a notch wing or the strip under the camera (Notch), or a place on the Desk (Desk). Click it to play or pause; two-finger click for Previous and Next. Nothing leaves your Mac, and titles are never saved (only the songs of looks you save from Claude, below). Runs only while it is placed somewhere and Desk is on.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                NowPlayingArrangeRow()
            }
            NowPlayingShowSection()
            NowPlayingLooksSection()
            NowPlayingBarsSection()
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
            checking: c.checking, reason: c.fallbackReason))
    }
}

/// The two ways to place it: the Notch page (wings, strip) and the Desk page (the Desk piece).
private struct NowPlayingArrangeRow: View {
    var body: some View {
        HStack {
            SettingsLinkButton(.notch, anchor: SettingsAnchor.text)
            SettingsLinkButton(.deskLayout, anchor: SettingsAnchor.pieces)
            Spacer()
        }
    }
}

private struct NowPlayingShowSection: View {
    @AppStorage(NowPlayingPrefs.hidePausedKey, store: .desk) private var hidePaused = false

    var body: some View {
        Section("Show") {
            Toggle("Hide while paused", isOn: $hidePaused)
                .settingsAnchor(SettingsAnchor.show)
            Text("Hidden \(Int(NowPlayingPrefs.pausedGrace)) seconds after you pause, so a short pause leaves the title to click to play again.")
                .font(.caption).foregroundStyle(.secondary)
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
            .settingsAnchor(SettingsAnchor.idle)
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
        Section {
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
        } header: {
            // The rows come and go with the apps seen, so the heading carries the anchor.
            Text("Apps").settingsAnchor(SettingsAnchor.apps)
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

/// Live audio bars (spec 14): off by default. Turning the switch on is the only moment macOS can ask
/// for System Audio Recording; turning it off ends the tap at once.
private struct NowPlayingBarsSection: View {
    @AppStorage(AudioBarsPrefs.levelKey, store: .desk) private var on = false
    @AppStorage(NowPlayingPrefs.barsGradientKey, store: .desk) private var gradient = false

    var body: some View {
        let driver = AudioBarsDriver.shared
        Section("Live bars") {
            Toggle("Live audio bars", isOn: Binding(
                get: { on && AudioBarsDriver.supported },
                set: { on = $0; driver.settingsChanged() }))
                .disabled(!AudioBarsDriver.supported)
                .settingsAnchor(SettingsAnchor.bars)
            Text(note)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if on, AudioBarsDriver.supported {
                Toggle("Gradient bars", isOn: $gradient)
                if driver.denied { deniedRow }
            }
        }
    }

    private var note: String {
        AudioBarsDriver.supported
            ? "Moves bars on the Desk line with how loud the playing app is. Turning this on asks macOS for System Audio Recording. Sanduhr reads one level from that app, keeps it in memory and never records, saves or sends audio."
            : "Needs macOS 14.2 or later."
    }

    private var deniedRow: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text("macOS is not giving Sanduhr that app's audio level. Allow Sanduhr in System Settings, Screen & System Audio Recording, under System Audio Recording Only.")
                .font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Button(AudioBarsPrefs.allowTitle + "…") { NSWorkspace.shared.open(AudioBarsPrefs.settingsURL) }
                .controlSize(.small)
                .accessibilityLabel("Open Screen and System Audio Recording in System Settings, to let Sanduhr read the playing app's level")
        }
    }
}

private struct NowPlayingAskSection: View {
    @AppStorage(NowPlayingPrefs.askAppsKey, store: .desk) private var askApps = false

    /// System Settings, Privacy & Security, Automation.
    static let automationSettings = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!

    var body: some View {
        Section("When the system now playing is unavailable") {
            Toggle("Ask Music and Spotify directly", isOn: $askApps)
                .settingsAnchor(SettingsAnchor.fallback)
            Text("If macOS stops sharing what plays, Sanduhr falls back to the notices Music and Spotify send on each change (no browsers, no permission needed). This switch also asks them for their track when the fallback starts, so it shows before the next change. macOS asks once per app whether Sanduhr may control Music or Spotify; Sanduhr only reads what's playing, and only from an app that is already open.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if NowPlayingController.shared.automationDenied { deniedRow }
        }
    }

    /// The Automation permission was declined or reset (NowPlayingController.automationDenied),
    /// in the Accessibility row's shape.
    private var deniedRow: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text("macOS is not letting Sanduhr ask Music or Spotify. Allow Sanduhr under Automation in System Settings, then try again.")
                .font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Button("Open System Settings…") { NSWorkspace.shared.open(Self.automationSettings) }
                .controlSize(.small)
                .accessibilityLabel("Open Automation in System Settings, to let Sanduhr ask Music and Spotify")
        }
    }
}
