using System.Globalization;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// The name a new account is offered and the rule a typed one passes (Settings v2, slice 5; WF30). The first account is offered
/// "Personal", later ones the next free "Account N"; every sign-in asks, and a cancelled, invalid or taken label keeps the offer,
/// so a captured sign-in is never thrown away. The rule is <see cref="AccountStore"/>'s label check, plus not blank.
/// </summary>
public static class AccountLabels
{
    /// <summary>The first account's offered label.</summary>
    public const string First = "Personal";

    private static readonly Regex Valid = new("^[A-Za-z0-9 _-]{1,32}$", RegexOptions.Compiled);

    /// <summary>"Personal" with no account yet, else the first "Account N" (from 2) no account uses, in any case.</summary>
    public static string Suggested(IReadOnlyCollection<string> existing)
    {
        if (existing.Count == 0)
            return First;
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int n = 2; ; n++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "Account {0}", n);
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>1 to 32 letters, digits, spaces, underscores or hyphens, and not blank.</summary>
    public static bool IsValid(string? label) =>
        label is not null && !string.IsNullOrWhiteSpace(label) && Valid.IsMatch(label);

    /// <summary>The label a sign-in saves: <paramref name="typed"/> trimmed when it is valid and no account uses it (in any
    /// case), else <paramref name="suggested"/>.</summary>
    public static string Accept(string? typed, string suggested, IReadOnlyCollection<string> existing)
    {
        var name = (typed ?? "").Trim();
        return IsValid(name) && !existing.Contains(name, StringComparer.OrdinalIgnoreCase) ? name : suggested;
    }
}
