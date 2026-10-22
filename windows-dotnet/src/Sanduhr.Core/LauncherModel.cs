using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>Whether the Launcher's buttons sit in a row or a column.</summary>
public enum LauncherOrientation { Row, Column }

/// <summary>
/// One Launcher button: a target (an app or an https link), an optional image (a file name inside the
/// launcher folder), an optional label and an optional glow color. The constructor does not check;
/// <see cref="Create"/> and <see cref="LauncherModel.Parse"/> do.
/// </summary>
/// <param name="Kind">App or link.</param>
/// <param name="Target">The AUMID, or the https URL.</param>
/// <param name="Image">A hashed PNG file name in <c>%APPDATA%\Sanduhr\launcher\</c>; null draws the app's icon (a link draws a link glyph).</param>
/// <param name="Label">Up to <see cref="LauncherButton.MaxLabel"/> characters drawn under the image; null draws none.</param>
/// <param name="Glow">A <c>#rrggbb</c> hover and touch halo; null uses the Desk ink.</param>
public sealed partial record LauncherButton(LauncherKind Kind, string Target, string? Image = null, string? Label = null, string? Glow = null)
{
    /// <summary>The longest label.</summary>
    public const int MaxLabel = 24;

    [GeneratedRegex("^[0-9a-f]{16,64}\\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageNamePattern();

    /// <summary>True for a stored image file name: lowercase hex of the content hash and <c>.png</c>, never a path.</summary>
    public static bool IsImageName(string? name) => name is not null && ImageNamePattern().IsMatch(name);

    /// <summary>The label as stored: control characters dropped, trimmed, cut to <see cref="MaxLabel"/>; null when nothing is left.</summary>
    public static string? NormalizeLabel(string? raw)
    {
        if (raw is null) return null;
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        if (clean.Length > MaxLabel)
        {
            int cut = MaxLabel;
            if (char.IsHighSurrogate(clean[cut - 1])) cut--;   // never split a surrogate pair
            clean = clean[..cut].TrimEnd();
        }
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>A glow color as <c>#rrggbb</c> (lowercase); null when it is not a 3 or 6 digit hex color.</summary>
    public static string? NormalizeGlow(string? raw) => InkHex.Normalize(raw) is { } h ? "#" + h : null;

    /// <summary>
    /// A checked button: the target must pass <see cref="LauncherTarget.Validate"/> (else null), the image must be a
    /// stored file name, the label and glow are normalized (an unusable one becomes null).
    /// </summary>
    public static LauncherButton? Create(LauncherKind kind, string? target, string? image = null, string? label = null, string? glow = null)
    {
        if (!LauncherTarget.Validate(kind, target).Ok) return null;
        return new LauncherButton(kind, target!, IsImageName(image) ? image : null, NormalizeLabel(label), NormalizeGlow(glow));
    }

    /// <summary>The words the button goes by for a screen reader and the Settings list: its label, else the
    /// app's name (<paramref name="appName"/>, looked up from the Start menu), else the site of a link.</summary>
    public string DisplayName(string? appName = null)
    {
        if (Label is { Length: > 0 } label) return label;
        if (!string.IsNullOrWhiteSpace(appName)) return appName.Trim();
        return Kind == LauncherKind.Link ? LauncherTarget.LinkHost(Target) : Target;
    }
}

/// <summary>
/// The Launcher's buttons as <c>settings.json</c> keeps them (key <see cref="Key"/>): up to
/// <see cref="MaxButtons"/> buttons in a row or a column. Parsing drops a bad entry and keeps the rest; every
/// edit returns a new model.
/// </summary>
public sealed record LauncherModel(LauncherOrientation Orientation, IReadOnlyList<LauncherButton> Buttons)
{
    /// <summary>The settings key.</summary>
    public const string Key = "desk_launcher";

    /// <summary>The widget word the Launcher piece goes by in <see cref="DeskArrangement"/>.</summary>
    public const string Widget = "launcher";

    /// <summary>The most buttons one Launcher holds.</summary>
    public const int MaxButtons = 8;

    /// <summary>No buttons, in a row: what an absent key reads as.</summary>
    public static LauncherModel Empty { get; } = new(LauncherOrientation.Row, []);

    public bool IsEmpty => Buttons.Count == 0;

    public bool CanAdd => Buttons.Count < MaxButtons;

    /// <summary>The model for a value read from settings.json. Anything that is not an object, and every entry
    /// that does not pass, reads as nothing; only the first <see cref="MaxButtons"/> good entries are kept.</summary>
    public static LauncherModel Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return Empty;
        var orientation = Str(o["orientation"]) == "column" ? LauncherOrientation.Column : LauncherOrientation.Row;
        var buttons = new List<LauncherButton>();
        if (o["buttons"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (buttons.Count >= MaxButtons) break;
                if (item is not JsonObject b) continue;
                if (LauncherTarget.ParseKind(Str(b["kind"])) is not { } kind) continue;
                if (LauncherButton.Create(kind, Str(b["target"]), Str(b["image"]), Str(b["label"]), Str(b["glow"])) is { } button)
                    buttons.Add(button);
            }
        }
        return new LauncherModel(orientation, buttons);
    }

    /// <summary>The value to store: <c>{ "orientation": "row"|"column", "buttons": [ { kind, target, image, label, glow } ] }</c>.</summary>
    public JsonObject ToJson()
    {
        var array = new JsonArray();
        foreach (var b in Buttons)
        {
            array.Add((JsonNode?)new JsonObject
            {
                ["kind"] = LauncherTarget.Word(b.Kind),
                ["target"] = b.Target,
                ["image"] = b.Image,
                ["label"] = b.Label,
                ["glow"] = b.Glow,
            });
        }
        return new JsonObject
        {
            ["orientation"] = Orientation == LauncherOrientation.Column ? "column" : "row",
            ["buttons"] = array,
        };
    }

    /// <summary>The model with a button added at the end; unchanged when it is full or the button does not pass.</summary>
    public LauncherModel Add(LauncherButton button)
    {
        if (!CanAdd || LauncherButton.Create(button.Kind, button.Target, button.Image, button.Label, button.Glow) is not { } ok) return this;
        return this with { Buttons = [.. Buttons, ok] };
    }

    /// <summary>The model without the button at <paramref name="index"/>; unchanged for an index out of range.</summary>
    public LauncherModel Remove(int index) =>
        index < 0 || index >= Buttons.Count ? this : this with { Buttons = [.. Buttons.Where((_, i) => i != index)] };

    /// <summary>The model with the button at <paramref name="index"/> replaced; unchanged when out of range or the new button does not pass.</summary>
    public LauncherModel Replace(int index, LauncherButton button)
    {
        if (index < 0 || index >= Buttons.Count) return this;
        if (LauncherButton.Create(button.Kind, button.Target, button.Image, button.Label, button.Glow) is not { } ok) return this;
        var list = Buttons.ToList();
        list[index] = ok;
        return this with { Buttons = list };
    }

    /// <summary>The model with the button at <paramref name="index"/> moved by <paramref name="delta"/> places
    /// (-1 up, 1 down), stopping at the ends; unchanged when it cannot move.</summary>
    public LauncherModel Move(int index, int delta)
    {
        if (index < 0 || index >= Buttons.Count) return this;
        int to = Math.Clamp(index + delta, 0, Buttons.Count - 1);
        if (to == index) return this;
        var list = Buttons.ToList();
        var b = list[index];
        list.RemoveAt(index);
        list.Insert(to, b);
        return this with { Buttons = list };
    }

    public LauncherModel WithOrientation(LauncherOrientation orientation) => this with { Orientation = orientation };

    /// <summary>The stored image files this model uses.</summary>
    public IReadOnlySet<string> Images =>
        Buttons.Where(b => b.Image is not null).Select(b => b.Image!).ToHashSet(StringComparer.Ordinal);

    /// <summary>The images <paramref name="before"/> used that <paramref name="after"/> no longer does: the files to delete.</summary>
    public static IReadOnlyList<string> Orphaned(LauncherModel before, LauncherModel after)
    {
        var keep = after.Images;
        return [.. before.Images.Where(i => !keep.Contains(i)).OrderBy(i => i, StringComparer.Ordinal)];
    }

    private static string? Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
