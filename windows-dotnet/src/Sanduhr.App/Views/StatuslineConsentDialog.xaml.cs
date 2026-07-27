using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>
/// Statusline install consent (WS-E). Unlike the vault's per-root checkboxes,
/// the statusline registers in exactly ONE Claude Code home — radio choice,
/// nothing pre-answered across tenants (the ".claude vs .claude-personal"
/// employer wall is why the picker exists). The details block names every file
/// the install touches and how to undo it — consent means knowing what's
/// written where.
/// </summary>
internal partial class StatuslineConsentDialog : Window
{
    private readonly List<RadioButton> _radios = new();
    private bool _install;

    private StatuslineConsentDialog(
        IReadOnlyList<string> homeNames, string scriptPath, Func<string, string> settingsPathFor)
    {
        InitializeComponent();
        foreach (var home in homeNames)
        {
            var rb = new RadioButton
            {
                Content = home,
                GroupName = "ccHome",
                IsChecked = _radios.Count == 0,   // first home pre-selected; the dialog itself is the gate
                Style = (Style)FindResource("ConsentRadio"),
                Tag = home,
            };
            rb.Checked += (_, _) => UpdateDetails(scriptPath, settingsPathFor);
            _radios.Add(rb);
            HomesPanel.Children.Add(rb);
        }
        UpdateDetails(scriptPath, settingsPathFor);
        Loaded += (_, _) => Sounds.PlayInfo();
    }

    private string? SelectedHome => _radios.FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void UpdateDetails(string scriptPath, Func<string, string> settingsPathFor)
    {
        var home = SelectedHome;
        if (home is null || DetailsText is null)
            return;
        DetailsText.Text =
            $"What gets written: the render script at {scriptPath}, a statusLine entry in " +
            $"{settingsPathFor(home)} (a timestamped backup is saved beside it first), and a usage " +
            "snapshot at %APPDATA%\\Sanduhr\\snapshot.json — percentages and reset times only, " +
            "never your account name or keys, stored only on this machine. Remove any time with " +
            "the Remove button in this tab; removal reverts all three.";
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        _install = true;
        DialogResult = true;
        Close();
    }

    private void OnNotNowClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Show the consent dialog; returns the chosen CC home name, or
    /// null when the user declines/closes.</summary>
    public static string? ShowConsent(
        Window? owner, IReadOnlyList<string> homeNames, string scriptPath, Func<string, string> settingsPathFor)
    {
        var dlg = new StatuslineConsentDialog(homeNames, scriptPath, settingsPathFor);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._install ? dlg.SelectedHome : null;
    }
}
