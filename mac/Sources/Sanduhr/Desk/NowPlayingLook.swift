import Foundation

/// A per-song look for now playing (item 65c), the pure half: a gradient and a letter style for
/// the track on the notch and the Desk line. Until Claude suggests one for a song (and the user
/// approves it), the look is seeded from a hash of the song: one of the owner's named palettes
/// (MessagePalette) and a letter style from a small set the Desk's hand font draws itself.
///
/// The seeded and resolved looks are cached in memory only (NowPlayingLookStore), keyed by app,
/// title and artist, as titles never reach the disk. Approved looks are the user's own data,
/// kept in now-playing-looks.json by artist and title until they clear them.
struct SongLook: Equatable, Hashable {
    /// Hex colors without "#", lowercase, 2 to 4.
    var colors: [String]
    var font: LetterStyle?
    /// Up to three words from Claude ("neon night drive"), shown in Settings only.
    var mood: String?

    /// The ink spec LinearGradient.ink reads: "ff2a6d,05d9e8".
    var ink: String { colors.joined(separator: ",") }
}

enum NowPlayingLooks {
    /// Settings, Desk, Now Playing: "Style what's playing", off by default.
    static let styleKey = "nowPlayingStyle"
    /// "Let Claude style songs directly", off by default.
    static let directKey = "nowPlayingLooksClaudeDirect"

    /// The letter styles a seeded look picks from: the ones the Desk's hand font draws itself.
    static let seedStyles: [LetterStyle] = [.smallCaps, .italic, .bold, .boldItalic]

    /// The in-memory cache's key: app, title and artist.
    static func cacheKey(_ info: NowPlayingInfo) -> String {
        [info.bundleID ?? "", info.title ?? "", info.artist ?? ""].joined(separator: "\u{1F}")
    }

    /// An approved look's key: artist and title, trimmed, spaces folded, case ignored, so a look
    /// Claude wrote as "Daft Punk" finds "daft punk " playing in any app.
    static func songKey(artist: String?, title: String?) -> String {
        func fold(_ s: String?) -> String {
            (s ?? "").lowercased().split(whereSeparator: \.isWhitespace).joined(separator: " ")
        }
        return fold(artist) + "\u{1F}" + fold(title)
    }

    /// FNV-1a, 64 bits: the same song seeds the same look on every launch (Swift's Hasher is
    /// seeded per process).
    static func hash(_ s: String) -> UInt64 {
        var h: UInt64 = 0xcbf2_9ce4_8422_2325
        for b in s.utf8 {
            h ^= UInt64(b)
            h = h &* 0x0000_0100_0000_01b3
        }
        return h
    }

    /// The default look for a song: a named palette and a seed style, both from its hash.
    static func seeded(_ key: String) -> SongLook {
        let h = hash(key)
        let palettes = MessagePalette.all
        let palette = palettes[Int(h % UInt64(palettes.count))]
        let style = seedStyles[Int((h / UInt64(palettes.count)) % UInt64(seedStyles.count))]
        return SongLook(colors: palette.colors, font: style)
    }

    /// The look for what plays: an approved look for its artist and title, else the seeded one.
    static func resolve(_ info: NowPlayingInfo, approved: [String: SongLook]) -> SongLook {
        approved[songKey(artist: info.artist, title: info.title)] ?? seeded(cacheKey(info))
    }
}

/// One look Claude suggested: the song it is for and the look.
struct ProposedSongLook: Equatable, Hashable {
    let artist: String
    let title: String
    let look: SongLook

    var key: String { NowPlayingLooks.songKey(artist: artist, title: title) }
}

/// Claude's suggested looks (item 65c): the request the MCP server's `propose_now_playing_looks`
/// writes, the checks the app runs again (it is the authority), the user's looks file and the
/// result the server waits for. NowPlayingLookStore does the files.
///
/// The server mirrors the file names, the limits and the reasons' wording
/// (`mac/integrations/sanduhr_mcp.py`); a test on each side pins them.
struct NowPlayingLookProposal: Equatable, Identifiable {
    let id: String
    let requestedAt: Date
    let looks: [ProposedSongLook]

    static let requestFile = "now-playing-looks-request.json"
    static let resultFile = "now-playing-looks-result.json"
    /// The user's approved looks, by artist and title.
    static let looksFile = "now-playing-looks.json"

    static let maxLooks = 50
    static let maxArtistChars = 100
    static let maxTitleChars = 200
    static let maxMoodWords = 3
    static let maxMoodChars = 40
    /// The looks file keeps at most this many songs; the oldest go first.
    static let maxStored = 500
    /// Each color reads at least this well on the black notch.
    static let minContrastOnBlack = 3.0
    static let maxAge: TimeInterval = 600

    static let fields: Set<String> = ["artist", "title", "colors", "font", "mood"]

    // MARK: - Reading the request

    enum Decoded: Equatable {
        case proposal(NowPlayingLookProposal)
        case refused(id: String, reasons: [String])
        case discard
    }

    static func decode(_ data: Data, now: Date) -> Decoded {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let id = root["id"] as? String, !id.isEmpty, id.count <= 64 else { return .discard }
        guard let stamp = root["requested_at"] as? String, let at = MessageProposal.parseDate(stamp) else {
            return .refused(id: id, reasons: ["requested_at is missing or not a date"])
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard (ThemeLint.number(root["schema_version"]) ?? 1) == 1 else {
            return .refused(id: id, reasons: ["this Sanduhr reads requests of schema 1"])
        }
        let checked = validate(root["looks"])
        guard checked.reasons.isEmpty else { return .refused(id: id, reasons: checked.reasons) }
        return .proposal(NowPlayingLookProposal(id: id, requestedAt: at, looks: checked.looks))
    }

    /// The looks, and why they are refused ([] when they may be saved). Same rules and wording as
    /// the server.
    static func validate(_ raw: Any?) -> (looks: [ProposedSongLook], reasons: [String]) {
        guard let items = raw as? [Any], !items.isEmpty, items.count <= maxLooks else {
            return ([], ["looks must be a list of 1 to \(maxLooks) looks"])
        }
        var reasons: [String] = []
        var out: [ProposedSongLook] = []
        for (i, item) in items.enumerated() {
            let one = check(item, number: i + 1)
            if let look = one.look { out.append(look) }
            reasons += one.reasons
        }
        return (out, Array(reasons.prefix(20)))
    }

    /// One look, or why not (each reason starts "look N").
    static func check(_ item: Any, number n: Int) -> (look: ProposedSongLook?, reasons: [String]) {
        guard let o = item as? [String: Any] else {
            return (nil, ["look \(n) must be an object with artist, title and colors"])
        }
        var reasons: [String] = []
        if let extra = o.keys.filter({ !fields.contains($0) }).sorted().first {
            reasons.append("look \(n) has an unknown field: \(extra.prefix(20))")
        }
        let artist = line(o["artist"], max: maxArtistChars)
        if artist == nil { reasons.append("look \(n) artist must be one line of 1 to \(maxArtistChars) characters") }
        let title = line(o["title"], max: maxTitleChars)
        if title == nil { reasons.append("look \(n) title must be one line of 1 to \(maxTitleChars) characters") }
        let list = o["colors"] as? [Any] ?? []
        let hexes = list.compactMap { ($0 as? String).flatMap(hex) }
        var colors: [String] = []
        if (2...4).contains(list.count), hexes.count == list.count {
            colors = hexes
            for hex in hexes {
                let ratio = contrastOnBlack(hex)
                if ratio < minContrastOnBlack {
                    reasons.append("look \(n) color #\(hex) is too dark for the black notch (\(ThemeLint.fmt(ratio, 1)):1, needs \(ThemeLint.fmt(minContrastOnBlack, 1)):1)")
                }
            }
        } else {
            reasons.append("look \(n) colors must be 2 to 4 hex colors, like [\"#ff2a6d\", \"#05d9e8\"]")
        }
        var font: LetterStyle?
        if let f = ThemeLint.present(o["font"]) {
            font = (f as? String).flatMap(LetterStyle.init(tag:))
            if font == nil { reasons.append("look \(n) font must be one of: \(LetterStyle.tagNames)") }
        }
        var mood: String?
        if let m = ThemeLint.present(o["mood"]) {
            let raw = m as? String
            let s = raw?.trimmingCharacters(in: .whitespaces)
            let words = s?.split(whereSeparator: \.isWhitespace).count ?? 0
            if let raw, let s, s.unicodeScalars.count <= maxMoodChars, words <= maxMoodWords,
               !raw.unicodeScalars.contains(where: MessageProposal.isControl) {
                mood = s.isEmpty ? nil : s
            } else {
                reasons.append("look \(n) mood must be at most \(maxMoodWords) words and \(maxMoodChars) characters")
            }
        }
        guard reasons.isEmpty, let artist, let title else { return (nil, reasons) }
        return (ProposedSongLook(artist: artist, title: title, look: SongLook(colors: colors, font: font, mood: mood)), [])
    }

    /// A one-line string of 1 to `max` characters, trimmed; nil otherwise.
    private static func line(_ v: Any?, max: Int) -> String? {
        guard let raw = v as? String, !raw.unicodeScalars.contains(where: MessageProposal.isControl) else { return nil }
        let s = raw.trimmingCharacters(in: .whitespaces)
        return !s.isEmpty && s.unicodeScalars.count <= max ? s : nil
    }

    /// "#FF2A6D", "ff2a6d" or "#f2d" as "ff2a6d" (ASCII hex only, as the server's check); nil
    /// for anything else.
    static func hex(_ raw: String) -> String? {
        var h = raw.lowercased()
        if h.hasPrefix("#") { h.removeFirst() }
        guard h.count == 3 || h.count == 6,
              h.unicodeScalars.allSatisfy({ ("0"..."9").contains($0) || ("a"..."f").contains($0) }) else { return nil }
        return h.count == 3 ? h.map { "\($0)\($0)" }.joined() : h
    }


    /// A color's contrast against black (the notch), 1 to 21.
    static func contrastOnBlack(_ hex: String) -> Double {
        guard let rgb = ThemeLint.parseHex("#" + hex) else { return 1 }
        return ThemeLint.contrast(ThemeLint.luminance(rgb), 0)
    }

    // MARK: - The looks file

    /// The approved looks in order, oldest first, from now-playing-looks.json (nil data: none).
    /// A file that isn't one reads as no looks; entries that fail the checks are skipped.
    static func readLooks(_ data: Data?) -> [ProposedSongLook] {
        guard let data, let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              (ThemeLint.number(root["schema_version"]) ?? 1) == 1,
              let items = root["looks"] as? [Any] else { return [] }
        return items.enumerated().compactMap { i, item in
            check(item, number: i + 1).look
        }
    }

    /// The file's contents for `looks` (oldest first).
    static func looksJSON(_ looks: [ProposedSongLook]) -> Data {
        let items: [[String: Any]] = looks.map { l in
            var o: [String: Any] = ["artist": l.artist, "title": l.title, "colors": l.look.colors.map { "#" + $0 }]
            if let f = l.look.font { o["font"] = f.rawValue }
            if let m = l.look.mood { o["mood"] = m }
            return o
        }
        let root: [String: Any] = ["schema_version": 1, "looks": items]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .sortedKeys])) ?? Data()
    }

    /// `existing` with `added` saved: a song already there takes the new look and moves to the
    /// end; past `maxStored` the oldest go.
    static func merge(_ added: [ProposedSongLook], into existing: [ProposedSongLook]) -> [ProposedSongLook] {
        var out = existing
        for look in added {
            out.removeAll { $0.key == look.key }
            out.append(look)
        }
        return Array(out.suffix(maxStored))
    }

    /// The approved looks by song key.
    static func byKey(_ looks: [ProposedSongLook]) -> [String: SongLook] {
        var out: [String: SongLook] = [:]
        for l in looks { out[l.key] = l.look }
        return out
    }

    // MARK: - The result

    enum Status: String { case applied, pendingApproval = "pending_approval", rejected }

    /// `{id, completed_at, result: {status, reasons?, looks_saved?, style_on}}`. `style_on` says
    /// whether Style what's playing is on, so the agent can tell the user where to switch it on.
    static func resultJSON(id: String, status: Status, reasons: [String] = [], saved: Int? = nil,
                           styleOn: Bool, now: Date = Date()) -> Data {
        var result: [String: Any] = ["status": status.rawValue, "style_on": styleOn]
        if !reasons.isEmpty { result["reasons"] = reasons }
        if let saved { result["looks_saved"] = saved }
        let root: [String: Any] = ["id": id, "completed_at": HandoffFiles.stamp(now), "result": result]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }
}
