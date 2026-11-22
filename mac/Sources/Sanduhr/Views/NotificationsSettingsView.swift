import AppKit
import SwiftUI

/// Settings, Notifications (design section 5): which agents Sanduhr can tell you about, where the news shows (a
/// macOS notification, the notch glow, a Desk card), how long a turn must run, the idle reminder and the Try it
/// samples. The glow switches and "not while in front" are the Desk keys Notch ▸ Glow shows too; the rest live in
/// the standard defaults (`AgentNotificationSettings`). Each section is its own view so no body is large.
struct NotificationsSettings: View {
    var vm: UsageViewModel
    @State private var model = IntegrationsModel()
    @State private var permission = NotificationPermissionModel()

    /// The accounts' linked folders, listed even where discovery wouldn't find them.
    private var linked: [String] { vm.realAccountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                NotificationsIntro()
                NotificationAgentsSection(model: model, linked: linked)
                    .settingsAnchor(SettingsAnchor.notifyAgents)
                NotificationSurfacesSection(permission: permission)
                    .settingsAnchor(SettingsAnchor.notifyWhere)
                NotificationWhenSection()
                    .settingsAnchor(SettingsAnchor.notifyWhen)
            }
            .frame(maxWidth: .infinity, alignment: .topLeading)
            .padding(20)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task {
            await model.load(linked: linked)
            permission.refresh()
        }
    }
}

private struct NotificationsIntro: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(NotificationsPageCopy.title).font(.headline)
            Text(NotificationsPageCopy.intro)
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// A caption under a switch.
private struct NotificationCaption: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}

private struct NotificationSectionHeader: View {
    let title: String

    init(_ title: String) { self.title = title }

    var body: some View { Text(title).font(.headline) }
}

// MARK: Agents

/// A row per agent: what it can tell Sanduhr, and where it stands. Claude Code's follows the hook per folder (it
/// does not follow the Claude usage source, decision D9); Codex and Antigravity follow their Sources switches.
private struct NotificationAgentsSection: View {
    var model: IntegrationsModel
    let linked: [String]
    @State private var setup = SetupStore.load()
    @State private var sources = AgentSourceSettings.load()

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            NotificationSectionHeader(NotificationsPageCopy.agentsHeader)
            ClaudeCodeAgentRow(model: model, linked: linked)
            ForEach([AgentSource.codex, .antigravity], id: \.self) { agent in
                SourceAgentRow(agent: agent, on: sources.isOn(agent, setup: setup))
            }
        }
        .onAppear {
            setup = SetupStore.load()
            sources = AgentSourceSettings.load()
        }
    }
}

private struct AgentRowTitle: View {
    let name: String
    let supports: String

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(name)
            Text(supports).font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct SourceAgentRow: View {
    let agent: AgentSource
    let on: Bool

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            AgentRowTitle(name: agent.displayName, supports: NotificationsPageCopy.supports(agent))
            Spacer(minLength: 8)
            if on {
                Text(NotificationsPageCopy.sourceOn).font(.caption).foregroundStyle(Color.hex("4ade80"))
            } else {
                Text(NotificationsPageCopy.sourceOff).font(.caption).foregroundStyle(.secondary)
                SettingsLinkButton(.usage)
            }
        }
    }
}

private struct ClaudeCodeAgentRow: View {
    var model: IntegrationsModel
    let linked: [String]

    /// The folders that have the hook, an older one, or settings that can't be read.
    private var folders: [IntegrationFolderState] {
        model.folders.filter { NotificationsPageCopy.listsFolder($0.hooks) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                AgentRowTitle(name: AgentBanner.name(.claude), supports: NotificationsPageCopy.supports(.claude))
                Spacer(minLength: 8)
                SettingsLinkButton(.integrations, anchor: SettingsAnchor.folders)
            }
            ForEach(folders) { f in
                HookFolderRow(folder: f, model: model, linked: linked)
            }
            if model.loaded {
                ForEach(NotificationsPageCopy.hookNotes(model.folders.map(\.hooks)), id: \.self) { line in
                    NotificationCaption(line)
                }
            }
            if let note = model.note, model.noteFolder == nil || folders.contains(where: { $0.path == model.noteFolder }) {
                Text(note.text)
                    .font(.caption)
                    .foregroundStyle(Color.hex(note.isError ? "f87171" : "4ade80"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// One folder's hook: the state, and Update for an older one (the same install the Claude Code page runs; it
/// posts `.sanduhrHooksDidChange` so the watcher notices).
private struct HookFolderRow: View {
    let folder: IntegrationFolderState
    var model: IntegrationsModel
    let linked: [String]

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(model.display(folder.path)).font(.caption.monospaced()).foregroundStyle(.secondary)
            Text(NotificationsPageCopy.hookStatus(folder.hooks)).font(.caption).foregroundStyle(color)
            Spacer(minLength: 8)
            if folder.hooks == .outdated {
                Button(NotificationsPageCopy.update) { update() }
                    .disabled(model.busy != nil)
            }
        }
        .padding(.leading, 12)
    }

    private var color: Color {
        switch folder.hooks {
        case .installed: Color.hex("4ade80")
        case .outdated: .orange
        case .unreadable: Color.hex("f87171")
        case .notInstalled, .other: .secondary
        }
    }

    private func update() {
        let path = folder.path, linked = linked, model = model
        Task {
            // An older hook of Sanduhr's own never asks about replacing someone else's entry; if one turns up,
            // the Claude Code page is where that is decided.
            if await model.install(.hooks, folder: path, replaceOther: false, linked: linked) != nil {
                model.note = (NotificationsPageCopy.hookOutdated, true)
            }
        }
    }
}

// MARK: Where it shows

private struct NotificationSurfacesSection: View {
    var permission: NotificationPermissionModel
    @AppStorage(AgentNotificationSettings.bannerKey) private var banner = true
    @AppStorage(AgentNotificationSettings.cardKey) private var card = true
    @AppStorage(NotchGlowSwitches.claudeDoneKey, store: .desk) private var glowFinished = false
    @AppStorage(NotchGlowSwitches.claudeWaitingKey, store: .desk) private var glowWaiting = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            NotificationSectionHeader(NotificationsPageCopy.showHeader)
            bannerRow
            PermissionLines(permission: permission, bannerOn: banner)
            glowRows
            cardRow
        }
    }

    private var bannerRow: some View {
        SurfaceRow(label: NotificationsPageCopy.banner, isOn: $banner, tryLabel: "Try the notification") {
            permission.ensure { tryIt(.notification) }
        }
        .settingsAnchor(SettingsAnchor.notifyTry)
        .onChange(of: banner) { _, on in
            if on { permission.requestIfNeeded() }
        }
    }

    @ViewBuilder private var glowRows: some View {
        SurfaceRow(label: NotificationsPageCopy.glowFinished, isOn: $glowFinished, tryLabel: "Try the finished glow") {
            tryIt(.glowFinished)
        }
        SurfaceRow(label: NotificationsPageCopy.glowWaiting, isOn: $glowWaiting, tryLabel: "Try the waiting glow") {
            tryIt(.glowWaiting)
        }
        HStack(alignment: .firstTextBaseline) {
            NotificationCaption(NotificationsPageCopy.glowCaption)
            Spacer(minLength: 8)
            SettingsLinkButton(.notch, anchor: SettingsAnchor.glow)
        }
    }

    @ViewBuilder private var cardRow: some View {
        SurfaceRow(label: NotificationsPageCopy.card, isOn: $card, tryLabel: "Try the Desk card") {
            tryIt(.card)
        }
        .onChange(of: card) { _, on in
            // With the card off, the ones already up go too: nothing would update them again.
            if !on { for agent in AgentSource.allCases { WatcherStore.shared.clear(agent: agent) } }
        }
        NotificationCaption(NotificationsPageCopy.cardCaption)
    }

    private func tryIt(_ surface: AgentNotifier.TrySurface) {
        (NSApp.delegate as? AppDelegate)?.tryAgentNotification(surface)
    }
}

/// A switch with its Try it beside it.
private struct SurfaceRow: View {
    let label: String
    @Binding var isOn: Bool
    let tryLabel: String
    let action: () -> Void

    var body: some View {
        HStack {
            Toggle(label, isOn: $isOn)
            Spacer(minLength: 8)
            Button(NotificationsPageCopy.tryIt, action: action)
                .help(NotificationsPageCopy.tryHelp)
                .accessibilityLabel(tryLabel)
        }
    }
}

/// What macOS has decided about notifications, said only while the notification switch is on (Review Focus 5).
private struct PermissionLines: View {
    var permission: NotificationPermissionModel
    let bannerOn: Bool

    var body: some View {
        if permission.deniedLineShows(bannerOn: bannerOn) {
            HStack(alignment: .firstTextBaseline) {
                NotificationCaption(NotificationPermission.deniedLine)
                Spacer(minLength: 8)
                Button(NotificationPermission.openSettings) { NSWorkspace.shared.open(NotificationPermission.settingsURL) }
            }
        } else if permission.notAskedLineShows(bannerOn: bannerOn) {
            HStack(alignment: .firstTextBaseline) {
                NotificationCaption(NotificationPermission.notAskedLine)
                Spacer(minLength: 8)
                Button(NotificationPermission.allow) { permission.requestIfNeeded() }
            }
        }
    }
}

// MARK: When

private struct NotificationWhenSection: View {
    @AppStorage(AgentNotificationSettings.thresholdKey) private var threshold = NotificationSettings.defaultThreshold
    @AppStorage(AgentNotificationSettings.idleKey) private var idle = false
    @AppStorage(NotchGlowSwitches.claudeSkipTerminalKey, store: .desk) private var skipInFront = true

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            NotificationSectionHeader(NotificationsPageCopy.whenHeader)
            Picker(NotificationsPageCopy.threshold, selection: $threshold) {
                ForEach(AgentNotificationSettings.thresholdChoices, id: \.seconds) { Text($0.title).tag($0.seconds) }
            }
            .frame(maxWidth: 360)
            NotificationCaption(NotificationsPageCopy.thresholdCaption)
            Toggle(NotificationsPageCopy.skipInFront, isOn: $skipInFront)
            NotificationCaption(NotificationsPageCopy.inFrontCaption)
            idleRows
            HStack(alignment: .firstTextBaseline) {
                NotificationCaption(NotificationsPageCopy.quietCaption)
                Spacer(minLength: 8)
                SettingsLinkButton(.alerts, anchor: SettingsAnchor.quietHours)
            }
        }
    }

    @ViewBuilder private var idleRows: some View {
        Toggle(NotificationsPageCopy.idle, isOn: $idle)
            .settingsAnchor(SettingsAnchor.notifyIdle)
        NotificationCaption(NotificationsPageCopy.idleCaption)
    }
}
