import Foundation

/// The Combine sheet's segment picker (item 63b), as data: the user's statusline split into
/// chips per line (on the separator detected, or the one chosen for that line), Sanduhr's own
/// segments as chips, and what is kept. A chip of theirs is known by its matcher (the leading
/// token), so chips sharing one go together, as they do when the runner filters.
struct StatuslineChips: Equatable, Sendable {
    enum Source: Equatable, Sendable {
        case theirs
        case sanduhr
    }

    struct Chip: Identifiable, Equatable, Sendable {
        let id: String
        let source: Source
        /// What the chip shows: the segment's text, escapes taken out.
        let label: String
        /// The matcher (theirs) or the segment's name (Sanduhr's).
        let key: String
        let kept: Bool
        /// A click changes it: false for a line the runner keeps whole and for Sanduhr's last
        /// kept segment.
        let enabled: Bool
        /// What it shows, named: "Branch", "Session", "Your segment".
        var name = ""
        /// Theirs: what the classifier says it shows.
        var kind: SegmentKind?
        /// It has a look of its own.
        var styled = false
    }

    private(set) var inspection: StatuslineInspection
    /// The separator chosen per line; nil follows the one detected.
    private(set) var separators: [StatuslineSeparator?]
    private(set) var dropped: Set<String> = []
    var keepNew = true
    private(set) var mine: [SanduhrSegment] = SanduhrSegment.defaults
    /// The glyph the final line joins segments with; nil keeps their own separators.
    var joinWith: StatuslineJoinGlyph?
    /// Looks set per segment: theirs by matcher, Sanduhr's by name.
    private(set) var styles: [String: SegmentStyle] = [:]
    private(set) var ourStyles: [SanduhrSegment: SegmentStyle] = [:]

    init(_ inspection: StatuslineInspection) {
        self.inspection = inspection
        separators = Array(repeating: nil, count: inspection.lines.count)
    }

    /// The same picks over a new run of their statusline (Test with live data): what was dropped
    /// stays dropped, a segment seen for the first time shows as a new chip, kept, and the
    /// separators chosen stay with their lines.
    func refilled(_ next: StatuslineInspection) -> StatuslineChips {
        var c = self
        c.inspection = next
        c.separators = (0..<next.lines.count).map { $0 < separators.count ? separators[$0] : nil }
        return c
    }

    /// The lines worth a row: ones with something visible.
    var lineIndices: [Int] {
        inspection.lines.indices.filter { !Self.plain(inspection.lines[$0].text).trimmingCharacters(in: .whitespaces).isEmpty }
    }

    /// The separator a line is cut on now.
    func separator(line: Int) -> StatuslineSeparator {
        separators[line] ?? detected(line: line)
    }

    func detected(line: Int) -> StatuslineSeparator {
        StatuslineSeparator(rawValue: inspection.lines[line].auto) ?? .none
    }

    /// Whether the separator can be chosen for `line` (the runner reads choices for the first 16).
    func separatorChangeable(line: Int) -> Bool { line < StatuslinePicks.maxLines }

    private func split(line: Int) -> StatuslineInspection.Split? {
        inspection.lines[line].splits[separator(line: line).rawValue]
    }

    /// The runner keeps this line whole as it is cut now.
    func isWhole(line: Int) -> Bool { split(line: line).map { $0.doubt || $0.segments.isEmpty } ?? true }

    /// A line's chips: one per segment, or one for the whole line when the runner keeps it whole.
    func chips(line: Int) -> [Chip] {
        guard let split = split(line: line), !isWhole(line: line) else {
            let text = Self.plain(inspection.lines[line].text).trimmingCharacters(in: .whitespaces)
            return [Chip(id: "\(line)-whole", source: .theirs, label: text, key: "", kept: true, enabled: false,
                         name: "Your line")]
        }
        return split.segments.enumerated().map { i, s in
            let kind = s.kind.flatMap(SegmentKind.init(rawValue:)) ?? .segment
            return Chip(id: "\(line)-\(i)", source: .theirs, label: s.text, key: s.matcher,
                        kept: !dropped.contains(s.matcher), enabled: !s.matcher.isEmpty, name: kind.title, kind: kind,
                        styled: !(styles[s.matcher]?.isEmpty ?? true))
        }
    }

    /// Sanduhr's chips, in the order they print.
    var mineChips: [Chip] {
        let texts = Dictionary(inspection.mine.map { ($0.name, $0.text) }, uniquingKeysWith: { a, _ in a })
        return SanduhrSegment.allCases.map { s in
            let kept = mine.contains(s)
            return Chip(id: "sanduhr-\(s.rawValue)", source: .sanduhr, label: texts[s.rawValue].flatMap { $0.isEmpty ? nil : $0 } ?? s.title,
                        key: s.rawValue, kept: kept, enabled: !(kept && mine.count == 1), name: s.title,
                        styled: !(ourStyles[s]?.isEmpty ?? true))
        }
    }

    mutating func toggle(_ chip: Chip) {
        guard chip.enabled else { return }
        switch chip.source {
        case .theirs:
            if dropped.contains(chip.key) { dropped.remove(chip.key) } else { dropped.insert(chip.key) }
        case .sanduhr:
            guard let s = SanduhrSegment(rawValue: chip.key) else { return }
            if let i = mine.firstIndex(of: s) {
                if mine.count > 1 { mine.remove(at: i) }
            } else {
                mine = SanduhrSegment.allCases.filter { $0 == s || mine.contains($0) }
            }
        }
    }

    /// A chip's look (empty: its own).
    func style(for chip: Chip) -> SegmentStyle {
        switch chip.source {
        case .theirs: styles[chip.key] ?? SegmentStyle()
        case .sanduhr: SanduhrSegment(rawValue: chip.key).flatMap { ourStyles[$0] } ?? SegmentStyle()
        }
    }

    mutating func setStyle(_ style: SegmentStyle, for chip: Chip) {
        let value: SegmentStyle? = style.isEmpty ? nil : style
        switch chip.source {
        case .theirs:
            guard !chip.key.isEmpty else { return }
            styles[chip.key] = value
        case .sanduhr:
            guard let s = SanduhrSegment(rawValue: chip.key) else { return }
            ourStyles[s] = value
        }
    }

    /// Keep yours (Sanduhr's segment goes) or Keep Sanduhr's (yours goes) for a duplicate of
    /// theirs against Sanduhr's. Sanduhr keeps at least one segment.
    mutating func resolve(_ duplicate: StatuslineDuplicate, keepYours: Bool) {
        guard case .theirs(let matcher) = duplicate.a, case .sanduhr(let segment) = duplicate.b else { return }
        if keepYours {
            if mine.count > 1 { mine.removeAll { $0 == segment } }
        } else {
            dropped.insert(matcher)
        }
    }

    mutating func setSeparator(_ sep: StatuslineSeparator, line: Int) {
        guard separators.indices.contains(line), separatorChangeable(line: line) else { return }
        separators[line] = sep
    }

    /// Every matcher of theirs as the lines are cut now, in order, each once.
    var seenMatchers: [String] {
        var out: [String] = []
        for line in inspection.lines.indices where !isWhole(line: line) {
            for s in split(line: line)?.segments ?? [] where !s.matcher.isEmpty && !out.contains(s.matcher) {
                out.append(s.matcher)
            }
        }
        return out
    }

    /// Their picks for the command: nil when every segment stays and new ones do too (the
    /// command then reads as it did before picks).
    var theirs: StatuslinePicks? {
        let seen = seenMatchers
        let drop = Array(seen.filter(dropped.contains).prefix(StatuslinePicks.maxMatchers))
        guard !drop.isEmpty || !keepNew || joinWith != nil || !styles.isEmpty || !ourStyles.isEmpty else { return nil }
        return allPicks(drop: drop)
    }

    private func allPicks(drop: [String]) -> StatuslinePicks {
        let keep = Array(seenMatchers.filter { !dropped.contains($0) }.prefix(StatuslinePicks.maxMatchers))
        return StatuslinePicks(keep: keep, drop: drop, keepNew: keepNew,
                               separators: Array(separators.prefix(StatuslinePicks.maxLines)), joinWith: joinWith,
                               styles: styles, ours: ourStyles)
    }

    /// What the command carries: nothing for a choice that changes nothing.
    var selection: StatuslineSelection {
        StatuslineSelection(theirs: theirs, mine: mine == SanduhrSegment.defaults ? nil : mine)
    }

    /// What the preview composes with: the command's picks, or, when those are empty, the split
    /// choices anyway, so a change of separator re-runs the preview at once (the line reads the
    /// same until a segment drops or Join with is set).
    var previewSelection: StatuslineSelection {
        var s = selection
        if s.theirs == nil { s.theirs = allPicks(drop: []) }
        return s
    }

    /// Escapes taken out of terminal text.
    static func plain(_ text: String) -> String {
        ANSIText.runs(text).map(\.text).joined()
    }
}
