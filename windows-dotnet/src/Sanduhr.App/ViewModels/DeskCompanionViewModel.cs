using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>An installed sprite pack the companion can be: its id and its name.</summary>
public sealed record CompanionChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Desk, Pieces, the Companion row (spec, Desk: the split; it was Settings, Widgets, Desk companion): which installed sprite is the
/// companion, whether it roams, and how it is growing. Whether it shows is the row's Show box (desk_layout), not a switch of its
/// own. The keys are unchanged: desk_companion.pack and desk_companion.roam.
/// </summary>
public sealed partial class DeskCompanionViewModel : ObservableObject
{
    private readonly SettingsStore _prefs;
    private readonly Func<WidgetStore> _widgets;
    private bool _loading;

    internal DeskCompanionViewModel(SettingsStore prefs, Func<WidgetStore> widgets)
    {
        _prefs = prefs;
        _widgets = widgets;
    }

    /// <summary>Called after the sprite or roaming changes (the Desk reloads).</summary>
    public Action? Changed { get; set; }

    /// <summary>The installed sprite packs, the companion's choices.</summary>
    public ObservableCollection<CompanionChoice> Companions { get; } = [];

    [ObservableProperty] private CompanionChoice? _selectedCompanion;
    [ObservableProperty] private bool _roams;
    [ObservableProperty] private bool _hasCompanions;
    [ObservableProperty] private string _status = "";

    /// <summary>The row's first line (WF21).</summary>
    public string Line => SettingsNames.CompanionLine;
    public string PickName => SettingsNames.WidgetCompanionPick;
    public string RoamName => SettingsNames.WidgetCompanionRoam;
    public string NoneYet => SettingsNames.NoCompanionYet;
    public string GetOneTitle => SettingsNames.GetACompanion;
    /// <summary>The catalog, where a sprite is installed (the alias table carries it to Mods &amp; Widgets in slice 4).</summary>
    public string GetOneLink => SettingsIndex.PageId(SettingsPage.Widgets) + "#" + SettingsIndex.WidgetsCatalog;

    partial void OnSelectedCompanionChanged(CompanionChoice? value)
    {
        if (_loading || value is null) return;
        _prefs.SaveCompanion(_prefs.LoadCompanion() with { Pack = value.Id });
        Status = StatusFor(value.Id);
        Changed?.Invoke();
    }

    partial void OnRoamsChanged(bool value)
    {
        if (_loading) return;
        _prefs.SaveCompanion(_prefs.LoadCompanion() with { Roam = value });
        Changed?.Invoke();
    }

    /// <summary>Lists the installed sprites and reads the companion's choices. A pick that is no longer installed falls back to the
    /// first sprite, as the Desk does.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            Companions.Clear();
            var store = _widgets();
            WidgetRegistry reg;
            try { reg = store.Load(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
            foreach (var row in reg.Packs)
                if (store.ReadInfo(row.Id) is { Kind: "sprite" } info)
                    Companions.Add(new CompanionChoice(row.Id, info.Name));
            HasCompanions = Companions.Count > 0;
            if (!HasCompanions)
            {
                SelectedCompanion = null;
                Status = "";
                return;
            }
            var settings = _prefs.LoadCompanion();
            var active = settings.ActivePack(reg, id => store.ReadInfo(id)?.Kind);
            SelectedCompanion = Companions.FirstOrDefault(c => c.Id == active);
            Roams = settings.Roam;
            Status = active is null ? "" : StatusFor(active);
        }
        finally { _loading = false; }
    }

    /// <summary>The growth line again: the piece was shown or hidden, or the Desk turned on or off.</summary>
    public void RefreshStatus()
    {
        if (SelectedCompanion is { } c) Status = StatusFor(c.Id);
    }

    /// <summary>"Stage 2 of 4, Sprout. 5 coding days so far." and, when the Desk is off or the piece hidden, how to see it.</summary>
    private string StatusFor(string packId)
    {
        string line = "";
        try
        {
            var store = _widgets();
            var growth = CompanionGrowthState.Load(Path.Combine(store.Root, CompanionGrowthState.FileName));
            var file = Path.Combine(store.PackDir(packId), "sprite.json");
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
        var desk = _prefs.LoadDesk();
        if (!desk.Enabled) line = (line + " Turn the Desk on in Settings, Desk to see it.").Trim();
        else if (new DeskArrangement(desk.Layout).Placement(CompanionSettings.Widget) is null)
            line = (line + " It is hidden; tick " + SettingsNames.DeskCompanion + " to show it.").Trim();
        return line;
    }
}
