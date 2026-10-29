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
    /// <param name="externalDir">
    /// The folder for files processes outside the app package read or write (spec, Plan B).
    /// Defaults to <c>%USERPROFILE%\.sanduhr</c>. When omitted but <paramref name="homeDir"/> is
    /// given it is <c>&lt;homeDir&gt;\.sanduhr</c>; when only <paramref name="appDataBase"/> is given
    /// it is <c>&lt;appDataBase&gt;\.sanduhr</c>, so a test that injects AppData never touches the
    /// real profile; with <c>SANDUHR_DEV_PROFILE</c> it is <c>&lt;profile&gt;\external</c>.
    /// </param>
    public Paths(string? appDataBase = null, string? homeDir = null, string? localAppDataBase = null, string? externalDir = null)
    {
        // Dev-tier override (never set by shipped config): SANDUHR_DEV_PROFILE names a
        // folder that stands in for both AppData bases, so a development build can run
        // beside the installed widget without touching its settings, snapshot, vault,
        // or themes. Credentials still come from Windows Credential Manager.
        string? devProfile = Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE");
        if (string.IsNullOrWhiteSpace(devProfile))
            devProfile = null;
        _appDataBase = appDataBase
            ?? (devProfile is null ? null : Path.Combine(devProfile, "roaming"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _homeDir = homeDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppDataBase = localAppDataBase
            ?? (devProfile is null ? null : Path.Combine(devProfile, "local"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _externalDir = externalDir
            ?? (homeDir is null ? null : Path.Combine(homeDir, ExternalDirName))
            ?? (appDataBase is null ? null : Path.Combine(appDataBase, ExternalDirName))
            ?? (devProfile is null ? null : Path.Combine(devProfile, "external"))
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ExternalDirName);
    }

    /// <summary>The external folder's name under the user profile.</summary>
    public const string ExternalDirName = ".sanduhr";

    private readonly string _externalDir;

    /// <summary>
    /// <c>%USERPROFILE%\.sanduhr</c>, created on first access: every file a process OUTSIDE the
    /// app package reads or writes (Claude Code, sanduhr-mcp, the statusline, the hooks, the meters
    /// mod). MSIX virtualizes the Store build's NEW files under AppData into the package's LocalCache,
    /// where those processes can't see them; writes outside AppData are not virtualized (spec, Plan B).
    /// One location for both builds. The folder is shared ground (other tools keep their own names
    /// in it), so Sanduhr only ever creates, reads and deletes its own names inside it.
    /// </summary>
    public string ExternalDir
    {
        get
        {
            Directory.CreateDirectory(_externalDir);
            return _externalDir;
        }
    }

    /// <summary>The external folder's path WITHOUT creating it (Reset, inventory, tests).</summary>
    public string ExternalDirPath => _externalDir;

    /// <summary>The projection of settings.json sanduhr-mcp reads (consent and publish readiness).</summary>
    public string McpSettingsFile => Path.Combine(ExternalDir, "mcp-settings.json");

    /// <summary>Before 3.5.0 the external files lived in <c>%APPDATA%\Sanduhr</c> itself. Read by the
    /// one-time migration and Reset only; nothing new is written there.</summary>
    public string LegacyStatuslineScriptFile => Path.Combine(AppDataDir, "bin", "sanduhr-statusline.ps1");

    /// <summary>The pre-3.5.0 MCP launcher (see <see cref="LegacyStatuslineScriptFile"/>).</summary>
    public string LegacyMcpLauncherFile => Path.Combine(AppDataDir, "bin", "sanduhr-mcp.cmd");

    /// <summary>The pre-3.5.0 meters mod root (see <see cref="LegacyStatuslineScriptFile"/>).</summary>
    public string LegacyModsRoot => Path.Combine(AppDataDir, "mods");

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

    /// <summary>The Desk Launcher's button images: <c>%APPDATA%\Sanduhr\launcher</c> (package storage on the Store
    /// build), PNGs named by a hash. Not auto-created; the import creates it when the first image arrives.</summary>
    public string LauncherDir => Path.Combine(AppDataDir, "launcher");

    /// <summary>The saved Desk designs: <c>%APPDATA%\Sanduhr\designs</c> (package storage on the Store build), one JSON file
    /// per design named by its id. Not auto-created; the first save creates it.</summary>
    public string DesignsDir => Path.Combine(AppDataDir, "designs");

    /// <summary>The user's saved song looks (spec, a look per song): <c>%APPDATA%\Sanduhr\now-playing-looks.json</c> (package storage on the
    /// Store build). Private to the widget; titles are stored here only for looks the user kept.</summary>
    public string NowPlayingLooksFile => Path.Combine(AppDataDir, NowPlayingLookProposal.StoreFile);

    /// <summary>Generated-chime cache folder: <c>%APPDATA%\Sanduhr\sounds</c>.
    /// Parity with <c>sounds._sounds_dir</c>. Not auto-created here — the player
    /// creates it lazily on first play.</summary>
    public string SoundsDir => Path.Combine(AppDataDir, "sounds");

    /// <summary>Shared fetch-transport browser profile:
    /// <c>%APPDATA%\Sanduhr\webview2-fetch</c>. Holds the ACTIVE account's
    /// claude.ai cookies on disk — account deletion purges it when no account
    /// remains (no future client init would ever wipe it otherwise).</summary>
    public string WebView2FetchDir => Path.Combine(AppDataDir, "webview2-fetch");

    /// <summary>An inactive account's own transport profile (account follow and seat meters):
    /// <c>webview2-&lt;account_ref&gt;</c> beside the shared one, deleted with the account.</summary>
    public string WebView2ProfileDir(string accountRef) => Path.Combine(AppDataDir, "webview2-" + accountRef);

    public string LogFile => Path.Combine(AppDataDir, "sanduhr.log");

    public string LastErrorFile => Path.Combine(AppDataDir, "last_error.json");

    /// <summary>Streamer mode's salt, codename maps and allowlist (Settings v2, slice 7). Only the app reads it, so it stays in AppData; it
    /// holds real names and is never sent anywhere.</summary>
    public string CodenamesFile => Path.Combine(AppDataDir, Codenames.FileName);

    /// <summary>Statusline/MCP rendezvous file (WS-E): the widget's active-account
    /// usage snapshot, written atomically each fetch when the Claude Code
    /// statusline integration is enabled. Read-only for every consumer.
    /// In <see cref="ExternalDir"/>: read from outside the package.</summary>
    public string SnapshotFile => Path.Combine(ExternalDir, "snapshot.json");

    /// <summary>Streamer mode's half for the statusline (Settings v2, slice 7): on, and folder ref to codename, never a real name. In
    /// <see cref="ExternalDir"/> because the statusline runs outside the package. Shared ground: only this name is Sanduhr's. Built on the
    /// folder path, not <see cref="ExternalDir"/>, so asking for the path never creates the shared folder.</summary>
    public string StreamerStateFile => Path.Combine(_externalDir, StreamerFile.FileName);

    /// <summary>Helper-script folder: <c>%USERPROFILE%\.sanduhr\bin</c>. OUTSIDE both
    /// package trees deliberately (WS-E) — the statusline script must survive an
    /// app update swap; the widget re-writes it on install and on start.
    /// Not auto-created; the installer creates it on first install.</summary>
    public string StatuslineBinDir => Path.Combine(ExternalDir, "bin");

    /// <summary>The installed statusline script path.</summary>
    public string StatuslineScriptFile => Path.Combine(StatuslineBinDir, "sanduhr-statusline.ps1");

    /// <summary>Usage-vault root: <c>%LOCALAPPDATA%\Sanduhr\vault</c>. LOCAL, not
    /// roaming, by design — enterprise roaming profiles must never sync the
    /// work-activity archive off the machine. Not auto-created; the VaultStore
    /// creates per-root dirs lazily on first write.</summary>
    public string VaultDir => Path.Combine(_localAppDataBase, "Sanduhr", "vault");

    /// <summary>Installed widget packs (spec 3.8, section 3): <c>%LOCALAPPDATA%\Sanduhr\widgets</c>, one folder per pack
    /// plus <c>widgets.json</c>. The app's own files (on the Store build the package virtualizes them, which is fine:
    /// nothing outside the app reads them). Not auto-created; the first install creates it.</summary>
    public string WidgetsDir => Path.Combine(_localAppDataBase, "Sanduhr", "widgets");

    /// <summary>The Desk companion's growth memory (<see cref="CompanionGrowthState"/>), in the widgets folder so Reset removes
    /// it with the packs.</summary>
    public string CompanionStateFile => Path.Combine(WidgetsDir, CompanionGrowthState.FileName);

    /// <summary>Sanduhr Time's day files (<c>%USERPROFILE%\.sanduhr\time\days</c>): another estate tool's folder, which the
    /// companion only reads (<see cref="SanduhrTimeDays"/>). Never created here.</summary>
    public string TimeDaysDir => Path.Combine(_externalDir, "time", "days");

    /// <summary>v1 plaintext config location — read-only, used for migration.</summary>
    public string LegacyConfigFile
        => Path.Combine(_homeDir, ".claude-usage-widget", "config.json");

    /// <summary>v1 history file — read during migration.</summary>
    public string LegacyHistoryFile
        => Path.Combine(_homeDir, ".claude-usage-widget", "history.json");
}
