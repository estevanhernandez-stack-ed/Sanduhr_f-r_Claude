using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog. Three read-only tools (design review must-fix #3:
/// get_reset_schedule was killed as a wrong-single-call subset of get_usage)
/// plus <c>publish_usage</c>, the one non-read-only tool — it hands a request
/// to the widget, which owns the key and the network; this server still posts
/// nothing itself. Descriptions carry the behavioral trigger — without it the
/// feature never fires. No tool accepts free-form paths or globs: params are
/// closed enums, booleans, and one strict-format date (the file-oracle guard,
/// must-fix #11).
/// </summary>
public static class ToolCatalog
{
    // Elements go through JsonArray's params ctor, never a collection initializer:
    // the initializer binds to the generic Add<T>, which asks the serializer for
    // type metadata that the trimmed single-file publish removes (tools/list then
    // throws NotSupportedException on the first int). Same rule everywhere a
    // JsonArray is filled in this project; the server's publish treats linker
    // warnings as errors so a regression fails the build, not the user.
    public static JsonArray Build() => new JsonArray(
        Tool(
            "get_usage",
            "Check Claude subscription quota headroom (the Sanduhr widget's live snapshot of " +
            "claude.ai usage, plus local Claude Code burn since that snapshot). Call BEFORE " +
            "spawning subagents, launching long autonomous runs, or choosing a bigger model " +
            "for a large job. Reflects the Sanduhr widget's ACTIVE account, which may not be " +
            "the account this session bills to - confirm with the user if they run multiple " +
            "accounts. A stale or no_data status means unknown headroom - never assume budget. " +
            "sanduhr-mcp requires widget " + ToolLogic.RequiredWidgetVersion + " or later (the " +
            "snapshot writer): with an older widget the reason is widget_too_old, and status " +
            "degraded serves the widget's history file instead (utilization and reset times only).",
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
                        ["enum"] = new JsonArray(1, 7, 30),
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
            "returns no_data, to distinguish server-broken from data-absent. Reports the widget " +
            "version that wrote the snapshot against the floor sanduhr-mcp requires (widget " +
            ToolLogic.RequiredWidgetVersion + " or later), whether the history file is fresh, " +
            "and the same usage_status / usage_reason get_usage would return.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false,
            }),
        Tool(
            "publish_usage",
            "Publish one day's local Claude Code token burn (per project basename, per home " +
            "the user marked shareable in Sanduhr's settings) plus current quota headroom to " +
            "the publish endpoint the user configured in Sanduhr (a URL of their own, or the " +
            "626 Labs dashboard preset). Call when the user asks to publish, push, or sync " +
            "usage, or when their destination is missing a day. The Sanduhr widget performs " +
            "the upload (it holds the token); this server only queues the request and returns " +
            "the widget's typed result. Refuses with status disabled / no_endpoint / no_token " +
            "when publishing is off, no publish endpoint is set, or no publish token is stored " +
            "for a scheme that needs one - the remedy names the setting. Re-publishing the " +
            "same day sends that day's record again.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["date"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["pattern"] = "^\\d{4}-\\d{2}-\\d{2}$",
                        ["description"] = "Local calendar day to publish, YYYY-MM-DD. Default: yesterday.",
                    },
                },
                ["additionalProperties"] = false,
            },
            readOnly: false));

    private static JsonObject Tool(string name, string description, JsonObject inputSchema, bool readOnly = true) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = readOnly,
            ["destructiveHint"] = false,
            ["openWorldHint"] = !readOnly,
        },
    };
}
