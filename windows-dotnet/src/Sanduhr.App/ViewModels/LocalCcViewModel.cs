using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the breakdown tables — display name + compact tokens.</summary>
public sealed record BreakdownRow(string Name, string Tokens, string Real = "", CodenameCategory Category = CodenameCategory.Project)
{
    /// <summary>What a screen reader reads for the row: "my-project, 1.2M" (the codename while streamer mode hides it).</summary>
    public override string ToString() => $"{Name}, {Tokens}";

    /// <summary>The row's menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => Real.Length > 0 && Name != Real;
}

/// <summary>A per-root vault consent toggle in the stewardship strip.</summary>
public sealed partial class VaultRootToggleViewModel : ObservableObject
{
    private readonly LocalCcViewModel _owner;
    private bool _loading = true;

    public string Name { get; }

    /// <summary>The folder as shown: its codename while streamer mode hides it (slice 7, CP 11). <see cref="Name"/> stays the key.</summary>
    public string Shown { get; private set; }

    [ObservableProperty] private bool _isEnabled;

    public VaultRootToggleViewModel(LocalCcViewModel owner, string name, bool enabled)
    {
        _owner = owner;
        Name = name;
        Shown = ShownFor(name);   // streamer: masked - agent names are product names, folders pass Mask.FolderName in ShownFor
        IsEnabled = enabled;
        _loading = false;
    }

    /// <summary>Redraws the folder's name after streamer mode flips or Shuffle draws new codenames (the checkbox is built once).</summary>
    public void RefreshShown()
    {
        Shown = ShownFor(Name);   // streamer: masked - agent names are product names, folders pass Mask.FolderName in ShownFor
        OnPropertyChanged(nameof(Shown));
    }

    /// <summary>A root as the page names it: an agent by its product name (never masked as a folder), a folder through the mask.</summary>
    internal static string ShownFor(string name)
        => AgentSources.Burn.Contains(name) && name != AgentSources.Claude
            ? AgentSources.BurnName(name)
            : StreamerMode.Mask.FolderName(name);   // streamer: masked - CP 11: Mask.FolderName masks on this line

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading)
            _owner.OnRootToggled(Name, value);
    }

    public override string ToString() => Shown;
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
    // True once the first full compute has presented: the breakdown's "none" lines wait for it.
    [ObservableProperty] private bool _hasLoaded;

    public ObservableCollection<BreakdownRow> Projects { get; } = new();
    public ObservableCollection<BreakdownRow> Skills { get; } = new();
    public ObservableCollection<VaultRootToggleViewModel> Roots { get; } = new();

    private Dictionary<DateOnly, long> _byDay = new();
    public IReadOnlyDictionary<DateOnly, long> ByDay => _byDay;

    /// <summary>Each non-Claude agent's per-day tokens in the 30-day window (spec 3). Claude Code's share of a day is the day's total minus these.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> ByDayByAgent { get; private set; }
        = new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>();

    private IReadOnlyDictionary<string, Dictionary<string, long>> _projectsByAgent
        = new Dictionary<string, Dictionary<string, long>>();

    private HashSet<DateOnly> _uncovered = new();
    public IReadOnlySet<DateOnly> UncoveredDays => _uncovered;

    /// <summary>True when an agent root is archived: the strip's empty line then speaks for every agent, not Claude Code alone.</summary>
    public bool AgentsConsented { get; private set; }

    private Dictionary<DateOnly, long> _calendarByDay = new();
    public IReadOnlyDictionary<DateOnly, long> CalendarDays => _calendarByDay;

    private DateOnly _calendarWindowStart;
    public DateOnly CalendarWindowStart => _calendarWindowStart;

    public ThemePalette Palette => _widget.Palette;

    private void OnThemeChanged(ThemePalette _) => Changed?.Invoke();

    /// <summary>Stop following the widget's theme (Settings closed): the widget outlives the window.</summary>
    public void Detach() => _widget.ThemeChanged -= OnThemeChanged;

    public LocalCcViewModel(WidgetViewModel widget, bool showBreakdowns, Action<bool> persistShowBreakdowns)
    {
        _widget = widget;
        _showBreakdowns = showBreakdowns;
        _persistShowBreakdowns = persistShowBreakdowns;
        _widget.ThemeChanged += OnThemeChanged;
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
        foreach (var root in vault.DetectedRootNames())   // streamer: masked - via VaultRootToggleViewModel: its Shown is Mask.FolderName (CP 11)
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
                var res = ThemedDialog.Show(_owner, StopArchivingQuestion(root), StopArchivingDetail(root),
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
            var res = ThemedDialog.Show(_owner, "Erase the session archive?",
                "Deletes the whole session archive and turns archiving off for every folder. This cannot be undone.",
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

    internal sealed record OverviewData(
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
        DateOnly CalendarStart,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>>? ByDayByAgent = null,
        IReadOnlyDictionary<string, Dictionary<string, long>>? ProjectsByAgent = null,
        bool AgentsConsented = false);

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
        HasLoaded = true;
    }

    /// <summary>Head shared by <see cref="Compute"/> and <see cref="ComputePreview"/> —
    /// today/window/roots/hot-boundary framing plus the calendar coverage
    /// gaps. Neither caller's downstream logic depends on anything here beyond
    /// what's returned, so this is a plain extraction, not a rewrite.</summary>
    /// <remarks>The archive decisions (on, hot boundary, degraded, coverage) read the Claude Code roots only: an agent root that is
    /// new, idle or failing never pauses Claude Code's archive, and an agent alone never moves the page off the live logs.</remarks>
    private static (DateOnly Today, DateOnly WindowStart, IReadOnlyList<string> Roots,
        DateTimeOffset? LastIngest, bool VaultOn, bool Degraded, DateOnly CalFrom,
        HashSet<DateOnly> Uncovered) ComputeHead(VaultReader? vaultReader, IReadOnlyList<string> roots)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(LookbackDays - 1));
        var claudeRoots = vaultReader is null
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : roots.Where(r => IsClaudeRoot(vaultReader, r)).ToList();
        var lastIngest = claudeRoots.Count > 0 ? vaultReader!.LastSuccessfulIngestUtc(claudeRoots) : null;
        bool vaultOn = claudeRoots.Count > 0;
        bool degraded = vaultOn
            && (lastIngest is null || DateTimeOffset.UtcNow - lastIngest.Value > DegradedAfter);

        var calFrom = today.AddDays(-34);
        var uncovered = new HashSet<DateOnly>();
        var covered = vaultOn
            ? vaultReader!.CoveredSet(claudeRoots, calFrom, today)
            : new HashSet<DateOnly>();
        for (var d = calFrom; d <= today; d = d.AddDays(1))
        {
            if (!covered.Contains(d))
                uncovered.Add(d);
        }

        return (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered);
    }

    /// <summary>A Claude Code home: its meta names Claude Code and its name is not an agent's. The name check covers an agent
    /// root the ingester has not written a meta for yet, which <see cref="VaultReader.AgentOf"/> reads as Claude Code.</summary>
    internal static bool IsClaudeRoot(VaultReader vaultReader, string root)
        => vaultReader.AgentOf(root) == AgentSources.Claude && !IsAgentRoot(root);

    /// <summary>What the agent roots add to the page: the window split per agent (strip, shares) and the hot days (from the
    /// hot boundary through today), which the live Claude Code reader never serves, so they are added to the totals here.</summary>
    internal sealed record AgentReads(
        Dictionary<string, IReadOnlyDictionary<DateOnly, long>> ByDay,
        Dictionary<string, Dictionary<string, long>> Projects,
        Dictionary<DateOnly, long> HotDay,
        Dictionary<string, long> HotProjects,
        Dictionary<string, long> HotSkills,
        Dictionary<DateOnly, long> HotIn,
        Dictionary<DateOnly, long> HotOut);

    /// <summary>Reads each non-Claude agent root on its own over [windowStart, today] (spec 3). Agents have no live reader, so the
    /// Vault is their only source and a hot day reads from it too; the combined <c>win</c> read excludes hot days for every root,
    /// so nothing counts twice. An agent with no tokens in the window is dropped: an idle consented root changes nothing on screen.</summary>
    internal static AgentReads ReadAgents(VaultReader reader, IReadOnlyList<string> consentedRoots, DateOnly windowStart,
        DateOnly hotStart, DateOnly today)
    {
        var byAgent = new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>(StringComparer.Ordinal);
        var projectsByAgent = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var hotDay = new Dictionary<DateOnly, long>();
        var hotProjects = new Dictionary<string, long>(StringComparer.Ordinal);   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
        var hotSkills = new Dictionary<string, long>(StringComparer.Ordinal);   // streamer: key - skill tokens summed into a dictionary; Rows masks when listed (CP 15)
        var hotIn = new Dictionary<DateOnly, long>();
        var hotOut = new Dictionary<DateOnly, long>();
        var end = today.AddDays(1);
        foreach (var root in consentedRoots.Where(r => reader.AgentOf(r) != AgentSources.Claude))
        {
            var agent = reader.AgentOf(root);
            var w = reader.ReadWindow([root], windowStart, end);
            if (w.ByDay.Values.Sum() <= 0)
                continue;
            byAgent[agent] = w.ByDay;
            foreach (var (name, tokens) in w.ByProjectName)   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
            {
                if (!projectsByAgent.TryGetValue(name, out var m)) projectsByAgent[name] = m = new(StringComparer.Ordinal);
                m[agent] = m.GetValueOrDefault(agent) + tokens;
            }
            if (hotStart > today)
                continue;
            var hot = reader.ReadWindow([root], hotStart, end);
            foreach (var (d, v) in hot.ByDay) hotDay[d] = hotDay.GetValueOrDefault(d) + v;
            foreach (var (n, v) in hot.ByProjectName) hotProjects[n] = hotProjects.GetValueOrDefault(n) + v;   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
            foreach (var (k, v) in hot.BySkill) hotSkills[k] = hotSkills.GetValueOrDefault(k) + v;   // streamer: key - skill tokens summed into a dictionary; Rows masks when listed (CP 15)
            foreach (var (d, v) in hot.ByDayInput) hotIn[d] = hotIn.GetValueOrDefault(d) + v;
            foreach (var (d, v) in hot.ByDayOutput) hotOut[d] = hotOut.GetValueOrDefault(d) + v;
        }
        return new AgentReads(byAgent, projectsByAgent, hotDay, hotProjects, hotSkills, hotIn, hotOut);
    }

    /// <summary>The live path's agent read: the whole window [windowStart, today] from the Vault. No Vault, or one that can't be
    /// read, gives nothing, so Claude Code's live figures still show.</summary>
    private static AgentReads ReadAgentsOrNone(VaultReader? vaultReader, IReadOnlyList<string> consentedRoots, DateOnly windowStart,
        DateOnly today)
    {
        if (vaultReader is not null)
        {
            try
            {
                return ReadAgents(vaultReader, consentedRoots, windowStart, windowStart, today);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // An unreadable Vault drops the agents' figures; the live Claude Code logs still answer.
            }
        }
        return new AgentReads(new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(), new(StringComparer.Ordinal),
            new(StringComparer.Ordinal), new(), new());
    }

    /// <summary>Adds the agents' hot-day burn to the combined maps the page shows.</summary>
    private static void AddHot(AgentReads agents, Dictionary<DateOnly, long> byDay, Dictionary<string, long> projects,
        Dictionary<string, long> skills)
    {
        foreach (var (d, v) in agents.HotDay) byDay[d] = byDay.GetValueOrDefault(d) + v;
        foreach (var (n, v) in agents.HotProjects) projects[n] = projects.GetValueOrDefault(n) + v;   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
        foreach (var (k, v) in agents.HotSkills) skills[k] = skills.GetValueOrDefault(k) + v;   // streamer: key - skill tokens summed into a dictionary; Rows masks when listed (CP 15)
    }

    /// <summary>Fast vault-only snapshot for the first paint — never touches
    /// <paramref name="vault"/>'s live reader, so it returns in milliseconds
    /// while the live walk runs behind it. Null when the vault can't serve
    /// alone (off or degraded): those modes have no fast source, only the
    /// live walk, so there is nothing to preview.</summary>
    private static OverviewData? ComputePreview(VaultService? vault)
        => ComputePreview(vault?.Reader, vault?.ConsentedRootNames() ?? (IReadOnlyList<string>)Array.Empty<string>());

    /// <summary>The preview over a Vault reader and the consented roots (Claude Code homes and agents); tests call it directly.</summary>
    internal static OverviewData? ComputePreview(VaultReader? vaultReader, IReadOnlyList<string> consented)
    {
        var (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered) = ComputeHead(vaultReader, consented);
        if (!vaultOn || degraded)
            return null;

        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var win = vaultReader!.ReadWindow(roots, windowStart, hotStart);
        // Calendar-only head slice — same union grammar as Compute, still
        // disjoint from `win` by construction.
        var winCal = vaultReader.ReadWindow(roots, calFrom, windowStart);
        var agents = ReadAgents(vaultReader, roots, windowStart, hotStart, today);

        var byDay = new Dictionary<DateOnly, long>(win.ByDay);   // no live Claude Code hot-day entries (those arrive in the full pass); the agents' Vault hot days are added below

        var calendarByDay = new Dictionary<DateOnly, long>(winCal.ByDay);
        foreach (var (d, v) in byDay)
            calendarByDay[d] = v;

        var projects = new Dictionary<string, long>(win.ByProjectName);
        var skills = new Dictionary<string, long>(win.BySkill);   // streamer: key - skill tokens copied into a dictionary; Rows masks when listed (CP 15)
        AddHot(agents, byDay, projects, skills);
        foreach (var (d, v) in agents.HotDay)
            calendarByDay[d] = byDay[d];

        long sentWindow = win.ByDayInput.Values.Sum() + agents.HotIn.Values.Sum();
        long recvWindow = win.ByDayOutput.Values.Sum() + agents.HotOut.Values.Sum();
        long windowTotal = byDay.Values.Sum();
        bool partial = windowTotal > 0 && (sentWindow + recvWindow) < (long)(windowTotal * 0.95);

        return new OverviewData(byDay, projects, skills, "", 0, 0, sentWindow, recvWindow, partial, uncovered,
            calendarByDay, calFrom, agents.ByDay, agents.Projects, roots.Any(IsAgentRoot));
    }

    private static OverviewData Compute(VaultService? vault, CcLogReader reader)
        => Compute(vault?.Reader, vault?.ConsentedRootNames() ?? (IReadOnlyList<string>)Array.Empty<string>(), reader);

    /// <summary>The full pass over a Vault reader, the consented roots (Claude Code homes and agents) and the live Claude Code
    /// reader; tests call it directly.</summary>
    internal static OverviewData Compute(VaultReader? vaultReader, IReadOnlyList<string> consented, CcLogReader reader)
    {
        var (today, windowStart, roots, lastIngest, vaultOn, degraded, calFrom, uncovered) = ComputeHead(vaultReader, consented);

        if (!vaultOn || degraded)
        {
            var agg = reader.AggregateForLocalCcTab(LookbackDays);
            var byName = new Dictionary<string, long>();
            foreach (var (cwd, v) in agg.ByProject)
            {
                var name = CcLogReader.ProjectDisplayName(cwd);   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
                byName[name] = byName.GetValueOrDefault(name) + v;
            }
            var liveByDay = new Dictionary<DateOnly, long>(agg.ByDay);
            var liveSkills = new Dictionary<string, long>(agg.BySkill);   // streamer: key - skill tokens copied into a dictionary; Rows masks when listed (CP 15)
            var liveCalendar = new Dictionary<DateOnly, long>(agg.ByDay);
            // Agents have no live reader: their whole window comes from the Vault, read as "hot" from the window's first day,
            // so it adds to the live Claude Code figures here exactly as the hot days do on the archive path.
            var liveAgents = ReadAgentsOrNone(vaultReader, roots, windowStart, today);
            AddHot(liveAgents, liveByDay, byName, liveSkills);
            foreach (var (d, v) in liveAgents.HotDay)
                liveCalendar[d] = liveCalendar.GetValueOrDefault(d) + v;
            long sentW = agg.ByDayInput.Values.Sum() + liveAgents.HotIn.Values.Sum();
            long recvW = agg.ByDayOutput.Values.Sum() + liveAgents.HotOut.Values.Sum();
            long sentT = agg.ByDayInput.GetValueOrDefault(today) + liveAgents.HotIn.GetValueOrDefault(today);
            long recvT = agg.ByDayOutput.GetValueOrDefault(today) + liveAgents.HotOut.GetValueOrDefault(today);
            long liveTotal = liveByDay.Values.Sum();
            return new OverviewData(
                liveByDay, byName, liveSkills,
                !vaultOn ? "session archive off, these numbers are the live logs, not an archive"
                         : "session archive paused, showing live logs only",
                sentT, recvT, sentW, recvW,
                liveTotal > 0 && (sentW + recvW) < (long)(liveTotal * 0.95),
                uncovered,
                liveCalendar, windowStart, liveAgents.ByDay, liveAgents.Projects, roots.Any(IsAgentRoot));
        }

        // Hot boundary: any day the vault hasn't confirmed since its midnight
        // serves live. With a fresh ingest that's just today; in the seconds
        // after midnight it's yesterday+today until the rollover ingest lands.
        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var live = reader.AggregateForLocalCcTab(LookbackDays);
        var todayAgg = reader.AggregateTodayOnly();
        var win = vaultReader!.ReadWindow(roots, windowStart, hotStart);
        // Calendar-only head slice — [calFrom, windowStart), never touches the
        // 30-day `win` read above, so every existing figure stays bit-identical.
        var winCal = vaultReader.ReadWindow(roots, calFrom, windowStart);
        var agents = ReadAgents(vaultReader, roots, windowStart, hotStart, today);

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
            var name = CcLogReader.ProjectDisplayName(cwd);   // streamer: key - project display name used as a dictionary key; Rows masks when listed (CP 15)
            projects[name] = projects.GetValueOrDefault(name) + v;
        }
        var skills = new Dictionary<string, long>(win.BySkill);   // streamer: key - skill tokens summed into a dictionary; Rows masks when listed (CP 15)
        foreach (var (skill, v) in todayAgg.BySkill)
            skills[skill] = skills.GetValueOrDefault(skill) + v;
        foreach (var (d, v) in agents.HotDay)
            calendarByDay[d] = calendarByDay.GetValueOrDefault(d) + v;   // the calendar union above already holds the live hot days
        AddHot(agents, byDay, projects, skills);

        long sentWindow = win.ByDayInput.Values.Sum();
        long recvWindow = win.ByDayOutput.Values.Sum();
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            sentWindow += live.ByDayInput.GetValueOrDefault(d);
            recvWindow += live.ByDayOutput.GetValueOrDefault(d);
        }
        sentWindow += agents.HotIn.Values.Sum();
        recvWindow += agents.HotOut.Values.Sum();
        long sentToday = todayAgg.ByDayInput.GetValueOrDefault(today) + agents.HotIn.GetValueOrDefault(today);
        long recvToday = todayAgg.ByDayOutput.GetValueOrDefault(today) + agents.HotOut.GetValueOrDefault(today);
        long windowTotal = byDay.Values.Sum();
        bool partial = windowTotal > 0 && (sentWindow + recvWindow) < (long)(windowTotal * 0.95);

        return new OverviewData(byDay, projects, skills, "",
            sentToday, recvToday, sentWindow, recvWindow, partial, uncovered,
            calendarByDay, calFrom, agents.ByDay, agents.Projects, roots.Any(IsAgentRoot));
    }

    /// <summary>"Stop archiving …?" naming the root as its checkbox does (slice 7, CP 11): a folder through the mask, an agent by
    /// its product name.</summary>
    internal static string StopArchivingQuestion(string root) => $"Stop archiving {VaultRootToggleViewModel.ShownFor(root)}?";

    /// <summary>The stop dialog's body: a folder's history, or an agent's token counts.</summary>
    internal static string StopArchivingDetail(string root) => IsAgentRoot(root)
        ? $"Archiving is off for {AgentSources.BurnName(root)} either way. Its token counts already in the archive stay until you delete them. Erase them now?"
        : "Archiving is off for this folder either way. Erase the history already stored for it?";

    private static bool IsAgentRoot(string root) => AgentSources.Burn.Contains(root) && root != AgentSources.Claude;

    /// <summary>A top-ten table's rows (slice 7, CP 15): each name as shown, with its real name kept for the quick route.</summary>
    internal static IReadOnlyList<BreakdownRow> Rows(IEnumerable<(string Name, long Tokens)> rows, CodenameCategory category,
        IReadOnlyDictionary<string, Dictionary<string, long>>? byAgent = null) =>
        [.. rows.Select(r => new BreakdownRow(StreamerMode.Mask.Name(category, r.Name), TokensText(r.Name, r.Tokens, byAgent), r.Name, category))];

    /// <summary>A row's figure: today's compact tokens, or with several agents the same plus each agent's share (spec 3).</summary>
    private static string TokensText(string name, long tokens, IReadOnlyDictionary<string, Dictionary<string, long>>? byAgent)
    {
        if (byAgent is null || !byAgent.TryGetValue(name, out var agents))
            return TokenFormat.Compact(tokens);
        var split = new Dictionary<string, long>(agents, StringComparer.Ordinal);
        split[AgentSources.Claude] = Math.Max(0, tokens - split.Values.Sum());
        return AgentShares.Describe(tokens, split);
    }

    private IReadOnlyList<(string Name, long Tokens)> _topProjects = [];
    private IReadOnlyList<(string Name, long Tokens)> _topSkills = [];

    /// <summary>Rebuild the two tables from the last data applied. Rows are made on display, so a codename is assigned for the ten shown
    /// and never for the whole archive.</summary>
    private void FillBreakdowns()
    {
        Projects.Clear();
        foreach (var row in Rows(_topProjects, CodenameCategory.Project, _projectsByAgent))
            Projects.Add(row);
        Skills.Clear();
        foreach (var row in Rows(_topSkills, CodenameCategory.Skill))
            Skills.Add(row);
    }

    /// <summary>Streamer mode moved (slice 7): the tables read their names again.</summary>
    public void RefreshNames()
    {
        FillBreakdowns();
        foreach (var root in Roots)
            root.RefreshShown();
    }

    /// <summary>Tests fill the two tables as an apply of the archive's top ten would.</summary>
    internal void ShowTopForTests(IEnumerable<(string Name, long Tokens)> projects, IEnumerable<(string Name, long Tokens)> skills)
    {
        _topProjects = [.. projects];
        _topSkills = [.. skills];
        FillBreakdowns();
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
        ByDayByAgent = data.ByDayByAgent ?? new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>();
        _projectsByAgent = data.ProjectsByAgent ?? new Dictionary<string, Dictionary<string, long>>();
        AgentsConsented = data.AgentsConsented;

        _topProjects = [.. data.Projects.OrderByDescending(kv => kv.Value).Take(10).Select(kv => (kv.Key, kv.Value))];   // streamer: masked - via FillBreakdowns: Rows masks each name (CP 15)
        _topSkills = [.. data.Skills.OrderByDescending(kv => kv.Value).Take(10).Select(kv => (kv.Key, kv.Value))];
        FillBreakdowns();

        _uncovered = data.Uncovered;
        _calendarByDay = data.CalendarByDay;
        _calendarWindowStart = data.CalendarStart;

        Changed?.Invoke();
    }
}
