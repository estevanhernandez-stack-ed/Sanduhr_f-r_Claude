using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Settings, Usage, Sources (agent layer): one row per source with on, primary, color and alerts. Claude's switch goes through
/// the widget's own <c>SetUsageOn</c>; Codex's is <c>setup.usage.codex</c>, and App starts or stops the Codex service when it changes
/// (<see cref="OnOffChanged"/>). Color, primary and alerts save to <c>sources</c>. Turning the primary off moves it to
/// <see cref="SourceSettings.PrimaryFor"/>.</summary>
/// <summary>What a Sources switch does to the widget window.</summary>
public enum WidgetVisibility { Leave, Show, Hide }

public sealed partial class SourcesViewModel : ObservableObject
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly SettingsStore _store;
    private readonly Func<bool> _claudeOn;
    private readonly Action<bool> _setClaudeOn;
    private readonly Func<IReadOnlyList<string>> _codexHomes;
    private readonly Func<bool> _antigravityInstalled;
    private readonly Action<string> _agentVaultOn;

    internal SourcesViewModel(SettingsStore store, Func<bool> claudeOn, Action<bool> setClaudeOn, Func<IReadOnlyList<string>> codexHomes,
        Func<bool> antigravityInstalled, Action<string> agentVaultOn)
    {
        _antigravityInstalled = antigravityInstalled;
        _agentVaultOn = agentVaultOn;
        _store = store;
        _claudeOn = claudeOn;
        _setClaudeOn = setClaudeOn;
        _codexHomes = codexHomes;
        foreach (var source in AgentSources.All) Rows.Add(new SourceRowViewModel(this, source));
        foreach (var s in AgentSources.ActivityOnly) ActivityRows.Add(new ActivityRowViewModel(this, s));
        Reload();
    }

    public ObservableCollection<SourceRowViewModel> Rows { get; } = [];

    /// <summary>Activity-only sources (Antigravity): a switch and a caption, never a primary, color or limits row.</summary>
    public ObservableCollection<ActivityRowViewModel> ActivityRows { get; } = [];

    internal void SetActivityOn(string source, bool on)
    {
        _store.SaveActivity(source, on);
        if (on) _agentVaultOn(source);   // clears a purge tombstone, so ingest resumes
    }

    /// <summary>A source was switched on or off and saved: App starts or stops the Codex service and redraws the widget.</summary>
    public Action<bool, bool>? OnOffChanged { get; set; }

    /// <summary>What the widget window should do when a switch took usage from <paramref name="wasAny"/> to <paramref name="nowAny"/>: show on
    /// none-on to some-on, hide on some-on to none-on, and nothing otherwise (the window is never raised or brought back by a switch
    /// between two sources, and never takes focus from Settings).</summary>
    public static WidgetVisibility VisibilityFor(bool wasAny, bool nowAny) =>
        !wasAny && nowAny ? WidgetVisibility.Show : wasAny && !nowAny ? WidgetVisibility.Hide : WidgetVisibility.Leave;

    /// <summary>Why the last switch was refused ("Keep one on, or turn on the Desk first."); empty otherwise.</summary>
    [ObservableProperty] private string _message = "";
    public bool HasMessage => Message.Length > 0;
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>Primary or a color changed and saved: App redraws the widget's sections, bars and tray.</summary>
    public Action? DisplayChanged { get; set; }

    /// <summary>Each burn source's color as saved in Sources (Antigravity's default), for the Local burn page's strip.</summary>
    public Func<string, System.Windows.Media.Color> ColorsForStrip()
    {
        var sources = _store.LoadSources();
        return id => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(sources.ColorOf(id));
    }

    /// <summary>Reads setup, the sources settings and whether Codex is installed again (Settings does this on each visit).</summary>
    public void Reload() => Reload(clearMessage: true);

    private void Reload(bool clearMessage)
    {
        if (clearMessage && Message.Length > 0) Message = "";
        var setup = _store.LoadSetup();
        var sources = _store.LoadSources();
        bool codexInstalled = _codexHomes().Count > 0;
        var ticked = Ticked(setup);
        var primary = sources.PrimaryFor(ticked);
        foreach (var row in Rows)
        {
            bool installed = row.Source != AgentSources.Codex || codexInstalled;
            row.Load(installed, ticked.Usage[row.Source], row.Source == primary, sources.ColorOf(row.Source), sources.AlertsOn(row.Source));
        }
        foreach (var row in ActivityRows) row.Load(_antigravityInstalled(), _store.LoadActivity().GetValueOrDefault(row.Source));
    }

    private SetupSettings Ticked(SetupSettings? setup) =>
        new(true, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [AgentSources.Claude] = _claudeOn(),
            [AgentSources.Codex] = setup?.CodexUsage ?? false,
        });

    /// <summary>Whether anything would still be shown with <paramref name="source"/> as <paramref name="on"/>: a usage source on, or the
    /// Desk (the same rule Set up Sanduhr enforces with "Pick at least one").</summary>
    private bool AnythingLeft(string source, bool on)
    {
        var setup = _store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null);
        var ticked = Ticked(setup);
        bool claude = source == AgentSources.Claude ? on : ticked.ClaudeUsage;
        bool codex = source == AgentSources.Codex ? on : ticked.CodexUsage;
        var choices = SetupChoices.Of(setup, _store.LoadDrawers().Enabled, _store.LoadDesk().Enabled) with { ClaudeUsage = claude, CodexUsage = codex };
        return choices.AnyTicked;
    }

    internal void SetOn(string source, bool on)
    {
        if (!on && !AnythingLeft(source, false))
        {
            // The line is set once per refusal, after the switch has snapped back (a change inside the binding's own update is not
            // reliably shown). A repeat of the same refusal blanks it first, so the live region has a change to announce.
            if (Message.Length > 0) Message = "";
            _ui.BeginInvoke(() =>
            {
                Reload(clearMessage: false);
                Message = SettingsNames.SourcesKeepOne;
            });
            return;
        }
        var before = Ticked(_store.LoadSetup());
        bool wasAny = before.ClaudeUsage || before.CodexUsage;
        try
        {
            if (source == AgentSources.Claude)
                _setClaudeOn(on);
            else
            {
                var setup = _store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null);
                _store.SaveSetup(setup.WithCodex(on));
                if (on) _agentVaultOn(AgentSources.Codex);
            }
            // The primary follows what is on: a source turned off while primary hands it to the first ticked one.
            var sources = _store.LoadSources();
            var primary = sources.PrimaryFor(Ticked(_store.LoadSetup()));
            if (primary != sources.Primary) _store.SaveSources(sources with { Primary = primary });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr.Sources] not saved: {ex.Message}");   // streamer: notname - written to the log file, never drawn
        }
        Reload();
        var after = Ticked(_store.LoadSetup());
        OnOffChanged?.Invoke(wasAny, after.ClaudeUsage || after.CodexUsage);
    }

    internal void SetPrimary(string source)
    {
        if (!Ticked(_store.LoadSetup()).Usage[source]) { Reload(); return; }   // only a source that is on can lead
        Save(_store.LoadSources() with { Primary = source });
        Reload();
        DisplayChanged?.Invoke();
    }

    /// <summary>True when the color was saved; false when it was refused (the saved one stays).</summary>
    internal bool SetColor(string source, string? text)
    {
        text = text?.Trim();
        if (!SourceSettings.IsColor(text)) return false;
        var sources = _store.LoadSources();
        var colors = new Dictionary<string, string>(sources.Colors) { [source] = text! };
        Save(sources with { Colors = colors });
        DisplayChanged?.Invoke();
        return true;
    }

    internal void ResetColor(string source)
    {
        var sources = _store.LoadSources();
        var colors = new Dictionary<string, string>(sources.Colors);
        colors.Remove(source);
        Save(sources with { Colors = colors });
        DisplayChanged?.Invoke();
    }

    internal void SetAlerts(string source, bool on)
    {
        var sources = _store.LoadSources();
        Save(sources with { Alerts = new Dictionary<string, bool>(sources.Alerts) { [source] = on } });
    }

    private void Save(SourceSettings value)
    {
        try { _store.SaveSources(value); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr.Sources] not saved: {ex.Message}");   // streamer: notname - written to the log file, never drawn
        }
    }
}

/// <summary>An activity-only source on the Sources page (Antigravity): one switch, disabled when its CLI folder is absent.</summary>
public sealed partial class ActivityRowViewModel(SourcesViewModel owner, string source) : ObservableObject
{
    public string Source { get; } = source;
    public string Name => AgentSources.Name(Source);

    /// <summary>The row reads as its source's name to a screen reader (an ItemsControl names its items by this).</summary>
    public override string ToString() => Name;
    public string Caption => Installed ? SettingsNames.AntigravityActivityCaption : SettingsNames.NothingFoundYet;
    [ObservableProperty] private bool _installed;
    [ObservableProperty] private bool _on;
    private bool _loading;

    partial void OnInstalledChanged(bool value) => OnPropertyChanged(nameof(Caption));
    partial void OnOnChanged(bool value) { if (!_loading) owner.SetActivityOn(Source, value); }

    internal void Load(bool installed, bool on) { _loading = true; Installed = installed; On = on; _loading = false; }
}

/// <summary>One source on the Sources page. Tab order is the order of its controls: On, Primary, Color, Reset, Alerts.</summary>
public sealed partial class SourceRowViewModel : ObservableObject
{
    private readonly SourcesViewModel _owner;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private bool _loading;
    private bool _on, _isPrimary, _alertsOn, _installed = true;
    private string _color;

    internal SourceRowViewModel(SourcesViewModel owner, string source)
    {
        _owner = owner;
        Source = source;
        Name = AgentSources.Name(source);
        ColorName = SettingsNames.SourcesColorName(Name);
        OnName = SettingsNames.SourcesTrackName(Name);
        PrimaryName = SettingsNames.SourcesPrimaryName(Name);
        ResetName = SettingsNames.SourcesResetName(Name);
        AlertsName = SettingsNames.SourcesAlertsName(Name);
        _color = AgentSources.DefaultColor(source);
        ResetColorCommand = new RelayCommand(OnResetColor);
    }

    public string Source { get; }
    public string Name { get; }

    /// <summary>The row reads as its source's name to a screen reader (an ItemsControl names its items by this).</summary>
    public override string ToString() => Name;

    /// <summary>The color box's spoken name, "Codex color".</summary>
    public string ColorName { get; }

    /// <summary>The other controls' spoken names, each with the source in it: "Track Codex", "Codex primary", "Reset Codex color", "Codex alerts".</summary>
    public string OnName { get; }
    public string PrimaryName { get; }
    public string ResetName { get; }
    public string AlertsName { get; }

    public ICommand ResetColor => ResetColorCommand;
    private RelayCommand ResetColorCommand { get; }

    public bool Installed
    {
        get => _installed;
        private set
        {
            if (_installed == value) return;
            _installed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotInstalledText));
            OnPropertyChanged(nameof(HasNotInstalledText));
            OnPropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>"Codex isn't installed on this PC." for a source whose program is not here; null otherwise.</summary>
    public string? NotInstalledText => Installed ? null : SettingsNames.SourcesCodexNotInstalled;
    public bool HasNotInstalledText => NotInstalledText is not null;

    /// <summary>A source that is not installed is greyed; one left on from before can still be turned off.</summary>
    public bool CanToggle => Installed || On;

    public bool On
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(CanBePrimary));
            if (!_loading) _owner.SetOn(Source, value);
        }
    }

    /// <summary>Primary is chosen among the sources that are on.</summary>
    public bool CanBePrimary => On;

    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            if (_isPrimary == value) return;
            _isPrimary = value;
            OnPropertyChanged();
            if (!_loading && value) _owner.SetPrimary(Source);
        }
    }

    /// <summary>The color box: <c>#RRGGBB</c>. Text that is not a color is refused: the saved color stays and <see cref="ColorMessage"/> says so.</summary>
    public string Color
    {
        get => _color;
        set
        {
            if (_loading)
            {
                _color = value;
                OnPropertyChanged();
                return;
            }
            if (_owner.SetColor(Source, value))
            {
                _color = value.Trim();
                ColorMessage = "";
            }
            else if (ColorMessage == SettingsNames.SourcesColorRefused)
            {
                ColorMessage = "";   // the same refusal again: blank now, say it again after, so the live region announces it
                _ui.BeginInvoke(() => ColorMessage = SettingsNames.SourcesColorRefused);
            }
            else
                ColorMessage = SettingsNames.SourcesColorRefused;
            OnPropertyChanged(nameof(Swatch));
            // The box still shows what was typed (refused, or untrimmed). Say the saved color after the commit has unwound: a
            // notification from inside the binding's own update is not reliably shown.
            _ui.BeginInvoke(() => OnPropertyChanged(nameof(Color)));
        }
    }

    [ObservableProperty] private string _colorMessage = "";
    public bool HasColorMessage => ColorMessage.Length > 0;
    partial void OnColorMessageChanged(string value) => OnPropertyChanged(nameof(HasColorMessage));

    /// <summary>The color as a brush for the swatch beside the box.</summary>
    public System.Windows.Media.Brush Swatch => CombinedBarViewModel.BrushFor(_color, false);

    public bool AlertsOn
    {
        get => _alertsOn;
        set
        {
            if (_alertsOn == value) return;
            _alertsOn = value;
            OnPropertyChanged();
            if (!_loading) _owner.SetAlerts(Source, value);
        }
    }

    private void OnResetColor()
    {
        _owner.ResetColor(Source);
        _color = AgentSources.DefaultColor(Source);
        ColorMessage = "";
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Swatch));
    }

    internal void Load(bool installed, bool on, bool primary, string color, bool alertsOn)
    {
        _loading = true;
        try
        {
            Installed = installed;
            On = on;
            IsPrimary = primary;
            Color = color;
            AlertsOn = alertsOn;
            ColorMessage = "";
        }
        finally { _loading = false; }
        OnPropertyChanged(nameof(Swatch));
    }
}
