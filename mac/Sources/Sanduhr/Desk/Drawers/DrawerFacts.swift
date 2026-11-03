import Foundation
import Darwin

/// The file system, as the mover needs to see it (docs/mac-drawers-setup-spec.md, sections 2 and 4).
/// Small and read-only: nothing here moves, makes or removes anything. Paths come in from the
/// caller, so a test points them at a temp folder and no default ever names a real one.
enum DrawerFS {
    /// `lstat`: the entry itself, never what a link points at.
    static func lstat(_ path: String) -> stat? {
        var st = stat()
        return Darwin.lstat(path, &st) == 0 ? st : nil
    }

    static func exists(_ path: String) -> Bool { lstat(path) != nil }

    static func isDirectory(_ st: stat) -> Bool { (st.st_mode & S_IFMT) == S_IFDIR }
    static func isRegular(_ st: stat) -> Bool { (st.st_mode & S_IFMT) == S_IFREG }
    static func isLink(_ st: stat) -> Bool { (st.st_mode & S_IFMT) == S_IFLNK }

    static func inode(_ st: stat) -> UInt64 { UInt64(st.st_ino) }

    static func modified(_ st: stat) -> Date {
        Date(timeIntervalSince1970: Double(st.st_mtimespec.tv_sec) + Double(st.st_mtimespec.tv_nsec) / 1e9)
    }

    /// The modification date of the entry itself, or nil when it is not there.
    static func modified(_ path: String) -> Date? { lstat(path).map(modified) }

    /// The path with every symlink followed (`realpath`), or nil when it does not exist.
    static func realPath(_ path: String) -> String? {
        guard let p = realpath(path, nil) else { return nil }
        defer { free(p) }
        return String(cString: p)
    }

    /// The real path of something that may not exist yet: the longest existing ancestor resolved,
    /// the rest appended. Normalized, so "." and ".." are gone.
    static func resolve(_ path: String) -> String? {
        guard let p = DrawerPaths.normalize(path) else { return nil }
        if let real = realPath(p) { return DrawerPaths.normalize(real) }
        if p == "/" { return nil }
        guard let up = resolve(DrawerPaths.parent(p)) else { return nil }
        return DrawerPaths.join(up, String(p.split(separator: "/").last ?? ""))
    }

    /// An entry's path with its folder resolved but the entry itself left alone, so a symlink stays
    /// visible as a symlink instead of standing in for its target.
    static func resolveEntry(_ path: String) -> String? {
        guard let p = DrawerPaths.normalize(path), p != "/" else { return nil }
        guard let dir = resolve(DrawerPaths.parent(p)) else { return nil }
        return DrawerPaths.join(dir, String(p.split(separator: "/").last ?? ""))
    }

    static func name(of path: String) -> String { String(path.split(separator: "/").last ?? "") }

    /// The volume a path lives on (its `st_dev`), reading the nearest ancestor that exists so a
    /// folder not made yet still has one. Used as the validator's volume input.
    static func volumeID(_ path: String) -> String {
        var p = DrawerPaths.normalize(path) ?? "/"
        while realPath(p) == nil && p != "/" { p = DrawerPaths.parent(p) }
        guard let real = realPath(p), let st = lstat(real) else { return "" }
        return String(st.st_dev)
    }

    /// The names in a folder, hidden ones included, or nil when it can't be read.
    static func names(in dir: String) -> [String]? {
        try? FileManager.default.contentsOfDirectory(atPath: dir)
    }
}

/// The Desktop Folder permission as Sanduhr can know it without private TCC calls
/// (docs/mac-drawers-setup-spec.md, section 6). It is read off the result of listing the Desktop:
/// a listing that works means granted, one refused with EPERM or EACCES means denied, and until
/// the Desktop has been read it is unknown. Kept in memory only; nothing about it is saved.
enum DesktopPermission: String, Equatable, Sendable {
    case unknown, granted, denied

    /// The Files and Folders pane of Privacy & Security, in the style the other panes use.
    static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_FilesAndFolders")!

    /// True when a failed read was the system refusing access: EPERM or EACCES, as a POSIX error,
    /// as Foundation's "no permission to read" (257), or behind an underlying error of either.
    static func isDenial(_ error: Error) -> Bool {
        let ns = error as NSError
        if ns.domain == NSPOSIXErrorDomain, ns.code == Int(EPERM) || ns.code == Int(EACCES) { return true }
        if ns.domain == NSCocoaErrorDomain, ns.code == NSFileReadNoPermissionError { return true }
        if let under = ns.userInfo[NSUnderlyingErrorKey] as? Error { return isDenial(under) }
        return false
    }
}

/// Reads the facts the rules look at (`DrawerItemFacts`) for real entries. The caller names the
/// folder; nothing here knows where the Desktop is.
enum DrawerFacts {
    /// macOS sets this on screenshots and screen recordings, in any language.
    static let screenCaptureAttribute = "com.apple.metadata:kMDItemIsScreenCapture"
    /// `SF_DATALESS` from `<sys/stat.h>`: the item is an iCloud placeholder.
    static let datalessFlag: UInt32 = 0x4000_0000

    /// Facts for one entry, or nil when it is not there. Size and date are the entry's own
    /// (`lstat`), so a link reports itself.
    static func read(_ path: String) -> DrawerItemFacts? {
        guard let st = DrawerFS.lstat(path) else { return nil }
        let keys: Set<URLResourceKey> = [.isPackageKey, .isHiddenKey, .isSymbolicLinkKey, .isAliasFileKey,
                                         .ubiquitousItemDownloadingStatusKey]
        let values = try? URL(fileURLWithPath: path).resourceValues(forKeys: keys)
        return DrawerItemFacts(
            name: DrawerFS.name(of: path),
            isDirectory: DrawerFS.isDirectory(st),
            isPackage: values?.isPackage ?? false,
            isHidden: values?.isHidden ?? false,
            isSymlink: DrawerFS.isLink(st) || (values?.isSymbolicLink ?? false),
            isAlias: values?.isAliasFile ?? false,
            isDataless: isDataless(st, values?.ubiquitousItemDownloadingStatus),
            isScreenCapture: isScreenCapture(path),
            size: Int64(st.st_size),
            modified: DrawerFS.modified(st))
    }

    /// Facts for every entry in `dir`, hidden ones too (the rules decide what to skip), by name.
    static func list(_ dir: String) -> [DrawerItemFacts] {
        (try? listing(dir)) ?? []
    }

    /// The same, but a folder that can't be listed throws, so the caller can tell a refusal
    /// (`DesktopPermission.isDenial`) from an empty folder.
    static func listing(_ dir: String) throws -> [DrawerItemFacts] {
        try FileManager.default.contentsOfDirectory(atPath: dir).sorted().compactMap { read(DrawerPaths.join(dir, $0)) }
    }

    /// Lists a folder's names and drops them: the cheapest read that raises the Desktop prompt or
    /// shows a denial. Used when Drawers is turned on.
    static func probe(_ dir: String) throws {
        _ = try FileManager.default.contentsOfDirectory(atPath: dir)
    }

    /// An iCloud placeholder: the stat flag, or a ubiquitous item that is not fully downloaded.
    /// Either one is enough, since the flag is newer than the resource key on some systems.
    static func isDataless(_ st: stat, _ status: URLUbiquitousItemDownloadingStatus? = nil) -> Bool {
        if st.st_flags & datalessFlag != 0 { return true }
        if let status { return status != .current }
        return false
    }

    /// The screen-capture attribute, read without following a link. Present counts as true unless
    /// its value is a property list that says false or 0.
    static func isScreenCapture(_ path: String) -> Bool {
        let size = getxattr(path, screenCaptureAttribute, nil, 0, 0, XATTR_NOFOLLOW)
        guard size > 0 else { return false }
        var buffer = [UInt8](repeating: 0, count: size)
        let got = getxattr(path, screenCaptureAttribute, &buffer, size, 0, XATTR_NOFOLLOW)
        guard got > 0 else { return false }
        let data = Data(buffer.prefix(got))
        if let value = try? PropertyListSerialization.propertyList(from: data, format: nil) {
            if let n = value as? NSNumber { return n.intValue != 0 }
        }
        return true
    }
}
