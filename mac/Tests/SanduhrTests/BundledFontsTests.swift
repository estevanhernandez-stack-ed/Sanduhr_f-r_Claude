import AppKit
import CoreText
import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// EsteFont 26 (item 58): the bundle lookup, the font files themselves, process-only
/// registration, the Desk's default and fallback, the pickers' order and the Bold face.
@Suite("Bundled fonts")
struct BundledFontsTests {
    /// mac/, from this file.
    static let mac = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    static let fontsFolder = mac.appendingPathComponent("Resources/Fonts")

    @Test func lookupFindsBothFacesUnderResourcesFonts() {
        let resources = URL(fileURLWithPath: "/Applications/Sanduhr.app/Contents/Resources")
        let urls = BundledFonts.urls(resources: resources, exists: { _ in true })
        #expect(urls.map(\.path) == [
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFont26-Regular.ttf",
            "/Applications/Sanduhr.app/Contents/Resources/Fonts/EsteFont26-Bold.ttf",
        ])
    }

    @Test func lookupLeavesOutMissingFilesAndNoResources() {
        let resources = URL(fileURLWithPath: "/tmp/R")
        let onlyBold = BundledFonts.urls(resources: resources, exists: { $0.lastPathComponent.contains("Bold") })
        #expect(onlyBold.map(\.lastPathComponent) == ["EsteFont26-Bold.ttf"])
        #expect(BundledFonts.urls(resources: resources, exists: { _ in false }).isEmpty)
        #expect(BundledFonts.urls(resources: nil, exists: { _ in true }).isEmpty)
    }

    @Test func repoFilesCarryTheExpectedNames() throws {
        let urls = BundledFonts.urls(resources: Self.mac.appendingPathComponent("Resources"))
        #expect(urls.count == 2)
        var seen: [String: String] = [:]
        for url in urls {
            let descs = try #require(CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor])
            #expect(descs.count == 1)
            let font = CTFontCreateWithFontDescriptor(descs[0], 12, nil)
            #expect(CTFontCopyName(font, kCTFontFamilyNameKey) as String? == "EsteFont 26")
            let ps = try #require(CTFontCopyName(font, kCTFontPostScriptNameKey) as String?)
            seen[ps] = CTFontCopyName(font, kCTFontStyleNameKey) as String?
            #expect((CTFontCopyName(font, kCTFontCopyrightNameKey) as String?)?.contains("626Labs LLC") == true)
            #expect(url.lastPathComponent == ps + ".ttf")
        }
        #expect(seen == ["EsteFont26-Regular": "Regular", "EsteFont26-Bold": "Bold"])
    }

    /// The third-party notices quote the font's own copyright and license records.
    @Test func noticesCarryTheFontsLicense() throws {
        let notices = try String(contentsOf: Self.mac.appendingPathComponent("THIRD-PARTY-NOTICES.txt"), encoding: .utf8)
        let flat = notices.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        let url = Self.fontsFolder.appendingPathComponent("EsteFont26-Regular.ttf")
        let desc = try #require((CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor])?.first)
        let font = CTFontCreateWithFontDescriptor(desc, 12, nil)
        for key in [kCTFontCopyrightNameKey, kCTFontLicenseNameKey] {
            let record = try #require(CTFontCopyName(font, key) as String?)
            #expect(flat.contains(record.split(whereSeparator: \.isWhitespace).joined(separator: " ")))
        }
        #expect(AppInfo.fontCredit.contains("EsteFont 26"))
    }

    /// Registered for the process, both faces are members of the family AppKit sees, which is
    /// what the widget's face lookup and the Desk's `.custom` resolve against.
    @Test func processRegistrationMakesBothFacesAvailable() {
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
        let members = NSFontManager.shared.availableMembers(ofFontFamily: BundledFonts.family) ?? []
        let faces = Set(members.compactMap { $0.first as? String })
        #expect(faces.isSuperset(of: [BundledFonts.regularFace, BundledFonts.boldFace]))
        #expect(DeskFont.isAvailable(BundledFonts.family))
        #expect(NSFont(name: BundledFonts.boldFace, size: 20) != nil)
    }

    @Test func boldFaceOnlyForEsteFont26() {
        #expect(BundledFonts.face("EsteFont 26", bold: true) == "EsteFont26-Bold")
        #expect(BundledFonts.face("EsteFont 26", bold: false) == "EsteFont26-Regular")
        #expect(BundledFonts.face("Avenir", bold: true) == "Avenir")
        #expect(BundledFonts.face("", bold: true) == "")
        #expect(FontSettings.wantsBold(.semibold) && FontSettings.wantsBold(.bold))
        #expect(!FontSettings.wantsBold(.regular) && !FontSettings.wantsBold(.medium))
    }

    @Test func pickersListEsteFont26FirstOnce() {
        let families = DeskFont.pickerFamilies(installed: ["Zapfino", "EsteFont 26", "avenir", "Menlo"])
        #expect(families == ["EsteFont 26", "avenir", "Menlo", "Zapfino"])
        #expect(DeskFont.pickerFamilies(installed: []) == ["EsteFont 26"])
    }
}

@Suite("Desk font default")
struct DeskFontTests {
    @Test func nothingSavedIsEsteFont26() {
        #expect(DeskFont.resolve(saved: nil, available: { _ in false }) == "EsteFont 26")
    }

    @Test func systemStaysSystem() {
        #expect(DeskFont.resolve(saved: "", available: { _ in false }) == "")
    }

    @Test func anInstalledChoiceStays() {
        #expect(DeskFont.resolve(saved: "Avenir", available: { $0 == "Avenir" }) == "Avenir")
        #expect(DeskFont.resolve(saved: "EsteFont 2.1", available: { $0 == "EsteFont 2.1" }) == "EsteFont 2.1")
    }

    @Test func aChoiceNoLongerInstalledFallsBackToEsteFont26() {
        #expect(DeskFont.resolve(saved: "EsteFont 2.1", available: { _ in false }) == "EsteFont 26")
        // EsteFont 26 itself never falls back to anything else.
        #expect(DeskFont.resolve(saved: "EsteFont 26", available: { _ in false }) == "EsteFont 26")
    }

    @Test func readsTheDeskSuite() {
        let d = MemoryDefaults()
        #expect(DeskFont.resolve(d, available: { _ in true }) == "EsteFont 26")
        d.set("Avenir", forKey: "font")
        #expect(DeskFont.resolve(d, available: { _ in true }) == "Avenir")
    }

    @Test func freshInstallKeepsNothingSaved() {
        let d = MemoryDefaults()
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.object(forKey: "font") == nil)
        #expect(d.bool(forKey: DeskFont.settledKey))
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

    @Test func aPickedFontIsNeverTouched() {
        let d = MemoryDefaults()
        d.set(true, forKey: "migrated")
        d.set("Avenir", forKey: "font")
        DeskFont.keepExistingDefault(desk: d)
        #expect(d.string(forKey: "font") == "Avenir")
    }

    /// The standalone apps' EsteFont 2.1, carried over by DeskMigration, falls back to EsteFont 26
    /// on a Mac without it.
    @Test func migrantsOldEsteFontFallsBack() {
        let s = ScratchDefaults()
        s.seed(s.older, ["layout": "clock:tr"])
        DeskFont.keepExistingDefault(desk: s.defaults)
        s.migrate()
        #expect(s.defaults.string(forKey: "font") == "EsteFont 2.1")
        #expect(DeskFont.resolve(s.defaults, available: { _ in false }) == "EsteFont 26")
    }
}
