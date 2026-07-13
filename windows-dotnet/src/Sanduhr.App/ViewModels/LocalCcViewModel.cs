using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the breakdown tables — display name + compact tokens.</summary>
public sealed record BreakdownRow(string Name, string Tokens);

/// <summary>A per-root vault consent toggle in the stewardship strip.</summary>
public sealed partial class VaultRootToggleViewModel : ObservableObject
{
    private readonly LocalCcViewModel _owner;
    private bool _loading = true;

    public string Name { get; }

    [ObservableProperty] private bool _isEnabled;

    public VaultRootToggleViewModel(LocalCcViewModel owner, string name, bool enabled)
    {
        _owner = owner;
        Name = name;
        IsEnabled = enabled;
        _loading = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading)
            _owner.OnRootToggled(Name, value);
    }
}

/// <summary>
/// Overview section of the Claude Usage tab. Sourcing (spec, binding): the
/// vault's rollups serve days strictly BEFORE the hot boundary (local date of
/// the last successful ingest); the live reader serves hot days — never both
/// for one day. A stale vault (no ingest for 3 cycles) degrades the whole
/// window to the live path with a visible status line: frozen closed-day
/// numbers with no signal would be the design lying. Also owns the data-
/// stewardship strip — consent toggles, per-root purge, erase, open folder.
/// </summary>
public sealed partial class LocalCcViewModel : ObservableObject
{
    private const int LookbackDays = 30;
    private static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(15);   // 3 fetch cycles

    private readonly WidgetViewModel _widget;
    private readonly Action<bool> _persistShowBreakdowns;
    private Window? _owner;

    public event Action? Changed;

    [ObservableProperty] private string _todayText = "Loading…";
    [ObservableProperty] private string _monthText = "Loading…";
    [ObservableProperty] private string _todaySplitText = "";
    [ObservableProperty] private string _monthSplitText = "";
    [ObservableProperty] private bool _showBreakdowns;
    [ObservableProperty] private string _statusLine = "";

    public ObservableCollection<BreakdownRow> Projects { get; } = new();
    public ObservableCollection<BreakdownRow> Skills { get; } = new();
    public ObservableCollection<VaultRootToggleViewModel> Roots { get; } = new();

    private Dictionary<DateOnly, long> _byDay = new();
    public IReadOnlyDictionary<DateOnly, long> ByDay => _byDay;

    public ThemePalette Palette => _widget.Palette;

    public LocalCcViewModel(WidgetViewModel widget, bool showBreakdowns, Action<bool> persistShowBreakdowns)
    {
        _widget = widget;
        _showBreakdowns = showBreakdowns;
        _persistShowBreakdowns = persistShowBreakdowns;
        _widget.ThemeChanged += _ => Changed?.Invoke();
        RebuildRoots();
    }

    partial void OnShowBreakdownsChanged(bool value) => _persistShowBreakdowns(value);

    public void AttachOwner(Window owner) => _owner = owner;

    private void RebuildRoots()
    {
        Roots.Clear();
        if (_widget.Vault is not { } vault)
            return;
        var consented = new HashSet<string>(vault.ConsentedRootNames(), StringComparer.Ordinal);
        foreach (var root in vault.DetectedRootNames())
            Roots.Add(new VaultRootToggleViewModel(this, root, consented.Contains(root)));
    }

    /// <summary>Consent toggle: ON resumes archiving (backfill within one
    /// cycle); OFF stops it and OFFERS purge — consent-off is the tombstone
    /// either way, so "off" alone never silently re-backfills.</summary>
    internal void OnRootToggled(string root, bool on)
    {
        try
        {
            if (_widget.Vault is not { } vault)
                return;
            if (on)
            {
                vault.SetRootConsent(root, true);
                vault.TriggerIngest();
            }
            else
            {
                vault.SetRootConsent(root, false);
                var res = ThemedDialog.Show(_owner, $"Stop archiving {root}?",
                    "Archiving is off for this home either way. Erase the history already stored for it?",
                    MessageBoxButton.YesNo, ThemedDialogKind.Warning,
                    primaryLabel: "Erase it", secondaryLabel: "Keep data");
                if (res == MessageBoxResult.Yes)
                {
                    _ = PurgeRootAsync(vault, root);   // purge waits on the writer mutex — off the UI thread
                    return;
                }
            }
            _ = RefreshAsync();
        }
        catch
        {
            // The vault layer already logs faults — a UI-path exception here
            // must never take down the Settings window.
        }
    }

    /// <summary>Purge can sit up to 10s in the writer-mutex wait — never on the
    /// UI thread. Fire-and-forget from OnRootToggled; refresh resumes on the UI
    /// context after the purge lands.</summary>
    private async Task PurgeRootAsync(VaultService vault, string root)
    {
        try
        {
            await Task.Run(() => vault.PurgeRoot(root));
            await RefreshAsync();
        }
        catch
        {
            // The vault layer already logs faults — a UI-path exception here
            // must never take down the Settings window.
        }
    }

    [RelayCommand]
    private void OpenVaultFolder() => _widget.Vault?.OpenVaultFolder();

    [RelayCommand]
    private async Task EraseArchive()
    {
        try
        {
            if (_widget.Vault is not { } vault)
                return;
            var res = ThemedDialog.Show(_owner, "Erase usage history?",
                "Deletes the entire local vault and turns archiving off for every home. This cannot be undone.",
                MessageBoxButton.YesNo, ThemedDialogKind.Warning,
                primaryLabel: "Erase everything", secondaryLabel: "Cancel");
            if (res != MessageBoxResult.Yes)
                return;
            // Erase waits on the writer mutex (up to 10s) — off the UI thread;
            // the awaits resume on the UI context for the rebuild + refresh.
            await Task.Run(() => vault.EraseArchive());
            RebuildRoots();
            await RefreshAsync();
        }
        catch
        {
            // The vault layer already logs faults — a UI-path exception here
            // must never take down the Settings window.
        }
    }

    private sealed record OverviewData(
        Dictionary<DateOnly, long> ByDay,
        Dictionary<string, long> Projects,
        Dictionary<string, long> Skills,
        string StatusLine,
        long SentToday,
        long ReceivedToday,
        long SentWindow,
        long ReceivedWindow,
        bool WindowSplitPartial);

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        var reader = _widget.CcReader;
        OverviewData data;
        try
        {
            data = await Task.Run(() => Compute(vault, reader)).ConfigureAwait(true);
        }
        catch
        {
            data = new OverviewData(new(), new(), new(), "", 0, 0, 0, 0, false);
        }
        Apply(data);
    }

    private static OverviewData Compute(VaultService? vault, CcLogReader reader)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(LookbackDays - 1));
        var roots = vault?.ConsentedRootNames() ?? (IReadOnlyList<string>)Array.Empty<string>();
        var lastIngest = roots.Count > 0 ? vault!.Reader.LastSuccessfulIngestUtc(roots) : null;
        bool vaultOn = roots.Count > 0;
        bool degraded = vaultOn
            && (lastIngest is null || DateTimeOffset.UtcNow - lastIngest.Value > DegradedAfter);

        if (!vaultOn || degraded)
        {
            var agg = reader.AggregateForLocalCcTab(LookbackDays);
            var byName = new Dictionary<string, long>();
            foreach (var (cwd, v) in agg.ByProject)
            {
                var name = CcLogReader.ProjectDisplayName(cwd);
                byName[name] = byName.GetValueOrDefault(name) + v;
            }
            long sentW = agg.ByDayInput.Values.Sum();
            long recvW = agg.ByDayOutput.Values.Sum();
            long sentT = agg.ByDayInput.GetValueOrDefault(today);
            long recvT = agg.ByDayOutput.GetValueOrDefault(today);
            long liveTotal = agg.ByDay.Values.Sum();
            return new OverviewData(
                new Dictionary<DateOnly, long>(agg.ByDay), byName,
                new Dictionary<string, long>(agg.BySkill),
                !vaultOn ? "history vault off — these numbers are the live logs, not an archive"
                         : "history vault paused — showing live logs only",
                sentT, recvT, sentW, recvW,
                liveTotal > 0 && (sentW + recvW) < (long)(liveTotal * 0.95));
        }

        // Hot boundary: any day the vault hasn't confirmed since its midnight
        // serves live. With a fresh ingest that's just today; in the seconds
        // after midnight it's yesterday+today until the rollover ingest lands.
        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var live = reader.AggregateForLocalCcTab(LookbackDays);
        var todayAgg = reader.AggregateTodayOnly();
        var win = vault!.Reader.ReadWindow(roots, windowStart, hotStart);

        var byDay = new Dictionary<DateOnly, long>(win.ByDay);
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            byDay.Remove(d);   // exclusion rule: a hot day never reads from both
            if (live.ByDay.TryGetValue(d, out var v))
                byDay[d] = v;
        }

        var projects = new Dictionary<string, long>(win.ByProjectName);
        foreach (var (cwd, v) in todayAgg.ByProject)
        {
            var name = CcLogReader.ProjectDisplayName(cwd);
            projects[name] = projects.GetValueOrDefault(name) + v;
        }
        var skills = new Dictionary<string, long>(win.BySkill);
        foreach (var (skill, v) in todayAgg.BySkill)
            skills[skill] = skills.GetValueOrDefault(skill) + v;

        long sentWindow = win.ByDayInput.Values.Sum();
        long recvWindow = win.ByDayOutput.Values.Sum();
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            sentWindow += live.ByDayInput.GetValueOrDefault(d);
            recvWindow += live.ByDayOutput.GetValueOrDefault(d);
        }
        long sentToday = todayAgg.ByDayInput.GetValueOrDefault(today);
        long recvToday = todayAgg.ByDayOutput.GetValueOrDefault(today);
        long windowTotal = byDay.Values.Sum();
        bool partial = windowTotal > 0 && (sentWindow + recvWindow) < (long)(windowTotal * 0.95);

        return new OverviewData(byDay, projects, skills, "",
            sentToday, recvToday, sentWindow, recvWindow, partial);
    }

    private void Apply(OverviewData data)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        long todayTotal = data.ByDay.GetValueOrDefault(today);
        TodayText = todayTotal > 0 ? $"{TokenFormat.Compact(todayTotal)} tokens" : "No activity yet";

        long monthTotal = data.ByDay.Values.Sum();
        MonthText = monthTotal > 0 ? $"{TokenFormat.Compact(monthTotal)} tokens" : "No activity";

        TodaySplitText = todayTotal > 0
            ? $"↑ {TokenFormat.Compact(data.SentToday)} sent · ↓ {TokenFormat.Compact(data.ReceivedToday)} received"
            : "";
        MonthSplitText = monthTotal > 0
            ? $"↑ {TokenFormat.Compact(data.SentWindow)} sent · ↓ {TokenFormat.Compact(data.ReceivedWindow)} received"
              + (data.WindowSplitPartial ? " (partial)" : "")
            : "";

        StatusLine = data.StatusLine;
        _byDay = data.ByDay;

        Projects.Clear();
        foreach (var (name, tokens) in data.Projects.OrderByDescending(kv => kv.Value).Take(10))
            Projects.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Skills.Clear();
        foreach (var (name, tokens) in data.Skills.OrderByDescending(kv => kv.Value).Take(10))
            Skills.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Changed?.Invoke();
    }
}
