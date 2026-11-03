using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// "Keep the Desk under desktop icons" (Settings, Desk Look; the Mac's name, v2.27.0-mac; spec 5.0d). On, the Desk's widgets draw
/// below the desktop icons and Fences, and the Launcher and drawers stay above them. On when the key is absent, so existing installs
/// get it with the update (Este, 2026-11-02); off is the only value ever written.
/// </summary>
public static class DeskUnderIcons
{
    /// <summary>The settings key (settings.json <c>desk_under_icons</c>).</summary>
    public const string Key = "desk_under_icons";

    /// <summary>The switch is on when the key is absent.</summary>
    public const bool DefaultOn = true;

    public const string Title = "Keep the Desk under desktop icons";
    public const string Caption = "Your icons and fences draw over the Desk. Right-click a bare part of a widget for the Desk menu.";

    /// <summary>Shown under the switch for the session when the route could not run (spec 5.0d, route failures).</summary>
    public const string Unavailable = "Your PC can't draw the Desk under the icons, so it draws above them.";

    /// <summary>Whether the switch is on in <paramref name="root"/> (the settings.json object); a missing or odd value is on.</summary>
    public static bool Read(JsonObject? root) =>
        root?[Key] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : DefaultOn;

    /// <summary>Writes the switch: off as false, on by removing the key, so on stays the default's.</summary>
    public static void Write(JsonObject root, bool on)
    {
        if (on) root.Remove(Key);
        else root[Key] = false;
    }
}
