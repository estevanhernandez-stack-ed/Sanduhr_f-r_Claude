import Foundation

/// What a new session in one seat starts with before anything is typed (item 71, slice 1c): the
/// skill listing Claude Code builds from every skill and command, the agent list, and the MCP
/// servers. Estimated from the files: Claude Code's own `/doctor` and `/context` give the exact
/// figure for a running session. Project folders add their own on top, so they aren't counted.
struct SessionLoad: Equatable, Sendable {
    /// What one source adds: "Yours" (the seat's own folders) or a plugin by name.
    struct Source: Equatable, Sendable, Identifiable {
        let name: String
        var isPlugin = false
        /// Skill listing entries (skills and commands Claude can see) and their characters.
        var listed = 0
        var listingChars = 0
        /// Agents and the characters of their names and descriptions.
        var agents = 0
        var agentChars = 0
        var id: String { (isPlugin ? "plugin:" : "own:") + name }

        var tokens: Int { SessionLoad.tokens(listingChars + agentChars) }
    }

    let seat: String
    /// Listing entries with their description, with their name only, and kept out of the listing
    /// (`disable-model-invocation`, or `skillOverrides` set to `off` or `user-invocable-only`).
    var listed = 0
    var nameOnly = 0
    var hidden = 0
    var listingChars = 0
    var agents = 0
    var agentChars = 0
    /// MCP servers that start with every session: user, managed and the on plugins' own. Their
    /// tools aren't counted, because Sanduhr never starts a server.
    var mcpServers = 0
    var settings = ListingSettings()
    /// Biggest first.
    var sources: [Source] = []

    /// About four characters a token, rounded: an estimate, said as one.
    static func tokens(_ chars: Int) -> Int { (chars + 2) / 4 }

    var listingTokens: Int { Self.tokens(listingChars) }
    var agentTokens: Int { Self.tokens(agentChars) }

    /// The listing's budget in tokens for a model with `context` tokens: a fixed character budget
    /// when the settings set one, else the budget fraction of the window.
    func budgetTokens(context: Int) -> Int {
        if let chars = settings.budgetChars { return Self.tokens(chars) }
        return Int((Double(context) * settings.budgetFraction).rounded())
    }

    /// The listing is over its budget on a 200K-token model: Claude Code then drops the
    /// descriptions of the skills used least.
    var overBudget: Bool { listingTokens > budgetTokens(context: 200_000) }

    /// One listing entry's characters: its name, and its description with `when_to_use` cut to
    /// the cap unless only the name is listed, plus a little for the line itself.
    static func entryChars(name: String, entry: SeatEntry, nameOnly: Bool, cap: Int) -> Int {
        guard !nameOnly else { return name.count + 4 }
        let text = entry.whenToUse.isEmpty ? entry.description : entry.description + " " + entry.whenToUse
        return name.count + min(text.count, cap) + 4
    }

    /// The load of one seat from its scan. Seat skills honor `skillOverrides`; plugin skills
    /// don't (the docs: manage those through `/plugin`). Plugin entries are named
    /// `plugin:name` as Claude Code lists them. Only plugins that are on and have a folder here
    /// count; one that loads only in its session or only in projects doesn't.
    static func build(_ scan: SeatScan) -> SessionLoad {
        var load = SessionLoad(seat: scan.seat, settings: scan.listing)
        let cap = scan.listing.maxDescChars
        var own = Source(name: "Yours")

        func list(_ e: SeatEntry, name: String, overridable: Bool, into src: inout Source) {
            let mode = overridable ? (scan.listing.overrides[e.name] ?? "on") : "on"
            if e.modelHidden || mode == "off" || mode == "user-invocable-only" {
                load.hidden += 1
                return
            }
            let chars = entryChars(name: name, entry: e, nameOnly: mode == "name-only", cap: cap)
            if mode == "name-only" { load.nameOnly += 1 } else { load.listed += 1 }
            load.listingChars += chars
            src.listed += 1
            src.listingChars += chars
        }
        func agent(_ e: SeatEntry, name: String, into src: inout Source) {
            let chars = name.count + e.description.count + 4
            load.agents += 1
            load.agentChars += chars
            src.agents += 1
            src.agentChars += chars
        }

        for e in scan.skills where e.scope != .project { list(e, name: e.name, overridable: true, into: &own) }
        for e in scan.commands where e.scope != .project { list(e, name: e.name, overridable: true, into: &own) }
        for e in scan.agents where e.scope != .project { agent(e, name: e.name, into: &own) }
        if own.listed + own.agents > 0 { load.sources.append(own) }

        var seen: Set<String> = []
        for item in scan.plugins where item.state == .on {
            guard let c = scan.pluginContents[item.id], !seen.contains(item.name) else { continue }
            // A plugin on from two sources loads once: the first one listed is counted.
            seen.insert(item.name)
            var src = Source(name: item.name, isPlugin: true)
            for e in c.skills { list(e, name: item.name + ":" + e.name, overridable: false, into: &src) }
            for e in c.commands { list(e, name: item.name + ":" + e.name, overridable: false, into: &src) }
            for e in c.agents { agent(e, name: item.name + ":" + e.name, into: &src) }
            load.mcpServers += c.mcpServers
            if src.listed + src.agents > 0 { load.sources.append(src) }
        }
        load.mcpServers += scan.mcpServers.filter { $0.scope == .user || $0.scope == .managed }.count
        load.sources.sort { $0.tokens != $1.tokens ? $0.tokens > $1.tokens : $0.name < $1.name }
        return load
    }

    /// "1.2K" for 1,234; "850" below a thousand.
    static func short(_ n: Int) -> String {
        n < 1_000 ? "\(n)" : String(format: n < 10_000 ? "%.1fK" : "%.0fK", Double(n) / 1_000)
    }
}
