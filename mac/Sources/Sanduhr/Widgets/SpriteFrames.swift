import Foundation
import ImageIO
import CoreGraphics

/// "Import an animated GIF" (spec 2.43.0 section 22): a GIF's frames as ImageIO composites them, laid out
/// row-major into one PNG sheet in memory, with the fps its delays make. Pure: bytes in, a sheet or a sentence out.
/// Only ImageIO and Core Graphics; nothing is shown, written or sent.
enum SpriteFrames {
    struct Result: Equatable {
        var png: Data
        var frameWidth: Int
        var frameHeight: Int
        var frameCount: Int
        var fps: Int
    }

    static let maxFrames = 256
    static let fpsRange = 1...24
    static let unreadable = "That file isn't a GIF Sanduhr can read."

    /// The sheet made from `data`, or the one sentence why not.
    static func read(_ data: Data) -> Swift.Result<Result, SpriteFramesError> {
        func fail(_ s: String) -> Swift.Result<Result, SpriteFramesError> { .failure(SpriteFramesError(message: s)) }
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              (CGImageSourceGetType(source) as String?) == "com.compuserve.gif" else { return fail(unreadable) }
        let count = CGImageSourceGetCount(source)
        if count == 0 { return fail(unreadable) }
        if count == 1 { return fail("That GIF has one frame. A sprite needs two or more to walk.") }
        guard let first = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return fail(unreadable) }
        let w = canvasSize(source, fallback: first).width, h = canvasSize(source, fallback: first).height
        let range = WidgetNewSprite.cellMin...WidgetNewSprite.cellMax
        if !range.contains(w) || !range.contains(h) {
            return fail("That GIF's frames are \(w)x\(h). A sprite's cells are \(WidgetNewSprite.cellMin) to \(WidgetNewSprite.cellMax) pixels on a side.")
        }
        if count > maxFrames { return fail("That GIF has \(count) frames. A sprite takes at most \(maxFrames).") }

        let (columns, rows) = PixelSheet.layout(frameCount: count)
        let sheetW = columns * w, sheetH = rows * h
        guard sheetW <= PackValidator.maxImageSide, sheetH <= PackValidator.maxImageSide else {
            return fail("That GIF's sheet would be \(sheetW)x\(sheetH). A sprite sheet is at most \(PackValidator.maxImageSide) pixels on a side.")
        }
        let space = CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
        let info = CGImageAlphaInfo.premultipliedLast.rawValue
        guard let canvas = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                     space: space, bitmapInfo: info),
              let sheet = CGContext(data: nil, width: sheetW, height: sheetH, bitsPerComponent: 8, bytesPerRow: 0,
                                    space: space, bitmapInfo: info) else { return fail(unreadable) }
        canvas.interpolationQuality = .none
        var delays: [Double] = []
        for i in 0..<count {
            guard let frame = CGImageSourceCreateImageAtIndex(source, i, nil) else { return fail(unreadable) }
            // ImageIO hands back every frame already composited at the full canvas, with each frame's disposal
            // applied (a delta GIF made by ffmpeg decodes with the moved sprite's old place clear). So each cell is
            // that frame alone: the canvas is cleared first, or a moving sprite on a clear background would smear.
            // A frame smaller than the canvas (not seen from ImageIO) is drawn at the top-left, never scaled.
            let fh = frame.height
            canvas.clear(CGRect(x: 0, y: 0, width: w, height: h))
            canvas.draw(frame, in: CGRect(x: 0, y: h - fh, width: frame.width, height: fh))
            guard let composed = canvas.makeImage() else { return fail(unreadable) }
            // Core Graphics' origin is bottom-left: row 0 of the sheet is its top.
            let col = i % columns, row = i / columns
            sheet.draw(composed, in: CGRect(x: col * w, y: sheetH - (row + 1) * h, width: w, height: h))
            delays.append(delay(source, i))
        }
        guard let image = sheet.makeImage(), let png = encode(image) else { return fail(unreadable) }
        let made = WidgetOwnArt.imageSize(png)
        if let why = made.error { return fail(why) }
        return .success(Result(png: png, frameWidth: w, frameHeight: h, frameCount: count, fps: fps(delays: delays)))
    }

    /// Frames per second from the median delay: round(1 / median), held in 1...24. A delay of 0 counts as 0.1 s.
    static func fps(delays: [Double]) -> Int {
        let sorted = delays.map { $0 <= 0 ? 0.1 : $0 }.sorted()
        guard !sorted.isEmpty else { return 8 }
        let mid = sorted.count / 2
        let median = sorted.count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2
        let raw = Int((1 / median).rounded())
        return min(max(raw, fpsRange.lowerBound), fpsRange.upperBound)
    }

    private static func canvasSize(_ source: CGImageSource, fallback: CGImage) -> (width: Int, height: Int) {
        if let props = CGImageSourceCopyProperties(source, nil) as? [CFString: Any],
           let w = props[kCGImagePropertyPixelWidth] as? Int, let h = props[kCGImagePropertyPixelHeight] as? Int,
           w > 0, h > 0 { return (w, h) }
        if let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
           let w = props[kCGImagePropertyPixelWidth] as? Int, let h = props[kCGImagePropertyPixelHeight] as? Int,
           w > 0, h > 0 { return (w, h) }
        return (fallback.width, fallback.height)
    }

    private static func delay(_ source: CGImageSource, _ index: Int) -> Double {
        guard let props = CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [CFString: Any],
              let gif = props[kCGImagePropertyGIFDictionary] as? [CFString: Any] else { return 0.1 }
        if let d = (gif[kCGImagePropertyGIFUnclampedDelayTime] as? NSNumber)?.doubleValue { return d }
        if let d = (gif[kCGImagePropertyGIFDelayTime] as? NSNumber)?.doubleValue { return d }
        return 0.1
    }

    private static func encode(_ image: CGImage) -> Data? {
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out, "public.png" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return out as Data
    }
}

struct SpriteFramesError: Error, Equatable {
    let message: String
}
