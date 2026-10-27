using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>An account choice in the Meters view's selector. <c>Account == null</c>
/// is the "All accounts" overlay.</summary>
public sealed record AccountOption(string Display, string? Account)
{
    /// <summary>What a screen reader reads: the shown name, never the record's text, which would carry the real label (slice 7, CP 3).</summary>
    public override string ToString() => Display;
}

/// <summary>A legend entry — an account's label and its overlay swatch brush.</summary>
public sealed record LegendItem(string Label, Brush Swatch)
{
    /// <summary>What a screen reader reads for the legend entry (the swatch is colour only).</summary>
    public override string ToString() => Label;
}

/// <summary>
/// Backs Settings, Claude Usage, Meters — the 30-day meter history with the per-account
/// vs all-accounts overlay toggle, the Week/Month window selector, the legend, and
/// the CSV export (Clear moved to Accounts, Data for this account, in Settings v2). Ported from
/// <c>settings_dialog._build_history_tab</c> + the surrounding handlers.
///
/// The chart itself is a custom-render control (<see cref="HistoryChart"/>); this
/// VM assembles the render rows from the shared <see cref="UsageHistory"/> and
/// raises <see cref="Changed"/> so the window re-pushes them. CSV building is the
/// Core-tested <see cref="CsvExport"/>; this layer only owns the save dialog + write.
/// </summary>
public sealed partial class HistoryTabViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private Window? _owner;

    /// <summary>Raised when the chart needs re-rendering (mode / account / theme /
    /// clear). The host window re-pushes <see cref="BuildRows"/> into the chart.</summary>
    public event Action? Changed;

    /// <summary>false = Month (30 days, default chart identity); true = Week (7 days).
    /// Matches the Python chart's "week"/"month" mode toggle. Defaults to Week to
    /// mirror <c>settings_dialog</c> (Week button checked on open).</summary>
    [ObservableProperty] private bool _weekMode = true;

    public ObservableCollection<AccountOption> AccountOptions { get; } = new();
    public ObservableCollection<LegendItem> Legend { get; } = new();

    [ObservableProperty] private AccountOption? _selectedOption;
    [ObservableProperty] private bool _showLegend;

    /// <summary>True in all-accounts mode (overlay one line per account); false for
    /// a single selected account (accent line + right-edge state label).</summary>
    public bool Aggregate => SelectedOption?.Account is null;

    public ThemePalette Palette => _widget.Palette;

    public HistoryTabViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        // Live theme switch re-tints the chart (accent line / text inks).
        _widget.ThemeChanged += OnThemeChanged;
        // Registry mutations (add / remove / rename) and demo mode rebuild the account selector.
        _widget.AccountsChanged += RefreshAccountOptions;
        RefreshAccountOptions();
    }

    private void OnThemeChanged(ThemePalette _) => Changed?.Invoke();

    /// <summary>Stop following the widget (Settings closed): it outlives every Settings window.</summary>
    public void Detach()
    {
        _widget.ThemeChanged -= OnThemeChanged;
        _widget.AccountsChanged -= RefreshAccountOptions;
    }

    /// <summary>Redraw the chart (Accounts cleared a history, or the Meters view came into view).</summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>Export acts on real history files: never while demo mode shows sample
    /// accounts, whose labels can match a real account's ("Personal").</summary>
    private bool NotDemo => !_widget.IsDemo;

    /// <summary>Modal dialogs (export result, clear confirm) parent to the Settings window.</summary>
    public void AttachOwner(Window owner) => _owner = owner;

    private void RefreshAccountOptions()
    {
        var prev = SelectedOption?.Account;
        AccountOptions.Clear();
        AccountOptions.Add(new AccountOption("All accounts", null));
        foreach (var label in _widget.ShownAccounts)
            AccountOptions.Add(new AccountOption(StreamerMode.Mask.Name(CodenameCategory.Account, label), label));   // slice 7, CP 3
        SelectedOption = AccountOptions.FirstOrDefault(o => o.Account == prev) ?? AccountOptions[0];
        ExportCsvCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOptionChanged(AccountOption? value)
    {
        RefreshLegend();
        Changed?.Invoke();
    }

    partial void OnWeekModeChanged(bool value) => Changed?.Invoke();

    /// <summary>Streamer mode moved (slice 7): the picker, the legend and the export caption read their names again.</summary>
    public void RefreshNames()
    {
        RefreshAccountOptions();
        RefreshLegend();
        OnPropertyChanged(nameof(ExportsKeepRealNamesShown));
    }

    /// <summary>"Exports keep real names." beside the export button, while streamer mode is on (spec).</summary>
    public bool ExportsKeepRealNamesShown => StreamerMode.On;

    /// <summary>The Save dialog's default name (slice 7, CP 4): the account's codename while hidden, never its label. The file itself keeps
    /// real names (spec).</summary>
    internal static string ExportFileName(string? account, DateTime day)
    {
        string scopeTag = account is null
            ? "all-accounts"
            : StreamerMode.Mask.Name(CodenameCategory.Account, account).ToLowerInvariant().Replace(" ", "-");
        return $"Sanduhr-usage-{scopeTag}-{day:yyyy-MM-dd}.csv";
    }

    /// <summary>"Wrote N rows to:" with the path's user folder as %USERPROFILE% while streamer mode is on (slice 7, CP 4).</summary>
    internal static string ExportedMessage(int rows, string path) => $"Wrote {rows} rows to:\n{StreamerMode.Mask.Paths(path)}";

    /// <summary>The write failure, with its path and the exception's own text scrubbed while streamer mode is on (slice 7, CP 4).</summary>
    internal static string ExportFailedMessage(string path, string error) => StreamerMode.Mask.Paths($"Could not write {path}:\n{error}");

    private void RefreshLegend()
    {
        Legend.Clear();
        if (!Aggregate)
        {
            ShowLegend = false;
            return;
        }
        var accounts = _widget.ShownAccounts;
        if (accounts.Count == 0)
        {
            ShowLegend = false;
            return;
        }
        // The swatch keeps its colour by the real label; the text is the codename while streamer mode hides it (slice 7, CP 3).
        foreach (var label in accounts)
            Legend.Add(new LegendItem(StreamerMode.Mask.Name(CodenameCategory.Account, label), AccountColors.BrushFor(label, accounts)));
        ShowLegend = true;
    }

    /// <summary>
    /// What the meter history says when it draws nothing (WF32, WF35): signed out, how to start it; signed in with nothing
    /// recorded yet, that it fills in. Empty while the chart has rows. A screen reader reads it; the chart is a drawing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText))]
    private string _emptyText = "";

    /// <summary>The empty-state line shows in place of the chart.</summary>
    public bool HasEmptyText => EmptyText.Length > 0;

    /// <summary>The empty-state line for a chart with or without rows.</summary>
    internal static string EmptyLine(bool signedIn, bool hasRows) => hasRows ? "" : signedIn ? SettingsNames.MeterHistoryEmpty : SettingsNames.MeterHistorySignedOut;

    /// <summary>The chart rows (see <see cref="Rows"/>), and the empty-state line that goes with them.</summary>
    public IReadOnlyList<ChartRow> BuildRows()
    {
        var rows = Rows();
        EmptyText = EmptyLine(_widget.ActiveAccount is not null || _widget.IsDemo, rows.Count > 0);
        return rows;
    }

    /// <summary>
    /// Assemble the chart rows from the shared history store. Single-account mode
    /// loads one window per tier (accent line); all-accounts mode overlays one
    /// per-account series per tier (registry order → stable colors). A tier with
    /// no data in the window is dropped (parity with the Python <c>if agg:</c>).
    /// </summary>
    private IReadOnlyList<ChartRow> Rows()
    {
        // Demo mode charts nothing: a sample label could match a real account's history file.
        if (_widget.IsDemo)
            return [];
        int days = WeekMode ? 7 : 30;
        var accounts = _widget.ShownAccounts;
        string? selected = SelectedOption?.Account;
        var rows = new List<ChartRow>();

        foreach (var key in TierModel.EffectiveOrder)
        {
            var series = new List<ChartSeries>();
            if (selected is null)
            {
                var agg = _widget.History.AggregateWindow(key, days);
                foreach (var label in accounts) // registry order keeps colors stable
                {
                    if (agg.TryGetValue(label, out var pts) && pts.Count > 0)
                        series.Add(new ChartSeries(AccountColors.ColorFor(label, accounts), pts));
                }
            }
            else
            {
                var pts = _widget.History.LoadWindow(key, days, selected);
                if (pts.Count > 0)
                    series.Add(new ChartSeries(_widget.Palette.Accent, pts));
            }

            if (series.Count > 0)
                rows.Add(new ChartRow(TierModel.Label(key), series));
        }

        return rows;
    }

    [RelayCommand]
    private void SetWeek()
    {
        if (!WeekMode) WeekMode = true;
    }

    [RelayCommand]
    private void SetMonth()
    {
        if (WeekMode) WeekMode = false;
    }

    [RelayCommand(CanExecute = nameof(NotDemo))]
    private void ExportCsv()
    {
        string? account = SelectedOption?.Account;
        string defaultName = ExportFileName(account, DateTime.Today);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = SettingsNames.ExportMeterHistoryTitle,
            FileName = defaultName,
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
        };
        bool? ok = _owner is not null ? dlg.ShowDialog(_owner) : dlg.ShowDialog();
        if (ok != true)
            return;

        try
        {
            var result = CsvExport.Build(_widget.History, _widget.AccountStore, account);
            File.WriteAllText(dlg.FileName, result.Text);
            Sounds.PlaySaveConfirmation();
            Info(ExportedMessage(result.RowCount, dlg.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Sounds.PlayError();
            Info(ExportFailedMessage(dlg.FileName, e.Message));
        }
    }

    /// <summary>Tests read the notices here instead of showing them.</summary>
    internal Action<string>? InfoHook { get; set; }

    private void Info(string message)
    {
        if (InfoHook is not null) { InfoHook(message); return; }
        ThemedDialog.Show(_owner, SettingsNames.MeterHistoryDialogTitle, message, MessageBoxButton.OK, ThemedDialogKind.Info);
    }
}
