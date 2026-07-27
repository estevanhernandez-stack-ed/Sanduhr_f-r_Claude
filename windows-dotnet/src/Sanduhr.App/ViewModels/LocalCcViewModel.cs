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
    // False until the first FULL Compute lands — gates preview so a stray
    // early timer tick can only re-run the cheap vault-only pass, and so
    // purge/erase/toggle refreshes (always after the first full apply) never
    // flash a preview over live data.
    private bool _hasPresented;

    public event Action? Changed;

    [ObservableProperty] private string _todayText = "Loading…";
    [ObservableProperty] private string _monthText = "";
    [ObservableProperty] private string _todaySplitText = "";
    [ObservableProperty] private string _monthSplitText = "";
    [ObservableProperty] private bool _showBreakdowns;
    [ObservableProperty] private string _statusLine = "";

    public ObservableCollection<BreakdownRow> Projects { get; } = new();
    public ObservableCollection<BreakdownRow> Skills { get; } = new();
    public ObservableCollection<VaultRootToggleViewModel> Roots { get; } = new();

    private Dictionary<DateOnly, long> _byDay = new();
    public IReadOnlyDictionary<DateOnly, long> ByDay => _byDay;

    private HashSet<DateOnly> _uncovered = new();
    public IReadOnlySet<DateOnly> UncoveredDays => _uncovered;

    private Dictionary<DateOnly, long> _calendarByDay = new();
    public IReadOnlyDictionary<DateOnly, long> CalendarDays => _calendarByDay;

    private DateOnly _calendarWindowStart;
    public DateOnly CalendarWindowStart => _calendarWindowStart;

    public ThemePalette Palette => _widget.Palette;

    public LocalCcViewModel(WidgetViewModel widget, bool showBreakdowns, Action<bool> persistShowBreakdowns)
    {
        _widget = widget;
        _showBreakdowns = showBreakdowns;
        _persistShowBreakdowns = persistShowBreakdowns;
        _statuslineInstalled = widget.LoadStatuslineEnabled();
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

    // -- WS-E statusline bridge (spec 2026-07-12-statusline-mcp-design.md) ------

    /// <summary>True while the Claude Code statusline integration is installed —
    /// drives which of Install/Remove shows.</summary>
    [ObservableProperty] private bool _statuslineInstalled;

    /// <summary>One-line outcome under the buttons ("Installed to .claude…" /
    /// failure copy). Empty = hidden.</summary>
    [ObservableProperty] private string _statuslineStatusText = "";

    /// <summary>Inverse for the Install button's visibility (no inverse-bool
    /// converter in the resource set).</summary>
    public bool StatuslineNotInstalled => !StatuslineInstalled;

    partial void OnStatuslineInstalledChanged(bool value)
        => OnPropertyChanged(nameof(StatuslineNotInstalled));

    private StatuslineInstaller MakeStatuslineInstaller() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        _widget.StatuslineBinDir);

    /// <summary>Consent-gated install: detect CC homes, ask WHICH home (never a
    /// silent default — one of them can be an employer tenant), write the script,
    /// register the statusLine key with a timestamped backup, flip the writer on.</summary>
    [RelayCommand]
    private void InstallStatusline()
    {
        try
        {
            var installer = MakeStatuslineInstaller();
            var homes = installer.DetectCcHomes();
            if (homes.Count == 0)
            {
                StatuslineStatusText = "No Claude Code install found (no ~/.claude or ~/.claude-personal).";
                return;
            }
            var chosen = StatuslineConsentDialog.ShowConsent(
                _owner, homes, installer.ScriptPath, installer.SettingsPathFor);
            if (chosen is null)
                return;
            if (!installer.InstallScript())
            {
                StatuslineStatusText = "Couldn't write the statusline script — see sanduhr.log.";
                Sounds.PlayError();
                return;
            }
            if (!installer.Register(chosen))
            {
                StatuslineStatusText = $"Couldn't safely edit {chosen}\\settings.json (it didn't parse) — nothing was changed.";
                Sounds.PlayError();
                return;
            }
            _widget.SaveStatuslineCcHome(chosen);
            _widget.SaveStatuslineEnabled(true);   // writes the first snapshot from the cached fetch
            StatuslineInstalled = true;
            StatuslineStatusText = $"Installed to {chosen} — shows under the prompt at Claude Code's next refresh.";
            Sounds.PlaySaveConfirmation();
        }
        catch
        {
            StatuslineStatusText = "Install failed — see sanduhr.log.";
            Sounds.PlayError();
        }
    }

    /// <summary>Full removal: deregister from the SAME home the install chose,
    /// delete the script and the snapshot. Never touches a foreign statusLine.</summary>
    [RelayCommand]
    private void RemoveStatusline()
    {
        try
        {
            var installer = MakeStatuslineInstaller();
            var home = _widget.LoadStatuslineCcHome();
            bool deregistered = string.IsNullOrEmpty(home) || installer.Deregister(home);
            installer.RemoveScript();
            _widget.SaveStatuslineEnabled(false);   // deletes snapshot.json
            StatuslineInstalled = false;
            StatuslineStatusText = deregistered
                ? "Removed."
                : "Removed here, but settings.json couldn't be edited — delete its statusLine entry manually.";
        }
        catch
        {
            StatuslineStatusText = "Remove failed — see sanduhr.log.";
            Sounds.PlayError();
        }
    }

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
        bool WindowSplitPartial,
        HashSet<DateOnly> Uncovered,
        Dictionary<DateOnly, long> CalendarByDay,
        DateOnly CalendarStart);

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        var reader = _widget.CcReader;
        // First paint only: the vault serves closed days in milliseconds while
        // the first live walk takes tens of seconds — show what's fast, keep
        // the single "Loading…" on Today (the one live-only figure).
        if (!_hasPresented)
        {
            try
            {
                var preview = await Task.Run(() => ComputePreview(vault)).ConfigureAwait(true);
                if (preview is not null)
                    Apply(preview, previewToday: true);
            }
            catch
            {
                // Preview is best-effort; the full compute below is the source of truth.
            }
        }
        OverviewData data;
        try
        {
            data = await Task.Run(() => Compute(vault, reader)).ConfigureAwait(true);
        }
        catch
        {
            data = new OverviewData(new(), new(), new(), "", 0, 0, 0, 0, false, new(),
                new(), DateOnly.FromDateTime(DateTime.Now));
        }
        Apply(data);
        _hasPresented = true;
    }

    /// <summary>Head shared by <see cref="Compute"/> and <see cref="ComputePreview"/> —
    /// today/window/roots/hot-boundary framing plus the calendar coverage
    /// gaps. Neither caller's downstream logic depends on anything here beyond
    /// what's returned, so this is a plain extraction, not a rewrite.</summary>
    private static (DateOnly Today, DateOnly WindowStart, IReadOnlyList<string> Roots,
        DateTimeOffset? LastIngest, bool VaultOn, bool Degraded, DateOnly CalFrom,
        HashSet<DateOnly> Uncovered) ComputeHead(VaultService? vault)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(LookbackDays - 1));
        var roots = vault?.ConsentedRootNames() ?? (IReadOnlyList<string>)Array.Empty<string>();
        var lastIngest = roots.Count > 0 ? vault!.Reader.LastSuccessfulIngestUtc(roots) : null;
        bool vaultOn = roots.Count > 0;
        bool degraded = vaultOn
            && (lastIngest is null || DateTimeOffset.UtcNow - lastIngest.Value > DegradedAfter);

        var calFrom = today.AddDays(-34);
        var uncovered = new HashSet<DateOnly>();
        var covered = (vault is not null && roots.Count > 0)
            ? vault.Reader.CoveredSet(roots, calFrom, today)
            : new HashSet<DateOnly>();
        for (var d = calFrom; d <= today; d = d.AddDays(1))
        {
            if (!covered.Contains(d))
                uncovered.Add(d);
        }

        return (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered);
    }

    /// <summary>Fast vault-only snapshot for the first paint — never touches
    /// <paramref name="vault"/>'s live reader, so it returns in milliseconds
    /// while the live walk runs behind it. Null when the vault can't serve
    /// alone (off or degraded): those modes have no fast source, only the
    /// live walk, so there is nothing to preview.</summary>
    private static OverviewData? ComputePreview(VaultService? vault)
    {
        var (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered) = ComputeHead(vault);
        if (!vaultOn || degraded)
            return null;

        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var win = vault!.Reader.ReadWindow(roots, windowStart, hotStart);
        // Calendar-only head slice — same union grammar as Compute, still
        // disjoint from `win` by construction.
        var winCal = vault.Reader.ReadWindow(roots, calFrom, windowStart);

        var byDay = new Dictionary<DateOnly, long>(win.ByDay);   // no hot-day/live entries — those arrive in the full pass

        var calendarByDay = new Dictionary<DateOnly, long>(winCal.ByDay);
        foreach (var (d, v) in byDay)
            calendarByDay[d] = v;

        var projects = new Dictionary<string, long>(win.ByProjectName);
        var skills = new Dictionary<string, long>(win.BySkill);

        long sentWindow = win.ByDayInput.Values.Sum();
        long recvWindow = win.ByDayOutput.Values.Sum();
        long windowTotal = byDay.Values.Sum();
        bool partial = windowTotal > 0 && (sentWindow + recvWindow) < (long)(windowTotal * 0.95);

        return new OverviewData(byDay, projects, skills, "", 0, 0, sentWindow, recvWindow, partial, uncovered,
            calendarByDay, calFrom);
    }

    private static OverviewData Compute(VaultService? vault, CcLogReader reader)
    {
        var (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered) = ComputeHead(vault);

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
                liveTotal > 0 && (sentW + recvW) < (long)(liveTotal * 0.95),
                uncovered,
                new Dictionary<DateOnly, long>(agg.ByDay), windowStart);
        }

        // Hot boundary: any day the vault hasn't confirmed since its midnight
        // serves live. With a fresh ingest that's just today; in the seconds
        // after midnight it's yesterday+today until the rollover ingest lands.
        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var live = reader.AggregateForLocalCcTab(LookbackDays);
        var todayAgg = reader.AggregateTodayOnly();
        var win = vault!.Reader.ReadWindow(roots, windowStart, hotStart);
        // Calendar-only head slice — [calFrom, windowStart), never touches the
        // 30-day `win` read above, so every existing figure stays bit-identical.
        var winCal = vault.Reader.ReadWindow(roots, calFrom, windowStart);

        var byDay = new Dictionary<DateOnly, long>(win.ByDay);
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            byDay.Remove(d);   // exclusion rule: a hot day never reads from both
            if (live.ByDay.TryGetValue(d, out var v))
                byDay[d] = v;
        }

        // Calendar dict: [calFrom, windowStart) head slice + the 30-day byDay
        // (already windowStart..hotStart rollups + hot-day live entries) — the
        // three source intervals are disjoint by construction, so this is a
        // plain union, no arithmetic re-derivation.
        var calendarByDay = new Dictionary<DateOnly, long>(winCal.ByDay);
        foreach (var (d, v) in byDay)
            calendarByDay[d] = v;

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
            sentToday, recvToday, sentWindow, recvWindow, partial, uncovered,
            calendarByDay, calFrom);
    }

    private void Apply(OverviewData data, bool previewToday = false)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        long todayTotal = data.ByDay.GetValueOrDefault(today);
        // Preview's ByDay never carries hot-day/live entries, so todayTotal
        // reads 0 even when today has real activity — previewToday pins the
        // single "Loading…" instead of letting the 0-total path claim "No
        // activity yet" mid-walk.
        TodayText = previewToday
            ? "Loading…"
            : todayTotal > 0 ? $"{TokenFormat.Compact(todayTotal)} tokens" : "No activity yet";

        long monthTotal = data.ByDay.Values.Sum();
        MonthText = monthTotal > 0
            ? $"{TokenFormat.Compact(monthTotal)} tokens"
            : previewToday ? "" : "No activity";   // a brand-new vault mid-walk must not claim "No activity"

        TodaySplitText = !previewToday && todayTotal > 0
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

        _uncovered = data.Uncovered;
        _calendarByDay = data.CalendarByDay;
        _calendarWindowStart = data.CalendarStart;

        Changed?.Invoke();
    }
}
