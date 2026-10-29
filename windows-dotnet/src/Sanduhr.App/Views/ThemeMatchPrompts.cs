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

    /// <summary>A hand edit while following: Update (only for a theme of the person's), Save as a new theme, or Cancel.</summary>
    public static ThemeEditChoice Where(string theme, bool canUpdate)
    {
        if (canUpdate)
            return ThemedDialog.ShowChoice(Owner, SettingsNames.ThemeMatchSection, SettingsNames.WhereQuestion(theme, true),
                SettingsNames.UpdateTheme(theme), SettingsNames.SaveAsNewTheme) switch
            {
                MessageBoxResult.Yes => ThemeEditChoice.UpdateTheme,
                MessageBoxResult.No => ThemeEditChoice.NewTheme,
                _ => ThemeEditChoice.Cancel,
            };
        return ThemedDialog.Show(Owner, SettingsNames.ThemeMatchSection, SettingsNames.WhereQuestion(theme, false), MessageBoxButton.YesNo,
            ThemedDialogKind.Info, primaryLabel: SettingsNames.SaveAsNewTheme, secondaryLabel: SettingsNames.ThemeMatchCancel) == MessageBoxResult.Yes
            ? ThemeEditChoice.NewTheme
            : ThemeEditChoice.Cancel;
    }

    /// <summary>"Save as a new theme": the name, the suggestion filled in; null when cancelled.</summary>
    public static string? Name(string suggested)
    {
        var window = new TextPromptWindow(SettingsNames.SaveAsNewTheme, SettingsNames.NewThemeNamePrompt, suggested);
        if (Owner is { } owner) window.Owner = owner;
        return window.ShowDialog() == true ? window.Value : null;
    }
}