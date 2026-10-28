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
    private static WhatsNewWindow? _tour;

    private Action<SettingsPage, string?> _showMe;

    /// <summary>Step mode only: how the tour ended (true = Finish, false = Skip, Escape or Alt+F4).</summary>
    private Action<bool>? _ended;
    private bool _endReported;

    private WhatsNewWindow(Action<SettingsPage, string?> showMe)
    {
        _showMe = showMe;
        InitializeComponent();
        // About writes the same "Don't show What's New after updates" key: read it again when this window is brought back (slice 6),
        // as About reads it again when Settings is (slice 5, P14). The tour's model has no such box.
        Activated += (_, _) => (DataContext as WhatsNewViewModel)?.Reload();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this)) _open = null;
            if (ReferenceEquals(_tour, this))
            {
                _tour = null;
                // Closed any other way than Finish counts as Skip, except when the app is
                // quitting: then the tour simply offers again next launch.
                if (!Quitting)
                    ReportEnd(false);
            }
        };
    }

    /// <summary>Set by the app as it quits (Quit, a Store update, sign-out of Windows), so the
    /// windows it closes are not read as the person skipping the tour.</summary>
    public static bool Quitting { get; set; }

    /// <summary>
    /// Open the welcome tour (the window in step mode), or bring the open one forward. Show me
    /// leaves the tour open; <paramref name="ended"/> runs once, when it finishes or is skipped.
    /// </summary>
    public static void ShowTour(WelcomeTourViewModel model, Action<SettingsPage, string?> showMe, Action<bool> ended)
    {
        if (_tour is { } open)
        {
            // Refill with the current steps (say, signed in since it opened) on the same step.
            if (open.DataContext is WelcomeTourViewModel was)
                model.GoTo(was.Index);
            open.DataContext = model;
            open._showMe = showMe;
            if (open.WindowState == WindowState.Minimized)
                open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new WhatsNewWindow(showMe) { DataContext = model, _ended = ended, Height = 560 };
        _tour = window;
        window.Show();
        window.Activate();
    }

    private void ReportEnd(bool finished)
    {
        if (_endReported || _ended is null)
            return;
        _endReported = true;
        _ended(finished);
    }

    /// <summary>Open the window with these cards, or refill and activate the one already open.
    /// <paramref name="showMe"/> runs after the window closes, with the card's destination and anchor.</summary>
    public static void Show(IReadOnlyList<WhatsNewCard> cards, string header, SettingsStore store,
        Action<SettingsPage, string?> showMe)
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
        var anchor = item.Card.Anchor;
        var callback = _showMe;
        // The tour stays open behind the page it shows (as on the Mac); What's New closes.
        if (DataContext is not WelcomeTourViewModel)
            Close();
        callback(destination, anchor);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ReportEnd(false);
        Close();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WelcomeTourViewModel tour)
            return;
        if (tour.Next())
            return;
        ReportEnd(true);
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => (DataContext as WelcomeTourViewModel)?.Back();

    /// <summary>Drag by any background; buttons and the check box handle their own clicks.</summary>
    private void Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !e.Handled)
            DragMove();
    }
}
