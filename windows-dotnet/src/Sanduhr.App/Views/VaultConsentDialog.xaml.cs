using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>
/// First-vault-run per-root consent (spec: Ingestion — "pre-checked, but
/// PROMPTED; silent-on for an employer root is the breach WS-E's review
/// named"). Returns a consent map covering every detected root; closing or
/// "Not now" returns all-false. Either way the caller marks vault_prompted so
/// the dialog shows once — the Claude Usage tab owns changes afterwards.
/// </summary>
internal partial class VaultConsentDialog : Window
{
    private readonly Dictionary<string, CheckBox> _checkboxes = new(StringComparer.Ordinal);
    private bool _keep;

    private VaultConsentDialog(IReadOnlyList<string> rootNames)
    {
        InitializeComponent();
        foreach (var root in rootNames)
        {
            var cb = new CheckBox
            {
                Content = root,
                IsChecked = true,   // pre-checked; the prompt itself is the consent gate
                Style = (Style)FindResource("ConsentCheckBox"),
            };
            _checkboxes[root] = cb;
            RootsPanel.Children.Add(cb);
        }
        Loaded += (_, _) => Sounds.PlayInfo();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _keep = true;
        DialogResult = true;
        Close();
    }

    private void OnNotNowClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static IReadOnlyDictionary<string, bool> ShowConsent(
        Window? owner, IReadOnlyList<string> rootNames)
    {
        var dlg = new VaultConsentDialog(rootNames);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();

        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (root, cb) in dlg._checkboxes)
            result[root] = dlg._keep && cb.IsChecked == true;
        return result;
    }
}
