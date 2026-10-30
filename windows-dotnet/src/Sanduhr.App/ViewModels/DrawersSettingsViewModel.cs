using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Settings, Drawers (5.0 drawers spec): on and off, where files live, Sort my desktop now, the rules, the drawers and the recent
/// moves with Undo. Works with no drawer service (tests, the harness): the page opens empty.</summary>
internal sealed partial class DrawersSettingsViewModel : ObservableObject
{
    private readonly DrawerService? _drawers;
    private readonly DrawerReviewService? _review;
    private readonly SettingsStore _store;
    private bool _loading;

    public DrawersSettingsViewModel(DrawerService? drawers, DrawerReviewService? review, SettingsStore store)
    {
        _drawers = drawers;
        _review = review;
        _store = store;
        if (_drawers is not null) _drawers.Changed += Refresh;
        Refresh();
    }

    /// <summary>Asks a yes or no question (the turn-off count); the page sets a themed dialog.</summary>
    public Func<string, bool> Confirm { get; set; } = _ => false;

    /// <summary>Picks a folder; the page sets the system folder picker.</summary>
    public Func<string?> PickFolder { get; set; } = () => null;

    public ObservableCollection<DrawerRuleRow> Rules { get; } = [];
    public ObservableCollection<DrawerRow> DrawerRows { get; } = [];
    public ObservableCollection<DrawerRecentRow> Recent { get; } = [];
    public bool StoreNoteVisible { get; } = Startup.IsPackaged();

    /// <summary>The "Nothing has moved yet." line shows while the journal has no entries.</summary>
    public bool NothingRecent => Recent.Count == 0;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _rootText = "";
    [ObservableProperty] private bool _canChangeRoot;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newRuleText = "";
    [ObservableProperty] private string _newRuleDrawer = "";

    public void Refresh()
    {
        _loading = true;
        try
        {
            if (_drawers is null) return;
            var s = _drawers.Settings;
            Enabled = s.Enabled;
            RootText = StreamerMode.Mask.Paths(s.RootOr(_drawers.Places.Desktop));   // streamer: masked - Mask.Paths hides the profile folder while on
            CanChangeRoot = !s.Drawers.Any(d => d.Owned);
            Rules.Clear();
            for (int i = 0; i < s.Rules.Count; i++) Rules.Add(new DrawerRuleRow(this, i, s.Rules[i], NameOf(s.Rules[i].DrawerId)));
            DrawerRows.Clear();
            foreach (var d in s.Drawers) DrawerRows.Add(new DrawerRow(this, d));
            Recent.Clear();
            foreach (var e in _drawers.Journal.Load().Entries.OrderByDescending(e => e.AtUtc).Take(10)) Recent.Add(new DrawerRecentRow(this, e));
            OnPropertyChanged(nameof(NothingRecent));
        }
        finally { _loading = false; }
    }

    private string NameOf(string id) =>
        _drawers?.Settings.Drawers.FirstOrDefault(d => d.Id == id) is { } d
            ? StreamerMode.Mask.Name(CodenameCategory.Folder, d.Name)   // streamer: masked - Mask.Name gives the codename while on
            : DrawerReviewService.NameFromId(id);

    partial void OnEnabledChanged(bool value)
    {
        if (_loading || _drawers is null) return;
        var s = _drawers.Settings;
        if (value)
        {
            var desk = _store.LoadDesk();
            if (!desk.Enabled) _store.SaveDesk(desk with { Enabled = true });
            _drawers.Save(s with { Enabled = true });
            _drawers.ShowAll();
            Status = DrawerReview.FirstOnLine;
            return;
        }
        // A clash name that ran out means nothing can be planned: say why and stay on.
        if (_drawers.ReturnPlan() is not { } plan) { Status = DrawerService.NoFreeName; StayOn(); return; }
        if (plan.Moves.Count > 0 && !Confirm(DrawerReturn.Prompt(plan.Moves.Count))) { StayOn(); return; }
        Status = Say(_drawers.TurnOff());
        Refresh();
    }

    /// <summary>The switch goes back to on without running the on path again.</summary>
    private void StayOn()
    {
        _loading = true;
        Enabled = true;
        _loading = false;
    }

    /// <summary>What a return said: the failure alone, else the result line and what had to stay.</summary>
    private static string Say(DrawerReturnRun result) =>
        result.Failure ?? string.Join(" ", new[] { result.Run.Line, result.KeptNote }.OfType<string>().Where(l => l.Length > 0));

    [RelayCommand]
    private void SortNow() => Status = _review?.SortNow() ?? "";

    [RelayCommand]
    private void ChangeRoot()
    {
        if (_drawers is null || !CanChangeRoot) { Status = SettingsNames.DrawersFolderLocked; return; }
        if (PickFolder() is not { } folder) return;
        var p = _drawers.Places;
        if (!DrawerPaths.IsSafeRoot(folder, p.Desktop, p.PublicDesktop)) { Status = "The Drawers folder can't be the Desktop or a folder that holds it."; return; }
        _drawers.Save(_drawers.Settings with { Root = folder });
        Refresh();
    }

    [RelayCommand]
    private void AddNameRule()
    {
        if (_drawers is null || string.IsNullOrWhiteSpace(NewRuleText) || !DrawerNames.IsValid(NewRuleDrawer.Trim())) return;
        var s = _drawers.Settings;
        var id = s.Drawers.FirstOrDefault(d => string.Equals(d.Name, NewRuleDrawer.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
            ?? DrawerIds.From(NewRuleDrawer.Trim());
        var rule = new DrawerRule(DrawerRuleKind.NameContains, id, Text: NewRuleText.Trim());
        _drawers.Save(s with { Rules = [.. s.Rules, rule] });
        NewRuleText = "";
        NewRuleDrawer = "";
        Refresh();
    }

    [RelayCommand]
    private void ShowDrawers() => _drawers?.ShowAll();

    /// <summary>Drawers are free panels, so no saved place means "top right": clearing the places sends every drawer back there at the next render.</summary>
    [RelayCommand]
    private void ResetPlaces()
    {
        if (_drawers is null) return;
        _drawers.Save(_drawers.Settings with { Places = new Dictionary<string, DrawerPlace>() });
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_drawers is null || PickFolder() is not { } folder) return;
        var name = new DirectoryInfo(folder).Name;   // streamer: masked - the user's own folder name; the row draws it through Mask.Name
        Status = _drawers.AddView(name, folder) ?? "";
        Refresh();
    }

    // ---- rows ----

    internal void SaveRule(int index, Func<DrawerRule, DrawerRule> change)
    {
        if (_loading || _drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        rules[index] = change(rules[index]);
        _drawers.Save(_drawers.Settings with { Rules = rules });
    }

    internal void MoveRule(int index, int by)
    {
        if (_drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        int to = index + by;
        if (to < 0 || to >= rules.Count) return;
        (rules[index], rules[to]) = (rules[to], rules[index]);
        _drawers.Save(_drawers.Settings with { Rules = rules });
        Refresh();
    }

    internal void DeleteRule(int index)
    {
        if (_drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        rules.RemoveAt(index);
        _drawers.Save(_drawers.Settings with { Rules = rules });
        Refresh();
    }

    internal void Rename(string id, string name) { Status = _drawers?.RenameDrawer(id, name) ?? ""; Refresh(); }

    internal void Remove(string id)
    {
        if (_drawers is null) return;
        var r = _drawers.Remove(id);
        Status = Say(r);
        Refresh();
    }

    internal void Undo(string entryId) { Status = _drawers?.Undo(entryId).Line ?? ""; Refresh(); }
}

/// <summary>One rule's row: what it matches, the drawer it fills, on or off, and its place in the order.</summary>
internal sealed partial class DrawerRuleRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly int _index;

    public DrawerRuleRow(DrawersSettingsViewModel page, int index, DrawerRule rule, string drawerName)
    {
        _page = page;
        _index = index;
        _on = rule.On;
        _days = rule.Days;
        Kind = rule.Kind;
        Label = rule.Kind switch
        {
            DrawerRuleKind.Screenshots => "Screenshots",
            DrawerRuleKind.Images => "Images",
            DrawerRuleKind.Documents => "Documents",
            DrawerRuleKind.Installers => "Installers",
            DrawerRuleKind.Archives => "Archives",
            DrawerRuleKind.Shortcuts => "Shortcuts",
            DrawerRuleKind.OlderThan => "Not touched in",
            DrawerRuleKind.NameContains => $"Name contains \"{rule.Text}\"",
            _ => $"Name starts with \"{rule.Text}\"",
        } + $" to {drawerName}";
    }

    public DrawerRuleKind Kind { get; }
    public string Label { get; }
    public bool HasDays => Kind == DrawerRuleKind.OlderThan;

    [ObservableProperty] private bool _on;
    [ObservableProperty] private int _days;

    partial void OnOnChanged(bool value) => _page.SaveRule(_index, r => r with { On = value });
    partial void OnDaysChanged(int value) => _page.SaveRule(_index, r => r with { Days = Math.Clamp(value, 1, 3650) });

    [RelayCommand] private void MoveUp() => _page.MoveRule(_index, -1);
    [RelayCommand] private void MoveDown() => _page.MoveRule(_index, +1);
    [RelayCommand] private void Delete() => _page.DeleteRule(_index);
}

/// <summary>One drawer's row: its name, what it is, and Rename and Remove.</summary>
internal sealed partial class DrawerRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    public DrawerRow(DrawersSettingsViewModel page, DrawerDef def)
    {
        _page = page;
        _id = def.Id;
        Name = StreamerMode.Mask.Name(CodenameCategory.Folder, def.Name);   // streamer: masked - Mask.Name gives the codename while on
        Kind = def.Owned ? "In the Drawers folder" : "Shows a folder in place";
        _newName = StreamerMode.On ? "" : def.Name;   // streamer: masked - the real name only while streamer mode is off
    }

    public string Name { get; }
    public string Kind { get; }

    [ObservableProperty] private string _newName;

    [RelayCommand] private void Rename() => _page.Rename(_id, NewName);
    [RelayCommand] private void Remove() => _page.Remove(_id);
}

/// <summary>One journal entry: "Today 10:42 · Rules · 14 files", with Undo.</summary>
internal sealed partial class DrawerRecentRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    public DrawerRecentRow(DrawersSettingsViewModel page, JournalEntry e)
    {
        _page = page;
        _id = e.Id;
        var local = e.AtUtc.ToLocalTime();
        var when = local.Date == DateTime.Today ? "Today " + local.ToString("t", CultureInfo.CurrentCulture) : local.ToString("g", CultureInfo.CurrentCulture);
        var who = e.Source switch { DrawerPlanSource.Rules => "Rules", DrawerPlanSource.Agent => e.Agent ?? "Agent", _ => "You" };
        Line = $"{when} · {who} · {e.Moves.Count} {(e.Moves.Count == 1 ? "file" : "files")}";
    }

    public string Line { get; }

    [RelayCommand] private void Undo() => _page.Undo(_id);
}
