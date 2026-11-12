using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Settings, Usage, Sources (agent layer): one row per source with on, primary, color and alerts. Claude's switch goes through
/// the widget's own <c>SetUsageOn</c>; Codex's is <c>setup.usage.codex</c>, and App starts or stops the Codex service when it changes
/// (<see cref="OnOffChanged"/>). Color, primary and alerts save to <c>sources</c>. Turning the primary off moves it to
/// <see cref="SourceSettings.PrimaryFor"/>.</summary>
public sealed class SourcesViewModel
{
    private readonly SettingsStore _store;
    private readonly Func<bool> _claudeOn;
    private readonly Action<bool> _setClaudeOn;
    private readonly Func<IReadOnlyList<string>> _codexHomes;

    internal SourcesViewModel(SettingsStore store, Func<bool> claudeOn, Action<bool> setClaudeOn, Func<IReadOnlyList<string>> codexHomes)
    {
        _store = store;
        _claudeOn = claudeOn;
        _setClaudeOn = setClaudeOn;
        _codexHomes = codexHomes;
        foreach (var source in AgentSources.All) Rows.Add(new SourceRowViewModel(this, source));
        Reload();
    }

    public ObservableCollection<SourceRowViewModel> Rows { get; } = [];

    /// <summary>A source was switched on or off and saved: App starts or stops the Codex service and redraws the widget.</summary>
    public Action? OnOffChanged { get; set; }

    /// <summary>Primary or a color changed and saved: App redraws the widget's sections, bars and tray.</summary>
    public Action? DisplayChanged { get; set; }

    /// <summary>Reads setup, the sources settings and whether Codex is installed again (Settings does this on each visit).</summary>
    public void Reload()
    {
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
    }

    private SetupSettings Ticked(SetupSettings? setup) =>
        new(true, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [AgentSources.Claude] = _claudeOn(),
            [AgentSources.Codex] = setup?.CodexUsage ?? false,
        });

    internal void SetOn(string source, bool on)
    {
        try
        {
            if (source == AgentSources.Claude)
                _setClaudeOn(on);
            else
            {
                var setup = _store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null);
                _store.SaveSetup(setup.WithCodex(on));
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
        if (source == AgentSources.Codex) OnOffChanged?.Invoke(); else DisplayChanged?.Invoke();
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

/// <summary>One source on the Sources page. Tab order is the order of its controls: On, Primary, Color, Reset, Alerts.</summary>
public sealed partial class SourceRowViewModel : ObservableObject
{
    private readonly SourcesViewModel _owner;
    private bool _loading;
    private bool _on, _isPrimary, _alertsOn, _installed = true;
    private string _color;

    internal SourceRowViewModel(SourcesViewModel owner, string source)
    {
        _owner = owner;
        Source = source;
        Name = AgentSources.Name(source);
        ColorName = SettingsNames.SourcesColorName(Name);
        _color = AgentSources.DefaultColor(source);
        ResetColorCommand = new RelayCommand(OnResetColor);
    }

    public string Source { get; }
    public string Name { get; }

    /// <summary>The row reads as its source's name to a screen reader (an ItemsControl names its items by this).</summary>
    public override string ToString() => Name;

    /// <summary>The color box's spoken name, "Codex color".</summary>
    public string ColorName { get; }

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
            else
                ColorMessage = SettingsNames.SourcesColorRefused;
            OnPropertyChanged();   // a refused entry reads as the old color again
            OnPropertyChanged(nameof(Swatch));
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
