import Foundation
import EventKit
import Observation
import CoreGraphics
import AppKit

struct Meeting: Identifiable {
    let id: String
    let time: String
    let title: String
    let start: Date
    let end: Date
    /// Join link found in the event (Teams, Zoom, Google Meet, Webex), if any.
    let link: URL?
    /// Short name for the link's service, shown after the title on the desktop.
    let service: String?
}

/// What the Desk says about Calendar access, by authorization status. Pure, so each status is
/// tested; DeskModel reads the status and draws the note.
enum CalendarAccess {
    static let deniedNote = "Allow Sanduhr in Settings, Privacy, Calendars"
    static let writeOnlyNote = "Sanduhr needs Full Access in Settings, Privacy, Calendars"
    static let restrictedNote = "Calendar access is restricted on this Mac"

    /// Privacy & Security, Calendars in System Settings.
    static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")!

    /// The note under the meetings, or nil when there is nothing to explain (full access, or
    /// not asked yet: the request is on its way).
    static func note(for status: EKAuthorizationStatus) -> String? {
        switch status {
        case .fullAccess, .notDetermined: return nil
        case .writeOnly: return writeOnlyNote
        case .restricted: return restrictedNote
        case .denied: return deniedNote
        @unknown default: return deniedNote
        }
    }

    /// macOS shows its prompt only while the status is not determined; asking later does nothing.
    static func shouldRequest(_ status: EKAuthorizationStatus) -> Bool { status == .notDetermined }
}

/// Today's remaining timed meetings, read straight from macOS Calendar (no icalBuddy).
/// Refreshes every 5 minutes and whenever Calendar reports a change.
@Observable
final class DeskModel {
    var meetings: [Meeting] = []
    var calendarNote: String?
    /// One row per Claude limit for the meters piece, in the widget's order, hidden limits left out.
    var meters: [DeskMeterRow] = []
    /// Every limit the server reported with a utilization, hidden or not, in the widget's order:
    /// Settings, Desk, Meters lists these, so a hidden limit can be shown again.
    var reportedTiers: [Tier] = []
    /// The reported limits believed temporary (LimitLifetime): only these get Settings' "Show
    /// this limit" switch.
    var temporaryTiers: Set<Tier> = []
    /// One line of Claude usage, or nil when there is none.
    var claudeLine: String?
    /// The line in two pieces (DeskClaudeText.parts): with two or more accounts the Desk draws the
    /// label as its own clickable element, which cycles to the next account.
    var claudeParts: DeskClaudeText.Parts?
    /// The numbers are older than 15 minutes or the sign-in was refused; the meters and the
    /// line draw dimmed.
    var claudeLineIsStale = false
    /// The widget's session key or Cloudflare clearance was refused.
    var signInNeeded = false
    /// An account switch is under way (AccountSwitchFade): the meters and the line keep the old
    /// account's layout, drawn unseen, until the new numbers fade in.
    var veiled = false
    /// The switch's fetch outlasts the fade: the faint "switching account…".
    var switchNote = false
    /// Today's usual line from MessageEngine (messages.txt, or the pin), or nil when there is none.
    var message: String?
    /// Today's date lines (item 69), drawn above the usual line; empty on most days.
    var specialMessages: [String] = []
    /// On special days: Stack, Take turns or Scroll (MessageSpecialMode), and how long each line
    /// shows while they take turns.
    var specialMode: MessageSpecialMode = .stack
    var specialSeconds: Double = MessageSpecialMode.defaultSeconds
    /// While the lines take turns: which of `messageLines` shows. Moves with the clock
    /// (MessageSpecialMode.index), rests while the Desk can't be seen.
    var cycleIndex = 0
    @ObservationIgnored private var cycleTimer: Timer?

    /// Everything the message piece draws today, in cycle order: the date's lines, then the usual one.
    var messageLines: [String] { specialMessages + [message].compactMap { $0 } }
    /// Take turns or Scroll on a special day.
    var cycling: Bool {
        MessageSpecialMode.cycles(specialMode, today: MessageEngine.Today(special: specialMessages, usual: message))
    }
    /// The line showing now while they take turns, nil otherwise.
    var cycleLine: String? {
        guard cycling else { return nil }
        let lines = messageLines
        return lines[cycleIndex % lines.count]
    }
    /// For places with room for one line (the notch): the line taking its turn, else the first
    /// special line, else the usual one.
    var oneLineMessage: String? { cycleLine ?? specialMessages.first ?? message }

    /// Moves the turn to the clock's line and waits for the next change; while the Desk can't be
    /// seen (`motionPaused`) the turn holds and nothing waits. Nothing runs without a cycle.
    func updateCycle(now: Date = Date()) {
        cycleTimer?.invalidate()
        cycleTimer = nil
        guard cycling, !motionPaused else { return }
        let index = MessageSpecialMode.index(at: now, count: messageLines.count, seconds: specialSeconds)
        if cycleIndex != index { cycleIndex = index }
        let wait = MessageSpecialMode.nextChange(after: now, seconds: specialSeconds).timeIntervalSince(now)
        cycleTimer = Timer.scheduledTimer(withTimeInterval: max(0.05, wait), repeats: false) { [weak self] _ in
            self?.updateCycle()
        }
    }

    /// The saved On special days choice and timing (Settings, Message).
    func applySpecialSettings(mode: MessageSpecialMode, seconds: Double, now: Date = Date()) {
        if specialMode != mode { specialMode = mode }
        if specialSeconds != seconds { specialSeconds = seconds }
        updateCycle(now: now)
    }
    /// Nobody can see the Desk (covered, screens asleep, screen saver, session switched away):
    /// the message's {shimmer} rests (MessageMotion). Set by DeskController.
    var motionPaused = false
    /// Height of the menu bar strip at the top of the screen, so top slots sit below it.
    var topInset: CGFloat = 0
    /// How far the Dock reaches into the screen on each side (item 56, DockFollower): the Desk's
    /// corners on that side sit this much further in. Changes with an animation while an
    /// auto-hiding Dock comes and goes.
    var dockInsets = DockInsets()
    /// The camera notch in window coordinates (top-left origin), or nil on screens without one.
    var notchRect: CGRect?
    /// The Claude meters, short enough for the notch: "5h 7%  wk 63%".
    var claudeCompact: String?
    /// What plays, after Now Playing's hide rules (NowPlayingController), nil for nothing to show.
    /// In memory only.
    var nowPlaying: NowPlayingInfo?
    /// The watchers that show (item 66), most urgent first: every watcher but the work ones while
    /// demo mode is on (WatcherBoard.shown). In memory only. Set by WatcherStore.
    var watchers: [Watcher] = []
    /// Every watcher, before the demo filter.
    @ObservationIgnored private(set) var allWatchers: [Watcher] = []
    /// While the notch plays a watcher's intro (WatcherIntro: the full line once, then the short
    /// one), when it ends; nil at rest. Observed, so the wings' width follows the phase.
    var watcherIntroUntil: Date?
    /// The top watcher's id and state the last intro was for (WatcherIntro.key).
    @ObservationIgnored private var watcherIntroKey: String?
    @ObservationIgnored private var watcherIntroTimer: Timer?
    /// Reduce Motion: no intro, straight to rest. A seam for the tests.
    @ObservationIgnored var reduceMotion: () -> Bool = { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    /// Each Desk watcher row's frame (SwiftUI global coordinates), keyed by watcher id: a click
    /// opens its link, a two-finger click opens the watcher menu.
    @ObservationIgnored var watcherRowFrames: [String: CGRect] = [:]
    /// The Desk's watcher stack as a whole, or .zero when it is not drawn (the pointer watch).
    @ObservationIgnored var watchersFrame: CGRect = .zero { didSet { if watchersFrame != oldValue { onHitAreasChange?() } } }
    /// Where the strip under the camera draws the watcher line, same coordinates.
    @ObservationIgnored var stripWatcherFrame: CGRect = .zero { didSet { if stripWatcherFrame != oldValue { onHitAreasChange?() } } }
    /// Where the meeting list sits in the window (SwiftUI global coordinates, top-left origin).
    /// The app delegate lets clicks through everywhere except here, so the rows can be clicked.
    @ObservationIgnored var meetingsFrame: CGRect = .zero { didSet { if meetingsFrame != oldValue { onHitAreasChange?() } } }
    /// Each meeting row's frame, same coordinates, keyed by meeting id. Clicks are matched here.
    @ObservationIgnored var rowFrames: [String: CGRect] = [:]
    /// Where the meters sit in the window, same coordinates, or .zero when they are not drawn.
    /// The window takes the mouse here for the two-finger limit menu; a plain click does nothing.
    @ObservationIgnored var metersFrame: CGRect = .zero { didSet { if metersFrame != oldValue { onHitAreasChange?() } } }
    /// Each meter row's frame, same coordinates: a two-finger click opens that limit's menu.
    @ObservationIgnored var meterRowFrames: [Tier: CGRect] = [:]
    /// Where the calendar note sits, same coordinates, or .zero when it is not drawn. A click
    /// here opens System Settings at Privacy & Security, Calendars.
    @ObservationIgnored var noteFrame: CGRect = .zero { didSet { if noteFrame != oldValue { onHitAreasChange?() } } }
    /// Where the account label at the start of the claude line sits, same coordinates, or .zero
    /// with one account or no line. A click here switches to the next account.
    @ObservationIgnored var accountFrame: CGRect = .zero { didSet { if accountFrame != oldValue { onHitAreasChange?() } } }
    /// Where the Desk's now playing line sits, same coordinates. A click plays or pauses.
    @ObservationIgnored var nowPlayingFrame: CGRect = .zero { didSet { if nowPlayingFrame != oldValue { onHitAreasChange?() } } }
    /// Where the text in the strip under the camera sits, same coordinates (clickable while it
    /// shows now playing).
    @ObservationIgnored var stripFrame: CGRect = .zero { didSet { if stripFrame != oldValue { onHitAreasChange?() } } }
    /// Where the strip's Next button sits while paused (item 53b), same coordinates. A click skips.
    @ObservationIgnored var stripNextFrame: CGRect = .zero { didSet { if stripNextFrame != oldValue { onHitAreasChange?() } } }
    /// The camera and mic indicators that show (item 67), after their switches: in-use booleans
    /// only, never which app. Set by AVIndicatorController.
    var avIndicators = AVIndicators()
    /// Where they draw now (AVIndicatorPlacement). Set by AVIndicatorController.
    var avSpot = AVIndicatorSpot.none
    /// The last indicators that showed: what the slot beside the camera draws while it fades out,
    /// so it fades the dot that was there rather than going blank. Set by AVIndicatorController.
    var avDrawn = AVIndicators()
    /// Where the strip under the camera draws them, same coordinates: a click opens their menu.
    @ObservationIgnored var stripAVFrame: CGRect = .zero { didSet { if stripAVFrame != oldValue { onHitAreasChange?() } } }
    /// Called when a clickable piece moves or comes and goes (DeskController takes the mouse there).
    @ObservationIgnored var onHitAreasChange: (() -> Void)?
    /// Arrange mode (item 60): the layout being edited on the desktop, nil-session outside it.
    let arrange = DeskArrangeMode()
    /// Alert pulses so far, per limit (Settings, Alerts, Where alerts show). A meter row pulses
    /// when its count goes up.
    var pulses: [Tier: Int] = [:]
    /// Every pulse so far, whatever the limit; the notch island pulses when it goes up.
    var pulseCount = 0

    /// The widget's last numbers (see `update`).
    @ObservationIgnored private var usage = DeskUsage()
    /// The numbers last handed over, for Settings' previews (item 68): they feed their own model
    /// the same input, so its rows come from the same functions.
    var lastUsage: DeskUsage { usage }

    /// Replaced when access turns full: a store made before the grant can keep the old answer.
    @ObservationIgnored private var store = EKEventStore()
    /// The status seen at the last check, so a change to full access is noticed once.
    @ObservationIgnored private var calendarStatus: EKAuthorizationStatus?
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var claudeTimer: Timer?
    @ObservationIgnored private var storeObserver: NSObjectProtocol?
    @ObservationIgnored private var defaultsObserver: NSObjectProtocol?

    func start() {
        // Calendar access is asked only when meetings are on (Desk settings, General).
        if Self.meetingsOn { requestCalendar() }
        observeStore()
        observeMeterSettings()
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.refreshEvents()
        }
        MessageEngine.ensureFile()
        refreshClaude()
        refreshMessage()
        claudeTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.refreshClaude()
            if self?.demo != true { self?.refreshMessage() }   // picks up messages.txt edits within a minute
            // A grant made in System Settings shows within a minute, no relaunch.
            if self?.calendarStatus != .fullAccess { self?.recheckCalendar() }
        }
    }

    private static var meetingsOn: Bool { UserDefaults.desk.object(forKey: "showMeetings") as? Bool ?? true }

    /// A Meters setting changed in Settings: restyle the rows now, not at the next minute.
    /// Any in-process defaults change counts (cheap: unchanged rows are not reassigned). A
    /// `defaults write` from another process posts nothing; the minute refresh picks that up.
    private func observeMeterSettings() {
        guard defaultsObserver == nil else { return }
        defaultsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.refreshMeterWarnings() }
    }

    private func observeStore() {
        if let storeObserver { NotificationCenter.default.removeObserver(storeObserver) }
        storeObserver = NotificationCenter.default.addObserver(
            forName: .EKEventStoreChanged, object: store, queue: .main
        ) { [weak self] _ in self?.refreshEvents() }
    }

    /// Asks for Calendar access (macOS shows its prompt only the first time) and loads today's
    /// meetings. Runs when Desk starts with meetings on, and when meetings are switched on later:
    /// new installs start with them off.
    func requestCalendar() {
        let status = EKEventStore.authorizationStatus(for: .event)
        guard CalendarAccess.shouldRequest(status) else { applyCalendar(status); return }
        store.requestFullAccessToEvents { [weak self] _, _ in
            DispatchQueue.main.async {
                self?.applyCalendar(EKEventStore.authorizationStatus(for: .event))
            }
        }
    }

    /// Reads the authorization again: when Sanduhr becomes active, when Settings opens and each
    /// minute while access is not full. Only while meetings are on. Asks if never asked.
    func recheckCalendar() {
        guard Self.meetingsOn else { return }
        requestCalendar()
    }

    /// Shows the note for `status`, or, on full access, loads the meetings. A change to full
    /// access gets a fresh store, so the old one's stale answer cannot hide the meetings.
    private func applyCalendar(_ status: EKAuthorizationStatus) {
        let previous = calendarStatus
        calendarStatus = status
        if status == .fullAccess {
            if previous != nil, previous != .fullAccess {
                store = EKEventStore()
                if storeObserver != nil { observeStore() }
            }
            refreshEvents()
        } else {
            meetings = []
            let note = CalendarAccess.note(for: status)
            if calendarNote != note { calendarNote = note }
        }
    }

    /// The calendar note was clicked: Privacy & Security, Calendars in System Settings.
    func openCalendarSettings() {
        NSWorkspace.shared.open(CalendarAccess.settingsURL)
    }

    func stop() {
        timer?.invalidate(); timer = nil
        claudeTimer?.invalidate(); claudeTimer = nil
        // A block observer is removed by its token; removeObserver(self) would leave it behind
        // and every Desk off and on would add another.
        if let storeObserver { NotificationCenter.default.removeObserver(storeObserver) }
        storeObserver = nil
    }

    /// Demo data for screenshots (`smoke do demo on`): while on, the calendar refresh and the
    /// message timer leave these alone. Nothing is written to the calendar or to defaults.
    @ObservationIgnored private(set) var demo = false

    /// The watchers, most urgent first (WatcherStore); the work ones hide while demo mode is on.
    func setWatchers(_ all: [Watcher]) {
        allWatchers = all
        let shown = WatcherBoard.shown(all, demo: demo)
        if shown != watchers { watchers = shown }
        updateWatcherIntro(now: Date())
        onHitAreasChange?()
    }

    /// Starts the notch intro when the top watcher or its state changed (WatcherIntro), for as long
    /// as the full line takes in the widest wing; ends it with no watcher or with Reduce Motion.
    func updateWatcherIntro(now: Date) {
        let key = WatcherIntro.key(watchers)
        let reduce = reduceMotion()
        if WatcherIntro.starts(from: watcherIntroKey, to: key, reduceMotion: reduce),
           let line = WatcherText.fullLine(watchers, now: now) {
            let size = max(10, (notchRect?.height ?? 32) * 0.42)
            let font = DeskFont.resolve(saved: UserDefaults.desk.string(forKey: "font"))
            let room = NotchWingsView.maxWings - NowPlayingWingLayout.wingInsets - WatcherLook.dotRoom(size)
            let until = now.addingTimeInterval(WatcherIntro.duration(
                textWidth: NotchWingsView.textWidth(line, size, font), room: room))
            watcherIntroUntil = until
            watcherIntroTimer?.invalidate()
            let t = Timer(timeInterval: until.timeIntervalSince(now), repeats: false) { [weak self] _ in
                self?.endWatcherIntro()
            }
            RunLoop.main.add(t, forMode: .common)
            watcherIntroTimer = t
        } else if key == nil || reduce {
            endWatcherIntro()
        }
        watcherIntroKey = key
    }

    /// The intro is over: the notch rests on the short line.
    func endWatcherIntro() {
        watcherIntroTimer?.invalidate()
        watcherIntroTimer = nil
        if watcherIntroUntil != nil { watcherIntroUntil = nil }
        onHitAreasChange?()
    }

    func setDemo(_ on: Bool, now: Date = Date()) {
        demo = on
        setWatchers(allWatchers)
        if on {
            meetings = Self.demoMeetings(now: now)
            message = Self.demoMessage
            specialMessages = []
            updateCycle()
            calendarNote = nil
        } else {
            meetings = []
            refreshEvents()
            refreshMessage()
        }
    }

    /// Picks today's lines again: the usual one and any date lines (MessageEngine.today).
    func refreshMessage(now: Date = Date()) {
        let today = MessageEngine.today(now: now)
        if message != today.usual { message = today.usual }
        if specialMessages != today.special { specialMessages = today.special }
        applySpecialSettings(mode: .saved(), seconds: MessageSpecialMode.savedSeconds(), now: now)
    }

    /// Demo mode's message, also the Settings previews' sample line (item 68).
    static let demoMessage = "ship small. ship often. sleep anyway."

    /// Three made-up meetings: one in 12 minutes (so the notch counts it down), two later today.
    static func demoMeetings(now: Date) -> [Meeting] {
        let fmt = DateFormatter()
        fmt.dateFormat = "h:mm"
        let minute: TimeInterval = 60
        let plan: [(id: String, title: String, inMinutes: Double, length: Double, link: String?)] = [
            ("demo-1", "Design review", 12, 30, "https://zoom.us/j/1234567890"),
            ("demo-2", "Pairing: notch glow", 75, 45, "https://meet.google.com/abc-defg-hij"),
            ("demo-3", "Ship 2.3", 180, 30, nil),
        ]
        return plan.map { p in
            let start = now.addingTimeInterval(p.inMinutes * minute)
            let link = p.link.flatMap(URL.init(string:))
            return Meeting(id: p.id, time: fmt.string(from: start), title: p.title, start: start,
                           end: start.addingTimeInterval(p.length * minute), link: link,
                           service: link.flatMap(serviceName))
        }
    }

    func refreshEvents() {
        guard !demo else { return }
        guard EKEventStore.authorizationStatus(for: .event) == .fullAccess else { return }
        let now = Date()
        let endOfDay = Calendar.current.startOfDay(for: now).addingTimeInterval(24 * 60 * 60)
        let predicate = store.predicateForEvents(withStart: now, end: endOfDay, calendars: nil)
        let fmt = DateFormatter()
        fmt.dateFormat = "h:mm"
        let upcoming = store.events(matching: predicate)
            .filter { !$0.isAllDay && $0.endDate > now }
            .sorted { $0.startDate < $1.startDate }
        meetings = upcoming.prefix(3).map { event in
            let link = Self.joinLink(event)
            return Meeting(id: event.eventIdentifier ?? UUID().uuidString,
                           time: fmt.string(from: event.startDate),
                           title: event.title ?? "",
                           start: event.startDate,
                           end: event.endDate,
                           link: link,
                           service: link.flatMap(Self.serviceName))
        }
        calendarNote = nil
    }

    /// Takes the widget's latest numbers. AppDelegate calls this after every refresh, whether
    /// or not Desk or the widget is showing, so Desk has them the moment it starts.
    func update(_ input: DeskUsage) {
        usage = input
        refreshClaude()
    }

    /// Rebuilds the meters and the text lines from the last numbers the widget handed over.
    /// Runs on every update and once a minute, so pace ticks and staleness move with the clock.
    func refreshClaude(now: Date = Date()) {
        meters = Self.meterRows(usage.usage, now: now)
        // Settings' list of limits waits for the new account's numbers.
        let reported = Tier.allCases.filter { usage.usage?.tiers[$0]?.utilization != nil }
        if !usage.veiled, reported != reportedTiers { reportedTiers = reported }
        let temporary = MeterVisibility.temporary(usage.usage, now: now, store: UserDefaults.desk)
        if !usage.veiled, temporary != temporaryTiers { temporaryTiers = temporary }
        signInNeeded = usage.signInNeeded
        if veiled != usage.veiled { veiled = usage.veiled }
        if switchNote != usage.switchNote { switchNote = usage.switchNote }
        claudeLine = DeskClaudeText.line(usage)
        let parts = DeskClaudeText.parts(usage)
        if parts != claudeParts { claudeParts = parts }
        claudeCompact = DeskClaudeText.compact(usage, now: now)
        claudeLineIsStale = usage.isStale(now: now)
    }

    /// Settings' "Show this limit" switch: hiding records what the limit reads now
    /// (MeterVisibility.hide), showing clears the hide.
    func setShown(_ tier: Tier, _ shown: Bool) {
        if shown {
            MeterVisibility.show(tier, store: UserDefaults.desk)
        } else {
            MeterVisibility.hide(tier, usage: usage.usage, now: Date(), store: UserDefaults.desk)
        }
    }

    /// The meter rows for the limits that show, with the saved warning settings (Settings, Desk, Meters).
    private static func meterRows(_ usage: UsageResponse?, now: Date) -> [DeskMeterRow] {
        let desk = UserDefaults.desk
        let shown = MeterVisibility.visible(usage, hidden: MeterVisibility.hidden(in: desk))
        return DeskMeterRow.rows(from: shown, now: now) { MeterWarningSettings.saved($0, in: desk) }
    }

    /// Re-applies the warning settings at once: a Meters setting changed. Rows that already match
    /// are left alone, so the Desk only redraws when a row turns red or back.
    func refreshMeterWarnings(now: Date = Date()) {
        let rows = Self.meterRows(usage.usage, now: now)
        if rows != meters { meters = rows }
    }

    /// An alert chose the Desk: pulse these limits' meters once. `pulseCount` counts every pulse;
    /// the notch glow that goes with it is fired by `DeskController.pulse`.
    func pulse(_ tiers: Set<Tier>) {
        for tier in tiers { pulses[tier, default: 0] += 1 }
        pulseCount += 1
    }

    /// Every interactive element DeskView draws, with the frame it reported (.zero when none
    /// arrived): what DeskHitTest picks from and state.yaml's `desk_frames` lists. Reads the same
    /// layout settings as DeskView.
    func elements() -> [DeskElement] {
        let desk = UserDefaults.desk
        var input = DeskElements.Input()
        input.placed = DeskLayout.placed(desk.string(forKey: "layout") ?? DeskLayout.standard,
                                         showMeetings: desk.object(forKey: "showMeetings") as? Bool ?? true,
                                         showClaude: desk.object(forKey: "showClaude") as? Bool ?? true)
        input.meterTiers = meters.map(\.tier)
        input.signInNeeded = signInNeeded
        input.switchNote = switchNote
        input.hasAccount = claudeParts?.account != nil
        input.calendarNote = calendarNote != nil
        input.rows = meetings.map { DeskElements.Row(id: $0.id, hasLink: $0.link != nil) }
        input.metersFrame = metersFrame
        input.meterRowFrames = meterRowFrames
        input.accountFrame = accountFrame
        input.noteFrame = noteFrame
        input.meetingsFrame = meetingsFrame
        input.rowFrames = rowFrames
        input.nowPlayingLine = input.placed.contains(NowPlayingPlacement.widget) && nowPlaying != nil
        input.nowPlayingFrame = nowPlayingFrame
        let strip = NotchContent.effective(NotchContent.saved(.strip, in: desk), at: .strip,
                                           nowPlaying: nowPlaying, idle: NowPlayingIdle.saved(in: desk),
                                           watchers: watchers, indicators: avIndicators)
        let notch = desk.bool(forKey: DeskController.notchKey)
        let chin = desk.object(forKey: "notchChin") as? Double ?? 26
        let chinText = desk.bool(forKey: "notchChinText")
        input.nowPlayingStrip = DeskNowPlaying.stripShows(
            notch: notch, hasNotch: notchRect != nil, chin: chin, chinText: chinText,
            strip: strip, hasTrack: NowPlayingText.line(nowPlaying, at: .strip) != nil)
        input.stripFrame = stripFrame
        input.nowPlayingStripNext = NowPlayingWingLayout.showsNext(nowPlaying?.state)
        input.stripNextFrame = stripNextFrame
        if input.placed.contains(WatcherPlacement.widget) {
            input.watcherRows = watchers.prefix(WatcherPlacement.deskRows).map(\.id)
        }
        input.watcherRowFrames = watcherRowFrames
        input.watcherStrip = notch && notchRect != nil && chin > 0 && chinText && strip == .watchers
        input.stripWatcherFrame = stripWatcherFrame
        input.avStrip = notch && notchRect != nil && chin > 0 && chinText && strip == .avIndicators
        input.stripAVFrame = stripAVFrame
        return DeskElements.build(input)
    }

    /// The first meeting still to come (or in progress) that has a join link.
    var nextJoinable: Meeting? { meetings.first { $0.link != nil } }

    /// Looks for a join link in the event's URL, location and notes. Notes are only searched
    /// here, on this Mac; nothing but the time, title and service name is ever shown.
    static func joinLink(_ event: EKEvent) -> URL? {
        let haystack = [event.url?.absoluteString, event.location, event.notes]
            .compactMap { $0 }
            .joined(separator: " ")
        let patterns = [
            #"https://teams\.microsoft\.com/(l/meetup-join|meet)/[^\s<>"]+"#,
            #"https://teams\.live\.com/meet/[^\s<>"]+"#,
            #"https://[\w.-]*zoom\.us/j/[^\s<>"]+"#,
            #"https://meet\.google\.com/[a-z-]+"#,
            #"https://[\w.-]+\.webex\.com/[^\s<>"]+"#,
        ]
        for pattern in patterns {
            if let range = haystack.range(of: pattern, options: .regularExpression) {
                return URL(string: String(haystack[range]))
            }
        }
        return nil
    }

    static func serviceName(_ url: URL) -> String? {
        let host = url.host ?? ""
        if host.contains("teams") { return "teams" }
        if host.contains("zoom") { return "zoom" }
        if host.contains("meet.google") { return "meet" }
        if host.contains("webex") { return "webex" }
        return nil
    }
}
