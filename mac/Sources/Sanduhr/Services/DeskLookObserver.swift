import Foundation

/// Watches Desk's font, ink and shadow keys in the desk suite and calls back on the main queue
/// when any of them changes: from Desk's Settings, or from `defaults write` in a terminal. The
/// Match Desk theme follows Desk live through it.
final class DeskLookObserver: NSObject {
    private let defaults: UserDefaults
    private let onChange: () -> Void

    init(defaults: UserDefaults = .desk, onChange: @escaping () -> Void) {
        self.defaults = defaults
        self.onChange = onChange
        super.init()
        for key in DeskLook.keys {
            defaults.addObserver(self, forKeyPath: key, options: [], context: nil)
        }
    }

    deinit {
        for key in DeskLook.keys {
            defaults.removeObserver(self, forKeyPath: key)
        }
    }

    override func observeValue(forKeyPath keyPath: String?, of object: Any?,
                               change: [NSKeyValueChangeKey: Any]?,
                               context: UnsafeMutableRawPointer?) {
        DispatchQueue.main.async { [onChange] in onChange() }
    }
}
