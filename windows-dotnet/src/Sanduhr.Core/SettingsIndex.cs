using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// One searchable place in Settings: a page (no anchor), a section or a control. The anchor is
/// page-local and marks the element Settings scrolls to. <paramref name="Subsection"/> names the
/// Claude Usage view (Overview, Trends, Sessions) the anchor sits in, null elsewhere.
/// </summary>
public sealed record SettingsEntry(
    string Title,
    SettingsPage Page,
    string? Anchor,
    IReadOnlyList<string> Synonyms,
    string? Subsection = null);

/// <summary>
/// Settings v2's reach (the Mac's SettingsIndex.swift): every section's anchor, the sidebar search
/// index built from the names table, and prefix-and-word matching. Settings marks each anchor in
/// its XAML; <c>SettingsIndexTests</c> checks every anchor here is marked there.
/// </summary>
public static class SettingsIndex
{
    // General
    public const string Tray = "tray";
    public const string Startup = "startup";
    public const string Shortcuts = "shortcuts";
    // Accounts
    public const string AccountList = "accounts";
    // Alerts
    public const string Notifications = "notifications";
    public const string Thresholds = "thresholds";
    public const string Sound = "sound";
    public const string TestAlert = "test";
    // History
    public const string Chart = "chart";
    public const string HistoryExport = "export";
    // Claude Usage
    public const string Overview = "overview";
    public const string Vault = "vault";
    public const string Statusline = "statusline";
    public const string Mcp = "mcp";
    public const string Trends = "trends";
    public const string Sessions = "sessions";
    // Publish usage
    public const string Destination = "destination";
    public const string Token = "token";
    public const string Homes = "homes";
    public const string Schedule = "schedule";
    // Themes
    public const string Gallery = "gallery";
    public const string Studio = "studio";
    public const string Paste = "paste";
    public const string Installed = "installed";
    // About
    public const string Version = "version";
    public const string Trademark = "trademark";
    public const string Notices = "notices";
    public const string Where = "where";

    /// <summary>The Claude Usage views an anchor can sit in.</summary>
    public const string OverviewView = "Overview";
    public const string TrendsView = "Trends";
    public const string SessionsView = "Sessions";

    private static SettingsEntry E(string title, SettingsPage page, string anchor, string[]? synonyms = null,
        string? subsection = null) => new(title, page, anchor, synonyms ?? [], subsection);

    /// <summary>Every anchor in sidebar order, with its title and the words a person might type for it.</summary>
    public static IReadOnlyList<SettingsEntry> Anchors { get; } =
    [
        E(SettingsNames.TrayShows, SettingsPage.General, Tray,
            ["tray", "percent", "number", "icon", "rotate", "session", "weekly", "highest", "whichever is higher"]),
        E("Startup", SettingsPage.General, Startup,
            ["start with Windows", "sign-in", "login", "launch", "auto-start", "Startup apps"]),
        E("Shortcuts", SettingsPage.General, Shortcuts,
            [SettingsNames.ShortcutToOpenSettings, "hotkey", "keyboard", "keys", "Ctrl+Alt+S", "record", "global shortcut"]),

        E("Accounts", SettingsPage.Accounts, AccountList,
            ["sign in", "sign out", "add account", "rename", "update sign-in", "remove account", "switch",
             "active account", "sessionKey", "session key", "cookie", "cf_clearance"]),

        E("Alerts", SettingsPage.Alerts, Notifications,
            ["notification", "toast", "enable alerts", "projection", "pace", "reset notifications", "Focus Assist"]),
        E("Warn at and Urgent at", SettingsPage.Alerts, Thresholds, ["threshold", "warn", "urgent", "percent"]),
        E("Sound", SettingsPage.Alerts, Sound, ["chime", "sound", "snake", "model-scoped", "mute"]),
        E(SettingsNames.SendTestAlert, SettingsPage.Alerts, TestAlert, ["test", "try"]),

        E("History chart", SettingsPage.History, Chart,
            ["meter history", "30 days", "week", "month", "account", "all accounts", "chart"]),
        E(SettingsNames.ExportCsv, SettingsPage.History, HistoryExport, ["csv", "export", "clear history", "spreadsheet"]),

        E(OverviewView, SettingsPage.ClaudeUsage, Overview,
            ["Claude Code tokens", "today", "last 30 days", "calendar", "projects", "skills", "breakdown"], OverviewView),
        E("History vault", SettingsPage.ClaudeUsage, Vault,
            ["vault", "archive", "erase archive", "open vault folder", "homes", ".claude"], OverviewView),
        E("Claude Code statusline", SettingsPage.ClaudeUsage, Statusline,
            ["statusline", "status line", "prompt", "install statusline", "remove statusline"], OverviewView),
        E("Claude quota tools (MCP)", SettingsPage.ClaudeUsage, Mcp,
            ["MCP", "MCP server", "sanduhr-mcp", "agents", "tools", "burn attribution"], OverviewView),
        E(TrendsView, SettingsPage.ClaudeUsage, Trends, ["weeks", "4 weeks", "12 weeks", "26 weeks", "trend"], TrendsView),
        E(SessionsView, SettingsPage.ClaudeUsage, Sessions,
            ["ledger", "sessions", "models", "yesterday", "7 days", "stack by project", "csv"], SessionsView),

        E("Destination", SettingsPage.Publish, Destination,
            ["endpoint", "URL", "preset", "626 Labs", "dashboard", "auth", "header"]),
        E("Token", SettingsPage.Publish, Token, ["set token", "clear token", "credential", "bearer"]),
        E("Homes to share", SettingsPage.Publish, Homes, ["homes", "share", ".claude"]),
        E("Publish daily", SettingsPage.Publish, Schedule, ["daily", "schedule", "time", "publish now"]),

        E("Choose a theme", SettingsPage.Themes, Gallery, ["theme", "color", "colour", "gallery", "palette"]),
        E("Studio", SettingsPage.Themes, Studio, ["edit tokens", "glass alpha", "border alpha", "custom theme"]),
        E("Paste a theme", SettingsPage.Themes, Paste,
            [SettingsNames.SaveAndApply, "json", "import", "Copy agent prompt", "themes folder"]),
        E("Installed user themes", SettingsPage.Themes, Installed, ["delete theme", "reload", "user themes"]),

        E("Version", SettingsPage.About, Version,
            ["version", "What's New", "Take the tour", "tour", "release", "update"]),
        E("Trademark", SettingsPage.About, Trademark, ["Anthropic", "trademark", "disclaimer", "affiliated"]),
        E(SettingsNames.ThirdPartyNotices, SettingsPage.About, Notices, ["license", "licence", "credits", "open source"]),
        E(SettingsNames.WhereSettingsLive, SettingsPage.About, Where,
            ["settings.json", "folder", "AppData", "files", "data", "Credential Manager", "Show in Explorer"]),
    ];

    /// <summary>Where each name in <see cref="SettingsNames.Table"/> lives on its page.</summary>
    public static IReadOnlyDictionary<string, string> NameAnchors { get; } = new Dictionary<string, string>
    {
        [SettingsNames.TrayShows] = Tray,
        [SettingsNames.ShortcutToOpenSettings] = Shortcuts,
        [SettingsNames.AddAccount] = AccountList,
        [SettingsNames.SendTestAlert] = TestAlert,
        [SettingsNames.ExportCsv] = HistoryExport,
        [SettingsNames.SaveAndApply] = Paste,
        [SettingsNames.WhatsNew] = Version,
        [SettingsNames.TakeTheTour] = Version,
        [SettingsNames.WhereSettingsLive] = Where,
        [SettingsNames.ThirdPartyNotices] = Notices,
    };

    /// <summary>Words a page goes by besides its title.</summary>
    public static IReadOnlyDictionary<SettingsPage, string[]> PageSynonyms { get; } = new Dictionary<SettingsPage, string[]>
    {
        [SettingsPage.General] = ["tray", "startup", "shortcut"],
        [SettingsPage.Accounts] = ["sign in", "account"],
        [SettingsPage.Alerts] = ["notification", "warning"],
        [SettingsPage.History] = ["meter history", "chart"],
        [SettingsPage.ClaudeUsage] = ["Claude Code", "tokens", "usage", "vault", "statusline", "MCP"],
        [SettingsPage.Publish] = ["publish", "upload", "endpoint", "share"],
        [SettingsPage.Themes] = ["theme", "colors", "look"],
        [SettingsPage.About] = ["help", "version", "trademark", "privacy"],
    };

    /// <summary>The whole index: every page, every anchor, and every name in the names table.</summary>
    public static IReadOnlyList<SettingsEntry> All { get; } = Build();

    private static List<SettingsEntry> Build()
    {
        var all = SettingsNames.Pages
            .Select(p => new SettingsEntry(SettingsNames.PageTitle(p), p, null,
                PageSynonyms.TryGetValue(p, out var s) ? s : []))
            .ToList();
        all.AddRange(Anchors);
        foreach (var (name, page) in SettingsNames.Table)
        {
            var anchor = NameAnchors.TryGetValue(name, out var a) ? a : null;
            if (!all.Any(e => e.Title == name && e.Page == page && e.Anchor == anchor))
            {
                var sub = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor)?.Subsection;
                all.Add(new SettingsEntry(name, page, anchor, [], sub));
            }
        }
        return all;
    }

    /// <summary>The anchors on <paramref name="page"/>, in page order.</summary>
    public static IReadOnlyList<SettingsEntry> EntriesOn(SettingsPage page) => [.. Anchors.Where(e => e.Page == page)];

    /// <summary>The anchor <paramref name="id"/> on <paramref name="page"/>, null when the page has none by that id.</summary>
    public static SettingsEntry? Find(SettingsPage page, string? id) =>
        id is null ? null : Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == id);

    /// <summary>A match: the entry, and 0 when its title matched, 1 when only a synonym did.</summary>
    public sealed record Hit(SettingsEntry Entry, int Rank);

    /// <summary>
    /// The entries <paramref name="query"/> finds, best first: title matches before synonym matches,
    /// then in sidebar order, then in index order. Empty for a blank query.
    /// </summary>
    public static IReadOnlyList<Hit> Search(string? query, IReadOnlyList<SettingsEntry>? index = null)
    {
        var q = Words(query);
        if (q.Count == 0)
            return [];
        index ??= All;
        var order = SettingsNames.Pages;
        var found = new List<(Hit Hit, int I)>();
        for (int i = 0; i < index.Count; i++)
        {
            var e = index[i];
            if (Matches(q, e.Title))
                found.Add((new Hit(e, 0), i));
            else if (e.Synonyms.Any(s => Matches(q, s)))
                found.Add((new Hit(e, 1), i));
        }
        int PageOrder(SettingsPage p) { var at = IndexOf(order, p); return at < 0 ? int.MaxValue : at; }
        return [.. found
            .OrderBy(f => f.Hit.Rank)
            .ThenBy(f => PageOrder(f.Hit.Entry.Page))
            .ThenBy(f => f.I)
            .Select(f => f.Hit)];
    }

    /// <summary>
    /// The hits grouped by page for the sidebar: each page once, in the order of its best hit, with
    /// its matching sections and controls under it (the page's own entry is the page row, not a line
    /// under it; a title shows once per page).
    /// </summary>
    public static IReadOnlyList<(SettingsPage Page, IReadOnlyList<SettingsEntry> Entries)> Grouped(IReadOnlyList<Hit> hits)
    {
        var pages = new List<SettingsPage>();
        foreach (var h in hits)
            if (!pages.Contains(h.Entry.Page))
                pages.Add(h.Entry.Page);
        var result = new List<(SettingsPage, IReadOnlyList<SettingsEntry>)>();
        foreach (var page in pages)
        {
            var seen = new List<SettingsEntry>();
            foreach (var h in hits)
            {
                if (h.Entry.Page == page && h.Entry.Anchor is not null && !seen.Any(e => e.Title == h.Entry.Title))
                    seen.Add(h.Entry);
            }
            result.Add((page, seen));
        }
        return result;
    }

    /// <summary>Each query word starts a word of <paramref name="text"/> ("glow" finds "Notch glow",
    /// "tra sh" finds "Tray shows"), or the whole query starts <paramref name="text"/>.</summary>
    public static bool Matches(IReadOnlyList<string> query, string text)
    {
        var t = Words(text);
        var phrase = string.Join(' ', query);
        if (phrase.Length > 0 && string.Join(' ', t).StartsWith(phrase, StringComparison.Ordinal))
            return true;
        return query.Count > 0 && query.All(q => t.Any(w => w.StartsWith(q, StringComparison.Ordinal)));
    }

    /// <summary>Lowercased words with accents folded ("für" reads "fur"), split at anything that is
    /// not a letter or a digit.</summary>
    public static IReadOnlyList<string> Words(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return [];
        var folded = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                folded.Append(char.ToLowerInvariant(c));
        }
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in folded.ToString())
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            words.Add(current.ToString());
        return words;
    }

    private static int IndexOf(IReadOnlyList<SettingsPage> list, SettingsPage p)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == p)
                return i;
        return -1;
    }
}
