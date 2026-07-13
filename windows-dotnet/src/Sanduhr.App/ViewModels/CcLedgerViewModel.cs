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
/// not own state); the expansion detail text builds lazily on first expand.</summary>
public sealed partial class LedgerRowViewModel : ObservableObject
{
    public string Key { get; }
    public VaultSessionInfo Info { get; private set; }
    public long ScopedTokens { get; private set; }
    public DateTimeOffset LastTs => Info.LastTs;
    public string ProjectSort => ProjectText;

    /// <summary>Full-path tooltip — non-null ONLY under store_full_paths
    /// (spec: Ledger). A null ToolTip renders nothing in WPF.</summary>
    public string? CwdTooltip => Info.Cwd;

    [ObservableProperty] private string _projectText = "";
    [ObservableProperty] private string _lastActiveText = "";
    [ObservableProperty] private string _modelBadge = "";
    [ObservableProperty] private string _scopedTokensText = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _detailText = "";

    public LedgerRowViewModel(VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate)
    {
        Key = info.Root + "|" + info.Uuid;
        Info = info;
        Update(info, from, to, disambiguate);
    }

    public void Update(VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate)
    {
        Info = info;
        ProjectText = disambiguate
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;
        LastActiveText = Relative(info.LastTs);
        ModelBadge = BuildBadge(info.ByModel);
        Rescope(from, to);
        if (IsExpanded)
            DetailText = BuildDetail();
    }

    public void Rescope(DateOnly from, DateOnly to)
    {
        ScopedTokens = VaultReader.TokensInScope(Info, from, to);
        ScopedTokensText = ScopedTokens > 0 ? TokenFormat.Compact(ScopedTokens) : "—";
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && DetailText.Length == 0)
            DetailText = BuildDetail();
    }

    private string BuildDetail()
    {
        var sb = new StringBuilder();
        var span = Info.LastTs - Info.FirstTs;
        sb.Append("Span (wall-clock): ").Append(span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{Math.Max(1, (int)span.TotalMinutes)}m");
        sb.Append("  ·  Lifetime: ").Append(TokenFormat.Compact(Info.Total)).Append(" tokens");
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
    private Window? _owner;

    [ObservableProperty] private string _scope = "7d";
    [ObservableProperty] private string _sortColumn = "Tokens";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private string _emptyText = "";

    public ObservableCollection<LedgerRowViewModel> Rows { get; } = new();

    public ListCollectionView View { get; }

    public ThemePalette Palette => _widget.Palette;

    public string LastActiveHeader => Header("Last active", "LastActive");
    public string ProjectHeader => Header("Project", "Project");
    public string TokensHeader => Header(Scope == "All" ? "Tokens" : $"Tokens ({Scope})", "Tokens");

    public CcLedgerViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        View = new ListCollectionView(Rows) { CustomSort = new LedgerSort(this) };
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
        var rows = View.Cast<LedgerRowViewModel>()
            .Select(r => new VaultLedgerCsv.Row(
                r.Info.Uuid, r.Info.Root, r.ProjectText,
                r.Info.FirstTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                r.Info.LastTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                r.ScopedTokens, r.Info.Total,
                string.Join(";", r.Info.ByModel.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}:{kv.Value}"))))
            .ToList();
        var built = VaultLedgerCsv.Build(rows);
        try
        {
            File.WriteAllText(dialog.FileName, built.Text);
            ThemedDialog.Show(_owner, "Export complete", $"Wrote {built.RowCount} rows.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(_owner, "Export failed", "Could not write the file. Is it open elsewhere?",
                kind: ThemedDialogKind.Warning);
        }
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

        var (from, to) = ScopeRange();
        // Disambiguate only when two DIFFERENT project keys share a name.
        var nameGroups = sessions.GroupBy(s => s.ProjectName, StringComparer.Ordinal)
            .Where(g => g.Select(s => s.ProjectKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Diff by key — NEVER Clear()+re-add (scroll + expansion survive).
        var incoming = sessions.ToDictionary(s => s.Root + "|" + s.Uuid, StringComparer.Ordinal);
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!incoming.ContainsKey(Rows[i].Key))
                Rows.RemoveAt(i);
        }
        var existing = Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        foreach (var (key, info) in incoming)
        {
            bool disambiguate = nameGroups.Contains(info.ProjectName);
            if (existing.TryGetValue(key, out var row))
                row.Update(info, from, to, disambiguate);
            else
                Rows.Add(new LedgerRowViewModel(info, from, to, disambiguate));
        }
        View.Refresh();

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
