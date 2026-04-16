// Renders Sanduhr's app icon to a 1024×1024 PNG using CoreGraphics.
// Call: swift generate.swift <output.png>
//
// Brand palette (matches 626 Labs logo — navy/cyan/magenta):
//   - Deep navy bg gradient (#162033 → #2a3a5c)
//   - Cyan primary   #3bb4d9  (hourglass frame, sand-top)
//   - Magenta accent #e13aa0  (sand-bottom, rim highlights)
//   - Pale cyan      #7ae0f5  (sand sheen)
//
// The hourglass "drains" cyan sand into a magenta heap — a visual dialogue
// with the cyan/magenta swoosh in the 626 Labs master logo.

import Foundation
import AppKit
import CoreGraphics

let size: CGFloat = 1024
let outputPath = CommandLine.arguments.dropFirst().first ?? "icon.png"

guard let ctx = CGContext(
    data: nil,
    width: Int(size), height: Int(size),
    bitsPerComponent: 8, bytesPerRow: 0,
    space: CGColorSpace(name: CGColorSpace.sRGB)!,
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
) else {
    fputs("Could not create CGContext\n", stderr); exit(1)
}

// Helper: hex → CGColor
func c(_ hex: UInt32, alpha a: CGFloat = 1) -> CGColor {
    CGColor(
        red:   CGFloat((hex >> 16) & 0xFF) / 255,
        green: CGFloat((hex >>  8) & 0xFF) / 255,
        blue:  CGFloat( hex        & 0xFF) / 255,
        alpha: a)
}

// Brand colors
let navyDeep  = c(0x0f182b)
let navyLight = c(0x2a3a5c)
let cyan      = c(0x3bb4d9)
let cyanPale  = c(0x7ae0f5)
let magenta   = c(0xe13aa0)

// MARK: Background — rounded-rect with radial gradient

// Big Sur icon template: 1024 canvas, ~824 art area, corner radius ~185.
let bgRect = CGRect(x: 100, y: 100, width: 824, height: 824)
let bgRadius: CGFloat = 185
let bgPath = CGPath(roundedRect: bgRect,
                    cornerWidth: bgRadius, cornerHeight: bgRadius,
                    transform: nil)

ctx.saveGState()
ctx.addPath(bgPath)
ctx.clip()

// Deep navy radial, echoing the 626 Labs logo background.
let bgGrad = CGGradient(
    colorsSpace: nil,
    colors: [navyLight, navyDeep] as CFArray,
    locations: [0.0, 1.0])!
ctx.drawRadialGradient(
    bgGrad,
    startCenter: CGPoint(x: size / 2, y: size / 2 + 60), startRadius: 40,
    endCenter:   CGPoint(x: size / 2, y: size / 2),      endRadius: 540,
    options: [])

// Faint hexagonal outline — nods to the logo's hex frame without imitating it.
let hex = CGMutablePath()
let hexCX = size / 2, hexCY = size / 2, hexR: CGFloat = 340
for i in 0..<6 {
    let angle = CGFloat.pi / 2 + CGFloat(i) * CGFloat.pi / 3
    let p = CGPoint(x: hexCX + cos(angle) * hexR,
                    y: hexCY + sin(angle) * hexR)
    i == 0 ? hex.move(to: p) : hex.addLine(to: p)
}
hex.closeSubpath()
ctx.addPath(hex)
ctx.setStrokeColor(c(0x3bb4d9, alpha: 0.10))
ctx.setLineWidth(3)
ctx.strokePath()

ctx.restoreGState()

// Subtle top sheen (glassy highlight).
ctx.saveGState()
ctx.addPath(bgPath)
ctx.clip()
let sheen = CGGradient(
    colorsSpace: nil,
    colors: [c(0xffffff, alpha: 0.07), c(0xffffff, alpha: 0.0)] as CFArray,
    locations: [0.0, 1.0])!
ctx.drawLinearGradient(
    sheen,
    start: CGPoint(x: size / 2, y: 900),
    end:   CGPoint(x: size / 2, y: 500),
    options: [])
ctx.restoreGState()

// MARK: Hourglass silhouette

let cx = size / 2
let cy = size / 2

let hgHalfW: CGFloat = 180
let hgHalfH: CGFloat = 270
let neckHalfW: CGFloat = 10

// Hourglass outline — a single closed path with curved shoulders.
let frame = CGMutablePath()
frame.move(to:    CGPoint(x: cx - hgHalfW, y: cy + hgHalfH))
frame.addLine(to: CGPoint(x: cx + hgHalfW, y: cy + hgHalfH))   // top rim
frame.addQuadCurve(to: CGPoint(x: cx + neckHalfW, y: cy),
                   control: CGPoint(x: cx + 70, y: cy + 70))
frame.addQuadCurve(to: CGPoint(x: cx + hgHalfW, y: cy - hgHalfH),
                   control: CGPoint(x: cx + 70, y: cy - 70))
frame.addLine(to: CGPoint(x: cx - hgHalfW, y: cy - hgHalfH))   // bottom rim
frame.addQuadCurve(to: CGPoint(x: cx - neckHalfW, y: cy),
                   control: CGPoint(x: cx - 70, y: cy - 70))
frame.addQuadCurve(to: CGPoint(x: cx - hgHalfW, y: cy + hgHalfH),
                   control: CGPoint(x: cx - 70, y: cy + 70))
frame.closeSubpath()

// Cyan outer glow behind the frame.
ctx.saveGState()
ctx.setShadow(offset: .zero, blur: 46, color: c(0x3bb4d9, alpha: 0.6))
ctx.addPath(frame)
ctx.setFillColor(c(0x3bb4d9, alpha: 0.12))
ctx.fillPath()
ctx.restoreGState()

// MARK: Sand — cyan at top, magenta heap at bottom (brand gradient)

// Bottom-bulb clip (heap).
ctx.saveGState()
let bottomBulb = CGMutablePath()
bottomBulb.move(to:    CGPoint(x: cx - neckHalfW, y: cy))
bottomBulb.addQuadCurve(to: CGPoint(x: cx - hgHalfW, y: cy - hgHalfH),
                        control: CGPoint(x: cx - 70, y: cy - 70))
bottomBulb.addLine(to: CGPoint(x: cx + hgHalfW, y: cy - hgHalfH))
bottomBulb.addQuadCurve(to: CGPoint(x: cx + neckHalfW, y: cy),
                        control: CGPoint(x: cx + 70, y: cy - 70))
bottomBulb.closeSubpath()
ctx.addPath(bottomBulb)
ctx.clip()

let heapGrad = CGGradient(
    colorsSpace: nil,
    colors: [c(0xff5eb6), magenta, c(0x9a2a78)] as CFArray,
    locations: [0.0, 0.6, 1.0])!
// Gradient fills the lower bulb with a bright top edge fading into deeper magenta.
ctx.drawLinearGradient(
    heapGrad,
    start: CGPoint(x: cx, y: cy - hgHalfH * 0.30),
    end:   CGPoint(x: cx, y: cy - hgHalfH),
    options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
ctx.restoreGState()

// Top-bulb clip (remaining cyan sand).
ctx.saveGState()
let topBulb = CGMutablePath()
topBulb.move(to:    CGPoint(x: cx - neckHalfW, y: cy))
topBulb.addQuadCurve(to: CGPoint(x: cx - hgHalfW, y: cy + hgHalfH),
                     control: CGPoint(x: cx - 70, y: cy + 70))
topBulb.addLine(to: CGPoint(x: cx + hgHalfW, y: cy + hgHalfH))
topBulb.addQuadCurve(to: CGPoint(x: cx + neckHalfW, y: cy),
                     control: CGPoint(x: cx + 70, y: cy + 70))
topBulb.closeSubpath()
ctx.addPath(topBulb)
ctx.clip()

let topGrad = CGGradient(
    colorsSpace: nil,
    colors: [cyan, cyanPale] as CFArray,
    locations: [0.0, 1.0])!
ctx.drawLinearGradient(
    topGrad,
    start: CGPoint(x: cx, y: cy),
    end:   CGPoint(x: cx, y: cy + hgHalfH * 0.40),
    options: [])
ctx.restoreGState()

// Thin stream through the neck — cyan fading into magenta as it falls.
ctx.saveGState()
let streamClip = CGPath(rect: CGRect(x: cx - 4, y: cy - hgHalfH * 0.55,
                                     width: 8, height: hgHalfH * 1.1),
                        transform: nil)
ctx.addPath(streamClip)
ctx.clip()
let streamGrad = CGGradient(
    colorsSpace: nil,
    colors: [cyanPale, cyan, magenta] as CFArray,
    locations: [0.0, 0.5, 1.0])!
ctx.drawLinearGradient(
    streamGrad,
    start: CGPoint(x: cx, y: cy + hgHalfH * 0.55),
    end:   CGPoint(x: cx, y: cy - hgHalfH * 0.55),
    options: [])
ctx.restoreGState()

// MARK: Frame outline on top

ctx.saveGState()
ctx.addPath(frame)
ctx.setStrokeColor(cyanPale)
ctx.setLineWidth(16)
ctx.setLineJoin(.round)
ctx.strokePath()
ctx.restoreGState()

// Rim caps — deeper navy with a cyan highlight stripe along the top edge of each.
let rimH: CGFloat = 26
let topRim    = CGRect(x: cx - hgHalfW - 18, y: cy + hgHalfH - rimH / 2,
                       width: hgHalfW * 2 + 36, height: rimH)
let bottomRim = CGRect(x: cx - hgHalfW - 18, y: cy - hgHalfH - rimH / 2,
                       width: hgHalfW * 2 + 36, height: rimH)
ctx.setFillColor(c(0x1b2a4a))
ctx.fill(topRim)
ctx.fill(bottomRim)
ctx.setFillColor(c(0x3bb4d9, alpha: 0.7))
ctx.fill(CGRect(x: topRim.minX + 6, y: topRim.maxY - 4,
                width: topRim.width - 12, height: 2))
ctx.fill(CGRect(x: bottomRim.minX + 6, y: bottomRim.maxY - 4,
                width: bottomRim.width - 12, height: 2))

// MARK: Write PNG

guard let cgImage = ctx.makeImage() else {
    fputs("Failed to create CGImage\n", stderr); exit(1)
}
let rep = NSBitmapImageRep(cgImage: cgImage)
rep.size = NSSize(width: size, height: size)
guard let data = rep.representation(using: .png, properties: [:]) else {
    fputs("Failed to encode PNG\n", stderr); exit(1)
}
try data.write(to: URL(fileURLWithPath: outputPath))
print("✓ Wrote \(outputPath) (\(Int(size))×\(Int(size)))")
