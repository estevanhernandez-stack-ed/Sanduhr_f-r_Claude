using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>The questions theme match asks, as themed dialogs over Settings. Tests replace them on <see cref="ThemeMatchService"/>.</summary>
internal static class ThemeMatchPrompts
{
    private static Window? Owner => Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsLoaded);

    /// <summary>Follow on while a theme of the person's is active: its name in, where the look goes out.</summary>
    public static ThemeAddChoice AddOrNew(string theme) =>
        ThemedDialog.ShowChoice(Owner, SettingsNames.FollowThemeSwitch, SettingsNames.AddOrNewQuestion(theme),
            SettingsNames.AddDeskLookTo(theme), SettingsNames.MakeANewTheme) switch
        {
            MessageBoxResult.Yes => ThemeAddChoice.AddToTheme,
            MessageBoxResult.No => ThemeAddChoice.NewTheme,
            _ => ThemeAddChoice.Cancel,
        };
}