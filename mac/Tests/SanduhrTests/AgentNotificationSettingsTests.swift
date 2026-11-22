import Foundation
import Testing
import UserNotifications
@testable import Sanduhr

/// Mac agent notifications, item 7: Settings, Notifications. The settings the rules read, the one-time upgrade
/// of the idle switch, the page's pinned copy, the notification permission model, the Try it samples, and the
/// new Settings section. Made-up values in memory only: nothing reads or writes the real defaults.

private let waitingKey = NotchGlowSwitches.claudeWaitingKey
private let doneKey = NotchGlowSwitches.claudeDoneKey
private let skipKey = NotchGlowSwitches.claudeSkipTerminalKey

@Suite("Agent notification settings: load")
struct AgentNotificationSettingsLoadTests {
    @Test func defaultsArePlanDefaults() {
        let s = AgentNotificationSettings.load(standard: MemoryDefaults(), desk: MemoryDefaults())
        #expect(s.banner && s.card && !s.idle && s.skipInFront)
        #expect(!s.glowFinished && !s.glowWaiting)
        #expect(s.threshold == 60)
        #expect(!s.quiet.enabled)
    }

    @Test func loadReadsTheDeskKeysAndTheNewOnes() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: doneKey)
        desk.set(true, forKey: waitingKey)
        desk.set(false, forKey: skipKey)
        std.set(false, forKey: AgentNotificationSettings.bannerKey)
        std.set(120.0, forKey: AgentNotificationSettings.thresholdKey)
        std.set(true, forKey: AgentNotificationSettings.idleKey)
        std.set(true, forKey: Notifier.Key.quietEnabled)
        std.set(1380, forKey: Notifier.Key.quietStart)
        std.set(420, forKey: Notifier.Key.quietEnd)
        let s = AgentNotificationSettings.load(standard: std, desk: desk)
        #expect(s.glowFinished && s.glowWaiting && !s.skipInFront)
        #expect(!s.banner && s.card && s.idle)
        #expect(s.threshold == 120)
        #expect(s.quiet.enabled && s.quiet.start == 1380 && s.quiet.end == 420)
    }

    @Test func zeroMeansEveryTurnAndAnUnknownThresholdFallsBack() {
        let std = MemoryDefaults()
        std.set(0, forKey: AgentNotificationSettings.thresholdKey)
        #expect(AgentNotificationSettings.load(standard: std, desk: MemoryDefaults()).threshold == 0)
        std.set(45.0, forKey: AgentNotificationSettings.thresholdKey)
        #expect(AgentNotificationSettings.load(standard: std, desk: MemoryDefaults()).threshold == 60)
    }

    @Test func theKeysAreThePlansKeys() {
        #expect(AgentNotificationSettings.bannerKey == "agentNotifications.banner")
        #expect(AgentNotificationSettings.cardKey == "agentNotifications.card")
        #expect(AgentNotificationSettings.thresholdKey == "agentNotifications.threshold")
        #expect(AgentNotificationSettings.idleKey == "agentNotifications.idle")
        #expect(AgentNotificationSettings.migratedKey == "agentNotifications.migrated")
    }

    @Test func thresholdChoicesAreThePlansFiveInOrder() {
        #expect(AgentNotificationSettings.thresholdChoices.map(\.seconds) == NotificationSettings.thresholds)
        #expect(AgentNotificationSettings.thresholdChoices.map(\.title)
                == ["Every turn", "30 seconds", "1 minute", "2 minutes", "5 minutes"])
        #expect(NotificationSettings.defaultThreshold == 60)
    }
}

@Suite("Agent notification settings: the upgrade")
struct AgentNotificationMigrationTests {
    @Test func idleComesOnWhenTheWaitingGlowWasOn() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: waitingKey)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        #expect(std.bool(forKey: AgentNotificationSettings.idleKey))
        #expect(std.bool(forKey: AgentNotificationSettings.migratedKey))
        #expect(AgentNotificationSettings.load(standard: std, desk: desk).idle)
    }

    @Test func idleStaysOffWhenTheWaitingGlowWasOff() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: doneKey)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        #expect(!std.bool(forKey: AgentNotificationSettings.idleKey))
        #expect(std.bool(forKey: AgentNotificationSettings.migratedKey))
        #expect(!AgentNotificationSettings.load(standard: std, desk: desk).idle)
    }

    @Test func itRunsOnce() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: waitingKey)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        // The person turns idle off on the page; a later launch must not turn it back on.
        std.set(false, forKey: AgentNotificationSettings.idleKey)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        #expect(!std.bool(forKey: AgentNotificationSettings.idleKey))
        // And a glow turned on after the migration never sets idle.
        let later = MemoryDefaults(), laterDesk = MemoryDefaults()
        AgentNotificationSettings.migrateOnce(standard: later, desk: laterDesk)
        laterDesk.set(true, forKey: waitingKey)
        AgentNotificationSettings.migrateOnce(standard: later, desk: laterDesk)
        #expect(!later.bool(forKey: AgentNotificationSettings.idleKey))
    }

    @Test func aChoiceAlreadyMadeIsKept() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: waitingKey)
        std.set(false, forKey: AgentNotificationSettings.idleKey)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        #expect(!std.bool(forKey: AgentNotificationSettings.idleKey))
    }

    @Test func aTwoFortySixMacsGlowSwitchesAreUnchanged() {
        let desk = MemoryDefaults(), std = MemoryDefaults()
        desk.set(true, forKey: waitingKey)
        desk.set(false, forKey: doneKey)
        desk.set(false, forKey: skipKey)
        let before = NotchGlowSwitches(desk)
        AgentNotificationSettings.migrateOnce(standard: std, desk: desk)
        #expect(NotchGlowSwitches(desk) == before)
        #expect(desk.values.count == 3)
        // A Mac that never set them: the same defaults after, and nothing written to the Desk store.
        let fresh = MemoryDefaults()
        AgentNotificationSettings.migrateOnce(standard: MemoryDefaults(), desk: fresh)
        #expect(fresh.values.isEmpty)
        #expect(NotchGlowSwitches(fresh) == NotchGlowSwitches())
    }
}

@Suite("Settings, Notifications: the copy")
struct NotificationsPageCopyTests {
    @Test func theAgentRowsSayWhatEachAgentCanTell() {
        #expect(NotificationsPageCopy.supports(.claude) == "Working, finished and waiting")
        #expect(NotificationsPageCopy.supports(.codex) == "Working and finished")
        #expect(NotificationsPageCopy.supports(.antigravity) == "Working, finished about a minute and a half late")
        #expect(NotificationsPageCopy.sourceOff == "Switch it on in Usage ▸ Sources")
    }

    @Test func theSwitchesAndTheIdleWordingArePinned() {
        #expect(NotificationsPageCopy.title == "Notifications")
        #expect(NotificationsPageCopy.banner == "Show a macOS notification")
        #expect(NotificationsPageCopy.glowFinished == "Glow when an agent finishes")
        #expect(NotificationsPageCopy.glowWaiting == "Glow when an agent is waiting on you")
        #expect(NotificationsPageCopy.card == "Show a card on the Desk")
        #expect(NotificationsPageCopy.threshold == "Only for turns longer than")
        #expect(NotificationsPageCopy.skipInFront == "Not while the agent's app is in front")
        #expect(NotificationsPageCopy.idle == "Remind me when Claude Code has waited a minute for my next prompt")
        #expect(NotificationsPageCopy.tryIt == "Try it")
    }

    @Test func theCopyNamesNoEmployerPersonOrHomePath() {
        let words = [NotificationsPageCopy.intro, NotificationsPageCopy.idleCaption, NotificationsPageCopy.inFrontCaption,
                     NotificationsPageCopy.cardCaption, NotificationsPageCopy.hookMissing, NotificationsPageCopy.hookOutdated,
                     NotificationsPageCopy.hookUnreadable, NotificationsPageCopy.thresholdCaption,
                     NotificationPermission.deniedLine, NotificationPermission.notAskedLine]
        for w in words {
            #expect(!w.contains("/Users/"))
            #expect(!w.contains("—"))
        }
        // Em-dashes stay out of the page's microcopy: commas and periods.
    }

    @Test func theHookNotesFollowTheFolders() {
        #expect(NotificationsPageCopy.hookNotes([]) == [NotificationsPageCopy.hookMissing])
        #expect(NotificationsPageCopy.hookNotes([.notInstalled, .other]) == [NotificationsPageCopy.hookMissing])
        #expect(NotificationsPageCopy.hookNotes([.installed, .notInstalled]).isEmpty)
        #expect(NotificationsPageCopy.hookNotes([.outdated]) == [NotificationsPageCopy.hookOutdated])
        #expect(NotificationsPageCopy.hookNotes([.installed, .outdated]) == [NotificationsPageCopy.hookOutdated])
        #expect(NotificationsPageCopy.hookNotes([.installed, .unreadable]) == [NotificationsPageCopy.hookUnreadable])
        #expect(NotificationsPageCopy.hookNotes([.unreadable]) == [NotificationsPageCopy.hookMissing, NotificationsPageCopy.hookUnreadable])
    }

    @Test func aFolderRowsStatusWords() {
        #expect(NotificationsPageCopy.hookStatus(.installed) == "Installed")
        #expect(NotificationsPageCopy.hookStatus(.outdated) == "Outdated")
        #expect(NotificationsPageCopy.hookStatus(.notInstalled) == "Not installed")
        #expect(NotificationsPageCopy.hookStatus(.other) == "Not installed")
        #expect(NotificationsPageCopy.hookStatus(.unreadable) == "Can't be read")
    }

    @Test func onlyFoldersWithAHookAreListed() {
        let all: [IntegrationStatus] = [.installed, .notInstalled, .outdated, .other, .unreadable]
        #expect(all.filter(NotificationsPageCopy.listsFolder) == [.installed, .outdated, .unreadable])
    }
}

@Suite("Settings, Notifications: the permission")
@MainActor
struct NotificationPermissionTests {
    @MainActor final class Fake {
        var status: NotificationPermission = .notDetermined
        var reads = 0
        var requests = 0
        var grant = true
        lazy var model = NotificationPermissionModel(
            read: { [self] done in reads += 1; done(status) },
            request: { [self] done in
                requests += 1
                if status == .notDetermined { status = grant ? .allowed : .denied }
                done(grant)
            })
    }

    @Test func authorizationStatusesMapToThreeStates() {
        #expect(NotificationPermission(.notDetermined) == .notDetermined)
        #expect(NotificationPermission(.denied) == .denied)
        #expect(NotificationPermission(.authorized) == .allowed)
        #expect(NotificationPermission(.provisional) == .allowed)
    }

    @Test func creatingTheModelReadsAndRequestsNothing() {
        let f = Fake()
        _ = f.model
        #expect(f.reads == 0 && f.requests == 0)
        #expect(f.model.state == .checking)
    }

    @Test func refreshReadsTheState() {
        let f = Fake()
        f.status = .denied
        f.model.refresh()
        #expect(f.model.state == .denied && f.reads == 1)
        #expect(f.model.showsDeniedLine && !f.model.showsNotAskedLine)
    }

    @Test func switchingASurfaceOnAsksOnceWhenNotYetDetermined() {
        let f = Fake()
        f.model.refresh()
        f.model.requestIfNeeded()
        #expect(f.requests == 1)
        #expect(f.model.state == .allowed)
        f.model.requestIfNeeded()
        #expect(f.requests == 1)
    }

    @Test func aDeniedMacIsNeverAskedAgainAndSaysSo() {
        let f = Fake()
        f.status = .denied
        f.model.refresh()
        f.model.requestIfNeeded()
        f.model.requestIfNeeded()
        #expect(f.requests == 0)
        #expect(f.model.showsDeniedLine)
        #expect(NotificationPermission.settingsURL.absoluteString == "x-apple.systempreferences:com.apple.preference.notifications")
        #expect(NotificationPermission.deniedLine.contains("System Settings") || NotificationPermission.deniedLine.contains("glow"))
    }

    @Test func aRefusalBecomesTheDeniedLine() {
        let f = Fake()
        f.grant = false
        f.model.refresh()
        f.model.requestIfNeeded()
        #expect(f.model.state == .denied && f.requests == 1)
    }

    @Test func tryItAsksFirstWhenNotDetermined() {
        let f = Fake()
        f.model.refresh()
        var ran = 0
        f.model.ensure { ran += 1 }
        #expect(f.requests == 1 && ran == 1)
        f.model.ensure { ran += 1 }
        #expect(f.requests == 1 && ran == 2)
    }

    @Test func tryItStillRunsWhenDenied() {
        let f = Fake()
        f.status = .denied
        f.model.refresh()
        var ran = 0
        f.model.ensure { ran += 1 }
        #expect(ran == 1 && f.requests == 0)
    }

    @Test func theLineIsOnlyForABannerThatIsOn() {
        let f = Fake()
        f.status = .denied
        f.model.refresh()
        #expect(f.model.deniedLineShows(bannerOn: true))
        #expect(!f.model.deniedLineShows(bannerOn: false))
        f.status = .notDetermined
        f.model.refresh()
        #expect(f.model.notAskedLineShows(bannerOn: true))
        #expect(!f.model.notAskedLineShows(bannerOn: false))
        #expect(!f.model.deniedLineShows(bannerOn: true))
    }
}

@Suite("Settings, Notifications: Try it")
struct AgentTryItTests {
    let t0 = Date(timeIntervalSince1970: 1_791_018_000)

    final class Recorder {
        var banners: [AgentBanner] = []
        var glows: [String?] = []
        var cards: [AgentTurnEvent] = []
    }

    /// A notifier with every switch off, every agent off and a front terminal: the rules would drop anything.
    private func make(_ r: Recorder) -> AgentNotifier {
        AgentNotifier(
            settings: { NotificationSettings(banner: false, card: false, glowFinished: false, glowWaiting: false) },
            banner: { r.banners.append($0) }, glow: { r.glows.append($0) }, card: { r.cards.append($0) },
            front: FrontApp(bundleID: { "com.apple.Terminal" }, name: { nil }), clock: { t0 },
            isOn: { _ in false }, color: { $0.defaultColor })
    }

    @Test func eachSurfaceShowsWhateverTheSwitchesSay() {
        let r = Recorder()
        let n = make(r)
        n.tryIt(.notification)
        #expect(r.banners.count == 1 && r.glows.isEmpty && r.cards.isEmpty)
        n.tryIt(.glowFinished)
        #expect(r.glows == [AgentSource.claude.defaultColor])
        n.tryIt(.glowWaiting)
        #expect(r.glows == [AgentSource.claude.defaultColor, nil])
        n.tryIt(.card)
        #expect(r.cards.count == 1 && r.banners.count == 1)
    }

    @Test func theSampleIsMadeUpAndTheLimiterNeverHoldsIt() {
        let r = Recorder()
        let n = make(r)
        for _ in 0..<3 { n.tryIt(.notification) }
        #expect(r.banners.count == 3)
        let b = r.banners[0]
        #expect(b.title == "Claude Code finished")
        #expect(b.body == "your project, 4m 12s")
        #expect(b.id.hasPrefix(AgentBanner.idPrefix))
        #expect(!b.id.contains("/"))
    }

    @Test func theCardSampleIsAFinishedTurnOfAMadeUpSession() {
        let r = Recorder()
        make(r).tryIt(.card)
        let e = r.cards[0]
        #expect(e.kind == .finished && e.agent == .claude && e.project == "your project")
        #expect(e.session == "try-it" && e.duration == 252 && e.at == t0)
    }

    @Test func aSurfaceThatFailsStopsNothing() {
        struct Boom: Error {}
        let r = Recorder()
        var logs: [String] = []
        let n = AgentNotifier(
            settings: { NotificationSettings() },
            banner: { _ in throw Boom() }, glow: { r.glows.append($0) }, card: { r.cards.append($0) },
            front: FrontApp(bundleID: { nil }, name: { nil }), clock: { t0 },
            color: { $0.defaultColor }, log: { logs.append($0) })
        n.tryIt(.notification)
        n.tryIt(.glowFinished)
        #expect(logs.count == 1 && r.glows.count == 1)
    }
}

@Suite("Settings, Notifications: the section")
struct NotificationsSectionTests {
    @Test func theSectionSitsAfterAlertsWithABellBadge() {
        #expect(SettingsSection.notifications.title == "Notifications")
        #expect(SettingsSection.notifications.symbol == "bell.badge")
        #expect(SettingsSection.notifications.rawValue == "notifications")
        #expect(SettingsSection.notifications.linkTitle == "Notifications Settings…")
        let first = SettingsSection.groups[0].sections
        #expect(first == [.general, .credentials, .usage, .alerts, .notifications])
        #expect(SettingsSection.allCases.firstIndex(of: .notifications) == SettingsSection.allCases.firstIndex(of: .alerts)! + 1)
    }

    @Test func theSmokeAndLinkParsersFindIt() {
        #expect(DebugLink.section("notifications") == .notifications)
        #expect(SettingsSection.resolve("notifications")?.section == .notifications)
        #expect(SettingsSection.linked("Notifications Settings…") == .notifications)
    }

    @Test func searchFindsThePageAndItsControls() {
        let page = SettingsSearch.hits("Notifications").map(\.entry).filter { $0.page == .notifications }
        #expect(page.contains { $0.anchor == nil })
        let idle = SettingsSearch.hits("remind me when Claude Code").first?.entry
        #expect(idle?.page == .notifications)
        // "prompt" is still Claude Code's word, not the idle reminder's.
        #expect(SettingsSearch.hits("prompt").first?.entry.page != .notifications)
        #expect(SettingsSearch.hits("Try it").contains { $0.entry.page == .notifications })
        for e in SettingsAnchor.entries(on: .notifications) {
            #expect(SettingsAnchor.exists(e.anchor ?? "", on: .notifications))
        }
    }

    @Test func noPreviewForThePage() {
        #expect(SettingsPreviewKind.of(.notifications) == nil)
    }
}
