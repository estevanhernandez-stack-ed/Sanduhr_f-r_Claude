using System.Reflection;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the tabbed <c>SettingsWindow</c> (port of <c>settings_dialog.py</c>'s
/// shell): the <b>Accounts</b> tab (multi-account), <b>Themes</b> (item 8),
/// <b>History</b> (item 9) + <b>Claude Code</b> (WS-C), and the <b>General</b>
/// tab — which surfaces version + the active account and now hosts the
/// item-10 auto-start control via <see cref="GeneralViewModel"/>.
/// </summary>
public sealed class SettingsViewModel
{
    public AccountsViewModel Accounts { get; }

    /// <summary>Backs the Themes tab (item 8): paste/save/apply, copy agent prompt,
    /// open folder, installed-themes management.</summary>
    public ThemesViewModel Themes { get; }

    /// <summary>Backs the History tab (item 9): 30-day chart with per-account /
    /// all-accounts overlay, Week/Month window, CSV export, Clear history.</summary>
    public HistoryTabViewModel History { get; }

    /// <summary>Backs the Claude Code tab (WS-C): Overview / Trends / Sessions
    /// over the usage vault + live reader.</summary>
    public ClaudeCodeTabViewModel ClaudeCode { get; }

    /// <summary>Backs the General tab's auto-start control (item 10).</summary>
    public GeneralViewModel General { get; }

    /// <summary>Backs the Alerts tab (WS-B): thresholds, projection/reset
    /// toggles, sound + snake sting, and the test-alert button.</summary>
    public AlertsViewModel Alerts { get; }

    /// <summary>Assembly version for the General tab footer (e.g. "3.0.0").</summary>
    public string Version { get; }

    public SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)
    {
        Accounts = new AccountsViewModel(widget, addAccountAsync, updateSignInAsync);
        Themes = new ThemesViewModel(widget);
        History = new HistoryTabViewModel(widget);
        ClaudeCode = new ClaudeCodeTabViewModel(widget, new LocalCcViewModel(
            widget, widget.LoadLocalCcShowBreakdowns(), widget.SaveLocalCcShowBreakdowns),
            new CcTrendsViewModel(widget), new CcLedgerViewModel(widget));
        General = new GeneralViewModel();
        Alerts = new AlertsViewModel(widget, () =>
        {
            widget.AlertService?.DeliverTest();
            return Task.CompletedTask;
        });
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is null ? "3.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
