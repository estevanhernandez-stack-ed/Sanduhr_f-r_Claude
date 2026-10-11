using System.Windows;
using System.Windows.Input;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// What's New: the release highlights as a two-column card grid, each with a "Show me" that
/// opens the page it talks about. Single instance: showing it again replaces the content of
/// the open window and activates it.
/// </summary>
internal partial class WhatsNewWindow : Window
{
    private static WhatsNewWindow? _open;

    private Action<SettingsPage> _showMe;

    private WhatsNewWindow(Action<SettingsPage> showMe)
    {
        _showMe = showMe;
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this)) _open = null;
        };
    }

    /// <summary>Open the window with these cards, or refill and activate the one already open.
    /// <paramref name="showMe"/> runs after the window closes, with the card's destination.</summary>
    public static void Show(IReadOnlyList<WhatsNewCard> cards, string header, SettingsStore store,
        Action<SettingsPage> showMe)
    {
        var model = new WhatsNewViewModel(cards, header, store);
        if (_open is { } window)
        {
            window._showMe = showMe;
            window.DataContext = model;
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            return;
        }

        window = new WhatsNewWindow(showMe) { DataContext = model };
        _open = window;
        window.Show();
        window.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MicaHelper.ApplyDarkRoundedChrome(this);
    }

    private void ShowMe_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WhatsNewCardItem item)
            return;
        var destination = item.Card.Destination;
        var callback = _showMe;
        Close();
        callback(destination);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Drag by any background; buttons and the check box handle their own clicks.</summary>
    private void Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !e.Handled)
            DragMove();
    }
}
