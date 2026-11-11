import Foundation
import ImageIO
import CoreGraphics
import Testing
@testable import Sanduhr

/// "Import an animated GIF" (spec 2.43.0 section 22): GIFs written here with CGImageDestination, frames of distinct
/// solid colours and delays, read back through `SpriteFrames` and the sprite maker.
@Suite("Import an animated GIF (2.43.0)")
@MainActor
struct SpriteFramesTests {
    typealias RGBA = (r: UInt8, g: UInt8, b: UInt8, a: UInt8)

    static let palette: [RGBA] = [
        (255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 255), (255, 255, 0, 255),
        (255, 0, 255, 255), (0, 255, 255, 255), (255, 128, 0, 255), (128, 0, 255, 255),
    ]

    /// A `w` x `h` image of one colour (premultiplied, so a transparent one is all zero).
    static func solid(_ w: Int, _ h: Int, _ c: RGBA) -> CGImage {
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        let a = Double(c.a) / 255
        ctx.setFillColor(red: Double(c.r) / 255 * a, green: Double(c.g) / 255 * a, blue: Double(c.b) / 255 * a, alpha: a)
        ctx.setBlendMode(.copy)
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()!
    }

    /// A GIF of `frames` with a delay (seconds) per frame; a nil delay sets the 0 the format allows.
    static func gif(_ frames: [(CGImage, Double)]) -> Data {
        let out = NSMutableData()
        let dest = CGImageDestinationCreateWithData(out, "com.compuserve.gif" as CFString, frames.count, nil)!
        CGImageDestinationSetProperties(dest, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)
        for (image, delay) in frames {
            let props = [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: delay,
                                                         kCGImagePropertyGIFUnclampedDelayTime: delay]]
            CGImageDestinationAddImage(dest, image, props as CFDictionary)
        }
        precondition(CGImageDestinationFinalize(dest))
        return out as Data
    }

    /// `n` frames of `w` x `h`, each a different colour, all at `delay`.
    static func colourGif(_ n: Int, _ w: Int = 16, _ h: Int = 16, delay: Double = 0.1) -> Data {
        gif((0..<n).map { (solid(w, h, palette[$0 % palette.count]), delay) })
    }

    /// The RGBA of the PNG, rows top to bottom.
    static func decode(_ png: Data) -> (w: Int, h: Int, px: [UInt8]) {
        let image = CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithData(png as CFData, nil)!, 0, nil)!
        let w = image.width, h = image.height
        var px = [UInt8](repeating: 0, count: w * h * 4)
        px.withUnsafeMutableBytes { buf in
            let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        }
        return (w, h, px)
    }

    static func pixel(_ d: (w: Int, h: Int, px: [UInt8]), _ x: Int, _ y: Int) -> RGBA {
        let i = (y * d.w + x) * 4
        return (d.px[i], d.px[i + 1], d.px[i + 2], d.px[i + 3])
    }

    static func near(_ a: RGBA, _ b: RGBA, _ tol: Int = 24) -> Bool {
        abs(Int(a.r) - Int(b.r)) <= tol && abs(Int(a.g) - Int(b.g)) <= tol
            && abs(Int(a.b) - Int(b.b)) <= tol && abs(Int(a.a) - Int(b.a)) <= tol
    }

    private func made(_ data: Data) throws -> SpriteFrames.Result {
        switch SpriteFrames.read(data) {
        case .success(let r): return r
        case .failure(let e): throw e
        }
    }

    private func refusal(_ data: Data) -> String? {
        if case .failure(let e) = SpriteFrames.read(data) { return e.message }
        return nil
    }

    // MARK: Sheet

    @Test func theSheetsCellsHoldTheFramesInOrder() throws {
        let r = try made(Self.colourGif(5))
        #expect(r.frameCount == 5 && r.frameWidth == 16 && r.frameHeight == 16)
        let d = Self.decode(r.png)
        #expect(d.w == 80 && d.h == 16)
        for i in 0..<5 {
            let got = Self.pixel(d, i * 16 + 8, 8)
            #expect(Self.near(got, Self.palette[i]), "cell \(i) is \(got)")
        }
    }

    @Test func twentyFramesLayOutSixteenAcrossAndTwoDown() throws {
        let r = try made(Self.colourGif(20))
        let d = Self.decode(r.png)
        #expect(d.w == 256 && d.h == 32)
        // Row-major: frame 16 is the first cell of the second row.
        #expect(Self.near(Self.pixel(d, 8, 16 + 8), Self.palette[16 % 8]))
        #expect(Self.near(Self.pixel(d, 3 * 16 + 8, 16 + 8), Self.palette[19 % 8]))
        #expect(Self.near(Self.pixel(d, 15 * 16 + 8, 8), Self.palette[15 % 8]))
    }

    // MARK: Compositing

    /// Probed on macOS 26: ImageIO's encoder writes every frame at the first frame's size (a smaller image is scaled
    /// up, so no partial frame can be written here), and the decoder hands each frame back at the full canvas size,
    /// uncomposited: a fully transparent frame decodes as fully transparent. So the persistent canvas does the
    /// compositing, and a frame with transparency over an opaque first frame is the case to test: the first frame
    /// shows through the clear pixels. (A smaller frame from a real-world GIF is drawn at the canvas's top-left.)
    @Test func aTransparentFrameShowsTheFrameBeneath() throws {
        let base = Self.solid(16, 16, Self.palette[2])
        let clear = Self.solid(16, 16, (0, 0, 0, 0))
        let r = try made(Self.gif([(base, 0.1), (clear, 0.1)]))
        let d = Self.decode(r.png)
        #expect(Self.near(Self.pixel(d, 8, 8), Self.palette[2]))
        #expect(Self.near(Self.pixel(d, 16 + 8, 8), Self.palette[2]), "frame 2 shows \(Self.pixel(d, 24, 8))")
        #expect(Self.pixel(d, 16 + 8, 8).a == 255)
    }

    // MARK: fps

    @Test func fpsIsTheMedianDelayAsFramesPerSecond() throws {
        #expect(try made(Self.colourGif(3, delay: 0.1)).fps == 10)
        #expect(try made(Self.colourGif(3, delay: 0.05)).fps == 20)
        #expect(try made(Self.colourGif(3, delay: 0)).fps == 10)
        #expect(try made(Self.colourGif(3, delay: 2.0)).fps == 1)
        #expect(try made(Self.colourGif(3, delay: 0.02)).fps == 24)
    }

    @Test func theMedianOfMixedDelaysDecides() {
        #expect(SpriteFrames.fps(delays: [0.5, 0.1, 0.1]) == 10)
        #expect(SpriteFrames.fps(delays: [0.1, 0.1, 0.5, 0.5]) == 3)
        #expect(SpriteFrames.fps(delays: [0, 0.2, 0.2]) == 5)
        #expect(SpriteFrames.fps(delays: [0.04, 0.5, 0.5]) == 2)
    }

    @Test func mixedDelaysInAGifReadBackAsTheirMedian() throws {
        let frames = (0..<5).map { (Self.solid(16, 16, Self.palette[$0]), [0.05, 0.05, 0.05, 0.5, 0.5][$0]) }
        #expect(try made(Self.gif(frames)).fps == 20)
    }

    // MARK: Refusals

    @Test func oneFrameIsRefused() {
        #expect(refusal(Self.colourGif(1)) == "That GIF has one frame. A sprite needs two or more to walk.")
    }

    @Test func framesOutsideEightToSixtyFourAreRefused() {
        #expect(refusal(Self.colourGif(3, 4, 4)) == "That GIF's frames are 4x4. A sprite's cells are 8 to 64 pixels on a side.")
        #expect(refusal(Self.colourGif(2, 80, 80)) == "That GIF's frames are 80x80. A sprite's cells are 8 to 64 pixels on a side.")
    }

    @Test func moreThanTwoFiftySixFramesIsRefused() {
        #expect(refusal(Self.colourGif(257, 8, 8)) == "That GIF has 257 frames. A sprite takes at most 256.")
        #expect(refusal(Self.colourGif(256, 8, 8)) == nil)
    }

    @Test func aFileThatIsNotAGifIsRefused() {
        let sentence = "That file isn't a GIF Sanduhr can read."
        #expect(refusal(WidgetOwnArtTests.sheet(16, 16)) == sentence)
        #expect(refusal(Data((0..<200).map { UInt8(($0 * 37 + 11) % 256) })) == sentence)
        #expect(refusal(Data()) == sentence)
    }

    // MARK: End to end

    @Test func aPackMadeFromAGifValidatesStrictWithTheGifsFps() throws {
        let r = try made(Self.colourGif(6, 16, 16, delay: 0.05))
        let model = NewSpriteModel(gif: r, install: { _ in [] })
        #expect(model.cellWidth == 16 && model.cellHeight == 16 && model.cellCount == 6)
        #expect(model.cells(of: "walk") == [0, 1, 2, 3, 4, 5] && model.cells(of: "sit") == [0])
        model.name = "Blinker"
        let pack = WidgetNewSprite.make(model.input, installedIds: [], now: Date(timeIntervalSince1970: 0))
        #expect(pack.ok, "\(pack.errors)")
        let verdict = PackValidator.validate(pack.files ?? [:], folderName: pack.manifest?.id ?? "", strict: true)
        #expect(verdict.ok)
        let json = String(decoding: pack.files?["sprite.json"] ?? Data(), as: UTF8.self)
        #expect(json.contains("\"fps\": 20"), "\(json)")
    }

    @Test func aPngSpriteKeepsEightAndAnOutOfRangeFpsIsRefused() {
        let png = WidgetNewSprite.Input(png: WidgetOwnArtTests.sheet(32, 16), cellWidth: 8, cellHeight: 8, name: "Plain",
                                        scale: nil, tags: ["walk": [0, 1], "sit": [0]])
        let plain = WidgetNewSprite.make(png, installedIds: [], now: Date(timeIntervalSince1970: 0))
        let plainJson = String(decoding: plain.files?["sprite.json"] ?? Data(), as: UTF8.self)
        #expect(plainJson.contains("\"fps\": 8"))
        var bad = png
        bad.fps = 30
        #expect(WidgetNewSprite.make(bad, installedIds: [], now: Date(timeIntervalSince1970: 0)).errors.first
                == "Frames per second is 1 to 24.")
    }
}
