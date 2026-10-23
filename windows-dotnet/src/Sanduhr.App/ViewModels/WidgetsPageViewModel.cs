using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One pack of the verified catalog: what it is, whether it is installed, and the one thing its button does.</summary>
public sealed partial class WidgetCatalogRowViewModel : ObservableObject
{
    private readonly WidgetsPageViewModel _owner;

    internal WidgetCatalogRowViewModel(WidgetsPageViewModel owner, WidgetCatalogEntry entry, InstalledWidget? installed,
        InstalledWidgetInfo? info, bool meetsMinimum)
    {
        _owner = owner;
        Entry = entry;
        Description = info?.Description ?? "";
        Preview = WidgetsPageViewModel.LoadPreview(info?.PreviewPath);
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

/// <summary>One installed pack, from the catalog or made here.</summary>
public sealed partial class InstalledWidgetRowViewModel : ObservableObject
{
    private readonly WidgetsPageViewModel _owner;

    internal InstalledWidgetRowViewModel(WidgetsPageViewModel owner, InstalledWidget row, InstalledWidgetInfo? info)
    {
        _owner = owner;
        Id = row.Id;
        Name = info?.Name ?? row.Id;
        Kind = info?.Kind ?? "";
        Version = row.Version;
        Description = info?.Description ?? "";
        Source = row.MadeHere ? SettingsNames.WidgetMadeHere : "from the catalog";
        Preview = WidgetsPageViewModel.LoadPreview(info?.PreviewPath);
        CanUseOwnArt = row.MadeHere && Kind == "sprite";
    }

    /// <summary>A sprite made here (Claude's or the user's own) takes the user's own PNG sheets (spec 3.8 section 6); a catalog
    /// pack never does.</summary>
    public bool CanUseOwnArt { get; }

    public string UseOwnArtName => $"Use my own art for {Name}";

    [RelayCommand]
    private Task UseOwnArt() => _owner.UseOwnArtAsync(Id, Name);

    public string Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Version { get; }
    public string Description { get; }
    public string Source { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string Detail => string.Join(" · ", new[] { Kind, Version, Source }.Where(s => s.Length > 0));
    public string UninstallName => $"{SettingsNames.WidgetUninstall} {Name}";
    public string RowName => string.Join(", ", new[] { Name, Kind, "version " + Version, Source }.Where(s => s.Length > 0));

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(Id, Name);

    public override string ToString() => RowName;
}

/// <summary>An installed sprite pack the companion can be: its id and its name.</summary>
public sealed record CompanionChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Settings ▸ Widgets (spec 3.8, sections 2 to 4): the Desk companion (which installed sprite, shown or not, roaming or
/// not), the verified catalog with Install and Update, the installed packs with Uninstall (made here ones included), and a
/// status line for the fetch and the checks. The catalog is fetched only when the page opens (<see cref="Activate"/>) or on
/// <see cref="SettingsNames.WidgetCatalogCheck"/>, and a pack only when the user installs it.
/// </summary>
public sealed partial class WidgetsPageViewModel : ObservableObject
{
    private readonly Lazy<WidgetCatalogService> _service;
    private readonly Lazy<SettingsStore> _settings;
    private Task? _refreshing;
    private bool _loading;

    internal WidgetsPageViewModel(Func<WidgetCatalogService>? service = null, SettingsStore? store = null)
    {
        _service = new Lazy<WidgetCatalogService>(service ?? (() => new WidgetCatalogService()));
        _settings = new Lazy<SettingsStore>(() => store ?? new SettingsStore(new Paths()));
    }

    /// <summary>settings.json, read only once a sprite is installed (the companion's choices live there).</summary>
    private SettingsStore Prefs => _settings.Value;

    /// <summary>Called after the companion's choice changes or a pack is installed or removed (the Desk reloads).</summary>
    public Action? Changed { get; set; }

    /// <summary>The installed sprite packs, the companion's choices.</summary>
    public ObservableCollection<CompanionChoice> Companions { get; } = [];

    [ObservableProperty] private CompanionChoice? _selectedCompanion;
    [ObservableProperty] private bool _companionShown;
    [ObservableProperty] private bool _companionRoams;
    [ObservableProperty] private bool _hasCompanions;
    [ObservableProperty] private string _companionStatus = "";

    public string CompanionPickName => SettingsNames.WidgetCompanionPick;
    public string CompanionShowName => SettingsNames.WidgetCompanionShow;
    public string CompanionRoamName => SettingsNames.WidgetCompanionRoam;

    partial void OnSelectedCompanionChanged(CompanionChoice? value)
    {
        if (_loading || value is null) return;
        Prefs.SaveCompanion(Prefs.LoadCompanion() with { Pack = value.Id });
        CompanionStatus = StatusFor(value.Id);
        Changed?.Invoke();
    }

    partial void OnCompanionRoamsChanged(bool value)
    {
        if (_loading) return;
        Prefs.SaveCompanion(Prefs.LoadCompanion() with { Roam = value });
        Changed?.Invoke();
    }

    /// <summary>Shown is the Desk's <c>companion</c> piece being placed: on puts it at its usual corner (bottom right) when it is
    /// hidden, off hides it. Where it sits is Settings ▸ Desk's, like every piece.</summary>
    partial void OnCompanionShownChanged(bool value)
    {
        if (_loading) return;
        var desk = Prefs.LoadDesk();
        var layout = new DeskArrangement(desk.Layout);
        bool placed = layout.Placement(CompanionSettings.Widget) is not null;
        if (value == placed) return;
        if (value) layout.Place(CompanionSettings.Widget, DeskArrangement.DefaultAnchor(CompanionSettings.Widget));
        else layout.Hide(CompanionSettings.Widget);
        Prefs.SaveDesk(desk with { Layout = layout.Layout });
        CompanionStatus = SelectedCompanion is { } c ? StatusFor(c.Id) : "";
        Changed?.Invoke();
    }

    /// <summary>Lists the installed sprites and reads the companion's choices. A pick that is no longer installed falls back
    /// to the first sprite, as the Desk does.</summary>
    internal void ReloadCompanions()
    {
        _loading = true;
        try
        {
            Companions.Clear();
            WidgetRegistry reg;
            try { reg = Service.Store.Load(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
            foreach (var row in reg.Packs)
                if (Service.Store.ReadInfo(row.Id) is { Kind: "sprite" } info)
                    Companions.Add(new CompanionChoice(row.Id, info.Name));
            HasCompanions = Companions.Count > 0;
            if (!HasCompanions)
            {
                SelectedCompanion = null;
                CompanionStatus = "";
                return;
            }
            var settings = Prefs.LoadCompanion();
            var active = settings.ActivePack(reg, id => Service.Store.ReadInfo(id)?.Kind);
            SelectedCompanion = Companions.FirstOrDefault(c => c.Id == active);
            CompanionRoams = settings.Roam;
            CompanionShown = new DeskArrangement(Prefs.LoadDesk().Layout).Placement(CompanionSettings.Widget) is not null;
            CompanionStatus = active is null ? "" : StatusFor(active);
        }
        finally { _loading = false; }
    }

    /// <summary>"Stage 2 of 4, Sprout. 5 coding days so far." and, when the Desk is off or the piece hidden, how to see it.</summary>
    private string StatusFor(string packId)
    {
        string line = "";
        try
        {
            var growth = CompanionGrowthState.Load(Path.Combine(Service.Store.Root, CompanionGrowthState.FileName));
            var file = Path.Combine(Service.Store.PackDir(packId), "sprite.json");
            if (File.Exists(file) && CompanionSprite.Parse(File.ReadAllBytes(file)) is { } sprite)
            {
                var stage = sprite.StageAt(growth.Growth);
                int at = sprite.Stages.ToList().IndexOf(stage) + 1;
                string days = growth.Days.Count == 1 ? "1 coding day" : $"{growth.Days.Count} coding days";
                line = sprite.Stages.Count > 1
                    ? $"Stage {at} of {sprite.Stages.Count}, {stage.Name}. {days} so far."
                    : $"{days} so far.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        var desk = Prefs.LoadDesk();
        if (!desk.Enabled) line = (line + " Turn the Desk on in Settings, Desk to see it.").Trim();
        else if (new DeskArrangement(desk.Layout).Placement(CompanionSettings.Widget) is null)
            line = (line + " It is hidden; switch on " + SettingsNames.WidgetCompanionShow + ".").Trim();
        return line;
    }

    internal WidgetCatalogService Service => _service.Value;

    public ObservableCollection<WidgetCatalogRowViewModel> Catalog { get; } = [];
    public ObservableCollection<InstalledWidgetRowViewModel> Installed { get; } = [];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _hasInstalled;
    [ObservableProperty] private bool _hasCatalog;

    public string CheckAgainTitle => SettingsNames.WidgetCatalogCheck;

    /// <summary>The page became visible: list what is installed and fetch the catalog once more.</summary>
    public Task Activate()
    {
        ReloadInstalled();
        ReloadCompanions();
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
            if (result.Ok) Changed?.Invoke();
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
        var result = Service.Store.Uninstall(id);
        Status = result.Ok ? $"{name} is uninstalled." : result.Error ?? "The widget couldn't be removed.";
        Rebuild();
        if (result.Ok) Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void Rebuild()
    {
        ReloadInstalled();
        ReloadCompanions();
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

    private void ReloadInstalled()
    {
        Installed.Clear();
        WidgetRegistry reg;
        try { reg = Service.Store.Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
        foreach (var row in reg.Packs)
            Installed.Add(new InstalledWidgetRowViewModel(this, row, Service.Store.ReadInfo(row.Id)));
        HasInstalled = Installed.Count > 0;
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
        if (installed.Ok) Changed?.Invoke();
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
