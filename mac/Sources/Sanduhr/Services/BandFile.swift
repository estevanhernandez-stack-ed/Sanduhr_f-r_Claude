import AppKit
import Foundation

/// The band file (items 65f and 66): what Sanduhr hands the `sanduhr-meters` mod for its band
/// above Claude Code's prompt, `band.json` in Sanduhr's Application Support folder. The mod reads
/// it (no network); Sanduhr writes it, owner-only, on each change:
///
///     { "meters": { "styles": { "session": { "ink": ["#ff2a6d", "#05d9e8"], "bold": true } } },
///       "reduce_motion": false, "schema_version": 1, "watchers": [ … ], "written_at": "…" }
///
/// `meters.styles` are Sanduhr's segment looks from the Combine sheet's Style popovers (session,
/// weekly, resets; the statusline's own style grammar). `watchers` is there only while "Show
/// watchers above the prompt" is on: an agent's watcher as its title, short title, state,
/// progress and times; Claude Code's background work as its kind and state only, never its
/// description. `reduce_motion` is macOS's Reduce Motion. With nothing to say (no styles, the
/// switch off) the file is deleted. While watchers show, it is written again at least once a
/// minute, so the mod can tell a Sanduhr that quit or crashed (an old `written_at`) from one with
/// nothing new.
enum BandFile {
    static let name = "band.json"
    static let schemaVersion = 1
    /// Settings, Integrations, Watchers: "Show watchers above the prompt", off by default.
    static let watchersKey = "watchersInBand"
    /// Sanduhr's segment looks for the band, as the picks' `ours` JSON; set at each Combine.
    static let stylesKey = "bandMeterStyles"
    /// How often the file is written while watchers show, changed or not.
    static let heartbeat: TimeInterval = 60
    /// Sanduhr's segments the band draws (context and model stay in the statusline).
    static let segments: [SanduhrSegment] = [.session, .weekly, .resets]

    /// One watcher as the band file carries it.
    static func entry(_ w: Watcher) -> [String: Any] {
        switch w.source {
        case .automatic:
            return ["source": "automatic", "kind": w.kind ?? "task", "state": w.state.rawValue]
        case .agent:
            var o: [String: Any] = ["source": "agent", "title": w.title, "state": w.state.rawValue,
                                    "started_at": HandoffFiles.stamp(w.started)]
            if !w.short.isEmpty { o["short"] = w.short }
            if let total = w.total {
                o["total"] = total
                o["done"] = min(w.done ?? 0, total)
            }
            if let ended = w.ended { o["ended_at"] = HandoffFiles.stamp(ended) }
            return o
        }
    }

    /// The file's object without `written_at`: what decides whether a write is due.
    static func body(watchers: [Watcher]?, styles: [SanduhrSegment: SegmentStyle], reduceMotion: Bool) -> [String: Any] {
        var o: [String: Any] = ["schema_version": schemaVersion, "reduce_motion": reduceMotion]
        let kept = styles.filter { segments.contains($0.key) && !$0.value.isEmpty }
        if !kept.isEmpty {
            o["meters"] = ["styles": Dictionary(uniqueKeysWithValues: kept.map { ($0.key.rawValue, $0.value.json) })]
        }
        if let watchers { o["watchers"] = watchers.map(entry) }
        return o
    }

    /// Sorted keys, no spaces.
    static func data(_ o: [String: Any]) -> Data {
        (try? JSONSerialization.data(withJSONObject: o, options: [.sortedKeys, .withoutEscapingSlashes])) ?? Data()
    }

    static func json(watchers: [Watcher]?, styles: [SanduhrSegment: SegmentStyle], reduceMotion: Bool, now: Date) -> Data {
        var o = body(watchers: watchers, styles: styles, reduceMotion: reduceMotion)
        o["written_at"] = HandoffFiles.stamp(now)
        return data(o)
    }

    /// The stored looks as the defaults keep them (the picks' `ours` object), nil with none.
    static func stylesText(_ ours: [SanduhrSegment: SegmentStyle]?) -> String? {
        let kept = (ours ?? [:]).filter { segments.contains($0.key) && !$0.value.isEmpty }
        guard !kept.isEmpty else { return nil }
        let o = Dictionary(uniqueKeysWithValues: kept.map { ($0.key.rawValue, $0.value.json) })
        return String(decoding: data(o), as: UTF8.self)
    }

    /// The stored looks read back; anything malformed reads as none.
    static func styles(from text: String?) -> [SanduhrSegment: SegmentStyle] {
        guard let text, let o = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any] else { return [:] }
        var out: [SanduhrSegment: SegmentStyle] = [:]
        for (key, value) in o {
            guard let segment = SanduhrSegment(rawValue: key), segments.contains(segment),
                  let style = SegmentStyle(json: value) else { continue }
            out[segment] = style
        }
        return out
    }
}

/// Keeps `band.json` in step: the switch, the looks, Reduce Motion and the watchers. Writes only
/// when what the file says changed (or the heartbeat is due while watchers show), deletes it when
/// it would say nothing.
@MainActor
final class BandFileWriter {
    static let shared = BandFileWriter(watchers: {
        WatcherBoard.shown(WatcherStore.shared.ordered, demo: DeskController.shared.model.demo)
    })

    let url: URL
    private let defaults: UserDefaults
    private let reduceMotion: () -> Bool
    private let now: () -> Date
    private let watchers: () -> [Watcher]
    private var lastBody: Data?
    private var lastWrite: Date?
    private var timer: Timer?
    private var observers: [NSObjectProtocol] = []

    init(support: URL = HandoffFiles.support, defaults: UserDefaults = .standard,
         reduceMotion: @escaping () -> Bool = { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion },
         now: @escaping () -> Date = Date.init, watchers: @escaping () -> [Watcher]) {
        url = support.appendingPathComponent(BandFile.name)
        self.defaults = defaults
        self.reduceMotion = reduceMotion
        self.now = now
        self.watchers = watchers
    }

    var showsWatchers: Bool { defaults.bool(forKey: BandFile.watchersKey) }

    /// The Combine sheet's looks for Sanduhr's segments (nil clears them: Replace).
    func setMeterStyles(_ ours: [SanduhrSegment: SegmentStyle]?) {
        if let text = BandFile.stylesText(ours) {
            defaults.set(text, forKey: BandFile.stylesKey)
        } else {
            defaults.removeObject(forKey: BandFile.stylesKey)
        }
        refresh()
    }

    /// Writes the file when what it says changed, or the heartbeat is due; deletes it when it
    /// would say nothing. `quitting` writes no watchers: Sanduhr drops them as it quits.
    func refresh(quitting: Bool = false) {
        let styles = BandFile.styles(from: defaults.string(forKey: BandFile.stylesKey))
        let on = showsWatchers
        guard on || !styles.isEmpty else {
            stopHeartbeat()
            if lastBody != nil || FileManager.default.fileExists(atPath: url.path) {
                try? FileManager.default.removeItem(at: url)
            }
            lastBody = nil
            return
        }
        let shown = on ? (quitting ? [] : watchers()) : nil
        let body = BandFile.data(BandFile.body(watchers: shown, styles: styles, reduceMotion: reduceMotion()))
        let at = now()
        let due = !(shown ?? []).isEmpty && (lastWrite.map { at.timeIntervalSince($0) >= BandFile.heartbeat - 1 } ?? true)
        if body != lastBody || due || !FileManager.default.fileExists(atPath: url.path) {
            HandoffFiles.writeOwnerOnly(BandFile.json(watchers: shown, styles: styles, reduceMotion: reduceMotion(), now: at),
                                        to: url)
            lastBody = body
            lastWrite = at
        }
        if (shown ?? []).isEmpty || quitting { stopHeartbeat() } else { startHeartbeat() }
    }

    private func startHeartbeat() {
        guard timer == nil else { return }
        let t = Timer(timeInterval: BandFile.heartbeat, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func stopHeartbeat() {
        timer?.invalidate()
        timer = nil
    }

    /// The app's wiring: the switch and the looks (defaults), Reduce Motion, then a first write.
    func startForApp() {
        observers.append(NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in MainActor.assumeIsolated { self?.refresh() } })
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in MainActor.assumeIsolated { self?.refresh() } })
        refresh()
    }
}
