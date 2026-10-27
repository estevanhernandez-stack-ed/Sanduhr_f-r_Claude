using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Reset Sanduhr… (spec 4.7): five checked parts, a Reset button, then progress, per-step results and
/// Quit. Themed like <see cref="ThemedDialog"/> (DynamicResource brushes, rounded glass, draggable).
/// The window runs nothing itself: <see cref="ResetService"/> does, and
/// <see cref="QuitRequested"/> is how the orchestrator ends the app once the results are read.
/// Nothing shown here is a path or an account label. No part of the copy promises a byte-for-byte
/// restore of anything in Claude Code.
/// </summary>
internal partial class ResetWindow : Window
{
    private readonly ResetService? _service;
    private bool _ready;
    private bool _running;
    private bool _finished;
    private bool _quitSent;

    /// <summary>Called once, when the user picks Quit (or closes the window) after the reset ran.</summary>
    public Action? QuitRequested { get; set; }

    private readonly ResetFolderListViewModel? _folders;

    public ResetWindow(ResetService? service = null, ResetFolderListViewModel? folders = null)
    {
        InitializeComponent();
        _service = service;
        _folders = folders;
        FolderList.ItemsSource = folders?.Rows;
        if (folders is not null)
            folders.Changed += UpdateChoiceState;   // the list lives as long as this window; detached on Closed
        _ready = true;
        UpdateChoiceState();
        Closing += (_, e) =>
        {
            if (_running) e.Cancel = true;   // never close on a reset in flight
        };
        Closed += (_, _) =>
        {
            if (_folders is not null)
                _folders.Changed -= UpdateChoiceState;
            if (_finished) SendQuit();
        };
    }

    /// <summary>Opens the dialog over <paramref name="owner"/> (or the screen). Returns when it closes.</summary>
    public static void ShowFor(Window? owner, ResetService service, ResetFolderListViewModel folders, Action quit)
    {
        var dlg = new ResetWindow(service, folders) { QuitRequested = quit };
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }

    /// <summary>The five names, as the check boxes carry them.</summary>
    public static string PartLabel(ResetPart part) => part switch
    {
        ResetPart.ClaudeCodeEntries => SettingsNames.ResetClaudeCodeEntries,
        ResetPart.Accounts => "Accounts and sign-in keys",
        ResetPart.History => SettingsNames.ResetMeterHistory,
        ResetPart.Themes => "Your themes and widgets",
        ResetPart.Settings => "Settings",
        _ => "",
    };

    public ResetChoices Choices => new(
        ClaudeCodeEntries: ClaudeCodeBox.IsChecked == true,
        Accounts: AccountsBox.IsChecked == true,
        History: HistoryBox.IsChecked == true,
        Themes: ThemesBox.IsChecked == true,
        Settings: SettingsBox.IsChecked == true);

    /// <summary>The parts as the service runs them: the Claude Code part only while it would change something (a ticked folder, or
    /// no list at all, when Sanduhr's leftover copies still go).</summary>
    internal ResetChoices EffectiveChoices => Choices with
    {
        ClaudeCodeEntries = ClaudeCodeBox.IsChecked == true && (_folders?.UndoesAny ?? true),
    };

    /// <summary>The real paths of the folders left as they are: unticked rows, never what a row shows (decision 14).</summary>
    internal IReadOnlyList<string> KeepFolders => ClaudeCodeBox.IsChecked == true ? [.. _folders?.Kept.Select(f => f.Path) ?? []] : [];   // streamer: key - the real paths Reset keeps, never drawn

    /// <summary>The choose state: five boxes, Cancel and Reset.</summary>
    public void ShowChoose()
    {
        _running = false;
        ChoosePanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        ResetButton.Visibility = Visibility.Visible;
        QuitButton.Visibility = Visibility.Collapsed;
        UpdateChoiceState();
    }

    /// <summary>The running state: a progress line, nothing to press.</summary>
    public void ShowRunning(string progress)
    {
        _running = true;
        ChoosePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ResultsList.ItemsSource = null;
        AfterText.Visibility = Visibility.Collapsed;
        ProgressText.Text = progress;
        CancelButton.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Collapsed;
        QuitButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>The results state: one row per step that ran, then Quit.</summary>
    public void ShowResults(IReadOnlyList<ResetStepResult> results)
    {
        _running = false;
        _finished = true;
        bool allOk = results.All(r => r.Ok);
        ChoosePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ProgressText.Text = allOk
            ? "Reset is done."
            : "Reset finished, and some things are still there.";
        var done = (Brush)FindResource("Sanduhr.Brush.Accent");
        var failed = (Brush)FindResource("Sanduhr.Brush.PaceMarker");
        ResultsList.ItemsSource = results.Select(r => new ResultRow(
            PartLabel(r.Part),
            r.Ok ? "Done" : "Not finished",
            r.Ok ? done : failed,
            r.Ok ? "" : FailureDetail(r))).ToList();
        AfterText.Text = "Backups of Claude Code files stay in the backups folder. Quit to finish.";
        AfterText.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Collapsed;
        QuitButton.Visibility = Visibility.Visible;
        QuitButton.Focus();
    }

    private static string FailureDetail(ResetStepResult r)
    {
        string what = r.Failures.Count == 0 ? "something" : string.Join(", ", r.Failures);
        string tail = r.Part == ResetPart.ClaudeCodeEntries
            ? " Sanduhr keeps its own copies of those, so Settings can still remove them after the next launch."
            : " Close anything using it and remove it by hand.";
        return $"Couldn't remove {what}.{tail}";
    }

    private sealed record ResultRow(string Name, string Status, Brush StatusBrush, string Detail)
    {
        public Visibility DetailVisibility => Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>A result row reads as "Meters mod, Done", never as a record dump.</summary>
        public override string ToString() => $"{Name}, {Status}";
    }

    private void Choice_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateChoiceState();
    }

    private void UpdateChoiceState()
    {
        bool part = ClaudeCodeBox.IsChecked == true;
        var lines = _folders?.LinesFor(part, AccountsBox.IsChecked == true, SettingsBox.IsChecked == true) ?? default;
        FolderList.IsEnabled = part;   // owner taste 7: dim, keep the ticks
        FolderScroller.Visibility = Vis(_folders is { Rows.Count: > 0 });
        NoFoldersText.Visibility = Vis(lines.NoFolders);
        UnreadableText.Visibility = Vis(lines.Unreadable);
        // The lines that come and go are live regions: a screen reader hears one appear (LiveRegion.Shown flips with it).
        Line(ClaudeCodeWarning, lines.Kept);
        Line(PartOffText, lines.KeptPartOff);
        Line(KeptSignInText, lines.KeptSignIn);
        Line(KeptSettingsText, lines.KeptSettings);
        Line(KeptMcpSettingsText, lines.KeptMcpSettings);
        Line(KeptAddedText, lines.KeptAdded);
        ResetButton.IsEnabled = ResetPlan.Steps(EffectiveChoices).Count > 0;
    }

    private static void Line(TextBlock line, bool on)
    {
        line.Visibility = Vis(on);
        LiveRegion.SetShown(line, on);
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        var choices = EffectiveChoices;
        IReadOnlyCollection<ResetFolder> keep = ClaudeCodeBox.IsChecked == true ? _folders?.Kept ?? [] : [];   // the real folders and their kinds
        ShowRunning("Resetting…");
        IReadOnlyList<ResetStepResult> results;
        try
        {
            results = await _service.RunAsync(choices, keep,
                part => ShowRunning($"Resetting: {PartLabel(part)}"));
        }
        catch (Exception)
        {
            results = [new ResetStepResult(ResetPlan.Steps(choices).FirstOrDefault(), false, ["the reset itself"])];
        }
        ShowResults(results);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        SendQuit();
        Close();
    }

    private void SendQuit()
    {
        if (_quitSent) return;
        _quitSent = true;
        QuitRequested?.Invoke();
    }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase)
        {
            try { DragMove(); } catch (InvalidOperationException) { /* released mid-press */ }
        }
    }
}
