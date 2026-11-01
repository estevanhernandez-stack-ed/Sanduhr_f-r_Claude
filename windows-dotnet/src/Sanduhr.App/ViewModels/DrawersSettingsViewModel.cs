using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Settings, Drawers (5.0 drawers spec): on and off, where files live, Sort my desktop now, the rules, the drawers and the recent
/// moves with Undo. Works with no drawer service (tests, the harness): the page opens empty.</summary>
public sealed partial class DrawersSettingsViewModel : ObservableObject, IDisposable
{
    private readonly DrawerService? _drawers;
    private readonly DrawerReviewService? _review;
    private readonly SettingsStore _store;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private bool _loading;
    private bool _disposed;

    internal DrawersSettingsViewModel(DrawerService? drawers, DrawerReviewService? review, SettingsStore store)
    {
        _drawers = drawers;
        _review = review;
        _store = store;
        if (_drawers is not null) _drawers.Changed += Refresh;
        Refresh();
    }

    /// <summary>The running Desk is told to start or stop drawing (the Desk page's own switch does the same through its Changed hook).</summary>
    public Action? DeskChanged { get; set; }

    /// <summary>Stops following the drawer service; the Settings window calls this when it closes.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_drawers is not null) _drawers.Changed -= Refresh;
        Flush();   // a drag that has not settled yet is not lost when Settings closes
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

    // ---- Drawer look and placement (look and placement spec, sections 1 and 2) ----
    [ObservableProperty] private int _lookIndex;
    [ObservableProperty] private int _opacity = DrawerStyle.DefaultOpacity;
    [ObservableProperty] private bool _snap = true;
    [ObservableProperty] private bool _keepClear = true;
    [ObservableProperty] private int _tidyCornerIndex;
    [ObservableProperty] private int _tidyFlowIndex;
    [ObservableProperty] private string _tidyStatus = "";

    /// <summary>Opacity shows when any look in force draws a plate (Solid, Glass): the shared look, or a drawer's own.</summary>
    public bool ShowOpacity =>
        (LookIndex is >= 0 and <= (int)DrawerLookKind.Bare && DrawerStyle.HasFill((DrawerLookKind)LookIndex))
        || (_drawers?.Settings.Looks.Values.Any(DrawerStyle.HasFill) ?? false);

    /// <summary>The slider's readout: "55%".</summary>
    public string OpacityText => $"{Opacity}%";

    /// <summary>Set by the app: lines the drawers up from the chosen corner and direction. Null when the Tidy bar opened, else the line to show.</summary>
    public Func<DrawerTidy, string?>? TidyRequested { get; set; }

    public void Refresh()
    {
        if (_disposed) return;
        _loading = true;
        try
        {
            if (_drawers is null) return;
            var s = _drawers.Settings;
            Enabled = s.Enabled;
            LookIndex = (int)s.Look;
            if (_pendingOpacity is null) Opacity = s.Opacity;   // a slider drag still settling keeps the value under the thumb
            Snap = s.Snap;
            KeepClear = s.KeepClear;
            TidyCornerIndex = (int)s.Tidy.Corner;
            TidyFlowIndex = (int)s.Tidy.Flow;
            RootText = StreamerMode.Mask.Paths(s.RootOr(_drawers.Places.Desktop));   // streamer: masked - Mask.Paths hides the profile folder while on
            CanChangeRoot = !s.Drawers.Any(d => d.Owned);
            Rules.Clear();
            for (int i = 0; i < s.Rules.Count; i++) Rules.Add(new DrawerRuleRow(this, i, s.Rules[i], NameOf(s.Rules[i].DrawerId)));
            DrawerRows.Clear();
            foreach (var d in s.Drawers) DrawerRows.Add(new DrawerRow(this, d));
            Recent.Clear();
            foreach (var e in _drawers.Journal.Load().Entries.OrderByDescending(e => e.AtUtc).Take(10)) Recent.Add(new DrawerRecentRow(this, e));
            OnPropertyChanged(nameof(NothingRecent));
            OnPropertyChanged(nameof(ShowOpacity));   // a drawer's own look may have changed without the shared one
        }
        finally { _loading = false; }
    }

    /// <summary>Streamer mode moved or the codenames changed (Settings' ReloadNames): the rows are built again, and the status line goes,
    /// since it is a sentence written with the names as they were then.</summary>
    public void ReloadNames()
    {
        Status = "";
        Refresh();
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
            try
            {
                var desk = _store.LoadDesk();
                if (!desk.Enabled) _store.SaveDesk(desk with { Enabled = true });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Status = SettingsNames.DrawersDeskSaveFailed;
                _ui.BeginInvoke(StayOff);   // like the turn-off revert: after the check box's binding has settled
                return;
            }
            _drawers.Save(s with { Enabled = true });
            _drawers.ShowAll();
            DeskChanged?.Invoke();   // the running Desk starts drawing the drawers, as the Desk page's own switch does
            Status = DrawerReview.FirstOnLine;
            return;
        }
        // The check box is still mid-update here: the question, and any revert, wait until its binding has settled.
        _ui.BeginInvoke(TurnOff);
    }

    /// <summary>Turning off, after the switch settled: a clash name that ran out means nothing can be planned (say why and stay on); a no to
    /// the count keeps drawers on; a yes returns the files.</summary>
    private void TurnOff()
    {
        if (_disposed || _drawers is null || Enabled || !_drawers.Settings.Enabled) return;
        if (_drawers.ReturnPlan() is not { } plan) { Status = DrawerService.NoFreeName; StayOn(); return; }
        if (plan.Moves.Count > 0 && !Confirm(DrawerReturn.Prompt(plan.Moves.Count))) { StayOn(); return; }
        DismissReview();   // a plan under review would move files into drawers that are going (final review I1)
        Status = Say(_drawers.TurnOff());
        Refresh();
        DeskChanged?.Invoke();
    }

    /// <summary>The switch goes back to off without running the on path again.</summary>
    private void StayOff()
    {
        if (_disposed) return;
        _loading = true;
        Enabled = false;
        _loading = false;
    }

    /// <summary>The switch goes back to on without running the on path again.</summary>
    private void StayOn()
    {
        _loading = true;
        Enabled = true;
        _loading = false;
    }

    /// <summary>A drawers review on the Desk ends before its drawers can change under it.</summary>
    private void DismissReview()
    {
        if (_review?.Reviewing == true) _review.Dismiss();
    }

    /// <summary>What a return said: the failure alone, else the result line (counts only while streamer mode is on) and what had to stay.</summary>
    private static string Say(DrawerReturnRun result) =>
        result.Failure ?? string.Join(" ", new[] { result.Run.Shown, result.KeptNote }.OfType<string>().Where(l => l.Length > 0));   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on

    [RelayCommand]
    private void SortNow() => Status = _review is null ? SettingsNames.DrawersSortUnavailable : StreamerMode.Mask.Paths(_review.SortNow() ?? "");   // streamer: masked - a refused plan's line can carry a path; Mask.Paths hides the profile folder while on

    [RelayCommand]
    private void ChangeRoot()
    {
        if (_drawers is null || !CanChangeRoot) { Status = SettingsNames.DrawersFolderLocked; return; }
        if (PickFolder() is not { } folder) return;
        var p = _drawers.Places;
        if (!DrawerPaths.IsSafeRoot(folder, p.Desktop, p.PublicDesktop)) { Status = SettingsNames.DrawersRootUnsafe; return; }
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

    /// <summary>Tidy drawers, from the corner and direction the page shows.</summary>
    [RelayCommand]
    private void Tidy() =>
        TidyStatus = TidyRequested?.Invoke(new DrawerTidy((DrawerCorner)TidyCornerIndex, (DrawerFlow)TidyFlowIndex)) ?? "";

    private void SaveWith(Func<DrawerSettings, DrawerSettings> change)
    {
        if (_loading || _drawers is null) return;
        Flush();   // a settling opacity lands first, so this change is made on top of it
        _drawers.Save(change(_drawers.Settings));
    }

    /// <summary>How long the opacity slider rests before its value is saved (a drag would otherwise save and rebuild the Desk on every tick).</summary>
    public static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(200);
    private DispatcherTimer? _settle;
    private int? _pendingOpacity;

    private void SaveOpacitySoon(int value)
    {
        if (_drawers is null) return;
        _pendingOpacity = value;
        if (_settle is null)
        {
            _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = SettleFor };
            _settle.Tick += (_, _) => Flush();
        }
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Saves a settling opacity now (the settle timer, any other change first, or closing Settings).</summary>
    public void Flush()
    {
        _settle?.Stop();
        if (_pendingOpacity is not { } pending) return;
        _pendingOpacity = null;
        if (_drawers is null || _drawers.Settings.Opacity == pending) return;
        _drawers.Save(_drawers.Settings with { Opacity = pending });
    }

    partial void OnLookIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowOpacity));
        if (value is < 0 or > (int)DrawerLookKind.Bare) return;   // a cleared combo box: the next Refresh puts the saved look back
        SaveWith(s => s with { Look = (DrawerLookKind)value });
    }

    partial void OnOpacityChanged(int value)
    {
        var clamped = Math.Clamp(value, DrawerStyle.MinOpacity, DrawerStyle.MaxOpacity);
        if (clamped != value) { Opacity = clamped; return; }
        OnPropertyChanged(nameof(OpacityText));
        if (!_loading) SaveOpacitySoon(clamped);
    }

    partial void OnSnapChanged(bool value) => SaveWith(s => s with { Snap = value });
    partial void OnKeepClearChanged(bool value) => SaveWith(s => s with { KeepClear = value });

    partial void OnTidyCornerIndexChanged(int value)
    {
        if (value is < 0 or > (int)DrawerCorner.BottomRight) return;
        SaveWith(s => s with { Tidy = s.Tidy with { Corner = (DrawerCorner)value } });
    }

    partial void OnTidyFlowIndexChanged(int value)
    {
        if (value is < 0 or > (int)DrawerFlow.Row) return;
        SaveWith(s => s with { Tidy = s.Tidy with { Flow = (DrawerFlow)value } });
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
        DismissReview();   // a plan under review may move files into this drawer (final review I1)
        var r = _drawers.Remove(id);
        Status = Say(r);
        Refresh();
    }

    internal void Undo(string entryId) { Status = _drawers?.Undo(entryId).Shown ?? ""; Refresh(); }   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on
}

/// <summary>One rule's row: what it matches, the drawer it fills, on or off, and its place in the order.</summary>
public sealed partial class DrawerRuleRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly int _index;

    internal DrawerRuleRow(DrawersSettingsViewModel page, int index, DrawerRule rule, string drawerName)
    {
        _page = page;
        _index = index;
        _on = rule.On;
        _days = rule.Days;
        _drawerName = drawerName;
        _rule = rule;
        Kind = rule.Kind;
    }

    private readonly string _drawerName;
    private readonly DrawerRule _rule;

    public DrawerRuleKind Kind { get; }

    /// <summary>"Screenshots go to Screenshots", "Files not opened in 30 days go to Older files": it follows the Days box.</summary>
    public string Label => DrawerRuleText.Sentence(_rule with { Days = Days }, _drawerName);

    // Each button names its own rule, so a screen reader can tell the rows apart.
    public string DeleteName => $"{SettingsNames.DrawersDelete} rule: {Label}";
    public string UpName => $"{SettingsNames.DrawersUp}, move rule up: {Label}";
    public string DownName => $"{SettingsNames.DrawersDown}, move rule down: {Label}";
    public string DaysName => $"Days for the rule: {Label}";
    public bool HasDays => Kind == DrawerRuleKind.OlderThan;

    /// <summary>The row's name for a screen reader (the list item reads as this, not as the type).</summary>
    public override string ToString() => Label;

    [ObservableProperty] private bool _on;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label), nameof(DeleteName), nameof(UpName), nameof(DownName), nameof(DaysName))] private int _days;

    partial void OnOnChanged(bool value) => _page.SaveRule(_index, r => r with { On = value });
    partial void OnDaysChanged(int value) => _page.SaveRule(_index, r => r with { Days = Math.Clamp(value, 1, 3650) });

    [RelayCommand] private void MoveUp() => _page.MoveRule(_index, -1);
    [RelayCommand] private void MoveDown() => _page.MoveRule(_index, +1);
    [RelayCommand] private void Delete() => _page.DeleteRule(_index);
}

/// <summary>One drawer's row: its name, what it is, and Rename and Remove.</summary>
public sealed partial class DrawerRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    internal DrawerRow(DrawersSettingsViewModel page, DrawerDef def)
    {
        _page = page;
        _id = def.Id;
        Name = StreamerMode.Mask.Name(CodenameCategory.Folder, def.Name);   // streamer: masked - Mask.Name gives the codename while on
        Kind = def.Owned ? SettingsNames.DrawersKindOwned : SettingsNames.DrawersKindView;
        _newName = StreamerMode.On ? "" : def.Name;   // streamer: masked - the real name only while streamer mode is off
    }

    public string Name { get; }
    public string Kind { get; }
    public string RemoveName => $"{SettingsNames.DrawersRemove} drawer: {Name}";
    public string RenameName => $"{SettingsNames.DrawersRename} drawer: {Name}";
    public string NewNameName => $"New name for {Name}";

    /// <summary>The row's name for a screen reader (the list item reads as this, not as the type).</summary>
    public override string ToString() => $"{Name}, {Kind}";

    [ObservableProperty] private string _newName;

    [RelayCommand] private void Rename() => _page.Rename(_id, NewName);
    [RelayCommand] private void Remove() => _page.Remove(_id);
}

/// <summary>One journal entry: "Today 10:42 · Rules · 14 files", with Undo.</summary>
public sealed partial class DrawerRecentRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    internal DrawerRecentRow(DrawersSettingsViewModel page, JournalEntry e)
    {
        _page = page;
        _id = e.Id;
        var local = e.AtUtc.ToLocalTime();
        var when = local.Date == DateTime.Today ? "Today " + local.ToString("t", CultureInfo.CurrentCulture) : local.ToString("g", CultureInfo.CurrentCulture);
        var who = e.Source switch { DrawerPlanSource.Rules => "Rules", DrawerPlanSource.Agent => e.Agent ?? "Agent", _ => "You" };
        Line = $"{when} · {who} · {e.Moves.Count} {(e.Moves.Count == 1 ? "file" : "files")}";
    }

    public string Line { get; }
    public string UndoName => $"{SettingsNames.DrawersUndo} these moves: {Line}";

    /// <summary>The row's name for a screen reader (the list item reads as this, not as the type).</summary>
    public override string ToString() => Line;

    [RelayCommand] private void Undo() => _page.Undo(_id);
}
