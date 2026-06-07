using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the Local CC breakdown tables — a display name and its
/// compact token total (e.g. "Sanduhr" / "1.2M").</summary>
public sealed record BreakdownRow(string Name, string Tokens);

/// <summary>
/// Backs the Settings → Local CC tab — the read-only local Claude Code token-burn
/// summary ported from <c>local_cc_dialog.LocalCCTab</c>: today's total, the last
/// 30 days total, the 30-day bar strip, and the per-project / per-skill breakdown
/// tables. Reuses the shared <see cref="CcLogReader.AggregateForLocalCcTab"/> (one
/// disk walk, 30-second cache) the card badges already hit, off the UI thread so
/// the first heavy read stays responsive.
/// </summary>
public sealed partial class LocalCcViewModel : ObservableObject
{
    private const int LookbackDays = 30;

    private readonly WidgetViewModel _widget;
    private readonly Action<bool> _persistShowBreakdowns;

    /// <summary>Raised when the bar strip needs re-rendering (new data / theme switch).</summary>
    public event Action? Changed;

    [ObservableProperty] private string _todayText = "Loading…";
    [ObservableProperty] private string _monthText = "Loading…";
    [ObservableProperty] private bool _showBreakdowns;

    public ObservableCollection<BreakdownRow> Projects { get; } = new();
    public ObservableCollection<BreakdownRow> Skills { get; } = new();

    private Dictionary<DateOnly, long> _byDay = new();
    public IReadOnlyDictionary<DateOnly, long> ByDay => _byDay;

    public ThemePalette Palette => _widget.Palette;

    public LocalCcViewModel(WidgetViewModel widget, bool showBreakdowns, Action<bool> persistShowBreakdowns)
    {
        _widget = widget;
        _showBreakdowns = showBreakdowns;
        _persistShowBreakdowns = persistShowBreakdowns;
        _widget.ThemeChanged += _ => Changed?.Invoke();
    }

    partial void OnShowBreakdownsChanged(bool value) => _persistShowBreakdowns(value);

    /// <summary>Run the aggregation off the UI thread (the first read on a heavy CC
    /// user can walk hundreds of session logs) and apply the results. Subsequent
    /// calls within the reader's 30s cache TTL return near-instantly.</summary>
    public async Task RefreshAsync()
    {
        LocalCcAggregate agg;
        try
        {
            agg = await Task.Run(() => _widget.CcReader.AggregateForLocalCcTab(LookbackDays))
                .ConfigureAwait(true);
        }
        catch
        {
            agg = new LocalCcAggregate(new Dictionary<DateOnly, long>(),
                new Dictionary<string, long>(), new Dictionary<string, long>());
        }
        ApplyAggregate(agg);
    }

    private void ApplyAggregate(LocalCcAggregate agg)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        long todayTotal = agg.ByDay.GetValueOrDefault(today);
        TodayText = todayTotal > 0 ? $"{TokenFormat.Compact(todayTotal)} tokens" : "No activity yet";

        long monthTotal = agg.ByDay.Values.Sum();
        MonthText = monthTotal > 0 ? $"{TokenFormat.Compact(monthTotal)} tokens" : "No activity";

        _byDay = new Dictionary<DateOnly, long>(agg.ByDay);

        // Combine projects sharing a display basename (symlink vs direct cwd would
        // otherwise show as duplicate rows). Top 10, descending.
        var projByName = new Dictionary<string, long>();
        foreach (var (cwd, tokens) in agg.ByProject)
        {
            var name = CcLogReader.ProjectDisplayName(cwd);
            projByName[name] = projByName.GetValueOrDefault(name) + tokens;
        }
        Projects.Clear();
        foreach (var (name, tokens) in projByName.OrderByDescending(kv => kv.Value).Take(10))
            Projects.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Skills.Clear();
        foreach (var (name, tokens) in agg.BySkill.OrderByDescending(kv => kv.Value).Take(10))
            Skills.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Changed?.Invoke();
    }
}
