namespace Sanduhr.Core;

/// <summary>
/// What Claude Code tells Sanduhr through its hooks: only that a session waits on you or finished a
/// turn. Nothing about the conversation ever comes with it. Port of the rules half of the Mac's
/// <c>ClaudeCodeGlow.swift</c>.
/// </summary>
public enum ClaudeCodeEvent
{
    /// <summary><c>Notification</c>: a permission prompt, an input request, or the idle reminder.</summary>
    Waiting,
    /// <summary><c>Stop</c>: a turn finished.</summary>
    Done,
}

/// <summary>The two signal files the hooks touch under <c>%APPDATA%\Sanduhr</c>.</summary>
public static class ClaudeCodeSignals
{
    public const string WaitingFileName = "glow-waiting.signal";
    public const string DoneFileName = "glow-done.signal";

    /// <summary>The file name the hooks touch for <paramref name="e"/>.</summary>
    public static string FileName(ClaudeCodeEvent e) => e == ClaudeCodeEvent.Waiting ? WaitingFileName : DoneFileName;

    /// <summary>The event a file name stands for (case-insensitive, name only), null for any other name.</summary>
    public static ClaudeCodeEvent? EventForFileName(string? name)
    {
        if (string.Equals(name, WaitingFileName, StringComparison.OrdinalIgnoreCase)) return ClaudeCodeEvent.Waiting;
        if (string.Equals(name, DoneFileName, StringComparison.OrdinalIgnoreCase)) return ClaudeCodeEvent.Done;
        return null;
    }
}

/// <summary>Which Claude Code events glow, and whether a terminal in front skips them.</summary>
public sealed record GlowSwitches(bool Waiting = false, bool Done = false, bool SkipTerminal = true)
{
    public bool IsOn(ClaudeCodeEvent e) => e == ClaudeCodeEvent.Waiting ? Waiting : Done;
}

/// <summary>
/// At most one glow per kind of event every 20 seconds, and no "done" glow within 5 seconds of a
/// "waiting" one (a turn that ends by asking does both). Only a glow that happens counts.
/// </summary>
public record struct ClaudeCodeGlowLimiter
{
    public static readonly TimeSpan SameKindGap = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DoneAfterWaiting = TimeSpan.FromSeconds(5);

    public DateTimeOffset? LastWaiting { get; private set; }
    public DateTimeOffset? LastDone { get; private set; }

    /// <summary>Whether <paramref name="e"/> may glow at <paramref name="now"/>; remembers it when it may. A clock set back allows it.</summary>
    public bool Allow(ClaudeCodeEvent e, DateTimeOffset now)
    {
        var last = e == ClaudeCodeEvent.Waiting ? LastWaiting : LastDone;
        if (last is { } t && Within(SameKindGap, t, now)) return false;
        if (e == ClaudeCodeEvent.Done && LastWaiting is { } w && Within(DoneAfterWaiting, w, now)) return false;
        if (e == ClaudeCodeEvent.Waiting) LastWaiting = now; else LastDone = now;
        return true;
    }

    private static bool Within(TimeSpan gap, DateTimeOffset from, DateTimeOffset now)
    {
        var d = now - from;
        return d >= TimeSpan.Zero && d < gap;
    }
}

/// <summary>Pure rules: the controller hands it the foreground process name.</summary>
public static class ClaudeCodeGlowRules
{
    /// <summary>
    /// Terminals and editors known to host Claude Code, as process names without <c>.exe</c>. With one in
    /// front you are already looking at the session, so "Not while a terminal is in front" skips the glow.
    /// </summary>
    public static readonly IReadOnlySet<string> TerminalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "wezterm-gui", "alacritty",
        "kitty", "mintty", "Hyper", "Tabby", "warp", "Code", "Code - Insiders", "Cursor", "Zed", "idea64",
        "rider64", "devenv", "windsurf",
    };

    public static bool IsTerminal(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var n = processName.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return TerminalProcessNames.Contains(n);
    }

    /// <summary>
    /// Whether <paramref name="e"/> glows: its switch is on, no terminal is in front (when that option is on),
    /// and the limiter lets it through. Only a glow that happens is remembered.
    /// </summary>
    public static bool Decide(ClaudeCodeEvent e, GlowSwitches switches, string? foregroundProcess,
        ref ClaudeCodeGlowLimiter limiter, DateTimeOffset now)
    {
        if (!switches.IsOn(e)) return false;
        if (switches.SkipTerminal && IsTerminal(foregroundProcess)) return false;
        return limiter.Allow(e, now);
    }
}
