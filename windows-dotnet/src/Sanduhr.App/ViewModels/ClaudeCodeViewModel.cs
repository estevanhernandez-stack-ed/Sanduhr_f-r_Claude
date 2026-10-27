using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One Claude Code folder card (Settings v2 slice 4): its path, the linked account, the MCP read switch, Sanduhr's pieces in this
/// folder, the meters' state here and the collapsed "What this folder holds". Every action on the card acts on this folder.</summary>
public sealed partial class CcFolderRowViewModel : ObservableObject
{
    private readonly ClaudeCodeViewModel _owner;
    private bool _loading = true;
    private FolderHoldsViewModel? _holds;

    internal CcFolder Folder { get; }
    public string Path => Folder.Path;
    public string Name => Folder.Name;
    public bool Discovered => Folder.Discovered;
    public bool IsAdded => !Folder.Discovered;

    /// <summary>The path with the profile folder written as ~.</summary>
    public string Display { get; }

    [ObservableProperty] private string _linkedAccount = ClaudeCodeViewModel.NoAccount;
    [ObservableProperty] private bool _mcpReadable;
    /// <summary>The meters above the prompt in this folder: On, Off, Files missing, Not installed or Can't be read. A status line, never a control (WF9).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetersSpoken))]
    private string _metersText = "";
    /// <summary>"What this folder holds" is open: the folder is read then.</summary>
    [ObservableProperty] private bool _holdsOpen;

    internal CcFolderRowViewModel(ClaudeCodeViewModel owner, CcFolder folder, string display, string linked, bool mcpReadable, bool hasAccounts)
    {
        _owner = owner;
        Folder = folder;
        Display = display;
        _linkedAccount = linked;
        _mcpReadable = mcpReadable;
        HasAccounts = hasAccounts;
        Statusline = owner.NewPiece(CcPieceKind.Statusline, this);
        Mcp = owner.NewPiece(CcPieceKind.Mcp, this);
        Hooks = owner.NewPiece(CcPieceKind.Hooks, this);
        _loading = false;
    }

    public CcPieceViewModel Statusline { get; }
    public CcPieceViewModel Mcp { get; }
    public CcPieceViewModel Hooks { get; }

    /// <summary>The folder's report, made the first time it is asked for.</summary>
    public FolderHoldsViewModel Holds => _holds ??= _owner.NewHolds(Path);

    /// <summary>An account is signed in to link; otherwise the card says how to get one (WF32).</summary>
    public bool HasAccounts { get; }
    public bool NoAccounts => !HasAccounts;

    partial void OnLinkedAccountChanged(string value)
    {
        // A combo whose selected item leaves its list writes null back. That is not a choice: "None" is the
        // choice that unlinks.
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!_loading) _owner.OnLinkChanged(this, value);
    }

    partial void OnMcpReadableChanged(bool value)
    {
        if (!_loading) _owner.OnMcpReadableChanged(this, value);
    }

    partial void OnHoldsOpenChanged(bool value)
    {
        if (!value) return;
        Holds.EnsureLoaded();
        if (!_loading) _owner.RememberHolds(this);
    }

    internal void SetLinkedQuietly(string value)
    {
        _loading = true;
        LinkedAccount = value;
        _loading = false;
    }

    internal void OpenHoldsQuietly()
    {
        _loading = true;
        HoldsOpen = true;
        _loading = false;
    }

    public string AutomationName => $"Folder {Display}";

    // Each card's controls are named for its folder, so a screen reader tells one card's from another's.
    public string LinkedAccountName => $"Linked account for {Display}";
    public string ForgetName => SettingsNames.ForgetFolderFor(Display);
    public string McpReadableName => SettingsNames.LetClaudeReadTotalsFor(Display);
    public string HoldsName => $"{SettingsNames.FolderHolds} {Display}";
    public string MetersLine => SettingsNames.MetersSection + ":";
    public string MetersLink => SettingsIndex.PageId(SettingsPage.Mods) + "#" + SettingsIndex.Meters;
    public string MetersLinkTitle => SettingsNames.LinkTitle(SettingsPage.Mods);
    public string MetersSpoken => SettingsNames.MetersSpoken(Display, MetersText);
    public string MetersLinkName => $"{SettingsNames.LinkTitle(SettingsPage.Mods)} {SettingsNames.MetersIn(Display)}";

    public override string ToString() => AutomationName;

    /// <summary>The account choices (the page's list), bound directly so the combo never depends on a RelativeSource walk to the page.</summary>
    public ObservableCollection<AccountChoice> AccountOptions => _owner.AccountOptions;

    /// <summary>Streamer mode hides this folder's name: the card's menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => StreamerMode.Mask.HidesFolder(Path);

    [RelayCommand]
    private void ShowRealName() => _owner.ShowRealName(this);
    public bool LinkEnabled => _owner.LinkEnabled;
    public System.Windows.Input.ICommand ForgetCommand => _owner.ForgetFolderCommand;
}

/// <summary>A linked-account choice (slice 7, CP 6): the label is the value the combo writes; <see cref="Shown"/> is what it shows and
/// says, the codename while streamer mode hides it. Updated in place, so a reload never clears a live selection (WF2).</summary>
public sealed partial class AccountChoice : ObservableObject
{
    public AccountChoice(string label, string shown)
    {
        Label = label;
        _shown = shown;
    }

    public string Label { get; }

    [ObservableProperty] private string _shown;

    public override string ToString() => Shown;
}

/// <summary>The three pieces a folder card installs (the meters are Mods &amp; Widgets' since slice 4).</summary>
internal enum CcPieceKind { Statusline, Mcp, Hooks }

/// <summary>One Sanduhr piece in one folder card: what it is, where it stands, and its buttons.</summary>
public sealed partial class CcPieceViewModel : ObservableObject
{
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty] private string _statusText = "";
    /// <summary>Sanduhr's entry is in the folder (current or not): Remove shows.</summary>
    [ObservableProperty] private bool _isInstalled;
    /// <summary>The piece can be installed in this card's folder.</summary>
    [ObservableProperty] private bool _canInstall = true;

    private readonly string _installedVerb;
    private readonly string _installedHelp;

    public IRelayCommand InstallCommand { get; }
    public IRelayCommand RemoveCommand { get; }
    public string RemoveName { get; }

    /// <param name="installedVerb">What the install button says once the piece is in the folder: running it
    /// again copies the current files and refreshes Sanduhr's entry, so "Update…" unless the piece offers a
    /// choice ("Change…" for the statusline).</param>
    /// <param name="installedHelp">The button's help text once installed: what pressing it does.</param>
    public CcPieceViewModel(string title, string description, Action install, Action remove,
        string installedVerb = "Update…", string? installedHelp = null)
    {
        Title = title;
        Description = description;
        InstallCommand = new RelayCommand(install);
        RemoveCommand = new RelayCommand(remove);
        _installedVerb = installedVerb;
        _installedHelp = installedHelp ?? $"Asks first, then copies the current {title} files again and refreshes Sanduhr's entry in this folder.";
        RemoveName = $"Remove {title}";
    }

    /// <summary>"Install…" while the piece is absent from the folder, the installed verb ("Update…", "Change…") once it is there.</summary>
    public string InstallLabel => IsInstalled ? _installedVerb : "Install…";

    /// <summary>The install button's accessible name: "Install MCP server", "Update MCP server".</summary>
    public string InstallName => $"{InstallLabel.TrimEnd('…')} {Title}";

    /// <summary>What the install button does right now, for screen readers.</summary>
    public string InstallHelp => IsInstalled ? _installedHelp : SettingsNames.PieceInstallHelp(Title);

    public bool NotInstalled => !IsInstalled;
    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(NotInstalled));
        OnPropertyChanged(nameof(InstallLabel));
        OnPropertyChanged(nameof(InstallName));
        OnPropertyChanged(nameof(InstallHelp));
    }
}

/// <summary>
/// The Claude Code page (Settings v2 slice 4): one card per folder Sanduhr knows (found, then added by hand), each holding the account
/// linked to it, the MCP read switch, Sanduhr's pieces in that folder (statusline, MCP server, Claude Code glow hook), the meters' state
/// there and "What this folder holds", plus the Claude Code glow switches. Every write to a folder waits for a consent dialog's confirm
/// button and goes only to the folder of the card it was pressed on. The installers are the Core ones; this class chooses nothing silently.
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

    /// <summary>The folder whose "What this folder holds" was opened last this session: a link to folder-config opens it first (decision 5).
    /// The Settings window is rebuilt each time it opens; this is what keeps it.</summary>
    private static string? s_holdsFolder;

    internal static void ForgetSessionChoice() => s_holdsFolder = null;

    private readonly ClaudeCodeHost _host;

    public ObservableCollection<CcFolderRowViewModel> Folders { get; } = new();
    public ObservableCollection<AccountChoice> AccountOptions { get; } = new();

    /// <summary>A card's quick route (decision 16): the folder's real name shows on stream; every masked page reloads through the service.</summary>
    internal void ShowRealName(CcFolderRowViewModel row) =>
        _widget.Streamer.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(row.Path), true);

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasFolders;
    public bool HasNoFolders => !HasFolders;
    partial void OnHasFoldersChanged(bool value) => OnPropertyChanged(nameof(HasNoFolders));
    [ObservableProperty] private bool _linkEnabled = true;

    [ObservableProperty] private bool _glowWaiting;
    [ObservableProperty] private bool _glowDone;
    [ObservableProperty] private bool _glowSkipTerminal = true;

    /// <summary>The Claude Code glow section's line: which folders have the hook (WF11).</summary>
    [ObservableProperty] private string _glowHookStatus = "";

    /// <summary>Asks before a write to a folder (the consent dialog). A seam for tests.</summary>
    internal Func<ConsentSpec, ConsentChoice?> AskConsent { get; set; }

    /// <summary>A link landed on folder-config and opened this card's report: the page brings the card into view.</summary>
    public event Action<CcFolderRowViewModel>? HoldsLanded;

    /// <summary>Raised after folders were added or forgotten, so Mods &amp; Widgets follows.</summary>
    public event Action? FoldersChanged;
    /// <summary>Raised after an install or remove, so Mods &amp; Widgets reads the folders again.</summary>
    public event Action? PiecesChanged;

    /// <summary>Set by App: tells the glow service to re-read the switches.</summary>
    public Action? GlowReload { get; set; }
    /// <summary>Set by App: plays the glow once ("Try it").</summary>
    public Action<ClaudeCodeEvent>? GlowPreview { get; set; }

    public ClaudeCodeViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _host = ClaudeCodeHost.For(widget);
        AskConsent = spec => IntegrationConsentDialog.Ask(_owner, spec);
        Reload();
        _loading = true;
        var glow = _widget.LoadClaudeCodeGlow();
        GlowWaiting = glow.Waiting;
        GlowDone = glow.Done;
        GlowSkipTerminal = glow.SkipTerminal;
        var target = _glowSaved = _glowStore.LoadGlowTarget();
        GlowTarget = GlowTargets.First(c => c.Kind == target.Kind);
        GlowSide = GlowSides.First(c => c.Side == target.Side);
        GlowMonitors = MonitorChoices();
        GlowMonitor = GlowMonitors.FirstOrDefault(m => string.Equals(m.Device, target.Monitor, StringComparison.OrdinalIgnoreCase))
                      ?? GlowMonitors[0];
        _loading = false;
    }

    // ------------------------------------------------------------------ where the glow lands (spec 6.8)

    private readonly SettingsStore _glowStore = new(new Paths());

    /// <summary>The target as saved. Each control replaces only its own member, so a value the page can't
    /// show (S7's Desk placeholder, a monitor that is unplugged) survives a change to the others.</summary>
    private GlowTargetSettings _glowSaved = GlowTargetSettings.Default;

    public sealed record GlowTargetChoice(GlowTargetKind Kind, string Name) { public override string ToString() => Name; }
    public sealed record GlowSideChoice(GlowEdgeSide Side, string Name) { public override string ToString() => Name; }
    public sealed record GlowMonitorChoice(string? Device, string Name) { public override string ToString() => Name; }

    /// <summary>The offered targets, the Desk's spot last (spec 7.3).</summary>
    public static IReadOnlyList<GlowTargetChoice> GlowTargets { get; } =
    [
        new(GlowTargetKind.Window, "At the top of the window you're working in"),
        new(GlowTargetKind.Edge, "Along an edge of a screen"),
        new(GlowTargetKind.Widget, "Only flash Sanduhr in the taskbar"),
        new(GlowTargetKind.Desk, "On a spot on the Desk"),
    ];

    public static IReadOnlyList<GlowSideChoice> GlowSides { get; } =
        [new(GlowEdgeSide.Top, "Top"), new(GlowEdgeSide.Bottom, "Bottom"), new(GlowEdgeSide.Left, "Left"), new(GlowEdgeSide.Right, "Right")];

    [ObservableProperty] private GlowTargetChoice _glowTarget = GlowTargets[0];
    [ObservableProperty] private GlowSideChoice _glowSide = GlowSides[0];
    [ObservableProperty] private IReadOnlyList<GlowMonitorChoice> _glowMonitors = [new(null, "Main monitor")];
    [ObservableProperty] private GlowMonitorChoice? _glowMonitor;

    public bool GlowOnEdge => GlowTarget.Kind == GlowTargetKind.Edge;

    /// <summary>The Desk is the chosen target: the caption under the list says when it shows there.</summary>
    public bool GlowOnDesk => GlowTarget.Kind == GlowTargetKind.Desk;

    /// <summary>Asks the running Desk to read its settings again (App sets it); the glow's spot was just placed.</summary>
    public Action? DeskReload { get; set; }

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
        OnPropertyChanged(nameof(GlowOnDesk));
        SaveGlowTarget(_glowSaved with { Kind = value.Kind });
        if (!_loading && value.Kind == GlowTargetKind.Desk) PlaceGlowSpot();
    }

    /// <summary>Choosing the Desk places the glow's spot at its default anchor when it is not placed yet,
    /// then asks the Desk to draw it (spec 7.3). A spot already placed stays where it is.</summary>
    private void PlaceGlowSpot()
    {
        try
        {
            var desk = _glowStore.LoadDesk();
            var layout = new DeskArrangement(desk.Layout);
            // DeskArrangement.DefaultPlaceFor: the one table of default places (Settings v2, Principle 4).
            if (layout.PlaceAtDefault("glow") is null) return;
            _glowStore.SaveDesk((desk with { Layout = layout.Layout }).Normalized());
            DeskReload?.Invoke();
            GlowReload?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The target is saved; the spot can be placed from Settings, Desk.
        }
    }

    partial void OnGlowSideChanged(GlowSideChoice value) => SaveGlowTarget(_glowSaved with { Side = value.Side });
    partial void OnGlowMonitorChanged(GlowMonitorChoice? value) => SaveGlowTarget(_glowSaved with { Monitor = value?.Device });

    private void SaveGlowTarget(GlowTargetSettings next)
    {
        if (_loading || next == _glowSaved) return;
        _glowSaved = next;
        _glowStore.SaveGlowTarget(next);
        GlowReload?.Invoke();
    }

    public void AttachOwner(Window owner) => _owner = owner;

    // ------------------------------------------------------------------ folders

    private string Profile => _host.Profile;

    private ClaudeCodeIntegrations MakeIntegrations() => _host.Integrations();

    private StatuslineInstaller MakeStatusline() => new(Profile, _widget.StatuslineBinDir, null, _widget.SanduhrAppDataDir);

    private McpIntegrationInstaller MakeMcp() => new(Profile, _widget.SanduhrAppDataDir, externalDir: _widget.SanduhrExternalDir);

    private static string McpSourceDir => Path.Combine(AppContext.BaseDirectory, "mcp");

    /// <summary>Every known folder, found first and added second.</summary>
    internal IReadOnlyList<CcFolder> KnownFolders() => _host.Folders();

    /// <summary>A card's piece, its buttons acting on that card's folder.</summary>
    internal CcPieceViewModel NewPiece(CcPieceKind kind, CcFolderRowViewModel row) => kind switch
    {
        CcPieceKind.Statusline => new CcPieceViewModel("Statusline",
            "Shows your session and weekly meters under the Claude Code prompt.",
            () => InstallStatusline(row), () => RemoveStatusline(row), "Change…",
            "Asks first, then lets you replace or adjust the statusline in this folder."),
        CcPieceKind.Mcp => new CcPieceViewModel("MCP server",
            "Lets Claude Code ask for your live headroom before it spawns agents. Read-only.",
            () => InstallMcp(row), () => RemoveMcp(row), "Update…",
            "Asks first, then copies the current server files again and refreshes Sanduhr's entry in this folder's .claude.json."),
        _ => new CcPieceViewModel(SettingsNames.ClaudeCodeGlowHook,
            "Two hooks that light the edge of your window when Claude Code waits on you or finishes.",
            () => InstallHooks(row), () => RemoveHooks(row), "Update…",
            "Asks first, then writes Sanduhr's two hooks into this folder's settings.json again."),
    };

    /// <summary>A card's "What this folder holds".</summary>
    internal FolderHoldsViewModel NewHolds(string folder) => new(_host, folder);

    /// <summary>The person opened a card's report: a later link to folder-config opens that folder first.</summary>
    internal void RememberHolds(CcFolderRowViewModel row) => s_holdsFolder = row.Path;

    /// <summary>
    /// A link landed on folder-config: opens the report of the card <see cref="ModsFolderChoice.Pick"/> chooses (the one opened last this
    /// session, then CLAUDE_CONFIG_DIR, the active account's, the most plugins on, the first) and returns it; null with no folders.
    /// </summary>
    internal CcFolderRowViewModel? OpenHoldsForLanding()
    {
        string? active = null;
        try { active = _host.ActiveAccountFolder(); } catch (Exception) { }
        var pick = ModsFolderChoice.Pick([.. Folders.Select(f => f.Path)], s_holdsFolder, _host.ConfigDir, active,
            ModInventory.EnabledPluginCount);
        if (Folders.FirstOrDefault(f => pick is not null && ClaudeCodeFolders.Same(f.Path, pick)) is not { } row) return null;
        row.HoldsOpen = true;
        HoldsLanded?.Invoke(row);
        return row;
    }

    /// <summary>The meters were switched, installed, updated or removed on Mods &amp; Widgets: the cards' lines follow, and the snapshot switch
    /// is recomputed (with a fresh snapshot after an install, as this page's installs do).</summary>
    public void AfterMetersChange(bool installed)
    {
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: installed);
    }

    /// <summary>Brings the account choices to "None" plus the shown accounts without ever emptying the list: the
    /// folder rows' combos are still bound to it, and a combo whose items are cleared writes a null selection back.
    /// Only labels that are gone are removed.</summary>
    private void SyncAccountOptions()
    {
        var wanted = new List<string> { NoAccount };
        wanted.AddRange(_widget.ShownAccounts);
        for (int i = AccountOptions.Count - 1; i >= 0; i--)
            if (!wanted.Contains(AccountOptions[i].Label)) AccountOptions.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            // Streamer mode (slice 7, CP 6): the choice shows the codename; its value stays the label.
            var shown = wanted[i] == NoAccount ? NoAccount : StreamerMode.Mask.Name(CodenameCategory.Account, wanted[i]);
            int at = -1;
            for (int j = 0; j < AccountOptions.Count; j++)
                if (AccountOptions[j].Label == wanted[i]) { at = j; break; }
            if (at < 0) AccountOptions.Insert(i, new AccountChoice(wanted[i], shown));
            else
            {
                if (at != i) AccountOptions.Move(at, i);
                AccountOptions[i].Shown = shown;
            }
        }
    }

    /// <summary>Re-reads the folder list, the account choices and every card's status. A card whose report was open stays open.</summary>
    public void Reload()
    {
        var open = Folders.Where(f => f.HoldsOpen).Select(f => f.Path).ToList();
        _loading = true;
        SyncAccountOptions();
        LinkEnabled = !_widget.IsDemo;

        var roots = _widget.LoadMcpRoots();
        bool hasAccounts = AccountOptions.Count > 1;
        Folders.Clear();
        foreach (var f in KnownFolders())
        {
            var linked = _widget.ShownAccounts.FirstOrDefault(l =>
                _widget.AccountFolder(l) is { } p && ClaudeCodeFolders.Same(p, f.Path)) ?? NoAccount;
            var row = new CcFolderRowViewModel(this, f, _host.Display(f.Path), linked,
                f.Discovered && roots.GetValueOrDefault(f.Name), hasAccounts);
            if (open.Any(p => ClaudeCodeFolders.Same(p, f.Path))) row.OpenHoldsQuietly();
            Folders.Add(row);
        }
        HasFolders = Folders.Count > 0;
        _loading = false;
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: false);
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
        // No account is not "unlink everything": only NoAccount unlinks.
        if (string.IsNullOrWhiteSpace(account)) return;
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
        string.IsNullOrEmpty(text) ? text
            : StreamerMode.On ? StreamerMode.FoldersIn(text, KnownFolders().Select(f => f.Path))   // slice 7, CP 8
            : text.Replace(Profile, "~", StringComparison.OrdinalIgnoreCase);

    private static string StatusWords(IntegrationStatus s) => s switch
    {
        IntegrationStatus.Installed => "Installed.",
        IntegrationStatus.Outdated => SettingsNames.PieceOutdated,
        IntegrationStatus.Unreadable => "This folder's settings.json can't be read, so nothing is changed.",
        _ => "Not installed.",
    };

    /// <summary>Recomputes every card's pieces and meters line, and the glow section's line.</summary>
    public void RefreshStatuses()
    {
        foreach (var row in Folders) RefreshRow(row);
        GlowHookStatus = SettingsNames.GlowHookIn([.. Folders.Where(f => f.Hooks.IsInstalled).Select(f => f.Display)]);
    }

    private void RefreshRow(CcFolderRowViewModel row)
    {
        try
        {
            var sl = MakeStatusline().Status(row.Path);
            row.Statusline.CanInstall = sl != StatuslineStatus.Unreadable;
            row.Statusline.IsInstalled = sl is StatuslineStatus.Installed or StatuslineStatus.Outdated;
            row.Statusline.StatusText = sl switch
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
                row.Mcp.CanInstall = true;
                row.Mcp.IsInstalled = registered;
                row.Mcp.StatusText = registered ? "Installed." : "Not installed.";
            }
            else
            {
                row.Mcp.CanInstall = false;
                row.Mcp.IsInstalled = false;
                row.Mcp.StatusText = "Not available for folders added by hand.";
            }

            var integ = MakeIntegrations();
            var hooks = integ.Status(IntegrationKind.Hooks, row.Path);
            row.Hooks.IsInstalled = hooks is IntegrationStatus.Installed or IntegrationStatus.Outdated;
            row.Hooks.CanInstall = hooks != IntegrationStatus.Unreadable;
            row.Hooks.StatusText = StatusWords(hooks);

            // One rule with Mods & Widgets' meters row (P12): Files missing is read from the same state, not guessed from the switch.
            row.MetersText = InstalledItems.StateWord(InstalledItems.MetersState(_host.ReadFolder(row.Path, integ)));
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

    private void InstallStatusline(CcFolderRowViewModel row)
    {
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
                ? StreamerMode.Mask.Paths(ConfigMask.MaskString(cmd)) : null;
            var choice = AskConsent(new ConsentSpec(
                "Show usage under the Claude Code prompt?",
                "Sanduhr adds a statusline with your live session and weekly meters, right where the tokens burn.",
                row.Display,
                "What gets written: the render script at %USERPROFILE%\\.sanduhr\\bin, a statusLine entry in this folder's "
                + "settings.json (a copy of the file is saved under %APPDATA%\\Sanduhr\\backups first), and a usage "
                + "snapshot at %USERPROFILE%\\.sanduhr\\snapshot.json with percentages and reset times only, never your "
                + "account name or keys. " + SettingsNames.StatuslineNameClause
                + " Remove brings back the statusline that was here before.",
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

    private void RemoveStatusline(CcFolderRowViewModel row)
    {
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

    private void InstallMcp(CcFolderRowViewModel row)
    {
        if (!row.Discovered) return;
        try
        {
            var installer = MakeMcp();
            if (!Directory.Exists(McpSourceDir))
            {
                Done("The server files are missing from this build. Reinstall Sanduhr.", ok: false);
                return;
            }
            var others = KnownFolders()
                .Where(f => f.Discovered && !ClaudeCodeFolders.Same(f.Path, row.Path) && installer.IsRegistered(f.Name))
                .Select(f => StreamerMode.ShowFolder(f.Path, Profile));
            var consent = McpConsentDialog.ShowConsent(
                _owner, [row.Name], null, installer.LauncherPath, installer.ConfigPathFor,
                McpConsentCopy.WontChange(row.Display, others));
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

    private void RemoveMcp(CcFolderRowViewModel row)
    {
        if (!row.Discovered) return;
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

    // ------------------------------------------------------------------ glow hooks

    private void InstallHooks(CcFolderRowViewModel row)
    {
        try
        {
            var choice = AskConsent(new ConsentSpec(
                "Glow when Claude Code needs you?",
                "Sanduhr lights the top edge of your window when Claude Code waits on you or finishes.",
                row.Display,
                "What gets written: one Notification hook and one Stop hook in this folder's settings.json. Each only "
                + "touches a signal file under %USERPROFILE%\\.sanduhr. Nothing from Claude Code is passed on or read. "
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

    private void RemoveHooks(CcFolderRowViewModel row)
    {
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
            var integ = new ClaudeCodeIntegrations(profile, sanduhrDir, externalDir: widget.SanduhrExternalDir)
            {
                LegacyModsRoots = [widget.SanduhrLegacyModsRoot],
            };
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
            var mcp = new McpIntegrationInstaller(profile, sanduhrDir, externalDir: widget.SanduhrExternalDir);
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
