import CoreGraphics
import Foundation
import ImageIO
import Testing
@testable import Sanduhr

/// Ported case for case from PixelSheetTests.cs, plus the PNG encode checked by decoding it back.
@Suite("Pixel sheet")
struct PixelSheetTests {
    private static func sheet(_ name: String, _ rel: String) -> Data { WidgetPackFixtures.load("good", name)[rel]! }

    private struct Px: Equatable {
        let r, g, b, a: UInt8
        init(_ r: UInt8, _ g: UInt8, _ b: UInt8, _ a: UInt8) { self.r = r; self.g = g; self.b = b; self.a = a }
    }

    private static func px(_ img: PixelSheetImage, _ x: Int, _ y: Int) -> Px {
        let at = (y * img.width + x) * 4
        return Px(img.rgba[at], img.rgba[at + 1], img.rgba[at + 2], img.rgba[at + 3])
    }

    private static func rasterized(_ data: Data) throws -> PixelSheetImage {
        try PixelSheet.rasterize(data).get()
    }

    @Test func theFixtureSheetRasterizesIntoAOneRowGrid() throws {
        let img = try Self.rasterized(Self.sheet("good-sprite-pixel", "sheet.json"))
        #expect(img.frameWidth == 8 && img.frameHeight == 8 && img.frameCount == 4)
        #expect(img.columns == 4 && img.rows == 1)
        #expect(img.width == 32 && img.height == 8)
        #expect(img.rgba.count == 32 * 8 * 4)

        // Frame 0, row 0 is "..0000..": transparent, then palette[0] #22d3ee fully opaque.
        #expect(Self.px(img, 0, 0) == Px(0, 0, 0, 0))
        #expect(Self.px(img, 2, 0) == Px(0x22, 0xd3, 0xee, 255))
        // Frame 0, row 2 is "01122110": palette[2] #ec4899 at x 3.
        #expect(Self.px(img, 3, 2) == Px(0xec, 0x48, 0x99, 255))
        // Frame 1 starts at x 8; its row 0 is ".011110.": palette[1] #a855f7 at x 2.
        #expect(Self.px(img, 8, 0) == Px(0, 0, 0, 0))
        #expect(Self.px(img, 9, 0) == Px(0x22, 0xd3, 0xee, 255))
        #expect(Self.px(img, 10, 0) == Px(0xa8, 0x55, 0xf7, 255))
    }

    @Test func everyPixelIsFullyOpaqueOrFullyTransparent() throws {
        let img = try Self.rasterized(Self.sheet("good-sprite-pixel", "sheet.json"))
        for i in stride(from: 3, to: img.rgba.count, by: 4) { #expect(img.rgba[i] == 0 || img.rgba[i] == 255) }
    }

    @Test func frameCopiesOneCell() throws {
        let img = try Self.rasterized(Self.sheet("good-sprite-pixel-stages", "sprites/one.json"))
        let f1 = try #require(img.frame(1))
        #expect(f1.count == 8 * 8 * 4)
        for y in 0..<8 {
            for x in 0..<8 {
                let at = (y * 8 + x) * 4
                #expect(Self.px(img, 8 + x, y) == Px(f1[at], f1[at + 1], f1[at + 2], f1[at + 3]))
            }
        }
        #expect(img.frame(4) == nil)
        #expect(img.frame(-1) == nil)
    }

    @Test func manyFramesWrapAtSixteenColumnsRowMajor() throws {
        var frames: [String] = []
        for i in 0..<20 {
            let rows = (0..<8).map { r in i == 17 && r == 0 ? "\"1.......\"" : "\"........\"" }
            frames.append("[" + rows.joined(separator: ",") + "]")
        }
        let json = "{\"palette\": [\"#101010\", \"#203040\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [" + frames.joined(separator: ",") + "]}"
        let img = try Self.rasterized(Data(json.utf8))
        #expect(img.columns == 16 && img.rows == 2)
        #expect(img.width == 128 && img.height == 16)
        // Frame 17 is cell (1, 1): x 8, y 8.
        #expect(Self.px(img, 8, 8) == Px(0x20, 0x30, 0x40, 255))
        // Cells past the last frame are transparent.
        #expect(Self.px(img, 127, 15) == Px(0, 0, 0, 0))
    }

    @Test func theLayoutIsPure() {
        #expect(PixelSheet.layout(frameCount: 1) == (1, 1))
        #expect(PixelSheet.layout(frameCount: 16) == (16, 1))
        #expect(PixelSheet.layout(frameCount: 17) == (16, 2))
        #expect(PixelSheet.layout(frameCount: 256) == (16, 16))
        #expect(PixelSheet.layout(frameCount: 0) == (1, 0))
    }

    @Test(arguments: [
        ("{\"palette\": [\"#ffffff\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"0.......\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\"]]}", "pure white"),
        ("{\"palette\": [\"#101010\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"1.......\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\"]]}", "palette index '1'"),
        ("{\"palette\": [\"#101010\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"........\"]]}", "has 1 rows, expected 8"),
        ("{\"palette\": [\"#101010\"], \"frame_w\": 40, \"frame_h\": 40, \"frames\": []}", "integers from 8 to 32"),
        ("[1, 2]", "must be a JSON object"),
        ("{\"palette\": [", "invalid JSON"),
        ("{\"palette\": [\"#101010\"], \"frame_w\": 1e300, \"frame_h\": 8, \"frames\": []}", "integers from 8 to 32"),
    ])
    func aBadSheetIsRefusedWithTheReason(json: String, needle: String) {
        switch PixelSheet.rasterize(Data(json.utf8)) {
        case .success: Issue.record("accepted")
        case .failure(let e): #expect(e.message.contains(needle), "\(e.message)")
        }
    }

    // MARK: - PNG

    /// Decode PNG bytes to straight 8-bit RGBA by drawing into an sRGB context at 1:1, no interpolation.
    private static func decode(_ png: Data) throws -> (w: Int, h: Int, rgba: [UInt8]) {
        let src = try #require(CGImageSourceCreateWithData(png as CFData, nil))
        let image = try #require(CGImageSourceCreateImageAtIndex(src, 0, nil))
        let w = image.width, h = image.height
        var out = [UInt8](repeating: 0, count: w * h * 4)
        let space = try #require(CGColorSpace(name: CGColorSpace.sRGB))
        try out.withUnsafeMutableBytes { buf in
            let ctx = try #require(CGContext(
                data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            ctx.interpolationQuality = .none
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        }
        return (w, h, out)
    }

    @Test func thePngDecodesBackToTheSamePixels() throws {
        for (pack, rel) in [("good-sprite-pixel", "sheet.json"), ("good-sprite-pixel-stages", "sprites/one.json")] {
            let img = try Self.rasterized(Self.sheet(pack, rel))
            let png = try #require(img.pngData())
            #expect(Array(png.prefix(8)) == [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
            let back = try Self.decode(png)
            #expect(back.w == img.width && back.h == img.height)
            // Opaque and transparent pixels only, so premultiplied equals straight; transparent ones carry no color.
            #expect(back.rgba == img.rgba, "\(pack)")
        }
    }

    @Test func aPaletteColorSurvivesThePngExactly() throws {
        let json = "{\"palette\": [\"#010203\", \"#fdfeff\", \"#7f8081\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"012.....\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\"]]}"
        let img = try Self.rasterized(Data(json.utf8))
        let back = try Self.decode(try #require(img.pngData()))
        #expect(Array(back.rgba[0..<4]) == [0x01, 0x02, 0x03, 255])
        #expect(Array(back.rgba[4..<8]) == [0xfd, 0xfe, 0xff, 255])
        #expect(Array(back.rgba[8..<12]) == [0x7f, 0x80, 0x81, 255])
        #expect(Array(back.rgba[12..<16]) == [0, 0, 0, 0])
    }
}
