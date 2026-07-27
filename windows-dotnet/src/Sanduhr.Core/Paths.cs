using System.Globalization;

namespace Sanduhr.Core;

/// <summary>
/// Filesystem location helpers for Sanduhr data files — the single source of
/// truth for where the app reads and writes on Windows. Ported 1:1 from
/// <c>paths.py</c>.
///
/// Parity note: the Python module reads <c>%APPDATA%</c> from the environment
/// (<c>os.environ["APPDATA"]</c>) and <c>Path.home()</c> for the v1 legacy
/// paths, and the tests redirect both via monkeypatch. The C# port keeps the
/// same defaults (the roaming-AppData folder + the user profile) but takes both
/// roots as constructor arguments so tests can inject temp directories instead
/// of mutating process-global environment state — safe under xUnit's parallel
/// test runner. App code constructs <c>new Paths()</c> and gets the real
/// locations; tests construct <c>new Paths(tempAppData, tempHome)</c>.
/// </summary>
public sealed class Paths
{
    private readonly string _appDataBase;
    private readonly string _homeDir;
    private readonly string _localAppDataBase;

    /// <param name="appDataBase">
    /// The roaming-AppData base (the parent of the <c>Sanduhr</c> folder).
    /// Defaults to <see cref="Environment.SpecialFolder.ApplicationData"/>
    /// (== <c>%APPDATA%</c>). Mirrors Python's <c>os.environ["APPDATA"]</c>.
    /// </param>
    /// <param name="homeDir">
    /// The user-home base used for the v1 legacy paths. Defaults to
    /// <see cref="Environment.SpecialFolder.UserProfile"/>. Mirrors Python's
    /// <c>Path.home()</c>.
    /// </param>
    /// <param name="localAppDataBase">
    /// The local (non-roaming) AppData base used for the usage vault. Defaults
    /// to <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// (== <c>%LOCALAPPDATA%</c>).
    /// </param>
    public Paths(string? appDataBase = null, string? homeDir = null, string? localAppDataBase = null)
    {
        _appDataBase = appDataBase
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _homeDir = homeDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppDataBase = localAppDataBase
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>
    /// Return <c>%APPDATA%\Sanduhr</c>, creating it on first access (parity with
    /// Python's <c>mkdir(parents=True, exist_ok=True)</c>).
    /// </summary>
    public string AppDataDir
    {
        get
        {
            var dir = Path.Combine(_appDataBase, "Sanduhr");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Legacy single-account history path. Read during migration only — new
    /// writes go to <see cref="HistoryFileFor"/>.
    /// </summary>
    public string HistoryFile => Path.Combine(AppDataDir, "history.json");

    /// <summary>Per-account history file: <c>history.{account}.json</c>.</summary>
    public string HistoryFileFor(string account)
        => Path.Combine(AppDataDir, string.Format(CultureInfo.InvariantCulture, "history.{0}.json", account));

    public string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    /// <summary>User theme drop-in folder: <c>%APPDATA%\Sanduhr\themes</c>.
    /// Parity with <c>settings_dialog._themes_dir</c> / <c>themes.load_user_themes</c>.
    /// Not auto-created here — the theme loader creates it on scan.</summary>
    public string ThemesDir => Path.Combine(AppDataDir, "themes");

    /// <summary>Generated-chime cache folder: <c>%APPDATA%\Sanduhr\sounds</c>.
    /// Parity with <c>sounds._sounds_dir</c>. Not auto-created here — the player
    /// creates it lazily on first play.</summary>
    public string SoundsDir => Path.Combine(AppDataDir, "sounds");

    /// <summary>Shared fetch-transport browser profile:
    /// <c>%APPDATA%\Sanduhr\webview2-fetch</c>. Holds the ACTIVE account's
    /// claude.ai cookies on disk — account deletion purges it when no account
    /// remains (no future client init would ever wipe it otherwise).</summary>
    public string WebView2FetchDir => Path.Combine(AppDataDir, "webview2-fetch");

    public string LogFile => Path.Combine(AppDataDir, "sanduhr.log");

    public string LastErrorFile => Path.Combine(AppDataDir, "last_error.json");

    /// <summary>Statusline/MCP rendezvous file (WS-E): the widget's active-account
    /// usage snapshot, written atomically each fetch when the Claude Code
    /// statusline integration is enabled. Read-only for every consumer.</summary>
    public string SnapshotFile => Path.Combine(AppDataDir, "snapshot.json");

    /// <summary>Helper-script folder: <c>%APPDATA%\Sanduhr\bin</c>. OUTSIDE both
    /// package trees deliberately (WS-E) — the statusline script must survive an
    /// app update swap; the widget re-writes it on install and on start.
    /// Not auto-created; the installer creates it on first install.</summary>
    public string StatuslineBinDir => Path.Combine(AppDataDir, "bin");

    /// <summary>The installed statusline script path.</summary>
    public string StatuslineScriptFile => Path.Combine(StatuslineBinDir, "sanduhr-statusline.ps1");

    /// <summary>Usage-vault root: <c>%LOCALAPPDATA%\Sanduhr\vault</c>. LOCAL, not
    /// roaming, by design — enterprise roaming profiles must never sync the
    /// work-activity archive off the machine. Not auto-created; the VaultStore
    /// creates per-root dirs lazily on first write.</summary>
    public string VaultDir => Path.Combine(_localAppDataBase, "Sanduhr", "vault");

    /// <summary>v1 plaintext config location — read-only, used for migration.</summary>
    public string LegacyConfigFile
        => Path.Combine(_homeDir, ".claude-usage-widget", "config.json");

    /// <summary>v1 history file — read during migration.</summary>
    public string LegacyHistoryFile
        => Path.Combine(_homeDir, ".claude-usage-widget", "history.json");
}
