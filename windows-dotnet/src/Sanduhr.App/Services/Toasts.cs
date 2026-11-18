using Microsoft.Toolkit.Uwp.Notifications;

namespace Sanduhr.App.Services;

/// <summary>
/// Every Windows toast Sanduhr shows goes through here (5.0 spec 4): one builder and one activation handler in place of
/// three builders and two handlers. ToastNotificationManagerCompat resolves identity for the Store package and registers
/// one for the Velopack build. Callers keep their own try/catch: a toast that cannot show must never break its feature.
/// </summary>
internal static class Toasts
{
    private static readonly object Gate = new();
    private static readonly List<Action<string>> Handlers = [];
    private static bool _hooked;

    /// <summary>Adds a handler for a clicked toast. It gets the toast's argument string: empty for a click on the body,
    /// the button's arguments for a button. Handlers run on the activation thread, so each one dispatches for itself.</summary>
    public static void OnActivated(Action<string> handler)
    {
        lock (Gate)
        {
            Handlers.Add(handler);
            if (_hooked) return;
            ToastNotificationManagerCompat.OnActivated += Route;
            _hooked = true;
        }
    }

    private static void Route(ToastNotificationActivatedEventArgsCompat args)
    {
        Action<string>[] handlers;
        lock (Gate) handlers = [.. Handlers];
        var argument = args.Argument ?? "";
        foreach (var handler in handlers)
        {
            try { handler(argument); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] toast handler failed: {e.GetType().Name}");
            }
        }
    }

    /// <summary>Shows a toast. A <paramref name="tag"/> within a <paramref name="group"/> makes a newer toast replace an
    /// older one; <paramref name="more"/> adds buttons.</summary>
    public static void Show(string title, string? body = null, string? group = null, string? tag = null,
        Action<ToastContentBuilder>? more = null)
    {
        var builder = new ToastContentBuilder().AddText(title);
        if (!string.IsNullOrEmpty(body)) builder.AddText(body);
        more?.Invoke(builder);
        builder.Show(t =>
        {
            if (tag is not null) t.Tag = tag;
            if (group is not null) t.Group = group;
        });
    }
}
