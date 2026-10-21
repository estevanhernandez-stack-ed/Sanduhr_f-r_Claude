using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// The tools/list catalog: six read-only tools (the five usage tools and
/// <c>get_desk_messages</c>) plus the tools that hand a request file to the widget
/// and never act themselves: <c>publish_usage</c> (the widget owns the key and the
/// network; this server posts nothing), <c>propose_theme</c>,
/// <c>propose_desk_messages</c> and <c>watch_start</c> / <c>watch_update</c> /
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
        "watchers_off when the user has not turned on Let agents show watchers in Sanduhr (Settings, " +
        "Desk); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";

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
