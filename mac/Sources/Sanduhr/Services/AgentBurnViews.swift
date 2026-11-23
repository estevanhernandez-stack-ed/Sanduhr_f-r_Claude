import Foundation

/// How the Local burn views name and split the agents (every-agents spec, section 4; Windows
/// `AgentSources.BurnName`, `AgentShares`, `AgentBurnDeltas`). With Claude Code alone every text
/// reads as it did in 2.45.0.
extension AgentSource {
    /// The product as the Local burn page names it: "Claude Code", "Codex", "Antigravity".
    var burnName: String {
        switch self {
        case .claude: "Claude Code"
        case .codex: "Codex"
        case .antigravity: "Antigravity"
        }
    }

    /// The agents whose burn the Vault holds, Claude Code first (the order every list draws in).
    static let burn: [AgentSource] = allCases

    /// The agents that live in their own Vault root, not in an account's.
    static let burnRoots: [AgentSource] = allCases.filter { $0 != .claude }
}

/// Text for figures split by agent. One agent reads the bare figure; another agent alone is named
/// so a Codex-only project never reads as Claude Code burn.
enum AgentShares {
    /// "13.0M" for Claude Code alone, "13.0M (Codex)" for one other agent, "1.2M (Claude Code 900k,
    /// Codex 300k)" for several.
    static func describe(_ total: Int64, _ byAgent: [AgentSource: Int64]) -> String {
        let parts = ordered(byAgent)
        let head = TokenFormat.compact(total)
        if parts.count > 1 { return "\(head) (\(parts.joined(separator: ", ")))" }
        guard let only = AgentSource.burn.first(where: { (byAgent[$0] ?? 0) > 0 }), only != .claude else { return head }
        return "\(head) (\(only.burnName))"
    }

    /// The strip's accessible name: "30 days, Claude Code 41M, Codex 6.2M".
    static func stripName(_ byAgent: [AgentSource: Int64]) -> String {
        "30 days, " + list(byAgent)
    }

    /// Each agent with tokens and its total, in agent order: "Claude Code 41M, Codex 6.2M".
    static func list(_ byAgent: [AgentSource: Int64]) -> String {
        ordered(byAgent).joined(separator: ", ")
    }

    private static func ordered(_ byAgent: [AgentSource: Int64]) -> [String] {
        AgentSource.burn.filter { (byAgent[$0] ?? 0) > 0 }
            .map { "\($0.burnName) \(TokenFormat.compact(byAgent[$0] ?? 0))" }
    }
}

/// The widget footer's agent pieces (Windows `AgentBurnDeltas`): each agent's Vault total for
/// today after the latest ingest minus the total after the ingest before it. The first
/// observation has no "before" and shows nothing; at a day change the earlier total counts as
/// zero; an agent with nothing new is left out.
struct AgentBurnDeltas {
    private var last: [AgentSource: (day: CCLocalDay, total: Int64)] = [:]
    private var delta: [AgentSource: Int64] = [:]

    mutating func observe(_ agent: AgentSource, day: CCLocalDay, todayTotal: Int64) {
        if let prev = last[agent] {
            delta[agent] = max(0, todayTotal - (prev.day == day ? prev.total : 0))
        } else {
            delta[agent] = 0
        }
        last[agent] = (day, todayTotal)
    }

    mutating func forget(_ agent: AgentSource) {
        last[agent] = nil
        delta[agent] = nil
    }

    /// One ingest's reading: the agents in `active` are observed with `total`, every other agent
    /// is forgotten (switched off, erased, or demo).
    mutating func observe(active: [AgentSource], day: CCLocalDay, total: (AgentSource) -> Int64) {
        for agent in AgentSource.burnRoots {
            if active.contains(agent) {
                observe(agent, day: day, todayTotal: total(agent))
            } else {
                forget(agent)
            }
        }
    }

    /// " · Codex +3.0k · Agy +1.0k", in agent order; empty when nothing is new.
    func footerPieces() -> String {
        AgentSource.burnRoots.filter { (delta[$0] ?? 0) > 0 }
            .map { " · \($0.shortName) +\(TokenFormat.compact(delta[$0] ?? 0))" }
            .joined()
    }
}

/// The footer line: "Updated 4:10 PM · Float", then the agent pieces. Pure, so a test pins it.
enum FooterText {
    static func make(updated: Date?, mode: String, agentPieces: String, demo: Bool,
                     timeZone: TimeZone = .current) -> String {
        // Claude never updated here (off, or signed out): the agents' pieces alone, so a Mac that tracks only
        // Codex or the Antigravity CLI still sees their new burn.
        guard let updated else {
            guard !demo, agentPieces.hasPrefix(" · ") else { return "" }
            return String(agentPieces.dropFirst(3))
        }
        let f = DateFormatter()
        f.dateFormat = "h:mm a"
        f.timeZone = timeZone
        return "Updated \(f.string(from: updated)) · \(mode)" + (demo ? "" : agentPieces)
    }
}

/// Today's total per agent in the Vault, read after an ingest for the footer.
enum AgentBurnToday {
    static func totals(_ agents: [AgentSource], reader: VaultReader, today: CCLocalDay) -> [AgentSource: Int64] {
        var out: [AgentSource: Int64] = [:]
        for agent in agents {
            out[agent] = reader.readWindow([agent.rawValue], from: today, toExclusive: today.adding(days: 1)).total
        }
        return out
    }
}
