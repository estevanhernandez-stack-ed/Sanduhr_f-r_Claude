import AppKit
import Foundation

/// `desk-state.json` (spec 8, ledger D2): what `get_desk` reads. The Desk's settings live in the
/// `com.626labs.sanduhr.desk` defaults suite, which the MCP server cannot read without racing cfprefsd, so the
/// app keeps this small owner-only file current instead. It holds only the schema version, the layout words
/// (anchor, size, tilt, style per piece), the Desk ink, plate and shadow, the font names, which pieces show,
/// the message and notch text colors, and the limits a design must keep. No usage numbers, no message text,
/// no paths, no account names.
enum DeskStateExport {
    static let schemaVersion = 1
    /// The installed families listed, as Windows caps them.
    static let maxInstalledFonts = 2000

    /// The ink as `#rrggbb` stops: one string for one color, a list for a gradient (the design's own shape).
    /// An unreadable value is white, as the Desk draws it.
    static func inkJson(_ stored: String) -> Any {
        let stops = stored.split(separator: ",", omittingEmptySubsequences: false).compactMap { DeskDesign.hex(String($0)) }.prefix(4)
        let colors = stops.isEmpty ? ["ffffff"] : stops.map { $0 }
        return colors.count == 1 ? "#" + colors[0] : colors.map { "#" + $0 }
    }

    /// The families Claude may name: valid names only, no duplicates, in the picker's order, capped.
    static func installedFamilies(_ families: [String]) -> [String] {
        var seen = Set<String>()
        var out: [String] = []
        for f in families where !f.isEmpty && DeskDesign.fontFamilyProblem(f) == nil && seen.insert(f.lowercased()).inserted {
            out.append(f)
            if out.count == maxInstalledFonts { break }
        }
        return out
    }

    /// The state file's bytes for the Desk settings in `defaults`.
    static func stateJson(defaults d: UserDefaults, installedFonts: [String], writerVersion: String,
                          fontAvailable: (String) -> Bool = DeskFont.isAvailable) -> Data {
        let arrangement = DeskArrangement(d.string(forKey: "layout") ?? DeskLayout.standard)
        var layout: [String: Any] = [:]
        for widget in DeskArrangement.known {
            guard let p = arrangement.placement(widget) else { layout[widget] = ["hidden": true]; continue }
            var piece: [String: Any] = ["anchor": p.anchor.rawValue, "size": p.scale, "hidden": false,
                                        "tilt": Int(arrangement.tilt(of: widget) ?? 0)]
            if DeskMeterStyles.supports(widget) { piece["style"] = DeskMeterStyles.word(arrangement.style(of: widget)) }
            layout[widget] = piece
        }
        let desk = DeskFont.resolve(saved: d.string(forKey: "font"), available: fontAvailable)
        let design: [String: Any] = [
            "layout": layout,
            "font": desk,
            "ink": inkJson(d.string(forKey: "inkColor") ?? "ffffff"),
            "plate": d.object(forKey: DeskPlate.key) as? Bool ?? DeskPlate.defaultOn,
            "shadow": d.object(forKey: "inkShadow") as? Bool ?? true,
        ]
        let showMeetings = d.object(forKey: "showMeetings") as? Bool ?? true
        let root: [String: Any] = [
            "schema_version": schemaVersion,
            "writer_version": writerVersion,
            "design": design,
            "pieces": DeskArrangement.known,
            "anchors": DeskAnchor.allCases.map(\.rawValue),
            "fonts": ["desk": desk, "message": d.string(forKey: "messageFont") ?? "",
                      "bundled": BundledFonts.families.map(\.name), "installed": installedFamilies(installedFonts)],
            "shown": arrangement.shown(showMeetings: showMeetings).map(\.widget),
            "message_color": d.string(forKey: "messageColor") ?? "9ad7ff",
            "notch_text_color": d.string(forKey: "notchTextColor") ?? "ffffff",
            "limits": [
                "name_chars": DeskDesign.nameMax,
                "note_chars": DeskDesign.noteMax,
                "piece_size": ["min": DeskArrangement.scaleRange.lowerBound, "max": DeskArrangement.scaleRange.upperBound],
                "piece_tilt": ["min": Int(DeskArrangement.tiltRange.lowerBound), "max": Int(DeskArrangement.tiltRange.upperBound)],
                "piece_style": ["pieces": DeskMeterStyles.pieces, "values": DeskMeterStyle.allCases.map(\.rawValue),
                                "default": DeskMeterStyles.word(.bar)],
            ] as [String: Any],
        ]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }
}

/// Keeps `desk-state.json` current: written at launch and, a moment after, whenever a Desk setting, the
/// installed fonts or a font pack changes (debounced, and only when the bytes differ, so a change elsewhere
/// writes nothing and nothing writes per frame). Owner-only and atomic (`HandoffFiles.writeOwnerOnly`).
@MainActor
final class DeskStateExporter {
    static let shared = DeskStateExporter()

    private let url: URL
    private let defaults: UserDefaults
    private let delay: TimeInterval
    private var lastData: Data?
    private var pending: DispatchWorkItem?
    private var started = false

    init(url: URL = HandoffFiles.support.appendingPathComponent(DeskDesignHandoff.stateFile),
         defaults: UserDefaults = .desk, delay: TimeInterval = 0.5) {
        self.url = url
        self.defaults = defaults
        self.delay = delay
    }

    func startForApp() {
        guard !started else { return }
        started = true
        let nc = NotificationCenter.default
        for name in [UserDefaults.didChangeNotification, .widgetPacksDidChange, NSFont.fontSetChangedNotification] {
            nc.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.schedule() }
            }
        }
        // A `defaults write` from outside the process fires no notification: Sanduhr's folder watch catches it.
        HandoffWatch.shared.add { [weak self] in self?.schedule() }
        writeNow()
    }

    func schedule() {
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in MainActor.assumeIsolated { _ = self?.writeNow() } }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    /// Writes the file when its bytes changed since the last write here.
    @discardableResult
    func writeNow() -> Bool {
        pending = nil
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
        let families = DeskFont.pickerFamilies(installed: NSFontManager.shared.availableFontFamilies)
        let data = DeskStateExport.stateJson(defaults: defaults, installedFonts: families, writerVersion: version)
        guard !data.isEmpty, data != lastData else { return false }
        lastData = data
        HandoffFiles.writeOwnerOnly(data, to: url)
        return true
    }
}
