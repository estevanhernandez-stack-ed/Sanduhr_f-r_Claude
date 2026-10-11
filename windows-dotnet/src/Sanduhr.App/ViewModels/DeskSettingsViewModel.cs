using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>A place a piece can sit: one of the eight anchors, or Hidden (null).</summary>
public sealed record DeskPlaceOption(DeskAnchor? Anchor, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One row of "Where each piece sits": a piece, its place and its size (60% to 160%).</summary>
public sealed partial class DeskPieceRow : ObservableObject
{
    private readonly DeskSettingsViewModel _owner;
    private bool _loading;

    public DeskPieceRow(DeskSettingsViewModel owner, string widget, string name)
    {
        _owner = owner;
        Widget = widget;
        Name = name;
    }

    public string Widget { get; }
    public string Name { get; }

    [ObservableProperty] private DeskPlaceOption _place = DeskSettingsViewModel.Hidden;
    [ObservableProperty] private double _size = 1;

    /// <summary>"120%", for the size picker.</summary>
    public string SizeText => $"{Math.Round(Size * 100)}%";

    public bool IsPlaced => Place.Anchor is not null;

    internal void Load(DeskPlacement? p)
    {
        _loading = true;
        Place = p is null ? DeskSettingsViewModel.Hidden : DeskSettingsViewModel.PlaceFor(p.Anchor);
        Size = p?.Scale ?? 1;
        _loading = false;
    }

    partial void OnPlaceChanged(DeskPlaceOption value)
    {
        OnPropertyChanged(nameof(IsPlaced));
        if (!_loading) _owner.Edit(a => a.Place(Widget, value.Anchor));
    }

    partial void OnSizeChanged(double value)
    {
        OnPropertyChanged(nameof(SizeText));
        if (!_loading) _owner.Edit(a => a.SetScale(Widget, value));
    }
}

/// <summary>
/// Settings, Desk (spec 5.1): the switch (off by default), where each piece sits and its size, the
/// font, the clock size and the margins. Every change saves at once and tells the running Desk
/// through <see cref="Changed"/>, so the desktop follows as you choose.
/// </summary>
public sealed partial class DeskSettingsViewModel : ObservableObject
{
    public static readonly DeskPlaceOption Hidden = new(null, "Hidden");

    /// <summary>The eight places in the Mac's order, then Hidden.</summary>
    public static IReadOnlyList<DeskPlaceOption> PlaceOptions { get; } =
        [.. DeskAnchors.All.Select(a => new DeskPlaceOption(a, DeskAnchors.Name(a))), Hidden];

    public static DeskPlaceOption PlaceFor(DeskAnchor a) => PlaceOptions.First(o => o.Anchor == a);

    /// <summary>The size steps, 60% to 160%.</summary>
    public static IReadOnlyList<double> SizeSteps => DeskArrangement.ScaleSteps;

    /// <summary>The font picker's first entry: the system font (saved as "").</summary>
    public const string SystemFont = "System font (Segoe UI)";

    private readonly SettingsStore _store;
    private DeskSettings _saved;
    private bool _loading;

    public DeskSettingsViewModel(SettingsStore store, IReadOnlyList<string> fontFamilies)
    {
        _store = store;
        _saved = store.LoadDesk();
        FontChoices = [.. fontFamilies, SystemFont];
        Pieces =
        [
            new DeskPieceRow(this, "clock", SettingsNames.DeskClock),
            new DeskPieceRow(this, "claude", SettingsNames.DeskClaudeLine),
            new DeskPieceRow(this, "meters", SettingsNames.DeskClaudeBars),
        ];
        Load();
    }

    /// <summary>Called after every saved change (App re-applies the Desk).</summary>
    public Action? Changed { get; set; }

    /// <summary>The layout changed (the map redraws).</summary>
    public event Action? LayoutChanged;

    public ObservableCollection<DeskPieceRow> Pieces { get; }
    public IReadOnlyList<string> FontChoices { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _font = BundledFonts.Family;
    [ObservableProperty] private double _timeSize = DeskSettings.DefaultTimeSize;
    [ObservableProperty] private double _marginLeft;
    [ObservableProperty] private double _marginRight;
    [ObservableProperty] private double _marginTop;
    [ObservableProperty] private double _marginBottom;

    /// <summary>The saved layout string (the map reads it).</summary>
    public string Layout => _saved.Layout;

    /// <summary>The saved settings (the map and previews read them).</summary>
    public DeskSettings Settings => _saved;

    private void Load()
    {
        _loading = true;
        Enabled = _saved.Enabled;
        Font = _saved.Font switch
        {
            null => BundledFonts.Family,
            "" => SystemFont,
            var f => f,
        };
        TimeSize = _saved.TimeSize;
        MarginLeft = _saved.Margins.Left;
        MarginRight = _saved.Margins.Right;
        MarginTop = _saved.Margins.Top;
        MarginBottom = _saved.Margins.Bottom;
        var a = new DeskArrangement(_saved.Layout);
        foreach (var row in Pieces) row.Load(a.Placement(row.Widget));
        _loading = false;
    }

    /// <summary>Change the layout through <see cref="DeskArrangement"/> and save.</summary>
    internal void Edit(Action<DeskArrangement> change)
    {
        var a = new DeskArrangement(_saved.Layout);
        change(a);
        Save(_saved with { Layout = a.Layout });
        _loading = true;
        foreach (var row in Pieces) row.Load(a.Placement(row.Widget));
        _loading = false;
        LayoutChanged?.Invoke();
    }

    partial void OnEnabledChanged(bool value) { if (!_loading) Save(_saved with { Enabled = value }); }

    partial void OnFontChanged(string value)
    {
        if (_loading) return;
        // EsteFont Pro is the default: saved as absent, so a later default change follows it.
        string? saved = value == SystemFont ? "" : value == BundledFonts.Family ? null : value;
        Save(_saved with { Font = saved });
    }

    partial void OnTimeSizeChanged(double value) { if (!_loading) Save(_saved with { TimeSize = value }); }
    partial void OnMarginLeftChanged(double value) => SaveMargins();
    partial void OnMarginRightChanged(double value) => SaveMargins();
    partial void OnMarginTopChanged(double value) => SaveMargins();
    partial void OnMarginBottomChanged(double value) => SaveMargins();

    private void SaveMargins()
    {
        if (_loading) return;
        Save(_saved with { Margins = new DeskMarginSettings(MarginLeft, MarginRight, MarginTop, MarginBottom) });
        LayoutChanged?.Invoke();
    }

    private void Save(DeskSettings next)
    {
        _saved = next.Normalized();
        _store.SaveDesk(_saved);
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(Settings));
        Changed?.Invoke();
    }
}
