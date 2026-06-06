using System.Reflection;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the tabbed <c>SettingsWindow</c> (port of <c>settings_dialog.py</c>'s
/// shell). This milestone ships the <b>Accounts</b> tab — the real feature — plus a
/// thin <b>General</b> tab that surfaces version + the active account and leaves
/// labelled room for the settings that arrive with later items (Themes = item 8,
/// History/CSV = item 9, Focus/Game/Auto-start = item 10). Those tabs are NOT built
/// here on purpose; the General tab just notes where they will land so the window
/// shape is stable when they do.
/// </summary>
public sealed class SettingsViewModel
{
    public AccountsViewModel Accounts { get; }

    /// <summary>Assembly version for the General tab footer (e.g. "3.0.0").</summary>
    public string Version { get; }

    public SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync)
    {
        Accounts = new AccountsViewModel(widget, addAccountAsync);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is null ? "3.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
