namespace Sanduhr.Core;

/// <summary>The usage sources Sanduhr can track. Colors evoke each brand without copying its hex (Store 10.1.4.4).</summary>
public static class AgentSources
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public static readonly IReadOnlyList<string> All = [Claude, Codex];

    public static string Name(string id) => id switch
    {
        Claude => "Claude",
        Codex => "Codex",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static string DefaultColor(string id) => id switch
    {
        Claude => "#E0875A",
        Codex => "#2FB5A3",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static int Order(string id)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] == id) return i;
        return int.MaxValue;
    }
}
