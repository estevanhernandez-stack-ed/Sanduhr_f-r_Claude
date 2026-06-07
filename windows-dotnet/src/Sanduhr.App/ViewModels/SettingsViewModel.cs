using System.Reflection;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the tabbed <c>SettingsWindow</c> (port of <c>settings_dialog.py</c>'s
/// shell): the <b>Accounts</b> tab (multi-account), <b>Themes</b> (item 8),
/// <b>History</b> + <b>Local CC</b> (item 9), and the <b>General</b> tab — which
/// surfaces version + the active account and now hosts the item-10 auto-start
/// control via <see cref="GeneralViewModel"/>.
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

    /// <summary>Backs the Local CC tab (item 9): local Claude Code token-burn
    /// summary — today / 30-day totals, daily bar strip, project + skill breakdowns.</summary>
    public LocalCcViewModel LocalCc { get; }

    /// <summary>Backs the General tab's auto-start control (item 10).</summary>
    public GeneralViewModel General { get; }

    /// <summary>Assembly version for the General tab footer (e.g. "3.0.0").</summary>
    public string Version { get; }

    public SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync)
    {
        Accounts = new AccountsViewModel(widget, addAccountAsync);
        Themes = new ThemesViewModel(widget);
        History = new HistoryTabViewModel(widget);
        LocalCc = new LocalCcViewModel(
            widget, widget.LoadLocalCcShowBreakdowns(), widget.SaveLocalCcShowBreakdowns);
        General = new GeneralViewModel();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is null ? "3.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
