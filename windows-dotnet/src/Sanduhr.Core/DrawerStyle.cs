namespace Sanduhr.Core;

/// <summary>How drawers blend into the desktop (drawers look and placement spec, section 1).</summary>
public enum DrawerLookKind { Solid, Glass, Outline, Bare }

/// <summary>Where Tidy lines drawers up, and which way (spec, section 2).</summary>
public enum DrawerCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public enum DrawerFlow { Column, Row }

public sealed record DrawerTidy(DrawerCorner Corner, DrawerFlow Flow)
{
    /// <summary>Top right, a column down: where drawers have always arrived.</summary>
    public static DrawerTidy Default { get; } = new(DrawerCorner.TopRight, DrawerFlow.Column);
}

/// <summary>The words settings.json and an AI chat use for looks, corners and directions, and the opacity range.</summary>
public static class DrawerStyle
{
    public const int MinOpacity = 20;
    public const int MaxOpacity = 100;
    /// <summary>Today's drawer plate (<c>DrawerLook.MinPlateOpacity</c>, 0.55): an upgrade looks the same.</summary>
    public const int DefaultOpacity = 55;

    public static IReadOnlyList<string> LookWords { get; } = ["solid", "glass", "outline", "bare"];
    public static IReadOnlyList<string> CornerWords { get; } = ["tl", "tr", "bl", "br"];
    public static IReadOnlyList<string> FlowWords { get; } = ["column", "row"];

    public static string Word(DrawerLookKind k) => LookWords[(int)k];
    public static DrawerLookKind? Look(string? w) => Index(LookWords, w) is { } i ? (DrawerLookKind)i : null;
    public static string Word(DrawerCorner c) => CornerWords[(int)c];
    public static DrawerCorner? Corner(string? w) => Index(CornerWords, w) is { } i ? (DrawerCorner)i : null;
    public static string Word(DrawerFlow f) => FlowWords[(int)f];
    public static DrawerFlow? Flow(string? w) => Index(FlowWords, w) is { } i ? (DrawerFlow)i : null;

    /// <summary>Solid and Glass draw a plate; Outline and Bare draw none (the opacity setting hides for them).</summary>
    public static bool HasFill(DrawerLookKind k) => k is DrawerLookKind.Solid or DrawerLookKind.Glass;

    private static int? Index(IReadOnlyList<string> words, string? w)
    {
        for (int i = 0; i < words.Count; i++) if (string.Equals(words[i], w, StringComparison.Ordinal)) return i;
        return null;
    }
}
