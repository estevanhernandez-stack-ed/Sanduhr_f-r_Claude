using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What a pasted AI chat answer held (setup spec, section 3): its Desk part (checked later by
/// <see cref="DeskDesign.Validate(JsonNode?, DeskDesignContext)"/>, which needs this PC), its drawer rules, the names the rules gave their
/// drawers, every problem by field, and how many Sanduhr blocks (JSON objects holding <c>desk</c> or <c>drawers</c>) the paste held.
/// <see cref="Rules"/> is null when the paste had no drawers part, and never holds a rule that failed a check.</summary>
public sealed record PastedSetupRead(JsonNode? Desk, IReadOnlyList<DrawerRule>? Rules, IReadOnlyList<string> RuleDrawerNames,
    IReadOnlyList<DeskReason> Reasons, int Blocks)
{
    public bool Ok => Reasons.Count == 0 && (Desk is not null || Rules is not null);
}

/// <summary>
/// Reads a pasted AI chat answer. The answer is prose around one JSON block, often fenced; the reader scans balanced JSON objects
/// (a brace inside a string does not count), takes the first one holding <c>desk</c> or <c>drawers</c>, and says how many there were.
/// The only parts are <c>desk</c> and <c>drawers</c>; the drawers part holds rules and nothing else, so a chat can name no file and no path.
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

        var candidates = Blocks(raw)
            .OfType<JsonObject>()
            .Where(o => o.ContainsKey("desk") || o.ContainsKey("drawers"))
            .ToList();
        if (candidates.Count == 0)
            return new(null, null, [],
                [new DeskReason("design", "no Sanduhr block found; ask the chat for one JSON block with a desk part, a drawers part or both")], 0);
        var first = candidates[0];

        var reasons = new List<DeskReason>();
        foreach (var (key, _) in first)
            if (key is not ("desk" or "drawers"))
                reasons.Add(new DeskReason(Clip(key, 24), "is not a part of the answer; the parts are desk and drawers"));

        JsonNode? desk = null;
        if (first.ContainsKey("desk"))
        {
            if (first["desk"] is JsonObject d) desk = d.DeepClone();
            else reasons.Add(new DeskReason("desk", "must be an object"));
        }

        List<DrawerRule>? rules = null;
        var names = new List<string>();
        if (first.ContainsKey("drawers"))
        {
            if (first["drawers"] is not JsonObject dobj)
                reasons.Add(new DeskReason("drawers", "must be { \"rules\": [ ... ] }"));
            else
            {
                foreach (var (key, _) in dobj)
                    if (key != "rules") reasons.Add(new DeskReason("drawers." + Clip(key, 24), "is not part of drawers; it holds rules only"));
                if (dobj["rules"] is not JsonArray arr || arr.Count == 0)
                    reasons.Add(new DeskReason("drawers.rules", $"must be a list of 1 to {MaxRules} rules"));
                else if (arr.Count > MaxRules)
                    reasons.Add(new DeskReason("drawers.rules", $"has {arr.Count} rules; at most {MaxRules}"));
                else
                {
                    rules = [];
                    for (int i = 0; i < arr.Count; i++)
                        if (Rule(arr[i], $"drawers.rules[{i}]", reasons) is { } rule) { rules.Add(rule.Rule); names.Add(rule.Name); }
                }
            }
        }

        // Nothing applies partly: a part that had a problem hands back nothing of itself.
        if (reasons.Count > 0) rules = null;
        return new(desk, rules, reasons.Count > 0 ? [] : names, [.. reasons.Take(40)], candidates.Count);
    }

    private static (DrawerRule Rule, string Name)? Rule(JsonNode? node, string at, List<DeskReason> reasons)
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
            else if (text.IndexOfAny(['\\', '/', ':']) >= 0 || text.EnumerateRunes().Any(MessageProposal.IsControl))
                reasons.Add(new DeskReason(at + ".text", "must be part of a file name, not a path"));
        }

        int days = 30;
        if (kind == DrawerRuleKind.OlderThan)
        {
            if (WholeNumber(o["days"]) is { } n && n is >= 1 and <= MaxDays) days = n;
            else reasons.Add(new DeskReason(at + ".days", $"must be a whole number from 1 to {MaxDays}"));
        }

        if (reasons.Count > before) return null;
        return (new DrawerRule(kind, DrawerIds.From(name!), On: true, Text: text, Days: days), name!);
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

    /// <summary>Every top-level JSON object in the paste, in order: a scan for balanced braces outside strings, each parsed alone.
    /// A span that does not parse is skipped and the scan goes on from the next character, so a prose brace before the real block is harmless.</summary>
    private static IEnumerable<JsonNode> Blocks(string raw)
    {
        int misses = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '{') continue;
            if (misses > MaxMisses) yield break;
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
            JsonNode? node = null;
            try { node = JsonNode.Parse(raw[i..(end + 1)]); }
            catch (JsonException) { }
            if (node is null) { misses++; continue; }
            yield return node;
            i = end;
        }
    }
}
