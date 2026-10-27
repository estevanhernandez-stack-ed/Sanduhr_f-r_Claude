using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Parent of Settings, Claude Usage: owns the Meters / Overview / Trends / Sessions views and fans refreshes to the one shown.
/// Meters is the 30-day meter history (Settings v2, slice 5: it was the History page) and the first view; the other three read the
/// session archive and the live session logs.
/// </summary>
public sealed partial class ClaudeCodeTabViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action _ingestHandler;

    /// <summary>The meter history (WF14). The same object as <see cref="SettingsViewModel.History"/>.</summary>
    public HistoryTabViewModel Meters { get; }

    public LocalCcViewModel Overview { get; }

    public CcTrendsViewModel Trends { get; }

    public CcLedgerViewModel Ledger { get; }

    /// <summary>Set by Settings (slice 7): "Show the real name on stream" from a table row (decision 16).</summary>
    public Action<CodenameCategory, string>? ShowRealName { get; set; }

    [RelayCommand]
    private void ShowRealNameOf(object? row)
    {
        switch (row)
        {
            case BreakdownRow b when b.CanShowRealName: ShowRealName?.Invoke(b.Category, b.Real); break;
            case LedgerRowViewModel l: ShowRealName?.Invoke(CodenameCategory.Project, l.ProjectKeyForStreamer); break;
        }
    }

    /// <summary>Set by the window on tab selection — ingest-completed refreshes
    /// only run while the user can see the tab.</summary>
    public bool IsTabActive { get; set; }

    /// <summary>The view shown: Meters first (owner taste 1), then wherever the person or a link left it this window.</summary>
    [ObservableProperty] private string _section = SettingsIndex.MetersView;

    public ClaudeCodeTabViewModel(WidgetViewModel widget, HistoryTabViewModel meters, LocalCcViewModel overview, CcTrendsViewModel trends,
        CcLedgerViewModel ledger)
    {
        _widget = widget;
        Meters = meters;
        Overview = overview;
        Trends = trends;
        Ledger = ledger;
        _ingestHandler = () => Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (IsTabActive)
                    await RefreshActiveAsync();
            }
            catch
            {
                // A refresh fault must never become an unhandled dispatcher
                // exception (global constraint: every UI path caught).
            }
        });
    }

    /// <summary>Subscribe to ingest completions (worker thread → dispatcher).
    /// The window calls Detach on close — the VaultService outlives every
    /// Settings window, so a missed unsubscribe is a VM leak.</summary>
    public void Attach()
    {
        if (_widget.Vault is { } vault)
            vault.IngestCompleted += _ingestHandler;
    }

    public void Detach()
    {
        if (_widget.Vault is { } vault)
            vault.IngestCompleted -= _ingestHandler;
        Overview.Detach();
        Trends.Detach();
    }

    public async Task RefreshActiveAsync()
    {
        switch (Section)
        {
            case SettingsIndex.MetersView:
                Meters.Refresh();   // the chart is drawn by the window; a view switch must redraw it at its new size
                break;
            case "Overview":
                await Overview.RefreshAsync();
                break;
            case "Trends":
                await Trends.RefreshAsync();
                break;
            case "Sessions":
                await Ledger.RefreshAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task SetMeters()
    {
        Section = SettingsIndex.MetersView;
        await RefreshActiveAsync();
    }

    [RelayCommand]
    private async Task SetOverview()
    {
        Section = "Overview";
        await RefreshActiveAsync();
    }

    [RelayCommand]
    private async Task SetTrends()
    {
        Section = "Trends";
        await RefreshActiveAsync();
    }

    [RelayCommand]
    private async Task SetSessions()
    {
        Section = "Sessions";
        await RefreshActiveAsync();
    }

    /// <summary>Calendar day-click bridge: scope the Ledger to the clicked day
    /// and switch to Sessions. Ledger.RefreshAsync() re-presents via
    /// ScopeRange(), which honors the day scope, so the day survives the
    /// vault re-read.</summary>
    public async Task OpenDayInSessionsAsync(DateOnly day)
    {
        try
        {
            Ledger.SetDayScope(day);
            Section = "Sessions";
            await Ledger.RefreshAsync();
        }
        catch
        {
            // A day-click navigation fault must never become an unhandled
            // dispatcher exception (global constraint: every UI path caught).
        }
    }
}
