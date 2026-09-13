using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog: five read-only tools plus <c>publish_usage</c>, the
/// one non-read-only tool (it hands a request to the widget, which owns the key
/// and the network; this server still posts nothing itself). The design review's
/// must-fix #3 still holds: no strict-subset tools like get_reset_schedule; the
/// abilities-wave additions answer questions get_usage does NOT (per-model
/// attribution, durable daily history). Descriptions carry the behavioral
/// trigger; without it the feature never fires. No tool accepts free-form paths
/// or globs: params are closed enums, booleans, and one strict-format date (the
/// file-oracle guard, must-fix #11).
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
                        ["enum"] = new JsonArray(1, 7, 30),
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
                        ["enum"] = new JsonArray(7, 30, 90),
                        ["description"] = "Lookback window in days. Default 30.",
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
            readOnly: false),
        Tool(
            "propose_theme",
            "Give the Sanduhr widget a new color theme. Call when the user asks for a theme, a " +
            "new look, or colors from an image, a palette, or a vibe. Write the palette per " +
            "docs/themes/AGENT_PROMPT.md: fourteen role-named #rrggbb colors (bg, glass, " +
            "glass_on_mica, title_bg, border, footer_bg, bar_bg, text, text_secondary, text_dim, " +
            "text_muted, accent, pace_marker, sparkline) plus name and optional dials (glass_alpha " +
            "0.7-0.9, border_alpha 0.2-0.6, border_tint, accent_bloom, inner_highlight). Rules: dark " +
            "base (bg/glass luminance under 0.25); text at 4.5:1 or better on the card; text_secondary, " +
            "text_dim, text_muted the same hue as text at decreasing luminance; one accent hue shared " +
            "by accent, sparkline and border_tint; pace_marker visible on every bar fill (a " +
            "complementary hue works). The server lints first: status rejected with findings means " +
            "fix the named fields and call again, nothing was written. Otherwise the widget saves the " +
            "theme under its themes folder and applies it (apply: false saves without switching); the " +
            "result names the previous theme's key so the user can go back, and carries any warnings.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["theme"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "The palette JSON: name, the fourteen #rrggbb colors, optional dials.",
                    },
                    ["save_as"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["pattern"] = "^[a-z0-9][a-z0-9-]{0,39}$",
                        ["description"] = "File key under the themes folder. Default: the name, slugged.",
                    },
                    ["apply"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Apply after saving. Default true.",
                    },
                },
                ["required"] = new JsonArray("theme"),
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
