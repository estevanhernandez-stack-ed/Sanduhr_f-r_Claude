using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
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

        // DataContext is assigned by App AFTER construction, so build the right-click
        // menu once the window is loaded (and the VM is present).
        Loaded += (_, _) => EnsureWidgetContextMenu();
    }

    private WidgetViewModel? Vm => DataContext as WidgetViewModel;

    // -- account access from the widget body ----------------------------------

    /// <summary>The widget's right-click menu (currently the gap this item closes):
    /// Accounts ▸ [switch list] · Settings… · Refresh · Hide. Built once; the
    /// Accounts submenu repopulates on open so the live registry + active marker
    /// stay current.</summary>
    private bool _menuBuilt;

    private void EnsureWidgetContextMenu()
    {
        if (_menuBuilt || Vm is null)
            return;
        _menuBuilt = true;

        // No inline brushes: the implicit ContextMenu / MenuItem styles in
        // App.xaml theme this from the active palette (DynamicResource, so it
        // re-tints live on a theme switch).
        var menu = new ContextMenu();

        var accounts = new MenuItem { Header = "Accounts" };
        // Placeholder so the submenu arrow renders; replaced on open.
        accounts.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
        accounts.SubmenuOpened += (_, _) =>
        {
            if (Vm is not null)
                AccountMenuBuilder.PopulateSwitchList(accounts.Items, Vm, includeAdd: true, includeManage: false);
        };
        menu.Items.Add(accounts);

        var settings = new MenuItem { Header = "Settings…", Command = Vm.OpenSettingsCommand };
        menu.Items.Add(settings);

        var refresh = new MenuItem { Header = "Refresh", Command = Vm.RefreshCommand };
        menu.Items.Add(refresh);

        menu.Items.Add(new Separator());

        var hide = new MenuItem { Header = "Hide" };
        hide.Click += (_, _) => Hide();
        menu.Items.Add(hide);

        RootBorder.ContextMenu = menu;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdropForTheme(Vm?.Palette ?? ThemePalette.Obsidian);
    }

    /// <summary>
    /// Set the window backdrop for the active theme. Glass themes get Mica when the
    /// OS supports it (Win11 22H2+): a Transparent window background plus a
    /// sheet-of-glass frame extension so the backdrop fills the window, with the
    /// root panel's semi-opaque glass keeping text legible. A Matrix-style opt-out
    /// theme renders <b>solid</b> — Mica is disabled and the window paints the
    /// theme's solid <c>bg</c> (the phosphor-terminal look). Older Windows always
    /// falls back to the solid background. Safe to call repeatedly on a live switch.
    /// </summary>
    public void ApplyBackdropForTheme(ThemePalette palette)
    {
        var solidBg = Application.Current.Resources["Sanduhr.Brush.Bg"] as Brush ?? Brushes.Black;

        if (palette.OptsOutOfMica)
        {
            MicaHelper.DisableBackdrop(this);
            Background = solidBg;
            Chrome.GlassFrameThickness = new Thickness(0);
            return;
        }

        bool mica = MicaHelper.TryApplyMica(this);
        if (mica)
        {
            Background = Brushes.Transparent;
            Chrome.GlassFrameThickness = new Thickness(-1);
        }
        else
        {
            Background = solidBg;
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
