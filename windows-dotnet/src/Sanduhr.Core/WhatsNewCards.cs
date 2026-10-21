namespace Sanduhr.Core;

/// <summary>
/// The release highlights What's New shows, newest release first. A later release adds its own
/// list and joins it in <see cref="Table"/>. Voice: short, plain, second person, sentence case,
/// periods, at most two sentences and 200 characters. Glyphs are Segoe Fluent Icons codepoints, checked against SegoeIcons.ttf and the Microsoft Learn list.
/// </summary>
public static class WhatsNewCards
{
    public static IReadOnlyList<WhatsNewCard> Table { get; } = [.. Release360(), .. Release350()];

    private static WhatsNewCard[] Release360() =>
    [
        WhatsNewCard.Of("3.6.0", "now-playing-steps-aside", "Now playing steps aside",
            "It shows only while something plays and fades 5 seconds after you pause, with the pieces around it closing the gap. The bars follow your music, not Windows' dings.",
            "", // MusicNote
            WhatsNewPreview.None, SettingsPage.NowPlaying),
        WhatsNewCard.Of("3.6.0", "watchers", "Watchers on the Desk",
            "Claude Code can show a live card on your Desk for work it's watching, and say when it needs you. Turn it on in Settings, Desk, then place Watchers.",
            "", // Stopwatch
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.6.0", "suggested-messages", "Claude can suggest Desk messages",
            "Ask Claude Code for a line or two. Suggestions wait in Settings, Message until you add them.",
            "", // Message
            WhatsNewPreview.None, SettingsPage.DeskMessage),
        WhatsNewCard.Of("3.6.0", "desk-plate", "A plate behind the text",
            "A soft plate behind the Desk's pieces keeps them readable over a busy desktop. Turn it on in Settings, Desk.",
            "", // Color
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.6.0", "arrange-hide", "Hide pieces while arranging",
            "Arrange Desk can hide a piece and bring it back from the Hidden strip. Cancel puts everything back.",
            "", // RedEye
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.6.0", "statusline-label", "The statusline names its folder",
            "In a folder like .claude-personal, Sanduhr's statusline starts with personal. Set SANDUHR_LABEL in that folder's settings to choose your own.",
            "", // CommandPrompt
            WhatsNewPreview.None, SettingsPage.ClaudeCode),
    ];

    private static WhatsNewCard[] Release350() =>
    [
        WhatsNewCard.Of("3.5.0", "desk", "The Desk",
            "A clock, your meters, messages, now playing and weather, drawn on your desktop behind your windows. It's off until you turn it on, and Just the Desk works without a Claude account.",
            "", // TVMonitor
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.5.0", "settings-redesigned", "Settings, redesigned",
            "Pages sit in groups down a sidebar, and the search box jumps to any setting by name. Try \"hotkey\" or \"csv\".",
            "", // Setting
            WhatsNewPreview.None, SettingsPage.General),
        WhatsNewCard.Of("3.5.0", "claude-code-page", "A page for Claude Code",
            "Set up the statusline, the MCP server, the meters mod and the glow for each Claude Code folder. Nothing is installed until you agree, and Remove undoes it exactly.",
            "", // CommandPrompt
            WhatsNewPreview.None, SettingsPage.ClaudeCode),
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
