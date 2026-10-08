import AppKit
import CoreText
import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// EsteFont Pro and EsteFont 26 (items 58, 65 (e)): the bundle lookup, the font files
/// themselves, process-only registration, the Desk's default and fallback, the pickers' order and
/// the Bold face.
@Suite("Bundled fonts")
struct BundledFontsTests {
    /// mac/, from this file.
    static let mac = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    static let fontsFolder = mac.appendingPathComponent("Resources/Fonts")

    @Test func lookupFindsEveryFaceUnderResourcesFonts() {
        let resources = URL(fileURLWithPath: "/Applications/Sanduhr.app/Contents/Resources")
        let urls = BundledFonts.urls(resources: resources, exists: { _ in true })
        #expect(urls.map(\.path) == [
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFontPro-Regular.ttf",
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFontPro-Bold.ttf",
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFont26-Regular.ttf",
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFont26-Bold.ttf",
        ])
    }

    @Test func lookupLeavesOutMissingFilesAndNoResources() {
        let resources = URL(fileURLWithPath: "/tmp/R")
        let onlyBold = BundledFonts.urls(resources: resources, exists: { $0.lastPathComponent.contains("Bold") })
        #expect(onlyBold.map(\.lastPathComponent) == ["EsteFontPro-Bold.ttf", "EsteFont26-Bold.ttf"])
        #expect(BundledFonts.urls(resources: resources, exists: { _ in false }).isEmpty)
        #expect(BundledFonts.urls(resources: nil, exists: { _ in true }).isEmpty)
    }

    @Test func repoFilesCarryTheExpectedNames() throws {
        let urls = BundledFonts.urls(resources: Self.mac.appendingPathComponent("Resources"))
        #expect(urls.count == 4)
        var seen: [String: [String]] = [:]
        for url in urls {
            let descs = try #require(CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor])
            #expect(descs.count == 1)
            let font = CTFontCreateWithFontDescriptor(descs[0], 12, nil)
            let family = try #require(CTFontCopyName(font, kCTFontFamilyNameKey) as String?)
            let ps = try #require(CTFontCopyName(font, kCTFontPostScriptNameKey) as String?)
            seen[ps] = [family, CTFontCopyName(font, kCTFontStyleNameKey) as String? ?? ""]
            let copyright = CTFontCopyName(font, kCTFontCopyrightNameKey) as String?
            #expect(copyright?.contains("626Labs LLC") == true)
            #expect(copyright?.contains("626 Labs") == false)
            #expect(url.lastPathComponent == ps + ".ttf")
        }
        #expect(seen == [
            "EsteFontPro-Regular": ["EsteFont Pro", "Regular"], "EsteFontPro-Bold": ["EsteFont Pro", "Bold"],
            "EsteFont26-Regular": ["EsteFont 26", "Regular"], "EsteFont26-Bold": ["EsteFont 26", "Bold"],
        ])
    }

    /// EsteFont Pro is the 3.000 release (decision 2026-10-06).
    @Test func esteFontProIsVersion3() throws {
        for face in [BundledFonts.esteFontPro.regularFace, BundledFonts.esteFontPro.boldFace] {
            let url = Self.fontsFolder.appendingPathComponent(face + ".ttf")
            let desc = try #require((CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor])?.first)
            let font = CTFontCreateWithFontDescriptor(desc, 12, nil)
            #expect((CTFontCopyName(font, kCTFontVersionNameKey) as String?)?.hasPrefix("Version 3.000") == true)
        }
    }

    /// The third-party notices name each file and quote each font's own copyright and license.
    @Test func noticesCarryTheFontsLicense() throws {
        let notices = try String(contentsOf: Self.mac.appendingPathComponent("THIRD-PARTY-NOTICES.txt"), encoding: .utf8)
        let flat = notices.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        for name in BundledFonts.fileNames {
            #expect(flat.contains("Fonts/" + name))
            let url = Self.fontsFolder.appendingPathComponent(name)
            let desc = try #require((CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor])?.first)
            let font = CTFontCreateWithFontDescriptor(desc, 12, nil)
            for key in [kCTFontCopyrightNameKey, kCTFontLicenseNameKey] {
                let record = try #require(CTFontCopyName(font, key) as String?)
                #expect(flat.contains(record.split(whereSeparator: \.isWhitespace).joined(separator: " ")))
            }
        }
        #expect(AppInfo.fontCredit.contains("EsteFont Pro"))
        #expect(AppInfo.fontCredit.contains("EsteFont 26"))
    }

    /// Registered for the process, every face is a member of its family as AppKit sees it, which
    /// is what the widget's face lookup and the Desk's `.custom` resolve against.
    @Test func processRegistrationMakesEveryFaceAvailable() {
        for name in BundledFonts.fileNames {
            var error: Unmanaged<CFError>?
            let url = Self.fontsFolder.appendingPathComponent(name)
            if !CTFontManagerRegisterFontsForURL(url as CFURL, .process, &error) {
                // Already registered (or the same font installed for this user) is fine.
                let code = error.map { CFErrorGetCode($0.takeRetainedValue()) }
                #expect(code == CTFontManagerError.alreadyRegistered.rawValue)
            }
        }
        DeskFont.forgetAvailability()
        for family in BundledFonts.families {
            let members = NSFontManager.shared.availableMembers(ofFontFamily: family.name) ?? []
            let faces = Set(members.compactMap { $0.first as? String })
            #expect(faces.isSuperset(of: [family.regularFace, family.boldFace]))
            #expect(DeskFont.isAvailable(family.name))
            #expect(NSFont(name: family.boldFace, size: 20) != nil)
        }
    }

    @Test func boldFaceForBothBundledFamilies() {
        #expect(BundledFonts.face("EsteFont Pro", bold: true) == "EsteFontPro-Bold")
        #expect(BundledFonts.face("EsteFont Pro", bold: false) == "EsteFontPro-Regular")
        #expect(BundledFonts.face("EsteFont 26", bold: true) == "EsteFont26-Bold")
        #expect(BundledFonts.face("EsteFont 26", bold: false) == "EsteFont26-Regular")
        #expect(BundledFonts.face("Avenir", bold: true) == "Avenir")
        #expect(BundledFonts.face("", bold: true) == "")
        #expect(BundledFonts.bundled("EsteFont 2.1") == nil)
        #expect(FontSettings.wantsBold(.semibold) && FontSettings.wantsBold(.bold))
        #expect(!FontSettings.wantsBold(.regular) && !FontSettings.wantsBold(.medium))
    }

    @Test func defaultIsEsteFontPro() {
        #expect(BundledFonts.family == "EsteFont Pro")
        #expect(DeskFont.defaultFamily == "EsteFont Pro")
        #expect(DeskFont.heritageFamily == "EsteFont 26")
        #expect(BundledFonts.regularFace == "EsteFontPro-Regular")
        #expect(BundledFonts.boldFace == "EsteFontPro-Bold")
    }

    @Test func pickersListEsteFontProThenEsteFont26FirstOnce() {
        let families = DeskFont.pickerFamilies(installed: ["Zapfino", "EsteFont 26", "avenir", "EsteFont Pro", "Menlo"])
        #expect(families == ["EsteFont Pro", "EsteFont 26", "avenir", "Menlo", "Zapfino"])
        #expect(DeskFont.pickerFamilies(installed: []) == ["EsteFont Pro", "EsteFont 26"])
    }
}

@Suite("Desk font default")
struct DeskFontTests {
    @Test func nothingSavedIsEsteFontPro() {
        #expect(DeskFont.resolve(saved: nil, available: { _ in false }) == "EsteFont Pro")
    }

    @Test func systemStaysSystem() {
        #expect(DeskFont.resolve(saved: "", available: { _ in false }) == "")
    }

    @Test func anInstalledChoiceStays() {
        #expect(DeskFont.resolve(saved: "Avenir", available: { $0 == "Avenir" }) == "Avenir")
        #expect(DeskFont.resolve(saved: "EsteFont 2.1", available: { $0 == "EsteFont 2.1" }) == "EsteFont 2.1")
        #expect(DeskFont.resolve(saved: "EsteFont 26", available: { _ in true }) == "EsteFont 26")
    }

    @Test func aChoiceNoLongerInstalledFallsBackToEsteFontPro() {
        #expect(DeskFont.resolve(saved: "EsteFont 2.1", available: { _ in false }) == "EsteFont Pro")
        #expect(DeskFont.resolve(saved: "Gone Font", available: { _ in false }) == "EsteFont Pro")
        // The bundled families never fall back to anything else.
        #expect(DeskFont.resolve(saved: "EsteFont 26", available: { _ in false }) == "EsteFont 26")
        #expect(DeskFont.resolve(saved: "EsteFont Pro", available: { _ in false }) == "EsteFont Pro")
    }

    @Test func readsTheDeskSuite() {
        let d = MemoryDefaults()
        #expect(DeskFont.resolve(d, available: { _ in true }) == "EsteFont Pro")
        d.set("Avenir", forKey: "font")
        #expect(DeskFont.resolve(d, available: { _ in true }) == "Avenir")
    }

    @Test func freshInstallKeepsNothingSaved() {
        let d = MemoryDefaults()
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.object(forKey: "font") == nil)
        #expect(d.bool(forKey: DeskFont.settledKey))
        #expect(d.bool(forKey: DeskFont.proSettledKey))
        #expect(DeskFont.resolve(d, available: { _ in false }) == "EsteFont Pro")
        // Later launches (DeskMigration has marked the suite by then) leave it alone.
        d.set(true, forKey: "migrated")
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.object(forKey: "font") == nil)
    }

    @Test func anEarlierVersionsDeskKeepsTheSystemFont() {
        let d = MemoryDefaults()
        d.set(true, forKey: "migrated")
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.string(forKey: "font") == "")
        #expect(DeskFont.resolve(d, available: { _ in false }) == "")
    }

    /// A Desk that 2.6.0 to 2.8.0 settled with no font drew in EsteFont 26, the default then: the
    /// upgrade writes it, so EsteFont Pro never replaces it on screen.
    @Test func aDeskThatDrewInEsteFont26KeepsIt() {
        let d = MemoryDefaults()
        d.set(true, forKey: "migrated")
        d.set(true, forKey: DeskFont.settledKey)
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.string(forKey: "font") == "EsteFont 26")
        #expect(d.bool(forKey: DeskFont.proSettledKey))
        #expect(DeskFont.resolve(d, available: { _ in false }) == "EsteFont 26")
        // Picking EsteFont Pro later sticks: the upgrade runs once.
        d.set("EsteFont Pro", forKey: "font")
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.string(forKey: "font") == "EsteFont Pro")
    }

    /// The Pro upgrade leaves any saved choice alone: the system font, EsteFont 2.1, another font.
    @Test func theProUpgradeKeepsASavedChoice() {
        for saved in ["", "EsteFont 2.1", "Avenir", "EsteFont 26"] {
            let d = MemoryDefaults()
            d.set(true, forKey: "migrated")
            d.set(true, forKey: DeskFont.settledKey)
            d.set(saved, forKey: "font")
            DeskFont.keepExistingDefault(desk: d)
            #expect(d.string(forKey: "font") == saved)
        }
    }

    @Test func aPickedFontIsNeverTouched() {
        let d = MemoryDefaults()
        d.set(true, forKey: "migrated")
        d.set("Avenir", forKey: "font")
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.string(forKey: "font") == "Avenir")
    }

    /// The standalone apps' EsteFont 2.1, carried over by DeskMigration, falls back to EsteFont Pro
    /// on a Mac without it.
    @Test func migrantsOldEsteFontFallsBack() {
        let s = ScratchDefaults()
        s.seed(s.older, ["layout": "clock:tr"])
        DeskFont.keepExistingDefault(desk: s.defaults)
        s.migrate()
        #expect(s.defaults.string(forKey: "font") == "EsteFont 2.1")
        #expect(DeskFont.resolve(s.defaults, available: { _ in false }) == "EsteFont Pro")
    }
}
