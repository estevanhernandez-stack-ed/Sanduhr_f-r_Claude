using System.Globalization;

namespace Sanduhr.Core;

/// <summary>
/// Compact human-readable token counts for badge + breakdown labels, ported 1:1
/// from <c>tiers._format_tokens_compact</c>. Shared by the live tier-card
/// "+Nk" Local CC delta badge and the Local CC settings view so the two never
/// drift on rounding. Pure — no WPF, no IO.
/// </summary>
public static class TokenFormat
{
    /// <summary>
    /// <c>1499 → "1.5k"</c>, <c>12345 → "12k"</c>, <c>1_234_567 → "1.2M"</c>,
    /// <c>999 → "999"</c>. Mirrors the Python ladder exactly: millions with one
    /// decimal, ten-thousands+ as integer-thousands, thousands with one decimal,
    /// and the bare integer below 1000.
    /// </summary>
    public static string Compact(long n)
    {
        if (n >= 1_000_000)
            return (n / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        if (n >= 10_000)
            return (n / 1_000).ToString(CultureInfo.InvariantCulture) + "k";
        if (n >= 1_000)
            return (n / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
        return n.ToString(CultureInfo.InvariantCulture);
    }
}
