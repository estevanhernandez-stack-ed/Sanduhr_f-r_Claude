using System.Windows;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Add link… (spec, Desk Launcher): an https address and an optional label. The address is checked as it is typed by the same
/// gate settings.json and the launch use (<see cref="LauncherTarget"/>), so what the dialog accepts is what the Launcher opens.
/// </summary>
internal partial class LauncherLinkWindow : Window
{
    /// <summary>The address that was entered, once Add was chosen.</summary>
    public string Url { get; private set; } = "";

    /// <summary>The label that was entered (may be empty).</summary>
    public string LinkLabel { get; private set; } = "";

    public LauncherLinkWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UrlBox.Focus();
            UrlBox.CaretIndex = UrlBox.Text.Length;
        };
        Check();
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Check();

    private void Check()
    {
        if (UrlBox is null || AddButton is null) return;
        var text = UrlBox.Text.Trim();
        // The prefilled "https://" is a start, not a mistake worth shouting about.
        bool untouched = text.Equals("https://", StringComparison.OrdinalIgnoreCase) || text.Length == 0;
        var check = LauncherTarget.Validate(LauncherKind.Link, text);
        AddButton.IsEnabled = check.Ok;
        ProblemText.Text = check.Ok || untouched ? "" : check.Reason;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var text = UrlBox.Text.Trim();
        if (!LauncherTarget.Validate(LauncherKind.Link, text).Ok) return;
        Url = text;
        LinkLabel = LabelBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
