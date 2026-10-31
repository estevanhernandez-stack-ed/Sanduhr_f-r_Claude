import Foundation

/// "New sprite from an image..." (spec 2.39.0 section 18): a user's own PNG sheet, a cell size and cells tagged to
/// actions, made into a sprite pack. Pure: bytes and choices in, files and sentences out; the caller installs the
/// result as a local widget by the same path as own art. Claude never supplies or sees this image.
///
/// The rules:
/// - The PNG meets the pack image rules (`WidgetOwnArt.imageSize`). Cells are 8 to 64 px on a side (width and
///   height each), the sheet a whole number of them, numbered row-major (left to right, then downward).
/// - `walk` needs two or more cells in order and `sit` needs at least one: a sprite that cannot walk or sit has no
///   rest pose to draw. `nap`, `cheer` and `worry` may be left untagged and then use `sit`'s cells.
/// - The hourglass's table is the model: the five tags fill the nine animations the format requires. `pop` and
///   `celebrate` play `cheer`, `focus` plays `sit`, `sleep` plays `nap`; the moods and triggers point at those the
///   way the hourglass points them (content sit, focused focus, worried worry, exhausted sleep, celebrate
///   celebrate; needs_you pop, job_passed cheer, near_limit worry, idle nap).
/// - The name becomes the id `my-<slug>`; an id already installed takes `-2`, `-3`.
enum WidgetNewSprite {
    /// The actions a cell can be tagged to.
    static let actions = ["walk", "sit", "nap", "cheer", "worry"]
    static let cellMin = 8
    static let cellMax = 64
    static let scaleRange = 1...4
    static let defaultScale = 2
    static let fps = 8
    static let stageId = "sprite"
    /// The longest id the format allows, `my-` included.
    private static let maxIdLength = 40

    struct Input {
        var png: Data
        var cellWidth: Int
        var cellHeight: Int
        var name: String
        /// Nil is the default scale (2).
        var scale: Int?
        /// Action to the cells (row-major indexes) it plays, in order. An action missing or empty is untagged.
        var tags: [String: [Int]]
    }

    /// `my-<slug>`, with `-2`, `-3` until it is not in `installedIds`. Nil when the name has no letter or digit.
    static func packId(name: String, installedIds: [String]) -> String? {
        let slug = slugged(name)
        if slug.isEmpty { return nil }
        let taken = Set(installedIds)
        var n = 1
        while true {
            let suffix = n == 1 ? "" : "-\(n)"
            let room = maxIdLength - WidgetProposal.idPrefix.count - suffix.count
            let id = WidgetProposal.idPrefix + String(slug.prefix(room)).trimmingCharacters(in: CharacterSet(charactersIn: "-")) + suffix
            if !taken.contains(id) { return id }
            n += 1
        }
    }

    /// Lowercase ASCII letters and digits, any run of anything else one dash, none at the ends.
    static func slugged(_ name: String) -> String {
        var out = ""
        var pendingDash = false
        for u in name.lowercased().unicodeScalars {
            let alnum = (u >= "a" && u <= "z") || (u >= "0" && u <= "9")
            if alnum {
                if pendingDash && !out.isEmpty { out.append("-") }
                pendingDash = false
                out.unicodeScalars.append(u)
            } else {
                pendingDash = true
            }
        }
        return out
    }

    /// The `animations` table the tags make (the nine the format requires), or the sentence why not.
    static func animations(_ tags: [String: [Int]], cells: Int) -> (table: [(String, [Int])]?, error: String?) {
        for key in tags.keys.sorted() where !actions.contains(key) {
            return (nil, "\(key) isn't an action. The actions are \(actions.joined(separator: ", ")).")
        }
        for action in actions {
            for i in tags[action] ?? [] where i < 0 || i >= cells {
                return (nil, "The \(action) cells use cell \(i + 1), but the sheet has \(cells) cell\(cells == 1 ? "" : "s").")
            }
            if (tags[action] ?? []).count > 64 { return (nil, "An action plays at most 64 cells.") }
        }
        let walk = tags["walk"] ?? [], sit = tags["sit"] ?? []
        if sit.isEmpty { return (nil, "Tag at least one cell as sit. It is the pose the sprite rests in.") }
        if walk.count < 2 { return (nil, "Walk needs two or more cells, in order. It has \(walk.count).") }
        func playing(_ action: String) -> [Int] { (tags[action] ?? []).isEmpty ? sit : tags[action]! }
        let nap = playing("nap"), cheer = playing("cheer"), worry = playing("worry")
        return ([("walk", walk), ("sit", sit), ("nap", nap), ("pop", cheer), ("cheer", cheer),
                 ("worry", worry), ("focus", sit), ("sleep", nap), ("celebrate", cheer)], nil)
    }

    /// The pack made from `input`, validated whole (strict), or the reasons in sentences.
    static func make(_ input: Input, installedIds: [String], now: Date) -> OwnArtResult {
        let size = WidgetOwnArt.imageSize(input.png)
        if let why = size.error { return .failed(why) }
        let cw = input.cellWidth, ch = input.cellHeight
        if !(cellMin...cellMax).contains(cw) || !(cellMin...cellMax).contains(ch) {
            return .failed("A cell is \(cellMin) to \(cellMax) pixels on a side. \(cw)x\(ch) is outside that.")
        }
        if size.width % cw != 0 || size.height % ch != 0 {
            return .failed("That image is \(size.width)x\(size.height), which isn't a whole number of \(cw)x\(ch) cells.")
        }
        let cells = size.width / cw * (size.height / ch)
        let name = input.name.trimmingCharacters(in: .whitespacesAndNewlines)
        if name.isEmpty { return .failed("Give the sprite a name.") }
        if name.count > 40 { return .failed("A name is at most 40 characters.") }
        guard let id = packId(name: name, installedIds: installedIds) else {
            return .failed("The name needs a letter or a digit in it.")
        }
        let scale = input.scale ?? defaultScale
        if !scaleRange.contains(scale) { return .failed("Scale is \(scaleRange.lowerBound) to \(scaleRange.upperBound).") }
        let made = animations(input.tags, cells: cells)
        guard let table = made.table else { return .failed(made.error ?? "The tags aren't usable.") }

        let animations = PackValue.object(WidgetJson.object(table.map { ($0, PackValue.array($1.map(WidgetJson.int))) }))
        let moods = PackValue.object(WidgetJson.object([
            ("content", .string("sit")), ("focused", .string("focus")), ("worried", .string("worry")),
            ("exhausted", .string("sleep")), ("celebrate", .string("celebrate")),
        ]))
        let triggers = PackValue.object(WidgetJson.object([
            ("needs_you", .string("pop")), ("job_passed", .string("cheer")),
            ("near_limit", .string("worry")), ("idle", .string("nap")),
        ]))
        let stage = PackValue.object(WidgetJson.object([
            ("id", .string(stageId)), ("name", .string("Sprite")), ("min_growth", WidgetJson.int(0)),
            ("sheet", .string(WidgetOwnArt.artPath(stageId))), ("frame_w", WidgetJson.int(cw)), ("frame_h", WidgetJson.int(ch)),
        ]))
        let sprite = WidgetJson.object([
            ("schema_version", WidgetJson.int(1)), ("frame_w", WidgetJson.int(cw)), ("frame_h", WidgetJson.int(ch)),
            ("scale", WidgetJson.int(scale)), ("fps", WidgetJson.int(fps)), ("stages", .array([stage])),
            ("animations", animations), ("moods", moods), ("triggers", triggers),
            ("roam", .object(WidgetJson.object([
                ("edges", .array([.string("bottom"), .string("left"), .string("right")])), ("speed_px_s", WidgetJson.int(30)),
            ]))),
        ])
        let manifest = WidgetJson.object([
            ("schema_version", WidgetJson.int(1)), ("id", .string(id)), ("name", .string(name)),
            ("version", .string(WidgetProposal.defaultVersion)), ("author", .string(WidgetProposal.defaultAuthor)),
            ("license", .string(WidgetProposal.defaultLicense)), ("kind", .string("sprite")),
            ("sanduhr_min", .string(WidgetProposal.sanduhrMin)),
            ("description", .string("A sprite made here from my own art.")),
            ("files", .array([.string(WidgetOwnArt.artPath(stageId)), .string("sprite.json")])),
        ])

        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(identifier: "UTC")!
        let files: [String: Data] = [
            "pack.json": Data((WidgetJson.indented(.object(manifest)) + "\n").utf8),
            "sprite.json": Data((WidgetJson.indented(.object(sprite)) + "\n").utf8),
            "LICENSE": Data(WidgetProposal.licenseText(WidgetProposal.defaultLicense, WidgetProposal.defaultAuthor,
                                                       utc.component(.year, from: now)).utf8),
            WidgetOwnArt.artPath(stageId): input.png,
        ]
        let verdict = PackValidator.validate(files, folderName: id, strict: true)
        if verdict.ok, let m = verdict.manifest { return OwnArtResult(files: files, manifest: m, errors: []) }
        return OwnArtResult(files: nil, manifest: nil, errors: verdict.errors)
    }
}
