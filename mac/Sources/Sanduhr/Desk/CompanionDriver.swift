import AppKit
import ImageIO
import Observation

/// Runs the Desk's companion (spec 4). It exists only while the piece is placed, a sprite pack is
/// picked and the Desk shows: with no sprite picked it holds no timer, subscription, observer or
/// image. Main thread only. While it runs, the main thread wakes for:
///   - the shared IdleClock, every 5 s (the idle trigger, and the mood's 10-minute celebrate end);
///   - one roam boundary, every 12 s walking and 8 s resting, and only with roaming allowed;
///   - one hop at a trigger's end;
///   - one hourly growth read, whose file work runs off the main thread.
/// Frames and the walk are keyframe animations the render server steps (CompanionNSView).
@Observable
final class CompanionDriver {
    /// The pack drawn, nil while nothing draws. Observed: the piece shows only with one.
    private(set) var pick: CompanionPick?
    /// The stage's cell times the pack's scale, in points at piece size 1. Observed.
    private(set) var cell = CGSize(width: 1, height: 1)

    @ObservationIgnored private weak var model: DeskModel?
    @ObservationIgnored private weak var view: CompanionNSView?
    @ObservationIgnored private var shown = false
    @ObservationIgnored private var pieces = CompanionPieceViews()
    @ObservationIgnored private var settings = CompanionSettings()
    @ObservationIgnored private var demo = false
    @ObservationIgnored private var demoStart = Date()
    @ObservationIgnored private var brain = CompanionBrain()
    @ObservationIgnored private var animator: CompanionAnimator?
    @ObservationIgnored private var stage: CompanionStage?
    @ObservationIgnored private var sheet: (image: CGImage, width: Int, height: Int)?
    @ObservationIgnored private var growthState = CompanionGrowthState.empty
    @ObservationIgnored private var growthReader: CompanionGrowthReader?
    @ObservationIgnored private var growthTimer: Timer?
    @ObservationIgnored private var idleToken: IdleClock.Token?
    @ObservationIgnored private var observers: [(NotificationCenter, NSObjectProtocol)] = []
    @ObservationIgnored private var lastKey: PlayKey?
    @ObservationIgnored private var triggerEnd = Date.distantPast
    @ObservationIgnored private var triggerTimer: Timer?
    @ObservationIgnored private var paused = false
    @ObservationIgnored private var lastMood = CompanionMood.content
    /// A proposed sprite drawn from memory in place of the saved choice (WidgetProposalHandoff). Never saved.
    @ObservationIgnored private var previewed: CompanionPreview?

    /// What the render server is playing, so a refresh that changes nothing changes nothing.
    private struct PlayKey: Equatable {
        let animation: String
        let oneShot: Bool
        let fps: Int
        let stage: String
    }

    // MARK: Roam

    private struct Segment {
        let walking: Bool
        var startedAt: Date
        let length: TimeInterval
    }

    @ObservationIgnored private var roamer = CompanionRoamer()
    @ObservationIgnored private var roamerBuilt = false
    @ObservationIgnored private var path: CompanionPath?
    @ObservationIgnored private var segment: Segment?
    @ObservationIgnored private var segmentTimer: Timer?
    @ObservationIgnored private var pausedAt: Date?

    // MARK: Showing and hiding

    /// A companion piece appeared: read the choice, and start what the pack needs. Idempotent.
    func show(_ model: DeskModel) {
        self.model = model
        pieces.appeared()
        guard !shown else { reload(force: true); return }
        shown = true
        brain = CompanionBrain()
        demoStart = Date()
        reload(force: true)
        let ws = NSWorkspace.shared.notificationCenter
        observers.append((ws, ws.addObserver(forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification,
                                             object: nil, queue: .main) { [weak self] _ in self?.reduceMotionChanged() }))
        let nc = NotificationCenter.default
        observers.append((nc, nc.addObserver(forName: UserDefaults.didChangeNotification, object: nil,
                                             queue: .main) { [weak self] _ in self?.reload(force: false) }))
        observers.append((nc, nc.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil,
                                             queue: .main) { [weak self] _ in self?.reload(force: true) }))
        observers.append((nc, nc.addObserver(forName: .widgetPacksDidChange, object: nil,
                                             queue: .main) { [weak self] _ in self?.reload(force: true) }))
    }

    /// A companion piece's view went away. The driver stops only when no piece shows: moving the piece
    /// rebuilds its view, and SwiftUI calls the new view's onAppear before the old one's onDisappear.
    func pieceDisappeared() {
        if pieces.disappeared() { hide() }
    }

    /// The piece went away (Hidden, or the Desk stopped): nothing runs.
    func hide() {
        pieces = CompanionPieceViews()
        guard shown else { return }
        shown = false
        for (center, token) in observers { center.removeObserver(token) }
        observers = []
        stopEverything()
        pick = nil
        lastKey = nil
    }

    func attach(_ view: CompanionNSView) {
        self.view = view
        lastKey = nil
        if sheet != nil { applySheet(); refresh() }
    }

    // MARK: Choice

    /// A proposed sprite becomes the companion for now, or (nil) the saved choice comes back. The saved
    /// `CompanionSettings` are neither read into nor written from the preview.
    func setPreview(_ preview: CompanionPreview?) {
        previewed = preview
        guard shown else { return }
        // Same pack id, other art: drop what is drawn so the next pick loads fresh.
        stopEverything()
        pick = nil
        lastKey = nil
        reload(force: true)
    }

    /// Reads the settings and, when they (or the demo switch) changed, the pack. Cheap otherwise.
    func reload(force: Bool) {
        guard shown else { return }
        let now = CompanionSettings.load()
        let nowDemo = model?.demo ?? false
        if !force, now == settings, nowDemo == demo { return }
        let changed = now != settings || nowDemo != demo || force
        settings = now
        demo = nowDemo
        guard changed else { return }
        let next = previewed.map { CompanionPick(packId: $0.packId, sprite: $0.sprite) }
            ?? CompanionPicker.pick(store: .shared, settings: settings, demo: demo)
        if next == pick, next != nil {
            refresh()
            return
        }
        stopEverything()
        pick = next
        lastKey = nil
        guard let next else { return }
        animator = CompanionAnimator(next.sprite)
        if demo {
            demoStart = Date()
            growthState = CompanionGrowthState(growth: CompanionDemo.growth, days: [], active: 0)
            applyGrowth()
        } else {
            startGrowth()
            observeBaselines()
        }
        startIdle()
    }

    /// Settings the brain's first reading from what the Desk already holds (a baseline fires nothing).
    private func observeBaselines() {
        guard let model else { return }
        usageChanged(model.lastUsage)
        _ = brain.observeWatchers(model.watchers)
    }

    private func stopEverything() {
        stopSegment(commit: false)
        triggerTimer?.invalidate(); triggerTimer = nil
        growthTimer?.invalidate(); growthTimer = nil
        growthReader = nil
        if let token = idleToken { MainActor.assumeIsolated { IdleClock.shared.unsubscribe(token) } }
        idleToken = nil
        view?.clear()
        animator = nil
        stage = nil
        sheet = nil
        path = nil
        roamerBuilt = false
    }

    // MARK: Growth and stage

    private func startGrowth() {
        let reader = CompanionGrowthReader(stateFile: CompanionGrowthState.file(in: WidgetStore.shared.root)) {
            let folders = Set(AccountData.allChoices(in: KeychainStore.accounts.defaults).values
                .compactMap { LocalActivity.folder(for: $0) })
            return CompanionGrowthReader.claudeCodeDays(folders.sorted().map { CCLogReader(root: $0) })
        }
        growthReader = reader
        let t = Timer(timeInterval: 3600, repeats: true) { [weak self] _ in self?.readGrowth() }
        t.tolerance = 300
        RunLoop.main.add(t, forMode: .common)
        growthTimer = t
        readGrowth()
    }

    /// The state file and the day sources are read off the main thread; the result lands here.
    private func readGrowth() {
        guard let reader = growthReader else { return }
        let was = growthState
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let base = was == .empty ? reader.loadState() : was
            let next = reader.refresh(from: base)
            DispatchQueue.main.async {
                guard let self, self.growthReader === reader else { return }
                self.growthState = next
                self.applyGrowth()
            }
        }
    }

    /// The stage growth has reached; a different one swaps the sheet.
    private func applyGrowth() {
        guard let sprite = pick?.sprite else { return }
        let next = sprite.stage(at: growthState.growth)
        guard next != stage || sheet == nil else { return }
        stage = next
        sheet = loadSheet(next)
        cell = CGSize(width: Double(next.frameWidth * sprite.scale), height: Double(next.frameHeight * sprite.scale))
        path = nil
        lastKey = nil
        applySheet()
        refresh()
    }

    private func loadSheet(_ stage: CompanionStage) -> (image: CGImage, width: Int, height: Int)? {
        guard let id = pick?.packId else { return nil }
        if let previewed, previewed.packId == id { return previewed.sheet(for: stage) }
        let url = CompanionSheet.drawnFile(store: .shared, packId: id, stage: stage)
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
        return (image, image.width, image.height)
    }

    private func applySheet() {
        view?.setImage(sheet?.image)
    }

    // MARK: Feeding the brain

    /// The active account's numbers (DeskModel.update). Veiled numbers are the old account's.
    func usageChanged(_ input: DeskUsage) {
        guard shown, !demo, pick != nil, !input.veiled else { return }
        let usage = input.usage.map { CompanionUsage($0, signInNeeded: input.signInNeeded) }
            ?? CompanionUsage(tiers: [], signInNeeded: input.signInNeeded)
        let fired = brain.observeUsage(usage, alerts: AlertSettings(UserDefaults.standard), now: Date())
        fired.forEach(play)
        refresh()
    }

    func watchersChanged(_ list: [Watcher]) {
        guard shown, !demo, pick != nil else { return }
        brain.observeWatchers(list).forEach(play)
        refresh()
    }

    private func startIdle() {
        idleToken = MainActor.assumeIsolated {
            IdleClock.shared.subscribe(every: 5) { [weak self] seconds in self?.idle(seconds) }
        }
    }

    private func idle(_ seconds: TimeInterval) {
        guard shown, !paused else { return }
        if !demo, let t = brain.observeActivity(idleSeconds: seconds) { play(t) }
        refresh()
    }

    private func play(_ trigger: CompanionTrigger) {
        guard let animator else { return }
        let now = Date()
        animator.play(trigger, at: now)
        triggerEnd = now.addingTimeInterval(animator.duration(of: trigger, reduceMotion: reduceMotion))
        lastKey = nil
        stopSegment(commit: true)
    }

    // MARK: Drawing

    private var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    private func reduceMotionChanged() {
        lastKey = nil
        refresh()
    }

    private func mood(at now: Date) -> CompanionMood {
        demo ? CompanionDemo.mood(secondsIn: now.timeIntervalSince(demoStart)) : brain.mood(at: now)
    }

    /// Asks what should play now and tells the render server only when that changed.
    func refresh() {
        guard shown, let sprite = pick?.sprite, let animator, let view, let stage, let sheet else { return }
        let now = Date()
        let reduce = reduceMotion
        let mood = mood(at: now)
        lastMood = mood
        updateRoam(mood: mood, reduce: reduce, now: now)
        let walking = segment?.walking == true
        let pose = animator.pose(mood: mood, walking: walking, at: now, reduceMotion: reduce)
        let fps = CompanionAnimator.fps(for: sprite, reduceMotion: reduce)
        let key = PlayKey(animation: pose.animation, oneShot: pose.oneShot, fps: fps, stage: stage.id)
        guard key != lastKey else { return }
        lastKey = key
        let rects = CompanionFrames.rects(sprite.frames(pose.animation), sheetWidth: sheet.width, sheetHeight: sheet.height,
                                          frameWidth: stage.frameWidth, frameHeight: stage.frameHeight)
        view.play(rects, fps: fps, repeatForever: !pose.oneShot)
        triggerTimer?.invalidate(); triggerTimer = nil
        if pose.oneShot { armTrigger(after: max(0.05, triggerEnd.timeIntervalSince(now) + 0.02)) }
    }

    private func armTrigger(after seconds: TimeInterval) {
        guard !paused else { triggerLeft = seconds; return }
        let t = Timer(timeInterval: seconds, repeats: false) { [weak self] _ in
            self?.triggerTimer = nil
            self?.lastKey = nil
            self?.refresh()
        }
        RunLoop.main.add(t, forMode: .common)
        triggerTimer = t
    }

    @ObservationIgnored private var triggerLeft: TimeInterval?
    @ObservationIgnored private var segmentLeft: TimeInterval?

    // MARK: Roaming

    /// Starts, keeps or stops the walk to match what is allowed now.
    private func updateRoam(mood: CompanionMood, reduce: Bool, now: Date) {
        guard let animator else { return }
        let allowed = CompanionRoamer.allowed(reduceMotion: reduce, mood: mood,
                                              triggerPlaying: animator.playing(at: now, reduceMotion: reduce))
        guard allowed else {
            stopSegment(commit: true)
            if reduce { view?.goHome() }
            return
        }
        if segment == nil, !paused { startSegment(now: now) }
    }

    /// The Desk's room: its window less the menu bar and the Dock.
    private func buildPath() -> CompanionPath? {
        guard let model, let sprite = pick?.sprite, let view else { return nil }
        let size = CGSize(width: view.cell.width, height: view.cell.height)
        let area = CompanionRoamMath.area(window: model.deskSize, topInset: model.topInset, dock: model.dockInsets)
        return CompanionPath.build(edges: sprite.edges, area: area, spriteWidth: size.width, spriteHeight: size.height)
    }

    private func startSegment(now: Date) {
        guard let sprite = pick?.sprite, let view else { return }
        guard let built = buildPath() else { return }
        path = built
        if !roamerBuilt {
            let home = view.homePoint() ?? .zero
            roamer = CompanionRoamer(start: built.nearest(home.x, home.y))
            roamerBuilt = true
        }
        let length = max(0.05, roamer.secondsLeftInPhase)
        let walking = roamer.walking
        if walking {
            let d0 = roamer.distance(speed: sprite.speedPxPerSecond)
            let d1 = d0 + length * sprite.speedPxPerSecond
            let spots = CompanionRoamMath.samples(built, from: d0, to: d1,
                                                  count: max(2, Int(length / CompanionRoamMath.sampleEvery)))
            view.walk(spots, duration: length)
        } else {
            view.hold(at: built.at(roamer.distance(speed: sprite.speedPxPerSecond)))
        }
        segment = Segment(walking: walking, startedAt: now, length: length)
        lastKey = nil
        armSegment(after: length)
    }

    private func armSegment(after seconds: TimeInterval) {
        segmentTimer?.invalidate()
        let t = Timer(timeInterval: seconds, repeats: false) { [weak self] _ in self?.segmentEnded() }
        t.tolerance = 0.2
        RunLoop.main.add(t, forMode: .common)
        segmentTimer = t
    }

    /// One boundary: the part of the cycle ran out. Count it, stand where it ended, ask what is next.
    private func segmentEnded() {
        segmentTimer = nil
        guard let seg = segment else { return }
        commit(seg.length)
        segment = nil
        stand()
        lastKey = nil
        refresh()
    }

    /// Ends the segment now (a trigger, sleep, roaming switched off). `commit` counts the time it ran.
    private func stopSegment(commit counts: Bool) {
        segmentTimer?.invalidate(); segmentTimer = nil
        segmentLeft = nil
        guard let seg = segment else { return }
        segment = nil
        if counts {
            let ran = min(seg.length, max(0, (pausedAt ?? Date()).timeIntervalSince(seg.startedAt)))
            commit(ran)
            stand()
        }
        lastKey = nil
    }

    private func commit(_ seconds: TimeInterval) {
        var left = seconds
        while left > 0 {
            let chunk = min(1, left)
            roamer.advance(chunk, allowed: true)
            left -= chunk
        }
    }

    private func stand() {
        guard let path, let sprite = pick?.sprite else { return }
        view?.hold(at: path.at(roamer.distance(speed: sprite.speedPxPerSecond)))
    }

    // MARK: Pausing

    /// Out of sight (MessageMotion.paused): the layers hold, and so do the timers, which carry on with
    /// the time they had left.
    func setPaused(_ value: Bool) {
        guard value != paused else { return }
        paused = value
        guard shown, pick != nil else { return }
        let now = Date()
        view?.setPaused(value)
        if value {
            pausedAt = now
            if let t = segmentTimer { segmentLeft = max(0.05, t.fireDate.timeIntervalSince(now)) }
            if let t = triggerTimer { triggerLeft = max(0.05, t.fireDate.timeIntervalSince(now)) }
            segmentTimer?.invalidate(); segmentTimer = nil
            triggerTimer?.invalidate(); triggerTimer = nil
        } else {
            let gone = pausedAt.map { now.timeIntervalSince($0) } ?? 0
            pausedAt = nil
            if var seg = segment {
                seg.startedAt = seg.startedAt.addingTimeInterval(gone)
                segment = seg
                if let left = segmentLeft { armSegment(after: left) }
            }
            segmentLeft = nil
            if let left = triggerLeft { triggerEnd = now.addingTimeInterval(left); armTrigger(after: left) }
            triggerLeft = nil
            refresh()
        }
    }

    // MARK: Debug

    /// state.yaml's `companion:`, nil while the piece is not shown.
    var debug: CompanionDebug? {
        guard shown, let pick, let stage else { return nil }
        return CompanionDebug(pack: pick.packId, stage: stage.id, mood: lastMood.key, roaming: segment != nil)
    }
}

/// How many companion piece views are on screen. Their onAppear and onDisappear can come in either
/// order when SwiftUI rebuilds the piece, so the driver counts instead of trusting the last call.
struct CompanionPieceViews: Equatable {
    private(set) var count = 0

    mutating func appeared() { count += 1 }

    /// True when this was the last one.
    @discardableResult
    mutating func disappeared() -> Bool {
        count = max(0, count - 1)
        return count == 0
    }
}
