namespace Sanduhr.Core;

/// <summary>
/// The release highlights What's New shows, newest release first. A later release adds its own
/// list and joins it in <see cref="Table"/>. Voice: short, plain, second person, sentence case,
/// periods, at most two sentences and 200 characters. Glyphs are Segoe Fluent Icons codepoints, checked against SegoeIcons.ttf and the Microsoft Learn list.
/// </summary>
public static class WhatsNewCards
{
    public static IReadOnlyList<WhatsNewCard> Table { get; } = [.. Release390(), .. Release380(), .. Release371(), .. Release360(), .. Release350()];

    // 3.9.0: the Store goes 3.8.0 to 3.9.0. One card; the cap of eight is not in play.
    private static WhatsNewCard[] Release390() =>
    [
        WhatsNewCard.Of("3.9.0", "ci-runners", "Your CI runners on the Desk",
            "Show my CI runners' jobs, in Settings, Desk, puts a card on your Desk for each job a runner on this PC runs. Sanduhr reads the runners' logs through Docker's local pipe: no account, no network.",
            "", // CommandPrompt
            WhatsNewPreview.None, SettingsPage.Desk),
    ];

    // 3.8.0: the Store goes 3.7.1 to 3.8.0 (3.7.2 was a GitHub-only patch with no cards). Seven cards
    // fill the cap of eight.
    private static WhatsNewCard[] Release380() =>
    [
        WhatsNewCard.Of("3.8.0", "widgets", "Widgets for your Desk",
            "Settings, Widgets installs packs from the 626 Labs catalog: sprites, themes, palettes and effects. Packs are data only, signed, and checked again before they install.",
            "", // Puzzle
            WhatsNewPreview.None, SettingsPage.Widgets),
        WhatsNewCard.Of("3.8.0", "companion", "A companion on your Desk",
            "A pixel hourglass sits on your Desk, grows with the days you code and reacts to your pace. Show it and let it roam in Settings, Widgets.",
            "", // Emoji
            WhatsNewPreview.None, SettingsPage.Widgets),
        WhatsNewCard.Of("3.8.0", "claude-widgets", "Claude can make you a widget",
            "Ask Claude Code for a pixel sprite, a palette or effects. It previews on your Desk and installs only when you choose Apply.",
            "", // Robot
            WhatsNewPreview.None, SettingsPage.Widgets),
        WhatsNewCard.Of("3.8.0", "restart-to-update", "Restart to update",
            "When an update is ready, Sanduhr asks, then closes, installs it and opens on the new version. Nothing happens until you click.",
            "", // UpdateRestore
            WhatsNewPreview.None, SettingsPage.About),
        WhatsNewCard.Of("3.8.0", "desk-fold", "Fold your Desk",
            "Claude Code can slant a stack of Desk pieces up to 20 degrees, so two stacks read as the faces of a fold. Ask for it in a Desk design.",
            "", // Rotate
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.8.0", "weather-nickname", "Call the weather anything",
            "Show it as, in Settings, Weather, puts a name like Funky Town on the weather piece instead of the city. The name never leaves your PC.",
            "", // Rename
            WhatsNewPreview.None, SettingsPage.Weather),
        WhatsNewCard.Of("3.8.0", "livelier-bars", "Livelier bars",
            "The bars beside now playing ease, fall and wear peak caps, and Gradient bars spreads your ink across them. They still read a level only, never the audio.",
            "", // Equalizer
            WhatsNewPreview.None, SettingsPage.NowPlaying),
        WhatsNewCard.Of("3.8.0", "meter-styles", "Meters that fit",
            "The Claude line, your limits and the system readings come as bars, slim lines, rings or ticks, with pace marks on every one. Set them in Settings, Desk.",
            "", // SpeedMedium
            WhatsNewPreview.None, SettingsPage.Desk),
    ];

    private static WhatsNewCard[] Release371() =>
    [
        WhatsNewCard.Of("3.7.1", "desk-design", "Claude can design your Desk",
            "Claude Code can read your Desk and wallpaper and propose a whole look. It waits on a bar on your desktop for Apply or Dismiss, unless you let it change the Desk directly.",
            "", // Edit
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.7.1", "launcher", "A Launcher on the Desk",
            "Up to eight buttons on your Desk open Start-menu apps, web apps or https links when you click. Add them in Settings, Desk.",
            "", // AllApps
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.7.1", "per-monitor", "A Desk piece on each monitor",
            "Put each piece on all monitors, the main one or a monitor you pick. Arrange Desk can drag a piece from one monitor to another.",
            "", // OpenInNewWindow
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.7.1", "song-looks", "A look for every song",
            "Turn on Style what's playing and each song gets its own gradient and letter style. Claude can suggest looks, and nothing is saved until you say so.",
            "", // Font
            WhatsNewPreview.None, SettingsPage.NowPlaying),
        WhatsNewCard.Of("3.7.1", "oled-care", "Care for OLED screens",
            "The Desk drifts a few pixels every 3 minutes, dims after 5 idle minutes, and can hold its ink off pure white. Turn it on in Settings, Desk.",
            "", // Brightness
            WhatsNewPreview.None, SettingsPage.Desk),
        WhatsNewCard.Of("3.7.1", "saved-designs", "Put my Desk back",
            "Sanduhr keeps your last ten Desk looks, and you can pin the ones you love. Put my Desk back, in Settings and the tray menu, returns to the look before a change.",
            "", // Undo
            WhatsNewPreview.None, SettingsPage.Desk),
    ];

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
