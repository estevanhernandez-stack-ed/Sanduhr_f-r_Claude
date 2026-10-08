import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// Per-song looks for now playing (item 65c) and themes with a title style (item 65d). Made-up
/// songs and temp folders only; the reasons' wording is pinned here and in the Python tests.

private func info(_ title: String, _ artist: String?, app: String = "com.apple.Music") -> NowPlayingInfo {
    NowPlayingInfo(title: title, artist: artist, playing: true, bundleID: app)
}

private func look(_ changes: [String: Any?] = [:]) -> [String: Any] {
    var o: [String: Any] = ["artist": "Kavinsky", "title": "Nightcall", "colors": ["#ff2a6d", "#d16ba5", "#05d9e8"],
                            "font": "small-caps", "mood": "neon night drive"]
    for (k, v) in changes {
        if let v { o[k] = v } else { o.removeValue(forKey: k) }
    }
    return o
}

@Suite("Now playing looks: seeding and keys")
struct NowPlayingLookSeedTests {
    @Test func theHashIsFNV1a() {
        #expect(NowPlayingLooks.hash("") == 0xcbf2_9ce4_8422_2325)
        #expect(NowPlayingLooks.hash("a") == 0xaf63_dc4c_8601_ec8c)
    }

    @Test func aSongSeedsTheSameLookEveryTime() {
        let key = NowPlayingLooks.cacheKey(info("Nightcall", "Kavinsky"))
        let a = NowPlayingLooks.seeded(key), b = NowPlayingLooks.seeded(key)
        #expect(a == b)
        #expect(MessagePalette.name(of: a.colors) != nil)   // one of the owner's palettes
        #expect(a.font.map(NowPlayingLooks.seedStyles.contains) == true)
        #expect(a.mood == nil)
    }

    @Test func songsSpreadOverThePalettesAndStyles() {
        var palettes = Set<String>(), styles = Set<String>()
        for i in 0..<200 {
            let l = NowPlayingLooks.seeded(NowPlayingLooks.cacheKey(info("Song \(i)", "Band")))
            palettes.insert(MessagePalette.name(of: l.colors) ?? "?")
            styles.insert(l.font?.rawValue ?? "none")
        }
        #expect(palettes == Set(MessagePalette.all.map(\.name)))
        #expect(styles == Set(NowPlayingLooks.seedStyles.map(\.rawValue)))
    }

    @Test func theCacheKeyIsAppTitleAndArtist() {
        #expect(NowPlayingLooks.cacheKey(info("A", "B")) != NowPlayingLooks.cacheKey(info("A", "B", app: "com.spotify.client")))
        #expect(NowPlayingLooks.cacheKey(info("A", nil)) == "com.apple.Music\u{1F}A\u{1F}")
    }

    @Test func anApprovedLookWinsInAnyAppIgnoringCaseAndSpaces() {
        let approved = [NowPlayingLooks.songKey(artist: "Kavinsky", title: "Nightcall"):
                            SongLook(colors: ["ffffff", "05d9e8"], font: .script)]
        let hit = NowPlayingLooks.resolve(info("  nightcall ", "KAVINSKY", app: "com.google.Chrome"), approved: approved)
        #expect(hit == SongLook(colors: ["ffffff", "05d9e8"], font: .script))
        let other = info("Odd Look", "Kavinsky")
        #expect(NowPlayingLooks.resolve(other, approved: approved) == NowPlayingLooks.seeded(NowPlayingLooks.cacheKey(other)))
        #expect(NowPlayingLooks.songKey(artist: "Daft  Punk", title: "One More Time") ==
                NowPlayingLooks.songKey(artist: "daft punk", title: "one more  time"))
    }
}

@Suite("Now playing looks: the checks")
struct NowPlayingLookCheckTests {
    @Test func theNamesAndLimitsMatchTheServer() {
        #expect(NowPlayingLookProposal.requestFile == "now-playing-looks-request.json")
        #expect(NowPlayingLookProposal.resultFile == "now-playing-looks-result.json")
        #expect(NowPlayingLookProposal.looksFile == "now-playing-looks.json")
        #expect(NowPlayingLookProposal.maxLooks == 50)
        #expect(NowPlayingLookProposal.maxArtistChars == 100)
        #expect(NowPlayingLookProposal.maxTitleChars == 200)
        #expect(NowPlayingLookProposal.maxMoodWords == 3)
        #expect(NowPlayingLookProposal.maxMoodChars == 40)
        #expect(NowPlayingLookProposal.minContrastOnBlack == 3.0)
        #expect(LetterStyle.tagNames == LetterStyle.allCases.map(\.rawValue).joined(separator: ", "))
    }

    @Test func aGoodLookIsCleaned() {
        let checked = NowPlayingLookProposal.validate([look(["colors": ["#FF2A6D", "05D9E8", "#fd0"]]),
                                                       look(["title": " Odd Look ", "font": "SmallCaps", "mood": nil])])
        #expect(checked.reasons.isEmpty)
        #expect(checked.looks.first == ProposedSongLook(artist: "Kavinsky", title: "Nightcall",
                                                        look: SongLook(colors: ["ff2a6d", "05d9e8", "ffdd00"],
                                                                       font: .smallCaps, mood: "neon night drive")))
        #expect(checked.looks.last?.title == "Odd Look")
        #expect(checked.looks.last?.look.mood == nil)
    }

    /// The Python test's cases, reason for reason.
    @Test func badLooksAreNamedAsTheServerNamesThem() {
        let cases: [(Any?, [String])] = [
            (nil, ["looks must be a list of 1 to 50 looks"]),
            ([Any](), ["looks must be a list of 1 to 50 looks"]),
            (Array(repeating: look(), count: 51), ["looks must be a list of 1 to 50 looks"]),
            (["x"], ["look 1 must be an object with artist, title and colors"]),
            ([look(["artist": ""])], ["look 1 artist must be one line of 1 to 100 characters"]),
            ([look(), look(["title": "a\nb"])], ["look 2 title must be one line of 1 to 200 characters"]),
            ([look(["colors": ["#ff2a6d"]])], ["look 1 colors must be 2 to 4 hex colors, like [\"#ff2a6d\", \"#05d9e8\"]"]),
            ([look(["colors": ["#ff2a6d", "red"]])], ["look 1 colors must be 2 to 4 hex colors, like [\"#ff2a6d\", \"#05d9e8\"]"]),
            ([look(["colors": ["#ff2a6d", "#ff2a6d\n"]])], ["look 1 colors must be 2 to 4 hex colors, like [\"#ff2a6d\", \"#05d9e8\"]"]),
            ([look(["colors": ["#ff2a6d", "#202020"]])], ["look 1 color #202020 is too dark for the black notch (1.3:1, needs 3.0:1)"]),
            ([look(["font": "comic"])], ["look 1 font must be one of: bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps"]),
            ([look(["mood": "one two three four"])], ["look 1 mood must be at most 3 words and 40 characters"]),
            ([look(["mood": String(repeating: "x", count: 41)])], ["look 1 mood must be at most 3 words and 40 characters"]),
            ([look(["cover": "x"])], ["look 1 has an unknown field: cover"]),
        ]
        for (raw, reasons) in cases {
            #expect(NowPlayingLookProposal.validate(raw).reasons == reasons, "\(reasons)")
        }
    }

    @Test func requestsDecodeRefuseOrDrop() throws {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        func request(_ looks: Any, at: Date) throws -> Data {
            try JSONSerialization.data(withJSONObject: ["schema_version": 1, "id": "req1",
                                                        "requested_at": HandoffFiles.stamp(at), "looks": looks])
        }
        guard case .proposal(let p) = NowPlayingLookProposal.decode(try request([look()], at: now), now: now) else {
            Issue.record("not a proposal"); return
        }
        #expect(p.looks.count == 1)
        #expect(NowPlayingLookProposal.decode(try request([look(["font": "x"])], at: now), now: now)
                == .refused(id: "req1", reasons: ["look 1 font must be one of: \(LetterStyle.tagNames)"]))
        #expect(NowPlayingLookProposal.decode(try request([look()], at: now.addingTimeInterval(-601)), now: now) == .discard)
        #expect(NowPlayingLookProposal.decode(Data("nope".utf8), now: now) == .discard)
    }

    @Test func theLooksFileRoundTripsAndMergeKeepsTheNewest() {
        let a = ProposedSongLook(artist: "A", title: "One", look: SongLook(colors: ["ffffff", "05d9e8"], font: .bold, mood: "bright"))
        let b = ProposedSongLook(artist: "B", title: "Two", look: SongLook(colors: ["ffffff", "ffd200"]))
        let back = NowPlayingLookProposal.readLooks(NowPlayingLookProposal.looksJSON([a, b]))
        #expect(back == [a, b])
        let a2 = ProposedSongLook(artist: "a", title: "ONE", look: SongLook(colors: ["fff6b7", "ffd200"]))
        #expect(NowPlayingLookProposal.merge([a2], into: [a, b]) == [b, a2])
        let many = (0..<600).map { ProposedSongLook(artist: "X", title: "\($0)", look: b.look) }
        let kept = NowPlayingLookProposal.merge(many, into: [])
        #expect(kept.count == NowPlayingLookProposal.maxStored)
        #expect(kept.first?.title == "100")
        #expect(NowPlayingLookProposal.readLooks(nil).isEmpty)
        #expect(NowPlayingLookProposal.readLooks(Data("{".utf8)).isEmpty)
    }
}

// MARK: - The store, on temp folders

@MainActor
@Suite("Now playing look store")
struct NowPlayingLookStoreTests {
    final class Folder {
        let root: URL
        let paths: NowPlayingLookStore.Paths
        init() {
            root = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-looks-\(UUID().uuidString)")
            paths = NowPlayingLookStore.Paths(support: root)
            try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        }
        deinit { try? FileManager.default.removeItem(at: root) }

        func result() -> [String: Any]? {
            guard let data = try? Data(contentsOf: paths.result),
                  let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
            return root["result"] as? [String: Any]
        }
        func drop(_ looks: [[String: Any]], id: String = "req1") {
            let root: [String: Any] = ["schema_version": 1, "id": id, "requested_at": HandoffFiles.stamp(NowPlayingLookStoreTests.t0), "looks": looks]
            try? JSONSerialization.data(withJSONObject: root).write(to: paths.request)
        }
    }

    static let t0 = Date(timeIntervalSince1970: 1_800_000_000)
    private func store(_ f: Folder, direct: Bool = false, style: Bool = true) -> NowPlayingLookStore {
        NowPlayingLookStore(paths: f.paths, direct: { direct }, styleOn: { style }, now: { Self.t0 })
    }

    @Test func aSuggestionWaitsThenSaves() throws {
        let f = Folder()
        let s = store(f, style: false)
        let song = info("Nightcall", "Kavinsky")
        let seeded = s.look(for: song)
        f.drop([look()])
        s.check()
        #expect(!FileManager.default.fileExists(atPath: f.paths.request.path))
        #expect(s.pending?.looks.count == 1)
        #expect(f.result()?["status"] as? String == "pending_approval")
        #expect(f.result()?["style_on"] as? Bool == false)
        #expect(!FileManager.default.fileExists(atPath: f.paths.looks.path))   // nothing on disk before Save
        #expect(s.look(for: song) == seeded)
        s.approve()
        #expect(s.pending == nil)
        #expect(f.result()?["status"] as? String == "applied")
        #expect(f.result()?["looks_saved"] as? Int == 1)
        let mode = try FileManager.default.attributesOfItem(atPath: f.paths.looks.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
        #expect(s.look(for: song) == SongLook(colors: ["ff2a6d", "d16ba5", "05d9e8"], font: .smallCaps, mood: "neon night drive"))
        // A new store reads the saved looks back.
        let again = store(f)
        again.loadIfNeeded()
        #expect(again.look(for: song).font == .smallCaps)
    }

    @Test func directSavesAtOnceAndDismissAnswers() {
        let f = Folder()
        let s = store(f, direct: true)
        f.drop([look()])
        s.check()
        #expect(s.pending == nil)
        #expect(s.saved.count == 1)
        #expect(f.result()?["status"] as? String == "applied")

        let g = Folder()
        let t = store(g)
        g.drop([look()])
        t.check()
        t.dismiss()
        #expect(g.result()?["status"] as? String == "rejected")
        #expect(g.result()?["reasons"] as? [String] == ["dismissed by the user"])
        #expect(t.saved.isEmpty)
    }

    @Test func aRefusedRequestIsAnsweredWithReasons() {
        let f = Folder()
        let s = store(f)
        f.drop([look(["colors": ["#000000", "#111111"]])])
        s.check()
        #expect(s.pending == nil)
        #expect(f.result()?["status"] as? String == "rejected")
        #expect((f.result()?["reasons"] as? [String])?.count == 2)
    }

    @Test func clearRemovesTheFileAndGoesBackToSeeds() {
        let f = Folder()
        let s = store(f, direct: true)
        f.drop([look()])
        s.check()
        let song = info("Nightcall", "Kavinsky")
        #expect(s.look(for: song).mood == "neon night drive")
        s.clear()
        #expect(!FileManager.default.fileExists(atPath: f.paths.looks.path))
        #expect(s.saved.isEmpty)
        #expect(s.look(for: song) == NowPlayingLooks.seeded(NowPlayingLooks.cacheKey(song)))
    }
}

// MARK: - Drawing

@Suite("Now playing looks: drawing")
struct NowPlayingLookDrawTests {
    @Test func aStyledTitleMeasuresAsDrawn() {
        let plain = MessageTypography.plan("Nightcall", style: nil, family: "Helvetica")
        let script = MessageTypography.plan("Nightcall", style: .script, family: "Helvetica")
        #expect(MessageTypography.width(plain, size: 14) > 0)
        #expect(MessageTypography.width(script, size: 14) > 0)
        #expect(MessageTypography.width(MessageTypography.plan("", style: .bold, family: "Helvetica"), size: 14) == 0)
        let upright = MessageTypography.plan("Nightcall", style: nil, family: "Helvetica")
        let slanted = MessageTypography.plan("Nightcall", style: .italic, family: "Helvetica")
        #expect(MessageTypography.width(slanted, size: 14) > MessageTypography.width(upright, size: 14))
    }

    @MainActor
    @Test func offOrNothingPlayingDrawsAsBefore() {
        #expect(NowPlayingStyled.look(info("Nightcall", "Kavinsky"), on: false) == nil)
        #expect(NowPlayingStyled.look(nil, on: true) == nil)
        #expect(NowPlayingStyled.ink(nil, fallback: "ffffff") == "ffffff")
        #expect(NowPlayingStyled.ink(SongLook(colors: ["ff2a6d", "05d9e8"]), fallback: "ffffff") == "ff2a6d,05d9e8")
        #expect(NowPlayingStyled.plan("x", look: nil, family: "Helvetica") == nil)
    }
}

// MARK: - Themes with a title style (item 65d)

@Suite("Theme title style")
struct ThemeTitleStyleTests {
    private func lint(_ changes: [String: Any]) -> [ThemeFinding] {
        var t = cleanTheme()
        for (k, v) in changes { t[k] = v }
        return ThemeLint.lint(t)
    }

    @Test func goodFieldsLintClean() {
        #expect(lint(["title_ink": ["#ffd6e7", "#f3e3ff", "#d6f6ff"], "title_style": "small-caps"]).isEmpty)
        #expect(lint(["title_ink": NSNull(), "title_style": NSNull()]).isEmpty)
        #expect(lint(["title_style": "SmallCaps"]).isEmpty)
    }

    @Test func badFieldsAreErrors() {
        let cases: [(String, Any)] = [
            ("title_ink", ["#ff2a6d"]), ("title_ink", Array(repeating: "#ff2a6d", count: 5)), ("title_ink", "#ff2a6d"),
            ("title_ink", ["#ff2a6d", "#fff"]), ("title_ink", ["#ff2a6d", 3]), ("title_style", "comic"), ("title_style", 1),
        ]
        for (field, value) in cases {
            let errors = lint([field: value]).filter { $0.level == .error }
            #expect(errors.map(\.field) == [field], "\(field) \(value)")
        }
        #expect(lint(["title_style": "comic"]).first?.message
                == "title_style must be one of bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps, or null.")
    }

    @Test func eachStopIsCheckedOnTheCard() {
        let warnings = lint(["title_ink": ["#ffffff", "#202020", "#e0e0ff", "#303030"]]).filter { $0.level == .warning }
        #expect(warnings.map(\.field) == ["title_ink", "title_ink"])
        #expect(warnings.first?.message.hasPrefix("title_ink stop 2 reads at ") == true)
        #expect(warnings.first?.message.contains("(needs 4.5:1)") == true)
        #expect(warnings.last?.message.hasPrefix("title_ink stop 4 reads at ") == true)
    }

    @Test func aThemeFileCarriesItsTitleStyle() throws {
        var t = cleanTheme()
        t["title_ink"] = ["#ffd6e7", "#d6f6ff"]
        t["title_style"] = "fraktur"
        let theme = try #require(UserThemes.decodeTheme(try JSONSerialization.data(withJSONObject: t), id: "x"))
        #expect(theme.palette.titleInk == [Color.hex("ffd6e7"), Color.hex("d6f6ff")])
        #expect(theme.palette.titleStyle == .fraktur)
        // A hand-written file with a value the widget can't draw still loads, drawn as before.
        t["title_ink"] = ["#ffd6e7"]
        t["title_style"] = "comic"
        let plain = try #require(UserThemes.decodeTheme(try JSONSerialization.data(withJSONObject: t), id: "x"))
        #expect(plain.palette.titleInk == nil)
        #expect(plain.palette.titleStyle == nil)
        #expect(ThemeRegistry.builtIn.allSatisfy { $0.palette.titleInk == nil && $0.palette.titleStyle == nil })
    }

    @Test func theTitleCharacters() {
        #expect(ThemeTitle.characters(nil) == "Sanduhr")
        #expect(ThemeTitle.characters(.smallCaps) == "Sanduhr")
        #expect(ThemeTitle.characters(.fraktur) == LetterMap.map("Sanduhr", .fraktur))
        #expect(ThemeTitle.characters(.fraktur) != "Sanduhr")
        #expect(ThemeTitle.weight(.boldItalic) == .bold)
        #expect(ThemeTitle.italic(.boldItalic))
        #expect(ThemeTitle.italic(LetterStyle.bold) == false)
    }
}
