using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>Visual tone of a <see cref="ThemedDialog"/> — drives the accent dot
/// color and which procedural chime plays (never a Windows system sound).</summary>
internal enum ThemedDialogKind
{
    Info,
    Warning,
}

/// <summary>
/// A themed, in-app replacement for <c>MessageBox.Show</c>: a borderless glass
/// dialog that follows the active theme (DynamicResource brushes) and plays one of
/// the app's procedural chimes instead of the Windows system sound. <see cref="Show"/>
/// mirrors the <c>MessageBox.Show</c> shape and returns a <see cref="MessageBoxResult"/>
/// so call sites swap in with no logic change.
/// </summary>
internal partial class ThemedDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private ThemedDialog(string title, string message, MessageBoxButton buttons, ThemedDialogKind kind, bool playSound)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        if (buttons == MessageBoxButton.YesNo)
        {
            PrimaryButton.Content = "Yes";
            SecondaryButton.Content = "No";
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (kind == ThemedDialogKind.Warning)
            KindDot.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xCF, 0x4D));

        if (playSound)
            Loaded += (_, _) =>
            {
                if (kind == ThemedDialogKind.Warning)
                    Sounds.PlayError();
                else
                    Sounds.PlayInfo();
            };
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        Result = SecondaryButton.Visibility == Visibility.Visible
            ? MessageBoxResult.Yes
            : MessageBoxResult.OK;
        DialogResult = true;
        Close();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
        Close();
    }

    /// <summary>Themed drop-in for <c>MessageBox.Show</c>. Centers on
    /// <paramref name="owner"/> when one is loaded, else on screen.
    /// <paramref name="primaryLabel"/> / <paramref name="secondaryLabel"/> override the
    /// default Yes/No (or OK) button text — a generic Yes/No cannot carry a consequence
    /// like "erase history"; the button must say what it does. Result mapping is
    /// unchanged: primary still returns Yes/OK, secondary still returns No.</summary>
    public static MessageBoxResult Show(Window? owner, string title, string message,
        MessageBoxButton buttons = MessageBoxButton.OK,
        ThemedDialogKind kind = ThemedDialogKind.Info,
        bool playSound = true,
        string? primaryLabel = null,
        string? secondaryLabel = null)
    {
        var dlg = new ThemedDialog(title, message, buttons, kind, playSound);
        if (primaryLabel is not null)
            dlg.PrimaryButton.Content = primaryLabel;
        if (secondaryLabel is not null)
            dlg.SecondaryButton.Content = secondaryLabel;
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }

    /// <summary>A yes/no confirm whose Yes deletes the person's data (Uninstall): the named buttons, no default button so Enter picks nothing, and
    /// Escape or closing the window answers No. Built apart from <see cref="ShowConfirm"/> so a test can read the buttons.</summary>
    internal static ThemedDialog BuildConfirm(string title, string message, string confirmLabel, string cancelLabel)
    {
        var dlg = new ThemedDialog(title, message, MessageBoxButton.YesNo, ThemedDialogKind.Warning, playSound: true);
        dlg.PrimaryButton.Content = confirmLabel;
        dlg.PrimaryButton.IsDefault = false;
        dlg.SecondaryButton.Content = cancelLabel;
        dlg.SecondaryButton.IsCancel = true;
        return dlg;
    }

    /// <summary>Shows <see cref="BuildConfirm"/>; true only when the person clicked the confirm button.</summary>
    public static bool ShowConfirm(Window? owner, string title, string message, string confirmLabel, string cancelLabel)
    {
        var dlg = BuildConfirm(title, message, confirmLabel, cancelLabel);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result == MessageBoxResult.Yes;
    }

    private void OnTertiaryClick(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }

    /// <summary>A three-way question (theme match: "Add the Desk look to Dusk", "Make a new theme", Cancel). Yes for the primary, No for the
    /// secondary, Cancel for the Cancel button, Escape or closing the window. Sizes to its buttons; Enter picks nothing, since the primary
    /// choice writes into the person's own theme.</summary>
    public static MessageBoxResult ShowChoice(Window? owner, string title, string message, string primaryLabel, string secondaryLabel)
    {
        var dlg = new ThemedDialog(title, message, MessageBoxButton.YesNo, ThemedDialogKind.Info, playSound: true);
        dlg.PrimaryButton.Content = primaryLabel;
        dlg.PrimaryButton.IsDefault = false;
        dlg.SecondaryButton.Content = secondaryLabel;
        dlg.SecondaryButton.IsCancel = false;
        dlg.TertiaryButton.Content = Sanduhr.Core.SettingsNames.ThemeMatchCancel;
        dlg.TertiaryButton.Visibility = Visibility.Visible;
        dlg.TertiaryButton.IsCancel = true;
        dlg.Result = MessageBoxResult.Cancel;
        dlg.SizeToContent = SizeToContent.WidthAndHeight;
        dlg.MinWidth = 430;
        dlg.MaxWidth = 720;
        dlg.MessageText.MaxWidth = 520;
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }
}
