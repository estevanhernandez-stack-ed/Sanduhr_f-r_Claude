import SwiftUI

/// Watchers' one home (Settings v2, item 72, slice 2; item 66): the two switches, where watchers
/// show (inline, on the same keys the Notch and Desk pages write), the glow rule, and the rows
/// above Claude Code's prompt. Was split across Integrations, Notch and Layout.
struct WatchersSection: View {
    @State private var page = WatchersPageModel()

    var body: some View {
        Form {
            NeedsDeskRow()
            WatcherSwitchesSection(hooked: page.hooked)
            WatcherPlacesSection()
            WatcherGlowSection()
            WatcherBandSection(page: page)
        }
        .formStyle(.grouped)
        .task { await page.load() }
    }
}

/// The Claude Code folders as the Watchers page needs them: whether any has the Claude Code glow
/// hook (background work needs it) and which have the meters mod (rows above the prompt need it).
/// Reads files only, off the main thread.
@MainActor
@Observable
final class WatchersPageModel {
    struct Folder: Identifiable, Equatable {
        let display: String
        let meters: IntegrationStatus
        var id: String { display }
    }

    private(set) var folders: [Folder] = []
    /// Nil until read.
    private(set) var hooked: Bool?

    func load() async {
        let home = NSHomeDirectory()
        let result = await Task.detached(priority: .userInitiated) { () -> ([Folder], Bool) in
            let installer = IntegrationInstaller.standard
            var paths = ClaudeCodeFolders.discover(home: home, environment: ProcessInfo.processInfo.environment).map(\.path)
            for p in installer.installedFolders() {
                let n = AccountData.normalized(p)
                if !paths.contains(n) { paths.append(n) }
            }
            let hooked = paths.contains { [.installed, .outdated].contains(installer.status(.hooks, folder: $0)) }
            let folders = paths.map {
                Folder(display: ClaudeCodeFolders.Folder(path: $0).display(home: home),
                       meters: installer.status(.meters, folder: $0))
            }
            return (folders, hooked)
        }.value
        folders = result.0
        hooked = result.1
    }
}

private struct WatcherSwitchesSection: View {
    let hooked: Bool?
    @AppStorage(WatcherStore.agentsKey) private var agents = false
    @AppStorage(WatcherStore.backgroundKey) private var background = false

    var body: some View {
        Section {
            Text("Live cards for work in flight, on the notch or the Desk: a title, a state dot (running, waiting on you, passed, failed), the time so far, the progress when there is a total and a one-line note. A click opens the watcher's link. Kept in memory only: quitting Sanduhr drops them.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Let agents show watchers", isOn: $agents)
                .settingsAnchor(SettingsAnchor.switches)
            Text("Agents use the MCP server's watch_start, watch_update and watch_end. Off, the server refuses them.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Show Claude Code's background work", isOn: $background)
            if hooked == false {
                HStack {
                    Text("Needs the \(SettingsNames.claudeCodeGlowHook) in a Claude Code folder.")
                        .font(.caption).foregroundStyle(.orange)
                    Spacer()
                    SettingsLinkButton(.integrations, anchor: SettingsAnchor.folders)
                }
            }
            Text("Background shells, monitors, subagents and workflows show as watchers on their own and end when they leave the session's list. Claude Code hands over each task's kind, status and short description, never a command or anything from the conversation; Sanduhr reads it and deletes it at once, and never logs it. Watchers from a work account's folder hide in demo mode.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// Where they show: one picker for the notch and one for the Desk. Each writes the same stored
/// value as the Notch page's place pickers and the Desk page's Watchers row, so they never
/// disagree (the spec's one deliberate exception to one control per setting).
private struct WatcherPlacesSection: View {
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var left: String?
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var right: String?
    @AppStorage(NotchContent.Place.strip.key, store: .desk) private var strip: String?
    @AppStorage("layout", store: .desk) private var layout = DeskLayout.standard

    var body: some View {
        // Read so a change on the Notch page redraws this one.
        let _ = (left, right, strip)
        Section("Where they show") {
            Picker(SettingsNames.watchersOnNotch, selection: notchSpot) {
                ForEach(WatcherPlacement.NotchSpot.allCases) { Text($0.label).tag($0) }
            }
            .settingsAnchor(SettingsAnchor.whereTheyShow)
            if let hint = WatcherPlacement.notchHint(in: UserDefaults.desk) {
                HStack {
                    Text(hint).font(.caption).foregroundStyle(.orange)
                    Spacer()
                    SettingsLinkButton(.notch, anchor: SettingsAnchor.text)
                }
            }
            Picker(SettingsNames.watchersOnDesk, selection: deskPlace) {
                Text("Hidden").tag("")
                Divider()
                ForEach(DeskAnchor.allCases, id: \.self) { Text($0.name).tag($0.rawValue) }
            }
            Text("The notch shows the most urgent watcher with a count of the rest; the Desk shows up to \(WatcherPlacement.deskRows). The Notch and Desk pages show the same choices.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var notchSpot: Binding<WatcherPlacement.NotchSpot> {
        Binding(get: { WatcherPlacement.notchSpot(in: UserDefaults.desk) },
                set: { WatcherPlacement.setNotchSpot($0, in: UserDefaults.desk) })
    }

    private var deskPlace: Binding<String> {
        Binding(get: { DeskArrangement(layout).placement(WatcherPlacement.widget)?.anchor.rawValue ?? "" },
                set: { layout = DeskLayout.placing(WatcherPlacement.widget, in: $0, layout: layout) })
    }
}

/// The rule WatcherStore follows, stated, and whether it holds now.
private struct WatcherGlowSection: View {
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskOn = false
    @AppStorage(DeskController.notchKey, store: .desk) private var notch = false
    @AppStorage("layout", store: .desk) private var layout = DeskLayout.standard
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var left: String?
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var right: String?
    @AppStorage(NotchContent.Place.strip.key, store: .desk) private var strip: String?

    var body: some View {
        let _ = (notch, layout, left, right, strip)
        Section("Glow when a watcher waits on you") {
            Text(WatcherPlacement.glowRule)
                .settingsAnchor(SettingsAnchor.glow)
            Text(WatcherPlacement.glowStatus(deskOn: deskOn, places: WatcherPlacement.places(in: UserDefaults.desk)))
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// Above Claude Code's prompt: the switch and the meters mod per folder, which draws the rows.
private struct WatcherBandSection: View {
    var page: WatchersPageModel
    @AppStorage(BandFile.watchersKey) private var inBand = false

    var body: some View {
        Section("Above the prompt") {
            Toggle("Show watchers above the prompt", isOn: $inBand)
                .settingsAnchor(SettingsAnchor.abovePrompt)
            Text("Claude Code draws a row per watcher above its prompt, through the meters mod: the state, the title, the time so far and the progress. For the mod, Sanduhr keeps band.json in its folder, readable by you only: an agent's watcher's title, short title, state, progress and times, and for background work only its kind and state, never its description. Off deletes the watchers from it.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(page.folders) { f in
                LabeledContent(f.display) {
                    Text(WatcherPlacement.metersLine(f.meters)).foregroundStyle(.secondary)
                }
                .font(.caption)
            }
            HStack {
                Text("\(SettingsNames.metersAbovePrompt) is switched per folder in Claude Code.")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                SettingsLinkButton(.integrations, anchor: SettingsAnchor.folders)
            }
        }
    }
}
