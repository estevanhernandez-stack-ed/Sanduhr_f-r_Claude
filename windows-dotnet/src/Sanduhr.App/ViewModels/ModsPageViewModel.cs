using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One pack of the verified catalog, merged with its installed row by id (spec, Catalog): Install, Update, or the same switch the
/// Installed row has, and the line under it.</summary>
public sealed partial class CatalogRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal CatalogRowViewModel(ModsPageViewModel owner, CatalogRow row, InstalledWidgetInfo? info)
    {
        _owner = owner;
        Entry = row.Entry;
        Action = row.Action;
        State = row.State;
        _enabled = row.Installed?.Enabled ?? false;
        Description = info?.Description ?? "";
        Preview = ModsPageViewModel.LoadPreview(info?.PreviewPath);
    }

    public WidgetCatalogEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Kind => Entry.Kind;
    public string Version => Entry.Version;
    public string Description { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string State { get; }
    public CatalogAction Action { get; }
    public bool HasButton => Action is CatalogAction.Install or CatalogAction.Update;
    public bool IsSwitch => Action == CatalogAction.Switch;

    /// <summary>"sprite · 1.0.0".</summary>
    public string Detail => $"{Kind} · {Version}";

    public string ActionLabel => Action switch
    {
        CatalogAction.Install => SettingsNames.WidgetInstall,
        CatalogAction.Update => SettingsNames.WidgetUpdate,
        _ => "",
    };

    /// <summary>The button's name: the action and the pack ("Install Hourglass", "Update Hourglass to 1.1.0").</summary>
    public string ActionName => Action switch
    {
        CatalogAction.Update => $"{SettingsNames.WidgetUpdate} {Name} to {Version}",
        CatalogAction.Install => $"{SettingsNames.WidgetInstall} {Name}",
        _ => "",
    };

    /// <summary>The pack's switch, bound one way: the command writes, then the row shows what is on disk (decision 7).</summary>
    [ObservableProperty] private bool _enabled;
    public string SwitchName => SettingsNames.PackOnFor(Name);

    internal void ShowEnabled(bool value)
    {
        // Not the generated setter: it skips the notice when the value is unchanged, and a click has already flipped the box.
#pragma warning disable MVVMTK0034
        _enabled = value;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Enabled));
    }

    [RelayCommand]
    private Task Act() => _owner.ActAsync(this);

    [RelayCommand]
    private void Toggle() => _owner.SwitchPack(Id, Name, !Enabled);

    /// <summary>The row read as one line: "Hourglass, sprite, version 1.0.0, installed."</summary>
    public string RowName => string.Join(", ", new[] { Name, Kind, "version " + Version, State.TrimEnd('.') }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() => RowName;
}

/// <summary>
/// One row of Mods &amp; Widgets' Installed list (Settings v2 slice 4): a widget pack with its on switch and where it shows, Sanduhr's meters
/// mod with its scope and a link up to its section, or another Claude Code plugin or mod, read-only, with its scope. No row shows a control
/// that can't act (Principle 5).
/// </summary>
public sealed partial class InstalledRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal InstalledRowViewModel(ModsPageViewModel owner, InstalledItem item, string? previewPath)
    {
        _owner = owner;
        Item = item;
        Name = item.Kind switch
        {
            InstalledKind.WidgetPack when item.MadeHere => StreamerMode.Mask.Name(CodenameCategory.AddOn, item.Name),
            InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin => StreamerMode.Mask.AddOn(item.Name),
            _ => item.Name,
        };
        Source = item.Kind is InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin
                 && item.Source != SettingsNames.SourceSkillsFolder && item.Source != SettingsNames.SourcePluginFolder
            ? StreamerMode.Mask.AddOn(item.Source)
            : item.Source;
        _enabled = item.Enabled;
        Preview = item.Kind == InstalledKind.WidgetPack ? ModsPageViewModel.LoadPreview(previewPath) : null;
        var where = InstalledItems.Where(item.Kind == InstalledKind.WidgetPack ? item.PackKind : null);
        WhereText = where.Text;
        WhereLink = where.Link;
        WhereLinkTitle = where.LinkTitle ?? "";
    }

    public InstalledItem Item { get; }
    public string Id => Item.Id;
    /// <summary>The row's name as shown (slice 7, CP 13): a Claude Code plugin's or mod's name and a pack made here are codenames while
    /// streamer mode hides them; catalog packs are public and Sanduhr's own mod is allowlisted. Display only: every action keys off <see cref="Id"/>.</summary>
    public string Name { get; }

    /// <summary>Where it came from: a plugin's marketplace is masked like its name; the source words ("from the catalog") stay.</summary>
    public string Source { get; }

    /// <summary>The author's text, which names the plugin: hidden while its name is (slice 7, CP 14).</summary>
    public string Description => HidesName ? "" : Item.Description;

    private bool HidesName => Name != Item.Name;
    private bool IsClaudeCodeItem => Item.Kind is InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin;

    /// <summary>Streamer mode hides this row's name: its menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => HidesName || Source != Item.Source;

    [RelayCommand]
    private void ShowRealName() =>
        _owner.ShowRealName?.Invoke([.. new[] { Item.Name, IsClaudeCodeItem && Source != Item.Source ? Item.Source : null }.OfType<string>()]);
    internal static InstalledRowViewModel ForTests(InstalledItem item, Action<IReadOnlyList<string>>? showRealName = null) =>
        new(new ModsPageViewModel { ShowRealName = showRealName }, item, null);
    public bool HasDescription => Description.Length > 0;
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;

    public bool IsPack => Item.Kind == InstalledKind.WidgetPack;
    public bool IsSanduhrMod => Item.Kind == InstalledKind.SanduhrIntegration;
    public bool IsReadOnly => !Item.CanSwitch;

    /// <summary>"palette · 1.0.0 · from the catalog"; "Claude Code mod · 0.1.0 · m".</summary>
    public string Detail => string.Join(" · ", new[] { KindWord, Item.Version ?? "", Source }.Where(s => s.Length > 0));

    private string KindWord => Item.Kind switch
    {
        InstalledKind.WidgetPack => Item.PackKind ?? "",
        InstalledKind.SanduhrIntegration => SettingsNames.KindSanduhrMod,
        InstalledKind.ClaudeCodeMod => SettingsNames.KindClaudeCodeMod,
        _ => SettingsNames.KindClaudeCodePlugin,
    };

    /// <summary>"On in 2 of 3 folders" for a Claude Code item; nothing for an app-wide pack.</summary>
    public string ScopeText => InstalledItems.ScopeSummary(Item);
    public bool HasScope => ScopeText.Length > 0;
    public string ReadOnlyText => Item.ReadOnlyReason ?? "";

    /// <summary>What a Claude Code mod or plugin touches, in one line ("Draws a band; can run programs."); nothing for a pack, Sanduhr's own
    /// mod or a row that touches nothing.</summary>
    public string TouchesText => Item.TouchesLine;
    public bool HasTouches => TouchesText.Length > 0;
    private string TouchesSpoken => HasTouches ? char.ToLowerInvariant(TouchesText[0]) + TouchesText[1..].TrimEnd('.') : "";

    /// <summary>Where a pack shows (WF3), and "&lt;Page&gt; settings…" to it.</summary>
    public string WhereText { get; }
    public string? WhereLink { get; }
    public string WhereLinkTitle { get; }
    public bool HasWhere => IsPack && WhereText.Length > 0;
    public bool HasWhereLink => IsPack && WhereLink is not null;
    public string WhereLinkName => SettingsNames.WhereLinkFor(WhereLinkTitle, Name);

    /// <summary>Sanduhr's meters mod is switched per folder in its own section, above the list.</summary>
    public string MetersLink => SettingsIndex.PageId(SettingsPage.Mods) + "#" + SettingsIndex.Meters;
    public string MetersLinkTitle => SettingsNames.MetersLinkUp;

    /// <summary>The pack's switch, bound one way: the command writes, then the row shows what is on disk (decision 7).</summary>
    [ObservableProperty] private bool _enabled;
    public string SwitchName => SettingsNames.PackOnFor(Name);

    internal void ShowEnabled(bool value)
    {
        // Not the generated setter: it skips the notice when the value is unchanged, and a click has already flipped the box.
#pragma warning disable MVVMTK0034
        _enabled = value;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(RowName));
    }

    [RelayCommand]
    private void Toggle() => _owner.SwitchPack(Id, Name, !Enabled);

    /// <summary>A sprite made here (Claude's or the user's own) takes the user's own PNG sheets (spec 3.8 section 6); a catalog pack never does.</summary>
    public bool CanUseOwnArt => Item.MadeHere && Item.PackKind == "sprite";
    public string UseOwnArtName => SettingsNames.UseOwnArtName(Name);

    [RelayCommand]
    private Task UseOwnArt() => _owner.UseOwnArtAsync(Id, Name);

    public string UninstallName => SettingsNames.UninstallFor(Name);

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(Id, Name);

    /// <summary>The row read as one line: a pack's name, kind, version, source and "on" or "off"; a Claude Code item's name, version, scope and
    /// mod tag, then "read-only" when Sanduhr can't switch it.</summary>
    public string RowName => IsPack
        ? string.Join(", ", new[] { Name, Item.PackKind ?? "", "version " + Item.Version, Source, Enabled ? SettingsNames.RowOn : SettingsNames.RowOff }
            .Where(s => s.Length > 0))
        : ModsPageText.RowName(Name, Item.Version, ScopeText, Item.IsMod && !IsSanduhrMod,
              hasFiles: Item.Scope?.Any(s => s.State is FolderState.On or FolderState.Off) == true)
          + (HasTouches ? ", " + TouchesSpoken : "")
          + (IsReadOnly ? ", " + SettingsNames.RowReadOnly : "");

    public override string ToString() => RowName;
}

/// <summary>
/// One folder's row under Meters above the prompt (Settings v2 slice 4, WF9): an On switch that writes enabledPlugins through SwitchOwnMod (or,
/// where the mod isn't installed, asks and installs it), "Update…" while the entry is outdated, "Remove…" while it is listed. A folder whose
/// settings.json can't be read shows why and nothing that would write.
/// </summary>
public sealed partial class MetersFolderRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal MetersFolderRowViewModel(ModsPageViewModel owner, FolderMods folder)
    {
        _owner = owner;
        Folder = folder.Folder;
        Display = folder.Display;
        Show(folder);
    }

    public string Folder { get; }
    public string Display { get; }

    [ObservableProperty] private FolderState _state;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _canUpdate;
    [ObservableProperty] private bool _canRemove;

    public bool CanSwitch => State != FolderState.Unreadable;
    public string StateText => InstalledItems.StateWord(State);
    public string Reason => State == FolderState.Unreadable ? SettingsNames.FolderUnreadableReason : "";
    public bool HasReason => Reason.Length > 0;

    public string SwitchName => SettingsNames.MetersOnFor(Display);
    /// <summary>The row read as one line: "~\.claude, On", with the reason when there is one.</summary>
    public string RowName => HasReason ? $"{Display}, {StateText}. {Reason}" : $"{Display}, {StateText}";
    public string UpdateName => SettingsNames.UpdateMetersIn(Display);
    public string RemoveName => SettingsNames.RemoveMetersFrom(Display);

    /// <summary>Shows what the folder holds now. Always raises IsOn, so a switch the person flipped and that was refused flips back (decision 7).
    /// IsOn is what settings.json says, so a folder whose files are gone (Files missing) can still be switched off (P12).</summary>
    internal void Show(FolderMods folder)
    {
        State = InstalledItems.MetersState(folder);
        // Not the generated setter: it skips the notice when the value is unchanged, and a click has already flipped the box.
#pragma warning disable MVVMTK0034
        _isOn = State != FolderState.Unreadable && folder.Own is { Listed: true, IsOn: true };
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(IsOn));
        CanRemove = State != FolderState.Unreadable && folder.Own is { Listed: true };
        CanUpdate = CanRemove && folder.Meters == IntegrationStatus.Outdated;
        OnPropertyChanged(nameof(CanSwitch));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RowName));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(HasReason));
    }

    [RelayCommand]
    private void Toggle() => _owner.SwitchMeters(this);

    [RelayCommand]
    private void Update() => _owner.UpdateMeters(this);

    [RelayCommand]
    private void Remove() => _owner.RemoveMeters(this);
}

/// <summary>A question the meters rows ask before Update… or Remove…: each caller names both buttons, and Remove is the warning.</summary>
internal sealed record MetersQuestion(string Title, string Message, string Primary, string Secondary, bool Warning);

/// <summary>
/// Settings, Mods &amp; Widgets (Settings v2 slice 4, spec "Mods &amp; Widgets: one list"), the Mods tab: Meters above the prompt per folder, the
/// Installed list over every Claude Code folder and the widget packs, and the catalog, which asks GitHub only when it is opened.
/// </summary>
public sealed partial class ModsPageViewModel : ObservableObject
{
    private readonly Lazy<WidgetCatalogService> _service;
    private Task? _refreshing;
    private readonly ClaudeCodeHost? _host;
    private IReadOnlyList<InstalledItem> _items = [];
    private Window? _window;

    /// <param name="host">The Claude Code folders the list reads; null lists the widget packs only (tests of the catalog and the review).</param>
    internal ModsPageViewModel(Func<WidgetCatalogService>? service = null, SettingsStore? store = null, ClaudeCodeHost? host = null)
    {
        _service = new Lazy<WidgetCatalogService>(service ?? (() => new WidgetCatalogService()));
        _host = host;
        Confirm = (question, detail) => ThemedDialog.ShowConfirm(_window, question, detail, SettingsNames.UninstallConfirm, SettingsNames.UninstallCancel);
        // settings.json is not read here; the store parameter stays for callers.
        AskMetersConsent = folder => IntegrationConsentDialog.Ask(_window, new ConsentSpec(
            SettingsNames.MetersConsentTitle, SettingsNames.MetersConsentIntro, folder, SettingsNames.MetersConsentDetails,
            SettingsNames.MetersConsentInstall)) is not null;
        Ask = q => ThemedDialog.Show(_window, q.Title, q.Message, MessageBoxButton.YesNo,   // streamer: notname - q.Message is a confirmation question built from fixed copy and version stamps, no names
            q.Warning ? ThemedDialogKind.Warning : ThemedDialogKind.Info,
            primaryLabel: q.Primary, secondaryLabel: q.Secondary) == MessageBoxResult.Yes;
    }

    /// <summary>Dialogs parent to the Settings window.</summary>
    public void AttachOwner(Window owner) => _window = owner;

    /// <summary>Asks before Uninstall… (decision 17): the question and its detail in, yes or no out. A seam for tests.</summary>
    internal Func<string, string, bool> Confirm { get; set; }

    /// <summary>The Claude Code folders as last read (the meters section reads them too).</summary>
    internal IReadOnlyList<FolderMods> Folders { get; private set; } = [];

    // ---- Meters above the prompt (spec, Mods & Widgets, section 1): every folder at once ----

    public ObservableCollection<MetersFolderRowViewModel> Meters { get; } = [];
    [ObservableProperty] private string _metersStatus = "";
    public bool HasMeterFolders => Meters.Count > 0;
    public string MetersCaption => SettingsNames.MetersOffVsRemove;

    /// <summary>Raised after a folder's meters were switched, installed, updated or removed (true after an install): the Claude Code cards
    /// read again and the snapshot switch follows (decision 22).</summary>
    public Action<bool>? MetersChanged { get; set; }

    /// <summary>Asks before the mod is installed into a folder (the consent dialog that names it): the folder's display in, yes out. A seam for tests.</summary>
    internal Func<string, bool> AskMetersConsent { get; set; }

    /// <summary>Asks before Update… and Remove…: the question in, yes out. A seam for tests.</summary>
    internal Func<MetersQuestion, bool> Ask { get; set; }

    /// <summary>Brings the rows to the folders as read, in place where a folder is still there, so a switch keeps keyboard focus.</summary>
    private void ShowMeters()
    {
        var wanted = Folders;
        for (int i = Meters.Count - 1; i >= 0; i--)
            if (!wanted.Any(f => ClaudeCodeFolders.Same(f.Folder, Meters[i].Folder))) Meters.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            var row = Meters.FirstOrDefault(m => ClaudeCodeFolders.Same(m.Folder, wanted[i].Folder));
            if (row is null) Meters.Insert(Math.Min(i, Meters.Count), new MetersFolderRowViewModel(this, wanted[i]));
            else row.Show(wanted[i]);
        }
        OnPropertyChanged(nameof(HasMeterFolders));
    }

    internal void SwitchMeters(MetersFolderRowViewModel row)
    {
        MetersStatus = "";   // an outcome that repeats is still news: the line announces a change of text
        if (_host is null || !row.CanSwitch)
        {
            ReloadLocal();
            return;
        }
        bool want = !row.IsOn;
        bool installed = false, changed = false;
        try
        {
            var integ = _host.Integrations();
            if (want && !integ.OwnModStateOf(row.Folder).Listed)
            {
                // Decision 6: the mod isn't here, so ask first and install; SwitchOwnMod would install silently.
                if (AskMetersConsent(row.Display))
                {
                    var res = integ.Install(IntegrationKind.Meters, row.Folder);
                    installed = changed = res.Ok;
                    MetersStatus = res.Ok ? SettingsNames.MetersInstalled : Short(res.Message, SettingsNames.MetersInstallFailed);   // streamer: masked - via Short: the install message passes Mask.Paths or FoldersIn (CP 8)
                }
            }
            else
            {
                var res = integ.SwitchOwnMod(row.Folder, want);
                changed = res.Ok;
                MetersStatus = res.Ok ? (want ? SettingsNames.MetersTurnedOn : SettingsNames.MetersTurnedOff)
                    : Short(res.Message, SettingsNames.MetersSwitchFailed);   // streamer: masked - via Short: the install message passes Mask.Paths or FoldersIn (CP 8)
            }
        }
        catch (Exception) { MetersStatus = SettingsNames.MetersSwitchFailed; }
        ReloadLocal();
        if (changed) MetersChanged?.Invoke(installed);
    }

    internal void UpdateMeters(MetersFolderRowViewModel row)
    {
        MetersStatus = "";   // an outcome that repeats is still news: the line announces a change of text
        if (_host is null || !row.CanUpdate) return;
        try
        {
            var integ = _host.Integrations();
            var state = integ.OwnModStateOf(row.Folder);
            if (!Ask(new MetersQuestion(SettingsNames.MetersUpdateTitle, SettingsNames.MetersUpdateBody(state.Pinned, integ.CurrentModStamp()),
                    SettingsNames.MetersUpdateConfirm, SettingsNames.MetersUpdateNotNow, Warning: false)))
                return;
            var res = integ.UpdateOwnMod(row.Folder);
            MetersStatus = !res.Ok ? Short(res.Message, SettingsNames.MetersUpdateFailed)   // streamer: masked - via Short: the install message passes Mask.Paths or FoldersIn (CP 8)
                : res.Outcome == ModUpdateOutcome.Updated ? SettingsNames.MetersUpdated : SettingsNames.MetersAlreadyCurrent;
        }
        catch (Exception) { MetersStatus = SettingsNames.MetersUpdateFailed; }
        ReloadLocal();
        MetersChanged?.Invoke(false);
    }

    internal void RemoveMeters(MetersFolderRowViewModel row)
    {
        MetersStatus = "";   // an outcome that repeats is still news: the line announces a change of text
        if (_host is null || !row.CanRemove) return;
        try
        {
            if (!Ask(new MetersQuestion(SettingsNames.MetersRemoveTitle, SettingsNames.MetersRemoveBody,
                    SettingsNames.MetersRemoveConfirm, SettingsNames.MetersRemoveCancel, Warning: true)))
                return;
            var res = _host.Integrations().Remove(IntegrationKind.Meters, row.Folder);
            MetersStatus = res.Ok ? SettingsNames.MetersRemoved : Short(res.Message, SettingsNames.MetersRemoveFailed);   // streamer: masked - via Short: the install message passes Mask.Paths or FoldersIn (CP 8)
        }
        catch (Exception) { MetersStatus = SettingsNames.MetersRemoveFailed; }
        ReloadLocal();
        MetersChanged?.Invoke(false);
    }

    internal string ShortForTests(string message, string fallback) => Short(message, fallback);

    /// <summary>Set by Settings (slice 7): a row's "Show the real name on stream" shows these add-on names through the widget's service.</summary>
    public Action<IReadOnlyList<string>>? ShowRealName { get; set; }

    private string Short(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback
            : _host is null ? StreamerMode.Mask.Paths(message)
            : StreamerMode.On ? StreamerMode.FoldersIn(message, _host.Folders().Select(f => f.Path))   // slice 7, CP 8   // streamer: masked - CP 8: FoldersIn masks on this line
            : message.Replace(_host.Profile, "~", StringComparison.OrdinalIgnoreCase);

    /// <summary>Called after a pack is installed or removed (the Desk reloads).</summary>
    public Action? Changed { get; set; }

    /// <summary>Raised with <see cref="Changed"/>: the Settings window's own pickers read the packs again (SettingsViewModel sets it).</summary>
    internal Action? SettingsChanged { get; set; }

    private void Notify()
    {
        Changed?.Invoke();
        SettingsChanged?.Invoke();
    }

    internal WidgetCatalogService Service => _service.Value;

    public ObservableCollection<CatalogRowViewModel> Catalog { get; } = [];

    /// <summary>The Catalog section is open. Opening it fetches the catalog when nothing was fetched in this window (WF6, decision 13).</summary>
    [ObservableProperty] private bool _catalogOpen;
    /// <summary>One pack kind to show ("sprite" from the companion row's link), null for every pack (decision 14).</summary>
    [ObservableProperty] private string? _catalogKind;
    /// <summary>What the last catalog fetch found: the verified line, or why not (WF40). List actions write <see cref="Status"/> instead.</summary>
    [ObservableProperty] private string? _catalogStatus;
    private bool _asked;

    public bool ShowingOneKind => CatalogKind is not null;
    public string? ShowingKindText => CatalogKind == "sprite" ? SettingsNames.ShowingSprites : null;

    partial void OnCatalogOpenChanged(bool value)
    {
        if (value) _ = OpenCatalog();
    }

    partial void OnCatalogKindChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowingOneKind));
        OnPropertyChanged(nameof(ShowingKindText));
        RebuildCatalog();
    }

    /// <summary>The catalog was opened: fetch once in this window (an attempt counts; "Check the catalog again" retries).</summary>
    public Task OpenCatalog()
    {
        if (!CatalogOpen) CatalogOpen = true;   // a landing on mods#catalog or the companion link opens it
        if (_refreshing is { IsCompleted: false } running) return running;
        if (_asked)
        {
            RebuildCatalog();
            return Task.CompletedTask;
        }
        return _refreshing = RefreshAsync();
    }

    /// <summary>The companion row's "Get one in Mods &amp; Widgets…": the catalog, sprites only, for this visit.</summary>
    public void ShowSprites()
    {
        _linkSetFilter = !_active;
        CatalogKind = "sprite";
        _ = OpenCatalog();
    }

    [RelayCommand]
    private void ShowEveryPack() => CatalogKind = null;
    /// <summary>The Installed list as the filters show it (spec, Installed: "All", "Claude Code", "Widgets", and a text filter).</summary>
    public ObservableCollection<InstalledRowViewModel> Installed { get; } = [];

    [ObservableProperty] private InstalledFilter _filter = InstalledFilter.All;
    [ObservableProperty] private string _filterText = "";
    /// <summary>The plugin-count note in plain words, summed over the folders; "" when there is nothing to explain.</summary>
    [ObservableProperty] private string _note = "";
    /// <summary>Something is installed, but the filters hide all of it.</summary>
    [ObservableProperty] private bool _nothingMatches;

    public bool IsFilterAll { get => Filter == InstalledFilter.All; set { if (value) Filter = InstalledFilter.All; } }
    public bool IsFilterClaudeCode { get => Filter == InstalledFilter.ClaudeCode; set { if (value) Filter = InstalledFilter.ClaudeCode; } }
    public bool IsFilterWidgets { get => Filter == InstalledFilter.Widgets; set { if (value) Filter = InstalledFilter.Widgets; } }

    partial void OnFilterChanged(InstalledFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterClaudeCode));
        OnPropertyChanged(nameof(IsFilterWidgets));
        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Themes' "Mods &amp; Widgets settings…" lands on the list showing widget packs (spec, Menus).</summary>
    public void ShowWidgetPacks()
    {
        _linkSetFilter = !_active;
        FilterText = "";
        Filter = InstalledFilter.Widgets;
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _hasInstalled;
    [ObservableProperty] private bool _hasCatalog;


    /// <summary>The page became visible: list what is installed. Nothing is fetched (the catalog asks only when opened, WF6).</summary>
    public Task Activate()
    {
        // Every visit starts on All with no text, unless the link that brought you set a filter first (Themes' pack link, the companion's
        // sprite link): that filter holds for this visit only (owner decision, slice 4 follow-up).
        if (!_linkSetFilter && !_active)
        {
            Filter = InstalledFilter.All;
            FilterText = "";
            CatalogKind = null;
        }
        _linkSetFilter = false;
        _active = true;
        ReloadLocal();
        return Task.CompletedTask;
    }

    /// <summary>The page left the screen: a link that lands next sets its filter ahead of the visit.</summary>
    public void Deactivate() => _active = false;

    private bool _active;
    private bool _linkSetFilter;

    [RelayCommand]
    private Task CheckAgain() => _refreshing is { IsCompleted: false } running ? running : _refreshing = RefreshAsync();

    private async Task RefreshAsync()
    {
        _asked = true;
        Busy = true;
        CatalogStatus = SettingsNames.CatalogChecking;
        try
        {
            var result = await Service.Client.RefreshAsync();
            if (result.Catalog is { } doc)
            {
                var date = doc.GeneratedAt.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
                CatalogStatus = SettingsNames.CatalogVerified(doc.Packs.Count, date);
            }
            else
            {
                CatalogStatus = result.Error ?? WidgetCatalogClient.Unreadable;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CatalogStatus = SettingsNames.WidgetsFolderUnreadable;
        }
        finally
        {
            Busy = false;
            Rebuild();
        }
    }

    internal async Task ActAsync(CatalogRowViewModel row)
    {
        if (Busy) return;
        if (row.Action is not (CatalogAction.Install or CatalogAction.Update)) return;
        Busy = true;
        Status = row.Action == CatalogAction.Update ? SettingsNames.WidgetUpdating(row.Name) : SettingsNames.WidgetInstalling(row.Name);
        try
        {
            var result = await Service.Client.InstallAsync(row.Entry);
            Status = result.Ok
                ? (row.Action == CatalogAction.Update ? SettingsNames.WidgetUpdated(row.Name, row.Version) : SettingsNames.WidgetInstalled(row.Name))
                : result.Error ?? SettingsNames.WidgetInstallFailed;
            if (result.Ok) Notify();
        }
        finally
        {
            Busy = false;
            Rebuild();
        }
    }

    internal Task UninstallAsync(string id, string name)
    {
        if (Busy) return Task.CompletedTask;
        if (!Confirm(SettingsNames.UninstallQuestion(name), SettingsNames.UninstallDetail)) return Task.CompletedTask;
        Status = "";   // a repeated outcome is still announced
        var result = Service.Store.Uninstall(id);
        Status = result.Ok ? SettingsNames.WidgetUninstalled(name) : result.Error ?? SettingsNames.WidgetRemoveFailed;
        Rebuild();
        if (result.Ok) Notify();
        return Task.CompletedTask;
    }

    private void Rebuild()
    {
        ReloadInstalled();
        RebuildCatalog();
    }

    private void RebuildCatalog()
    {
        Catalog.Clear();
        if (Service.Client.Current is { } doc)
            foreach (var row in InstalledItems.CatalogRows(doc, _items, Service.Client.Running, CatalogKind))
                Catalog.Add(new CatalogRowViewModel(this, row, row.Installed is null ? null : Service.Store.ReadInfo(row.Entry.Id)));
        HasCatalog = Catalog.Count > 0;
    }

    /// <summary>Reads every Claude Code folder, the installed packs and the catalog document already fetched (no fetch), and rebuilds the list.</summary>
    private void ReloadInstalled()
    {
        WidgetRegistry reg;
        try { reg = Service.Store.Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
        Folders = _host?.ReadFolders() ?? [];
        _items = InstalledItems.Build(Folders, reg, id => Service.Store.ReadInfo(id), Service.Client.Current);
        Note = ModsPageText.PlainNote(Folders);
        HasInstalled = _items.Count > 0;
        ApplyFilter();
        ShowMeters();
    }

    private void ApplyFilter()
    {
        Installed.Clear();
        var text = FilterText?.Trim();
        foreach (var item in _items.Where(i => InstalledItems.Matches(i, Filter, null)))
        {
            var row = new InstalledRowViewModel(this, item,
                item.Kind == InstalledKind.WidgetPack ? Service.Store.ReadInfo(item.Id)?.PreviewPath : null);
            // The box also matches what the row shows: while streamer mode is on, that is its codename (final review M7).
            if (InstalledItems.Matches(item, Filter, text)
                || (!string.IsNullOrEmpty(text) && (row.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || row.Source.Contains(text, StringComparison.OrdinalIgnoreCase))))
                Installed.Add(row);
        }
        NothingMatches = HasInstalled && Installed.Count == 0;
    }

    /// <summary>
    /// A pack's switch (spec, Installed: "a switch bound to widgets.json enabled"): writes the flag, then shows what is on disk on every row of
    /// that pack, and tells the consumers (App's Changed, the window's pickers) when it changed.
    /// </summary>
    internal void SwitchPack(string id, string name, bool on)
    {
        if (Busy)
        {
            foreach (var row in Installed.Where(r => r.Id == id)) row.ShowEnabled(row.Enabled);
            foreach (var row in Catalog.Where(r => r.Id == id)) row.ShowEnabled(row.Enabled);
            return;
        }
        Status = "";   // a repeated outcome is still announced
        var result = Service.Store.SetEnabled(id, on);
        // What is on disk now; a pack gone behind the page's back keeps the row's last state (nothing was written).
        bool now = Service.Store.Load().Find(id)?.Enabled ?? Installed.FirstOrDefault(r => r.Id == id)?.Enabled ?? !on;
        Status = result.Ok ? (now ? SettingsNames.PackIsOn(name) : SettingsNames.PackIsOff(name)) : result.Error ?? SettingsNames.PackSwitchFailed;
        _items = [.. _items.Select(i => i.Id == id && i.Kind == InstalledKind.WidgetPack ? i with { Enabled = now } : i)];
        foreach (var row in Installed.Where(r => r.Id == id)) row.ShowEnabled(now);
        foreach (var row in Catalog.Where(r => r.Id == id)) row.ShowEnabled(now);
        if (result.Ok) Notify();
    }

    /// <summary>Picks one PNG for a stage: the dialog's title in, the chosen file's path out (null when cancelled). The app's is
    /// the Windows file dialog; tests pass their own.</summary>
    internal Func<string, string?> PickImage { get; set; } = DefaultPickImage;

    /// <summary>A pack installed, uninstalled or changed somewhere else (a widget made with Claude was applied): the lists are read
    /// again from the widgets folder. The catalog is not fetched.</summary>
    public void ReloadLocal() => Rebuild();

    /// <summary>
    /// "Use my own art…" (spec 3.8 section 6): one PNG per stage, each checked with the pack image rules before it is copied in,
    /// then the whole pack validated again and installed as the user's own (local-only). A stage whose dialog is cancelled keeps
    /// its sheet. The images never leave this PC.
    /// </summary>
    internal Task UseOwnArtAsync(string id, string name)
    {
        if (Busy) return Task.CompletedTask;
        Status = "";   // a repeated outcome is still announced
        var files = Service.Store.ReadFiles(id);
        if (files is null || WidgetOwnArt.Sprite(files) is not { } sprite)
        {
            Status = SettingsNames.OwnArtUnreadable(name);
            return Task.CompletedTask;
        }
        var art = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < sprite.Stages.Count; i++)
        {
            var stage = sprite.Stages[i];
            string title = sprite.Stages.Count == 1
                ? SettingsNames.OwnArtPickTitle(name, stage.FrameWidth, stage.FrameHeight)
                : SettingsNames.OwnArtPickStageTitle(name, i + 1, sprite.Stages.Count, stage.Name, stage.FrameWidth, stage.FrameHeight);
            if (PickImage(title) is not { } path) continue;
            byte[] png;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > PackValidator.MaxImageBytes)
                {
                    Status = SettingsNames.OwnArtTooLarge(stage.Name, name);
                    return Task.CompletedTask;
                }
                png = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Status = SettingsNames.OwnArtImageUnreadable(name);
                return Task.CompletedTask;
            }
            if (WidgetOwnArt.CheckImage(png, stage, sprite.MaxFrame) is { } why)
            {
                Status = SettingsNames.OwnArtRejected(stage.Name, why, name);
                return Task.CompletedTask;
            }
            art[stage.Id] = png;
        }
        if (art.Count == 0)
        {
            Status = SettingsNames.OwnArtNoneChosen(name);
            return Task.CompletedTask;
        }
        var result = WidgetOwnArt.Apply(files, id, art);
        if (!result.Ok)
        {
            Status = SettingsNames.OwnArtUnchanged(name, result.Errors.FirstOrDefault() ?? SettingsNames.OwnArtChecksFailed);
            return Task.CompletedTask;
        }
        var installed = Service.Store.Install(result.Files!, result.Manifest!, WidgetSource.Local, replaceMadeHere: true);
        Status = installed.Ok
            ? SettingsNames.OwnArtApplied(name)
            : installed.Error ?? SettingsNames.OwnArtChangeFailed(name);
        Rebuild();
        if (installed.Ok) Notify();
        return Task.CompletedTask;
    }

    private static string? DefaultPickImage(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = SettingsNames.OwnArtFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;   // streamer: key - the picked sprite sheet path is imported, never drawn
    }

    /// <summary>A pack's preview, decoded from memory so the file is never held open (Uninstall deletes it).</summary>
    internal static ImageSource? LoadPreview(string? path)
    {
        if (path is null) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > PackValidator.MaxImageBytes) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
