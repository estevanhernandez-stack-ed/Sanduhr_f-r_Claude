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
    private double _expandedHeight;

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
        // menu + wire focus/game once the window is loaded (and the VM is present).
        Loaded += (_, _) =>
        {
            EnsureWidgetContextMenu();
            WireFocusAndGame();
        };
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

        var compact = new MenuItem { Command = Vm.ToggleCompactCommand };
        menu.Opened += (_, _) => compact.Header = (Vm?.IsCompact ?? false) ? "Expand" : "Compact Mode";
        menu.Items.Add(compact);

        menu.Items.Add(new Separator());

        var focus = new MenuItem { Header = "Deep Work (Focus)", Command = Vm.ToggleFocusCommand };
        menu.Items.Add(focus);

        var game = new MenuItem { Header = "Cooldown Snake", Command = Vm.StartGameCommand };
        menu.Items.Add(game);

        menu.Items.Add(new Separator());

        var hide = new MenuItem { Header = "Hide" };
        hide.Click += (_, _) => Hide();
        menu.Items.Add(hide);

        RootBorder.ContextMenu = menu;
    }

    // -- focus mode + cooldown game -------------------------------------------

    private bool _focusWired;

    /// <summary>Wire the focus timer + snake overlay to the VM's affordance events
    /// and seed their palettes. Idempotent (Loaded can fire more than once).</summary>
    private void WireFocusAndGame()
    {
        if (_focusWired || Vm is null)
            return;
        _focusWired = true;

        FocusView.ApplyPalette(Vm.Palette);
        GameView.ApplyPalette(Vm.Palette);

        Vm.FocusToggleRequested += ToggleFocusMode;
        Vm.GameStartRequested += StartCooldownGame;
        Vm.CompactChanged += OnCompactChanged;
        Vm.ThemeChanged += p =>
        {
            FocusView.ApplyPalette(p);
            GameView.ApplyPalette(p);
        };

        // Session complete → restore the cards. The in-panel Stop button keeps the
        // user on the focus page (setup state); the Focus affordance toggles out.
        FocusView.Finished += ExitFocusMode;
        FocusView.Started += minutes => Vm?.SaveFocusDuration(minutes);

        GameView.Finished += ExitGame;
        GameView.HighScoreChanged += score => Vm?.SaveSnakeHighScore(score);
    }

    /// <summary>Hide the content chrome — the tier cards (which carry the account
    /// switcher + StatusText) and the bottom icon strip. Shared by focus mode and the
    /// cooldown game so both overlays read as one clean surface.</summary>
    private void HideContentChrome()
    {
        CardsScroller.Visibility = Visibility.Collapsed;
        BottomIconStrip.Visibility = Visibility.Collapsed;
    }

    /// <summary>Restore the content chrome hidden by <see cref="HideContentChrome"/>.</summary>
    private void ShowContentChrome()
    {
        CardsScroller.Visibility = Visibility.Visible;
        BottomIconStrip.Visibility = Visibility.Visible;
    }

    /// <summary>Enter focus mode if out, exit if in — ports
    /// <c>widget._toggle_focus_mode</c>.</summary>
    private void ToggleFocusMode()
    {
        if (FocusView.IsActive || FocusView.Visibility == Visibility.Visible)
            ExitFocusMode();
        else
            EnterFocusMode();
    }

    private void EnterFocusMode()
    {
        ThemePopup.IsOpen = false; // a Popup is a top-level HWND — close it so it can't float over the hourglass
        HideContentChrome();
        FocusView.Visibility = Visibility.Visible;
        // Land on the setup state (stepper + Start) seeded with the saved duration —
        // the user adjusts the minutes and clicks Start. Do NOT auto-start; Start
        // persists the chosen duration via FocusView.Started → SaveFocusDuration.
        FocusView.PrepareSetup(Vm?.LoadFocusDuration() ?? 25);
    }

    private void ExitFocusMode()
    {
        FocusView.Stop();
        FocusView.Visibility = Visibility.Collapsed;
        ShowContentChrome();
    }

    /// <summary>Open the cooldown snake. Mirrors focus mode: hide the content chrome
    /// first so the board (and its score) own the surface with nothing behind to
    /// collide with.</summary>
    private void StartCooldownGame()
    {
        ThemePopup.IsOpen = false; // close the swatch flyout so it can't float over the board
        HideContentChrome();
        GameView.Start(Vm?.LoadSnakeHighScore() ?? 0);
    }

    /// <summary>Game closed (Esc) — restore the content chrome and refocus the
    /// window. If focus mode is the layer underneath, leave the chrome hidden so we
    /// don't pull it out from under the active hourglass.</summary>
    private void ExitGame()
    {
        if (FocusView.Visibility != Visibility.Visible)
            ShowContentChrome();
        Focus();
    }

    /// <summary>Compact toggled — auto-size the window's HEIGHT to the single remaining
    /// card (width untouched, matching Python), restoring the expanded height on the
    /// way out. Deferred a layout pass so the card collection has settled before we
    /// measure (Python uses QTimer.singleShot(0) for the same reason).</summary>
    private void OnCompactChanged(bool compact)
    {
        // Hide the toolbar in compact (footer stays), like the Python tool strip.
        BottomIconStrip.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (compact)
        {
            _expandedHeight = ActualHeight;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                SizeToContent = SizeToContent.Height;
            }));
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            if (_expandedHeight > 0)
                Height = _expandedHeight;
        }
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
        // Double-click the title bar toggles compact mode (widget.py parity).
        if (e.ClickCount == 2)
        {
            Vm?.ToggleCompactCommand.Execute(null);
            return;
        }
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
