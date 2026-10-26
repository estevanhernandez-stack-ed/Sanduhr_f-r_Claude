using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One What's New card as the window shows it: the Core card plus what the template needs.</summary>
public sealed class WhatsNewCardItem
{
    private static readonly IReadOnlyList<int> SteadyValues = [96, 96, 96, 97, 96, 96];

    public WhatsNewCard Card { get; }
    public ImageSource? TrayImage { get; }

    /// <summary>The Meters preview's rows (the user's own meters, or demo mode's samples).</summary>
    public IReadOnlyList<MeterPreviewRow> Meters { get; }

    /// <summary>The account chip preview's text.</summary>
    public string ChipText { get; }

    /// <summary>A tour card's in-place setting (the tray readout), empty otherwise.</summary>
    public IReadOnlyList<TrayChoiceItem> Choices { get; }

    /// <summary>The button's title: "Show me", or "Sign in" on the signed-out tour's first card.</summary>
    public string ShowMeTitle { get; }

    public WhatsNewCardItem(WhatsNewCard card, ImageSource? trayImage,
        IReadOnlyList<MeterPreviewRow>? meters = null, string chipText = "",
        IReadOnlyList<TrayChoiceItem>? choices = null, string showMeTitle = "Show me", bool isSample = false)
    {
        Card = card;
        TrayImage = trayImage;
        Meters = meters ?? [];
        ChipText = chipText;
        Choices = choices ?? [];
        ShowMeTitle = showMeTitle;
        IsSample = isSample;
    }

    /// <summary>The preview draws demo mode's made-up numbers, and says so.</summary>
    public bool IsSample { get; }

    public string Title => Card.Title;
    public string Body => Card.Body;
    public string Glyph => Card.Glyph;

    public bool IsGlyph => Card.Preview == WhatsNewPreview.None;
    public bool IsTray => Card.Preview == WhatsNewPreview.Tray;
    public bool IsSparkline => Card.Preview == WhatsNewPreview.Sparkline;
    public bool IsShortcut => Card.Preview == WhatsNewPreview.Shortcut;
    public bool IsMeters => Card.Preview == WhatsNewPreview.Meters && Meters.Count > 0;
    /// <summary>The Meters preview with nothing to show yet: a line instead of empty bars.</summary>
    public bool IsMetersEmpty => Card.Preview == WhatsNewPreview.Meters && Meters.Count == 0;
    public bool IsAccountChip => Card.Preview == WhatsNewPreview.AccountChip;
    public bool HasChoices => Choices.Count > 0;

    /// <summary>A steady series: the horizon sparkline draws it as a level line.</summary>
    public IReadOnlyList<int> SparkValues => SteadyValues;

    public string AutomationName => $"{Card.Title}. {Card.Body}";

    /// <summary>The card's list item reads by its title ("Watchers on the Desk"), never as the type behind it.</summary>
    public override string ToString() => Card.Title;
    public string ShowMeName => $"{ShowMeTitle}: {Card.Title}";

    public string ShowMeToolTip => SettingsIndex.ShowsTheWidget(new SettingsLink(Card.Destination, Card.Anchor))
        ? "Shows the widget"
        : $"Opens Settings, {SettingsNames.PageTitle(SettingsIndex.Resolve(Card.Destination, Card.Anchor).Page)}";
}

/// <summary>What the What's New window binds to: the header, the cards, and the persisted "don't show after updates" flag.</summary>
public sealed class WhatsNewViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private bool _hideAfterUpdates;

    public string Header { get; }
    public IReadOnlyList<WhatsNewCardItem> Items { get; }
    public bool IsEmpty => Items.Count == 0;

    // The window's mode: What's New (cards, Don't show What's New after updates, Done).
    public string WindowTitle => "What's New in Sanduhr";
    public string HeaderName => Header;
    public bool IsTour => false;
    public bool IsWhatsNew => true;
    public int Columns => 2;

    public WhatsNewViewModel(IReadOnlyList<WhatsNewCard> cards, string header, SettingsStore store)
    {
        _store = store;
        Header = header;
        _hideAfterUpdates = store.LoadWhatsNewHideAfterUpdates();
        var tray = cards.Any(c => c.Preview == WhatsNewPreview.Tray) ? RenderTray() : null;
        Items = cards.Select(c => new WhatsNewCardItem(c, tray)).ToList();
    }

    public bool HideAfterUpdates
    {
        get => _hideAfterUpdates;
        set
        {
            if (!SetProperty(ref _hideAfterUpdates, value)) return;
            _store.SaveWhatsNewHideAfterUpdates(value);
        }
    }

    /// <summary>The tray glyph at 12% as a WPF image, for the Tray preview.</summary>
    internal static ImageSource? RenderTray() => TrayIconRenderer.RenderImage(12);
}
