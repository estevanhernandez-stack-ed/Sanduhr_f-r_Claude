import Foundation
import Testing
@testable import Sanduhr

/// The band file (items 65f, 66): what band.json says for the meters mod, that background work
/// reaches it as its kind and state only, the writer's switch (off by default), owner-only
/// writes, deletes and the heartbeat, and the Combine sheet's `--band` through the command.
/// Temp folders and a throwaway defaults suite only.
@Suite("Band file")
@MainActor
struct BandFileTests {
    let t0 = Date(timeIntervalSince1970: 1_790_000_000)

    private func board() -> [Watcher] {
        var b = WatcherBoard()
        b.apply(.start(id: "ci", title: "CI on main", link: URL(string: "https://example.com/ci"), total: 12,
                       work: false, short: "CI"), now: t0)
        b.apply(.update(id: "ci", done: 4, note: "lint passed, a private note", state: .waiting), now: t0 + 30)
        b.applyStop(StopReport(session: "s1", folder: nil, tasks: [
            BackgroundTask(id: "t1", type: "shell", status: "running", description: "npm test -- --secret-flag", name: nil),
        ]), work: false, now: t0 + 40)
        return b.ordered()
    }

    private func object(_ data: Data) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func scratch() throws -> (URL, UserDefaults, String) {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-band-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let suite = "sanduhr-band-tests-\(UUID().uuidString)"
        return (dir, try #require(UserDefaults(suiteName: suite)), suite)
    }

    // MARK: What the file says

    @Test func agentWatchersCarryTitleStateProgressAndTimesOnly() throws {
        let o = try object(BandFile.json(watchers: board(), styles: [:], reduceMotion: false, now: t0 + 50))
        #expect(o["schema_version"] as? Int == 1)
        #expect(o["reduce_motion"] as? Bool == false)
        #expect(o["written_at"] as? String == HandoffFiles.stamp(t0 + 50))
        #expect(o["meters"] == nil)
        let rows = try #require(o["watchers"] as? [[String: Any]])
        #expect(rows.count == 2)
        let agent = try #require(rows.first { $0["source"] as? String == "agent" })
        #expect(Set(agent.keys) == ["source", "title", "short", "state", "done", "total", "started_at"])
        #expect(agent["title"] as? String == "CI on main")
        #expect(agent["short"] as? String == "CI")
        #expect(agent["state"] as? String == "waiting")
        #expect(agent["done"] as? Int == 4 && agent["total"] as? Int == 12)
        #expect(agent["started_at"] as? String == HandoffFiles.stamp(t0))
    }

    @Test func backgroundWorkIsItsKindAndStateNeverItsDescription() throws {
        let data = BandFile.json(watchers: board(), styles: [:], reduceMotion: true, now: t0)
        let text = String(decoding: data, as: UTF8.self)
        #expect(!text.contains("npm"))
        #expect(!text.contains("secret"))
        #expect(!text.contains("private note"))
        #expect(!text.contains("example.com"))
        let rows = try #require(try object(data)["watchers"] as? [[String: Any]])
        let auto = try #require(rows.first { $0["source"] as? String == "automatic" })
        #expect(Set(auto.keys) == ["source", "kind", "state"])
        #expect(auto["kind"] as? String == "shell" && auto["state"] as? String == "running")
    }

    @Test func endedWatchersCarryTheirEndAndStates() throws {
        var b = WatcherBoard()
        b.apply(.start(id: "a", title: "Deploy", link: nil, total: nil, work: false), now: t0)
        b.apply(.start(id: "b", title: "Nightly", link: nil, total: nil, work: false), now: t0)
        let t1 = t0 + WatcherBoard.agentWindow + 5
        b.tick(now: t1)
        b.apply(.end(id: "a", result: .passed, note: nil), now: t1 + 1)
        let rows = try #require(try object(BandFile.json(watchers: b.ordered(), styles: [:], reduceMotion: false,
                                                         now: t1 + 2))["watchers"] as? [[String: Any]])
        let passed = try #require(rows.first { $0["title"] as? String == "Deploy" })
        #expect(passed["state"] as? String == "passed")
        #expect(passed["ended_at"] as? String == HandoffFiles.stamp(t1 + 1))
        #expect(passed["total"] == nil)
        #expect(rows.first { $0["title"] as? String == "Nightly" }?["state"] as? String == "lost_touch")
    }

    @Test func meterStylesKeepTheBandSegmentsOnly() throws {
        let styles: [SanduhrSegment: SegmentStyle] = [
            .session: SegmentStyle(ink: ["#ff2a6d", "#05d9e8"], font: .script, bold: true),
            .model: SegmentStyle(ink: ["#ffffff"]),
            .weekly: SegmentStyle(),
        ]
        let o = try object(BandFile.json(watchers: nil, styles: styles, reduceMotion: false, now: t0))
        #expect(o["watchers"] == nil)
        let meters = try #require(o["meters"] as? [String: Any])
        let all = try #require(meters["styles"] as? [String: Any])
        #expect(Set(all.keys) == ["session"])
        let session = try #require(all["session"] as? [String: Any])
        #expect(session["ink"] as? [String] == ["#ff2a6d", "#05d9e8"])
        #expect(session["font"] as? String == "script")
        #expect(session["bold"] as? Bool == true)
        // The defaults' copy reads back; junk reads as none.
        let text = try #require(BandFile.stylesText(styles))
        #expect(BandFile.styles(from: text) == [.session: SegmentStyle(ink: ["#ff2a6d", "#05d9e8"], font: .script, bold: true)])
        #expect(BandFile.styles(from: "{not json").isEmpty)
        #expect(BandFile.styles(from: #"{"session":{"ink":"red"}}"#).isEmpty)
        #expect(BandFile.stylesText([.model: SegmentStyle(bold: true)]) == nil)
    }

    // MARK: The writer

    @Test func theSwitchIsOffByDefaultAndNothingIsWritten() throws {
        let (dir, defaults, suite) = try scratch()
        defer { try? FileManager.default.removeItem(at: dir); defaults.removePersistentDomain(forName: suite) }
        let w = BandFileWriter(support: dir, defaults: defaults, reduceMotion: { false }, now: { self.t0 },
                               watchers: { self.board() })
        #expect(!w.showsWatchers)
        w.refresh()
        #expect(!FileManager.default.fileExists(atPath: w.url.path))
    }

    @Test func writesOwnerOnlyWhileOnAndDeletesWhenOff() throws {
        let (dir, defaults, suite) = try scratch()
        defer { try? FileManager.default.removeItem(at: dir); defaults.removePersistentDomain(forName: suite) }
        var reduce = false
        var watchers = board()
        let w = BandFileWriter(support: dir, defaults: defaults, reduceMotion: { reduce }, now: { self.t0 },
                               watchers: { watchers })
        defaults.set(true, forKey: BandFile.watchersKey)
        w.refresh()
        let attrs = try FileManager.default.attributesOfItem(atPath: w.url.path)
        #expect((attrs[.posixPermissions] as? NSNumber)?.intValue == 0o600)
        #expect(try (object(Data(contentsOf: w.url))["watchers"] as? [Any])?.count == 2)

        // A change rewrites it; Reduce Motion is carried.
        watchers = []
        reduce = true
        w.refresh()
        let o = try object(Data(contentsOf: w.url))
        #expect((o["watchers"] as? [Any])?.isEmpty == true)
        #expect(o["reduce_motion"] as? Bool == true)

        // Off with no looks: deleted. Looks alone keep the file, without watchers.
        defaults.set(false, forKey: BandFile.watchersKey)
        w.refresh()
        #expect(!FileManager.default.fileExists(atPath: w.url.path))
        w.setMeterStyles([.weekly: SegmentStyle(italic: true)])
        let styled = try object(Data(contentsOf: w.url))
        #expect(styled["watchers"] == nil)
        #expect(styled["meters"] != nil)
        w.setMeterStyles(nil)
        #expect(!FileManager.default.fileExists(atPath: w.url.path))
        #expect(defaults.string(forKey: BandFile.stylesKey) == nil)
    }

    @Test func unchangedWritesWaitForTheHeartbeatAndQuittingDropsWatchers() throws {
        let (dir, defaults, suite) = try scratch()
        defer { try? FileManager.default.removeItem(at: dir); defaults.removePersistentDomain(forName: suite) }
        var now = t0
        let w = BandFileWriter(support: dir, defaults: defaults, reduceMotion: { false }, now: { now },
                               watchers: { self.board() })
        defaults.set(true, forKey: BandFile.watchersKey)
        w.refresh()
        let first = try object(Data(contentsOf: w.url))["written_at"] as? String
        now = t0 + 10
        w.refresh()
        #expect(try object(Data(contentsOf: w.url))["written_at"] as? String == first)
        now = t0 + BandFile.heartbeat
        w.refresh()
        #expect(try object(Data(contentsOf: w.url))["written_at"] as? String == HandoffFiles.stamp(now))
        w.refresh(quitting: true)
        #expect((try object(Data(contentsOf: w.url))["watchers"] as? [Any])?.isEmpty == true)
    }

    // MARK: The Combine sheet's switch

    @Test func theBandFlagRoundTripsThroughTheCommand() throws {
        let script = "/x/integrations/0123456789ab/sanduhr_statusline.py"
        let picks = StatuslinePicks(drop: ["x"], ours: [.session: SegmentStyle(bold: true)])
        let selection = StatuslineSelection(theirs: picks, mine: [.session, .model], band: true)
        let built = IntegrationInstaller.statuslineCommand(python: "python3", script: script, chain: "my.sh",
                                                           selection: selection)
        #expect(built.hasSuffix(" --mine session,model --band"))
        let parsed = try #require(IntegrationInstaller.parseStatusline(built))
        #expect(parsed.selection == selection)
        let bandOnly = IntegrationInstaller.statuslineCommand(python: "python3", script: script, chain: "a",
                                                              selection: StatuslineSelection(band: true))
        #expect(bandOnly.hasSuffix("--join line --band"))
        #expect(IntegrationInstaller.parseStatusline(bandOnly)?.selection == StatuslineSelection(band: true))
        #expect(IntegrationInstaller.parseStatusline(bandOnly + " --band") == nil)
        #expect(!IntegrationInstaller.statuslineCommand(python: "python3", script: script, chain: "a").contains("--band"))
    }
}
