using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>How the Desk's ink is chosen in a design: the three words the Desk settings have, the widget theme's text
/// color, or one to four colors of the user's (or Claude's) own.</summary>
public enum DeskInkKind { Auto, Light, Dark, Theme, Custom }

/// <summary>A design's ink. <see cref="Colors"/> are <c>#rrggbb</c> (lowercase) and only used by <see cref="DeskInkKind.Custom"/>:
/// one color, or two to four for a left-to-right gradient.</summary>
public sealed record DeskInkChoice(DeskInkKind Kind, IReadOnlyList<string> Colors)
{
    public static DeskInkChoice Of(DeskInkKind kind) => new(kind, []);

    /// <summary>The words the "what changed" list uses.</summary>
    public string Describe() => Kind switch
    {
        DeskInkKind.Auto => "automatic",
        DeskInkKind.Light => "light",
        DeskInkKind.Dark => "dark",
        DeskInkKind.Theme => "the widget theme's text color",
        _ => string.Join(", ", Colors),
    };
}

/// <summary>A design's font: a family name and, for a family the user may lack, an https link to get it. Sanduhr never
/// follows the link; the review bar only shows it.</summary>
public sealed record DeskFontChoice(string Family, string? Link = null);

/// <summary>One piece's part of a design's layout. Every member but <see cref="Piece"/> is optional: absent leaves the
/// current value alone. A hidden piece has no anchor, no size and no monitor. <c>Monitor</c> is "all", "main" or a monitor id.</summary>
public sealed record DeskPieceLayout(string Piece, DeskAnchor? Anchor, double? Size, bool? Hidden, string? Monitor = null)
{
    public bool IsHidden => Hidden == true || (Anchor is null && Hidden != false);
}

/// <summary>The Desk message lines a design brings, and whether they join the list or replace it (the same two modes as
/// Claude's message suggestions).</summary>
public sealed record DeskMessagesChange(IReadOnlyList<string> Lines, MessageProposalMode Mode);

/// <summary>Why a design (or one field of it) is refused: the field's path and a sentence that names the fix.</summary>
public sealed record DeskReason(string Field, string Message)
{
    public override string ToString() => Field + ": " + Message;

    public JsonObject ToJson() => new() { ["field"] = Field, ["message"] = Message };
}

/// <summary>What <see cref="DeskDesign.Parse"/> made of a design: the design when nothing was wrong, and the reasons and
/// warnings either way.</summary>
public sealed record DeskParse(DeskDesign? Design, IReadOnlyList<DeskReason> Reasons, IReadOnlyList<DeskReason> Warnings);

/// <summary>
/// A Desk design (spec, the agent-designed Desk, A): a PARTIAL set of changes to the Desk and the widget theme. A field that
/// is null leaves the current value alone. Pure data and rules, no IO: the MCP server links this file so it refuses
/// exactly what the widget would; <see cref="DeskDesignValidator"/> adds the rules that need the machine's fonts, the
/// wallpaper and the Start menu.
///
/// JSON, snake_case: <c>name</c>, <c>note</c>, <c>layout</c> ({piece: {anchor, size, hidden}}), <c>font</c> (a family, or
/// {family, link}), <c>ink</c> ("auto", "light", "dark", "theme", one #hex, or two to four #hex), <c>plate</c>,
/// <c>shadow</c>, <c>launcher</c> ({orientation, buttons[]}), <c>messages</c> ({lines[], mode}) and <c>theme</c> (a widget
/// theme object). <c>looks</c> is a list of per-song looks (<see cref="NowPlayingLookCheck"/>): they are saved with the design
/// and never become part of the Desk it reads back.
/// </summary>
public sealed partial record DeskDesign
{
    public const string RequestFile = "desk-design-request.json";
    public const string ResultFile = "desk-design-result.json";
    public const string StateFile = "desk-state.json";

    public const int NameMax = 40;
    public const int NoteMax = 200;
    public const int FontFamilyMax = 64;
    public const int MaxPieces = 16;

    /// <summary>The whole design is far below this by construction; the cap is a belt against a hostile or runaway request.</summary>
    public const int MaxJsonBytes = 128 * 1024;

    public string? Name { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<DeskPieceLayout>? Layout { get; init; }
    public DeskFontChoice? Font { get; init; }
    public DeskInkChoice? Ink { get; init; }
    public bool? Plate { get; init; }
    public bool? Shadow { get; init; }
    public LauncherModel? Launcher { get; init; }
    public DeskMessagesChange? Messages { get; init; }
    public JsonObject? Theme { get; init; }
    /// <summary>Per-song looks for now playing (1 to 20), saved into the user's looks when the design is applied.</summary>
    public IReadOnlyList<SongLook>? Looks { get; init; }

    private static readonly string[] Fields =
        ["name", "note", "layout", "font", "ink", "plate", "shadow", "launcher", "messages", "looks", "theme"];

    public static string Word(MessageProposalMode mode) => mode == MessageProposalMode.Add ? "add" : "replace";

    // -- reading ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a design from JSON, checking everything that needs no knowledge of the machine: shapes, ranges, hex colors,
    /// piece and anchor words, the Launcher's targets (the shape rules of <see cref="LauncherTarget"/>), message lines
    /// (<see cref="MessageProposal"/>), the theme (<see cref="ThemeLint"/>), text lengths and control characters. A
    /// field that is wrong is a reason and the design is null; an unknown field is a reason too, so a typo never
    /// silently does nothing. Warnings are the theme lint's.
    /// </summary>
    public static DeskParse Parse(JsonNode? node)
    {
        var r = new List<DeskReason>();
        var warnings = new List<DeskReason>();
        if (node is not JsonObject o)
            return new DeskParse(null, [new DeskReason("design", "must be an object")], warnings);

        var unknown = o.Select(kv => kv.Key).Where(k => !Fields.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            r.Add(new DeskReason("design", "unknown field(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 24)))
                + ". The fields are " + string.Join(", ", Fields) + "."));

        string? name = Text(o, "name", NameMax, r);
        string? note = Text(o, "note", NoteMax, r);
        var layout = ParseLayout(o["layout"], r);
        var font = ParseFont(o["font"], r);
        var ink = ParseInk(o["ink"], r);
        bool? plate = Bool(o, "plate", r);
        bool? shadow = Bool(o, "shadow", r);
        var launcher = ParseLauncher(o["launcher"], r);
        var messages = ParseMessages(o["messages"], r);
        JsonObject? theme = null;
        IReadOnlyList<SongLook>? looks = null;
        if (o["looks"] is { } lk)
        {
            var parsedLooks = NowPlayingLookCheck.ParseEntries(lk);
            foreach (var why in parsedLooks.Reasons) r.Add(new DeskReason("looks", why));
            if (parsedLooks.Reasons.Count == 0) looks = parsedLooks.Looks;
        }
        if (o["theme"] is { } t)
            theme = ParseTheme(t, r, warnings);

        if (r.Count > 0) return new DeskParse(null, r.Take(40).ToList(), warnings);
        return new DeskParse(new DeskDesign
        {
            Name = name, Note = note, Layout = layout, Font = font, Ink = ink, Plate = plate, Shadow = shadow,
            Launcher = launcher, Messages = messages, Theme = theme, Looks = looks,
        }, [], warnings);
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];

    private static bool IsControl(Rune r) => MessageProposal.IsControl(r);

    private static bool HasControl(string s)
    {
        foreach (var rune in s.EnumerateRunes())
            if (IsControl(rune)) return true;
        return false;
    }

    private static bool TryStr(JsonNode? n, out string value)
    {
        value = "";
        if (n is JsonValue v && v.TryGetValue<string>(out var s) && s is not null)
        {
            value = s;
            return true;
        }
        return false;
    }

    private static bool TryNumber(JsonNode? n, out double value)
    {
        value = 0;
        if (n is not JsonValue v) return false;
        if (v.TryGetValue<double>(out value)) return double.IsFinite(value);
        if (v.TryGetValue<int>(out var i)) { value = i; return true; }
        if (v.TryGetValue<long>(out var l)) { value = l; return true; }
        if (v.TryGetValue<decimal>(out var d)) { value = (double)d; return true; }
        return false;
    }

    private static string? Text(JsonObject o, string key, int max, List<DeskReason> r)
    {
        if (o[key] is not { } n) return null;
        if (!TryStr(n, out var raw)) { r.Add(new DeskReason(key, "must be a string")); return null; }
        if (raw.Length > max * 4 || raw.EnumerateRunes().Count() > max)
        {
            r.Add(new DeskReason(key, $"must be at most {max} characters"));
            return null;
        }
        if (HasControl(raw)) { r.Add(new DeskReason(key, "must be one line, with no control characters")); return null; }
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    private static bool? Bool(JsonObject o, string key, List<DeskReason> r)
    {
        if (o[key] is not { } n) return null;
        if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        r.Add(new DeskReason(key, "must be true or false"));
        return null;
    }

    private static List<DeskPieceLayout>? ParseLayout(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        if (node is not JsonObject lo)
        {
            r.Add(new DeskReason("layout", "must be an object of piece name to {anchor, size, hidden}"));
            return null;
        }
        if (lo.Count > MaxPieces)
        {
            r.Add(new DeskReason("layout", $"names {lo.Count} pieces; the Desk has {DeskArrangement.Known.Count}"));
            return null;
        }
        var pieces = new List<DeskPieceLayout>();
        int before = r.Count;
        foreach (var (key, value) in lo)
        {
            string field = "layout." + Clip(key, 24);
            if (!DeskArrangement.Known.Contains(key, StringComparer.Ordinal))
            {
                r.Add(new DeskReason(field, "is not a Desk piece; the pieces are " + string.Join(", ", DeskArrangement.Known)));
                continue;
            }
            if (value is not JsonObject po)
            {
                r.Add(new DeskReason(field, "must be an object with anchor, size and hidden"));
                continue;
            }
            foreach (var bad in po.Select(kv => kv.Key).Where(k => k is not ("anchor" or "size" or "hidden" or "monitor")).Order(StringComparer.Ordinal).Take(3))
                r.Add(new DeskReason(field, $"unknown member '{Clip(bad, 20)}'; use anchor, size, hidden and monitor"));

            DeskAnchor? anchor = null;
            if (po["anchor"] is { } an)
            {
                if (TryStr(an, out var word) && DeskAnchors.Parse(word.Trim().ToLowerInvariant()) is { } a) anchor = a;
                else r.Add(new DeskReason(field + ".anchor", "must be one of " + string.Join(", ", DeskAnchors.All.Select(DeskAnchors.Word))));
            }
            double? size = null;
            if (po["size"] is { } sn)
            {
                if (TryNumber(sn, out var s) && s >= DeskArrangement.MinScale - 1e-9 && s <= DeskArrangement.MaxScale + 1e-9)
                    size = DeskArrangement.ClampScale(s);
                else r.Add(new DeskReason(field + ".size", $"must be a number from {DeskArrangement.MinScale} to {DeskArrangement.MaxScale}"));
            }
            bool? hidden = null;
            if (po["hidden"] is { } hn)
            {
                if (hn is JsonValue hv && hv.TryGetValue<bool>(out var h)) hidden = h;
                else r.Add(new DeskReason(field + ".hidden", "must be true or false"));
            }
            string? monitor = null;
            if (po["monitor"] is { } mn)
            {
                if (TryStr(mn, out var mword) && MonitorProblem(mword) is null) monitor = DeskMonitors.Word(mword);
                else r.Add(new DeskReason(field + ".monitor", "must be \"all\", \"main\" or a monitor id from get_desk monitors"));
            }
            if (hidden == true) { anchor = null; size = null; monitor = null; }
            pieces.Add(new DeskPieceLayout(key, anchor, size, hidden, monitor));
        }
        return r.Count > before ? null : pieces;
    }

    /// <summary>Why a monitor word is refused, or null: it is "all", "main" or an id, a short string with no control characters.</summary>
    private static string? MonitorProblem(string word)
    {
        var t = word.Trim();
        if (t.Length == 0 || t.Length > DeskMonitors.MaxIdLength) return "length";
        foreach (var rune in t.EnumerateRunes())
            if (IsControl(rune)) return "control";
        return null;
    }

    /// <summary>Why a font family name is refused, or null. A family is a name as the font picker shows it: letters, digits,
    /// spaces and a few marks. Anything that could be a path, a URI, a font list or markup is not a family.</summary>
    public static string? FontFamilyProblem(string family)
    {
        if (family.Length == 0) return null;   // the system font
        if (family != family.Trim()) return "must not start or end with a space";
        if (family.EnumerateRunes().Count() > FontFamilyMax) return $"must be at most {FontFamilyMax} characters";
        foreach (var rune in family.EnumerateRunes())
        {
            if (IsControl(rune)) return "must not contain control characters";
            if (rune.Value < 0x80 && !(char.IsAsciiLetterOrDigit((char)rune.Value) || (char)rune.Value is ' ' or '.' or '-' or '_' or '(' or ')' or '+' or '&' or '!'))
                return "must be a font family name, not a path, link or list";
            if (rune.Value >= 0x80 && !Rune.IsLetterOrDigit(rune) && !Rune.IsWhiteSpace(rune) && !Rune.IsPunctuation(rune) && !Rune.IsSymbol(rune))
                return "must be a font family name";
        }
        return null;
    }

    private static DeskFontChoice? ParseFont(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        string family;
        string? link = null;
        if (node is JsonObject fo)
        {
            foreach (var bad in fo.Select(kv => kv.Key).Where(k => k is not ("family" or "link")).Order(StringComparer.Ordinal).Take(3))
                r.Add(new DeskReason("font", $"unknown member '{Clip(bad, 20)}'; use family and link"));
            if (!TryStr(fo["family"], out family))
            {
                r.Add(new DeskReason("font.family", "must be a font family name"));
                return null;
            }
            if (fo["link"] is { } ln)
            {
                if (!TryStr(ln, out var l)) r.Add(new DeskReason("font.link", "must be an https link"));
                else if (LauncherTarget.Validate(LauncherKind.Link, l) is { Ok: false } bad) r.Add(new DeskReason("font.link", bad.Reason));
                else link = l;
            }
        }
        else if (!TryStr(node, out family))
        {
            r.Add(new DeskReason("font", "must be a font family name (or {family, link})"));
            return null;
        }
        if (FontFamilyProblem(family) is { } problem)
        {
            r.Add(new DeskReason(node is JsonObject ? "font.family" : "font", problem));
            return null;
        }
        return new DeskFontChoice(family, link);
    }

    private static DeskInkChoice? ParseInk(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        const string Help = "must be auto, light, dark, theme, one #rrggbb color, or two to four colors for a gradient";
        if (TryStr(node, out var s))
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return DeskInkChoice.Of(DeskInkKind.Auto);
                case "light": return DeskInkChoice.Of(DeskInkKind.Light);
                case "dark": return DeskInkChoice.Of(DeskInkKind.Dark);
                case "theme": return DeskInkChoice.Of(DeskInkKind.Theme);
            }
            if (!s.Contains(',') && InkHex.Normalize(s) is { } one) return new DeskInkChoice(DeskInkKind.Custom, ["#" + one]);
            r.Add(new DeskReason("ink", Help));
            return null;
        }
        if (node is JsonArray arr)
        {
            if (arr.Count is < 1 or > 4) { r.Add(new DeskReason("ink", "a gradient is two to four colors")); return null; }
            var colors = new List<string>();
            foreach (var item in arr)
            {
                if (TryStr(item, out var c) && InkHex.Normalize(c) is { } hex) colors.Add("#" + hex);
                else { r.Add(new DeskReason("ink", "every color must be a #rrggbb hex color")); return null; }
            }
            return new DeskInkChoice(DeskInkKind.Custom, colors);
        }
        r.Add(new DeskReason("ink", Help));
        return null;
    }

    private static LauncherModel? ParseLauncher(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        if (node is not JsonObject lo)
        {
            r.Add(new DeskReason("launcher", "must be an object with orientation and buttons"));
            return null;
        }
        int before = r.Count;
        foreach (var bad in lo.Select(kv => kv.Key).Where(k => k is not ("orientation" or "buttons")).Order(StringComparer.Ordinal).Take(3))
            r.Add(new DeskReason("launcher", $"unknown member '{Clip(bad, 20)}'; use orientation and buttons"));
        var orientation = LauncherOrientation.Row;
        if (lo["orientation"] is { } on)
        {
            if (TryStr(on, out var w) && w.Trim().ToLowerInvariant() is "row" or "column")
                orientation = w.Trim().Equals("column", StringComparison.OrdinalIgnoreCase) ? LauncherOrientation.Column : LauncherOrientation.Row;
            else r.Add(new DeskReason("launcher.orientation", "must be row or column"));
        }
        var buttons = new List<LauncherButton>();
        if (lo["buttons"] is not JsonArray arr)
        {
            r.Add(new DeskReason("launcher.buttons", $"must be a list of 0 to {LauncherModel.MaxButtons} buttons"));
        }
        else if (arr.Count > LauncherModel.MaxButtons)
        {
            r.Add(new DeskReason("launcher.buttons", $"{arr.Count} buttons; the limit is {LauncherModel.MaxButtons}"));
        }
        else
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (ParseButton(arr[i], $"launcher.buttons[{i}]", r) is { } b) buttons.Add(b);
            }
        }
        return r.Count > before ? null : new LauncherModel(orientation, buttons);
    }

    private static LauncherButton? ParseButton(JsonNode? node, string field, List<DeskReason> r)
    {
        if (node is not JsonObject bo)
        {
            r.Add(new DeskReason(field, "must be an object with kind, target, image, label and glow"));
            return null;
        }
        int before = r.Count;
        foreach (var bad in bo.Select(kv => kv.Key).Where(k => k is not ("kind" or "target" or "image" or "label" or "glow")).Order(StringComparer.Ordinal).Take(3))
            r.Add(new DeskReason(field, $"unknown member '{Clip(bad, 20)}'; a button has kind, target, image, label and glow"));
        LauncherKind? kind = TryStr(bo["kind"], out var kw) ? LauncherTarget.ParseKind(kw) : null;
        if (kind is null) r.Add(new DeskReason(field + ".kind", "must be app or link"));
        string target = "";
        if (!TryStr(bo["target"], out target)) r.Add(new DeskReason(field + ".target", "must be a string"));
        else if (kind is { } k && LauncherTarget.Validate(k, target) is { Ok: false } why) r.Add(new DeskReason(field + ".target", why.Reason));
        string? image = null;
        if (bo["image"] is { } img)
        {
            if (TryStr(img, out var name) && LauncherButton.IsImageName(name)) image = name;
            else r.Add(new DeskReason(field + ".image", "must be the stored image name from get_desk, or null; Claude cannot supply a new image"));
        }
        string? label = null;
        if (bo["label"] is { } lab)
        {
            if (!TryStr(lab, out var raw)) r.Add(new DeskReason(field + ".label", "must be a string"));
            else if (HasControl(raw)) r.Add(new DeskReason(field + ".label", "must be one line, with no control characters"));
            else if (raw.Trim().Length > LauncherButton.MaxLabel) r.Add(new DeskReason(field + ".label", $"must be at most {LauncherButton.MaxLabel} characters"));
            else label = LauncherButton.NormalizeLabel(raw);
        }
        string? glow = null;
        if (bo["glow"] is { } gl)
        {
            if (TryStr(gl, out var g) && LauncherButton.NormalizeGlow(g) is { } hex) glow = hex;
            else r.Add(new DeskReason(field + ".glow", "must be a #rrggbb color, or null"));
        }
        if (r.Count > before || kind is null) return null;
        return LauncherButton.Create(kind.Value, target, image, label, glow);
    }

    private static DeskMessagesChange? ParseMessages(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        if (node is not JsonObject mo)
        {
            r.Add(new DeskReason("messages", "must be an object with lines and mode"));
            return null;
        }
        int before = r.Count;
        foreach (var bad in mo.Select(kv => kv.Key).Where(k => k is not ("lines" or "mode")).Order(StringComparer.Ordinal).Take(3))
            r.Add(new DeskReason("messages", $"unknown member '{Clip(bad, 20)}'; use lines and mode"));
        var mode = MessageProposalMode.Add;
        if (mo["mode"] is { } mn)
        {
            if (TryStr(mn, out var w) && w is "add" or "replace") mode = w == "replace" ? MessageProposalMode.Replace : MessageProposalMode.Add;
            else r.Add(new DeskReason("messages.mode", "must be add or replace"));
        }
        var lines = new List<string>();
        if (mo["lines"] is not JsonArray arr || arr.Count == 0)
        {
            r.Add(new DeskReason("messages.lines", $"must be a list of 1 to {MessageProposal.MaxLines} strings"));
        }
        else if (arr.Count > MessageProposal.MaxLines * 2)
        {
            r.Add(new DeskReason("messages.lines", $"{arr.Count} lines; the limit is {MessageProposal.MaxLines}"));
        }
        else
        {
            bool allStrings = true;
            foreach (var item in arr)
            {
                if (TryStr(item, out var line)) lines.Add(line);
                else allStrings = false;
            }
            if (!allStrings) r.Add(new DeskReason("messages.lines", $"must be a list of 1 to {MessageProposal.MaxLines} strings"));
            else foreach (var why in MessageProposal.Validate(lines)) r.Add(new DeskReason("messages.lines", why));
        }
        return r.Count > before ? null : new DeskMessagesChange(lines.Select(MessageText.Trim).ToList(), mode);
    }

    private static JsonObject? ParseTheme(JsonNode node, List<DeskReason> r, List<DeskReason> warnings)
    {
        if (node is not JsonObject to)
        {
            r.Add(new DeskReason("theme", "must be a widget theme object (name and the fourteen #rrggbb colors)"));
            return null;
        }
        ThemeLintResult lint;
        try { lint = ThemeLint.Lint(to); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or InvalidCastException or ArgumentException)
        {
            r.Add(new DeskReason("theme", "could not be read as a widget theme"));
            return null;
        }
        foreach (var f in lint.Findings.Take(30))
        {
            var reason = new DeskReason("theme." + f.Field, f.Message);
            if (f.Level == ThemeFindingLevel.Error) r.Add(reason); else warnings.Add(reason);
        }
        return lint.Ok ? (JsonObject)to.DeepClone() : null;
    }

    // -- writing ------------------------------------------------------------------------------------------

    /// <summary>The design as JSON: only the fields that are set, in the contract's shape.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Name is not null) o["name"] = Name;
        if (Note is not null) o["note"] = Note;
        if (Layout is not null)
        {
            var lo = new JsonObject();
            foreach (var p in Layout)
            {
                var po = new JsonObject();
                if (p.Anchor is { } a) po["anchor"] = DeskAnchors.Word(a);
                if (p.Size is { } s) po["size"] = s;
                if (p.Hidden is { } h) po["hidden"] = h;
                if (p.Monitor is { } mo) po["monitor"] = mo;
                lo[p.Piece] = po;
            }
            o["layout"] = lo;
        }
        if (Font is not null)
        {
            if (Font.Link is null) o["font"] = Font.Family;
            else o["font"] = new JsonObject { ["family"] = Font.Family, ["link"] = Font.Link };
        }
        if (Ink is { } ink)
        {
            if (ink.Kind != DeskInkKind.Custom) o["ink"] = ink.Kind.ToString().ToLowerInvariant();
            else if (ink.Colors.Count == 1) o["ink"] = ink.Colors[0];
            else
            {
                var arr = new JsonArray();
                foreach (var c in ink.Colors) arr.Add((JsonNode?)c);
                o["ink"] = arr;
            }
        }
        if (Plate is { } plate) o["plate"] = plate;
        if (Shadow is { } shadow) o["shadow"] = shadow;
        if (Launcher is not null) o["launcher"] = Launcher.ToJson();
        if (Messages is not null)
        {
            var arr = new JsonArray();
            foreach (var l in Messages.Lines) arr.Add((JsonNode?)l);
            o["messages"] = new JsonObject { ["lines"] = arr, ["mode"] = Word(Messages.Mode) };
        }
        if (Looks is not null)
        {
            var arr = new JsonArray();
            foreach (var l in Looks) arr.Add((JsonNode?)l.ToJson());
            o["looks"] = arr;
        }
        if (Theme is not null) o["theme"] = Theme.DeepClone();
        return o;
    }

    // -- merging and comparing ----------------------------------------------------------------------------

    /// <summary>
    /// The full design that results from applying <paramref name="proposal"/> to <paramref name="current"/> (null: nothing
    /// is known of the current Desk). A field the proposal sets wins; the layout is merged piece by piece through
    /// <see cref="DeskArrangement"/> (a moved piece keeps its size, a shown piece takes its standard anchor); the
    /// Launcher, ink, font, theme and messages replace as wholes. A Launcher with buttons that the layout leaves
    /// unplaced is placed at the first free corner, as adding a first button does in Settings.
    /// </summary>
    public static DeskDesign Merge(DeskDesign? current, DeskDesign proposal)
    {
        var layout = MergeLayout(current?.Layout, proposal.Layout);
        var launcher = proposal.Launcher ?? current?.Launcher;
        if (proposal.Launcher is { IsEmpty: false } && layout is not null
            && layout.FirstOrDefault(p => p.Piece == LauncherModel.Widget) is { IsHidden: true }
            && proposal.Layout?.Any(p => p.Piece == LauncherModel.Widget) != true)
        {
            var arr = ToArrangement(layout);
            arr.Place(LauncherModel.Widget, arr.FirstFreeAnchor());
            layout = FromArrangement(arr);
        }
        return new DeskDesign
        {
            Name = proposal.Name ?? current?.Name,
            Note = proposal.Note ?? current?.Note,
            Layout = layout,
            Font = proposal.Font ?? current?.Font,
            Ink = proposal.Ink ?? current?.Ink,
            Plate = proposal.Plate ?? current?.Plate,
            Shadow = proposal.Shadow ?? current?.Shadow,
            Launcher = launcher,
            Messages = proposal.Messages ?? current?.Messages,
            Theme = proposal.Theme ?? current?.Theme,
            Looks = proposal.Looks ?? current?.Looks,
        };
    }

    private static DeskArrangement ToArrangement(IReadOnlyList<DeskPieceLayout>? layout)
    {
        var arr = new DeskArrangement();
        if (layout is null) return arr;
        var placed = layout.Where(p => !p.IsHidden && DeskArrangement.Known.Contains(p.Piece, StringComparer.Ordinal))
            .Select(p => new DeskPlacement(p.Piece, p.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece), p.Size ?? 1, DeskMonitors.FromWord(p.Monitor)));
        return new DeskArrangement(placed);
    }

    /// <summary>Every known piece: the placed ones in layout order, then the hidden ones in Settings order.</summary>
    public static List<DeskPieceLayout> FromArrangement(DeskArrangement arr)
    {
        var list = arr.Pieces.Select(p => new DeskPieceLayout(p.Widget, p.Anchor, p.Scale, false, p.Monitor)).ToList();
        foreach (var w in DeskArrangement.Known)
            if (arr.Placement(w) is null) list.Add(new DeskPieceLayout(w, null, null, true));
        return list;
    }

    private static List<DeskPieceLayout>? MergeLayout(IReadOnlyList<DeskPieceLayout>? current, IReadOnlyList<DeskPieceLayout>? proposal)
    {
        if (proposal is null) return current?.ToList();
        var arr = ToArrangement(current);
        foreach (var p in proposal)
        {
            if (p.Hidden == true) { arr.Hide(p.Piece); continue; }
            if (p.Anchor is { } a) arr.Place(p.Piece, a);
            else if (p.Hidden == false && arr.Placement(p.Piece) is null) arr.Show(p.Piece);
            if (p.Size is { } s) arr.SetScale(p.Piece, s);
            if (p.Monitor is not null) arr.SetMonitor(p.Piece, p.Monitor);
        }
        return FromArrangement(arr);
    }

    /// <summary>
    /// The "what changed" lines for the review bar: what <paramref name="b"/> sets that differs from
    /// <paramref name="a"/>, one plain sentence each. A field <paramref name="b"/> leaves null is not mentioned.
    /// <paramref name="pieceName"/> turns a piece key into the name Settings shows; <paramref name="monitorName"/> turns a monitor id into
    /// how a person knows it ("1920×1080, right of main"), null for an id it cannot name.
    /// </summary>
    public static IReadOnlyList<string> Diff(DeskDesign a, DeskDesign b, Func<string, string>? pieceName = null, Func<string, string?>? monitorName = null)
    {
        string M(string? monitor) => DeskMonitors.FromWord(monitor) switch
        {
            null => "all monitors",
            DeskMonitors.Main => "the main monitor",
            var id => monitorName?.Invoke(id) ?? "another monitor",
        };
        string P(string key) => pieceName?.Invoke(key) ?? key;
        var lines = new List<string>();

        if (b.Layout is { } layout)
        {
            foreach (var p in layout)
            {
                var was = a.Layout?.FirstOrDefault(x => x.Piece == p.Piece);
                bool wasKnown = was is not null;
                bool wasPlaced = was is { IsHidden: false };
                if (p.IsHidden)
                {
                    if (!wasKnown || wasPlaced) lines.Add($"{P(p.Piece)}: hidden");
                    continue;
                }
                var anchor = p.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece);
                if (wasPlaced)
                {
                    var from = was!.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece);
                    if (from != anchor) lines.Add($"{P(p.Piece)}: moved from {DeskAnchors.Name(from).ToLowerInvariant()} to {DeskAnchors.Name(anchor).ToLowerInvariant()}");
                    if (Math.Abs((was.Size ?? 1) - (p.Size ?? 1)) > 1e-9)
                        lines.Add($"{P(p.Piece)}: size {Percent(was.Size ?? 1)} to {Percent(p.Size ?? 1)}");
                    if (!DeskMonitors.Same(was.Monitor, p.Monitor))
                        lines.Add(DeskMonitors.FromWord(p.Monitor) is null ? $"{P(p.Piece)}: now on all monitors" : $"{P(p.Piece)}: moved to {M(p.Monitor)}");
                }
                else
                {
                    lines.Add($"{P(p.Piece)}: shown at {DeskAnchors.Name(anchor).ToLowerInvariant()}"
                        + (Math.Abs((p.Size ?? 1) - 1) > 1e-9 ? $", size {Percent(p.Size ?? 1)}" : "")
                        + (DeskMonitors.FromWord(p.Monitor) is null ? "" : $", on {M(p.Monitor)}"));
                }
            }
        }
        if (b.Font is { } font && (a.Font is null || !string.Equals(a.Font.Family, font.Family, StringComparison.OrdinalIgnoreCase)))
            lines.Add($"Font: {FontName(a.Font)} to {FontName(font)}");
        if (b.Ink is { } ink && (a.Ink is null || a.Ink.Describe() != ink.Describe()))
            lines.Add(a.Ink is null ? $"Ink: {ink.Describe()}" : $"Ink: {a.Ink.Describe()} to {ink.Describe()}");
        if (b.Plate is { } plate && a.Plate != plate) lines.Add($"Plate behind the text: {(plate ? "on" : "off")}");
        if (b.Shadow is { } shadow && a.Shadow != shadow) lines.Add($"Text shadow: {(shadow ? "on" : "off")}");
        if (b.Launcher is { } launcher && (a.Launcher is null || a.Launcher.ToJson().ToJsonString() != launcher.ToJson().ToJsonString()))
            lines.Add($"Launcher: {Buttons(launcher)}");
        if (b.Messages is { } m)
        {
            int n = m.Lines.Count(MessageProposal.IsMessageLine);
            lines.Add(m.Mode == MessageProposalMode.Add
                ? $"Messages: adds {n} line{(n == 1 ? "" : "s")}"
                : $"Messages: replaces the list with {n} line{(n == 1 ? "" : "s")}");
        }
        if (b.Looks is { Count: > 0 } looks)
            lines.Add($"Song looks: {looks.Count} song{(looks.Count == 1 ? "" : "s")} styled for now playing");
        if (b.Theme is { } theme && (a.Theme is null || a.Theme.ToJsonString() != theme.ToJsonString()))
        {
            string to = ThemeName(theme) ?? "a new theme";
            string? from = a.Theme is null ? null : ThemeName(a.Theme);
            lines.Add(from is null ? $"Widget theme: {to}" : from == to ? $"Widget theme: {to}, recolored" : $"Widget theme: {from} to {to}");
        }
        return lines;
    }

    private static string Percent(double scale) => Math.Round(scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string FontName(DeskFontChoice? f) => f is null ? "the current font" : f.Family.Length == 0 ? "the system font" : f.Family;

    private static string Buttons(LauncherModel l)
    {
        if (l.IsEmpty) return "no buttons";
        int n = l.Buttons.Count;
        return $"{n} button{(n == 1 ? "" : "s")} in a {(l.Orientation == LauncherOrientation.Column ? "column" : "row")}";
    }

    private static string? ThemeName(JsonObject theme) =>
        theme["name"] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
