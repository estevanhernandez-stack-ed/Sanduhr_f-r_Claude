using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One Claude Code folder on the Claude Code page: its path, the account it is linked to and the
/// MCP read switch.</summary>
public sealed partial class CcFolderRowViewModel : ObservableObject
{
    private readonly ClaudeCodeViewModel _owner;
    private bool _loading = true;

    internal CcFolder Folder { get; }
    public string Path => Folder.Path;
    public string Name => Folder.Name;
    public bool Discovered => Folder.Discovered;
    public bool IsAdded => !Folder.Discovered;

    /// <summary>The path with the profile folder written as ~.</summary>
    public string Display { get; }

    [ObservableProperty] private string _linkedAccount = ClaudeCodeViewModel.NoAccount;
    [ObservableProperty] private bool _mcpReadable;

    internal CcFolderRowViewModel(ClaudeCodeViewModel owner, CcFolder folder, string display, string linked, bool mcpReadable)
    {
        _owner = owner;
        Folder = folder;
        Display = display;
        _linkedAccount = linked;
        _mcpReadable = mcpReadable;
        _loading = false;
    }

    partial void OnLinkedAccountChanged(string value)
    {
        if (!_loading) _owner.OnLinkChanged(this, value);
    }

    partial void OnMcpReadableChanged(bool value)
    {
        if (!_loading) _owner.OnMcpReadableChanged(this, value);
    }

    internal void SetLinkedQuietly(string value)
    {
        _loading = true;
        LinkedAccount = value;
        _loading = false;
    }

    public string AutomationName => $"Folder {Display}";

    /// <summary>The account choices (the page's list), bound directly so the combo never depends on
    /// a RelativeSource walk to the page.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> AccountOptions => _owner.AccountOptions;
    public bool LinkEnabled => _owner.LinkEnabled;
    public System.Windows.Input.ICommand ForgetCommand => _owner.ForgetFolderCommand;
}

/// <summary>One Sanduhr piece for the selected folder: what it is, where it stands, and its buttons.</summary>
public sealed partial class CcPieceViewModel : ObservableObject
{
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty] private string _statusText = "";
    /// <summary>Sanduhr's entry is in the folder (current or not): Remove shows.</summary>
    [ObservableProperty] private bool _isInstalled;
    /// <summary>The piece can be installed in the selected folder.</summary>
    [ObservableProperty] private bool _canInstall = true;
    [ObservableProperty] private string _installLabel = "Install…";

    public IRelayCommand InstallCommand { get; }
    public IRelayCommand RemoveCommand { get; }
    public string InstallName { get; }
    public string RemoveName { get; }

    public CcPieceViewModel(string title, string description, Action install, Action remove)
    {
        Title = title;
        Description = description;
        InstallCommand = new RelayCommand(install);
        RemoveCommand = new RelayCommand(remove);
        InstallName = $"Install {title}";
        RemoveName = $"Remove {title}";
    }

    public bool NotInstalled => !IsInstalled;
    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(NotInstalled));
}

/// <summary>
/// The Claude Code page: the folders Sanduhr knows (found, then added by hand), the accounts linked to them,
/// and Sanduhr's four pieces in a chosen folder (statusline, MCP server, meters above the prompt, glow hooks)
/// with the glow switches. Every write to a folder waits for a consent dialog's confirm button and goes only
/// to the folder the person picked. The installers are the Core ones; this class chooses nothing silently.
///
/// <para><c>statusline_enabled</c> keeps its key and now means "a Claude Code piece that reads
/// <c>snapshot.json</c> is installed": true when the statusline or the meters mod is installed in any known
/// folder, false when none remains. It is recomputed after every install or remove and when the page loads;
/// <see cref="WidgetViewModel.SaveStatuslineEnabled"/> writes the snapshot while it is true and deletes it
/// when it turns false.</para>
/// </summary>
public sealed partial class ClaudeCodeViewModel : ObservableObject
{
    /// <summary>The combo's "no account" choice.</summary>
    public const string NoAccount = "None";

    private readonly WidgetViewModel _widget;
    private Window? _owner;
    private bool _loading;

    public ObservableCollection<CcFolderRowViewModel> Folders { get; } = new();
    public ObservableCollection<string> AccountOptions { get; } = new();
    public ObservableCollection<CcFolderRowViewModel> FolderChoices => Folders;

    public CcPieceViewModel Statusline { get; }
    public CcPieceViewModel Mcp { get; }
    public CcPieceViewModel Meters { get; }
    public CcPieceViewModel Hooks { get; }

    [ObservableProperty] private CcFolderRowViewModel? _selectedFolder;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasFolders;
    public bool HasNoFolders => !HasFolders;
    partial void OnHasFoldersChanged(bool value) => OnPropertyChanged(nameof(HasNoFolders));
    [ObservableProperty] private bool _linkEnabled = true;

    [ObservableProperty] private bool _glowWaiting;
    [ObservableProperty] private bool _glowDone;
    [ObservableProperty] private bool _glowSkipTerminal = true;

    /// <summary>Raised after folders were added or forgotten, so Mods &amp; Config follows.</summary>
    public event Action? FoldersChanged;
    /// <summary>Raised after an install or remove, so Mods &amp; Config re-reads the folder.</summary>
    public event Action? PiecesChanged;

    /// <summary>Set by App: tells the glow service to re-read the switches.</summary>
    public Action? GlowReload { get; set; }
    /// <summary>Set by App: plays the glow once ("Try it").</summary>
    public Action<ClaudeCodeEvent>? GlowPreview { get; set; }

    public ClaudeCodeViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        Statusline = new CcPieceViewModel("Statusline",
            "Shows your session and weekly meters under the Claude Code prompt.",
            InstallStatusline, RemoveStatusline);
        Mcp = new CcPieceViewModel("MCP server",
            "Lets Claude Code ask for your live headroom before it spawns agents. Read-only.",
            InstallMcp, RemoveMcp);
        Meters = new CcPieceViewModel("Meters above the prompt",
            "A mod that draws your meters in a band above the prompt.",
            InstallMeters, RemoveMeters);
        Hooks = new CcPieceViewModel("Glow when Claude needs you",
            "Two hooks that light the edge of your window when Claude Code waits on you or finishes.",
            InstallHooks, RemoveHooks);
        Reload();
        _loading = true;
        var glow = _widget.LoadClaudeCodeGlow();
        GlowWaiting = glow.Waiting;
        GlowDone = glow.Done;
        GlowSkipTerminal = glow.SkipTerminal;
        var target = _glowStore.LoadGlowTarget();
        GlowTarget = GlowTargets.First(c => c.Kind == target.Effective);
        GlowSide = GlowSides.First(c => c.Side == target.Side);
        GlowMonitors = MonitorChoices();
        GlowMonitor = GlowMonitors.FirstOrDefault(m => string.Equals(m.Device, target.Monitor, StringComparison.OrdinalIgnoreCase))
                      ?? GlowMonitors[0];
        _loading = false;
    }

    // ------------------------------------------------------------------ where the glow lands (spec 6.8)

    private readonly SettingsStore _glowStore = new(new Paths());

    public sealed record GlowTargetChoice(GlowTargetKind Kind, string Name) { public override string ToString() => Name; }
    public sealed record GlowSideChoice(GlowEdgeSide Side, string Name) { public override string ToString() => Name; }
    public sealed record GlowMonitorChoice(string? Device, string Name) { public override string ToString() => Name; }

    /// <summary>The offered targets (the Desk spot waits for S7 and is never listed).</summary>
    public static IReadOnlyList<GlowTargetChoice> GlowTargets { get; } =
    [
        new(GlowTargetKind.Window, "At the top of the window you're working in"),
        new(GlowTargetKind.Edge, "Along an edge of a screen"),
        new(GlowTargetKind.Widget, "Only flash Sanduhr in the taskbar"),
    ];

    public static IReadOnlyList<GlowSideChoice> GlowSides { get; } =
        [new(GlowEdgeSide.Top, "Top"), new(GlowEdgeSide.Bottom, "Bottom"), new(GlowEdgeSide.Left, "Left"), new(GlowEdgeSide.Right, "Right")];

    [ObservableProperty] private GlowTargetChoice _glowTarget = GlowTargets[0];
    [ObservableProperty] private GlowSideChoice _glowSide = GlowSides[0];
    [ObservableProperty] private IReadOnlyList<GlowMonitorChoice> _glowMonitors = [new(null, "Main monitor")];
    [ObservableProperty] private GlowMonitorChoice? _glowMonitor;

    public bool GlowOnEdge => GlowTarget.Kind == GlowTargetKind.Edge;

    private static IReadOnlyList<GlowMonitorChoice> MonitorChoices()
    {
        var list = new List<GlowMonitorChoice> { new(null, "Main monitor") };
        try
        {
            int n = 1;
            foreach (var m in Sanduhr.App.Interop.DeskNative.Monitors())
                list.Add(new(m.Device, $"Monitor {n++}{(m.Primary ? " (main)" : "")}, {m.Bounds.Width}x{m.Bounds.Height}"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return list;
    }

    partial void OnGlowTargetChanged(GlowTargetChoice value)
    {
        OnPropertyChanged(nameof(GlowOnEdge));
        SaveGlowTarget();
    }

    partial void OnGlowSideChanged(GlowSideChoice value) => SaveGlowTarget();
    partial void OnGlowMonitorChanged(GlowMonitorChoice? value) => SaveGlowTarget();

    private void SaveGlowTarget()
    {
        if (_loading) return;
        _glowStore.SaveGlowTarget(new GlowTargetSettings(GlowTarget.Kind, GlowSide.Side, GlowMonitor?.Device));
        GlowReload?.Invoke();
    }

    public void AttachOwner(Window owner) => _owner = owner;

    // ------------------------------------------------------------------ folders

    private string Profile => ClaudeCodeProfile.Dir;

    private ClaudeCodeIntegrations MakeIntegrations() => new(
        Profile, _widget.SanduhrAppDataDir, Path.Combine(AppContext.BaseDirectory, "mods", ClaudeCodeIntegrations.ModName));

    private StatuslineInstaller MakeStatusline() => new(Profile, _widget.StatuslineBinDir, null, _widget.SanduhrAppDataDir);

    private McpIntegrationInstaller MakeMcp() => new(Profile, _widget.SanduhrAppDataDir);

    private static string McpSourceDir => Path.Combine(AppContext.BaseDirectory, "mcp");

    /// <summary>Every known folder, found first and added second.</summary>
    internal IReadOnlyList<CcFolder> KnownFolders() =>
        ClaudeCodeFolders.Build(Profile, ClaudeCodeProfile.ConfigDir, _widget.LoadCcFoldersAdded());

    /// <summary>Re-reads the folder list, the account choices and every status.</summary>
    public void Reload()
    {
        var keep = SelectedFolder?.Path;
        _loading = true;
        AccountOptions.Clear();
        AccountOptions.Add(NoAccount);
        foreach (var label in _widget.ShownAccounts)
            AccountOptions.Add(label);
        LinkEnabled = !_widget.IsDemo;

        var roots = _widget.LoadMcpRoots();
        Folders.Clear();
        foreach (var f in KnownFolders())
        {
            var linked = _widget.ShownAccounts.FirstOrDefault(l =>
                _widget.AccountFolder(l) is { } p && ClaudeCodeFolders.Same(p, f.Path)) ?? NoAccount;
            Folders.Add(new CcFolderRowViewModel(this, f, ClaudeCodeFolders.Display(f.Path, Profile), linked,
                f.Discovered && roots.GetValueOrDefault(f.Name)));
        }
        HasFolders = Folders.Count > 0;
        SelectedFolder = Folders.FirstOrDefault(r => keep is not null && ClaudeCodeFolders.Same(r.Path, keep))
                         ?? Folders.FirstOrDefault();
        _loading = false;
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: false);
    }

    partial void OnSelectedFolderChanged(CcFolderRowViewModel? value)
    {
        if (!_loading) RefreshStatuses();
    }

    [RelayCommand]
    private void AddFolder()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose a Claude Code folder",
                Multiselect = false,
            };
            if (dialog.ShowDialog(_owner) != true) return;
            var path = ClaudeCodeFolders.Norm(dialog.FolderName);
            if (path.Length == 0) return;
            if (Folders.Any(f => ClaudeCodeFolders.Same(f.Path, path)))
            {
                StatusText = "That folder is already in the list.";
                return;
            }
            var added = _widget.LoadCcFoldersAdded().ToList();
            added.Add(path);
            _widget.SaveCcFoldersAdded(added);
            Reload();
            SelectedFolder = Folders.FirstOrDefault(f => ClaudeCodeFolders.Same(f.Path, path)) ?? SelectedFolder;
            StatusText = "Folder added. Sanduhr changes nothing in it until you install a piece.";
            FoldersChanged?.Invoke();
        }
        catch (Exception)
        {
            StatusText = "Couldn't add that folder.";
            Sounds.PlayError();
        }
    }

    /// <summary>Drops a folder that was added by hand. Nothing in the folder changes: Sanduhr's pieces stay
    /// where they are until you remove them.</summary>
    [RelayCommand]
    private void ForgetFolder(CcFolderRowViewModel? row)
    {
        if (row is null || row.Discovered) return;
        var left = _widget.LoadCcFoldersAdded().Where(p => !ClaudeCodeFolders.Same(p, row.Path)).ToList();
        _widget.SaveCcFoldersAdded(left);
        Reload();
        StatusText = "Folder forgotten. Anything Sanduhr installed in it stays until you remove it.";
        FoldersChanged?.Invoke();
    }

    internal void OnLinkChanged(CcFolderRowViewModel row, string account)
    {
        try
        {
            // One folder per account, and one account per folder.
            foreach (var label in _widget.ShownAccounts)
                if (_widget.AccountFolder(label) is { } p && ClaudeCodeFolders.Same(p, row.Path) && label != account)
                    _widget.SetAccountFolder(label, null);
            if (account != NoAccount)
            {
                _widget.SetAccountFolder(account, row.Path);
                foreach (var other in Folders.Where(f => !ReferenceEquals(f, row) && f.LinkedAccount == account))
                    other.SetLinkedQuietly(NoAccount);
            }
        }
        catch (Exception)
        {
            StatusText = "Couldn't save that link.";
        }
    }

    internal void OnMcpReadableChanged(CcFolderRowViewModel row, bool on)
    {
        // Consent is keyed by a discovered folder's name; the server never builds a path from it.
        if (!row.Discovered) return;
        try
        {
            var map = _widget.LoadMcpRoots().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            map[row.Name] = on;
            _widget.SaveMcpRoots(map);
        }
        catch (Exception)
        {
            // The server treats a root it can't find consent for as not consented.
        }
    }

    // ------------------------------------------------------------------ statuses

    private string Shorten(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Replace(Profile, "~", StringComparison.OrdinalIgnoreCase);

    private static string StatusWords(IntegrationStatus s) => s switch
    {
        IntegrationStatus.Installed => "Installed.",
        IntegrationStatus.Outdated => "Installed, but not the current version. Install again to update it.",
        IntegrationStatus.Unreadable => "This folder's settings.json can't be read, so nothing is changed.",
        _ => "Not installed.",
    };

    /// <summary>Recomputes the four statuses for the selected folder.</summary>
    public void RefreshStatuses()
    {
        var row = SelectedFolder;
        if (row is null)
        {
            foreach (var p in new[] { Statusline, Mcp, Meters, Hooks })
            {
                p.StatusText = "Add a folder to install here.";
                p.IsInstalled = false;
                p.CanInstall = false;
            }
            return;
        }
        try
        {
            var statusline = MakeStatusline();
            var sl = statusline.Status(row.Path);
            Statusline.CanInstall = sl != StatuslineStatus.Unreadable;
            Statusline.IsInstalled = sl is StatuslineStatus.Installed or StatuslineStatus.Outdated;
            Statusline.InstallLabel = Statusline.IsInstalled ? "Change…" : "Install…";
            Statusline.StatusText = sl switch
            {
                StatuslineStatus.Installed => "Installed.",
                StatuslineStatus.Outdated => "Installed, but not the current version. Change it to update.",
                StatuslineStatus.Other => "This folder has another statusline. Install offers to replace it or add to it.",
                StatuslineStatus.Unreadable => "This folder's settings.json can't be read, so nothing is changed.",
                _ => "Not installed.",
            };

            if (row.Discovered)
            {
                bool registered = MakeMcp().IsRegistered(row.Name);
                Mcp.CanInstall = true;
                Mcp.IsInstalled = registered;
                Mcp.StatusText = registered ? "Installed." : "Not installed.";
            }
            else
            {
                Mcp.CanInstall = false;
                Mcp.IsInstalled = false;
                Mcp.StatusText = "Not available for folders added by hand.";
            }

            var integ = MakeIntegrations();
            var meters = integ.Status(IntegrationKind.Meters, row.Path);
            Meters.IsInstalled = meters is IntegrationStatus.Installed or IntegrationStatus.Outdated;
            Meters.CanInstall = meters != IntegrationStatus.Unreadable;
            Meters.StatusText = StatusWords(meters);
            var hooks = integ.Status(IntegrationKind.Hooks, row.Path);
            Hooks.IsInstalled = hooks is IntegrationStatus.Installed or IntegrationStatus.Outdated;
            Hooks.CanInstall = hooks != IntegrationStatus.Unreadable;
            Hooks.StatusText = StatusWords(hooks);
        }
        catch (Exception)
        {
            StatusText = "Couldn't read this folder.";
        }
    }

    /// <summary>
    /// Keeps <c>statusline_enabled</c> true while the statusline or the meters mod is installed in any known
    /// folder, and false when none remains. With <paramref name="forceWrite"/> a true value also rewrites the
    /// snapshot from the last fetch, so a piece installed just now has numbers at its next refresh.
    /// </summary>
    private void SyncSnapshotConsumers(bool forceWrite)
    {
        try
        {
            var statusline = MakeStatusline();
            var integ = MakeIntegrations();
            // An unreadable folder is unknown, not empty, and a receipt outlives a folder dropped from the
            // list: only "nothing anywhere" turns the snapshot off and deletes the script.
            var states = new List<ConsumerState>();
            foreach (var f in KnownFolders())
            {
                states.Add(SnapshotConsumers.Of(statusline.Status(f.Path)));
                states.Add(SnapshotConsumers.Of(integ.Status(IntegrationKind.Meters, f.Path)));
            }
            var (receipts, unknown) = SnapshotConsumers.ReadReceipts(integ.ReceiptsPath);
            var decision = SnapshotConsumers.Decide(states, receipts.Select(r => r.Kind), unknown);
            if (decision == SnapshotDecision.Keep)
                return;
            bool any = decision == SnapshotDecision.On;
            if (any != _widget.LoadStatuslineEnabled() || (any && forceWrite))
                _widget.SaveStatuslineEnabled(any);
            if (!any)
                statusline.RemoveScript();
        }
        catch (Exception)
        {
            // The snapshot switch is housekeeping; the install already reported its own outcome.
        }
    }

    private void Done(string message, bool ok = true)
    {
        StatusText = message;
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: ok);
        PiecesChanged?.Invoke();
        if (ok) Sounds.PlaySaveConfirmation(); else Sounds.PlayError();
    }

    // ------------------------------------------------------------------ statusline

    private void InstallStatusline()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            var installer = MakeStatusline();
            var status = installer.Status(row.Path);
            if (status == StatuslineStatus.Unreadable)
            {
                Done("This folder's settings.json can't be read, so nothing was changed.", ok: false);
                return;
            }
            string? theirs = status == StatuslineStatus.Other && installer.TheirCommand(row.Path) is { } cmd
                ? ConfigMask.MaskString(cmd) : null;
            var choice = IntegrationConsentDialog.Ask(_owner, new ConsentSpec(
                "Show usage under the Claude Code prompt?",
                "Sanduhr adds a statusline with your live session and weekly meters, right where the tokens burn.",
                row.Display,
                "What gets written: the render script at %APPDATA%\\Sanduhr\\bin, a statusLine entry in this folder's "
                + "settings.json (a copy of the file is saved under %APPDATA%\\Sanduhr\\backups first), and a usage "
                + "snapshot at %APPDATA%\\Sanduhr\\snapshot.json with percentages and reset times only, never your "
                + "account name or keys. Remove brings back the statusline that was here before.",
                "Install", theirs, OfferSegments: true));
            if (choice is null) return;
            if (!installer.InstallScript())
            {
                Done("Couldn't write the statusline script.", ok: false);
                return;
            }
            bool ok = installer.Install(row.Path, choice.Mode, choice.Segments, choice.Join);
            Done(ok ? "Statusline installed. It shows at Claude Code's next refresh."
                    : "Couldn't safely edit settings.json, so nothing was changed.", ok);
        }
        catch (Exception)
        {
            Done("Install failed.", ok: false);
        }
    }

    private void RemoveStatusline()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            bool ok = MakeStatusline().Remove(row.Path);
            Done(ok ? "Statusline removed." : "Couldn't edit settings.json. Delete its statusLine entry by hand.", ok);
        }
        catch (Exception)
        {
            Done("Remove failed.", ok: false);
        }
    }

    // ------------------------------------------------------------------ MCP server

    private void InstallMcp()
    {
        if (SelectedFolder is not { Discovered: true } row) return;
        try
        {
            var installer = MakeMcp();
            if (!Directory.Exists(McpSourceDir))
            {
                Done("The server files are missing from this build. Reinstall Sanduhr.", ok: false);
                return;
            }
            var consent = McpConsentDialog.ShowConsent(
                _owner, [row.Name], null, installer.LauncherPath, installer.ConfigPathFor);
            if (consent is null) return;
            if (installer.RefreshInstall(McpSourceDir) is null)
            {
                Done("Couldn't copy the server files.", ok: false);
                return;
            }
            var reg = installer.Register(row.Name);
            if (!reg.Ok)
            {
                Done("Couldn't safely edit this folder's .claude.json, so nothing was changed.", ok: false);
                return;
            }
            _widget.SaveMcpCcHome(row.Name);
            var map = _widget.LoadMcpRoots().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var (name, on) in consent.Roots)
                map[name] = on;
            _widget.SaveMcpRoots(map);
            _widget.SaveMcpEnabled(true);
            Reload();
            Done("MCP server installed. New Claude Code sessions pick it up.");
        }
        catch (Exception)
        {
            Done("Install failed.", ok: false);
        }
    }

    private void RemoveMcp()
    {
        if (SelectedFolder is not { Discovered: true } row) return;
        try
        {
            var installer = MakeMcp();
            bool ok = installer.Deregister(row.Name);
            bool anyLeft = KnownFolders().Any(f => f.Discovered && installer.IsRegistered(f.Name));
            if (!anyLeft && ok)
                installer.RemoveIntegrationFiles();
            _widget.SaveMcpEnabled(anyLeft);
            Done(ok ? "MCP server removed."
                    : "Couldn't edit .claude.json. Delete its mcpServers.sanduhr entry by hand.", ok);
        }
        catch (Exception)
        {
            Done("Remove failed.", ok: false);
        }
    }

    // ------------------------------------------------------------------ meters mod

    private void InstallMeters()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            var choice = IntegrationConsentDialog.Ask(_owner, new ConsentSpec(
                "Draw your meters above the Claude Code prompt?",
                "Sanduhr adds a mod that draws your session and weekly meters in a band above the prompt.",
                row.Display,
                "What gets written: the mod's folder, copied under %APPDATA%\\Sanduhr\\mods, and one entry in this "
                + "folder's settings.json under env, CLAUDE_CODE_PLUGIN_DIRS. The mod reads only Sanduhr's snapshot "
                + "file. Remove takes out only Sanduhr's entry and leaves any other folder in that list alone.",
                "Install"));
            if (choice is null) return;
            var res = MakeIntegrations().Install(IntegrationKind.Meters, row.Path);
            Done(res.Ok ? "Meters installed. New Claude Code sessions show them."
                        : Shorten(res.Message is { Length: > 0 } m ? m : "Couldn't install the meters."), res.Ok);
        }
        catch (Exception)
        {
            Done("Install failed.", ok: false);
        }
    }

    private void RemoveMeters()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            var res = MakeIntegrations().Remove(IntegrationKind.Meters, row.Path);
            Done(res.Ok ? "Meters removed." : Shorten(res.Message is { Length: > 0 } m ? m : "Couldn't remove the meters."), res.Ok);
        }
        catch (Exception)
        {
            Done("Remove failed.", ok: false);
        }
    }

    // ------------------------------------------------------------------ glow hooks

    private void InstallHooks()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            var choice = IntegrationConsentDialog.Ask(_owner, new ConsentSpec(
                "Glow when Claude Code needs you?",
                "Sanduhr lights the top edge of your window when Claude Code waits on you or finishes.",
                row.Display,
                "What gets written: one Notification hook and one Stop hook in this folder's settings.json. Each only "
                + "touches a signal file under %APPDATA%\\Sanduhr. Nothing from Claude Code is passed on or read. "
                + "Remove takes out only those two hooks.",
                "Install"));
            if (choice is null) return;
            var res = MakeIntegrations().Install(IntegrationKind.Hooks, row.Path);
            Done(res.Ok ? "Glow hooks installed. Turn the glow on below."
                        : Shorten(res.Message is { Length: > 0 } m ? m : "Couldn't install the hooks."), res.Ok);
        }
        catch (Exception)
        {
            Done("Install failed.", ok: false);
        }
    }

    private void RemoveHooks()
    {
        if (SelectedFolder is not { } row) return;
        try
        {
            var res = MakeIntegrations().Remove(IntegrationKind.Hooks, row.Path);
            Done(res.Ok ? "Glow hooks removed." : Shorten(res.Message is { Length: > 0 } m ? m : "Couldn't remove the hooks."), res.Ok);
        }
        catch (Exception)
        {
            Done("Remove failed.", ok: false);
        }
    }

    // ------------------------------------------------------------------ glow switches

    partial void OnGlowWaitingChanged(bool value) => SaveGlow();
    partial void OnGlowDoneChanged(bool value) => SaveGlow();
    partial void OnGlowSkipTerminalChanged(bool value) => SaveGlow();

    private void SaveGlow()
    {
        if (_loading) return;
        _widget.SaveClaudeCodeGlow(new GlowSwitches(GlowWaiting, GlowDone, GlowSkipTerminal));
        GlowReload?.Invoke();
    }

    [RelayCommand]
    private void TryWaiting() => GlowPreview?.Invoke(ClaudeCodeEvent.Waiting);

    [RelayCommand]
    private void TryDone() => GlowPreview?.Invoke(ClaudeCodeEvent.Done);

    // ------------------------------------------------------------------ reset

    /// <summary>
    /// Reset Sanduhr…: undoes every receipt (meters mod, glow hooks), takes Sanduhr's statusline out of each
    /// folder where it is still Sanduhr's, and deregisters the MCP server. Returns the kinds of failures,
    /// never a path. Safe to call off the UI thread.
    /// </summary>
    public static IReadOnlyList<string> UndoEverything(WidgetViewModel widget)
    {
        var failures = new List<string>();
        var profile = ClaudeCodeProfile.Dir;
        var sanduhrDir = widget.SanduhrAppDataDir;
        var folders = ClaudeCodeFolders.Build(profile, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded());

        try
        {
            var integ = new ClaudeCodeIntegrations(profile, sanduhrDir);
            if (integ.UndoAll().Any(r => !r.Ok))
                failures.Add("a Claude Code entry");
        }
        catch (Exception) { failures.Add("a Claude Code entry"); }

        var statusline = new StatuslineInstaller(profile, widget.StatuslineBinDir, null, sanduhrDir);
        // Known folders first, then any folder a receipt names that is no longer in the list, so Reset
        // never leaves an orphan statusLine. Each folder once.
        var statuslineFolders = folders.Select(f => f.Path).ToList();
        try
        {
            var (receipts, _) = SnapshotConsumers.ReadReceipts(
                Path.Combine(sanduhrDir, "integrations", "installs.json"));
            foreach (var folder in SnapshotConsumers.StatuslineReceiptFolders(receipts))
                if (!statuslineFolders.Any(p => ClaudeCodeFolders.Same(p, folder)))
                    statuslineFolders.Add(folder);
        }
        catch (Exception) { failures.Add("a statusline entry"); }
        foreach (var folder in statuslineFolders)
        {
            try
            {
                // A rooted second argument to Path.Combine returns it, so a full folder path works as the home name.
                if (statusline.IsRegistered(folder) && !statusline.Remove(folder))
                    failures.Add("a statusline entry");
            }
            catch (Exception) { failures.Add("a statusline entry"); }
        }
        try { statusline.RemoveScript(); } catch (Exception) { failures.Add("the statusline script"); }

        try
        {
            var mcp = new McpIntegrationInstaller(profile, sanduhrDir);
            var homes = folders.Where(f => f.Discovered).Select(f => f.Name)
                .Concat(widget.LoadMcpCcHome() is { Length: > 0 } saved ? [saved] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase);
            // A failed deregister keeps the launcher and server files, as the reset promises.
            if (!mcp.UndoAll(homes))
                failures.Add("an MCP server entry");
        }
        catch (Exception) { failures.Add("an MCP server entry"); }

        return failures.Distinct().ToList();
    }
}
