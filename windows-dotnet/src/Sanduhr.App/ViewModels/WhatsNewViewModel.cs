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

    public WhatsNewCardItem(WhatsNewCard card, ImageSource? trayImage)
    {
        Card = card;
        TrayImage = trayImage;
    }

    public string Title => Card.Title;
    public string Body => Card.Body;
    public string Glyph => Card.Glyph;

    public bool IsGlyph => Card.Preview == WhatsNewPreview.None;
    public bool IsTray => Card.Preview == WhatsNewPreview.Tray;
    public bool IsSparkline => Card.Preview == WhatsNewPreview.Sparkline;
    public bool IsShortcut => Card.Preview == WhatsNewPreview.Shortcut;

    /// <summary>A steady series: the horizon sparkline draws it as a level line.</summary>
    public IReadOnlyList<int> SparkValues => SteadyValues;

    public string AutomationName => $"{Card.Title}. {Card.Body}";
    public string ShowMeName => $"Show me: {Card.Title}";

    public string ShowMeToolTip => Card.Destination == SettingsPage.Widget
        ? "Shows the widget"
        : $"Opens Settings, {PageName(Card.Destination)}";

    private static string PageName(SettingsPage page) => page switch
    {
        SettingsPage.ClaudeUsage => "Claude Usage",
        SettingsPage.Publish => "Publish usage",
        _ => page.ToString(),
    };
}

/// <summary>What the What's New window binds to: the header, the cards, and the persisted "don't show after updates" flag.</summary>
public sealed class WhatsNewViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private bool _hideAfterUpdates;

    public string Header { get; }
    public IReadOnlyList<WhatsNewCardItem> Items { get; }
    public bool IsEmpty => Items.Count == 0;

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
    private static ImageSource? RenderTray()
    {
        try
        {
            using var icon = TrayIconRenderer.Render(12);
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
