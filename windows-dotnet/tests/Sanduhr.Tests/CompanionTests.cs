using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The companion's growth curve and memory, Sanduhr Time's day files, the sprite.json model and sheet cutting.</summary>
public class CompanionGrowthTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanduhr-companion-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(-4, 0.0)]
    [InlineData(3, 0.10)]
    [InlineData(10, 0.35)]
    [InlineData(30, 0.75)]
    [InlineData(60, 1.0)]
    [InlineData(400, 1.0)]
    public void The_curve_passes_through_its_knots(int days, double growth) =>
        Assert.Equal(growth, CompanionGrowth.FromDays(days), 6);

    [Fact]
    public void The_curve_is_linear_between_knots_and_never_falls()
    {
        Assert.Equal(0.10 + 0.25 * 3 / 7, CompanionGrowth.FromDays(6), 6);
        Assert.Equal(0.35 + 0.40 * 5 / 20, CompanionGrowth.FromDays(15), 6);
        double last = -1;
        for (int d = 0; d <= 90; d++)
        {
            double g = CompanionGrowth.FromDays(d);
            Assert.True(g >= last, $"day {d}");
            last = g;
        }
    }

    [Fact]
    public void The_hourglass_reaches_stage_two_on_day_three_stage_three_on_day_ten_and_the_last_on_day_thirty()
    {
        var sprite = CompanionSprite.Parse(File.ReadAllBytes(Path.Combine(WidgetPackFixtures.Root, "packs", "hourglass", "sprite.json")))!;
        Assert.Equal([0.0, 0.1, 0.35, 0.75], sprite.Stages.Select(s => s.MinGrowth));
        Assert.Equal([0, 3, 10, 30], sprite.Stages.Select(s => CompanionGrowth.DaysFor(s.MinGrowth)));
        Assert.Equal("egg", sprite.StageAt(CompanionGrowth.FromDays(2)).Id);
        Assert.Equal("sprout", sprite.StageAt(CompanionGrowth.FromDays(3)).Id);
        Assert.Equal("sprout", sprite.StageAt(CompanionGrowth.FromDays(9)).Id);
        Assert.Equal("runner", sprite.StageAt(CompanionGrowth.FromDays(10)).Id);
        Assert.Equal("runner", sprite.StageAt(CompanionGrowth.FromDays(29)).Id);
        Assert.Equal("keeper", sprite.StageAt(CompanionGrowth.FromDays(30)).Id);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.05, 2)]
    [InlineData(0.5, 18)]
    [InlineData(0.9, 48)]
    [InlineData(1.0, 60)]
    [InlineData(1.5, 60)]
    public void Any_packs_threshold_maps_to_the_first_day_the_curve_reaches_it(double growth, int days)
    {
        Assert.Equal(days, CompanionGrowth.DaysFor(growth));
        Assert.True(CompanionGrowth.FromDays(days) + 1e-9 >= Math.Min(growth, 1));
        if (days > 0) Assert.True(CompanionGrowth.FromDays(days - 1) < growth);
    }

    [Fact]
    public void The_stage_is_the_highest_threshold_reached()
    {
        IReadOnlyList<double> mins = [0, 0.1, 0.35, 0.75];
        Assert.Equal(0, CompanionGrowth.StageIndex(mins, 0));
        Assert.Equal(0, CompanionGrowth.StageIndex(mins, 0.0999));
        Assert.Equal(1, CompanionGrowth.StageIndex(mins, 0.1));
        Assert.Equal(2, CompanionGrowth.StageIndex(mins, 0.74));
        Assert.Equal(3, CompanionGrowth.StageIndex(mins, 1));
        Assert.Equal(0, CompanionGrowth.StageIndex([0], 1));
    }

    private static CodingDay Day(string date, double minutes = 30) => new(DateOnly.Parse(date), TimeSpan.FromMinutes(minutes));

    [Fact]
    public void Growth_never_goes_backwards_and_days_accumulate_across_sources()
    {
        var s = CompanionGrowthState.Empty.Observe([Day("2026-10-01"), Day("2026-10-02"), Day("2026-10-03")]);
        Assert.Equal(0.10, s.Growth, 6);
        Assert.Equal(3, s.Days.Count);

        // The source forgot the old days (Claude Code's logs are pruned): the remembered ones still count.
        s = s.Observe([Day("2026-10-04")]);
        Assert.Equal(4, s.Days.Count);
        Assert.Equal(CompanionGrowth.FromDays(4), s.Growth, 6);

        // A day seen twice is one day.
        Assert.Equal(4, s.Observe([Day("2026-10-04"), Day("2026-10-04")]).Days.Count);

        // A held value above the curve is kept: never backwards.
        var high = new CompanionGrowthState(0.8, new HashSet<DateOnly>(), TimeSpan.Zero).Observe([Day("2026-10-01")]);
        Assert.Equal(0.8, high.Growth, 6);
    }

    [Fact]
    public void Usage_never_enters_growth()
    {
        // The same coding days with ten times the active time grow exactly the same.
        var calm = CompanionGrowthState.Empty.Observe([Day("2026-10-01", 20), Day("2026-10-02", 20)]);
        var heavy = CompanionGrowthState.Empty.Observe([Day("2026-10-01", 600), Day("2026-10-02", 600)]);
        Assert.Equal(calm.Growth, heavy.Growth);
        Assert.True(heavy.Active > calm.Active);
    }

    [Fact]
    public void The_memory_round_trips_and_a_broken_file_reads_as_empty()
    {
        var s = CompanionGrowthState.Empty.Observe([Day("2026-10-01", 90), Day("2026-09-30", 30)]);
        var file = Path.Combine(_dir, CompanionGrowthState.FileName);
        Assert.True(s.Save(file));
        var back = CompanionGrowthState.Load(file);
        Assert.Equal(s.Growth, back.Growth, 6);
        Assert.True(s.Days.SetEquals(back.Days));
        Assert.Equal(TimeSpan.FromMinutes(120), back.Active);
        var json = File.ReadAllText(file);
        Assert.Contains("\"2026-09-30\"", json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);

        Assert.Same(CompanionGrowthState.Empty, CompanionGrowthState.Parse("not json"));
        Assert.Equal(1.0, CompanionGrowthState.Parse("""{"growth": 7, "days": ["nope", 3, "2026-10-01"]}""").Growth);
        Assert.Single(CompanionGrowthState.Parse("""{"growth": 7, "days": ["nope", 3, "2026-10-01"]}""").Days);
        Assert.Equal(0, CompanionGrowthState.Load(Path.Combine(_dir, "absent.json")).Growth);
    }

    // ---- Sanduhr Time day files ----

    private const string V1 = """{"v":1,"date":"2026-10-05","machine":"m","generatedAt":"2026-10-05T23:00:00Z","totals":{"youMs":1000,"claudeMs":2000,"bothMs":500,"totalMs":3500,"agentMs":2500,"linesYou":3,"linesClaude":9},"projects":[],"caveats":{}}""";
    private const string V2 = """{"v":2,"date":"2026-10-06","machine":"m","generatedAt":"2026-10-06T23:00:00Z","totals":{"youMs":60000,"agentsMs":120000,"bothMs":0,"totalMs":180000,"agentMs":120000,"linesYou":0,"linesAgents":0,"byAgent":{}},"projects":[],"caveats":{}}""";

    [Fact]
    public void A_v1_and_a_v2_day_file_each_give_their_date_and_total_time()
    {
        var v1 = SanduhrTimeDays.Parse(Encoding.UTF8.GetBytes(V1));
        Assert.Equal(new DateOnly(2026, 10, 5), v1!.Value.Date);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), v1.Value.Active);
        var v2 = SanduhrTimeDays.Parse(Encoding.UTF8.GetBytes(V2));
        Assert.Equal(new DateOnly(2026, 10, 6), v2!.Value.Date);
        Assert.Equal(TimeSpan.FromMinutes(3), v2.Value.Active);

        // A v1 file without totalMs is summed from its parts; v2 reads agentsMs instead of claudeMs.
        var parts = SanduhrTimeDays.Parse(Encoding.UTF8.GetBytes("""{"v":1,"date":"2026-10-07","totals":{"youMs":10,"claudeMs":20,"bothMs":5}}"""));
        Assert.Equal(TimeSpan.FromMilliseconds(35), parts!.Value.Active);
    }

    [Theory]
    [InlineData("""{"v":3,"date":"2026-10-05","totals":{"totalMs":100}}""")]
    [InlineData("""{"v":2,"date":"10/05/2026","totals":{"totalMs":100}}""")]
    [InlineData("""{"v":2,"date":"2026-10-05","totals":{"totalMs":0,"youMs":0}}""")]
    [InlineData("""{"v":2,"date":"2026-10-05"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void A_file_that_is_not_a_day_with_time_is_not_a_coding_day(string json) =>
        Assert.Null(SanduhrTimeDays.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void The_days_folder_is_read_by_date_named_files_only_and_absent_is_empty()
    {
        Assert.Empty(SanduhrTimeDays.Read(Path.Combine(_dir, "none")));
        Assert.False(SanduhrTimeDays.Present(Path.Combine(_dir, "none")));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "2026-10-05.json"), V1);
        File.WriteAllText(Path.Combine(_dir, "2026-10-06.json"), V2);
        File.WriteAllText(Path.Combine(_dir, "2026-10-07.json"), """{"v":2,"date":"2026-10-07","totals":{"totalMs":0}}""");
        File.WriteAllText(Path.Combine(_dir, "notes.json"), V2);
        var days = SanduhrTimeDays.Read(_dir);
        Assert.True(SanduhrTimeDays.Present(_dir));
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6)], days.Select(d => d.Date));
    }
}

/// <summary>sprite.json as the companion reads it, and cutting frames out of PNG and rasterized pixel sheets.</summary>
public class CompanionSpriteTests
{
    private static CompanionSprite Fixture(string group, string name) =>
        CompanionSprite.Parse(File.ReadAllBytes(Path.Combine(WidgetPackFixtures.Root, group, name, "sprite.json")))!;

    [Fact]
    public void The_hourglass_reads_with_its_mapping_roam_and_mixed_cell_sizes()
    {
        var s = Fixture("packs", "hourglass");
        Assert.Equal(2, s.Scale);
        Assert.Equal(8, s.Fps);
        Assert.Equal(["egg", "sprout", "runner", "keeper"], s.Stages.Select(x => x.Id));
        Assert.Equal((32, 32), (s.Stages[0].FrameWidth, s.Stages[0].FrameHeight));
        Assert.Equal((40, 40), (s.Stages[3].FrameWidth, s.Stages[3].FrameHeight));
        Assert.Equal("sit", s.Moods[CompanionMood.Content]);
        Assert.Equal("sleep", s.Moods[CompanionMood.Exhausted]);
        Assert.Equal("pop", s.Triggers[CompanionTrigger.NeedsYou]);
        Assert.Equal("nap", s.Triggers[CompanionTrigger.Idle]);
        Assert.Equal([CompanionEdge.Bottom, CompanionEdge.Left, CompanionEdge.Right], s.Edges);
        Assert.Equal(30, s.SpeedPxPerSecond);
        Assert.Equal(27, s.MaxFrame);
        Assert.Equal([0, 1, 2, 3], s.Frames("walk"));
        Assert.Empty(s.Frames("dance"));
    }

    [Fact]
    public void A_single_sheet_sprite_is_one_stage_at_the_top_level_cell_size()
    {
        var s = Fixture("good", "good-sprite");
        var only = Assert.Single(s.Stages);
        Assert.Equal(CompanionSprite.SingleStageId, only.Id);
        Assert.Equal(0, only.MinGrowth);
        Assert.Same(only, s.StageAt(1));
    }

    [Fact]
    public void Stage_selection_with_a_pixel_sheet_stage_and_a_larger_png_stage()
    {
        var s = Fixture("good", "good-sprite-pixel-stages");
        var one = s.StageAt(0.49);
        Assert.Equal("one", one.Id);
        Assert.True(one.IsPixelSheet);
        Assert.Equal((8, 8), (one.FrameWidth, one.FrameHeight));
        var two = s.StageAt(0.5);
        Assert.Equal("two", two.Id);
        Assert.False(two.IsPixelSheet);
        Assert.Equal((16, 16), (two.FrameWidth, two.FrameHeight));

        var store = new WidgetStore(Path.Combine(Path.GetTempPath(), "w"), _ => []);
        Assert.Equal(store.RasterPath("good-sprite-pixel-stages", "sprites/one.json"), CompanionSheet.DrawnFile(store, "good-sprite-pixel-stages", one));
        Assert.EndsWith(Path.Combine("good-sprite-pixel-stages", "sprites", "two.png"), CompanionSheet.DrawnFile(store, "good-sprite-pixel-stages", two));
    }

    [Theory]
    [InlineData("""{"moods":{}}""")]
    [InlineData("""not json""")]
    public void A_sprite_that_is_not_usable_reads_as_null(string json) =>
        Assert.Null(CompanionSprite.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_mapping_to_an_animation_that_is_missing_or_a_missing_trigger_reads_as_null()
    {
        var good = File.ReadAllText(Path.Combine(WidgetPackFixtures.Root, "packs", "hourglass", "sprite.json"));
        Assert.NotNull(CompanionSprite.Parse(Encoding.UTF8.GetBytes(good)));
        Assert.Null(CompanionSprite.Parse(Encoding.UTF8.GetBytes(good.Replace("\"content\": \"sit\"", "\"content\": \"dance\""))));
        Assert.Null(CompanionSprite.Parse(Encoding.UTF8.GetBytes(good.Replace("\"idle\": \"nap\"", "\"idle_x\": \"nap\""))));
        Assert.Null(CompanionSprite.Parse(Encoding.UTF8.GetBytes(good.Replace("\"min_growth\": 0.35", "\"min_growth\": 0.05"))));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(6, 192, 0)]
    [InlineData(7, 0, 32)]
    [InlineData(27, 192, 96)]
    public void A_png_frame_is_the_ith_cell_row_major(int index, int x, int y) =>
        Assert.Equal(new PxRect(x, y, 32, 32), CompanionSheet.Cell(index, 224, 128, 32, 32));

    [Fact]
    public void A_cell_past_the_sheet_or_a_sheet_that_is_not_whole_cells_has_no_frame()
    {
        Assert.Null(CompanionSheet.Cell(28, 224, 128, 32, 32));
        Assert.Null(CompanionSheet.Cell(-1, 224, 128, 32, 32));
        Assert.Null(CompanionSheet.Cell(0, 225, 128, 32, 32));
        Assert.Null(CompanionSheet.Cell(0, 224, 100, 32, 32));
        Assert.Equal(new PxRect(40, 0, 40, 40), CompanionSheet.Cell(1, 280, 160, 40, 40));
    }

    [Fact]
    public void A_rasterized_pixel_sheet_is_cut_by_the_same_rule_as_a_png()
    {
        // 20 frames of 8 x 8, each a different color at its own pixel: rasterized 16 to a row.
        var palette = Enumerable.Range(0, 16).Select(i => $"\"#{(i * 15 + 1):x2}2040\"");
        var frames = Enumerable.Range(0, 20).Select(i =>
        {
            var rows = Enumerable.Range(0, 8).Select(y => y == i % 8 ? "\"" + new string('.', i % 8) + (i % 16).ToString("x") + new string('.', 7 - i % 8) + "\"" : "\"........\"");
            return "[" + string.Join(",", rows) + "]";
        });
        var json = $$"""{"palette":[{{string.Join(",", palette)}}],"frame_w":8,"frame_h":8,"frames":[{{string.Join(",", frames)}}]}""";
        var image = PixelSheet.Rasterize(Encoding.UTF8.GetBytes(json), out var error);
        Assert.Null(error);
        Assert.Equal((128, 16), (image!.Width, image.Height));
        foreach (var i in new[] { 0, 15, 16, 19 })
        {
            var cell = CompanionSheet.Cell(i, image.Width, image.Height, 8, 8)!.Value;
            var cut = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
                Array.Copy(image.Rgba, ((cell.Y + y) * image.Width + cell.X) * 4, cut, y * 32, 32);
            Assert.Equal(image.Frame(i), cut);
            int at = ((i % 8) * 8 + i % 8) * 4;
            Assert.Equal(255, cut[at + 3]);
        }
        Assert.Null(CompanionSheet.Cell(32, image.Width, image.Height, 8, 8));
    }
}

/// <summary>desk_companion: which installed sprite is the companion and whether it roams.</summary>
public class CompanionSettingsTests
{
    private static WidgetRegistry Reg(params string[] ids) =>
        new(null, [.. ids.Select(id => new InstalledWidget(id, "1.0.0", WidgetSource.Catalog, DateTimeOffset.UnixEpoch, true))]);

    private static string? Kind(string id) => id.Contains("sprite") || id == "hourglass" ? "sprite" : "palette";

    [Fact]
    public void Absent_is_no_pick_and_roaming_on_and_it_round_trips()
    {
        Assert.Equal(CompanionSettings.Default, CompanionSettings.Decode(null));
        Assert.Equal(new CompanionSettings(null, true), CompanionSettings.Decode(new System.Text.Json.Nodes.JsonObject()));
        var s = new CompanionSettings("hourglass", false);
        Assert.Equal(s, CompanionSettings.Decode(s.Encode()));
        Assert.Null(CompanionSettings.Decode(System.Text.Json.Nodes.JsonNode.Parse("""{"pack":"..\\evil"}""")).Pack);
    }

    [Fact]
    public void The_active_pack_is_the_pick_while_it_is_an_installed_sprite_else_the_first_sprite()
    {
        var reg = Reg("good-palette", "hourglass", "my-sprite");
        Assert.Equal("my-sprite", new CompanionSettings("my-sprite", true).ActivePack(reg, Kind));
        Assert.Equal("hourglass", new CompanionSettings(null, true).ActivePack(reg, Kind));
        Assert.Equal("hourglass", new CompanionSettings("gone-sprite", true).ActivePack(reg, Kind));
        Assert.Equal("hourglass", new CompanionSettings("good-palette", true).ActivePack(reg, Kind));
        Assert.Null(new CompanionSettings("hourglass", true).ActivePack(Reg("good-palette"), Kind));
    }

    [Fact]
    public void The_growth_memory_lives_in_the_widgets_folder_and_time_is_only_read_from_the_shared_folder()
    {
        var p = new Paths(@"C:\r", @"C:\h", @"C:\l", @"C:\h\.sanduhr");
        Assert.Equal(Path.Combine(p.WidgetsDir, "companion.json"), p.CompanionStateFile);
        Assert.Equal(Path.Combine(@"C:\h\.sanduhr", "time", "days"), p.TimeDaysDir);
    }
}
