using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>A Graph style choice: the style and the words the strip's tooltip uses for it.</summary>
public sealed record GraphStyleOption(SparklineStyle Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One row of Settings, Widget, Limit order: a limit the widget shows, with Move up and Move down where they can act.</summary>
public sealed partial class LimitOrderRowViewModel : ObservableObject
{
    private readonly WidgetPageViewModel _page;

    public string Key { get; }
    public string Label { get; }

    /// <summary>Not the first row; the button is collapsed otherwise (no control that can't act).</summary>
    [ObservableProperty] private bool _canMoveUp;

    /// <summary>Not the last row.</summary>
    [ObservableProperty] private bool _canMoveDown;

    public LimitOrderRowViewModel(WidgetPageViewModel page, string key)
    {
        _page = page;
        Key = key;
        Label = TierModel.Label(key);
    }

    /// <summary>"Move up, Weekly - Opus": the visible label first (WCAG 2.5.3), then the limit.</summary>
    public string MoveUpName => SettingsNames.MoveLimitUp(Label);
    public string MoveDownName => SettingsNames.MoveLimitDown(Label);

    [RelayCommand]
    private void MoveUp() => _page.Move(Key, -1);

    [RelayCommand]
    private void MoveDown() => _page.Move(Key, +1);

    public override string ToString() => Label;
}

/// <summary>
/// Settings, Widget (Settings v2, slice 5; WF15, WF24): the widget's own options, which had no Settings home. Every control writes
/// through the widget, which saves its key and raises the change, so the strip, the widget menu and this page never disagree.
/// </summary>
public sealed partial class WidgetPageViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private bool _loading;

    public static IReadOnlyList<GraphStyleOption> GraphStyles { get; } =
    [
        new(SparklineStyle.Classic, SettingsNames.GraphStyleClassic),
        new(SparklineStyle.Horizon, SettingsNames.GraphStyleHorizon),
    ];

    /// <summary>Limit order's rows, synced in place so a moved row keeps its buttons and its keyboard focus.</summary>
    public ObservableCollection<LimitOrderRowViewModel> Limits { get; } = new();

    public WidgetPageViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _deepWorkMinutes = widget.LoadFocusDuration().ToString(CultureInfo.InvariantCulture);
        _widget.PropertyChanged += OnWidgetChanged;
        _widget.LimitsChanged += ReloadLimits;
        ReloadLimits();
    }

    /// <summary>The page's title, from the names table.</summary>
    public string PageTitle => SettingsNames.PageTitle(SettingsPage.Widget);

    /// <summary>Compact mode: the widget's in-memory state (Ctrl+D); no key (decision 16).</summary>
    public bool IsCompact => _widget.IsCompact;
    public ICommand ToggleCompactCommand => _widget.ToggleCompactCommand;

    /// <summary>Graph style ("sparkline_style").</summary>
    public GraphStyleOption SelectedGraphStyle
    {
        get => GraphStyles.FirstOrDefault(o => o.Value == _widget.SparklineStyle) ?? GraphStyles[0];
        set
        {
            if (value is not null)
                _widget.SetSparklineStyle(value.Value);
        }
    }

    /// <summary>Keep the widget on top ("pinned").</summary>
    public bool KeepOnTop
    {
        get => _widget.Pinned;
        set => _widget.SetPinned(value);
    }

    /// <summary>Usage is off (setup spec, section 4): the page says so and offers Set up Sanduhr.</summary>
    public bool ShowUsageOff => _widget.ShowUsageOff;

    /// <summary>Connected by App: opens Set up Sanduhr.</summary>
    public Action? SetupRequested { get; set; }

    [RelayCommand]
    private void OpenSetup() => SetupRequested?.Invoke();

    /// <summary>Deep Work length in minutes ("focus_mode_duration"), 1 to 120 like the focus timer's own stepper.</summary>
    [ObservableProperty] private string _deepWorkMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeepWorkHint))]
    private string _deepWorkHint = "";

    public bool HasDeepWorkHint => DeepWorkHint.Length > 0;

    partial void OnDeepWorkMinutesChanged(string value)
    {
        if (_loading)
            return;
        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= 1 and <= 120)
        {
            _widget.SaveFocusDuration(minutes);
            DeepWorkHint = "";
        }
        else
        {
            DeepWorkHint = SettingsNames.DeepWorkRange;
        }
    }

    /// <summary>Reads Deep Work length again (P22): the focus timer's own stepper writes the key behind this page's back. Called each
    /// time the page is selected.</summary>
    public void Reload()
    {
        _loading = true;
        try { DeepWorkMinutes = _widget.LoadFocusDuration().ToString(CultureInfo.InvariantCulture); }
        finally { _loading = false; }
        DeepWorkHint = "";
    }

    /// <summary>What Limit order says while it has no row: signed out, or signed in with nothing fetched yet.</summary>
    [ObservableProperty] private string _limitsEmptyText = "";

    /// <summary>True while Limit order has no row; drives the empty line's live region (P4).</summary>
    [ObservableProperty] private bool _hasNoLimits = true;

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetViewModel.IsCompact): OnPropertyChanged(nameof(IsCompact)); break;
            case nameof(WidgetViewModel.SparklineStyle): OnPropertyChanged(nameof(SelectedGraphStyle)); break;
            case nameof(WidgetViewModel.Pinned): OnPropertyChanged(nameof(KeepOnTop)); break;
            case nameof(WidgetViewModel.UsageOn): OnPropertyChanged(nameof(ShowUsageOff)); break;
        }
    }

    private void ReloadLimits()
    {
        var keys = _widget.ShownLimitOrder;
        for (int i = 0; i < keys.Count; i++)
        {
            int at = -1;
            for (int j = i; j < Limits.Count; j++)
            {
                if (Limits[j].Key == keys[i]) { at = j; break; }
            }
            if (at < 0)
                Limits.Insert(i, new LimitOrderRowViewModel(this, keys[i]));
            else if (at != i)
                Limits.Move(at, i);
        }
        while (Limits.Count > keys.Count)
            Limits.RemoveAt(Limits.Count - 1);
        for (int i = 0; i < Limits.Count; i++)
        {
            Limits[i].CanMoveUp = i > 0;
            Limits[i].CanMoveDown = i < Limits.Count - 1;
        }
        LimitsEmptyText = Limits.Count > 0 ? "" : _widget.LimitsEmptyLine();
        HasNoLimits = Limits.Count == 0;
    }

    /// <summary>Moves a limit one place in the widget's order; nothing when it can't move.</summary>
    internal void Move(string key, int delta)
    {
        if (LimitSettings.Move(_widget.ShownLimitOrder, key, delta) is { } order)
            _widget.SetLimitOrder(order);
    }

    /// <summary>Stop following the widget (Settings closed): it outlives every Settings window.</summary>
    public void Detach()
    {
        _widget.PropertyChanged -= OnWidgetChanged;
        _widget.LimitsChanged -= ReloadLimits;
    }
}
