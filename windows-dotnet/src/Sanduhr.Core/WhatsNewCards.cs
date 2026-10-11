namespace Sanduhr.Core;

/// <summary>
/// The release highlights What's New shows, newest release first. A later release adds its own
/// list and joins it in <see cref="Table"/>. Voice: short, plain, second person, sentence case,
/// periods, at most two sentences and 200 characters. Glyphs are Segoe Fluent Icons codepoints, checked against SegoeIcons.ttf and the Microsoft Learn list.
/// </summary>
public static class WhatsNewCards
{
    public static IReadOnlyList<WhatsNewCard> Table { get; } = [.. Release350()];

    private static WhatsNewCard[] Release350() =>
    [
        WhatsNewCard.Of("3.5.0", "shortcut-keys", "Open Settings from any app",
            "Turn on the shortcut in Settings, General, and Ctrl+Alt+S opens Settings from anywhere. It's off until you ask for it.",
            "\uE765", // KeyboardClassic
            WhatsNewPreview.Shortcut, SettingsPage.General),
        WhatsNewCard.Of("3.5.0", "tray-shows", "Pick what the tray shows",
            "The tray icon can show Session, Weekly, Whichever is higher, Rotate, or the Highest of any limit. Choose from the tray menu or Settings.",
            "\uE7C4", // TaskView, the fallback behind the tray preview
            WhatsNewPreview.Tray, SettingsPage.General),
        WhatsNewCard.Of("3.5.0", "limits-come-and-go", "Limits that come and go",
            "A promo or special limit can hide until it resets, then come back on its own. Each limit can also stop warning you.",
            "\uE7BA", // Warning
            WhatsNewPreview.None, SettingsPage.Widget),
        WhatsNewCard.Of("3.5.0", "every-folder", "Every Claude Code folder counts",
            "Every .claude-* folder with a projects folder now counts toward your burn and model usage. A new one is listed, off, until you say yes.",
            "\uE8B7", // Folder
            WhatsNewPreview.None, SettingsPage.ClaudeUsage),
        WhatsNewCard.Of("3.5.0", "steady-sparklines", "Steady meters read steady",
            "When a meter holds still, the horizon sparkline draws a level line instead of a wobble.",
            "\uE9D2", // AreaChart
            WhatsNewPreview.Sparkline, SettingsPage.Widget),
        WhatsNewCard.Of("3.5.0", "whats-new", "What's new, after every update",
            "You'll see what changed after each update. It stays in the tray menu and Settings, so you can reopen it any time.",
            "\uE789", // Megaphone
            WhatsNewPreview.None, SettingsPage.About),
    ];
}
