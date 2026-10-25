using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog: seven read-only tools (the five usage tools,
/// <c>get_desk_messages</c> and <c>get_desk</c>) plus the tools that hand a request file to the widget
/// and never act themselves: <c>publish_usage</c> (the widget owns the key and the
/// network; this server posts nothing), <c>propose_theme</c>,
/// <c>propose_desk_messages</c>, <c>propose_desk</c> and <c>propose_now_playing_looks</c> (3.7.0) and <c>watch_start</c> / <c>watch_update</c> /
/// <c>watch_end</c> (3.6.0; the widget owns messages.txt and the board, checks every
/// request again, and the user's switches gate them). The design review's
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
            "tokens, keyed per Claude Code folder. Attribution only - token counts are a proxy " +
            "(input+output, cache excluded) and are NOT convertible to quota percentages; use " +
            "get_usage for headroom. Scoped to the Claude Code folders the user consented to in " +
            "Sanduhr (Settings, Claude Code, Folders); every response names the roots it covered.",
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
            "excluded), never convertible to quota %. Scoped to consented Claude Code folders.",
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
            "Daily local Claude Code usage history from Sanduhr's durable session archive (the vault; survives Claude " +
            "Code's ~30-day log cleanup): tokens per day with sent/received split, window totals, " +
            "top projects. Trend and attribution data, not quota - use get_usage for headroom. " +
            "Days without a record are omitted, never fabricated as zero. Scoped to consented " +
            "Claude Code folders.",
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
            readOnly: false),
        Tool(
            "get_desk_messages",
            DeskGuideRead,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false,
            }),
        Tool(
            "propose_desk_messages",
            DeskGuidePropose,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["lines"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string", ["maxLength"] = 120 },
                        ["minItems"] = 1,
                        ["maxItems"] = 60,
                        ["description"] = "The lines, exactly as they would sit in messages.txt (prefix first, then effects, then the text).",
                    },
                    ["mode"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("add", "replace"),
                        ["description"] = "add appends the lines (lines already in the list are skipped); replace swaps the whole list for them (the comment block at the top of the file stays). Default add.",
                    },
                    ["note"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 300,
                        ["description"] = "One short sentence for the user on why these lines (shown beside the suggestion).",
                    },
                },
                ["required"] = new JsonArray("lines"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "get_desk",
            DeskDesignGuideRead,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false,
            }),
        Tool(
            "propose_desk",
            DeskDesignGuidePropose,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["design"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "A partial Desk design: only the fields to change. See the tool description for each field.",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = new JsonObject { ["type"] = "string", ["maxLength"] = 40 },
                            ["note"] = new JsonObject { ["type"] = "string", ["maxLength"] = 200 },
                            ["layout"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "Piece key (from get_desk pieces) to {anchor: tl|tc|tr|ml|mr|bl|bc|br, size: 0.6-1.6, hidden: boolean, monitor: all|main|a monitor id from get_desk monitors, tilt: -20 to 20 degrees, the slant of the piece's whole stack, style: bar|slim|ring|ticks, only on claude, meters and telemetry}.",
                            },
                            ["weather"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "{nickname: up to 24 characters}: the name the weather piece shows instead of the place's own; \"\" goes back to the place's name. Local only; the forecast keeps the city's coordinates.",
                                ["properties"] = new JsonObject
                                {
                                    ["nickname"] = new JsonObject { ["type"] = "string", ["maxLength"] = 24 },
                                },
                                ["additionalProperties"] = false,
                            },
                            ["font"] = new JsonObject
                            {
                                ["description"] = "A font family name, or {family, link} with an https link to get a font the user lacks.",
                            },
                            ["ink"] = new JsonObject
                            {
                                ["description"] = "auto, light, dark, theme, one #rrggbb color, or a list of 2 to 4 #rrggbb colors (gradient).",
                            },
                            ["plate"] = new JsonObject { ["type"] = "boolean" },
                            ["shadow"] = new JsonObject { ["type"] = "boolean" },
                            ["launcher"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "{orientation: row|column, buttons: up to 8 of {kind: app|link, target, label, glow}}. An app target is an id from get_desk start_menu_apps; a link is https.",
                            },
                            ["messages"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "{lines: [...], mode: add|replace}, the lines as propose_desk_messages takes them.",
                            },
                            ["looks"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "Per-song looks for the now playing line, up to 20 of {title, artist, ink, style, mood}, as propose_now_playing_looks takes them.",
                            },
                            ["theme"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "A widget palette as propose_theme takes it: name and the fourteen #rrggbb colors.",
                            },
                        },
                        ["additionalProperties"] = false,
                    },
                    ["name"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 40,
                        ["description"] = "The design's name (overrides design.name).",
                    },
                    ["note"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 200,
                        ["description"] = "One short sentence for the user on why this look (shown in the review bar; overrides design.note).",
                    },
                },
                ["required"] = new JsonArray("design"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "watch_start",
            WatchGuideStart,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["title"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 80,
                        ["description"] = "What is being watched, one line (\"PR 140 CI: combine statuslines\").",
                    },
                    ["short"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 12,
                        ["description"] = "The resting name, up to 12 characters: \"PR 140\", \"v2.8.0\", \"deploy\". Pass one; without it Sanduhr derives one from the title.",
                    },
                    ["link"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 2048,
                        ["description"] = "An https link to the run's page.",
                    },
                    ["total"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                        ["maximum"] = 1000000,
                        ["description"] = "How many steps there are, for done/total.",
                    },
                    ["work"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Work for the user's employer: hidden in demo mode. Default false.",
                    },
                },
                ["required"] = new JsonArray("title"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "watch_update",
            WatchGuideUpdate,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["id"] = WatchIdSchema(),
                    ["done"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["maximum"] = 1000000,
                        ["description"] = "How many are done.",
                    },
                    ["note"] = WatchNoteSchema(),
                    ["state"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("running", "waiting"),
                        ["description"] = "waiting: on the user.",
                    },
                },
                ["required"] = new JsonArray("id"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "watch_end",
            WatchGuideEnd,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["id"] = WatchIdSchema(),
                    ["result"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("passed", "failed"),
                    },
                    ["note"] = WatchNoteSchema(),
                },
                ["required"] = new JsonArray("id", "result"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "propose_now_playing_looks",
            LooksGuide,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["looks"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["maxItems"] = 20,
                        ["description"] = "One look per song.",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["title"] = new JsonObject { ["type"] = "string", ["maxLength"] = 200 },
                                ["artist"] = new JsonObject { ["type"] = "string", ["maxLength"] = 100 },
                                ["ink"] = new JsonObject
                                {
                                    ["type"] = "array",
                                    ["minItems"] = 1,
                                    ["maxItems"] = 4,
                                    ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$" },
                                    ["description"] = "1 to 4 hex colors, left to right, each 3:1 or better on what is behind the now playing piece.",
                                },
                                ["style"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["enum"] = new JsonArray("plain", "bold", "italic", "bolditalic", "smallcaps"),
                                },
                                ["mood"] = new JsonObject { ["type"] = "string", ["maxLength"] = 40, ["description"] = "A few words for the user (\"neon night drive\")." },
                            },
                            ["required"] = new JsonArray("title", "ink"),
                            ["additionalProperties"] = false,
                        },
                    },
                    ["note"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 200,
                        ["description"] = "One short sentence for the user on why these looks (shown beside the suggestion).",
                    },
                },
                ["required"] = new JsonArray("looks"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false),
        Tool(
            "propose_widget",
            WidgetGuide,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("sprite", "palette", "effects"),
                        ["description"] = "What the pack is. A widget theme goes through propose_theme instead; fonts are never proposed.",
                    },
                    ["pack"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "pack.json's own fields. Sanduhr writes files, kind, schema_version and sanduhr_min, and the LICENSE text.",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[a-z][a-z0-9]*(-[a-z0-9]+)*$", ["description"] = "Lowercase kebab-case; Sanduhr prefixes my- (3 to 40 characters with it)." },
                            ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 40 },
                            ["description"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 },
                            ["version"] = new JsonObject { ["type"] = "string", ["description"] = "MAJOR.MINOR.PATCH, default 1.0.0." },
                            ["author"] = new JsonObject { ["type"] = "string", ["maxLength"] = 80, ["description"] = "Default: Made here. Never a brand or model name." },
                            ["license"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray("CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"),
                                ["description"] = "Default CC0-1.0. Sanduhr writes the matching LICENSE.",
                            },
                        },
                        ["required"] = new JsonArray("id", "name", "description"),
                        ["additionalProperties"] = false,
                    },
                    ["sprite"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "kind sprite: sprite.json (schema_version 2): frame_w, frame_h, scale, fps, sheet or stages, animations, moods, triggers, roam. Every sheet path ends .json and is a key of sheets.",
                    },
                    ["sheets"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "kind sprite: pixel sheets, path (such as sheet.json or sprites/egg.json) to {palette, frame_w, frame_h, frames}.",
                    },
                    ["palettes"] = new JsonObject
                    {
                        ["description"] = "kind palette: palettes.json, {schema_version: 1, palettes: [...]} or the list alone.",
                    },
                    ["effects"] = new JsonObject
                    {
                        ["description"] = "kind effects: effects.json, {schema_version: 1, effects: [...]} or the list alone.",
                    },
                    ["note"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = 200,
                        ["description"] = "One short sentence for the user on what you made and why (shown on the review bar).",
                    },
                },
                ["required"] = new JsonArray("kind", "pack"),
                ["additionalProperties"] = false,
            },
            readOnly: false, openWorld: false));

    // The Desk and watcher descriptions are the Mac server's, adapted to this machine's paths and
    // words. Plain ASCII, like every description here.

    private const string DeskSyntax =
        "messages.txt syntax, one line each: a plain line shows on any day; 'Mon: text' only on that " +
        "weekday (Mon Tue Wed Thu Fri Sat Sun, exactly so); '10-31: text' only on that date (MM-DD); " +
        "'# text' is a comment and blank lines are ignored. Each day the Desk shows one usual line: " +
        "today's weekday lines if there are any (they replace the plain lines that day, unless the " +
        "user's mix_daily switch is on, which lets the plain lines take turns with them), else the " +
        "plain lines; it rotates through them once a day (a weekday's lines once a week) or once an " +
        "hour (rotate, the user's choice), steady in between. Date lines are special days: they do not " +
        "replace the usual line, they add to it, drawn above it, and every line for that date shows " +
        "(two birthdays on one date both show; more than 3 take turns hourly, 3 at a time). So " +
        "birthdays, anniversaries and holidays are date lines, one per person or occasion, short and " +
        "friendly, like '03-14: {ink:#ff7e5f,#feb47b} happy birthday, Sam.'; the user's everyday line " +
        "still shows under them. Pinning one line is a user setting (pinned): the pinned line replaces " +
        "the usual line every day, so weekday and plain lines don't show while it is pinned (say so " +
        "before proposing them), but date lines still stack above the pinned line, so birthdays and " +
        "holidays show either way. The user's On special days setting (special_mode) decides how the date's " +
        "lines share the Desk with the usual line: stack (all at once, the date's lines on top), turns " +
        "(one at a time, crossfading) or scroll (one at a time, gliding up like a slow ticker), each " +
        "line shown for special_seconds. ";

    private const string DeskEffects =
        "Effects: tags at the start of the text, after any prefix, in any order, each in braces: " +
        "{ink:#ff2a6d,#05d9e8} this line's ink, 1 to 4 hex colors (2 or more make a left-to-right " +
        "gradient); {glow} / {noglow} turn the soft glow on or off for this line; {size:1.3} scales " +
        "the line from 0.5 to 2 times the Desk's message size; {write} draws the line in, left to right " +
        "like handwriting, once when it first appears; {shimmer} sends a slow light sweep across it " +
        "every few seconds; {sweep} runs a three-character light across the line that brightens it " +
        "toward white, once when the line appears, and {sweep:20} every 20 seconds (2 to 3600); " +
        "{font:small-caps} draws the line in a letter style: bold, italic, bold-italic and small-caps " +
        "in the Desk's own font (its weight, a slant, small capitals), sans, mono, " +
        "double-struck, script and fraktur as Unicode math letters (A to Z, a to z and, where the " +
        "style has them, digits; accents and punctuation stay as written). " +
        "Examples: '{ink:#ffd08a} showtime.', 'Fri: {ink:#ff2a6d,#05d9e8} {glow} " +
        "ship it.', '10-31: {ink:#ff7518,#6b2fa0} {write} happy halloween.', '{size:0.8} {noglow} " +
        "breathe.', '{font:small-caps} {sweep} showtime.', '{font:script} {sweep:30} good morning.'. " +
        "Good taste: short lines (handwriting reads best under about 40 characters, two " +
        "lines at most on screen), lowercase and a period suit the hand, {write}, {shimmer} and {sweep} " +
        "sparingly (a few lines, not every line, and one of them per line), {font:...} on short " +
        "lines, gradients with 2 or 3 colors that sit near each other, sizes near 1. With animations " +
        "turned off in Windows, {write} shows at once and {shimmer} and {sweep} are skipped. ";

    private const string DeskGuideRead =
        "Read the user's Desk messages: the handwritten line Sanduhr draws on the Windows desktop, picked " +
        "from %APPDATA%\\Sanduhr\\messages.txt. Returns every raw line of the file " +
        "(comments and effects included), whether one line is pinned, the rotation (daily or hourly), " +
        "mix_daily (whether plain lines take turns with a weekday's own lines), " +
        "today: the usual raw line the Desk shows now, and today_special: the date lines it shows above " +
        "it today (empty on most days). Call this before propose_desk_messages to match " +
        "the user's voice and to avoid repeats. " + DeskSyntax + DeskEffects;

    private const string DeskGuidePropose =
        "Suggest lines for the user's Desk messages. Sanduhr checks them and, unless the user lets " +
        "Claude change the messages directly, shows them as a suggestion to Add, Review or Dismiss: " +
        "pending_approval means the user decides, applied means they are in the list now, rejected " +
        "comes with reasons to fix. This server never writes messages.txt. Limits: 1 to 60 lines, " +
        "120 characters each, no control characters or line breaks inside a line, at least one line " +
        "that is not a comment; prefixes and effects must parse. mode add appends (default), replace " +
        "swaps the whole list (the user's previous list is kept as messages.txt.previous); to add " +
        "birthdays, anniversaries or holidays use mode add with one date line each (MM-DD:), never " +
        "replace, so the user's own lines stay. Read the current list first with get_desk_messages. " +
        DeskSyntax + DeskEffects;

    private const string DeskDesignGuideRead =
        "Read the user's Desk before designing it. The Desk is what Sanduhr draws on the Windows desktop: a clock, Claude " +
        "usage, a handwritten message, a Launcher of buttons, a sprite companion and other pieces, each placed at one of eight anchors " +
        "(tl tc tr ml mr bl bc br) with a size, in a font and an ink, optionally on a soft plate, beside the widget's " +
        "color theme. Returns design (the current look as a full design: layout with every piece's anchor, size, " +
        "hidden, monitor and tilt (the slant of its stack in degrees, 0 upright), and for claude, meters and telemetry its style (bar, slim, ring or ticks; " +
        "limits.piece_style lists the pieces and the words), weather.nickname (the name the weather piece shows " +
        "instead of the place's, \"\" for none), font, ink, plate, shadow, launcher, theme), pieces and anchors (the allowed words), fonts (installed " +
        "families, and the bundled handwriting fonts EsteFont Pro and EsteFont 26), wallpaper (path of a copy of the wallpaper " +
        "image scaled to at most 1600 pixels on its longest side, which you can open only if the user allows Claude Code to read that file; " +
        "source file, or cache when Windows' cached copy stands in for a wallpaper file that was deleted; kind image, or color when the shell " +
        "names no image and the summary is the desktop's background color; and summary: overall " +
        "brightness 0-1, up to five dominant colors with their share, and for each monitor the brightness 0-1 and " +
        "busyness 0-1 (edge density) behind each anchor), designs (saved designs: id, name, pinned, applied_at), " +
        "direct (true when the user lets Claude change the Desk without review), limits, and start_menu_apps (the apps " +
        "a Launcher button may open, as id and name; absent until Sanduhr has read the Start menu). Call this before " +
        "propose_desk and design from what it says. status no_data with reason missing means the Sanduhr widget is not " +
        "running or its Claude Code integration is not installed.";

    private const string DeskDesignGuidePropose =
        "Propose a look for the user's Desk, and optionally the widget's color theme. The design is PARTIAL: send only " +
        "the fields to change; everything absent stays as it is. Fields, all optional: name (up to 40 characters); " +
        "note (one sentence for the user, up to 200); layout {piece: {anchor, size, hidden}}, where piece is a key from " +
        "get_desk pieces, anchor is tl tc tr ml mr bl bc br, size is 0.6 to 1.6, hidden removes the piece and monitor puts it on \"all\" monitors (the default), the \"main\" monitor or one monitor by the id in get_desk monitors (a piece on a monitor that is unplugged shows on the main one meanwhile; " +
        "the companion piece draws the sprite the user chose in Settings, Widgets, sits at its anchor or roams the monitor's edges from it, and draws nothing until a sprite is installed); " +
        "tilt in a layout piece slants the piece's whole stack from -20 to 20 degrees (negative rises to the right, positive falls; 0 upright), " +
        "so a left stack at -18 and a right stack at 18 read as two faces of a fold; pieces at one anchor share one slant, so give them the same tilt; " +
        "style in a layout piece, only on claude, meters and telemetry, is how its numbers draw so a stack fits its column: bar (the default, " +
        "a full-width bar with its label above), slim (label and value on one line over a thin bar, about half the height; claude becomes a compact two-row line), " +
        "ring (small arc gauges with the value inside, side by side in a row), ticks (ten segmented cells, the OLED-friendly one); " +
        "a Claude limit keeps its pace mark in every style, and slim or ring keep a dense or slanted stack from running wide; " +
        "weather {nickname} (up to 24 characters, shown on the Desk's weather piece instead of the place's name, \"\" for the place's own name; " +
        "it never leaves the PC and the forecast still uses the city the user picked); font (a " +
        "family from get_desk fonts.installed or the bundled EsteFont Pro or EsteFont 26, or {family, link} with an " +
        "https link to get a font the user lacks: Sanduhr never downloads it, the review shows the link and Apply waits " +
        "for the font, status needs_font); ink (auto, light, dark, theme, one #rrggbb, or a list of 2 to 4 #rrggbb for a " +
        "left-to-right gradient); plate and shadow (true or false; the plate is a soft panel behind each stack of pieces); " +
        "launcher {orientation row or column, buttons: up to 8 of {kind app or link, target, label up to 24 characters, " +
        "glow #rrggbb}}, where an app target is an id from get_desk start_menu_apps (a button opens only what the Start " +
        "menu lists; there are no command lines) and a link target is an https link; messages {lines, mode add or " +
        "replace}, the lines as propose_desk_messages takes them (see its description for the syntax); looks (up to 20 " +
        "per-song looks for the now playing line, {title, artist, ink, style, mood} as propose_now_playing_looks takes " +
        "them, saved with the design when it is applied; each color must keep 3:1 against what is behind the now " +
        "playing piece, and they show while the user's Style what's playing is on); theme (a widget " +
        "palette: name and the fourteen #rrggbb colors, per docs/themes/AGENT_PROMPT.md, as propose_theme takes it). " +
        "The server checks before anything is sent: every field's shape and range; ink against the wallpaper (each " +
        "placed piece's ink must keep a WCAG contrast of at least 3:1 against the wallpaper brightness under its anchor " +
        "on every monitor, or against the plate when the plate is on; auto ink picks its own and is not judged), so read " +
        "get_desk wallpaper.summary and choose ink, plate and anchors together; the Launcher's targets; the message " +
        "lines; the theme lint. status rejected comes with reasons, each {field, message}: fix those fields and call " +
        "again; nothing was written. Otherwise the Sanduhr widget draws the design live on the desktop with a bar where " +
        "the user chooses Apply or Dismiss: previewing means the user is reviewing it now (do not send the same look " +
        "again), applied means it is in place (the user allows direct changes; their previous look is saved), " +
        "needs_font lists the fonts (family, link) the user must install first, queued means the widget did not answer " +
        "and will show it when it starts. Every applied look is saved and can be switched back to in Sanduhr's " +
        "settings. This server never changes the Desk itself.";

    private const string LooksGuide =
        "Suggest a look for songs the user plays: a gradient and a letter style per song, drawn on the now playing line " +
        "of the user's Windows Desk while Sanduhr's Style what's playing is on (Settings, Now playing, Looks). Until a " +
        "song has a look it wears one picked from its name out of eight palettes. Pass looks: 1 to 20 of {title, artist, " +
        "ink, style, mood}: title and artist exactly as the player shows them (matched ignoring case, extra spaces and a " +
        "trailing Remastered mark), ink 1 to 4 #rrggbb colors left to right (a gradient across the line), style one of " +
        "plain, bold, italic, bolditalic or smallcaps (omit for plain; drawn in the Desk's own font), mood up to 40 " +
        "characters for the user ('neon night drive'). Every color must keep a WCAG contrast of 3:1 against what is behind " +
        "the now playing piece, the wallpaper under its anchor or the plate when the plate is on, so no dark shades on a " +
        "dark wallpaper and no pale ones on a bright one; read get_desk (wallpaper summary and layout) first. Refused " +
        "looks come back as rejected with reasons, each starting with the look's number, and nothing is sent. Pick songs " +
        "you know the user plays (they said so, or ask them); a look should feel like the song: its era, its cover, its " +
        "energy. Sanduhr checks the looks again and, unless the user lets Claude style songs directly, shows them as a " +
        "card to Save or Dismiss: pending_approval means the user decides, applied means they are saved (looks_saved), " +
        "rejected comes with reasons. style_on says whether Style what's playing is on; when it is false, tell the user " +
        "where to switch it on (Settings, Now playing, Looks). A saved look replaces an older one for the same song. " +
        "queued means Sanduhr did not answer: it is not running. This server never saves a look and never reads what is playing.";

    private const string WatchGuideStart =
        "Put up a watcher: a live card on the user's Desk for work you are keeping an eye on " +
        "(a CI run, a deploy, a long build or test run, a migration). The Desk shows the full title, " +
        "the time so far, done/total when you pass a total, and a one-line note. " +
        "Pass short: a name of up to 12 characters such as 'PR 140', 'v2.8.0' or 'deploy' " +
        "(without it Sanduhr derives one from the title). " +
        "Returns the watcher's id: move it with watch_update (progress, a note, or state waiting when " +
        "the user must act: it pulses and the glow fires once) and finish it with watch_end (passed or " +
        "failed). Without an update for 10 minutes it greys as lost touch, so update long jobs. Limits: " +
        "title 1 to 80 characters on one line, short up to 12, link https only, total 1 to 1000000. Pass work: true for " +
        "anything from the user's employer, so it hides in demo mode. Refused with reason " +
        "watchers_off when the user has not turned on 'Let Claude show watchers' in Sanduhr " +
        "(Settings, Desk); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";

    private const string WatchGuideUpdate =
        "Move a watcher from watch_start: done (how many of its total are done), note (one line, up to " +
        "140 characters, replaces the last), state running or waiting (waiting on the user: it pulses " +
        "and the glow fires once; set running again when they have acted). Any update also keeps it " +
        "from greying as lost touch.";

    private const string WatchGuideEnd =
        "Finish a watcher from watch_start: result passed (fades after a few seconds) " +
        "or failed (stays until the user dismisses it), with an optional one-line note (up to 140 " +
        "characters) such as what failed.";

    private static JsonObject WatchIdSchema() => new()
    {
        ["type"] = "string",
        ["pattern"] = "^w[0-9a-f]{12}$",
        ["description"] = "The id watch_start returned.",
    };

    private static JsonObject WatchNoteSchema() => new()
    {
        ["type"] = "string",
        ["maxLength"] = 140,
        ["description"] = "One line for the user, up to 140 characters.",
    };

    // propose_widget (3.8): what an agent needs to draw a pack as data, the pixel-sheet grammar and the limits included.
    private const string WidgetGuide =
        "Make a widget for the user's Sanduhr Desk with them: a pixel-art companion sprite, a palette pack for the Desk ink, or " +
        "an effects pack. Call when the user asks for a desk pet, a companion, a sprite, a palette or an effect preset. The pack " +
        "is data only: never a PNG, never a font, never code. A widget theme goes through propose_theme instead. Pass kind, pack " +
        "{id, name (1-40), description (1-200), version?, author?, license?} and the kind's files. Sanduhr namespaces the id my-<id>, " +
        "writes pack.json and a LICENSE (CC0-1.0 by default), validates the whole pack with the catalog's own rules (strict), and " +
        "refuses with status rejected and reasons before anything is sent. Trademark rule: no 'claude' or 'anthropic' anywhere and " +
        "no 'opus', 'sonnet', 'haiku' or 'fable' as words, in any name, description, author, file name or JSON string; original " +
        "art only, no studio or game characters. " +
        "SPRITE: sprite = sprite.json, schema_version 2: frame_w and frame_h (8 to 32 for pixel sheets), scale 1-4 (nearest " +
        "neighbour), fps 1-24, either sheet (one .json path) or stages (1-6 of {id kebab 2-20, name 1-24, min_growth 0 for the " +
        "first then strictly increasing up to 1, sheet, frame_w, frame_h}; stage 0's size equals the top-level size; stages grow " +
        "with the user's coding days, never tokens), animations (name to 1-64 frame indexes; required: walk, sit, nap, pop, cheer, " +
        "worry, focus, sleep, celebrate), moods {content, focused, worried, exhausted, celebrate} and triggers {needs_you, " +
        "job_passed, near_limit, idle} each naming an animation, roam {edges: subset of bottom, left, right; speed_px_s 5-120}. " +
        "sheets = path to a pixel sheet: {palette: 1-16 '#rrggbb' colors, never #ffffff (use #f0f0f0), frame_w, frame_h (equal " +
        "to the stage's), frames: 1-256 frames, each exactly frame_h strings of exactly frame_w characters, top row first; '.' " +
        "is transparent and 0-9, a-f (lowercase) index the palette}. Frame i is frames[i]; every sheet needs a frame for the " +
        "highest index any animation uses. Keep the character on transparency, readable at scale 2, and give walk two or more " +
        "frames. PALETTE: palettes = 1-64 of {name: lowercase-kebab 2-24, unique; colors: 1-4 '#rrggbb' stops left to right; mood? " +
        "up to 24 characters}. EFFECTS: effects = 1-64 of {name as for palettes, type glow|sweep|shimmer|pulse, intensity 0-1, " +
        "period_ms integer 400-10000, color '#rrggbb' or 'ink'}. " +
        "Sanduhr checks the pack again and previews it on the Desk (a sprite as the companion, a palette as the Desk ink) with a " +
        "review bar: previewing means the user is looking at it; it installs only when they press Apply (applied, as a local " +
        "widget made here, listed in Settings, Widgets; companion true when they made a sprite the companion), and Dismiss " +
        "answers rejected with reason dismissed and writes nothing. 'Let Claude change the Desk directly' does not cover " +
        "widgets: a new pack always waits for Apply. queued means Sanduhr did not answer: it is not running. Nothing is uploaded; " +
        "sharing a pack is a pull request to the sanduhr-widgets catalog.";

    private static JsonObject Tool(string name, string description, JsonObject inputSchema, bool readOnly = true, bool? openWorld = null) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = readOnly,
            ["destructiveHint"] = false,
            ["openWorldHint"] = openWorld ?? !readOnly,
        },
    };
}
