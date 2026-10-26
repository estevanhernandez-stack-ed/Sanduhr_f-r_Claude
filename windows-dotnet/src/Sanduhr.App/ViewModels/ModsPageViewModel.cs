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

/// <summary>One pack of the verified catalog: what it is, whether it is installed, and the one thing its button does.</summary>
public sealed partial class WidgetCatalogRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal WidgetCatalogRowViewModel(ModsPageViewModel owner, WidgetCatalogEntry entry, InstalledWidget? installed,
        InstalledWidgetInfo? info, bool meetsMinimum)
    {
        _owner = owner;
        Entry = entry;
        Description = info?.Description ?? "";
        Preview = ModsPageViewModel.LoadPreview(info?.PreviewPath);
        if (installed is { MadeHere: true })
        {
            Action = WidgetRowAction.None;
            State = $"A widget {SettingsNames.WidgetMadeHere} already uses this id.";
        }
        else if (installed is null)
        {
            Action = meetsMinimum ? WidgetRowAction.Install : WidgetRowAction.None;
            State = meetsMinimum ? "" : $"Needs Sanduhr {entry.SanduhrMin} or later.";
        }
        else if (WidgetCatalog.CompareVersions(entry.Version, installed.Version) > 0)
        {
            Action = meetsMinimum ? WidgetRowAction.Update : WidgetRowAction.None;
            State = meetsMinimum
                ? $"Version {installed.Version} is installed."
                : $"Version {installed.Version} is installed. {entry.Version} needs Sanduhr {entry.SanduhrMin} or later.";
        }
        else
        {
            // Uninstall sits on the installed list, once per pack.
            Action = WidgetRowAction.None;
            State = "Installed.";
        }
    }

    public WidgetCatalogEntry Entry { get; }
    public string Name => Entry.Name;
    public string Kind => Entry.Kind;
    public string Version => Entry.Version;
    public string Description { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string State { get; }
    public WidgetRowAction Action { get; }
    public bool HasAction => Action != WidgetRowAction.None;

    /// <summary>"Hourglass · sprite · 1.0.0".</summary>
    public string Detail => $"{Kind} · {Version}";

    public string ActionLabel => Action switch
    {
        WidgetRowAction.Install => SettingsNames.WidgetInstall,
        WidgetRowAction.Update => SettingsNames.WidgetUpdate,
        _ => "",
    };

    /// <summary>The button's name for a screen reader: the action and the pack ("Install Hourglass", "Update Hourglass
    /// to 1.1.0"), so one row's button is told from the next.</summary>
    public string ActionName => Action switch
    {
        WidgetRowAction.Update => $"{SettingsNames.WidgetUpdate} {Name} to {Version}",
        WidgetRowAction.None => "",
        _ => $"{ActionLabel} {Name}",
    };

    /// <summary>The row read as one line: "Hourglass, sprite, version 1.0.0, installed."</summary>
    public string RowName => string.Join(", ", new[] { Name, Kind, "version " + Version, State.TrimEnd('.') }
        .Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task Act() => _owner.ActAsync(this);

    public override string ToString() => RowName;
}

public enum WidgetRowAction { None, Install, Update }

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
        _enabled = item.Enabled;
        Preview = item.Kind == InstalledKind.WidgetPack ? ModsPageViewModel.LoadPreview(previewPath) : null;
        var where = InstalledItems.Where(item.Kind == InstalledKind.WidgetPack ? item.PackKind : null);
        WhereText = where.Text;
        WhereLink = where.Link;
        WhereLinkTitle = where.LinkTitle ?? "";
    }

    public InstalledItem Item { get; }
    public string Id => Item.Id;
    public string Source => Item.Source;
    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => Description.Length > 0;
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;

    public bool IsPack => Item.Kind == InstalledKind.WidgetPack;
    public bool IsSanduhrMod => Item.Kind == InstalledKind.SanduhrIntegration;
    public bool IsReadOnly => !Item.CanSwitch;

    /// <summary>"palette · 1.0.0 · from the catalog"; "Claude Code mod · 0.1.0 · m".</summary>
    public string Detail => string.Join(" · ", new[] { KindWord, Item.Version ?? "", Item.Source }.Where(s => s.Length > 0));

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
    public string UseOwnArtName => $"Use my own art for {Name}";

    [RelayCommand]
    private Task UseOwnArt() => _owner.UseOwnArtAsync(Id, Name);

    public string UninstallName => SettingsNames.UninstallFor(Name);

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(Id, Name);

    /// <summary>The row read as one line: a pack's name, kind, version, source and "on" or "off"; a Claude Code item's name, version, scope and
    /// mod tag, then "read-only" when Sanduhr can't switch it.</summary>
    public string RowName => IsPack
        ? string.Join(", ", new[] { Name, Item.PackKind ?? "", "version " + Item.Version, Item.Source, Enabled ? SettingsNames.RowOn : SettingsNames.RowOff }
            .Where(s => s.Length > 0))
        : ModsPageText.RowName(Name, Item.Version, ScopeText, Item.IsMod && !IsSanduhrMod,
              hasFiles: Item.Scope?.Any(s => s.State is FolderState.On or FolderState.Off) == true)
          + (IsReadOnly ? ", " + SettingsNames.RowReadOnly : "");

    public override string ToString() => RowName;
}

/// <summary>
/// Settings, Mods &amp; Widgets (Settings v2 slice 4, spec "Mods &amp; Widgets: one list"): the Installed list over every Claude Code folder and the widget packs, the catalog, and a status line. Until the page flip (Task 12) it is hosted in the Widgets tab.
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
    }

    /// <summary>Dialogs parent to the Settings window.</summary>
    public void AttachOwner(Window owner) => _window = owner;

    /// <summary>Asks before Uninstall… (decision 17): the question and its detail in, yes or no out. A seam for tests.</summary>
    internal Func<string, string, bool> Confirm { get; set; }

    /// <summary>The Claude Code folders as last read (the meters section reads them too).</summary>
    internal IReadOnlyList<FolderMods> Folders { get; private set; } = [];

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

    public ObservableCollection<WidgetCatalogRowViewModel> Catalog { get; } = [];
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
        FilterText = "";
        Filter = InstalledFilter.Widgets;
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _hasInstalled;
    [ObservableProperty] private bool _hasCatalog;

    public string CheckAgainTitle => SettingsNames.WidgetCatalogCheck;

    /// <summary>The page became visible: list what is installed and fetch the catalog once more.</summary>
    public Task Activate()
    {
        ReloadInstalled();
        return _refreshing is { IsCompleted: false } running ? running : _refreshing = RefreshAsync();
    }

    [RelayCommand]
    private Task CheckAgain() => _refreshing is { IsCompleted: false } running ? running : _refreshing = RefreshAsync();

    private async Task RefreshAsync()
    {
        Busy = true;
        Status = "Checking the widget catalog…";
        try
        {
            var result = await Service.Client.RefreshAsync();
            if (result.Catalog is { } doc)
            {
                var date = doc.GeneratedAt.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
                Status = doc.Packs.Count == 1
                    ? $"The catalog is verified: 1 widget, published {date}."
                    : $"The catalog is verified: {doc.Packs.Count} widgets, published {date}.";
            }
            else
            {
                Status = result.Error ?? WidgetCatalogClient.Unreadable;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = "The widgets folder couldn't be read.";
        }
        finally
        {
            Busy = false;
            Rebuild();
        }
    }

    internal async Task ActAsync(WidgetCatalogRowViewModel row)
    {
        if (Busy) return;
        if (row.Action is not (WidgetRowAction.Install or WidgetRowAction.Update)) return;
        Busy = true;
        Status = row.Action == WidgetRowAction.Update ? $"Updating {row.Name}…" : $"Installing {row.Name}…";
        try
        {
            var result = await Service.Client.InstallAsync(row.Entry);
            Status = result.Ok
                ? (row.Action == WidgetRowAction.Update ? $"{row.Name} is updated to {row.Version}." : $"{row.Name} is installed.")
                : result.Error ?? "The widget couldn't be installed.";
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
        var result = Service.Store.Uninstall(id);
        Status = result.Ok ? $"{name} is uninstalled." : result.Error ?? "The widget couldn't be removed.";
        Rebuild();
        if (result.Ok) Notify();
        return Task.CompletedTask;
    }

    private void Rebuild()
    {
        ReloadInstalled();
        Catalog.Clear();
        var doc = Service.Client.Current;
        if (doc is not null)
        {
            var reg = Service.Store.Load();
            var running = Service.Client.Running;
            foreach (var entry in doc.Packs)
            {
                var installed = reg.Find(entry.Id);
                Catalog.Add(new WidgetCatalogRowViewModel(this, entry, installed,
                    installed is null ? null : Service.Store.ReadInfo(entry.Id),
                    WidgetCatalog.MeetsMinimum(entry.SanduhrMin, running)));
            }
        }
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
    }

    private void ApplyFilter()
    {
        Installed.Clear();
        foreach (var item in _items.Where(i => InstalledItems.Matches(i, Filter, FilterText)))
            Installed.Add(new InstalledRowViewModel(this, item,
                item.Kind == InstalledKind.WidgetPack ? Service.Store.ReadInfo(item.Id)?.PreviewPath : null));
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
            return;
        }
        var result = Service.Store.SetEnabled(id, on);
        // What is on disk now; a pack gone behind the page's back keeps the row's last state (nothing was written).
        bool now = Service.Store.Load().Find(id)?.Enabled ?? Installed.FirstOrDefault(r => r.Id == id)?.Enabled ?? !on;
        Status = result.Ok ? (now ? SettingsNames.PackIsOn(name) : SettingsNames.PackIsOff(name)) : result.Error ?? "The widget couldn't be switched.";
        _items = [.. _items.Select(i => i.Id == id && i.Kind == InstalledKind.WidgetPack ? i with { Enabled = now } : i)];
        foreach (var row in Installed.Where(r => r.Id == id)) row.ShowEnabled(now);
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
        var files = Service.Store.ReadFiles(id);
        if (files is null || WidgetOwnArt.Sprite(files) is not { } sprite)
        {
            Status = $"{name} couldn't be read, so it is unchanged.";
            return Task.CompletedTask;
        }
        var art = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < sprite.Stages.Count; i++)
        {
            var stage = sprite.Stages[i];
            string title = sprite.Stages.Count == 1
                ? $"Your own art for {name} ({stage.FrameWidth}x{stage.FrameHeight} pixel cells)"
                : $"Your own art for {name}: stage {i + 1} of {sprite.Stages.Count}, {stage.Name} ({stage.FrameWidth}x{stage.FrameHeight} pixel cells)";
            if (PickImage(title) is not { } path) continue;
            byte[] png;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > PackValidator.MaxImageBytes)
                {
                    Status = $"{stage.Name}: that image is larger than 2 MB. A sprite sheet is at most 2 MB, so {name} is unchanged.";
                    return Task.CompletedTask;
                }
                png = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Status = $"That image couldn't be read, so {name} is unchanged.";
                return Task.CompletedTask;
            }
            if (WidgetOwnArt.CheckImage(png, stage, sprite.MaxFrame) is { } why)
            {
                Status = $"{stage.Name}: {why} {name} is unchanged.";
                return Task.CompletedTask;
            }
            art[stage.Id] = png;
        }
        if (art.Count == 0)
        {
            Status = $"No image was chosen, so {name} is unchanged.";
            return Task.CompletedTask;
        }
        var result = WidgetOwnArt.Apply(files, id, art);
        if (!result.Ok)
        {
            Status = $"{name} is unchanged: {result.Errors.FirstOrDefault() ?? "the pack didn't pass its checks."}";
            return Task.CompletedTask;
        }
        var installed = Service.Store.Install(result.Files!, result.Manifest!, WidgetSource.Local, replaceMadeHere: true);
        Status = installed.Ok
            ? $"{name} now uses your own art. It stays on this PC."
            : installed.Error ?? $"{name} couldn't be changed.";
        Rebuild();
        if (installed.Ok) Notify();
        return Task.CompletedTask;
    }

    private static string? DefaultPickImage(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "PNG sprite sheet (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
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
