import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 1c: the Catalog grouped (what needs a look, yours, plugins by source, MCP
/// servers, the marketplaces' plugins last), what each plugin brings, and what a new session in
/// each seat starts with. Made-up seats under the temp directory; nothing reads the real folders.
@Suite("Mods & Config: Catalog groups and session load")
struct ModsCatalogGroupsTests {
    static func put(_ h: TempHome, _ relative: String, _ text: String) {
        ModsPageTests.put(h, relative, text)
    }

    /// A seat at `.claude-c`: `kit` on (two skills, one kept from the model, an agent, a command,
    /// an MCP server), `spare` off, `kit` listed by a marketplace with `extra` installed nowhere,
    /// two skills of its own (one overridden to name-only, one off), a command and an agent.
    static func seatC(_ h: TempHome, settings: String? = nil) {
        let s = ".claude-c"
        put(h, "\(s)/settings.json", settings ?? """
        {"enabledPlugins":{"kit@market":true,"spare@market":false},
         "skillOverrides":{"quiet":"name-only","gone":"off","check":"off"}}
        """)
        put(h, "cache/kit/.claude-plugin/plugin.json", #"{"name":"kit","version":"2.0.0"}"#)
        put(h, "cache/kit/skills/check/SKILL.md", "---\nname: check\ndescription: Checks it.\nwhen_to_use: Before a commit.\n---\n")
        put(h, "cache/kit/skills/manual/SKILL.md", "---\nname: manual\ndescription: Only by hand.\ndisable-model-invocation: true\n---\n")
        put(h, "cache/kit/agents/fixer.md", "---\nname: fixer\ndescription: Fixes it.\n---\n")
        put(h, "cache/kit/commands/run.md", "---\ndescription: Runs it.\n---\n")
        put(h, "cache/kit/.mcp.json", #"{"mcpServers":{"kit-server":{"command":"kit-mcp"}}}"#)
        put(h, "cache/spare/.claude-plugin/plugin.json", #"{"name":"spare"}"#)
        put(h, "cache/spare/skills/idle/SKILL.md", "---\nname: idle\ndescription: Never loads.\n---\n")
        put(h, "\(s)/plugins/installed_plugins.json", """
        {"version":2,"plugins":{
          "kit@market":[{"scope":"user","installPath":"\(h.at("cache/kit"))"}],
          "spare@market":[{"scope":"user","installPath":"\(h.at("cache/spare"))"}]}}
        """)
        put(h, "\(s)/plugins/known_marketplaces.json", """
        {"market":{"source":{"source":"github","repo":"example/market"},"installLocation":"\(h.at("markets/market"))"}}
        """)
        put(h, "markets/market/.claude-plugin/marketplace.json", """
        {"name":"market","plugins":[{"name":"kit"},{"name":"extra","description":"Installed nowhere."}]}
        """)
        put(h, "\(s)/skills/loud/SKILL.md", "---\nname: loud\ndescription: A long one.\n---\n")
        put(h, "\(s)/skills/quiet/SKILL.md", "---\nname: quiet\ndescription: Listed by name only.\n---\n")
        put(h, "\(s)/skills/gone/SKILL.md", "---\nname: gone\ndescription: Turned off.\n---\n")
        put(h, "\(s)/commands/ship.md", "---\ndescription: Ships it.\n---\n")
        put(h, "\(s)/agents/helper.md", "---\nname: helper\ndescription: Helps.\n---\n")
        put(h, "\(s)/.claude.json", #"{"mcpServers":{"files":{"command":"files-mcp"}}}"#)
    }

    static func scan(_ h: TempHome) -> SeatScan {
        SeatScanner.scan(seat: h.at(".claude-c"), home: h.path, managedFolder: h.at("managed"))
    }

    // MARK: What a plugin brings

    @Test func readsAPluginsSkillsAgentsCommandsAndServersAndWritesNothing() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        let before = TempHomeSnapshot(h)
        let scan = Self.scan(h)
        #expect(TempHomeSnapshot(h) == before)
        let kit = try #require(scan.plugins.first { $0.name == "kit" })
        let c = try #require(scan.pluginContents[kit.id])
        #expect(c.skills.map(\.name) == ["check", "manual"])
        #expect(c.skills[0].whenToUse == "Before a commit.")
        #expect(!c.skills[0].modelHidden && c.skills[1].modelHidden)
        #expect(c.agents.map(\.name) == ["fixer"])
        #expect(c.commands.map(\.name) == ["run"])
        #expect(c.mcpServers == 1)
        // An off plugin's contents are read too, for its card.
        let spare = try #require(scan.plugins.first { $0.name == "spare" })
        #expect(scan.pluginContents[spare.id]?.skills.map(\.name) == ["idle"])
        #expect(scan.listing.overrides == ["quiet": "name-only", "gone": "off", "check": "off"])
    }

    @Test func listingSettingsComeFromTheSeatWithManagedOverIt() {
        let l = ListingSettings.read([
            ["skillListingBudgetFraction": 0.02, "skillListingMaxDescChars": 200,
             "env": ["SLASH_COMMAND_TOOL_CHAR_BUDGET": "12000"], "skillOverrides": ["a": "off", "b": "on"]],
            ["skillOverrides": ["b": "name-only"], "skillListingMaxDescChars": 300],
        ])
        #expect(l.budgetFraction == 0.02 && l.maxDescChars == 300 && l.budgetChars == 12_000)
        #expect(l.overrides == ["a": "off", "b": "name-only"])
        // Nonsense values keep the defaults.
        let d = ListingSettings.read([["skillListingBudgetFraction": -1, "skillListingMaxDescChars": "x",
                                       "env": ["SLASH_COMMAND_TOOL_CHAR_BUDGET": "lots"]]])
        #expect(d == ListingSettings())
    }

    // MARK: Session load

    @Test func countsWhatANewSessionListsAndKeepsOut() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        let load = SessionLoad.build(Self.scan(h))
        // Yours: loud and ship listed, quiet by name, gone off. Kit: check and run listed (its
        // skills ignore skillOverrides, so "check": "off" doesn't apply), manual kept out by its
        // frontmatter. Spare is off and adds nothing.
        #expect(load.listed == 4)
        #expect(load.nameOnly == 1)
        #expect(load.hidden == 2)
        #expect(load.agents == 2)
        // Kit's server and the seat's own one.
        #expect(load.mcpServers == 2)
        #expect(Set(load.sources.map(\.name)) == ["Yours", "kit"])
        let kit = load.sources.first { $0.name == "kit" }
        #expect(kit?.isPlugin == true && kit?.listed == 2 && kit?.agents == 1)
        // Plugin entries are named as Claude Code lists them, plugin:skill.
        let check = SeatEntry(name: "check", description: "Checks it.", scope: .seat, path: "", whenToUse: "Before a commit.")
        let checkChars = SessionLoad.entryChars(name: "kit:check", entry: check, nameOnly: false, cap: 1_536)
        let run = SeatEntry(name: "run", description: "Runs it.", scope: .seat, path: "")
        #expect(kit?.listingChars == checkChars + SessionLoad.entryChars(name: "kit:run", entry: run, nameOnly: false, cap: 1_536))
        #expect(load.listingChars == load.sources.reduce(0) { $0 + $1.listingChars })
    }

    @Test func anEntryIsItsNameAndItsTextCutToTheCap() {
        let e = SeatEntry(name: "x", description: String(repeating: "d", count: 50), scope: .seat, path: "",
                          whenToUse: String(repeating: "w", count: 50))
        #expect(SessionLoad.entryChars(name: "x", entry: e, nameOnly: false, cap: 1_536) == 1 + 101 + 4)
        #expect(SessionLoad.entryChars(name: "x", entry: e, nameOnly: false, cap: 20) == 1 + 20 + 4)
        #expect(SessionLoad.entryChars(name: "x", entry: e, nameOnly: true, cap: 20) == 1 + 4)
    }

    @Test func theBudgetIsAShareOfTheWindowUnlessAFixedOneIsSet() {
        var load = SessionLoad(seat: "s")
        #expect(load.budgetTokens(context: 200_000) == 2_000)
        #expect(load.budgetTokens(context: 1_000_000) == 10_000)
        load.listingChars = 8_004
        #expect(load.listingTokens == 2_001 && load.overBudget)
        load.settings.budgetFraction = 0.02
        #expect(!load.overBudget)
        load.settings.budgetChars = 4_000
        #expect(load.budgetTokens(context: 200_000) == 1_000 && load.overBudget)
    }

    @Test func aPluginOnFromTwoSourcesLoadsOnce() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h, settings: #"{"enabledPlugins":{"kit@market":true,"kit@other":true}}"#)
        Self.put(h, ".claude-c/plugins/installed_plugins.json", """
        {"version":2,"plugins":{
          "kit@market":[{"scope":"user","installPath":"\(h.at("cache/kit"))"}],
          "kit@other":[{"scope":"user","installPath":"\(h.at("cache/kit"))"}]}}
        """)
        let load = SessionLoad.build(Self.scan(h))
        #expect(load.sources.filter { $0.name == "kit" }.count == 1)
        #expect(load.mcpServers == 2)
    }

    @Test func shortNumbers() {
        #expect(SessionLoad.short(850) == "850")
        #expect(SessionLoad.short(1_234) == "1.2K")
        #expect(SessionLoad.short(23_456) == "23K")
    }

    // MARK: Groups

    static func seats(_ h: TempHome, work: Bool = false) -> [(seat: Seat, scan: SeatScan)] {
        let store = MemoryDefaults()
        let path = h.at(".claude-c")
        SeatPreferences.setLabel("Home", for: path, in: store)
        SeatPreferences.setMark(work ? .work : .personal, for: path, in: store)
        return [(Seats.seat(path, origin: .found, home: h.path, store: store), Self.scan(h))]
    }

    @Test func cardsGroupFromWhatNeedsALookToWhatIsInstalledNowhere() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        let cards = ConfigCatalog.build(Self.seats(h))
        let sections = ConfigCatalog.sections(cards)
        #expect(sections.map(\.group) == [.yours, .plugins, .mcp, .available])
        let yours = try #require(sections.first { $0.group == .yours })
        #expect(yours.runs.map(\.title) == ["Skills", "Agents", "Commands"])
        let plugins = try #require(sections.first { $0.group == .plugins })
        // Sanduhr's own run first, then by source.
        #expect(plugins.runs.map(\.title) == [ConfigCatalog.sanduhrSource, "market"])
        #expect(plugins.runs[1].cards.map(\.name) == ["kit", "spare"])
        let available = try #require(sections.first { $0.group == .available })
        #expect(available.runs.map(\.title) == ["market"])
        #expect(available.runs[0].cards.map(\.name) == ["extra"])
        #expect(!CatalogGroup.available.startsOpen && CatalogGroup.plugins.startsOpen)

        // A failed check moves a card to Needs a look, first.
        let kit = try #require(cards.first { $0.name == "kit" })
        let flagged = ConfigCatalog.sections(cards, failedChecks: [kit.id])
        #expect(flagged.first?.group == .attention && flagged.first?.runs[0].cards.map(\.name) == ["kit"])
        #expect(ConfigCatalog.counts(cards).groups == [0, 5, 3, 1, 1])
        #expect(ConfigCatalog.counts(cards, failedChecks: [kit.id]).groups == [1, 5, 2, 1, 1])
    }

    @Test func pluginCardsCarryWhatTheyBringAndTheirLoad() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        let seats = Self.seats(h)
        let cards = ConfigCatalog.build(seats)
        let kit = try #require(cards.first { $0.name == "kit" })
        #expect(kit.contents?.summary == "2 skills, 1 agent, 1 command, 1 MCP server")
        let source = try #require(SessionLoad.build(seats[0].scan).sources.first { $0.name == "kit" })
        #expect(kit.load == [seats[0].seat.path: source.tokens])
        // Off: what it brings shows, but it adds nothing to a session.
        let spare = try #require(cards.first { $0.name == "spare" })
        #expect(spare.contents?.skills == ["idle"] && spare.load.isEmpty)
        // Search reaches inside a plugin.
        var f = CatalogFilter()
        f.search = "fixer"
        #expect(ConfigCatalog.filter(cards, f, demo: false).map(\.name) == ["kit"])
    }

    @Test func pluginsSortByLoadWhenAsked() throws {
        var a = CatalogCard(kind: .plugin, name: "a")
        a.chips = [CatalogChip(seat: "s", label: "S", mark: .personal, state: .on)]
        a.installedSources = ["Marketplace m"]
        a.load = ["s": 10]
        var b = a
        b = CatalogCard(kind: .plugin, name: "b")
        b.chips = a.chips
        b.installedSources = a.installedSources
        b.load = ["s": 500]
        let byName = ConfigCatalog.sections([b, a])[0].runs[0].cards.map(\.name)
        let byLoad = ConfigCatalog.sections([a, b], byLoad: true)[0].runs[0].cards.map(\.name)
        #expect(byName == ["a", "b"] && byLoad == ["b", "a"])
    }

    @Test func aPersonalSeatsContentsReplaceAWorkSeatsAcrossABuild() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        // A personal seat with its own copy of kit, which brings one skill only.
        Self.put(h, ".claude-p/settings.json", #"{"enabledPlugins":{"kit@market":true}}"#)
        Self.put(h, "cache/kit-p/.claude-plugin/plugin.json", #"{"name":"kit"}"#)
        Self.put(h, "cache/kit-p/skills/solo/SKILL.md", "---\nname: solo\ndescription: Alone.\n---\n")
        Self.put(h, ".claude-p/plugins/installed_plugins.json", """
        {"version":2,"plugins":{"kit@market":[{"scope":"user","installPath":"\(h.at("cache/kit-p"))"}]}}
        """)
        let store = MemoryDefaults()
        SeatPreferences.setMark(.work, for: h.at(".claude-c"), in: store)
        SeatPreferences.setMark(.personal, for: h.at(".claude-p"), in: store)
        let seats = [".claude-c", ".claude-p"].map { name in
            (seat: Seats.seat(h.at(name), origin: .found, home: h.path, store: store),
             scan: SeatScanner.scan(seat: h.at(name), home: h.path, managedFolder: h.at("managed")))
        }
        let kit = try #require(ConfigCatalog.build(seats).first { $0.name == "kit" })
        // The work seat came first, but the personal seat's contents win, so demo mode keeps them.
        #expect(kit.contents?.skills == ["solo"] && kit.contents?.work == false)
        let shown = try #require(kit.demoView())
        #expect(shown.contents?.skills == ["solo"])
        #expect(Set(shown.load.keys) == [h.at(".claude-p")])
    }

    @Test func aPartialWalkMakesTheLoadAFloor() {
        var scan = SeatScan(seat: "/s")
        #expect(!SessionLoad.build(scan).partial)
        scan.pluginsPartial = true
        #expect(SessionLoad.build(scan).partial)
    }

    @Test func demoModeDropsAWorkSeatsContentsAndLoad() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatC(h)
        var kit = try #require(ConfigCatalog.build(Self.seats(h, work: true)).first { $0.name == "kit" })
        // Listed by the work seat's marketplace only: nothing but work stands behind it.
        #expect(kit.demoView() == nil)
        // Also on in a personal seat: the card shows, without the work seat's contents or load.
        kit.chips.append(CatalogChip(seat: "/p", label: "P", mark: .personal, state: .on))
        kit.sources.append("Marketplace public")
        kit.load["/p"] = 7
        let shown = try #require(kit.demoView())
        #expect(shown.contents == nil)
        #expect(shown.load == ["/p": 7])
    }
}
