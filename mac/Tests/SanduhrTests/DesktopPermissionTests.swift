import Foundation
import Testing
@testable import Sanduhr

/// The Desktop Folder permission (docs/mac-drawers-setup-spec.md, section 6). Every reader here
/// is injected, so no test lists the real Desktop or can raise the macOS prompt; the denial is a
/// fake EPERM or EACCES, and the one real refusal is a temp folder with its mode cleared.

/// Records every Desktop read and probe, in order, and fails them on demand.
private final class FakeDesktop: @unchecked Sendable {
    private let lock = NSLock()
    private var _calls: [String] = []
    private var _failure: Error?
    private var _facts: [DrawerItemFacts] = [DrawerItemFacts(name: "a.pdf")]

    var calls: [String] { lock.lock(); defer { lock.unlock() }; return _calls }
    func fail(with error: Error?) { lock.lock(); _failure = error; lock.unlock() }

    func read(_ dir: String) throws -> [DrawerItemFacts] {
        lock.lock(); defer { lock.unlock() }
        _calls.append("read")
        if let e = _failure { throw e }
        return _facts
    }

    func probe(_ dir: String) throws {
        lock.lock(); defer { lock.unlock() }
        _calls.append("probe")
        if let e = _failure { throw e }
    }
}

private let eperm = POSIXError(.EPERM)
private let eacces = NSError(domain: NSPOSIXErrorDomain, code: Int(EACCES))

@MainActor
private struct Rig {
    let fx = DrawerFixture()
    let scratch = DrawerScratchDefaults()
    let fake = FakeDesktop()
    let service: DrawerService
    /// The Desktop is a path that does not exist, so a stray real listing would fail loudly.
    let desktop: String

    init(enabled: Bool = false) {
        let fx = self.fx, fake = self.fake
        desktop = fx.base.path + "/Nowhere/Desktop"
        if enabled { var s = DrawerSettings(); s.enabled = true; DrawerPrefs.save(s, to: scratch.defaults) }
        service = DrawerService(defaults: scratch.defaults, desktop: desktop, home: fx.home, store: fx.store,
                                turnDeskOn: {}, readFacts: { try fake.read($0) }, probeDesktop: { try fake.probe($0) })
    }
}

@MainActor
@Suite("The Desktop Folder permission")
struct DesktopPermissionTests {
    @Test("constructing and launching never read the Desktop; turning on probes it once, Sort reads it")
    func firstReadIsTheUsers() {
        let r = Rig(enabled: true)
        #expect(r.fake.calls.isEmpty && r.service.desktopPermission == .unknown)
        r.service.launch()
        #expect(r.fake.calls.isEmpty && r.service.desktopPermission == .unknown)
        r.service.turnOn()
        #expect(r.fake.calls == ["probe"] && r.service.desktopPermission == .granted)
        r.service.sort()
        #expect(r.fake.calls == ["probe", "read"])
    }

    @Test("with Drawers off, launch and Sort read nothing")
    func offReadsNothing() {
        let r = Rig()
        r.service.launch()
        #expect(r.service.sort() == nil)
        #expect(r.fake.calls.isEmpty && r.service.desktopPermission == .unknown)
    }

    @Test("Sort is the first read when Drawers was already on at launch")
    func sortIsFirstRead() {
        let r = Rig(enabled: true)
        r.service.launch()
        #expect(r.fake.calls.isEmpty)
        r.service.sort()
        #expect(r.fake.calls == ["read"] && r.service.desktopPermission == .granted)
    }

    @Test("EPERM and EACCES on Sort give denied, no review and no retry")
    func sortDenied() {
        for error in [eperm as Error, eacces as Error] {
            let r = Rig(enabled: true)
            r.fake.fail(with: error)
            #expect(r.service.sort() == nil)
            #expect(r.service.desktopPermission == .denied)
            #expect(r.service.pending == nil)
            #expect(r.fake.calls == ["read"], "one attempt, no loop")
        }
    }

    @Test("a denial found when turning on shows at once")
    func turnOnDenied() {
        let r = Rig()
        r.fake.fail(with: eperm)
        r.service.turnOn()
        #expect(r.service.settings.enabled && r.service.desktopPermission == .denied)
        #expect(r.fake.calls == ["probe"])
    }

    @Test("a later successful read clears denied to granted")
    func recovers() {
        let r = Rig(enabled: true)
        r.fake.fail(with: eacces)
        r.service.sort()
        #expect(r.service.desktopPermission == .denied)
        r.fake.fail(with: nil)
        r.service.sort()
        #expect(r.service.desktopPermission == .granted)
    }

    @Test("a failure that is not a refusal leaves the state alone")
    func otherFailure() {
        let r = Rig(enabled: true)
        r.fake.fail(with: POSIXError(.ENOENT))
        #expect(r.service.sort() == nil)
        #expect(r.service.desktopPermission == .unknown)
        r.fake.fail(with: eperm)
        r.service.sort()
        r.fake.fail(with: POSIXError(.ENOENT))
        r.service.sort()
        #expect(r.service.desktopPermission == .denied, "a missing folder is not proof the permission came back")
    }

    @Test("what counts as a denial")
    func denialShapes() {
        #expect(DesktopPermission.isDenial(eperm))
        #expect(DesktopPermission.isDenial(eacces))
        #expect(DesktopPermission.isDenial(NSError(domain: NSCocoaErrorDomain, code: NSFileReadNoPermissionError)))
        #expect(DesktopPermission.isDenial(NSError(domain: NSCocoaErrorDomain, code: NSFileReadUnknownError,
                                                   userInfo: [NSUnderlyingErrorKey: eperm])))
        #expect(!DesktopPermission.isDenial(POSIXError(.ENOENT)))
        #expect(!DesktopPermission.isDenial(NSError(domain: NSCocoaErrorDomain, code: NSFileReadNoSuchFileError)))
    }

    @Test("the real listing throws a denial for a folder that refuses, and lists one that doesn't")
    func realListing() throws {
        let fx = DrawerFixture()
        let locked = fx.base.path + "/Locked"
        try FileManager.default.createDirectory(atPath: locked, withIntermediateDirectories: true)
        fx.write("Locked/x.txt")
        #expect(try DrawerFacts.listing(locked).map(\.name) == ["x.txt"])
        guard chmod(locked, 0) == 0 else { return }
        defer { chmod(locked, 0o700) }
        // Skipped as root, which reads anything.
        guard geteuid() != 0 else { return }
        #expect(throws: (any Error).self) { try DrawerFacts.listing(locked) }
        do { _ = try DrawerFacts.listing(locked) } catch { #expect(DesktopPermission.isDenial(error)) }
    }

    @Test("the pane link and the panel line")
    func strings() {
        #expect(DesktopPermission.settingsURL.absoluteString
            == "x-apple.systempreferences:com.apple.preference.security?Privacy_FilesAndFolders")
        #expect(DrawerPanelNote.text(denied: true, entries: 0) == "Sanduhr can't see your Desktop.")
        #expect(DrawerPanelNote.text(denied: true, entries: 3) == nil)
        #expect(DrawerPanelNote.text(denied: false, entries: 0) == nil)
    }

    @Test("Settings: the row appears when denied, its button opens the pane, and a good Sort clears it")
    func settingsRow() {
        let r = Rig(enabled: true)
        final class Opened { var urls: [URL] = [] }
        let opened = Opened()
        let model = DrawersSettingsModel(service: r.service, tidy: {}, openURL: { opened.urls.append($0) })
        #expect(!model.desktopDenied)
        r.fake.fail(with: eperm)
        model.sort()
        #expect(model.desktopDenied)
        #expect(model.message == nil, "the row speaks, not the 'nothing matches' line")
        #expect(DrawersSettingsModel.deniedLine == "Sanduhr can't see your Desktop.")
        #expect(DrawersSettingsModel.deniedButton == "Open Files and Folders…")
        model.openFilesAndFolders()
        #expect(opened.urls == [DesktopPermission.settingsURL])
        r.fake.fail(with: nil)
        model.sort()
        #expect(!model.desktopDenied)
        #expect(r.fake.calls == ["read", "read"])
    }

    @Test("state.yaml reports the permission")
    func debugValue() {
        let r = Rig(enabled: true)
        #expect(DrawersDebug.of(r.service).permission == "unknown")
        r.fake.fail(with: eperm)
        r.service.sort()
        #expect(DrawersDebug.of(r.service).permission == "denied")
        r.fake.fail(with: nil)
        r.service.sort()
        #expect(DrawersDebug.of(r.service).permission == "granted")
    }
}
