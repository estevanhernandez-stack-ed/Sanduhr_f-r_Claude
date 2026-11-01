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
    public const string SaveFailed = "Your choices could not be saved.";
    public const string DesktopOffLine = "The clock, message and weather hide. Your layout is kept.";
    public const string UsageOffLine = "Your account stays saved. Remove it in Settings, Accounts.";
    public const string AddOnsStayLine = "The Claude Code add-ons stay installed. Remove them on the Claude Code page.";

    /// <summary>Names for the two lists on the setup screen, for a screen reader.</summary>
    public const string WhatChanges = "What changes";
    public const string WhatHappened = "What happened";

    /// <summary>On the review bar of a Desk design (from the MCP or pasted) while A nicer desktop is off: Apply still saves it.</summary>
    public const string DesignDesktopOffNote = "A nicer desktop is off, so Apply saves this for when you turn it on.";

    public const string ArrangeNeedsDesktop = "Turn on A nicer desktop in Set up Sanduhr to arrange the Desk.";

    // ---- Design it with an AI chat (spec 3): the window's copy. It says "any AI chat" and names no product, Claude included. ----

    public const string ChatIntro = "Copy a prompt into any AI chat. It asks what you like, then gives you a block to paste below.";
    public const string ChatCopyPrompt = "Copy prompt";
    public const string ChatPasteName = "The AI chat's answer";
    public const string ChatPastePlaceholder = "{\"desk\": {...}, \"drawers\": {\"rules\": [...]}}";
    public const string ChatPreview = "Preview on my Desk";
    public const string ChatUseRules = "Use these drawer rules";
    public const string ChatUseDrawerSettings = "Use these drawer settings";
    public const string ChatClose = "Close";
    public const string ChatFindingsName = "What to fix in the paste";
    public const string ChatReady = "Ready.";
    public const string ChatPromptCopied = "Prompt copied. Paste it into any AI chat, answer its questions, then paste its answer here.";
    public const string ChatDeskShown = "Showing it on your Desk. Apply or Dismiss it there.";
    public const string ChatRulesShown = "Showing what the rules would sort on your Desk. Nothing moves until you Apply.";
    public const string ChatNoDeskService = "The Desk is not running, so there is nothing to preview on.";

    public static string ChatFixCount(int n) => n == 1 ? "1 thing to fix before this can be used." : $"{n} things to fix before this can be used.";

    public static string ChatManyBlocks(int n) => $"The paste held {n} blocks. The first good one is used.";

    /// <summary>What Use these drawer rules will do, said before it does it: the rules replace the current ones, drawers come on if they
    /// were off, and nothing moves before Apply. <paramref name="drawerNames"/> are already masked for streamer mode.</summary>
    public static string ChatRulesLine(int rules, IReadOnlyList<string> drawerNames)
    {
        string head = rules == 1 ? "1 drawer rule" : $"{rules} drawer rules";
        string into = drawerNames.Count == 0 ? "" : " into " + string.Join(", ", drawerNames);
        string them = rules == 1 ? "it" : "them";
        return $"{head}{into}. Using {them} replaces your drawer rules, turns drawers and your Desk on if they are off, then shows what {them} would sort. Nothing moves until you Apply.";
    }

    /// <summary>The window's status once the drawer look is saved and no Tidy was asked for.</summary>
    public const string ChatDrawerSettingsDone = "Drawer look saved.";

    /// <summary>The window's status once a Tidy ran with the look: the result and its Undo are on the drawers bar.</summary>
    public const string ChatDrawerSettingsArranged = "Drawer settings applied. Undo is on your Desk.";

    /// <summary>The drawers bar line after the chat's settings ran a Tidy: what the look did, then what Tidy did.</summary>
    public static string ChatDrawerSettingsResult(bool lookSaved, string tidyLine) =>
        lookSaved ? ChatDrawerSettingsDone + " " + tidyLine : tidyLine;

    /// <summary>One line saying what a pasted drawers look would do, for the window under the paste. <paramref name="corner"/> and
    /// <paramref name="flow"/> are spoken words (never the short codes), and null where the paste left the part to the user's own Tidy.</summary>
    public static string ChatDrawerSettingsLine(string? look, int? opacity, string? corner, string? flow)
    {
        var parts = new List<string>();
        if (look is not null) parts.Add(opacity is { } o ? $"Look: {look}, {o}%." : $"Look: {look}.");
        else if (opacity is { } o2) parts.Add($"Opacity: {o2}%.");
        if (corner is not null || flow is not null)
        {
            string from = corner is null ? "your usual corner" : $"the {corner}";
            string how = flow is null ? "" : $", in a {flow}";
            parts.Add($"Lines drawers up from {from}{how}.");
        }
        parts.Add("Using it changes how your drawers look and sit right away.");
        return string.Join(" ", parts);
    }

    /// <summary>Rules that parse and check out but match nothing on the Desktop now are still saved.</summary>
    public const string ChatRulesSavedNothing = "The rules are saved, but nothing on your Desktop matches them yet.";

    /// <summary>The name a pasted design is saved under when the chat gave it none.</summary>
    public const string ChatUntitledDesign = "Your AI chat's design";

    public static string ChatNotShown(string why) => $"Not shown: {why}.";

    public static string DrawersOffLine(int files) =>
        files == 1 ? "1 drawer file goes back to your Desktop." : $"{files} drawer files go back to your Desktop.";

    /// <summary>For the copy tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Title, Intro, DesktopTitle, DesktopBody, DrawersTitle, DrawersBody, UsageTitle, UsageBody, ClaudeSource, ClaudeTrademark,
        DesignWithChat, Continue, NotNow, Cancel, MenuItem, PickOne, SaveFailed, WhatChanges, WhatHappened, DesktopOffLine, UsageOffLine, AddOnsStayLine, ArrangeNeedsDesktop, DesignDesktopOffNote, DrawersOffLine(2),
        ChatIntro, ChatCopyPrompt, ChatPasteName, ChatPastePlaceholder, ChatPreview, ChatUseRules, ChatUseDrawerSettings, ChatClose, ChatFindingsName, ChatReady,
        ChatPromptCopied, ChatDeskShown, ChatRulesShown, ChatNoDeskService, ChatRulesSavedNothing, ChatUntitledDesign, ChatFixCount(1), ChatFixCount(2), ChatManyBlocks(2),
        ChatRulesLine(1, ["Photos"]), ChatRulesLine(2, ["Photos", "Receipts"]), ChatNotShown("the Desk is busy"),
        ChatDrawerSettingsDone, ChatDrawerSettingsArranged, ChatDrawerSettingsResult(true, "2 drawers lined up."), ChatDrawerSettingsResult(false, "2 drawers lined up."),
        ChatDrawerSettingsLine("glass", 70, "bottom left", "row"), ChatDrawerSettingsLine("outline", null, null, null),
        ChatDrawerSettingsLine(null, 40, "top right", null), ChatDrawerSettingsLine(null, null, null, "column"),
    ];

    /// <summary>The strings the AI chat window draws, which name no product at all.</summary>
    public static IReadOnlyList<string> Chat { get; } = [.. All.Skip(All.ToList().IndexOf(ChatIntro)), DesignWithChat];
}

/// <summary>What a change says before Continue applies it (spec 4): one line per tick taken away.</summary>
public static class SetupChange
{
    /// <param name="hasAccount">Whether an account is saved: "Your account stays saved" is said only when there is one to stay.</param>
    public static IReadOnlyList<string> Lines(SetupChoices before, SetupChoices after, int drawerFiles, bool addonsInstalled, bool hasAccount)
    {
        var lines = new List<string>();
        if (before.Desktop && !after.Desktop) lines.Add(SetupCopy.DesktopOffLine);
        if (before.Drawers && !after.Drawers && drawerFiles > 0) lines.Add(SetupCopy.DrawersOffLine(drawerFiles));
        if (before.ClaudeUsage && !after.ClaudeUsage)
        {
            if (hasAccount) lines.Add(SetupCopy.UsageOffLine);
            if (addonsInstalled) lines.Add(SetupCopy.AddOnsStayLine);
        }
        return lines;
    }

    /// <summary>Why Continue is disabled, or null when it is not.</summary>
    public static string? Blocked(SetupChoices after) => after.AnyTicked ? null : SetupCopy.PickOne;
}
