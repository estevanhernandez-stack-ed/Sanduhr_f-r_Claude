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
    /// <paramref name="owner"/> when one is loaded, else on screen.</summary>
    public static MessageBoxResult Show(Window? owner, string title, string message,
        MessageBoxButton buttons = MessageBoxButton.OK,
        ThemedDialogKind kind = ThemedDialogKind.Info,
        bool playSound = true)
    {
        var dlg = new ThemedDialog(title, message, buttons, kind, playSound);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }
}
