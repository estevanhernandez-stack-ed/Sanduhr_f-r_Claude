import Foundation
import Testing
@testable import Sanduhr

/// "Import an Aseprite sheet" (spec 2.43.0 section 23): the JSON and the PNG are built here; the file layer runs
/// against a temp folder.
@Suite("Import an Aseprite sheet (2.43.0)")
@MainActor
struct AsepriteSheetTests {
    /// A frame entry: `n` frames of `w`x`h` on a sheet `cols` wide.
    static func rect(_ i: Int, cols: Int, w: Int = 16, h: Int = 16) -> (x: Int, y: Int) { (i % cols * w, i / cols * h) }

    static func frameJson(_ i: Int, cols: Int, w: Int = 16, h: Int = 16, duration: Int? = 100, trimmed: Bool = false, rotated: Bool = false) -> String {
        let r = rect(i, cols: cols, w: w, h: h)
        let d = duration.map { ", \"duration\": \($0)" } ?? ""
        return "\"frame\": {\"x\": \(r.x), \"y\": \(r.y), \"w\": \(w), \"h\": \(h)}, \"rotated\": \(rotated), \"trimmed\": \(trimmed)\(d)"
    }

    static func tagsJson(_ tags: [(String, Int, Int, String?)]) -> String {
        tags.map { t in
            "{\"name\": \"\(t.0)\", \"from\": \(t.1), \"to\": \(t.2)" + (t.3.map { ", \"direction\": \"\($0)\"" } ?? "") + "}"
        }.joined(separator: ", ")
    }

    static func array(_ n: Int, cols: Int, durations: [Int?]? = nil, tags: [(String, Int, Int, String?)] = [],
                      image: String = "walk.png", w: Int = 16, h: Int = 16, trimmed: Bool = false, rotated: Bool = false) -> Data {
        let frames = (0..<n).map { "{\"filename\": \"f \($0).aseprite\", " + frameJson($0, cols: cols, w: w, h: h, duration: durations?[$0] ?? (durations == nil ? 100 : nil), trimmed: trimmed, rotated: rotated) + "}" }
        let json = "{\"frames\": [\(frames.joined(separator: ", "))], \"meta\": {\"app\": \"https://www.aseprite.org/\", \"image\": \"\(image)\", \"size\": {\"w\": \(cols * w), \"h\": \((n + cols - 1) / cols * h)}, \"frameTags\": [\(tagsJson(tags))]}}"
        return Data(json.utf8)
    }

    static func hash(_ n: Int, cols: Int, tags: [(String, Int, Int, String?)] = [], image: String = "walk.png") -> Data {
        let frames = (0..<n).map { "\"f \($0).aseprite\": {" + frameJson($0, cols: cols, duration: 100) + "}" }
        let json = "{\"frames\": {\(frames.joined(separator: ", "))}, \"meta\": {\"image\": \"\(image)\", \"frameTags\": [\(tagsJson(tags))]}}"
        return Data(json.utf8)
    }

    static func plan(_ data: Data, w: Int, h: Int) -> AsepriteSheet.Plan? {
        guard case .success(let p) = AsepriteSheet.parse(data), case .success(let plan) = AsepriteSheet.plan(p, sheetWidth: w, sheetHeight: h) else { return nil }
        return plan
    }

    static func refusal(_ data: Data, w: Int = 64, h: Int = 16) -> String? {
        switch AsepriteSheet.parse(data) {
        case .failure(let e): return e.message
        case .success(let p):
            if case .failure(let e) = AsepriteSheet.plan(p, sheetWidth: w, sheetHeight: h) { return e.message }
            return nil
        }
    }

    // MARK: Forms

    @Test func hashAndArrayFormsGiveTheSameResult() {
        let tags: [(String, Int, Int, String?)] = [("walk", 0, 3, nil), ("sit", 0, 0, nil)]
        let a = Self.plan(Self.array(4, cols: 4, tags: tags), w: 64, h: 16)
        let h = Self.plan(Self.hash(4, cols: 4, tags: tags), w: 64, h: 16)
        #expect(a != nil && a == h)
        #expect(a?.tags["walk"] == [0, 1, 2, 3])
    }

    @Test func aHashKeepsItsKeysInFileOrder() {
        // Frame entries listed out of grid order: the order of the file is the order of the frames.
        let json = """
        {"frames": {"z": {"frame": {"x": 32, "y": 0, "w": 16, "h": 16}, "duration": 100},
                    "a": {"frame": {"x": 0, "y": 0, "w": 16, "h": 16}, "duration": 100},
                    "m": {"frame": {"x": 16, "y": 0, "w": 16, "h": 16}, "duration": 100}},
         "meta": {"image": "s.png"}}
        """
        let plan = Self.plan(Data(json.utf8), w: 48, h: 16)
        #expect(plan?.tags["walk"] == [2, 0, 1])
        #expect(plan?.tags["sit"] == [2])
    }

    // MARK: Directions

    @Test func eachDirectionOnAFourFrameTag() {
        func walk(_ direction: String?) -> [Int]? {
            Self.plan(Self.array(4, cols: 4, tags: [("walk", 0, 3, direction)]), w: 64, h: 16)?.tags["walk"]
        }
        #expect(walk(nil) == [0, 1, 2, 3])
        #expect(walk("forward") == [0, 1, 2, 3])
        #expect(walk("reverse") == [3, 2, 1, 0])
        #expect(walk("pingpong") == [0, 1, 2, 3, 2, 1])
        #expect(walk("pingpong_reverse") == [3, 2, 1, 0, 1, 2])
    }

    @Test func aTagOnANonZeroFromInATwoRowSheetMapsRowMajor() {
        // 6 frames, 3 columns, 2 rows; the tag covers frames 2..5, which cross the row break.
        let plan = Self.plan(Self.array(6, cols: 3, tags: [("walk", 0, 1, nil), ("nap", 2, 5, nil)]), w: 48, h: 32)
        #expect(plan?.tags["walk"] == [0, 1])
        #expect(plan?.tags["nap"] == [2, 3, 4, 5])
        // A tag from frame 1, on a sheet 4 columns wide: frame 3 sits at x 0 on row 2, which is cell 4.
        let wide = Self.plan(Self.array(6, cols: 3, tags: [("walk", 1, 4, nil)]), w: 64, h: 32)
        #expect(wide?.tags["walk"] == [1, 2, 4, 5])
    }

    // MARK: fps

    @Test func durationsBecomeFpsByTheMedian() {
        #expect(Self.plan(Self.array(4, cols: 4), w: 64, h: 16)?.fps == 10)
        #expect(Self.plan(Self.array(4, cols: 4, durations: [50, 50, 50, 50]), w: 64, h: 16)?.fps == 20)
        // Mixed: median of 40, 100, 100, 1000 is 100 ms.
        #expect(Self.plan(Self.array(4, cols: 4, durations: [40, 1000, 100, 100]), w: 64, h: 16)?.fps == 10)
        // Missing durations count as 100 ms.
        #expect(Self.plan(Self.array(4, cols: 4, durations: [nil, nil, nil, nil]), w: 64, h: 16)?.fps == 10)
        // Out of range is held to 1...24.
        #expect(Self.plan(Self.array(4, cols: 4, durations: [5, 5, 5, 5]), w: 64, h: 16)?.fps == 24)
        #expect(Self.plan(Self.array(4, cols: 4, durations: [5000, 5000, 5000, 5000]), w: 64, h: 16)?.fps == 1)
    }

    // MARK: Tags

    @Test func anUntaggedExportWalksEveryFrameAndSitsOnTheFirst() {
        let plan = Self.plan(Self.array(4, cols: 4), w: 64, h: 16)
        #expect(plan?.tags["walk"] == [0, 1, 2, 3] && plan?.tags["sit"] == [0])
        #expect(plan?.ignored == [])
    }

    @Test func otherTagsAreListedAndActionNamesAreCaseInsensitive() {
        let plan = Self.plan(Self.array(4, cols: 4, tags: [("Walk", 0, 2, nil), ("idle", 3, 3, nil), ("CHEER", 3, 3, nil), ("boss", 0, 1, nil)]), w: 64, h: 16)
        #expect(plan?.tags["walk"] == [0, 1, 2] && plan?.tags["cheer"] == [3])
        #expect(plan?.ignored == ["idle", "boss"])
        // Action tags suppress the untagged default.
        #expect(plan?.tags["sit"] == nil)
        let model = NewSpriteModel(aseprite: .init(png: Data(), plan: plan!), install: { _ in [] })
        #expect(model.ignoredNote == "Not used: idle, boss.")
    }

    // MARK: Refusals

    @Test func notAsepriteJson() {
        let sentence = "That isn't an Aseprite sheet's JSON."
        #expect(Self.refusal(Data("{\"hello\": 1}".utf8)) == sentence)
        #expect(Self.refusal(Data("[1, 2]".utf8)) == sentence)
        #expect(Self.refusal(Data("not json at all".utf8)) == sentence)
        #expect(Self.refusal(Data()) == sentence)
        #expect(Self.refusal(Data("{\"frames\": [], \"meta\": {\"image\": \"a.png\"}}".utf8)) == sentence)
        #expect(Self.refusal(Data("{\"frames\": [{\"frame\": {\"x\": 0}}], \"meta\": {\"image\": \"a.png\"}}".utf8)) == sentence)
    }

    @Test func aNonGridExportIsRefused() {
        let grid = "The frames aren't one grid of same-size cells. Export with Trim and Rotate off."
        #expect(Self.refusal(Self.array(4, cols: 4, trimmed: true)) == grid)
        #expect(Self.refusal(Self.array(4, cols: 4, rotated: true)) == grid)
        let mixed = """
        {"frames": [{"frame": {"x": 0, "y": 0, "w": 16, "h": 16}}, {"frame": {"x": 16, "y": 0, "w": 16, "h": 24}}],
         "meta": {"image": "s.png"}}
        """
        #expect(Self.refusal(Data(mixed.utf8)) == grid)
        let offGrid = """
        {"frames": [{"frame": {"x": 0, "y": 0, "w": 16, "h": 16}}, {"frame": {"x": 18, "y": 0, "w": 16, "h": 16}}],
         "meta": {"image": "s.png"}}
        """
        #expect(Self.refusal(Data(offGrid.utf8)) == grid)
        // A sheet that the cells don't tile.
        #expect(Self.refusal(Self.array(4, cols: 4), w: 60, h: 16) == grid)
    }

    @Test func frameSizesOutsideTheCellRangeAreRefused() {
        #expect(Self.refusal(Self.array(2, cols: 2, w: 4, h: 4), w: 8, h: 4)
                == "That sheet's frames are 4x4. A sprite's cells are 8 to 64 pixels on a side.")
    }

    @Test func aSheetNameWithAFolderIsRefused() {
        let sentence = "The sheet's name has a folder in it. Sanduhr reads only a sheet beside the JSON."
        for bad in ["sub/walk.png", "..", "../walk.png", "a\\\\b.png", "/etc/x.png", ""] {
            #expect(Self.refusal(Self.array(4, cols: 4, image: bad)) == sentence, "\(bad)")
        }
    }

    // MARK: Files

    private func folder() throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("aseprite-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    @Test func theFileLayerReadsOnlyTheSheetBesideTheJson() throws {
        let dir = try folder(); defer { try? FileManager.default.removeItem(at: dir) }
        let json = dir.appendingPathComponent("walk.json")
        try Self.array(4, cols: 4, tags: [("walk", 0, 3, "pingpong")]).write(to: json)

        // Missing sheet.
        if case .failure(let e) = AsepriteImport.load(jsonURL: json) { #expect(e.message == "The sheet walk.png isn't next to the JSON.") }
        else { Issue.record("expected a refusal") }

        // The sheet, beside it.
        try WidgetOwnArtTests.sheet(64, 16).write(to: dir.appendingPathComponent("walk.png"))
        guard case .success(let loaded) = AsepriteImport.load(jsonURL: json) else { Issue.record("expected a sheet"); return }
        #expect(loaded.plan.tags["walk"] == [0, 1, 2, 3, 2, 1] && loaded.plan.fps == 10)
        #expect(loaded.plan.cellWidth == 16 && loaded.png.count > 0)
    }

    @Test func aSheetThatIsALinkIsRefused() throws {
        let dir = try folder(); defer { try? FileManager.default.removeItem(at: dir) }
        let real = dir.appendingPathComponent("real.png")
        try WidgetOwnArtTests.sheet(64, 16).write(to: real)
        try FileManager.default.createSymbolicLink(at: dir.appendingPathComponent("walk.png"), withDestinationURL: real)
        let json = dir.appendingPathComponent("walk.json")
        try Self.array(4, cols: 4).write(to: json)
        if case .failure(let e) = AsepriteImport.load(jsonURL: json) { #expect(e.message.hasPrefix("The sheet walk.png is a link.")) }
        else { Issue.record("expected a refusal") }
    }

    @Test func aSheetThatIsAFolderOrNotAPngIsRefused() throws {
        let dir = try folder(); defer { try? FileManager.default.removeItem(at: dir) }
        let json = dir.appendingPathComponent("walk.json")
        try Self.array(4, cols: 4).write(to: json)
        try FileManager.default.createDirectory(at: dir.appendingPathComponent("walk.png"), withIntermediateDirectories: false)
        if case .failure(let e) = AsepriteImport.load(jsonURL: json) { #expect(e.message == "The sheet walk.png isn't next to the JSON.") }
        else { Issue.record("expected a refusal") }
        try FileManager.default.removeItem(at: dir.appendingPathComponent("walk.png"))
        try Data("not a png".utf8).write(to: dir.appendingPathComponent("walk.png"))
        if case .failure(let e) = AsepriteImport.load(jsonURL: json) { #expect(e.message.contains("isn't a PNG")) }
        else { Issue.record("expected a refusal") }
    }

    // MARK: End to end

    @Test func aPackMadeFromAnAsepriteSheetValidatesStrictWithItsFps() throws {
        let dir = try folder(); defer { try? FileManager.default.removeItem(at: dir) }
        let json = dir.appendingPathComponent("blink.json")
        try Self.array(4, cols: 4, durations: [50, 50, 50, 50], tags: [("walk", 0, 3, nil), ("sit", 0, 0, nil), ("extra", 1, 2, nil)]).write(to: json)
        try WidgetOwnArtTests.sheet(64, 16).write(to: dir.appendingPathComponent("walk.png"))
        guard case .success(let loaded) = AsepriteImport.load(jsonURL: json) else { Issue.record("expected a sheet"); return }
        let model = NewSpriteModel(aseprite: loaded, install: { _ in [] })
        #expect(model.cellWidth == 16 && model.cellHeight == 16 && model.cellCount == 4)
        #expect(model.cells(of: "walk") == [0, 1, 2, 3] && model.cells(of: "sit") == [0])
        #expect(model.ignoredNote == "Not used: extra.")
        model.name = "Blinker"
        let pack = WidgetNewSprite.make(model.input, installedIds: [], now: Date(timeIntervalSince1970: 0))
        #expect(pack.ok, "\(pack.errors)")
        let verdict = PackValidator.validate(pack.files ?? [:], folderName: pack.manifest?.id ?? "", strict: true)
        #expect(verdict.ok)
        let text = String(decoding: pack.files?["sprite.json"] ?? Data(), as: UTF8.self)
        #expect(text.contains("\"fps\": 20"), "\(text)")
    }
}
