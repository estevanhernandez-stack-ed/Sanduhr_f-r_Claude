using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What <see cref="DeskLook.Parse"/> made of a look file: the look when nothing was wrong, else every problem found.</summary>
public sealed record DeskLookParse(DeskLook? Look, IReadOnlyList<string> Problems);

/// <summary>
/// A ready-made Desk look (5.0 spec 5, section 3): a design in exactly the format <c>propose_desk</c> takes, wrapped with an id, a
/// name, one line of blurb and the font to use when this PC lacks the design's. The looks ship inside the app; nothing is downloaded.
/// JSON: <c>{schema_version, id, name, blurb, font_fallback, design}</c>. An unknown wrapper field is ignored, which is where a
/// wallpaper goes after 5.0; an unknown field inside <c>design</c> is refused, as it is for any design.
///
/// A look owns the arrangement and stays out of the user's content: its design names every piece Windows draws as shown or hidden,
/// and carries font, ink, plate, shadow, glow and <c>follows_theme: false</c>; it carries no launcher, messages, song looks, theme,
/// weather, name or note, and no monitor on any piece. The pieces at one anchor stack in the order the file lists them.
/// Pure. Not linked into the MCP server: an agent does not list or apply built-in looks.
/// </summary>
public sealed partial record DeskLook(string Id, string Name, string Blurb, string FontFallback, DeskDesign Design)
{
    public const int SchemaVersion = 1;
    public const int NameMax = 24;
    public const int BlurbMax = 60;

    /// <summary>
    /// The font families a look or its fallback may name, besides the bundled ones: families that ship with every Windows the package
    /// supports (build 17763 and later), under the names WPF lists them by. Weights are not families here: WPF lists "Segoe UI", never
    /// "Segoe UI Light" or "Segoe UI Semibold" (checked 2026-11-20). Nothing that exists only on Windows 11.
    /// </summary>
    public static readonly IReadOnlyList<string> WindowsFonts =
    [
        "Arial", "Bahnschrift", "Calibri", "Cambria", "Candara", "Comic Sans MS", "Consolas", "Constantia", "Corbel", "Courier New",
        "Gabriola", "Georgia", "Impact", "Ink Free", "Lucida Console", "Palatino Linotype", "Segoe Print", "Segoe Script", "Segoe UI",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    ];

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>An id is lowercase letters and single hyphens between them, at most 32 characters. It is the file's name.</summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= 32 } && IdPattern().IsMatch(id);

    /// <summary>The family is bundled with Sanduhr or is one of <see cref="WindowsFonts"/> (by any case).</summary>
    public static bool ShipsWithWindows(string family) =>
        BundledFonts.Bundled(family) is not null || WindowsFonts.Contains(family, StringComparer.OrdinalIgnoreCase);

    /// <summary>The look shows this piece.</summary>
    public bool Shows(string piece) => Design.Layout?.Any(p => p.Piece == piece && !p.IsHidden) == true;

    private static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s is not null ? s.Trim() : "";

    /// <summary>Reads a look file. Every problem is listed, so a broken look is fixed in one pass.</summary>
    public static DeskLookParse Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return new DeskLookParse(null, ["must be an object"]);
        var problems = new List<string>();
        if (!(o["schema_version"] is JsonValue sv && sv.TryGetValue<int>(out int version) && version == SchemaVersion))
            problems.Add($"schema_version must be {SchemaVersion}");
        string id = Str(o, "id"), name = Str(o, "name"), blurb = Str(o, "blurb"), fallback = Str(o, "font_fallback");
        if (!IsValidId(id)) problems.Add("id must be lowercase letters and hyphens, at most 32 characters");
        if (name.Length is 0 or > NameMax) problems.Add($"name must be 1 to {NameMax} characters");
        if (blurb.Length is 0 or > BlurbMax) problems.Add($"blurb must be 1 to {BlurbMax} characters");
        if (fallback.Length == 0 || !ShipsWithWindows(fallback))
            problems.Add("font_fallback must be a font that ships with every supported Windows, or a bundled one");

        var parsed = DeskDesign.Parse(o["design"]);
        if (parsed.Design is not { } design)
        {
            problems.AddRange(parsed.Reasons.Select(r => "design." + r));
            return new DeskLookParse(null, problems);
        }
        problems.AddRange(Rules(design));
        return problems.Count > 0
            ? new DeskLookParse(null, problems)
            : new DeskLookParse(new DeskLook(id, name, blurb, fallback, design), []);
    }

    /// <summary>What a look's design must and must not carry (spec section 3), as problems; empty when it keeps every rule.</summary>
    public static IReadOnlyList<string> Rules(DeskDesign d)
    {
        var p = new List<string>();
        if (d.Layout is not { } layout) p.Add("design.layout is missing");
        else
        {
            foreach (var piece in SetupPrompt.Pieces)
                if (layout.All(l => l.Piece != piece)) p.Add($"design.layout must name {piece}, shown or hidden");
            foreach (var l in layout)
            {
                if (!SetupPrompt.Pieces.Contains(l.Piece, StringComparer.Ordinal)) p.Add($"design.layout.{l.Piece} is not a piece Windows draws");
                if (l.Monitor is not null) p.Add($"design.layout.{l.Piece} must not name a monitor: a piece stays on the screen the user put it on");
                if (!l.IsHidden && (l.Anchor is null || l.Size is null)) p.Add($"design.layout.{l.Piece} must give its anchor and size");
            }
            if (layout.All(l => l.IsHidden)) p.Add("design.layout shows nothing");
        }
        if (d.Font is not { Family.Length: > 0 } font) p.Add("design.font is missing");
        else
        {
            if (font.Link is not null) p.Add("design.font must not bring a link: nothing is downloaded for a look");
            if (!ShipsWithWindows(font.Family)) p.Add("design.font must ship with every supported Windows, or be a bundled one");
        }
        if (d.Ink is null) p.Add("design.ink is missing");
        if (d.Plate is null) p.Add("design.plate is missing");
        if (d.Shadow is null) p.Add("design.shadow is missing");
        if (d.Glow is null) p.Add("design.glow is missing");
        if (d.FollowsTheme != false) p.Add("design.follows_theme must be false: a followed widget theme would override the look's ink");
        foreach (var (field, set) in new (string Field, bool Set)[]
                 {
                     ("launcher", d.Launcher is not null), ("messages", d.Messages is not null), ("looks", d.Looks is not null),
                     ("theme", d.Theme is not null), ("weather", d.Weather is not null), ("name", d.Name is not null), ("note", d.Note is not null),
                 })
            if (set) p.Add($"design.{field} is the user's, not a look's");
        return p;
    }
}

/// <summary>The ready-made looks, in the gallery's order, and the files that could not be used (each as "name: why").</summary>
public sealed record DeskLookCatalog(IReadOnlyList<DeskLook> Looks, IReadOnlyList<string> Skipped)
{
    /// <summary>The gallery's order. A look not named here comes after these, by id.</summary>
    public static readonly IReadOnlyList<string> Order = ["quiet-corner", "ice", "marquee", "terminal", "editorial", "dashboard"];

    public static DeskLookCatalog Empty { get; } = new([], []);

    public DeskLook? Find(string? id) => Looks.FirstOrDefault(l => l.Id == id);

    /// <summary>Reads the look files, each as its name (without the extension) and its text. A file that is not JSON, breaks a rule,
    /// carries an id that is not its name, or repeats an id is skipped and named in <see cref="Skipped"/>; the others load.</summary>
    public static DeskLookCatalog Load(IEnumerable<(string Name, string Json)> files)
    {
        var looks = new List<DeskLook>();
        var skipped = new List<string>();
        foreach (var (name, json) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (JsonException) { skipped.Add($"{name}: is not JSON"); continue; }
            var parsed = DeskLook.Parse(node);
            if (parsed.Look is not { } look) { skipped.Add($"{name}: {string.Join("; ", parsed.Problems)}"); continue; }
            if (look.Id != name) { skipped.Add($"{name}: its id is {look.Id}, not the file's name"); continue; }
            if (looks.Any(l => l.Id == look.Id)) { skipped.Add($"{name}: a second look with this id"); continue; }
            looks.Add(look);
        }
        int Rank(DeskLook l)
        {
            for (int i = 0; i < Order.Count; i++)
                if (Order[i] == l.Id) return i;
            return int.MaxValue;
        }
        return new DeskLookCatalog([.. looks.OrderBy(Rank).ThenBy(l => l.Id, StringComparer.Ordinal)], skipped);
    }
}
