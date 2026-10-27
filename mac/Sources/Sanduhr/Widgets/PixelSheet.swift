import CoreGraphics
import Foundation
import ImageIO

/// A rasterized pixel sheet: straight (not premultiplied) RGBA, 8 bits a channel, rows top to
/// bottom. Frames sit in a grid of `columns` by `rows` cells of `frameWidth` by `frameHeight`,
/// row-major, exactly as FORMAT 5.5 lays out a PNG sheet: frame `i` is cell
/// `(i % columns, i / columns)`. Cells past the last frame are transparent.
struct PixelSheetImage: Equatable {
    let frameWidth: Int
    let frameHeight: Int
    let frameCount: Int
    let columns: Int
    let rows: Int
    let rgba: [UInt8]

    var width: Int { columns * frameWidth }
    var height: Int { rows * frameHeight }

    /// One frame's RGBA (`frameWidth` x `frameHeight` x 4 bytes), copied out of the sheet; nil when out of range.
    func frame(_ index: Int) -> [UInt8]? {
        guard index >= 0, index < frameCount else { return nil }
        let ox = index % columns * frameWidth, oy = index / columns * frameHeight
        let rowBytes = frameWidth * 4
        var out: [UInt8] = []
        out.reserveCapacity(rowBytes * frameHeight)
        for y in 0..<frameHeight {
            let start = ((oy + y) * width + ox) * 4
            out.append(contentsOf: rgba[start..<start + rowBytes])
        }
        return out
    }

    /// The sheet as PNG data (8-bit RGBA, sRGB), or nil when ImageIO cannot encode it. Pixels go
    /// straight from `rgba` into the encoder with no drawing, so no value is resampled or converted.
    /// Every pixel is fully opaque or fully transparent, so straight and premultiplied agree.
    func pngData() -> Data? {
        guard width > 0, height > 0, rgba.count == width * height * 4,
              let provider = CGDataProvider(data: Data(rgba) as CFData),
              let space = CGColorSpace(name: CGColorSpace.sRGB),
              let image = CGImage(
                  width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: width * 4,
                  space: space, bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.last.rawValue),
                  provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out, "public.png" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return out as Data
    }
}

/// Why a pixel sheet did not rasterize: the first reason, as the validator words it.
struct PixelSheetError: Error, Equatable {
    let message: String
}

/// Sprite v2 pixel sheets (FORMAT 5.5): frames written as characters, `.` transparent and `0`-`9`,
/// `a`-`f` indexing the palette. `rasterize` turns one into the RGBA grid a PNG sheet would hold,
/// which is what Sanduhr draws (`PixelSheetImage.pngData` encodes it for the install folder). A
/// port of Windows' PixelSheet.cs. The parsing and layout are pure; only `pngData` touches Core Graphics.
enum PixelSheet {
    /// Frames per row of the rasterized grid: 256 frames of 32 px make a 512 x 512 sheet, inside the
    /// 4096-pixel image limit.
    static let sheetColumns = 16

    /// The grid for `count` frames: columns wrap at `sheetColumns`, rows fill row-major.
    static func layout(frameCount count: Int) -> (columns: Int, rows: Int) {
        let columns = max(1, min(count, sheetColumns))
        return (columns, (count + columns - 1) / columns)
    }

    /// Rasterize a pixel sheet's JSON. The sheet is checked first with the validator's own rules
    /// (against its own frame size); a sheet that fails returns the first reason. Never traps on bad input.
    static func rasterize(_ json: Data) -> Result<PixelSheetImage, PixelSheetError> {
        let bytes = [UInt8](json)
        func fail(_ m: String) -> Result<PixelSheetImage, PixelSheetError> { .failure(PixelSheetError(message: m)) }
        if bytes.count > PackValidator.pixelMaxBytes { return fail("a pixel sheet is at most 2 MB") }
        if bytes.starts(with: [0xEF, 0xBB, 0xBF]) { return fail("a pixel sheet must be UTF-8 without a byte-order mark") }
        guard let scalars = PackJson.decodeUTF8(bytes) else { return fail("invalid JSON (not valid UTF-8)") }
        let data: PackValue
        switch PackJson.parse(scalars) {
        case .success(let v): data = v
        case .failure(let e): return fail("invalid JSON (" + e.message + ")")
        }
        guard let o = data.object, let fwn = o["frame_w"]?.number, let fhn = o["frame_h"]?.number else {
            return fail("a pixel sheet must be a JSON object with " + PackValidator.pixelKeys.joined(separator: ", "))
        }
        // Frame sizes that are not plain integers fail the validator below; keep the cast from trapping first.
        guard fwn.isFinite, fhn.isFinite, abs(fwn.value) < 1e9, abs(fhn.value) < 1e9 else {
            return fail("frame_w and frame_h must be integers from 8 to 32")
        }
        let problems = PackValidator.checkPixelSheet("sheet", data, Int(fwn.value), Int(fhn.value), maxIndex: -1, label: "sheet")
        if let first = problems.first { return fail(first) }

        let fw = Int(fwn.value), fh = Int(fhn.value)
        guard let paletteValues = o["palette"]?.array, let frames = o["frames"]?.array else {
            return fail("a pixel sheet must be a JSON object with " + PackValidator.pixelKeys.joined(separator: ", "))
        }
        var palette: [(r: UInt8, g: UInt8, b: UInt8)] = []
        for c in paletteValues {
            guard let hex = c.string, let rgb = parseHex(hex) else { return fail("palette colors must be #rrggbb") }
            palette.append(rgb)
        }
        let count = frames.count
        let (columns, rows) = layout(frameCount: count)
        let width = columns * fw
        var rgba = [UInt8](repeating: 0, count: width * rows * fh * 4)
        for i in 0..<count {
            guard let frame = frames[i].array else { return fail("frames must be arrays of rows") }
            let ox = i % columns * fw, oy = i / columns * fh
            for y in 0..<fh {
                guard y < frame.count, let row = frame[y].string else { return fail("frames must be arrays of rows") }
                let cells = Array(row.unicodeScalars)
                for x in 0..<fw {
                    guard x < cells.count else { return fail("frames must be arrays of rows") }
                    let c = cells[x].value
                    if c == 0x2E { continue }   // transparent: already zero
                    let index = PackValidator.pixelIndex(c)
                    guard index >= 0, index < palette.count else { return fail("a pixel names a color past the palette") }
                    let (r, g, b) = palette[index]
                    let at = ((oy + y) * width + ox + x) * 4
                    rgba[at] = r
                    rgba[at + 1] = g
                    rgba[at + 2] = b
                    rgba[at + 3] = 255
                }
            }
        }
        return .success(PixelSheetImage(frameWidth: fw, frameHeight: fh, frameCount: count, columns: columns, rows: rows, rgba: rgba))
    }

    private static func parseHex(_ hex: String) -> (r: UInt8, g: UInt8, b: UInt8)? {
        let u = Array(hex.utf8)
        guard u.count == 7, u[0] == 0x23 else { return nil }
        func nib(_ b: UInt8) -> UInt8? {
            switch b {
            case 0x30...0x39: return b - 0x30
            case 0x61...0x66: return b - 0x61 + 10
            case 0x41...0x46: return b - 0x41 + 10
            default: return nil
            }
        }
        var v: [UInt8] = []
        for i in stride(from: 1, to: 7, by: 2) {
            guard let h = nib(u[i]), let l = nib(u[i + 1]) else { return nil }
            v.append(h << 4 | l)
        }
        return (v[0], v[1], v[2])
    }
}
