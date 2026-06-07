using System.Windows.Media;

namespace Sanduhr.App.Views;

/// <summary>
/// Stable per-account color palette for the History tab's all-accounts overlay,
/// ported from <c>history_chart.ACCOUNT_COLORS</c> / <c>color_for_account</c>.
/// Colors are keyed on an account's position in the registry list so they stay
/// stable across refreshes; the palette cycles after the 5th account (most users
/// have ≤2 — Personal + Work).
/// </summary>
public static class AccountColors
{
    private static readonly Color[] Palette =
    {
        Hex("#7DD3FC"), // sky-300
        Hex("#FCA5A5"), // red-300
        Hex("#86EFAC"), // green-300
        Hex("#C4B5FD"), // violet-300
        Hex("#FDE68A"), // yellow-300
    };

    /// <summary>The color assigned to <paramref name="label"/> given the registry
    /// order in <paramref name="accounts"/>. Unknown labels fall back to the first
    /// palette entry (parity with the Python guard).</summary>
    public static Color ColorFor(string label, IReadOnlyList<string> accounts)
    {
        int idx = accounts is null ? -1 : IndexOf(accounts, label);
        if (idx < 0)
            return Palette[0];
        return Palette[idx % Palette.Length];
    }

    /// <summary>A frozen brush for the legend swatch.</summary>
    public static Brush BrushFor(string label, IReadOnlyList<string> accounts)
    {
        var b = new SolidColorBrush(ColorFor(label, accounts));
        b.Freeze();
        return b;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
