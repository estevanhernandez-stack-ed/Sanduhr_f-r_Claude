using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>What the MCP consent dialog resolved to: the CC home receiving the
/// registration, and the per-home burn-attribution consent map (unchecked by
/// default — tool results become conversation content, a stricter exposure
/// than the local-only vault).</summary>
internal sealed record McpConsentResult(string CcHome, IReadOnlyDictionary<string, bool> Roots);

/// <summary>
/// MCP install consent (WS-E registration slice). Two decisions, neither
/// pre-answered across tenants: WHICH home gets the registration (radio, first
/// pre-selected — the dialog itself is the gate), and which homes the burn
/// tool may read (checkboxes, all OFF by default). The details block names
/// every file the install touches and how to undo it.
/// </summary>
internal partial class McpConsentDialog : Window
{
    private readonly List<RadioButton> _radios = new();
    private readonly Dictionary<string, CheckBox> _rootChecks = new(StringComparer.Ordinal);
    private bool _install;

    private McpConsentDialog(
        IReadOnlyList<string> homeNames, string? defaultHome,
        string launcherPath, Func<string, string> configPathFor)
    {
        InitializeComponent();
        foreach (var home in homeNames)
        {
            var rb = new RadioButton
            {
                Content = home,
                GroupName = "mcpHome",
                IsChecked = defaultHome is not null ? home == defaultHome : _radios.Count == 0,
                Style = (Style)FindResource("ConsentRadio"),
                Tag = home,
            };
            rb.Checked += (_, _) => UpdateDetails(launcherPath, configPathFor);
            _radios.Add(rb);
            HomesPanel.Children.Add(rb);

            var cb = new CheckBox
            {
                Content = home,
                IsChecked = false,   // burn-attribution consent is NEVER pre-answered
                Style = (Style)FindResource("ConsentCheckBox"),
            };
            _rootChecks[home] = cb;
            RootsPanel.Children.Add(cb);
        }
        UpdateDetails(launcherPath, configPathFor);
        Loaded += (_, _) => Sounds.PlayInfo();
    }

    private string? SelectedHome => _radios.FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void UpdateDetails(string launcherPath, Func<string, string> configPathFor)
    {
        var home = SelectedHome;
        if (home is null || DetailsText is null)
            return;
        DetailsText.Text =
            $"What gets written: the server files under %APPDATA%\\Sanduhr\\mcp\\, a launcher at {launcherPath}, " +
            $"and a 'sanduhr' entry in {configPathFor(home)} (a timestamped backup is saved beside it first). " +
            "The helper is read-only: it can see your usage percentages and - only for homes checked above - " +
            "project-level token totals. It cannot see your keys, your accounts, or conversation content. " +
            "Remove any time with the Remove button in this tab; removal reverts everything.";
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

    /// <summary>Show the dialog; null when declined/closed. <paramref name="defaultHome"/>
    /// pre-selects the radio (the statusline's chosen home, when one exists).</summary>
    public static McpConsentResult? ShowConsent(
        Window? owner, IReadOnlyList<string> homeNames, string? defaultHome,
        string launcherPath, Func<string, string> configPathFor)
    {
        var dlg = new McpConsentDialog(homeNames, defaultHome, launcherPath, configPathFor);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();

        if (!dlg._install || dlg.SelectedHome is not { } chosen)
            return null;
        var roots = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (name, cb) in dlg._rootChecks)
            roots[name] = cb.IsChecked == true;
        return new McpConsentResult(chosen, roots);
    }
}
