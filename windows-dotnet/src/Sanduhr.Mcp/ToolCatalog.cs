using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog: five read-only tools. (The design review's must-fix
/// #3 still holds — no strict-subset tools like get_reset_schedule; the two
/// abilities-wave additions answer questions get_usage does NOT: per-model
/// attribution and durable daily history.) Descriptions carry the behavioral
/// trigger — without it the feature never fires. No tool accepts free-form
/// paths or globs: params are closed enums and booleans (the file-oracle
/// guard, must-fix #11).
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
            "get_model_usage",
            "Which models are burning the budget: per-model local Claude Code token totals over " +
            "a window, joined with each model's weekly meter utilization from the live snapshot. " +
            "Use when choosing between models for a big job - a model whose per-model weekly " +
            "meter is hot is the one to avoid. Token counts are a proxy (input+output, cache " +
            "excluded), never convertible to quota %. Scoped to consented Claude Code homes.",
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
                },
                ["additionalProperties"] = false,
            }),
        Tool(
            "get_usage_history",
            "Daily local Claude Code usage history from Sanduhr's durable vault (survives Claude " +
            "Code's ~30-day log cleanup): tokens per day with sent/received split, window totals, " +
            "top projects. Trend and attribution data, not quota - use get_usage for headroom. " +
            "Days without a record are omitted, never fabricated as zero. Scoped to consented " +
            "Claude Code homes.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["window_days"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JsonArray { 7, 30, 90 },
                        ["description"] = "Lookback window in days. Default 30.",
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
