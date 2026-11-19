using System.Security.Cryptography;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// One agent turn event in, the surfaces called (5.0 spec 4). The rules are <see cref="NotificationRules"/>; the app
/// plugs the four surfaces in as delegates, so this stays testable without a window. A surface that throws is logged by
/// its exception's type and never stops the others. Call <see cref="Handle"/> on the UI thread.
/// </summary>
public sealed class AgentNotifier(Func<NotificationSettings> settings, Func<bool> demo, Func<string?> foregroundProcess,
    Func<string?, string?> project, Func<DateTimeOffset> clock)
{
    private readonly NotificationLimiter _limiter = new();

    /// <summary>Title, body and a tag: a session's newer toast replaces its older one.</summary>
    public Action<string, string, string>? Toast { get; set; }

    /// <summary>The event, and whether the taskbar flashes with the light.</summary>
    public Action<AgentTurnEvent, bool>? Glow { get; set; }

    /// <summary>The taskbar flash without the light.</summary>
    public Action? Flash { get; set; }

    /// <summary>The event, the card's title and its short title.</summary>
    public Action<AgentTurnEvent, string, string>? Card { get; set; }

    public Action<string>? Log { get; set; }

    public NotifySurfaces Handle(AgentTurnEvent e)
    {
        if (demo()) return NotifySurfaces.None;
        // The front window is read only once an interruption is otherwise due (the rules call the function at most once).
        var surfaces = NotificationRules.Decide(e, settings(), () => NotificationRules.InFront(e.Agent, foregroundProcess()), _limiter, clock());
        if (surfaces == NotifySurfaces.None) return surfaces;

        var name = project(e.Folder);
        if (surfaces.HasFlag(NotifySurfaces.Card))
            Try("card", () => Card?.Invoke(e, NotificationText.CardTitle(e.Agent, name), AgentSources.ShortName(e.Agent)));
        if (surfaces.HasFlag(NotifySurfaces.Toast))
            Try("toast", () => Toast?.Invoke(NotificationText.Title(e), NotificationText.Body(e, name), Tag(e)));
        if (surfaces.HasFlag(NotifySurfaces.Glow))
            Try("glow", () => Glow?.Invoke(e, surfaces.HasFlag(NotifySurfaces.Flash)));
        else if (surfaces.HasFlag(NotifySurfaces.Flash))
            Try("flash", () => Flash?.Invoke());
        return surfaces;
    }

    /// <summary>What ties a session's toasts together: 16 hex characters, well inside a toast tag's limit. The idle reminder
    /// has a tag of its own, so it never replaces that turn's "finished" toast in the notification history.</summary>
    public static string Tag(AgentTurnEvent e) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            e.Agent + "\n" + e.Session + (e.Kind == AgentTurnKind.Idle ? "\nidle" : ""))))[..16];

    private void Try(string surface, Action call)
    {
        try { call(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log?.Invoke($"agent notifications: {surface} failed ({ex.GetType().Name})");
        }
    }
}
