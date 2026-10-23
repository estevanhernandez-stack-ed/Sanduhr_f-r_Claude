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
    }

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

/// <summary>
/// Settings ▸ Widgets (spec 3.8, sections 2 and 3): the verified catalog with Install and Update, the
/// installed packs with Uninstall (made here ones included), and a status line for the fetch and the checks. The catalog is fetched
/// only when the page opens (<see cref="Activate"/>) or on <see cref="SettingsNames.WidgetCatalogCheck"/>, and a pack
/// only when the user installs it. Nothing renders yet: the Desk companion is the next step.
/// </summary>
public sealed partial class WidgetsPageViewModel : ObservableObject
{
    private readonly Lazy<WidgetCatalogService> _service;
    private Task? _refreshing;

    internal WidgetsPageViewModel(Func<WidgetCatalogService>? service = null)
    {
        _service = new Lazy<WidgetCatalogService>(service ?? (() => new WidgetCatalogService()));
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
