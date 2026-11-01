using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// The prompt Design it with an AI chat copies (setup spec, section 3). It lists only what this build's Desk can do, and every word,
/// range and limit it teaches is read from the Core types the validator reads (<see cref="DeskArrangement"/>, <see cref="DeskAnchors"/>,
/// <see cref="DeskMeterStyles"/>, <see cref="DeskInkKind"/>, <see cref="MessageProposal"/>, <see cref="PastedSetup"/>), so the prompt and
/// <see cref="DeskDesign.Parse"/> cannot drift. It carries the monitors by name only, never an id, and nothing personal: no file names,
/// account, usage or paths. It names no product.
/// </summary>
public static class SetupPrompt
{
    /// <summary>The pieces a Windows Desk does not draw yet, though the layout grammar knows the word (the Mac draws the meetings
    /// piece). The prompt does not teach them. A test pins this list against what <c>DeskView</c> draws.</summary>
    public static readonly IReadOnlyList<string> NotDrawn = ["meetings"];

    /// <summary>The pieces the prompt lists: the layout's known words that this build draws.</summary>
    public static IReadOnlyList<string> Pieces { get; } =
        [.. DeskArrangement.Known.Where(p => !NotDrawn.Contains(p, StringComparer.Ordinal))];

    /// <summary>The design fields the prompt teaches. Every one is a field <see cref="DeskDesign.Parse"/> reads. The launcher (its buttons
    /// need ids from this PC's Start menu), song looks and a whole theme are not for a chat that cannot see this PC.</summary>
    public static readonly IReadOnlyList<string> DeskFields =
        ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow", "messages"];

    /// <summary>The ink words (<see cref="DeskInkKind"/> but the custom colors), as the validator spells them.</summary>
    public static IReadOnlyList<string> InkWords { get; } =
        [.. Enum.GetValues<DeskInkKind>().Where(k => k != DeskInkKind.Custom).Select(k => k.ToString().ToLowerInvariant())];

    private static readonly Dictionary<string, string> PieceNotes = new(StringComparer.Ordinal)
    {
        ["message"] = "a short line of text that changes through the day",
        ["clock"] = "the time and date",
        ["claude"] = "a compact line of AI usage pace",
        ["meters"] = "the AI usage limits as meters",
        ["nowPlaying"] = "the song that is playing",
        ["telemetry"] = "processor, memory and disk meters",
        ["weather"] = "the forecast",
        ["watchers"] = "cards for long jobs, shown only while something is being watched",
        ["launcher"] = "a row of buttons (placement only, a chat cannot set its buttons)",
        ["glow"] = "a soft light that comes on when something happens",
        ["companion"] = "a small character that shows only when a sprite is installed",
    };

    /// <summary>What the prompt says a piece is; empty for a word with no note (a test fails when a drawn piece has none).</summary>
    public static string NoteFor(string piece) => PieceNotes.GetValueOrDefault(piece, "");

    public static string Build(IReadOnlyList<string> monitorNames)
    {
        static string N(double v) => v.ToString("0.0#", CultureInfo.InvariantCulture);
        static string Degrees(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');

        Line("You are helping me set up Sanduhr, a desktop app for Windows that puts a clock, a message, the weather and other");
        Line("small pieces on my desktop, and can sort my desktop files into drawers.");
        Line();
        Line("Ask me a few short questions first, one at a time: the look I like, colors, which pieces I want and where,");
        Line("and what kinds of files I would like sorted into drawers. Then answer with exactly one JSON block and nothing");
        Line("after it. I will paste it back into Sanduhr, which checks it and shows me a preview before anything changes.");
        Line();
        Line("The block has two parts, both optional, and no other part:");
        Line("{ \"desk\": { ... }, \"drawers\": { \"rules\": [ ... ], \"look\": ..., \"opacity\": ..., \"arrange\": { ... } } }");
        Line();
        Line("\"desk\" is a partial design: send only what should change. Use only these fields; any other field is refused.");
        Line($"- name: up to {DeskDesign.NameMax} characters. note: one sentence for me, up to {DeskDesign.NoteMax}.");
        Line("- layout: { piece: { anchor, size, hidden, monitor, tilt, style } }, every member optional. Pieces:");
        foreach (var piece in Pieces)
            Line($"  {piece}: {NoteFor(piece)}.");
        Line("  Each piece takes these members:");
        Line("  anchor: " + string.Join(", ", DeskAnchors.All.Select(DeskAnchors.Word))
            + " (t top, m middle, b bottom; l left, c center, r right). Pieces at one anchor stack top to bottom.");
        Line($"  size: a number from {N(DeskArrangement.MinScale)} to {N(DeskArrangement.MaxScale)}, 1 is the piece's own size.");
        Line("  hidden: true removes the piece; send nothing else for it.");
        Line("  monitor: all or main.");
        Line($"  tilt: whole degrees from {Degrees(DeskArrangement.MinTilt)} to {Degrees(DeskArrangement.MaxTilt)}, the slant of the whole stack at that anchor, so pieces at one anchor must share one tilt.");
        Line("  style: " + string.Join(", ", DeskMeterStyles.All.Select(DeskMeterStyles.Word)) + ", only on " + string.Join(", ", DeskMeterStyles.Pieces) + ".");
        Line("- ink: " + string.Join(", ", InkWords) + ", one #rrggbb color, or a list of two to four #rrggbb colors for a gradient.");
        Line("  Sanduhr refuses a color that would be hard to read on my wallpaper, so prefer auto unless I ask for colors.");
        Line($"- font: a font family name that comes with Windows, up to {DeskDesign.FontFamilyMax} characters, letters, digits and spaces. Leave it out if unsure.");
        Line("- plate: true or false, a soft panel behind each stack of pieces. shadow: true or false.");
        Line($"- messages: {{ lines: [ ... ], mode }}. mode: {DeskDesign.Word(MessageProposalMode.Add)} (join my list) or {DeskDesign.Word(MessageProposalMode.Replace)} (replace it).");
        Line($"  1 to {MessageProposal.MaxLines} lines, each plain text of at most {MessageProposal.MaxLineChars} characters on one line, with no curly braces and no colon, not starting with #.");
        Line($"- weather: {{ nickname }}, a name of up to {DeskDesign.WeatherNicknameMax} characters shown in place of the city's name.");
        Line();
        Line("\"drawers\" starts with rules, never file moves: you cannot see my files and you never name one. Each rule sends the");
        Line("desktop files it matches to a drawer, a folder Sanduhr makes for me. A rule is { kind, drawer, text, days }:");
        Line("- kind: " + string.Join(", ", PastedSetup.KindWords) + ".");
        Line($"- drawer: a short folder name of up to {DrawerNames.MaxLength} characters, a name and never a path.");
        Line($"- text: for {PastedSetup.KindWord(DrawerRuleKind.NameContains)} and {PastedSetup.KindWord(DrawerRuleKind.NameStartsWith)}, part of a file name, up to {PastedSetup.MaxTextChars} characters.");
        Line($"- days: for {PastedSetup.KindWord(DrawerRuleKind.OlderThan)}, a whole number from 1 to {PastedSetup.MaxDays}.");
        Line($"1 to {PastedSetup.MaxRules} rules; leave drawers out to keep my rules as they are. The first rule that matches a file wins, and a file no rule matches stays on the desktop.");
        Line("\"drawers\" can also set how drawers look and line up. Send any of these, and leave out the rest:");
        Line("- look: " + string.Join(", ", DrawerStyle.LookWords) + " (solid is a plate, glass a lighter plate, outline a thin frame, bare no frame).");
        Line($"- opacity: a whole number from {DrawerStyle.MinOpacity} to {DrawerStyle.MaxOpacity}, for solid and glass.");
        Line("- arrange: { corner, direction } lines the drawers up from that corner. corner: " + string.Join(", ", DrawerStyle.CornerWords)
            + " (t top, b bottom, l left, r right). direction: " + string.Join(", ", DrawerStyle.FlowWords) + ". Send at least one of the two.");
        Line();
        var names = monitorNames.Select(CleanName).Where(n => n.Length > 0).ToList();
        Line("My monitors, for reference (in the design use only all or main): " + (names.Count == 0 ? "one." : string.Join("; ", names) + "."));
        return sb.ToString().Replace("\n", "\r\n");
    }

    /// <summary>A monitor name as the prompt shows it: one line, no control characters, cut to 60 characters.</summary>
    private static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var rune in (name ?? "").EnumerateRunes())
            sb.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        var t = sb.ToString().Trim();
        return t.Length <= 60 ? t : t[..60].TrimEnd();
    }
}
