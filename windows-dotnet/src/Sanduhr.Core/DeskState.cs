using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One app the Start menu lists, as the Launcher can open it.</summary>
public sealed record StartMenuApp(string Id, string Name);

/// <summary>
/// The Desk as <c>get_desk</c> reports it (spec, the agent-designed Desk, MCP tools): the widget writes it as
/// <c>desk-state.json</c> in <c>%USERPROFILE%\.sanduhr</c>, the server reads it, and it is rewritten when the Desk or the
/// wallpaper changes. It exists only while the MCP integration is installed. Pure: the App gathers the inputs.
/// </summary>
public static class DeskState
{
    public const int SchemaVersion = 1;

    /// <summary>The Desk's current look as a full design, built from the settings and the active widget theme.</summary>
    /// <param name="theme">The active theme as its JSON document (<see cref="ThemeDraft.ToJson"/>); null leaves it out.</param>
    /// <param name="weatherNickname">The weather piece's nickname (#315), "" or null for none; reported as <c>weather.nickname</c>.</param>
    /// <param name="followsTheme">"Follow the widget theme" (theme match), reported as <c>follows_theme</c>.</param>
    public static DeskDesign Current(DeskSettings desk, DeskInkSettings ink, LauncherModel launcher, JsonObject? theme, string? weatherNickname = null,
        bool followsTheme = false)
    {
        var arrangement = new DeskArrangement(desk.Layout);
        return new DeskDesign
        {
            // Every placed piece says where it is drawn ("all", "main" or a monitor id), so Claude never has to guess the default.
            Layout = [.. DeskDesign.FromArrangement(arrangement).Select(p => p.IsHidden ? p : p with { Monitor = DeskMonitors.Word(p.Monitor) })],
            Font = new DeskFontChoice(desk.Font ?? DeskFont.DefaultFamily),
            Ink = InkChoice(ink),
            Plate = desk.Plate,
            Shadow = ink.Shadow,
            Launcher = launcher,
            Theme = theme,
            Weather = new DeskWeatherChange(weatherNickname ?? ""),
            FollowsTheme = followsTheme,
        };
    }

    private static DeskInkChoice InkChoice(DeskInkSettings ink)
    {
        switch (ink.Mode)
        {
            case DeskInkMode.Light: return DeskInkChoice.Of(DeskInkKind.Light);
            case DeskInkMode.Dark: return DeskInkChoice.Of(DeskInkKind.Dark);
            case DeskInkMode.Theme: return DeskInkChoice.Of(DeskInkKind.Theme);
            case DeskInkMode.Custom:
                var colors = (ink.Color ?? "").Split(',').Select(InkHex.Normalize).OfType<string>().Take(4).Select(h => "#" + h).ToList();
                return new DeskInkChoice(DeskInkKind.Custom, colors.Count > 0 ? colors : ["#" + InkHex.Fallback]);
            default: return DeskInkChoice.Of(DeskInkKind.Auto);
        }
    }

    /// <summary>
    /// The state document. <paramref name="startMenuApps"/> null means the Start menu has not been read yet (the key is
    /// left out, so the server skips the membership rule); <paramref name="wallpaper"/> null leaves the summary out.
    /// </summary>
    public static JsonObject Build(
        DeskDesign current,
        IEnumerable<string> installedFonts,
        WallpaperOverview? wallpaper,
        string? wallpaperPath,
        IEnumerable<StartMenuApp>? startMenuApps,
        IEnumerable<SavedDesign> designs,
        bool direct,
        string writerVersion,
        string? wallpaperSource = null,
        bool? wallpaperIsImage = null,
        IEnumerable<DeskMonitorInfo>? monitors = null)
    {
        var pieces = new JsonArray();
        foreach (var p in DeskArrangement.Known) pieces.Add((JsonNode?)p);
        var anchors = new JsonArray();
        foreach (var a in DeskAnchors.All) anchors.Add((JsonNode?)DeskAnchors.Word(a));
        var installed = new JsonArray();
        foreach (var f in installedFonts.Where(f => f.Length > 0 && DeskDesign.FontFamilyProblem(f) is null).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(2000))
            installed.Add((JsonNode?)f);
        var bundled = new JsonArray();
        foreach (var f in BundledFonts.Families) bundled.Add((JsonNode?)f.Name);
        var list = new JsonArray();
        foreach (var d in designs.OrderByDescending(d => d.AppliedAt).ThenBy(d => d.Id, StringComparer.Ordinal)) list.Add((JsonNode?)d.SummaryJson());

        // path: the downscaled copy the widget wrote for Claude Code to look at (longest side 1600 px), null when none was written.
        // source: "file" when that copy was made from the file Windows names, "cache" when from Windows' cached copy of it.
        // kind: "image" when the shell named a wallpaper file; "color" when it named none, in which case the summary is
        // the desktop's background color (an agent should not read it as a picture).
        var wall = new JsonObject { ["path"] = wallpaperPath };
        if (wallpaperSource is not null) wall["source"] = wallpaperSource;
        if (wallpaper is not null)
        {
            wall["kind"] = wallpaperIsImage ?? wallpaperPath is not null ? "image" : "color";
            wall["summary"] = wallpaper.ToJson();
        }

        var monitorList = new JsonArray();
        foreach (var m in monitors ?? [])
            monitorList.Add((JsonNode?)new JsonObject
            {
                ["id"] = m.Id, ["name"] = m.Name, ["width"] = m.Width, ["height"] = m.Height,
                ["primary"] = m.Primary, ["left"] = m.Left, ["top"] = m.Top,
            });

        var state = new JsonObject
        {
            ["schema_version"] = SchemaVersion,
            ["writer_version"] = writerVersion,
            ["design"] = current.ToJson(),
            ["pieces"] = pieces,
            ["anchors"] = anchors,
            ["fonts"] = new JsonObject { ["installed"] = installed, ["bundled"] = bundled },
            ["monitors"] = monitorList,
            ["wallpaper"] = wall,
            ["designs"] = list,
            ["direct"] = direct,
            ["limits"] = new JsonObject
            {
                ["name_chars"] = DeskDesign.NameMax,
                ["note_chars"] = DeskDesign.NoteMax,
                ["piece_size"] = new JsonObject { ["min"] = DeskArrangement.MinScale, ["max"] = DeskArrangement.MaxScale },
                ["piece_tilt"] = new JsonObject { ["min"] = DeskArrangement.MinTilt, ["max"] = DeskArrangement.MaxTilt },
                // The meter styles (#336): which pieces take one and the words; bar is the default.
                ["piece_style"] = new JsonObject
                {
                    ["pieces"] = new JsonArray([.. DeskMeterStyles.Pieces.Select(p => (JsonNode?)p)]),
                    ["values"] = new JsonArray([.. DeskMeterStyles.All.Select(s => (JsonNode?)DeskMeterStyles.Word(s))]),
                    ["default"] = DeskMeterStyles.Word(DeskMeterStyle.Bar),
                },
                ["weather_nickname_chars"] = DeskDesign.WeatherNicknameMax,
                ["launcher_buttons"] = LauncherModel.MaxButtons,
                ["launcher_label_chars"] = LauncherButton.MaxLabel,
                ["message_lines"] = MessageProposal.MaxLines,
                ["message_line_chars"] = MessageProposal.MaxLineChars,
                ["contrast_min"] = DeskDesign.MinContrast,
            },
        };
        if (startMenuApps is not null)
        {
            var apps = new JsonArray();
            foreach (var a in startMenuApps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).Take(1000))
                apps.Add((JsonNode?)new JsonObject { ["id"] = a.Id, ["name"] = a.Name });
            state["start_menu_apps"] = apps;
        }
        return state;
    }
}
