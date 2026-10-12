import Foundation

/// What a Catalog card is (item 71, slice 1).
enum CatalogKind: String, CaseIterable, Sendable {
    case plugin, skill, agent, command, outputStyle, mcpServer

    /// The filter's words.
    var title: String {
        switch self {
        case .plugin: "Mods and plugins"
        case .skill: "Skills"
        case .agent: "Agents"
        case .command: "Commands"
        case .outputStyle: "Output styles"
        case .mcpServer: "MCP servers"
        }
    }

    /// The card's badge.
    var badge: String {
        switch self {
        case .plugin: "Plugin"
        case .skill: "Skill"
        case .agent: "Agent"
        case .command: "Command"
        case .outputStyle: "Output style"
        case .mcpServer: "MCP server"
        }
    }
}

/// What a seat chip says about the extension in that seat.
enum CatalogChipState: Equatable, Sendable {
    /// Loads in new sessions.
    case on
    /// Installed and turned off.
    case off
    /// Turned on in settings, but its files aren't on this Mac.
    case declared
    /// Only at project scope, in this many projects. Never which ones.
    case projects(Int)
    /// A mod made in one session, loading only there.
    case session

    var title: String {
        switch self {
        case .on: "On"
        case .off: "Off"
        case .declared: "Declared, not installed"
        case .projects(let n): n == 1 ? "In 1 project" : "In \(n) projects"
        case .session: "Its session only"
        }
    }

    /// Which state a seat shows when it holds the extension several ways.
    var rank: Int {
        switch self {
        case .on: 5
        case .session: 4
        case .declared: 3
        case .projects: 2
        case .off: 1
        }
    }
}

/// One seat on a card: its user-chosen label, its side of the wall and the state there.
struct CatalogChip: Identifiable, Equatable, Sendable {
    let seat: String
    let label: String
    let mark: SeatMark
    let state: CatalogChipState
    var id: String { seat }
}

/// One extension across every seat (item 71, slice 1).
struct CatalogCard: Identifiable, Equatable, Sendable {
    let kind: CatalogKind
    let name: String
    var isMod = false
    /// Sanduhr's own mod, `sanduhr-meters`.
    var isSanduhrs = false
    /// Where it comes from, in words, first seen first: "Marketplace official", "Inline folder",
    /// "Seat folder", "Project". Never a path or a project's name.
    var sources: [String] = []
    var version: String?
    var description = ""
    /// One per seat that holds it, in seat order. None: Available.
    var chips: [CatalogChip] = []
    /// The Mods page's item ids, for a Check answer.
    var itemIDs: [String] = []
    /// What needs a look besides a duplicate: declared but not installed, enabled twice in one
    /// seat, a folder that's gone. Seat labels only.
    var problems: [String] = []

    /// One seat's contribution to a card, and whether a work seat made it: demo mode keeps only
    /// what a seat that isn't work supplied.
    struct Contribution: Equatable, Sendable {
        let text: String
        let work: Bool
    }

    /// Per source, true while only work seats supplied it.
    var sourceWork: [String: Bool] = [:]
    /// The sources it is installed or turned on from (not just listed by a marketplace).
    var installedSources: [String] = []
    var descriptions: [Contribution] = []
    var versions: [Contribution] = []
    /// Problems found in a work seat.
    var workProblems: Set<String> = []

    var id: String { kind.rawValue + ":" + name }

    /// A plugin installed or turned on from two sources (two marketplaces, or a marketplace and
    /// a folder). A marketplace that only lists it doesn't count.
    var isDuplicate: Bool { kind == .plugin && installedSources.count > 1 }
    var isAvailable: Bool { chips.isEmpty }
    var needsLook: Bool { isDuplicate || !problems.isEmpty }
    /// Every seat holding it is a work seat: hidden in demo mode.
    var isWorkOnly: Bool { !chips.isEmpty && chips.allSatisfy { $0.mark == .work } }

    /// The card as demo mode shows it: no work seat's chip, source, description, version or
    /// problem. Nil when nothing but work seats stand behind it (a work-only card, or one only a
    /// work seat's marketplace lists).
    func demoView() -> CatalogCard? {
        guard !isWorkOnly else { return nil }
        var c = self
        c.chips = chips.filter { $0.mark != .work }
        c.sources = sources.filter { sourceWork[$0] != true }
        c.installedSources = installedSources.filter { sourceWork[$0] != true }
        c.description = descriptions.first { !$0.work }?.text ?? ""
        c.version = versions.first { !$0.work }?.text
        c.problems = problems.filter { !workProblems.contains($0) }
        guard !c.chips.isEmpty || !c.sources.isEmpty else { return nil }
        return c
    }
}

/// The Catalog's filters across the top.
struct CatalogFilter: Equatable, Sendable {
    enum Show: String, CaseIterable, Sendable {
        case all, installed, available, needsLook

        var title: String {
            switch self {
            case .all: "Everything"
            case .installed: "Installed somewhere"
            case .available: "Available only"
            case .needsLook: "Needs a look"
            }
        }
    }

    var kind: CatalogKind?
    /// A seat's path.
    var seat: String?
    var show: Show = .all
    var search = ""
}

/// Builds the Catalog from every seat's scan (item 71, slice 1): one card per extension, a chip
/// per seat that holds it. Pure: the scans are read elsewhere.
enum ConfigCatalog {
    static let sanduhrSource = "Sanduhr"
    static let sanduhrDescription = "Sanduhr's meters as animated bars above Claude Code's prompt."

    static func build(_ seats: [(seat: Seat, scan: SeatScan)]) -> [CatalogCard] {
        var cards: [String: CatalogCard] = [:]
        var order: [String] = []
        func card(_ kind: CatalogKind, _ name: String) -> String {
            let id = kind.rawValue + ":" + name
            if cards[id] == nil {
                cards[id] = CatalogCard(kind: kind, name: name)
                order.append(id)
            }
            return id
        }
        // Who supplied what: a work seat's contributions are marked, so demo mode can drop them.
        var work = false
        func addSource(_ id: String, _ s: String, installed: Bool = false) {
            if cards[id]?.sources.contains(s) == false { cards[id]?.sources.append(s) }
            if installed, cards[id]?.installedSources.contains(s) == false { cards[id]?.installedSources.append(s) }
            let onlyWork = (cards[id]?.sourceWork[s] ?? true) && work
            cards[id]?.sourceWork[s] = onlyWork
        }
        func describe(_ id: String, _ description: String, _ version: String?) {
            // Descriptions and versions come from files anyone could write: masked like any value.
            let d = ConfigMask.maskString(description)
            if !d.isEmpty {
                if cards[id]?.description.isEmpty == true { cards[id]?.description = d }
                cards[id]?.descriptions.append(CatalogCard.Contribution(text: d, work: work))
            }
            if let version, !version.isEmpty {
                let v = ConfigMask.maskString(version)
                if cards[id]?.version == nil { cards[id]?.version = v }
                cards[id]?.versions.append(CatalogCard.Contribution(text: v, work: work))
            }
        }
        func chip(_ id: String, _ seat: Seat, _ state: CatalogChipState) {
            cards[id]?.chips.append(CatalogChip(seat: seat.path, label: seat.label, mark: seat.mark, state: state))
        }
        func problem(_ id: String, _ text: [String]) {
            cards[id]?.problems += text
            if work { cards[id]?.workProblems.formUnion(text) }
        }

        let own = card(.plugin, IntegrationScripts.modName)
        cards[own]?.isMod = true
        cards[own]?.isSanduhrs = true
        addSource(own, sanduhrSource, installed: true)
        describe(own, sanduhrDescription, nil)

        for (seat, scan) in seats {
            work = seat.isWork
            // Plugins and mods: what the seat loads, then what its projects turn on.
            var byName: [String: [ModItem]] = [:]
            for item in scan.plugins { byName[item.name, default: []].append(item) }
            var projectsByName: [String: Set<String>] = [:]
            for (key, projects) in scan.projectPlugins { projectsByName[pluginName(key), default: []].formUnion(projects) }
            for name in Set(byName.keys).union(projectsByName.keys).sorted() {
                let id = card(.plugin, name)
                let items = byName[name] ?? []
                for item in items {
                    addSource(id, source(item), installed: true)
                    describe(id, item.description, item.version)
                    if item.isMod { cards[id]?.isMod = true }
                    cards[id]?.itemIDs.append(item.id)
                }
                for key in scan.projectPlugins.keys.sorted() where pluginName(key) == name && id != own {
                    addSource(id, keySource(key), installed: true)
                }
                let projects = projectsByName[name]?.count ?? 0
                chip(id, seat, state(items, projects: projects))
                problem(id, problems(items, seat: seat))
            }
            for m in scan.marketplaces {
                for listed in m.plugins {
                    let id = card(.plugin, listed.name)
                    addSource(id, "Marketplace " + m.name)
                    describe(id, listed.description, listed.version)
                }
            }
            // Agents, skills, commands and output styles, grouped by name.
            for (kind, entries) in [(CatalogKind.agent, scan.agents), (.skill, scan.skills),
                                    (.command, scan.commands), (.outputStyle, scan.outputStyles)] {
                var grouped: [String: [SeatEntry]] = [:]
                for e in entries { grouped[e.name, default: []].append(e) }
                for name in grouped.keys.sorted() {
                    let list = grouped[name] ?? []
                    let id = card(kind, name)
                    for e in list {
                        addSource(id, entrySource(e.scope))
                        describe(id, e.description, nil)
                    }
                    let projects = Set(list.filter { $0.scope == .project }.compactMap(\.project)).count
                    chip(id, seat, list.contains { $0.scope != .project } ? .on : .projects(projects))
                }
            }
            // MCP servers at the three scopes and managed; a plugin's servers belong to its card.
            var servers: [String: [SeatMCPServer]] = [:]
            for s in scan.mcpServers where s.scope != .plugin { servers[s.name, default: []].append(s) }
            for name in servers.keys.sorted() {
                let list = servers[name] ?? []
                let id = card(.mcpServer, name)
                for s in list { addSource(id, s.scope.title) }
                describe(id, "", nil)
                let projects = Set(list.filter { $0.scope == .local || $0.scope == .project }.compactMap(\.owner)).count
                chip(id, seat, list.contains { $0.scope == .user || $0.scope == .managed } ? .on : .projects(projects))
            }
        }
        return sorted(order.compactMap { cards[$0] })
    }

    /// Sanduhr's own mod first, then by kind and name.
    static func sorted(_ cards: [CatalogCard]) -> [CatalogCard] {
        let kinds = CatalogKind.allCases
        return cards.sorted { a, b in
            if a.isSanduhrs != b.isSanduhrs { return a.isSanduhrs }
            let ka = kinds.firstIndex(of: a.kind) ?? 0, kb = kinds.firstIndex(of: b.kind) ?? 0
            if ka != kb { return ka < kb }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
    }

    static func pluginName(_ key: String) -> String {
        guard let at = key.lastIndex(of: "@"), at != key.startIndex else { return key }
        return String(key[..<at])
    }

    static func market(_ key: String) -> String {
        guard let at = key.lastIndex(of: "@") else { return "" }
        return String(key[key.index(after: at)...])
    }

    /// Where a plugin item comes from, in words, without a path.
    static func source(_ item: ModItem) -> String {
        if item.ownership == .sanduhrs { return sanduhrSource }
        switch item.origin {
        case .pluginDirs: return "Inline folder"
        case .installed(let m): return m == "synced" ? "claude.ai (synced)" : "Marketplace " + m
        case .skillsDir: return "Skills folder"
        case .devMods: return "Made in a session"
        }
    }

    /// Where an `enabledPlugins` key's plugin comes from, in words.
    static func keySource(_ key: String) -> String {
        switch market(key) {
        case "inline": "Inline folder"
        case "skills-dir": "Skills folder"
        case "synced": "claude.ai (synced)"
        case let m: "Marketplace " + m
        }
    }

    static func entrySource(_ scope: SeatEntry.Scope) -> String {
        switch scope {
        case .seat: "Seat folder"
        case .synced: "claude.ai (synced)"
        case .project: "Project"
        }
    }

    /// The seat's state for a plugin it holds as `items`, turned on in `projects` projects.
    static func state(_ items: [ModItem], projects: Int) -> CatalogChipState {
        var best: CatalogChipState?
        for item in items {
            let s: CatalogChipState
            switch item.state {
            case .on:
                if item.path != nil { s = .on } else if case .installed("synced") = item.origin { s = .on } else { s = .declared }
            case .session: s = .session
            case .missing: s = .declared
            case .off: s = projects > 0 ? .projects(projects) : .off
            }
            if best == nil || s.rank > best!.rank { best = s }
        }
        return best ?? .projects(projects)
    }

    /// What needs a look in one seat: declared and not installed, a folder that's gone, turned
    /// on from two sources. Seat labels only.
    static func problems(_ items: [ModItem], seat: Seat) -> [String] {
        var out: [String] = []
        if items.contains(where: { $0.state == .missing }) {
            out.append("Its folder is missing in \(seat.label).")
        } else if items.contains(where: { $0.state == .on && $0.path == nil && $0.origin != .installed(marketplace: "synced") }) {
            out.append("Turned on in \(seat.label) but not installed on this Mac.")
        }
        let on = Set(items.filter { $0.state == .on || $0.state == .session }.map(source))
        if on.count > 1 { out.append("On twice in \(seat.label): \(on.sorted().joined(separator: " and ")).") }
        return out
    }

    // MARK: Filters

    /// One line per seat that has files Sanduhr couldn't read (item 71, slice 1): its label and
    /// a pointer to By Seat. Demo mode leaves work seats out.
    static func unreadableNotes(_ seats: [(seat: Seat, scan: SeatScan)], demo: Bool) -> [String] {
        seats.compactMap { pair -> String? in
            let (seat, scan) = pair
            guard !scan.unreadableFiles.isEmpty || scan.partial, !(demo && seat.isWork) else { return nil }
            return scan.unreadableFiles.isEmpty
                ? "\(seat.label): a folder walk stopped at its limit, so what's listed may be partial. See By Seat."
                : "\(seat.label): some files couldn't be read. See By Seat."
        }
    }

    /// The cards the filters let through. Demo mode drops every work seat's chip, source,
    /// description, version and problem, and every card nothing but work seats stand behind.
    static func filter(_ cards: [CatalogCard], _ f: CatalogFilter, demo: Bool,
                       failedChecks: Set<String> = []) -> [CatalogCard] {
        let words = f.search.lowercased().split(whereSeparator: \.isWhitespace).map(String.init)
        // Demo mode shows each card as demoView draws it, and drops the ones only work seats stand behind.
        let shown = demo ? cards.compactMap { $0.demoView() } : cards
        return shown.filter { c in
            if let k = f.kind, c.kind != k { return false }
            if let s = f.seat, !c.chips.contains(where: { $0.seat == s }) { return false }
            switch f.show {
            case .all: break
            case .installed: if c.isAvailable { return false }
            case .available: if !c.isAvailable { return false }
            case .needsLook: if !(c.needsLook || failedChecks.contains(c.id)) { return false }
            }
            guard !words.isEmpty else { return true }
            let text = ([c.name, c.description] + c.sources).joined(separator: " ").lowercased()
            return words.allSatisfy(text.contains)
        }
    }

    /// The counts state.yaml reports: never a name.
    struct Counts: Equatable, Sendable {
        var cards = 0
        var available = 0
        var needsLook = 0
        var duplicates = 0
        var workOnly = 0
    }

    static func counts(_ cards: [CatalogCard]) -> Counts {
        Counts(cards: cards.count, available: cards.filter(\.isAvailable).count,
               needsLook: cards.filter(\.needsLook).count, duplicates: cards.filter(\.isDuplicate).count,
               workOnly: cards.filter(\.isWorkOnly).count)
    }
}
