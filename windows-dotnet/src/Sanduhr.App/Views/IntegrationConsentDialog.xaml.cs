using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>What the consent dialog asks for, in plain words. Nothing is written until the person presses the
/// confirm button.</summary>
/// <param name="Title">The question.</param>
/// <param name="Intro">One or two sentences on what the piece does.</param>
/// <param name="Folder">The folder it goes into, shown with ~ for the profile.</param>
/// <param name="Details">Exactly what is written, where, and how to undo it.</param>
/// <param name="ConfirmLabel">The button that writes, such as "Install".</param>
/// <param name="TheirCommand">When the folder already has someone else's statusline, its command, masked.
/// The dialog then offers Replace or Combine.</param>
/// <param name="OfferSegments">Show the meter picker and, with Combine, the line choice.</param>
internal sealed record ConsentSpec(
    string Title, string Intro, string Folder, string Details, string ConfirmLabel,
    string? TheirCommand = null, bool OfferSegments = false);

/// <summary>What the person chose.</summary>
internal sealed record ConsentChoice(StatuslineMode Mode, IReadOnlyList<string> Segments, string Join);

/// <summary>
/// One consent dialog for the Claude Code page's writes (statusline, meters mod, glow hooks): it names the
/// folder and exactly what is written, and offers Replace or Combine when the folder already has someone
/// else's statusline. Every write waits for the confirm button; closing the dialog changes nothing.
/// </summary>
internal partial class IntegrationConsentDialog : Window
{
    private bool _confirmed;

    private IntegrationConsentDialog(ConsentSpec spec)
    {
        InitializeComponent();
        TitleText.Text = spec.Title;
        IntroText.Text = spec.Intro;
        FolderText.Text = spec.Folder;
        DetailsText.Text = spec.Details;
        ConfirmButton.Content = spec.ConfirmLabel;
        System.Windows.Automation.AutomationProperties.SetName(ConfirmButton, spec.ConfirmLabel);   // streamer: notname - a fixed confirm label from the consent spec
        if (spec.TheirCommand is { Length: > 0 } theirs)
        {
            ExistingText.Text = "This folder already has a statusline: " + theirs;
            ExistingPanel.Visibility = Visibility.Visible;
        }
        if (spec.OfferSegments)
            SegmentsPanel.Visibility = Visibility.Visible;
        Loaded += (_, _) => Sounds.PlayInfo();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (JoinPanel is null) return;
        JoinPanel.Visibility = CombineRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnNotNowClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private ConsentChoice Choice()
    {
        var segments = new List<string>();
        if (AccountBox.IsChecked == true) segments.Add("account");
        if (SessionBox.IsChecked == true) segments.Add("session");
        if (WeeklyBox.IsChecked == true) segments.Add("weekly");
        if (ResetsBox.IsChecked == true) segments.Add("resets");
        return new ConsentChoice(
            CombineRadio.IsChecked == true ? StatuslineMode.Combine : StatuslineMode.Replace,
            StatuslineInstaller.NormalizeSegments(segments),
            SameRadio.IsChecked == true ? StatuslineInstaller.JoinSame : StatuslineInstaller.JoinLine);
    }

    /// <summary>Shows the dialog; null when the person declines or closes it.</summary>
    public static ConsentChoice? Ask(Window? owner, ConsentSpec spec)
    {
        var dlg = new IntegrationConsentDialog(spec);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._confirmed ? dlg.Choice() : null;
    }
}
