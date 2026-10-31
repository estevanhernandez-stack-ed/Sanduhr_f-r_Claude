namespace Sanduhr.Core;

/// <summary>Every string the setup screen shows (setup spec, sections 2 to 4).</summary>
public static class SetupCopy
{
    public const string Title = "Set up Sanduhr";
    public const string Intro = "Pick what you want. You can change this any time from the tray or Settings.";
    public const string DesktopTitle = "A nicer desktop";
    public const string DesktopBody = "A clock, a message and the weather on your desktop.";
    public const string DrawersTitle = "Tidy my files";
    public const string DrawersBody = "Sort your desktop files into drawers you can fold away. Nothing moves until you say so.";
    public const string UsageTitle = "Track my AI usage";
    public const string UsageBody = "See how much of your plan you've used.";
    public const string ClaudeSource = "Claude";
    public const string ClaudeTrademark = SettingsNames.TrademarkNotice;
    public const string DesignWithChat = "Design it with an AI chat";
    public const string Continue = "Continue";
    public const string NotNow = "Not now";
    public const string Cancel = "Cancel";
    public const string MenuItem = "Set up Sanduhr…";
    public const string PickOne = "Pick at least one, or Sanduhr has nothing to show.";
    public const string DesktopOffLine = "The clock, message and weather hide. Your layout is kept.";
    public const string UsageOffLine = "Your account stays saved. Remove it in Settings, Accounts.";
    public const string AddOnsStayLine = "The Claude Code add-ons stay installed. Remove them on the Claude Code page.";

    public static string DrawersOffLine(int files) =>
        files == 1 ? "1 drawer file goes back to your Desktop." : $"{files} drawer files go back to your Desktop.";

    /// <summary>For the copy tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Title, Intro, DesktopTitle, DesktopBody, DrawersTitle, DrawersBody, UsageTitle, UsageBody, ClaudeSource, ClaudeTrademark,
        DesignWithChat, Continue, NotNow, Cancel, MenuItem, PickOne, DesktopOffLine, UsageOffLine, AddOnsStayLine, DrawersOffLine(2),
    ];
}

/// <summary>What a change says before Continue applies it (spec 4): one line per tick taken away.</summary>
public static class SetupChange
{
    public static IReadOnlyList<string> Lines(SetupChoices before, SetupChoices after, int drawerFiles, bool addonsInstalled)
    {
        var lines = new List<string>();
        if (before.Desktop && !after.Desktop) lines.Add(SetupCopy.DesktopOffLine);
        if (before.Drawers && !after.Drawers && drawerFiles > 0) lines.Add(SetupCopy.DrawersOffLine(drawerFiles));
        if (before.ClaudeUsage && !after.ClaudeUsage)
        {
            lines.Add(SetupCopy.UsageOffLine);
            if (addonsInstalled) lines.Add(SetupCopy.AddOnsStayLine);
        }
        return lines;
    }

    /// <summary>Why Continue is disabled, or null when it is not.</summary>
    public static string? Blocked(SetupChoices after) => after.AnyTicked ? null : SetupCopy.PickOne;
}
