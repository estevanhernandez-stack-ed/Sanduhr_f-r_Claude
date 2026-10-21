namespace Sanduhr.Core;

/// <summary>What a Launcher button opens: a Start-menu app by its AppUserModelID, or an https link.</summary>
public enum LauncherKind
{
    /// <summary>A Start-menu app, by AppUserModelID, opened as <c>shell:AppsFolder\&lt;AUMID&gt;</c>.</summary>
    App,
    /// <summary>An absolute <c>https://</c> URL, opened in the default browser.</summary>
    Link,
}

/// <summary>The answer to "may this be a Launcher target": <see cref="Ok"/>, or the reason it was refused.</summary>
public readonly record struct LauncherTargetCheck(bool Ok, string Reason)
{
    public static readonly LauncherTargetCheck Valid = new(true, "");

    public static LauncherTargetCheck Refused(string reason) => new(false, reason);
}

/// <summary>
/// The Launcher's safety property (spec, Desk Launcher): a button opens an installed Start-menu app or an
/// https link, and nothing else. No command lines, no file paths, no arguments, no <c>.lnk</c> or
/// <c>.exe</c> targets, so a button Claude later proposes cannot run anything the person didn't install.
/// One pure gate for settings.json parsing, the Settings controls and the launch itself.
/// </summary>
public static class LauncherTarget
{
    /// <summary>The longest AppUserModelID accepted (Windows' own limit is 128 for a packaged app's ID).</summary>
    public const int MaxAppIdLength = 256;

    /// <summary>The longest link accepted.</summary>
    public const int MaxLinkLength = 2048;

    /// <summary>The settings.json word for a kind.</summary>
    public static string Word(LauncherKind kind) => kind == LauncherKind.App ? "app" : "link";

    /// <summary>The kind a settings.json word names; null for anything else.</summary>
    public static LauncherKind? ParseKind(string? word) => word switch
    {
        "app" => LauncherKind.App,
        "link" => LauncherKind.Link,
        _ => null,
    };

    /// <summary>
    /// May this be a target of this kind? An app is an AUMID shape: letters, digits and <c>. _ ! - { } +</c> only
    /// (the packaged form <c>Publisher.App_hash!App</c>, a dotted desktop-app ID, the <c>Chrome._crx_...</c> PWA
    /// form), 1 to <see cref="MaxAppIdLength"/> characters, with no path separator, drive or UNC prefix, space,
    /// quote or argument character anywhere. A link is an absolute <c>https://</c> URL with a host, at most
    /// <see cref="MaxLinkLength"/> characters, with no whitespace, quote or backslash and no sign-in part.
    /// </summary>
    public static LauncherTargetCheck Validate(LauncherKind kind, string? target) => kind switch
    {
        LauncherKind.App => ValidateApp(target),
        LauncherKind.Link => ValidateLink(target),
        _ => LauncherTargetCheck.Refused("Unknown kind of button."),
    };

    private static LauncherTargetCheck ValidateApp(string? id)
    {
        if (string.IsNullOrEmpty(id)) return LauncherTargetCheck.Refused("Choose an app.");
        if (id.Length > MaxAppIdLength) return LauncherTargetCheck.Refused("That app ID is too long.");
        if (id.Trim() != id || id.Any(char.IsWhiteSpace) || id.Any(char.IsControl))
            return LauncherTargetCheck.Refused("An app ID has no spaces.");
        foreach (var c in id)
        {
            if (c == '\\' || c == '/') return LauncherTargetCheck.Refused("A path is not an app ID.");
            if (c == '"' || c == '\'' || c == '`') return LauncherTargetCheck.Refused("An app ID has no quotes.");
            if (!IsAppIdChar(c)) return LauncherTargetCheck.Refused("That is not an app ID.");
        }
        if (!id.Any(char.IsAsciiLetterOrDigit)) return LauncherTargetCheck.Refused("That is not an app ID.");
        return LauncherTargetCheck.Valid;
    }

    private static bool IsAppIdChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '!' or '-' or '{' or '}' or '+';

    private static LauncherTargetCheck ValidateLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return LauncherTargetCheck.Refused("Enter a link.");
        if (link.Length > MaxLinkLength) return LauncherTargetCheck.Refused("That link is too long.");
        if (link.Trim() != link || link.Any(char.IsWhiteSpace) || link.Any(char.IsControl))
            return LauncherTargetCheck.Refused("A link has no spaces.");
        if (link.Contains('"') || link.Contains('\'') || link.Contains('\\') || link.Contains('`'))
            return LauncherTargetCheck.Refused("A link has no quotes or backslashes.");
        if (!link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return LauncherTargetCheck.Refused("A link must start with https://.");
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return LauncherTargetCheck.Refused("That is not a link.");
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal))
            return LauncherTargetCheck.Refused("A link must start with https://.");
        if (string.IsNullOrEmpty(uri.Host)) return LauncherTargetCheck.Refused("That link has no site name.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) return LauncherTargetCheck.Refused("A link has no sign-in part.");
        return LauncherTargetCheck.Valid;
    }

    /// <summary>The string the shell is handed for an app: <c>shell:AppsFolder\&lt;AUMID&gt;</c>. Null when the ID is refused.</summary>
    public static string? AppShellPath(string? id) =>
        ValidateApp(id).Ok ? @"shell:AppsFolder\" + id : null;

    /// <summary>The URL the browser is handed for a link: the absolute form of the validated link. Null when refused.</summary>
    public static string? LinkUrl(string? link) =>
        ValidateLink(link).Ok ? new Uri(link!, UriKind.Absolute).AbsoluteUri : null;

    /// <summary>A short line for a link button with no label: the site name (<c>www.</c> dropped).</summary>
    public static string LinkHost(string? link)
    {
        if (!ValidateLink(link).Ok) return "";
        var host = new Uri(link!, UriKind.Absolute).Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
