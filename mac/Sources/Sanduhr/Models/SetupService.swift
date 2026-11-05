import Foundation

/// What setup changes, as a seam for tests (setup spec, sections 10 and 11).
@MainActor
protocol SetupHost: AnyObject {
    var drawersOn: Bool { get }
    /// A rules review was applied once: ticking drawers opens the first review only before that.
    var drawersEverSorted: Bool { get }
    var drawerFiles: Int { get }
    var addOnsInstalled: Bool { get }
    var usageOn: Bool { get }
    /// An account is saved (usage off keeps it, and says so only when there is one).
    var hasAccount: Bool { get }
    /// Turns drawers off the way the Drawers page does: the put-back question, the review dismissed, the files returned.
    func turnOffDrawers() -> SetupDrawersOff
    /// Turns drawers on the way the Drawers page does.
    func turnOnDrawers()
    /// Opens the rules' review; nil when it opened, otherwise the line to show.
    func sortNow() -> String?
    func setUsage(_ on: Bool)
    func reloadDesk()
}

/// Turning drawers off: whether they are off now, and the line to show (nil for a plain yes or a plain no).
struct SetupDrawersOff: Equatable {
    var off: Bool
    var line: String?
}

struct SetupResult: Equatable {
    var now: SetupChoices
    var lines: [String]
    var reviewOpened: Bool
}

/// Where the setup lives. Saving can fail, so a test can make it.
struct SetupStorage {
    var loadSetup: () -> SetupSettings?
    var deskEnabled: () -> Bool
    /// Saves the setup and the Desk switch together.
    var save: (_ setup: SetupSettings, _ deskRuns: Bool) throws -> Void

    struct SaveError: Error {}

    static func defaults(setup: DefaultsStore, desk: DefaultsStore) -> SetupStorage {
        SetupStorage(
            loadSetup: { SetupStore.load(from: setup) },
            deskEnabled: { desk.bool(forKey: DeskController.enabledKey) },
            save: { settings, deskRuns in
                SetupStore.save(settings, to: setup)
                if desk.bool(forKey: DeskController.enabledKey) != deskRuns { desk.set(deskRuns, forKey: DeskController.enabledKey) }
            })
    }
}

/// Applies the setup screen's ticks one thing at a time (spec 10, Continue): drawers off first (it can
/// be refused), then the saved choices and the Desk, usage, drawers on, one Desk reload, and last the
/// first drawers review.
@MainActor
final class SetupService {
    private let storage: SetupStorage
    private let host: SetupHost

    init(storage: SetupStorage, host: SetupHost) {
        self.storage = storage
        self.host = host
    }

    func current() -> SetupChoices {
        let stored = storage.loadSetup() ?? SetupChoices.firstRun.toSettings(before: nil)
        return SetupChoices.of(stored, drawersOn: host.drawersOn, deskEnabled: storage.deskEnabled())
    }

    func preview(_ after: SetupChoices) -> [String] {
        SetupChange.lines(before: current(), after: after, drawerFiles: host.drawerFiles,
                          addonsInstalled: host.addOnsInstalled, hasAccount: host.hasAccount)
    }

    func apply(_ after: SetupChoices) -> SetupResult {
        if let blocked = SetupChange.blocked(after) { return SetupResult(now: current(), lines: [blocked], reviewOpened: false) }
        var lines: [String] = []

        // Drawers off first: it can be refused (a no to the count), and then drawers stay ticked
        // while the rest of the change still applies.
        var drawers = after.drawers
        var turnedOff = false
        if !after.drawers && host.drawersOn {
            let off = host.turnOffDrawers()
            if let line = off.line { lines.append(line) }
            drawers = !off.off
            turnedOff = off.off
        }

        var now = after
        now.drawers = drawers
        // Read before the save: the app's usage switch reads the stored setup, so after the save it
        // already says the new value and the change would look like no change at all.
        let usageBefore = host.usageOn
        do {
            try storage.save(now.toSettings(before: storage.loadSetup()), now.deskRuns)
        } catch {
            lines.append(SetupCopy.saveFailed)   // nothing past the save is touched
            if turnedOff { host.reloadDesk() }   // the turn-off already happened: the Desk stops drawing the drawers
            return SetupResult(now: current(), lines: lines, reviewOpened: false)
        }

        if usageBefore != now.claudeUsage { host.setUsage(now.claudeUsage) }
        let turnedOn = now.drawers && !host.drawersOn
        if turnedOn {
            host.turnOnDrawers()
            lines.append(SetupCopy.drawersFirstOnLine)
        }
        host.reloadDesk()

        // The first review is for drawers just turned on, and only while no rules review was ever accepted.
        var review = false
        if turnedOn && !host.drawersEverSorted {
            let said = host.sortNow()
            review = said == nil
            if let said, said != SetupCopy.nothingToSort { lines.append(said) }
        }
        return SetupResult(now: now, lines: lines, reviewOpened: review)
    }
}

/// The running app's half of setup: drawers on and off behave as the Drawers page does.
@MainActor
final class AppSetupHost: SetupHost {
    private let drawers: DrawerService
    private let usage: UsageViewModel
    private let confirm: (Int) -> Bool

    /// `confirm` shows the Drawers page's "Put N files back on the Desktop?" and answers it.
    init(drawers: DrawerService = .shared, usage: UsageViewModel, confirm: @escaping (Int) -> Bool) {
        self.drawers = drawers
        self.usage = usage
        self.confirm = confirm
    }

    var drawersOn: Bool { drawers.settings.enabled }
    var drawersEverSorted: Bool { drawers.settings.everSorted || drawers.runs.contains { $0.source == .rules } }
    var drawerFiles: Int { drawers.offCount() }
    var usageOn: Bool { usage.usageOn }
    var hasAccount: Bool { !usage.accountLabels.isEmpty }

    var addOnsInstalled: Bool {
        let installer = IntegrationInstaller.standard
        return installer.installedFolders().contains { folder in
            installer.status(.mcp, folder: folder).isOurs || installer.status(.statusline, folder: folder).isOurs
        }
    }

    func turnOffDrawers() -> SetupDrawersOff {
        let count = drawers.offCount()
        if count > 0 && !confirm(count) { return SetupDrawersOff(off: false, line: nil) }
        let result = drawers.confirmTurnOff()
        let line = count == 0 && result.refusal == nil ? nil : DrawersSettingsModel.returnLine(result)
        return SetupDrawersOff(off: !drawers.settings.enabled, line: line)
    }

    func turnOnDrawers() { drawers.turnOn() }

    func sortNow() -> String? {
        if drawers.sort() != nil { return nil }
        return drawers.desktopPermission == .denied ? DrawersSettingsModel.deniedLine : SetupCopy.nothingToSort
    }

    /// The setup is already saved with usage `on`: starts or stops what follows from it.
    func setUsage(_ on: Bool) { usage.usageSwitch.applySaved() }

    func reloadDesk() {
        DeskController.shared.apply()
        NotificationCenter.default.post(name: .sanduhrSetupDidChange, object: nil)
    }
}
