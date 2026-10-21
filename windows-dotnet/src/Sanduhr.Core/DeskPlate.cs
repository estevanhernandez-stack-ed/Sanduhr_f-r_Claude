using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// "Plate behind the text" (Settings, Desk Look; the Mac's <c>DeskPlate</c>). On, each Desk piece draws a
/// soft rounded plate behind it, for a desktop with files under it: the text reads over file names
/// instead of landing on them. Off when the key is absent, so every Desk from before the plate looks
/// exactly as it did; a fresh install writes it on (<see cref="Seed"/>).
/// </summary>
public static class DeskPlate
{
    /// <summary>The settings key (settings.json <c>desk_plate</c>).</summary>
    public const string Key = "desk_plate";

    /// <summary>The switch is off when the key is absent.</summary>
    public const bool DefaultOn = false;

    public const string Title = "Plate behind the text";
    public const string Caption = "A soft plate behind each piece, for desktops with files under it.";

    /// <summary>The plate's corner radius, as the Mac's.</summary>
    public const double CornerRadius = 18;

    /// <summary>How far the plate reaches past the piece's own bounds, each side. The Mac reaches 14 and
    /// blurs 10; here the reach plus <see cref="Blur"/> fits the 16 DIPs a Desk window leaves around its pieces.</summary>
    public const double Reach = 12;

    /// <summary>The plate's blur radius in DIPs.</summary>
    public const double Blur = 4;

    /// <summary>The plate fill's opacity, as the Mac's soft style.</summary>
    public const double Opacity = 0.30;

    /// <summary>The plate's color hex: black behind light ink, white behind dark ink so the text keeps its contrast.</summary>
    public static string ColorHex(bool darkInk) => darkInk ? "ffffff" : "000000";

    /// <summary>Whether the plate is on in <paramref name="root"/> (the settings.json object); a missing or odd value is off.</summary>
    public static bool Read(JsonObject? root) =>
        root?[Key] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : DefaultOn;

    /// <summary>Writes the switch. Off is written as false, never removed, so a later <see cref="Seed"/>
    /// can't mistake a choice for a missing key and turn the plate back on.</summary>
    public static void Write(JsonObject root, bool on) => root[Key] = on;

    /// <summary>
    /// A fresh install's first launch: the plate goes on unless the key is already there. An existing
    /// install passes <paramref name="freshInstall"/> false and is left as it is. True when it wrote.
    /// </summary>
    public static bool Seed(JsonObject root, bool freshInstall)
    {
        if (!freshInstall || root.ContainsKey(Key)) return false;
        root[Key] = true;
        return true;
    }
}
