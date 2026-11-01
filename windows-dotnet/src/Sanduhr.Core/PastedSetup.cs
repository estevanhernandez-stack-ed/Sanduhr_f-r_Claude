using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What a pasted AI chat answer held (setup spec, section 3): its Desk part (checked later by
/// <see cref="DeskDesign.Validate(JsonNode?, DeskDesignContext)"/>, which needs this PC), its drawer rules, the names the rules gave their
/// drawers, the look the drawers part asked for, every problem by field, and how many Sanduhr blocks (JSON objects holding <c>desk</c> or
/// <c>drawers</c>) the paste held. <see cref="Rules"/> is null when the paste had no rules, and never holds a rule that failed a check;
/// <see cref="DrawerLook"/> is null when the paste had no look, opacity or arrange, and never holds one that failed a check.</summary>
public sealed record PastedSetupRead(JsonNode? Desk, IReadOnlyList<DrawerRule>? Rules, IReadOnlyList<string> RuleDrawerNames,
    IReadOnlyList<DeskReason> Reasons, int Blocks, PastedDrawerLook? DrawerLook = null)
{
    public bool Ok => Reasons.Count == 0 && (Desk is not null || Rules is not null || DrawerLook is not null);
}

/// <summary>What a pasted drawers part set besides rules: a look, an opacity, a Tidy arrangement, each optional and at least one present.
/// <see cref="Arrange"/> carries a full Tidy, but only the parts the paste named count (<see cref="HasCorner"/>, <see cref="HasFlow"/>): the
/// rest come from the user's own Tidy when it is applied.</summary>
public sealed record PastedDrawerLook(DrawerLookKind? Look, int? Opacity, DrawerTidy? Arrange, bool HasCorner = true, bool HasFlow = true)
{
    public bool Any => Look is not null || Opacity is not null || Arrange is not null;
}

/// <summary>One line of the paste window's list: the field, what is wrong with it, and whether it blocks (an error) or only advises.</summary>
public sealed record PastedFinding(string Field, string Text, bool IsError);

/// <summary>
/// Reads a pasted AI chat answer. The answer is prose around one JSON block, often fenced; the reader scans balanced JSON objects
/// (a brace inside a string does not count), takes the first one holding <c>desk</c> or <c>drawers</c>, and says how many there were.
/// The only parts are <c>desk</c> and <c>drawers</c>; the drawers part holds rules, look, opacity and arrange and nothing else, so a chat can name no file and no path.
/// </summary>
public static class PastedSetup
{
    public const int MaxRules = 20;
    public const int MaxDays = 3650;
    public const int MaxTextChars = 60;

    /// <summary>The longest paste the reader scans, in characters; the scan is quadratic in unmatched braces, so a runaway paste is refused.</summary>
    public const int MaxPasteChars = 200_000;

    /// <summary>The scan gives up after this many opening braces that start nothing readable, so a paste of braces cannot stall it.</summary>
    private const int MaxMisses = 500;

    /// <summary>The rule kinds as a chat writes them: the enum's names in snake_case, in enum order.</summary>
    public static IReadOnlyList<string> KindWords { get; } = [.. Enum.GetValues<DrawerRuleKind>().Select(KindWord)];

    public static string KindWord(DrawerRuleKind kind)
    {
        var sb = new StringBuilder();
        foreach (var c in kind.ToString())
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static PastedSetupRead Read(string? raw)
    {
        raw ??= "";
        if (raw.Length > MaxPasteChars)
            return new(null, null, [], [new DeskReason("design", $"is longer than {MaxPasteChars} characters; paste only the answer")], 0);

        var blocks = Blocks(raw, out int broken);
        var candidates = blocks
            .OfType<JsonObject>()
            .Where(o => o.ContainsKey("desk") || o.ContainsKey("drawers"))
            .ToList();
        if (candidates.Count == 0)
            return new(null, null, [],
                [new DeskReason("design", broken > 0
                    ? "a block that looks like Sanduhr's did not parse as JSON; check for a missing quote or bracket, or ask the chat to send it again"
                    : "no Sanduhr block found; ask the chat for one JSON block with a desk part, a drawers part or both")], 0);

        // The first good block is the first whose parts pass this reader's checks; DeskDesign.Validate runs later, with this PC's context.
        // When none is clean, the first block's problems are the ones to show.
        PastedSetupRead? firstRead = null;
        foreach (var candidate in candidates)
        {
            var read = ReadBlock(candidate, candidates.Count);
            if (read.Reasons.Count == 0) return read;
            firstRead ??= read;
        }
        return firstRead!;
    }

    /// <summary>Refuses a desk part that names a piece this Windows build does not draw (<see cref="SetupPrompt.NotDrawn"/>, today
    /// <c>meetings</c>): <see cref="DeskDesign.Validate(JsonNode?, DeskDesignContext)"/> knows the word, since the Mac draws it, so it passes there.
    /// The reason is by field (<c>layout.meetings</c>), and nothing applies partly: the verdict comes back rejected with no design.</summary>
    public static DeskValidation RefuseUndrawn(DeskValidation verdict)
    {
        if (verdict.Design is not { } design) return verdict;
        var reasons = new List<DeskReason>();
        // The reader already names a field outside the prompt's list; a verdict built another way is held to the same list here.
        foreach (var (key, _) in design.ToJson())
            if (!SetupPrompt.DeskFields.Contains(key, StringComparer.Ordinal))
                reasons.Add(new DeskReason("desk." + key, "is not a field a chat can set; the fields are " + string.Join(", ", SetupPrompt.DeskFields)));
        reasons.AddRange((design.Layout ?? [])
            .Where(p => SetupPrompt.NotDrawn.Contains(p.Piece, StringComparer.Ordinal))
            .Select(p => new DeskReason("layout." + p.Piece, "is not drawn by Sanduhr on Windows yet; leave it out")));
        return reasons.Count == 0
            ? verdict
            : verdict with { Status = DeskDesignStatus.Rejected, Reasons = [.. verdict.Reasons, .. reasons], Fonts = [], Design = null };
    }

    /// <summary>The validator's own phrasings that name a product or a tool, and what the AI chat window says instead. Only these are
    /// replaced: text echoed from the paste (a field a chat made up, a name it typed) stays as typed.</summary>
    private static readonly (string From, string To)[] Phrasings =
    [
        ("\"all\", \"main\" or a monitor id from get_desk monitors", "\"all\" or \"main\""),
        ("use \"all\", \"main\" or an id from monitors in get_desk.", "use \"all\" or \"main\"."),
        ("use an id from start_menu_apps in get_desk. A button opens only what the Start menu offers.", "A button opens only what this PC's Start menu offers."),
        ("must be the stored image name from get_desk, or null", "must be an image Sanduhr already stored, or null"),
        ("Claude cannot", "an AI chat cannot"),
        ("Claude's", "the AI chat's"),
        ("get_desk", "this PC's Desk settings"),
    ];

    /// <summary>A reason's words as the AI chat window shows them: the Desk's own rules were written for Claude Code, and the chat window
    /// names no product and no tool, so "Claude cannot" reads "an AI chat cannot" and a mention of <c>get_desk</c> reads in plain words.</summary>
    public static string Neutral(string text)
    {
        foreach (var (from, to) in Phrasings) text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }

    /// <summary>The paste window's problem list: each reason by field in the window's neutral words, errors first, then the warnings.</summary>
    public static IReadOnlyList<PastedFinding> Findings(IEnumerable<DeskReason> errors, IEnumerable<DeskReason> warnings) =>
        errors.Select(r => new PastedFinding(Neutral(r.Field), Neutral(r.Message), IsError: true))
            .Concat(warnings.Select(r => new PastedFinding(Neutral(r.Field), Neutral(r.Message), IsError: false)))
            .Distinct()   // the reader and the validator can name the same field twice
            .ToList();

    private static PastedSetupRead ReadBlock(JsonObject block, int count)
    {
        var reasons = new List<DeskReason>();
        foreach (var (key, _) in block)
            if (key is not ("desk" or "drawers"))
                reasons.Add(new DeskReason(Clip(key, 24), "is not a part of the answer; the parts are desk and drawers"));

        JsonNode? desk = null;
        if (block.ContainsKey("desk"))
        {
            if (block["desk"] is JsonObject d)
            {
                desk = d.DeepClone();
                // The prompt teaches these fields and says any other is refused: a chat cannot bring a theme, song looks or Launcher buttons.
                foreach (var (key, _) in d)
                    if (!SetupPrompt.DeskFields.Contains(key, StringComparer.Ordinal))
                        reasons.Add(new DeskReason("desk." + Clip(key, 24), "is not a field a chat can set; the fields are " + string.Join(", ", SetupPrompt.DeskFields)));
            }
            else reasons.Add(new DeskReason("desk", "must be an object"));
        }

        List<DrawerRule>? rules = null;
        PastedDrawerLook? drawerLook = null;
        var names = new List<string>();
        if (block.ContainsKey("drawers"))
        {
            if (block["drawers"] is not JsonObject dobj)
                reasons.Add(new DeskReason("drawers", "must be { \"rules\": [ ... ], \"look\": ..., \"opacity\": ..., \"arrange\": { ... } }"));
            else if (dobj.Count == 0)
                reasons.Add(new DeskReason("drawers", "must hold rules, look, opacity or arrange; leave drawers out to change nothing"));
            else
            {
                foreach (var (key, _) in dobj)
                    if (key is not ("rules" or "look" or "opacity" or "arrange"))
                        reasons.Add(new DeskReason("drawers." + Clip(key, 24), "is not part of drawers; it holds rules, look, opacity and arrange"));
                drawerLook = ReadDrawerLook(dobj, reasons);
                if (!dobj.ContainsKey("rules")) { /* rules are optional: a look alone is a good drawers part */ }
                else if (dobj["rules"] is not JsonArray arr || arr.Count == 0)
                    reasons.Add(new DeskReason("drawers.rules", $"must be a list of 1 to {MaxRules} rules; leave rules out to keep them as they are"));
                else if (arr.Count > MaxRules)
                    reasons.Add(new DeskReason("drawers.rules", $"has {arr.Count} rules; at most {MaxRules}"));
                else
                {
                    rules = [];
                    var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string IdFor(string name)
                    {
                        // One name is one drawer; two names that slug alike ("My Photos", "My-Photos") or have no ASCII letters get ids of their own.
                        if (ids.TryGetValue(name, out var id)) return id;
                        id = DrawerIds.Unique(name, ids.Values);
                        ids[name] = id;
                        return id;
                    }
                    for (int i = 0; i < arr.Count; i++)
                        if (Rule(arr[i], $"drawers.rules[{i}]", reasons, IdFor) is { } rule) { rules.Add(rule.Rule); names.Add(rule.Name); }
                }
            }
        }

        // Nothing applies partly: a part that had a problem hands back nothing of itself.
        if (reasons.Count > 0) { rules = null; drawerLook = null; }
        return new(desk, rules, reasons.Count > 0 ? [] : names, [.. reasons.Take(40)], count, drawerLook);
    }

    /// <summary>The look, opacity and arrange members of a drawers part, each optional. A member that fails a check is named by field in
    /// <paramref name="reasons"/>; null when the part held none of the three.</summary>
    private static PastedDrawerLook? ReadDrawerLook(JsonObject d, List<DeskReason> reasons)
    {
        DrawerLookKind? look = null;
        int? opacity = null;
        DrawerTidy? arrange = null;
        bool hasCorner = false, hasFlow = false;
        if (d.ContainsKey("look"))
        {
            look = DrawerStyle.Look(Str(d["look"]));
            if (look is null) reasons.Add(new DeskReason("drawers.look", "must be one of " + string.Join(", ", DrawerStyle.LookWords)));
        }
        if (d.ContainsKey("opacity"))
        {
            if (WholeNumber(d["opacity"]) is { } n && n >= DrawerStyle.MinOpacity && n <= DrawerStyle.MaxOpacity) opacity = n;
            else reasons.Add(new DeskReason("drawers.opacity", $"must be a whole number from {DrawerStyle.MinOpacity} to {DrawerStyle.MaxOpacity}"));
        }
        if (d.ContainsKey("arrange"))
        {
            if (d["arrange"] is not JsonObject a || a.Count == 0)
                reasons.Add(new DeskReason("drawers.arrange", "must be { \"corner\": ..., \"direction\": ... } with at least one of them"));
            else
            {
                bool bad = false;
                foreach (var (key, _) in a)
                    if (key is not ("corner" or "direction"))
                    {
                        reasons.Add(new DeskReason("drawers.arrange." + Clip(key, 24), "is not part of arrange; it holds corner and direction"));
                        bad = true;
                    }
                DrawerCorner? corner = null;
                DrawerFlow? flow = null;
                if (a.ContainsKey("corner") && (corner = DrawerStyle.Corner(Str(a["corner"]))) is null)
                {
                    reasons.Add(new DeskReason("drawers.arrange.corner", "must be one of " + string.Join(", ", DrawerStyle.CornerWords)));
                    bad = true;
                }
                if (a.ContainsKey("direction") && (flow = DrawerStyle.Flow(Str(a["direction"]))) is null)
                {
                    reasons.Add(new DeskReason("drawers.arrange.direction", "must be one of " + string.Join(", ", DrawerStyle.FlowWords)));
                    bad = true;
                }
                if (!bad)
                {
                    arrange = new DrawerTidy(corner ?? DrawerTidy.Default.Corner, flow ?? DrawerTidy.Default.Flow);
                    hasCorner = corner is not null;
                    hasFlow = flow is not null;
                }
            }
        }
        var result = new PastedDrawerLook(look, opacity, arrange, hasCorner, hasFlow);
        return result.Any ? result : null;
    }

    private static (DrawerRule Rule, string Name)? Rule(JsonNode? node, string at, List<DeskReason> reasons, Func<string, string> idFor)
    {
        if (node is not JsonObject o) { reasons.Add(new DeskReason(at, "must be an object with kind and drawer")); return null; }
        int before = reasons.Count;
        foreach (var (key, _) in o)
            if (key is not ("kind" or "drawer" or "text" or "days"))
                reasons.Add(new DeskReason(at + "." + Clip(key, 24), "is not part of a rule; a rule has kind, drawer, text and days"));

        DrawerRuleKind kind = default;
        string? kindWord = Str(o["kind"]);
        var found = kindWord is null ? null : Enum.GetValues<DrawerRuleKind>().Cast<DrawerRuleKind?>().FirstOrDefault(k => KindWord(k!.Value) == kindWord);
        if (found is { } k) kind = k;
        else reasons.Add(new DeskReason(at + ".kind", "must be one of " + string.Join(", ", KindWords)));

        string? name = Str(o["drawer"])?.Trim();
        if (name is null || !DrawerNames.IsValid(name) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            reasons.Add(new DeskReason(at + ".drawer", $"must be a short folder name of up to {DrawerNames.MaxLength} characters, not a path"));

        bool named = kind is DrawerRuleKind.NameContains or DrawerRuleKind.NameStartsWith;
        string? text = null;
        if (named)
        {
            text = Str(o["text"])?.Trim();
            if (string.IsNullOrEmpty(text)) reasons.Add(new DeskReason(at + ".text", $"is needed for {kindWord}: part of a file name"));
            else if (text.EnumerateRunes().Count() > MaxTextChars) reasons.Add(new DeskReason(at + ".text", $"must be at most {MaxTextChars} characters"));
            else if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || text.EnumerateRunes().Any(MessageProposal.IsControl))
                reasons.Add(new DeskReason(at + ".text", "must be part of a file name, not a path"));
        }

        int days = 30;
        if (kind == DrawerRuleKind.OlderThan)
        {
            if (WholeNumber(o["days"]) is { } n && n is >= 1 and <= MaxDays) days = n;
            else reasons.Add(new DeskReason(at + ".days", $"must be a whole number from 1 to {MaxDays}"));
        }

        if (reasons.Count > before) return null;
        return (new DrawerRule(kind, idFor(name!), On: true, Text: text, Days: days), name!);
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? WholeNumber(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue) return (int)d;
        return null;
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];

    /// <summary>The reader's JSON rules for a chat's block: trailing commas and comments are forgiven, a repeated key is not (it would be
    /// ambiguous, and a JSON node cannot hold it).</summary>
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        AllowDuplicateProperties = false,
    };

    /// <summary>Every top-level JSON object in the paste, in order: a scan for balanced braces outside strings, each parsed alone.
    /// A span that does not parse is skipped and the scan goes on from the next character, so a prose brace before the real block is harmless.
    /// <paramref name="broken"/> counts the skipped spans that mention desk or drawers, so a block that was nearly right can be told apart from none.</summary>
    private static List<JsonNode> Blocks(string raw, out int broken)
    {
        var found = new List<JsonNode>();
        broken = 0;
        int misses = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '{') continue;
            if (misses > MaxMisses) break;
            int depth = 0, end = -1;
            bool inString = false, escaped = false;
            for (int j = i; j < raw.Length; j++)
            {
                char c = raw[j];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) { end = j; break; }
            }
            if (end < 0) { misses++; continue; }
            var span = raw[i..(end + 1)];
            JsonNode? node = null;
            try { node = JsonNode.Parse(span, null, ParseOptions); }
            catch (JsonException) { }
            if (node is null)
            {
                misses++;
                if (span.Contains("\"desk\"", StringComparison.Ordinal) || span.Contains("\"drawers\"", StringComparison.Ordinal)) broken++;
                continue;
            }
            found.Add(node);
            i = end;
        }
        return found;
    }
}
