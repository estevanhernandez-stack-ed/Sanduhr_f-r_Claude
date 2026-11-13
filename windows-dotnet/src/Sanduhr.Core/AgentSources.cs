namespace Sanduhr.Core;

/// <summary>The usage sources Sanduhr can track. Colors evoke each brand without copying its hex (Store 10.1.4.4).</summary>
public static class AgentSources
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Antigravity = "antigravity";

    /// <summary>Limits sources: widget sections, primary, Desk meters, setup ticks.</summary>
    public static readonly IReadOnlyList<string> All = [Claude, Codex];

    /// <summary>Activity-only sources (spec 3): local burn in the Vault, never limits.</summary>
    public static readonly IReadOnlyList<string> ActivityOnly = [Antigravity];

    /// <summary>Every source whose local burn the Vault can hold, in ledger and footer order.</summary>
    public static readonly IReadOnlyList<string> Burn = [Claude, Codex, Antigravity];

    public static string Name(string id) => id switch
    {
        Claude => "Claude",
        Codex => "Codex",
        Antigravity => "Antigravity",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    /// <summary>The name a local-burn surface uses: Claude's burn is Claude Code's.</summary>
    public static string BurnName(string id) => id == Claude ? "Claude Code" : Name(id);

    /// <summary>The footer's short label ("CC +12K  ·  Codex +3K  ·  Agy +1K").</summary>
    public static string ShortName(string id) => id switch
    {
        Claude => "CC",
        Codex => "Codex",
        Antigravity => "Agy",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static string DefaultColor(string id) => id switch
    {
        Claude => "#E0875A",
        Codex => "#2FB5A3",
        Antigravity => "#7B8CF0",   // a periwinkle that evokes Google's blue without copying it (Store 10.1.4.4)
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static int Order(string id)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] == id) return i;
        return int.MaxValue;
    }
}
