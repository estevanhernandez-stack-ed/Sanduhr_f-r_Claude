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
    }

    let inspection: StatuslineInspection
    /// The separator chosen per line; nil follows the one detected.
    private(set) var separators: [StatuslineSeparator?]
    private(set) var dropped: Set<String> = []
    var keepNew = true
    private(set) var mine: [SanduhrSegment] = SanduhrSegment.defaults

    init(_ inspection: StatuslineInspection) {
        self.inspection = inspection
        separators = Array(repeating: nil, count: inspection.lines.count)
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
            return [Chip(id: "\(line)-whole", source: .theirs, label: text, key: "", kept: true, enabled: false)]
        }
        return split.segments.enumerated().map { i, s in
            Chip(id: "\(line)-\(i)", source: .theirs, label: s.text, key: s.matcher,
                 kept: !dropped.contains(s.matcher), enabled: !s.matcher.isEmpty)
        }
    }

    /// Sanduhr's chips, in the order they print.
    var mineChips: [Chip] {
        let texts = Dictionary(inspection.mine.map { ($0.name, $0.text) }, uniquingKeysWith: { a, _ in a })
        return SanduhrSegment.allCases.map { s in
            let kept = mine.contains(s)
            return Chip(id: "sanduhr-\(s.rawValue)", source: .sanduhr, label: texts[s.rawValue].flatMap { $0.isEmpty ? nil : $0 } ?? s.title,
                        key: s.rawValue, kept: kept, enabled: !(kept && mine.count == 1))
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
        guard !drop.isEmpty || !keepNew else { return nil }
        let keep = Array(seen.filter { !dropped.contains($0) }.prefix(StatuslinePicks.maxMatchers))
        return StatuslinePicks(keep: keep, drop: drop, keepNew: keepNew,
                               separators: Array(separators.prefix(StatuslinePicks.maxLines)))
    }

    var selection: StatuslineSelection {
        StatuslineSelection(theirs: theirs, mine: mine == SanduhrSegment.defaults ? nil : mine)
    }

    /// Escapes taken out of terminal text.
    static func plain(_ text: String) -> String {
        ANSIText.runs(text).map(\.text).joined()
    }
}
