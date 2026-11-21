import Foundation

/// The `sources` debug action (agent sources, 2.46.0): each word goes through `SourcesSettingsModel`, the
/// model the Sources group in Settings drives, so a refusal reads as it does there.
enum DebugSources {
    /// Applies the steps in order and stops at the first refusal, whose message is returned (the action's
    /// error). Nil when every step applied. A step the group would not offer is a refusal too.
    @MainActor
    static func apply(_ steps: [DebugAction.SourcesStep], to model: SourcesSettingsModel) -> String? {
        for step in steps {
            if let refusal = apply(step, to: model) { return refusal }
        }
        return nil
    }

    @MainActor
    private static func apply(_ step: DebugAction.SourcesStep, to model: SourcesSettingsModel) -> String? {
        func row(_ source: AgentSource) -> SourceRow? { model.rows.first { $0.source == source } }
        switch step {
        case .antigravity(let on):
            model.setOn(.antigravity, on)
            return model.message
        case .primary(let source):
            guard row(source)?.canBePrimary == true else {
                return "\(source.displayName) can't be primary: it has to be on and report limits."
            }
            model.setPrimary(source)
            return nil
        case .color(let source, let hex):
            if let hex {
                return model.setColor(source, hex) ? nil : (model.colorMessage[source] ?? SourcesCopy.colorRefused)
            }
            model.resetColor(source)
            return nil
        case .mcp(let source, let on):
            // The consent switch shows only under an agent that is on or holds a Vault record.
            guard row(source)?.mcpAgent != nil else {
                return "\(SourcesCopy.mcpAgentSwitch(source)) is not offered: switch \(source.displayName) on first."
            }
            model.setMCPAgent(source, on)
            return nil
        }
    }
}

/// state.yaml's `sources:` (2.46.0): per source whether it is on and its color, the resolved Primary and each
/// agent's consent for Claude to read its project totals. Names, flags and colors only.
struct SourcesDebug: Equatable {
    struct Row: Equatable {
        var source: AgentSource
        var on: Bool
        var color: String
        /// Claude's consent to read this agent's totals; nil for Claude Code (consented per folder).
        var mcp: Bool?
    }

    var primary = AgentSource.claude
    var rows: [Row] = []

    static func of(setup: SetupSettings?, settings: AgentSourceSettings, consent: MCPAgentConsent) -> SourcesDebug {
        SourcesDebug(
            primary: settings.primary(setup: setup),
            rows: AgentSource.allCases.map {
                Row(source: $0, on: settings.isOn($0, setup: setup), color: settings.color(of: $0),
                    mcp: $0 == .claude ? nil : consent.isOn($0))
            })
    }
}

/// state.yaml's `codex:` (2.46.0): what the Codex service holds. Percents and times only: no path, no plan,
/// no session.
struct CodexDebug: Equatable {
    var running = false
    var asOf: Date?
    var freshTiers: [Tier] = []
    /// `not_installed`, `no_limits_yet` or `could_not_read`; nil with a reading or while off.
    var emptyState: String?
    var fiveHour: Int?
    var sevenDay: Int?

    @MainActor
    static func of(_ service: CodexUsageService) -> CodexDebug {
        let empty: String? = switch service.emptyState {
        case .notInstalled: "not_installed"
        case .noLimitsYet: "no_limits_yet"
        case .couldNotRead: "could_not_read"
        case nil: nil
        }
        func percent(_ tier: Tier) -> Int? { service.codexUsage?.tiers[tier]?.utilization.map { Int($0) } }
        return CodexDebug(running: service.running, asOf: service.asOf,
                          freshTiers: Tier.allCases.filter(service.freshTiers.contains), emptyState: empty,
                          fiveHour: percent(.fiveHour), sevenDay: percent(.sevenDay))
    }
}

/// state.yaml's `widget_sources:` (2.46.0): the combined bars' windows and, per section, the source, whether it
/// is collapsed, what it holds and the header's "as of" text.
struct WidgetSourcesDebug: Equatable {
    struct Section: Equatable {
        var source: AgentSource
        var collapsed: Bool
        /// `cards`, `empty` or `collapsed`.
        var content: String
        var cards: Int
        var asOf: String?
    }

    /// `five_hour` and `weekly`, in bar order.
    var combined: [String] = []
    var sections: [Section] = []

    static func of(_ layout: WidgetSourceLayout) -> WidgetSourcesDebug {
        WidgetSourcesDebug(
            combined: layout.combined.map { $0.window == .fiveHour ? "five_hour" : "weekly" },
            sections: layout.sections.map { section in
                var content = "empty"
                var cards = 0
                switch section.content {
                case .cards(let c): content = "cards"; cards = c.count
                case .empty: content = "empty"
                case .collapsed: content = "collapsed"
                }
                return Section(source: section.source, collapsed: section.header?.collapsed ?? false,
                               content: content, cards: cards, asOf: section.header?.asOf)
            })
    }
}

/// state.yaml's `vault_agents:` (2.46.0): each agent root the Vault holds and its meta's `last_ingest_ts`.
struct VaultAgentDebug: Equatable {
    var agent: AgentSource
    var lastIngestTs: String?

    /// The roots present, in agent order, each with its meta's last ingest ("" reads as nil).
    static func rows(roots: [AgentSource], meta: (AgentSource) -> VaultRootMeta?) -> [VaultAgentDebug] {
        roots.map { agent in
            let ts = meta(agent)?.lastIngestTs
            return VaultAgentDebug(agent: agent, lastIngestTs: ts?.isEmpty == false ? ts : nil)
        }
    }
}

/// The text the menu bar and the widget footer show, as state.yaml's `menu_bar_display:` and `footer:`.
struct MenuBarDisplayDebug: Equatable {
    var text: String?
    var tooltip: String?
}

extension DebugState {
    static func sourcesYAML(_ s: SourcesDebug) -> YAMLNode {
        var pairs = [YAMLPair("primary", .string(s.primary.rawValue))]
        for row in s.rows {
            pairs.append(YAMLPair(row.source.rawValue, .map([
                YAMLPair("on", .bool(row.on)), YAMLPair("color", .string(row.color)),
                YAMLPair("mcp", row.mcp.map(YAMLNode.bool) ?? .null),
            ])))
        }
        return .map(pairs)
    }

    static func codexYAML(_ c: CodexDebug) -> YAMLNode {
        func percent(_ v: Int?) -> YAMLNode { v.map(YAMLNode.int) ?? .null }
        return .map([
            YAMLPair("running", .bool(c.running)),
            YAMLPair("as_of", c.asOf.map { .string(ISO8601.stamp($0, fractional: false)) } ?? .null),
            YAMLPair("fresh_tiers", .list(c.freshTiers.map { .string($0.rawValue) })),
            YAMLPair("empty_state", c.emptyState.map(YAMLNode.string) ?? .null),
            YAMLPair("tiers", .map([YAMLPair("five_hour", percent(c.fiveHour)), YAMLPair("seven_day", percent(c.sevenDay))])),
        ])
    }

    static func widgetSourcesYAML(_ w: WidgetSourcesDebug) -> YAMLNode {
        let sections: [YAMLNode] = w.sections.map {
            .map([YAMLPair("source", .string($0.source.rawValue)), YAMLPair("collapsed", .bool($0.collapsed)),
                  YAMLPair("content", .string($0.content)), YAMLPair("cards", .int($0.cards)),
                  YAMLPair("as_of", $0.asOf.map(YAMLNode.string) ?? .null)])
        }
        return .map([YAMLPair("combined", .list(w.combined.map(YAMLNode.string))), YAMLPair("sections", .list(sections))])
    }

    static func vaultAgentsYAML(_ rows: [VaultAgentDebug]) -> YAMLNode {
        .list(rows.map {
            .map([YAMLPair("agent", .string($0.agent.rawValue)),
                  YAMLPair("last_ingest_ts", $0.lastIngestTs.map(YAMLNode.string) ?? .null)])
        })
    }

    static func menuBarDisplayYAML(_ m: MenuBarDisplayDebug) -> YAMLNode {
        .map([YAMLPair("text", m.text.map(YAMLNode.string) ?? .null), YAMLPair("tooltip", m.tooltip.map(YAMLNode.string) ?? .null)])
    }
}
