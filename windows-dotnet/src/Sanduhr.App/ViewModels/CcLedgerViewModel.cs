using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One ledger row. IsExpanded lives HERE (recycled containers must
/// not own state); the expansion detail text builds lazily on first expand.
/// IsGroup rows (stack-by-project mode) carry the member session list so the
/// expansion detail can list them individually — everything else (Info) is a
/// synthesized merge so sorting/badges/rescoping work unchanged.</summary>
public sealed partial class LedgerRowViewModel : ObservableObject
{
    public string Key { get; }
    public bool IsGroup { get; }
    public VaultSessionInfo Info { get; private set; }
    public IReadOnlyList<VaultSessionInfo> Members { get; private set; } = Array.Empty<VaultSessionInfo>();
    public long ScopedTokens { get; private set; }
    public DateTimeOffset LastTs => Info.LastTs;
    public string ProjectSort => ProjectText;

    /// <summary>Full-path tooltip — non-null ONLY under store_full_paths
    /// (spec: Ledger). A null ToolTip renders nothing in WPF. Group rows carry
    /// no single cwd (Info.Cwd is synthesized null), so this is naturally
    /// absent for them too.</summary>
    public string? CwdTooltip => Info.Cwd;

    [ObservableProperty] private string _projectText = "";
    [ObservableProperty] private string _lastActiveText = "";
    [ObservableProperty] private string _modelBadge = "";
    [ObservableProperty] private string _scopedTokensText = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _detailText = "";

    private DateOnly _scopeFrom;
    private DateOnly _scopeTo;

    public LedgerRowViewModel(string key, VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate,
        bool isGroup = false, IReadOnlyList<VaultSessionInfo>? members = null)
    {
        Key = key;
        IsGroup = isGroup;
        Info = info;
        Update(info, from, to, disambiguate, members);
    }

    public void Update(VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate,
        IReadOnlyList<VaultSessionInfo>? members = null)
    {
        Info = info;
        if (IsGroup)
            Members = members ?? Array.Empty<VaultSessionInfo>();
        ProjectText = disambiguate
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;
        LastActiveText = Relative(info.LastTs);
        ModelBadge = BuildBadge(info.ByModel);
        RescopeCore(from, to);
        // Single rebuild per Update() — Rescope() has its own rebuild for the
        // scope-chip-only path below; calling it from here would double-build
        // an expanded group row's detail.
        if (IsExpanded)
            DetailText = BuildDetail();
    }

    public void Rescope(DateOnly from, DateOnly to)
    {
        RescopeCore(from, to);
        // Session-row detail doesn't echo the current scope, but a group row's
        // member list does (per-session scoped tokens) — rebuild it here so an
        // expanded group stays honest across a scope-chip click.
        if (IsGroup && IsExpanded)
            DetailText = BuildDetail();
    }

    private void RescopeCore(DateOnly from, DateOnly to)
    {
        _scopeFrom = from;
        _scopeTo = to;
        ScopedTokens = VaultReader.TokensInScope(Info, from, to);
        ScopedTokensText = ScopedTokens > 0 ? TokenFormat.Compact(ScopedTokens) : "—";
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && DetailText.Length == 0)
            DetailText = BuildDetail();
    }

    private string BuildDetail() => IsGroup ? BuildGroupDetail() : BuildSessionDetail();

    private string BuildSessionDetail()
    {
        var sb = new StringBuilder();
        var span = Info.LastTs - Info.FirstTs;
        sb.Append("Span (wall-clock): ").Append(span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{Math.Max(1, (int)span.TotalMinutes)}m");
        sb.Append("  ·  Lifetime: ").Append(TokenFormat.Compact(Info.Total)).Append(" tokens");
        if (Info.AgentCount > 0)
        {
            sb.Append("\nAgents: ").Append(Info.AgentCount)
              .Append(" · ").Append(TokenFormat.Compact(Info.AgentTokens)).Append(" tokens");
        }
        foreach (var (day, bucket) in Info.ByDay.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append('\n').Append(day).Append(": ").Append(TokenFormat.Compact(bucket.Total));
            var models = string.Join(", ", bucket.ByModel.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{ShortModel(kv.Key)} {TokenFormat.Compact(kv.Value)}"));
            if (models.Length > 0)
                sb.Append("  (").Append(models).Append(')');
        }
        if (Info.BySkill is { Count: > 0 })
        {
            sb.Append("\nSkills: ").Append(string.Join(", ",
                Info.BySkill.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {TokenFormat.Compact(kv.Value)}")));
        }
        sb.Append("\nHome: ").Append(Info.Root).Append("  ·  Session: ").Append(Info.Uuid);
        return sb.ToString();
    }

    /// <summary>Group row detail: one line per member session (most recent
    /// first) — relative last-active, scoped tokens (current scope), lifetime
    /// tokens, short uuid — capped at 20 lines with a "… and N more" tail, plus
    /// a "Sessions: N · Homes: {roots}" footer in place of the session row's
    /// Home/Session line.</summary>
    private string BuildGroupDetail()
    {
        var ordered = Members.OrderByDescending(m => m.LastTs).ToList();
        var sb = new StringBuilder();
        int shown = Math.Min(20, ordered.Count);
        for (int i = 0; i < shown; i++)
        {
            var m = ordered[i];
            if (i > 0) sb.Append('\n');
            long scoped = VaultReader.TokensInScope(m, _scopeFrom, _scopeTo);
            string shortUuid = m.Uuid.Length > 8 ? m.Uuid[..8] : m.Uuid;
            sb.Append(Relative(m.LastTs))
              .Append("  ·  ").Append(scoped > 0 ? TokenFormat.Compact(scoped) : "—")
              .Append("  ·  ").Append(TokenFormat.Compact(m.Total)).Append(" lifetime")
              .Append("  ·  ").Append(shortUuid);
        }
        if (ordered.Count > shown)
            sb.Append('\n').Append("… and ").Append(ordered.Count - shown).Append(" more");
        var roots = ordered.Select(m => m.Root).Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal);
        if (sb.Length > 0) sb.Append('\n');
        sb.Append("Sessions: ").Append(ordered.Count);
        if (Info.AgentCount > 0)
        {
            sb.Append("  ·  Agents: ").Append(Info.AgentCount)
              .Append("  ·  ").Append(TokenFormat.Compact(Info.AgentTokens)).Append(" tokens");
        }
        sb.Append("  ·  Homes: ").Append(string.Join(", ", roots));
        return sb.ToString();
    }

    /// <summary>Read-time tier projection with an honest raw fallback — an
    /// unmapped model (claude-fable-5 is 49% of live traffic) shows its
    /// trimmed raw name, never disappears.</summary>
    private static string BuildBadge(Dictionary<string, long> byModel)
    {
        long total = byModel.Values.Sum();
        if (total <= 0)
            return "";
        var parts = byModel.OrderByDescending(kv => kv.Value).Take(2)
            .Select(kv => $"{ShortModel(kv.Key)} {kv.Value * 100 / total}%");
        return string.Join(" · ", parts);
    }

    private static string ShortModel(string model)
    {
        var tier = CcLogReader.TierForModel(model);
        if (tier == "seven_day_opus") return "opus";
        if (tier == "seven_day_sonnet") return "sonnet";
        if (tier == "seven_day") return "haiku";
        var name = model.StartsWith("claude-", StringComparison.Ordinal) ? model[7..] : model;
        int dateIdx = name.Length - 9;   // trim trailing -yyyymmdd build stamps
        if (dateIdx > 0 && name[dateIdx] == '-' && name[(dateIdx + 1)..].All(char.IsDigit))
            name = name[..dateIdx];
        return name;
    }

    private static string Relative(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.UtcNow - ts.ToUniversalTime();
        if (delta < TimeSpan.FromMinutes(2)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromHours(24)) return $"{(int)delta.TotalHours}h ago";
        if (delta < TimeSpan.FromDays(14)) return $"{(int)delta.TotalDays}d ago";
        return ts.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Sessions (Ledger) section. Scope chips (Today / Yesterday / 7d / All,
/// default 7d) make the token column answer "what ate 800k yesterday" — a
/// lifetime sort ranks week-old monsters, not yesterday's culprit. Refresh
/// diffs rows by key so scroll position and expansion survive the 5-minute
/// ingest cadence.
/// </summary>
public sealed partial class CcLedgerViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action<bool> _persistGroupByProject;
    private Window? _owner;
    private bool _loading = true;

    /// <summary>Last sessions read from the vault — toggling GroupByProject
    /// re-presents from this cache instead of re-reading (mode switches are a
    /// user action, not the 5-minute ingest cadence).</summary>
    private IReadOnlyList<VaultSessionInfo> _lastSessions = Array.Empty<VaultSessionInfo>();

    [ObservableProperty] private string _scope = "7d";
    [ObservableProperty] private string _sortColumn = "Tokens";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private string _emptyText = "";
    [ObservableProperty] private bool _groupByProject;

    public ObservableCollection<LedgerRowViewModel> Rows { get; } = new();

    public ListCollectionView View { get; }

    public ThemePalette Palette => _widget.Palette;

    public string LastActiveHeader => Header("Last active", "LastActive");
    public string ProjectHeader => Header("Project", "Project");
    public string TokensHeader => Header(Scope == "All" ? "Tokens" : $"Tokens ({Scope})", "Tokens");

    public CcLedgerViewModel(WidgetViewModel widget, bool groupByProject, Action<bool> persistGroupByProject)
    {
        _widget = widget;
        _persistGroupByProject = persistGroupByProject;
        View = new ListCollectionView(Rows) { CustomSort = new LedgerSort(this) };
        GroupByProject = groupByProject;
        _loading = false;
    }

    partial void OnGroupByProjectChanged(bool value)
    {
        if (_loading)
            return;
        try
        {
            _persistGroupByProject(value);
            Present();
        }
        catch
        {
            // A mode-toggle fault must never become an unhandled dispatcher
            // exception (global constraint: every UI path caught).
        }
    }

    public void AttachOwner(Window owner) => _owner = owner;

    private string Header(string label, string column)
        => SortColumn == column ? $"{label} {(SortDescending ? "▼" : "▲")}" : label;

    private void NotifyHeaders()
    {
        OnPropertyChanged(nameof(LastActiveHeader));
        OnPropertyChanged(nameof(ProjectHeader));
        OnPropertyChanged(nameof(TokensHeader));
    }

    private (DateOnly From, DateOnly To) ScopeRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return Scope switch
        {
            "Today" => (today, today),
            "Yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "7d" => (today.AddDays(-6), today),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
    }

    [RelayCommand]
    private void SetScope(string scope)
    {
        if (scope is not ("Today" or "Yesterday" or "7d" or "All"))
            return;
        Scope = scope;
        var (from, to) = ScopeRange();
        foreach (var row in Rows)
            row.Rescope(from, to);
        View.Refresh();
        NotifyHeaders();
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column != "Project";   // text asc, numbers/dates desc
        }
        View.Refresh();
        NotifyHeaders();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"sanduhr-sessions-{DateTime.Now:yyyyMMdd}.csv",
        };
        bool? ok = _owner is not null ? dialog.ShowDialog(_owner) : dialog.ShowDialog();
        if (ok != true)
            return;
        var rows = BuildCsvRows();
        var built = VaultLedgerCsv.Build(rows);
        try
        {
            File.WriteAllText(dialog.FileName, built.Text);
            ThemedDialog.Show(_owner, "Export complete", $"Wrote {built.RowCount} session rows.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(_owner, "Export failed", "Could not write the file. Is it open elsewhere?",
                kind: ThemedDialogKind.Warning);
        }
    }

    /// <summary>CSV always exports session-level rows (product decision) —
    /// even in stack-by-project mode, where the visible rows are project
    /// aggregates and a project key has no business sitting under the
    /// "session" header. Built straight from <see cref="_lastSessions"/>,
    /// scoped to the active range, and sorted to mirror the active column as
    /// closely as per-session values allow: same SortColumn/SortDescending
    /// semantics as <see cref="LedgerSort"/> (Project text disambiguated the
    /// same way row display is), ties by LastTs. In flat mode this reproduces
    /// today's row set and order exactly, since flat rows are already 1:1
    /// with sessions.</summary>
    private List<VaultLedgerCsv.Row> BuildCsvRows()
    {
        var (from, to) = ScopeRange();

        var nameGroups = _lastSessions.GroupBy(s => s.ProjectName, StringComparer.Ordinal)
            .Where(g => g.Select(s => s.ProjectKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        string ProjectTextFor(VaultSessionInfo info) => nameGroups.Contains(info.ProjectName)
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;

        var scoped = _lastSessions
            .Select(s => (Info: s, ScopedTokens: VaultReader.TokensInScope(s, from, to), ProjectText: ProjectTextFor(s)))
            .ToList();

        scoped.Sort((a, b) =>
        {
            int cmp = SortColumn switch
            {
                "LastActive" => a.Info.LastTs.CompareTo(b.Info.LastTs),
                "Project" => string.Compare(a.ProjectText, b.ProjectText, StringComparison.OrdinalIgnoreCase),
                _ => a.ScopedTokens.CompareTo(b.ScopedTokens),
            };
            if (cmp == 0)
                cmp = a.Info.LastTs.CompareTo(b.Info.LastTs);
            return SortDescending ? -cmp : cmp;
        });

        return scoped.Select(x => new VaultLedgerCsv.Row(
                x.Info.Uuid, x.Info.Root, x.ProjectText,
                x.Info.FirstTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                x.Info.LastTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                x.ScopedTokens, x.Info.Total,
                string.Join(";", x.Info.ByModel.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}:{kv.Value}"))))
            .ToList();
    }

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        IReadOnlyList<VaultSessionInfo> sessions;
        try
        {
            sessions = await Task.Run(() =>
                vault is null
                    ? (IReadOnlyList<VaultSessionInfo>)Array.Empty<VaultSessionInfo>()
                    : vault.Reader.ReadSessions(vault.ConsentedRootNames())).ConfigureAwait(true);
        }
        catch
        {
            sessions = Array.Empty<VaultSessionInfo>();
        }

        _lastSessions = sessions;
        Present();
    }

    /// <summary>One row's data source — either a plain session (flat mode) or
    /// a synthesized per-project merge (stack-by-project mode) plus its member
    /// sessions. Row keys are disjoint across modes ("root|uuid" vs
    /// "grp|projectKey"), so the diff-by-key loop below rebuilds Rows in full
    /// on a mode toggle for free — no separate Clear()+re-add path needed.</summary>
    private readonly record struct RowSource(
        string Key, VaultSessionInfo Info, bool IsGroup, IReadOnlyList<VaultSessionInfo>? Members);

    private List<RowSource> BuildRowSources()
    {
        if (!GroupByProject)
            return _lastSessions
                .Select(s => new RowSource(s.Root + "|" + s.Uuid, s, false, null))
                .ToList();

        var result = new List<RowSource>();
        foreach (var group in _lastSessions.GroupBy(s => s.ProjectKey, StringComparer.Ordinal))
        {
            var members = group.ToList();
            result.Add(new RowSource("grp|" + group.Key, MergeGroup(group.Key, members), true, members));
        }
        return result;
    }

    /// <summary>Merge one project's member sessions into a synthesized
    /// VaultSessionInfo — Root is the shared root name or "both", ByDay is
    /// summed per day (keeps TokensInScope/Rescope working unchanged), Cache
    /// is null (no single cache-token reading applies to a project stack).</summary>
    private static VaultSessionInfo MergeGroup(string projectKey, IReadOnlyList<VaultSessionInfo> members)
    {
        var first = members[0];
        string root = members.Select(m => m.Root).Distinct(StringComparer.Ordinal).Count() == 1
            ? first.Root
            : "both";

        var byModel = new Dictionary<string, long>(StringComparer.Ordinal);
        Dictionary<string, long>? bySkill = null;
        var byDay = new Dictionary<string, VaultDayBucket>(StringComparer.Ordinal);
        long total = 0;

        foreach (var m in members)
        {
            total += m.Total;
            foreach (var (model, v) in m.ByModel)
                byModel[model] = byModel.GetValueOrDefault(model) + v;
            if (m.BySkill is { Count: > 0 })
            {
                bySkill ??= new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var (skill, v) in m.BySkill)
                    bySkill[skill] = bySkill.GetValueOrDefault(skill) + v;
            }
            foreach (var (day, bucket) in m.ByDay)
            {
                if (!byDay.TryGetValue(day, out var merged))
                    byDay[day] = merged = new VaultDayBucket();
                merged.Total += bucket.Total;
                foreach (var (model, v) in bucket.ByModel)
                    merged.ByModel[model] = merged.ByModel.GetValueOrDefault(model) + v;
                if (bucket.BySkill is { Count: > 0 })
                {
                    merged.BySkill ??= new Dictionary<string, long>(StringComparer.Ordinal);
                    foreach (var (skill, v) in bucket.BySkill)
                        merged.BySkill[skill] = merged.BySkill.GetValueOrDefault(skill) + v;
                }
            }
        }

        return new VaultSessionInfo(
            projectKey, root, projectKey, first.ProjectName, null,
            members.Min(m => m.FirstTs), members.Max(m => m.LastTs),
            total, byModel, bySkill, byDay, null,
            members.Sum(m => m.AgentCount), members.Sum(m => m.AgentTokens));
    }

    /// <summary>Re-presents Rows from <see cref="_lastSessions"/> under the
    /// current mode/scope — no vault read. Called after every RefreshAsync AND
    /// on a GroupByProject toggle. Diffs by row key (never Clear()+re-add), so
    /// the 5-minute refresh preserves scroll + expansion exactly as before.</summary>
    private void Present()
    {
        var (from, to) = ScopeRange();
        var sources = BuildRowSources();

        // Disambiguate only when two DIFFERENT project keys share a name.
        var nameGroups = sources.GroupBy(s => s.Info.ProjectName, StringComparer.Ordinal)
            .Where(g => g.Select(s => s.Info.ProjectKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Diff by key — NEVER Clear()+re-add (scroll + expansion survive).
        var incoming = sources.ToDictionary(s => s.Key, StringComparer.Ordinal);
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!incoming.ContainsKey(Rows[i].Key))
                Rows.RemoveAt(i);
        }
        var existing = Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        foreach (var src in sources)
        {
            bool disambiguate = nameGroups.Contains(src.Info.ProjectName);
            if (existing.TryGetValue(src.Key, out var row))
                row.Update(src.Info, from, to, disambiguate, src.Members);
            else
                Rows.Add(new LedgerRowViewModel(src.Key, src.Info, from, to, disambiguate, src.IsGroup, src.Members));
        }
        View.Refresh();

        var vault = _widget.Vault;
        EmptyText = Rows.Count > 0 ? ""
            : (vault is null || vault.ConsentedRootNames().Count == 0)
                ? "History vault is off — enable a Claude Code home in Overview."
                : "No sessions recorded yet — the first backfill lands within a minute.";
    }

    /// <summary>Typed comparer for ListCollectionView.CustomSort — the spec
    /// forbids reflection-based SortDescriptions on a recycling list.</summary>
    private sealed class LedgerSort : IComparer
    {
        private readonly CcLedgerViewModel _vm;

        public LedgerSort(CcLedgerViewModel vm) => _vm = vm;

        public int Compare(object? x, object? y)
        {
            if (x is not LedgerRowViewModel a || y is not LedgerRowViewModel b)
                return 0;
            int cmp = _vm.SortColumn switch
            {
                "LastActive" => a.LastTs.CompareTo(b.LastTs),
                "Project" => string.Compare(a.ProjectSort, b.ProjectSort, StringComparison.OrdinalIgnoreCase),
                _ => a.ScopedTokens.CompareTo(b.ScopedTokens),
            };
            if (cmp == 0)
                cmp = a.LastTs.CompareTo(b.LastTs);
            return _vm.SortDescending ? -cmp : cmp;
        }
    }
}
