using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The Settings window (Settings v2): a grouped sidebar with search. The sidebar is the TabControl
/// re-templated; its groups and their order come from <see cref="SettingsNames.Sidebar"/>, and the constructor sets
/// each group's heading on that group's first page. Each TabItem's Tag is its <see cref="SettingsPage"/>, which
/// <see cref="SelectPage"/> and the search use. <see cref="SelectPage"/> resolves an old link through
/// <see cref="SettingsIndex.Resolve"/> before it lands; <see cref="Place"/> is where the window was left, for the
/// App to reopen there; <see cref="FitToWorkArea"/> is called before the first Show so a short screen never hides
/// the foot; Quit asks before it quits.
/// Opened non-modally and kept single-instance by the App so a live account switch
/// from here reflects in the widget immediately.
///
/// Fully themed: a custom <see cref="System.Windows.Shell.WindowChrome"/> title bar
/// (no gray OS chrome — parity with the widget) and every surface bound to the
/// app-level <c>Sanduhr.Brush.*</c> DynamicResources, so the whole window —
/// including the title bar — re-tints live when the active theme changes.
///
/// The History chart and the Claude Usage Overview bar strip are custom
/// <c>OnRender</c> controls that can't bind their data declaratively, so this
/// code-behind bridges them: it re-pushes data on the VMs' <c>Changed</c>
/// events, renders the chart on load, and drives the Claude Usage tab's show +
/// 30-second refresh (parity with the Python tab's showEvent/hideEvent timer)
/// only while that tab is selected.
/// </summary>
internal partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    private readonly DispatcherTimer _localCcTimer;

    /// <summary>
    /// Where the window is now: the page, and the anchor <see cref="SelectPage"/> last opened it at (null after a sidebar
    /// click). App keeps it when the window closes, to reopen there this session (spec, Reopen where it was left).
    /// </summary>
    public SettingsLink? Place { get; private set; }

    /// <summary>Opens a page's https link (the Open-Meteo credit and licence) in the default browser.</summary>
    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri is not { Scheme: "https" } uri) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        // Create the timer BEFORE InitializeComponent: the TabControl can raise its
        // initial SelectionChanged during XAML load, and the handler touches it.
        _localCcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _localCcTimer.Tick += async (_, _) =>
        {
            if (ViewModel.ClaudeCode.Section == "Overview")
                await ViewModel.ClaudeCode.Overview.RefreshAsync();
        };

        InitializeComponent();
        // A button carrying views:SettingsLinkTo.Link opens that place, wherever it sits (Desk, Pieces' rows, a page's status line).
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnSettingsLinkClick));
        // The Desk's Layout map folds below 600 DIP. A window's first SizeChanged comes before Loaded, so Loaded folds a window that opens short.
        SizeChanged += (_, _) => FoldMapForHeight();
        Loaded += (_, _) => FoldMapForHeight();
        // The sidebar's group headings have one source, the names table: each group's first page carries its title.
        foreach (var group in SettingsNames.Sidebar)
            if (group.Title is { } title
                && Tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string?)t.Tag == group.Pages[0].ToString()) is { } first)
                SettingsSidebar.SetGroupHeader(first, title);
        // Modal dialogs spawned by the tabs (rename, confirm, export result) parent to us.
        ViewModel.AttachOwner(this);
        ViewModel.Accounts.AttachOwner(this);
        ViewModel.Themes.AttachOwner(this);
        ViewModel.History.AttachOwner(this);
        ViewModel.ClaudeCode.Overview.AttachOwner(this);
        ViewModel.ClaudeCode.Ledger.AttachOwner(this);
        ViewModel.ClaudeCode.Attach();
        ViewModel.Publish.AttachOwner(this);
        ViewModel.Publish.Attach();
        ViewModel.ClaudeCodePage.AttachOwner(this);
        ViewModel.ModsPage.AttachOwner(this);
        ViewModel.Desk.Launcher.AttachOwner(this);
        ViewModel.NowPlayingPage.AttachOwner(this);

        // Bridge the custom-render controls to their VMs.
        ViewModel.History.Changed += RenderHistory;
        ViewModel.ClaudeCode.Overview.Changed += RenderLocalCc;
        ViewModel.ClaudeCode.Trends.Changed += RenderTrends;
        CalendarStrip.DayClicked += OnCalendarDayClicked;

        Loaded += (_, _) => RenderHistory();
        PreviewKeyDown += OnRecorderPreviewKeyDown;
        Deactivated += (_, _) => ViewModel.Shortcuts.CancelRecording();
        Closed += (_, _) =>
        {
            ViewModel.Desk.Flush();
            ViewModel.Desk.Launcher.Detach();
            ViewModel.MessagePage.Dispose();
            ViewModel.Shortcuts.Detach();
            ViewModel.General.Detach();
            _localCcTimer.Stop();
            ViewModel.History.Changed -= RenderHistory;
            ViewModel.History.Detach();
            ViewModel.ClaudeCode.Overview.Changed -= RenderLocalCc;
            ViewModel.ClaudeCode.Trends.Changed -= RenderTrends;
            CalendarStrip.DayClicked -= OnCalendarDayClicked;
            ViewModel.ClaudeCode.Detach();
            ViewModel.Publish.Detach();
            ViewModel.Themes.Detach();
        };
    }

    /// <summary>Calendar day-click → jump to the Sessions ledger scoped to
    /// that day. Swallows navigation faults per the window's global
    /// every-UI-path-caught constraint (mirrors OpenDayInSessionsAsync's own
    /// guard — belt and suspenders across the event-handler boundary).</summary>
    private async void OnCalendarDayClicked(DateOnly day)
    {
        try { await ViewModel.ClaudeCode.OpenDayInSessionsAsync(day); } catch { }
    }

    /// <summary>Borderless window: paint the OS frame edge + rounded corners dark so
    /// the custom themed chrome doesn't sit inside a light OS frame.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MicaHelper.ApplyDarkRoundedChrome(this);
    }

    /// <summary>
    /// Before the first Show: sizes and places the window inside the work area of the monitor <paramref name="near"/> is
    /// on (the primary monitor without one), at most its designed 780 x 800 and at least MinWidth x MinHeight, centred,
    /// so its foot and Quit never sit off screen (spec, "Short windows"; WF41). A work area that cannot be read, or that
    /// is empty or not a number, leaves the window as the XAML sizes it.
    /// </summary>
    internal void FitToWorkArea(Window? near)
    {
        if (WorkAreaNear(near) is not { } work)
            return;
        var fit = WindowFit.Fit(work, Width, Height, MinWidth, MinHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = fit.Width;
        Height = fit.Height;
        Left = fit.Left;
        Top = fit.Top;
    }

    /// <summary>The work area, in DIPs, of the monitor <paramref name="near"/> is on; the primary's when it has no handle; null when unusable.</summary>
    private static WindowFit.Area? WorkAreaNear(Window? near)
    {
        var primary = SystemParameters.WorkArea;
        var fallback = Usable(new WindowFit.Area(primary.Left, primary.Top, primary.Width, primary.Height));
        var hwnd = near is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(near).Handle;
        if (hwnd == IntPtr.Zero)
            return fallback;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        var monitor = GlowNative.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new GlowNative.MONITORINFO { cbSize = Marshal.SizeOf<GlowNative.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GlowNative.GetMonitorInfoW(monitor, ref info))
            return fallback;
        // rcWork is in the coordinates this process runs in (system-scaled pixels while the process is system-DPI aware;
        // the owner's own scale if it ever goes per-monitor), and the owner, which sits on that monitor, draws at that scale.
        var r = info.rcWork;
        return WorkAreaDips(r.Left, r.Top, r.Right, r.Bottom, VisualTreeHelper.GetDpi(near!).DpiScaleX) ?? fallback;
    }

    /// <summary>A work-area rectangle in pixels as DIPs at <paramref name="scale"/>; null when it is empty, inverted or the scale is not a positive number.</summary>
    internal static WindowFit.Area? WorkAreaDips(int left, int top, int right, int bottom, double scale) =>
        double.IsFinite(scale) && scale > 0
            ? Usable(new WindowFit.Area(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale))
            : null;

    private static WindowFit.Area? Usable(WindowFit.Area a) =>
        double.IsFinite(a.Left) && double.IsFinite(a.Top) && double.IsFinite(a.Width) && double.IsFinite(a.Height)
        && a.Width > 0 && a.Height > 0
            ? a
            : null;

    private void RenderHistory()
        => Chart.SetData(
            ViewModel.History.BuildRows(),
            ViewModel.History.WeekMode,
            ViewModel.History.Aggregate,
            ViewModel.History.Palette);

    private void RenderLocalCc()
    {
        BarStrip.SetData(ViewModel.ClaudeCode.Overview.ByDay, ViewModel.ClaudeCode.Overview.Palette);
        CalendarStrip.SetData(
            ViewModel.ClaudeCode.Overview.CalendarDays,
            ViewModel.ClaudeCode.Overview.UncoveredDays,
            ViewModel.ClaudeCode.Overview.CalendarWindowStart,
            ViewModel.ClaudeCode.Overview.Palette);
    }

    private void RenderTrends()
        => TrendsChart.SetData(ViewModel.ClaudeCode.Trends.Weeks, ViewModel.ClaudeCode.Trends.Palette);

    /// <summary>Refresh + timer-arm the Claude Usage tab only while it's selected so
    /// we don't walk session logs on a tab the user can't see (parity with the
    /// Python tab's showEvent/hideEvent). The History chart re-renders on
    /// (re)selection too, since the control has no size until its tab is first
    /// realized.</summary>
    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
            return; // ignore bubbled selection changes from inner controls (combo, lists)

        var page = PageOf(Tabs.SelectedItem as TabItem);
        if (page is { } shown)
            Place = new SettingsLink(shown, null);
        ViewModel.ClaudeCode.IsTabActive = page == SettingsPage.ClaudeUsage;
        if (page == SettingsPage.ClaudeUsage)
        {
            await ViewModel.ClaudeCode.RefreshActiveAsync();
            _localCcTimer.Start();
        }
        else
        {
            _localCcTimer.Stop();
            if (page == SettingsPage.History)
                RenderHistory();
        }

        // The Claude Code page reads files other tools change: look again each time it is opened. Mods & Widgets lists itself when it
        // becomes visible (ModsPage.Activate).
        if (page == SettingsPage.ClaudeCode)
            ViewModel.ClaudeCodePage.Reload();
    }

    /// <summary>The page a sidebar entry stands for: its Tag names the <see cref="SettingsPage"/>.</summary>
    private static SettingsPage? PageOf(TabItem? tab) =>
        tab?.Tag is string tag && Enum.TryParse<SettingsPage>(tag, out var page) ? page : null;

    /// <summary>While the shortcut recorder is listening, every key press is a candidate combo:
    /// Escape cancels, a pure modifier is ignored, anything else goes to the view model.</summary>
    private void OnRecorderPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var shortcuts = ViewModel.Shortcuts;
        if (!shortcuts.IsRecording)
            return;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;
        if (key == Key.Escape)
        {
            shortcuts.CancelRecording();
            return;
        }
        if (key == Key.None)
            return;

        var mods = HotKeyModifiers.None;
        var held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control)) mods |= HotKeyModifiers.Control;
        if (held.HasFlag(ModifierKeys.Alt)) mods |= HotKeyModifiers.Alt;
        if (held.HasFlag(ModifierKeys.Shift)) mods |= HotKeyModifiers.Shift;
        if (held.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            mods |= HotKeyModifiers.Win;

        shortcuts.TryRecord(KeyInterop.VirtualKeyFromKey(key), mods);
    }

    private void ShortcutKeys_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ViewModel.Shortcuts.CancelRecording();

    /// <summary>
    /// Open <paramref name="page"/> and, with an <paramref name="anchor"/> from
    /// <see cref="SettingsIndex"/>, scroll to that section and outline it for 1.5 s. On Claude Usage
    /// the anchor's view (Overview, Trends, Sessions) is selected first. Widget is not a Settings
    /// page and is ignored; an anchor the page doesn't have opens the page's top.
    /// </summary>
    public void SelectPage(SettingsPage page, string? anchor = null)
    {
        // An old page or anchor (a card, a menu or agent text from before 4.0) lands where it lives now.
        (page, anchor) = SettingsIndex.Resolve(page, anchor);
        var tab = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => PageOf(t) == page);
        if (tab is null)
            return;
        // A link from outside (a card's Show me, a menu) shows the page list, not an old search.
        ViewModel.Search.Clear();
        var entry = SettingsIndex.Find(page, anchor);

        // Claude Usage: pick the anchor's view first. Selecting the page then refreshes that view
        // once; on the page already shown, the view's own command refreshes it.
        if (page == SettingsPage.ClaudeUsage && entry?.Subsection is { } view && ViewModel.ClaudeCode.Section != view)
        {
            if (ReferenceEquals(Tabs.SelectedItem, tab))
            {
                var command = view switch
                {
                    SettingsIndex.TrendsView => ViewModel.ClaudeCode.SetTrendsCommand,
                    SettingsIndex.SessionsView => ViewModel.ClaudeCode.SetSessionsCommand,
                    _ => ViewModel.ClaudeCode.SetOverviewCommand,
                };
                command.Execute(null);
            }
            else
            {
                ViewModel.ClaudeCode.Section = view;
            }
        }
        // Themes: the paste box only exists in paste mode.
        if (page == SettingsPage.Themes && anchor == SettingsIndex.Paste)
            ViewModel.Themes.IsStudioMode = false;

        Tabs.SelectedItem = tab;
        // Claude Code, What this folder holds is one anchor on the page: a link to it opens one card's report (slice 4, decision 5). After the
        // selection, whose page reload rebuilds the cards.
        if (page == SettingsPage.ClaudeCode && entry?.Anchor == SettingsIndex.FolderConfig)
            ViewModel.ClaudeCodePage.OpenHoldsForLanding();
        Place = new SettingsLink(page, entry?.Anchor);
        if (entry is null)
            return;

        // After layout: the page may have just been realized for the first time.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (tab.Content is not DependencyObject content || SettingsAnchor.Find(content, entry.Anchor!) is not { } target)
            {
                tab.Focus();   // never leave keyboard focus nowhere
                return;
            }
            // A section inside a collapsed group (a search hit "(in Advanced)") opens its group first.
            if (RevealInside(target, content))
                target.UpdateLayout();
            if (!target.IsVisible)
            {
                tab.Focus();
                return;
            }
            target.BringIntoView();
            if (TryFindResource("Sanduhr.Brush.Accent") is Brush accent)
                AnchorHighlightAdorner.Flash(target, accent, TimeSpan.FromSeconds(1.5));
            // Keyboard users land on the section: its first control that takes focus, else the
            // next one after a heading, else the page's sidebar entry.
            if (FirstFocusable(target) is { } control)
                control.Focus();
            else if (target.PredictFocus(FocusNavigationDirection.Down) is UIElement { IsVisible: true } next
                     && IsInside(next, content))
                next.Focus();
            else
                tab.Focus();
        }));
    }

    private static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, root))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Opens every collapsed Expander between <paramref name="target"/> and the page's content root
    /// <paramref name="root"/> (exclusive: the walk stops there and never opens anything above the page), so a section
    /// inside one can be shown and focused. True when one was opened.
    /// </summary>
    internal static bool RevealInside(DependencyObject target, DependencyObject root)
    {
        bool opened = false;
        for (var d = target; d is not null && !ReferenceEquals(d, root); d = LogicalTreeHelper.GetParent(d) ?? (d is Visual ? VisualTreeHelper.GetParent(d) : null))
        {
            if (d is Expander { IsExpanded: false } expander)
            {
                expander.IsExpanded = true;
                opened = true;
            }
        }
        return opened;
    }

    private static UIElement? FirstFocusable(DependencyObject root)
    {
        if (root is UIElement { Focusable: true, IsVisible: true, IsEnabled: true } el
            && root is Control)
            return el;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            if (FirstFocusable(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }
        return null;
    }

    // ---- sidebar search ----

    private ListBox? SearchResultsList => Tabs.Template?.FindName("SearchResults", Tabs) as ListBox;

    /// <summary>Enter opens the best hit, Down moves into the results, Escape clears the search.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var search = ViewModel.Search;
        switch (e.Key)
        {
            case Key.Enter when search.First is { } first:
                e.Handled = true;
                OpenSearchRow(first);
                break;
            case Key.Down when search.Results.Count > 0 && SearchResultsList is { } list:
                e.Handled = true;
                list.SelectedIndex = 0;
                list.UpdateLayout();
                (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                break;
            case Key.Escape when search.IsSearching:
                e.Handled = true;
                search.Clear();
                break;
        }
    }

    /// <summary>Enter opens the selected result; Escape goes back to the search box.</summary>
    private void SearchResults_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchResultsList?.SelectedItem is SettingsSearchRow row)
        {
            e.Handled = true;
            OpenSearchRow(row);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            (Tabs.Template?.FindName("SearchBox", Tabs) as TextBox)?.Focus();
        }
    }

    private void SearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(SearchResultsList, source) is ListBoxItem { DataContext: SettingsSearchRow row })
            OpenSearchRow(row);
    }

    /// <summary>A chosen result: the search closes and the page opens at the section.</summary>
    private void OpenSearchRow(SettingsSearchRow row)
    {
        if (row.Anchor == SettingsIndex.Quit)
        {
            // Quit lives at the sidebar's foot: show the list, put the keyboard on the button.
            ViewModel.Search.Clear();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                (Tabs.Template?.FindName("QuitButton", Tabs) as Button)?.Focus()));
            return;
        }
        SelectPage(row.Page, row.Anchor);
        if (row.Anchor is null)
            (Tabs.SelectedItem as TabItem)?.Focus();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>A link inside Settings (an empty list's "Claude Code settings…"): its Tag is a link, "claudecode#folders".</summary>
    private void OpenSettingsLink_Click(object sender, RoutedEventArgs e) => OpenLink((sender as FrameworkElement)?.Tag as string);

    /// <summary>Opens the place a clicked button's <see cref="SettingsLinkTo.Link"/> names.</summary>
    private void OnSettingsLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && SettingsLinkTo.GetLink(d) is { Length: > 0 } text)
        {
            OpenLink(text);
            e.Handled = true;
        }
    }

    // The Desk's Layout map folds below 600 DIP of window height (spec, Short windows). A fold or unfold by the person, or a link that
    // opens it, holds for this window's life, as on the Mac.
    private bool? _mapChosenOpen;
    private bool _foldingMap;

    private void FoldMapForHeight()
    {
        if (!IsLoaded || ActualHeight <= 0) return;
        bool open = !WindowFit.MapFolds(ActualHeight, _mapChosenOpen);
        if (DeskMapFold.IsExpanded == open) return;
        _foldingMap = true;
        DeskMapFold.IsExpanded = open;
        _foldingMap = false;
    }

    private void DeskMapFold_Toggled(object sender, RoutedEventArgs e)
    {
        // XAML's own IsExpanded="True" raises Expanded while loading: that is not a choice.
        if (IsLoaded && !_foldingMap && ReferenceEquals(e.OriginalSource, sender))
            _mapChosenOpen = DeskMapFold.IsExpanded;
    }

    /// <summary>Selects the page and anchor a link's text names ("claudecode#folders"); the widget is not a Settings page.</summary>
    private void OpenLink(string? text)
    {
        if (text is not null && SettingsIndex.ParseLink(text) is { } link && link.Page != SettingsPage.Widget)
            SelectPage(link.Page, link.Anchor);
    }
}
