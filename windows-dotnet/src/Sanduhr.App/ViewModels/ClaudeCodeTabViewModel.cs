using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Parent of the Settings → Claude Code tab: owns the Overview / Trends /
/// Sessions sub-nav state and fans refreshes to the active section. Sections
/// are added as they land (Task 6: Overview; Task 7: Trends; Task 8: Sessions).
/// </summary>
public sealed partial class ClaudeCodeTabViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action _ingestHandler;

    public LocalCcViewModel Overview { get; }

    /// <summary>Set by the window on tab selection — ingest-completed refreshes
    /// only run while the user can see the tab.</summary>
    public bool IsTabActive { get; set; }

    [ObservableProperty] private string _section = "Overview";

    public ClaudeCodeTabViewModel(WidgetViewModel widget, LocalCcViewModel overview)
    {
        _widget = widget;
        Overview = overview;
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
    }

    public async Task RefreshActiveAsync()
    {
        switch (Section)
        {
            case "Overview":
                await Overview.RefreshAsync();
                break;
            // Task 7 adds: case "Trends": await Trends.RefreshAsync(); break;
            // Task 8 adds: case "Sessions": await Ledger.RefreshAsync(); break;
        }
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
}
