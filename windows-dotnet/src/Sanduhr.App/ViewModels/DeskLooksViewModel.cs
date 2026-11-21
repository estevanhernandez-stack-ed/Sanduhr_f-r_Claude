using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>What a shown piece still needs ("Weather needs a place."), as a link to that piece's Settings page.</summary>
public sealed record DeskLookNeedLink(string Text, string Link);

/// <summary>
/// One ready-made look in the gallery (5.0 spec 5, section 6). The card is a button: pressing it applies the look. Everything it
/// shows is fixed copy (a look's name, its blurb, the fit's notes), so streamer mode has nothing to mask here; the wallpaper on the
/// card is the user's own desktop, already on their screen.
/// </summary>
public sealed partial class DeskLookCardViewModel : ObservableObject
{
    private readonly Action<string> _apply;

    internal DeskLookCardViewModel(string id, Action<string> apply)
    {
        Id = id;
        _apply = apply;
    }

    public string Id { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private string _name = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private string _blurb = "";
    /// <summary>The look as fitted to this PC: what the card draws and what a press applies.</summary>
    [ObservableProperty] private DeskDesign? _fitted;
    /// <summary>What the fit changed, at most two lines.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private IReadOnlyList<string> _notes = [];
    /// <summary>What the look's shown pieces still need; empty when the fit's notes took both lines.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private IReadOnlyList<DeskLookNeedLink> _needs = [];
    /// <summary>The Desk is this look now, untouched.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private bool _current;
    /// <summary>The user's wallpaper, small; null when there is no picture to show.</summary>
    [ObservableProperty] private ImageSource? _wallpaper;
    /// <summary>The wallpaper's first dominant color (six hex digits) for a card without a picture; "" for the theme's background.</summary>
    [ObservableProperty] private string _backdropHex = "";
    /// <summary>The wallpaper's overall brightness, which automatic ink follows on the card; null when unknown.</summary>
    [ObservableProperty] private double? _brightness;

    /// <summary>What a screen reader says for the card: "Marquee look. Clock up top, neon glow. Uses its plate on your wallpaper. On your Desk."</summary>
    public string AccessibleName => string.Join(" ",
        new[] { $"{Name} look.", Blurb }.Concat(Notes).Concat(Needs.Select(n => n.Text)).Concat(Current ? [SettingsNames.OnYourDesk + "."] : [])
            .Where(s => s.Length > 0));

    [RelayCommand]
    private void Apply() => _apply(Id);

    /// <summary>The page for a piece that has nothing to draw yet.</summary>
    internal static string LinkFor(string piece) => piece switch
    {
        "weather" => SettingsIndex.PageId(SettingsPage.Weather),
        "launcher" => SettingsIndex.PageId(SettingsPage.Launcher) + "#" + SettingsIndex.LauncherButtons,
        "watchers" => SettingsIndex.PageId(SettingsPage.Watchers) + "#" + SettingsIndex.Watchers,
        _ => SettingsIndex.PageId(SettingsPage.Desk) + "#" + SettingsIndex.DeskPieces,
    };

    internal void Update(DeskLookCardData data, ImageSource? wallpaper, string backdropHex, double? brightness)
    {
        Name = data.Look.Name;
        Blurb = data.Look.Blurb;
        Fitted = data.Fit.Design;
        Notes = [.. data.Fit.Notes.Take(2)];
        // At most two notes, fit notes first: what the pieces need is one more line, when there is room for it.
        Needs = Notes.Count < 2 ? [.. data.Needs.Select(n => new DeskLookNeedLink(n.Line, LinkFor(n.Piece)))] : [];
        Current = data.Current;
        Wallpaper = wallpaper;
        BackdropHex = backdropHex;
        Brightness = brightness;
    }
}

/// <summary>
/// Settings, Desk, Looks (5.0 spec 5, section 6): a card per ready-made look, each applied in one click, the same Put my Desk back as
/// Designs and the tray, and a line that says what the last press did. The cards are kept and updated in place, so the keyboard stays
/// on the one that was pressed.
/// </summary>
public sealed partial class DeskLooksViewModel : ObservableObject
{
    private readonly Func<DeskDesignService?> _service;

    /// <param name="service">The app's design service (null where there is none: the gallery is empty).</param>
    internal DeskLooksViewModel(Func<DeskDesignService?> service)
    {
        _service = service;
        Reload();
    }

    public ObservableCollection<DeskLookCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool HasStatus => Status.Length > 0;

    public bool CanPutBack => _service()?.CanPutBack == true;

    /// <summary>Fits every look again and reads the wallpaper again (after a press, when the page shows, when the wallpaper, the fonts
    /// or the saved designs change).</summary>
    public void Reload()
    {
        if (_service() is not { } service)
        {
            Cards.Clear();
        }
        else
        {
            DeskLooksSnapshot snapshot;
            ImageSource? picture;
            try
            {
                snapshot = service.LooksSnapshot();
                picture = service.LookPicture();
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return;   // the gallery keeps what it showed
            }
            string backdrop = InkHex.Normalize(snapshot.Wallpaper?.Dominant.FirstOrDefault()?.Hex) ?? "";
            double? brightness = snapshot.Wallpaper?.Brightness;
            var keep = Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Cards.Count; i++)
            {
                var data = snapshot.Cards[i];
                if (!keep.TryGetValue(data.Look.Id, out var card)) card = new DeskLookCardViewModel(data.Look.Id, Apply);
                card.Update(data, picture, backdrop, brightness);
                if (i >= Cards.Count) Cards.Add(card);
                else if (!ReferenceEquals(Cards[i], card)) Cards[i] = card;
            }
            while (Cards.Count > snapshot.Cards.Count) Cards.RemoveAt(Cards.Count - 1);
        }
        OnPropertyChanged(nameof(CanPutBack));
        PutBackCommand.NotifyCanExecuteChanged();
    }

    private void Say(DesignActionResult result)
    {
        Status = "";   // the same line twice is one change to a live region: clear it first
        Status = result.Message;   // streamer: notname - fixed copy and a built-in look's name, never the user's text
        Reload();
    }

    private void Apply(string id)
    {
        if (_service() is not { } s) return;
        Say(s.ApplyLook(id));
    }

    [RelayCommand(CanExecute = nameof(CanPutBack))]
    private void PutBack()
    {
        if (_service() is not { } s) return;
        Say(s.PutBack());
    }
}
