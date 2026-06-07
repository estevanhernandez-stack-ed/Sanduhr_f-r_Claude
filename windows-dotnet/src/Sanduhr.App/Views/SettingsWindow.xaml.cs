using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Tabbed settings window (port of <c>settings_dialog.py</c>'s shell). Hosts the
/// Accounts tab (multi-account management), the Themes tab (swatch grid +
/// agent-assisted theme creation), the History tab (30-day chart + CSV export),
/// the Local CC tab (local Claude Code token-burn summary), and a thin General tab.
/// Opened non-modally and kept single-instance by the App so a live account switch
/// from here reflects in the widget immediately.
///
/// Fully themed: a custom <see cref="System.Windows.Shell.WindowChrome"/> title bar
/// (no gray OS chrome — parity with the widget) and every surface bound to the
/// app-level <c>Sanduhr.Brush.*</c> DynamicResources, so the whole window —
/// including the title bar — re-tints live when the active theme changes.
///
/// The History chart and the Local CC bar strip are custom <c>OnRender</c> controls
/// that can't bind their data declaratively, so this code-behind bridges them: it
/// re-pushes data on the VMs' <c>Changed</c> events, renders the chart on load, and
/// drives the Local CC tab's show + 30-second refresh (parity with the Python tab's
/// showEvent/hideEvent timer) only while that tab is selected.
/// </summary>
internal partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    private readonly DispatcherTimer _localCcTimer;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        // Create the timer BEFORE InitializeComponent: the TabControl can raise its
        // initial SelectionChanged during XAML load, and the handler touches it.
        _localCcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _localCcTimer.Tick += async (_, _) => await ViewModel.LocalCc.RefreshAsync();

        InitializeComponent();
        // Modal dialogs spawned by the tabs (rename, confirm, export result) parent to us.
        ViewModel.Accounts.AttachOwner(this);
        ViewModel.Themes.AttachOwner(this);
        ViewModel.History.AttachOwner(this);

        // Bridge the custom-render controls to their VMs.
        ViewModel.History.Changed += RenderHistory;
        ViewModel.LocalCc.Changed += RenderLocalCc;

        Loaded += (_, _) => RenderHistory();
        Closed += (_, _) =>
        {
            _localCcTimer.Stop();
            ViewModel.History.Changed -= RenderHistory;
            ViewModel.LocalCc.Changed -= RenderLocalCc;
        };
    }

    /// <summary>Borderless window: paint the OS frame edge + rounded corners dark so
    /// the custom themed chrome doesn't sit inside a light OS frame.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MicaHelper.ApplyDarkRoundedChrome(this);
    }

    private void RenderHistory()
        => Chart.SetData(
            ViewModel.History.BuildRows(),
            ViewModel.History.WeekMode,
            ViewModel.History.Aggregate,
            ViewModel.History.Palette);

    private void RenderLocalCc()
        => BarStrip.SetData(ViewModel.LocalCc.ByDay, ViewModel.LocalCc.Palette);

    /// <summary>Refresh + timer-arm the Local CC tab only while it's selected so we
    /// don't walk session logs on a tab the user can't see (parity with the Python
    /// tab's showEvent/hideEvent). The History chart re-renders on (re)selection too,
    /// since the control has no size until its tab is first realized.</summary>
    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
            return; // ignore bubbled selection changes from inner controls (combo, lists)

        var header = (Tabs.SelectedItem as TabItem)?.Header as string;
        if (header == "Local CC")
        {
            await ViewModel.LocalCc.RefreshAsync();
            _localCcTimer.Start();
        }
        else
        {
            _localCcTimer.Stop();
            if (header == "History")
                RenderHistory();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
