using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog. Exactly three tools (design review must-fix #3:
/// get_reset_schedule was killed as a wrong-single-call subset of get_usage).
/// Descriptions carry the behavioral trigger — without it the feature never
/// fires. No tool accepts free-form paths or globs: params are closed enums and
/// booleans (the file-oracle guard, must-fix #11).
/// </summary>
public static class ToolCatalog
{
    public static JsonArray Build() => new()
    {
        Tool(
            "get_usage",
            "Check Claude subscription quota headroom (the Sanduhr widget's live snapshot of " +
            "claude.ai usage, plus local Claude Code burn since that snapshot). Call BEFORE " +
            "spawning subagents, launching long autonomous runs, or choosing a bigger model " +
            "for a large job. Reflects the Sanduhr widget's ACTIVE account, which may not be " +
            "the account this session bills to - confirm with the user if they run multiple " +
            "accounts. A stale or no_data status means unknown headroom - never assume budget.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false,
            }),
        Tool(
            "get_local_burn_by_project",
            "Attribute recent local Claude Code token burn to projects: which project ate the " +
            "tokens, keyed per Claude Code home. Attribution only - token counts are a proxy " +
            "(input+output, cache excluded) and are NOT convertible to quota percentages; use " +
            "get_usage for headroom. Scoped to the Claude Code homes the user consented to in " +
            "Sanduhr's settings; every response names the roots it covered.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["window_days"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JsonArray { 1, 7, 30 },
                        ["description"] = "Lookback window. Default 7.",
                    },
                    ["full_paths"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Return full project paths instead of basenames. Default false.",
                    },
                },
                ["additionalProperties"] = false,
            }),
        Tool(
            "ping",
            "Health check and verify anchor: server version, snapshot presence and age, and " +
            "which Claude Code roots exist vs are consented. Call this first when get_usage " +
            "returns no_data, to distinguish server-broken from data-absent.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false,
            }),
    };

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = true,
            ["openWorldHint"] = false,
        },
    };
}
