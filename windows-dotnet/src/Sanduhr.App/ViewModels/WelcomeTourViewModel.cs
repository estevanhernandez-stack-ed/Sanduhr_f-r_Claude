using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of the Meters preview: a limit's name, its percent and the bar's fill (0 to 1).</summary>
public sealed record MeterPreviewRow(string Label, int Percent, Color BarColor)
{
    public string PercentText => $"{Percent}%";
    public double Fraction => Math.Clamp(Percent / 100.0, 0, 1);
    public string AutomationName => $"{Label} {Percent}%";

    /// <summary>A meter row reads as "Session (5hr) 12%", never as a record dump.</summary>
    public override string ToString() => AutomationName;
}

/// <summary>
/// One option of a tour card's in-place choice (what the tray shows). Choosing writes the real
/// setting at once, through the widget, so Back shows it as made and the tray follows.
/// </summary>
public sealed class TrayChoiceItem : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action _changed;

    public TrayMode Mode { get; }
    public string Title => TrayReadout.Title(Mode);

    public override string ToString() => Title;

    public TrayChoiceItem(WidgetViewModel widget, TrayMode mode, Action changed)
    {
        _widget = widget;
        Mode = mode;
        _changed = changed;
    }

    public bool IsChecked
    {
        get => _widget.TrayMode == Mode;
        set
        {
            if (!value || _widget.TrayMode == Mode)
                return;
            _widget.TrayMode = Mode;
            _changed();
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>
/// The welcome tour as the What's New window shows it in step mode: the steps, which one is up,
/// and each step's cards with live previews drawn from what the widget already has (no fetch).
/// </summary>
public sealed class WelcomeTourViewModel : ObservableObject
{
    private readonly IReadOnlyList<IReadOnlyList<TourCard>> _steps;
    private readonly WidgetViewModel _widget;
    private readonly ImageSource? _trayImage;
    private int _index;
    private IReadOnlyList<WhatsNewCardItem> _items = [];
    private IReadOnlyList<TrayChoiceItem> _choices = [];

    public WelcomeTourViewModel(IReadOnlyList<IReadOnlyList<TourCard>> steps, WidgetViewModel widget)
    {
        _steps = steps;
        _widget = widget;
        _trayImage = steps.SelectMany(s => s).Any(c => c.Card.Preview == WhatsNewPreview.Tray)
            ? WhatsNewViewModel.RenderTray()
            : null;
        Build();
    }

    public string WindowTitle => "Welcome to Sanduhr";
    public bool IsTour => true;
    public bool IsWhatsNew => false;
    public string AgainLine => WelcomeTour.AgainLine;
    public string SkipTitle => WelcomeTour.SkipTitle;

    public int Count => _steps.Count;
    public int Index => _index;
    public bool IsFirst => _index == 0;
    public bool CanGoBack => !IsFirst;
    public bool IsLast => _index >= Count - 1;
    public string NextTitle => IsLast ? "Finish" : "Next";

    /// <summary>"1 of 4".</summary>
    public string Header => WelcomeTour.StepText(_index, Count);
    /// <summary>What a screen reader says for the header: "Step 1 of 4".</summary>
    public string HeaderName => $"Step {_index + 1} of {Count}";

    public IReadOnlyList<WhatsNewCardItem> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    /// <summary>One card fills the row; a pair sits side by side.</summary>
    public int Columns => Math.Clamp(_items.Count, 1, 2);

    /// <summary>Next; false on the last step (the caller finishes).</summary>
    public bool Next()
    {
        if (IsLast)
            return false;
        _index++;
        Build();
        return true;
    }

    /// <summary>Jump to a step (clamped), for a tour refilled while open.</summary>
    public void GoTo(int index)
    {
        _index = Math.Clamp(index, 0, Math.Max(0, Count - 1));
        Build();
    }

    public void Back()
    {
        if (_index == 0)
            return;
        _index--;
        Build();
    }

    /// <summary>Rebuild the current step's cards so previews show the widget as it is now.</summary>
    private void Build()
    {
        var cards = _index < _steps.Count ? _steps[_index] : [];
        _items = cards.Select(Item).ToList();
        foreach (var name in new[]
                 {
                     nameof(Items), nameof(IsEmpty), nameof(Columns), nameof(Index), nameof(IsFirst),
                     nameof(CanGoBack), nameof(IsLast), nameof(NextTitle), nameof(Header), nameof(HeaderName),
                 })
            OnPropertyChanged(name);
    }

    private WhatsNewCardItem Item(TourCard tour)
    {
        IReadOnlyList<TrayChoiceItem> choices = [];
        if (tour.Choice == TourChoice.TrayMode)
        {
            choices = TrayReadout.All.Select(m => new TrayChoiceItem(_widget, m, RefreshChoices)).ToList();
            _choices = choices;
        }
        var meters = tour.Card.Preview == WhatsNewPreview.Meters ? MeterRows() : null;
        var chip = tour.Card.Preview == WhatsNewPreview.AccountChip ? ChipText() : "";
        // Demo mode's samples are labelled on the preview that draws them.
        var sample = _widget.IsDemo && tour.Card.Preview is WhatsNewPreview.Meters or WhatsNewPreview.AccountChip;
        return new WhatsNewCardItem(tour.Card, _trayImage, meters, chip, choices, tour.ShowMeTitle, sample);
    }

    private void RefreshChoices()
    {
        foreach (var c in _choices)
            c.Refresh();
    }

    /// <summary>The widget's visible meters, at most three, as they read now.</summary>
    private IReadOnlyList<MeterPreviewRow> MeterRows() =>
        _widget.Tiers.Take(3)
            .Select(t => new MeterPreviewRow(t.Label, (int)Math.Round(t.Utilization), t.BarColor))
            .ToList();

    /// <summary>The chip's text: the demo sample, the active account, or a plain placeholder.</summary>
    private string ChipText() =>
        _widget.DemoAccount is { } demo ? DemoMode.ChipText(demo)
        : _widget.ActiveAccount is { } active ? active
        : "No account yet";
}
