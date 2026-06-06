using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App;

/// <summary>
/// The floating glass widget shell (port of <c>widget.py</c>'s window concerns).
/// Borderless via <see cref="System.Windows.Shell.WindowChrome"/>; Mica applied
/// in <see cref="OnSourceInitialized"/> with a solid obsidian fallback on
/// pre-22H2 Windows. Frame is persisted on MOVE only.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _saveDebounce;

    public MainWindow()
    {
        InitializeComponent();
        _settings = new SettingsStore(new Paths());

        // MOVE-only persistence: a single debounced save off LocationChanged.
        // SizeChanged is deliberately NOT subscribed — persisting mid-resize is
        // the Python gotcha that relaunched the widget invisible at e.g. 420x123.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveFrame();
        };

        RestoreFrame();
        LocationChanged += (_, _) => { _saveDebounce.Stop(); _saveDebounce.Start(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdrop();
    }

    /// <summary>
    /// Apply Mica when the OS supports it (Win11 22H2+): a Transparent window
    /// background plus a sheet-of-glass frame extension so the backdrop fills
    /// the window, with the root panel's semi-opaque obsidian keeping text
    /// legible. Otherwise paint a solid obsidian fallback.
    /// </summary>
    private void ApplyBackdrop()
    {
        bool mica = MicaHelper.TryApplyMica(this);
        if (mica)
        {
            Background = Brushes.Transparent;
            Chrome.GlassFrameThickness = new Thickness(-1);
        }
        else
        {
            Background = Application.Current.Resources["Sanduhr.Brush.Bg"] as Brush ?? Brushes.Black;
            Chrome.GlassFrameThickness = new Thickness(0);
        }
    }

    // -- frame persistence (move-only) ----------------------------------------

    private void RestoreFrame()
    {
        var frame = _settings.LoadFrame();
        if (frame is { } f)
        {
            Width = Math.Max(MinWidth, f.Width);
            Height = Math.Max(MinHeight, f.Height);
            Left = f.X;
            Top = f.Y;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            // Default: tucked into the bottom-right of the work area.
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Bottom - Height - 24;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }

    private void SaveFrame()
    {
        if (WindowState != WindowState.Normal)
            return;
        _settings.SaveFrame(new WindowFrame(Left, Top, ActualWidth, ActualHeight));
    }

    // -- chrome interactions --------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>× hides the widget to the tray — the app stays alive. Real quit
    /// lives in the tray menu (orderOut equivalent).</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    // -- drag-reorder drop ----------------------------------------------------

    private void Tiers_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not WidgetViewModel host)
            return;
        if (e.Data.GetData(typeof(TierCardViewModel)) is not TierCardViewModel source)
            return;

        var target = FindCardViewModel(e.OriginalSource as DependencyObject);
        int from = host.Tiers.IndexOf(source);
        int to = target is null ? host.Tiers.Count - 1 : host.Tiers.IndexOf(target);
        if (from >= 0 && to >= 0)
            host.MoveTier(from, to);
    }

    private static TierCardViewModel? FindCardViewModel(DependencyObject? origin)
    {
        var node = origin;
        while (node is not null)
        {
            if (node is TierCard card && card.DataContext is TierCardViewModel vm)
                return vm;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
