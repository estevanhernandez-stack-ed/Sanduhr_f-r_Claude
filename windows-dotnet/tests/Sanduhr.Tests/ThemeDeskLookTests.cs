using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match: one function turns a theme into the Desk's whole look (spec, "The rules"). The built-ins' looks are pinned,
/// shadow and plate tint included (the plan's formulas: bg toward black at 0.5, bg toward the plate's color at 0.25).</summary>
public class ThemeDeskLookTests
{
    [Theory]
    //          key,         gradient,         ink,              glow,     plate,    shadow,   font,            track,    tick
    [InlineData("obsidian", "6c63ff,e8e4dc", "e8e4dc,6c63ff", "6c63ff", "0a0a0a", "070707", null, "2a2a2a", "ff6b6b")]
    [InlineData("aurora", "38bdf8,e2e8f0", "e2e8f0,38bdf8", "38bdf8", "080b14", "05080d", null, "1e293b", "f472b6")]
    [InlineData("ember", "f97316,f5e6e0", "f5e6e0,f97316", "f97316", "140808", "0d0505", null, "2d1a1a", "fbbf24")]
    [InlineData("mint", "34d399,e0f5ec", "e0f5ec,34d399", "34d399", "08140f", "050d0a", null, "163020", "f472b6")]
    [InlineData("matrix", "00ff41", "00ff41", "00ff41", "020802", "010501", "Cascadia Code", "0a1a0a", "ff0040")]
    [InlineData("blueprint", "4dffc4,e0e2f5", "e0e2f5,4dffc4", "4dffc4", "14111c", "0d0b13", null, "1f1a2e", "ff4d88")]
    public void Each_built_in_has_its_pinned_look(string key, string gradient, string ink, string glow, string plate, string shadow,
        string? font, string track, string tick)
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns[key]);
        Assert.Equal(gradient.Split(','), look.Gradient);
        Assert.Equal(ink.Split(','), look.Ink);
        Assert.Equal(look.Gradient, look.Bars);       // no desk.bars: the theme gradient
        Assert.Equal(look.Gradient, look.Message);
        Assert.Equal(glow, look.Glow);
        Assert.True(look.Plate);
        Assert.Equal(plate, look.PlateColor);
        Assert.Equal(shadow, look.Shadow);
        Assert.Equal(font, look.Font);
        Assert.Equal(track, look.BarTrack);
        Assert.Equal(tick, look.PaceTick);
        Assert.False(look.DarkInk);
    }

    [Fact]
    public void The_gradient_is_accent_to_sparkline_or_accent_to_text_when_they_are_one()
    {
        Assert.Equal(["6c63ff", "9d97ff"], ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Sparkline = "#9d97ff" }).Gradient);
        Assert.Equal(["6c63ff", "e8e4dc"], ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).Gradient);
    }

    [Fact]
    public void Every_desk_field_wins_over_its_rule()
    {
        var t = ThemeCatalog.BuiltIns["obsidian"] with
        {
            Font = "Segoe UI",
            Desk = new ThemeDesk
            {
                Ink = ["#FFFFFF", "#9AD7FF"], Bars = ["#ff0000", "#00ff00", "#0000ff", "#ffffff"], Message = ["#ffd08a"],
                Glow = "#ffd08a", Plate = false, PlateColor = "#112233", Shadow = "#000000", Font = "Consolas",
            },
        };
        var look = ThemeDeskLook.From(t);
        Assert.Equal(["ffffff", "9ad7ff"], look.Ink);
        Assert.Equal(["ff0000", "00ff00", "0000ff", "ffffff"], look.Bars);
        Assert.Equal(["ffd08a"], look.Message);
        Assert.Equal("ffd08a", look.Glow);
        Assert.False(look.Plate);
        Assert.Equal("112233", look.PlateColor);
        Assert.Equal("000000", look.Shadow);
        Assert.Equal("Consolas", look.Font);
        Assert.Equal(["6c63ff", "e8e4dc"], look.Gradient);   // the gradient itself stays the theme's
    }

    [Fact]
    public void The_font_falls_back_from_desk_font_to_font_to_monospace_to_none()
    {
        var matrix = ThemeCatalog.BuiltIns["matrix"];
        Assert.Equal("Cascadia Code", ThemeDeskLook.From(matrix).Font);
        Assert.Equal("Consolas", ThemeDeskLook.From(matrix with { Font = "Consolas" }).Font);
        Assert.Equal("EsteFont 26", ThemeDeskLook.From(matrix with { Font = "Consolas", Desk = new ThemeDesk { Font = "EsteFont 26" } }).Font);
        Assert.Null(ThemeDeskLook.From(ThemeCatalog.BuiltIns["aurora"]).Font);   // the Desk keeps its own font
    }

    [Fact]
    public void Dark_ink_turns_the_shadow_and_the_plate_toward_white()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Bg = "#f4f4f2", Text = "#1b1f27" });
        Assert.True(look.DarkInk);
        Assert.Equal("fafaf9", look.Shadow);
        Assert.Equal("f7f7f5", look.PlateColor);
    }

    [Theory]
    [InlineData("0d0d0d", "000000", 0.5, "070707")]
    [InlineData("0d0d0d", "000000", 0.25, "0a0a0a")]
    [InlineData("#F4F4F2", "ffffff", 0.5, "fafaf9")]
    [InlineData("zzz", "000000", 0.5, "808080")]   // an unreadable color is white, InkHex's rule
    public void Mix_blends_in_srgb_bytes_and_rounds_half_away(string from, string toward, double amount, string expected) =>
        Assert.Equal(expected, ThemeDeskLook.Mix(from, toward, amount));

    [Fact]
    public void The_ink_reads_when_its_first_stop_keeps_three_to_one()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.True(look.ReadsOver(0.05));    // #e8e4dc on a dark wallpaper: 8.3:1
        Assert.False(look.ReadsOver(0.9));    // on a bright one: 1.1:1
    }

    [Fact]
    public void The_ink_as_a_desk_ink_setting_is_custom_with_the_joined_stops()
    {
        var s = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).InkSettings;
        Assert.Equal(DeskInkMode.Custom, s.Mode);
        Assert.Equal("e8e4dc,6c63ff", s.Color);
        Assert.True(s.Shadow);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void A_bad_sample_is_no_sample_and_reads(double luminance) =>
        Assert.True(ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).ReadsOver(luminance));

    [Fact]
    public void Stops_drop_consecutive_repeats_normalize_short_hex_and_cap_at_four()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Bars = ["#f00", "#ff0000", "#0f0"] } });
        Assert.Equal(["ff0000", "00ff00"], look.Bars);
        var five = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with
        {
            Desk = new ThemeDesk { Bars = ["#111111", "#222222", "#333333", "#444444", "#555555"] },
        });
        Assert.Equal(["111111", "222222", "333333", "444444"], five.Bars);
    }

    [Fact]
    public void The_guard_turns_the_plate_on_only_where_the_ink_does_not_read()
    {
        var bare = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Plate = false } });
        Assert.False(bare.Guarded(0.05).Plate);
        Assert.True(bare.Guarded(0.9).Plate);
        Assert.False(bare.Guarded(null).Plate);        // no sample: as the theme says
        Assert.False(bare.Guarded(double.NaN).Plate);
        var plated = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Same(plated, plated.Guarded(0.9));       // already on: nothing to do
    }

    [Fact]
    public void The_look_as_get_desk_reports_it()
    {
        var json = ThemeDeskLook.From(ThemeCatalog.BuiltIns["matrix"]).ToJson();
        Assert.Equal("#00ff41", (string?)json["ink"]![0]);
        Assert.Equal("#020802", (string?)json["plate_color"]);
        Assert.True((bool)json["plate"]!);
        Assert.Equal("Cascadia Code", (string?)json["font"]);
        Assert.Equal(["gradient", "ink", "bars", "message", "glow", "plate", "plate_color", "shadow", "font", "bar_track", "pace_tick"],
            json.Select(kv => kv.Key));
    }

    // ---- the plate opacity rule (final review I-3) ----

    private static readonly ThemeDeskLook Obsidian = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(-0.2)]
    [InlineData(1.4)]
    [InlineData(0.05)]   // reads over the plate already
    public void The_plate_stays_at_todays_opacity_when_there_is_no_sample_or_the_ink_already_reads(double? luminance) =>
        Assert.Equal(DeskPlate.Opacity, Obsidian.PlateOpacityOver(luminance));

    [Theory]
    [InlineData(0.79)]
    [InlineData(1.0)]
    public void The_plate_is_the_smallest_step_from_030_to_060_at_which_the_ink_reads(double luminance)
    {
        double opacity = Obsidian.PlateOpacityOver(luminance);
        Assert.InRange(opacity, DeskPlate.Opacity + 0.049, ThemeDeskLook.MaxPlateOpacity);
        Assert.Equal(0, Math.Round(opacity / 0.05) * 0.05 - opacity, 6);   // on the 0.05 grid
        double ink = InkHex.Luminance(Obsidian.Ink[0]);
        Assert.True(DeskDesign.Ratio(ink, DeskDesign.UnderPlate(luminance, Obsidian.PlateColor, opacity)) >= DeskDesign.MinContrast);
        Assert.True(DeskDesign.Ratio(ink, DeskDesign.UnderPlate(luminance, Obsidian.PlateColor, opacity - 0.05)) < DeskDesign.MinContrast);
    }

    [Fact]
    public void The_plate_is_050_for_obsidian_over_the_light_wallpaper_and_055_over_white()
    {
        Assert.Equal(0.50, Obsidian.PlateOpacityOver(0.79), 6);
        Assert.Equal(0.55, Obsidian.PlateOpacityOver(1.0), 6);
    }

    [Fact]
    public void The_plate_stops_at_the_maximum_when_no_opacity_can_make_the_ink_read()
    {
        var white = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { PlateColor = "#ffffff" } });
        Assert.Equal(ThemeDeskLook.MaxPlateOpacity, white.PlateOpacityOver(0.9));
    }

    [Fact]
    public void UnderPlate_at_the_default_opacity_is_the_two_argument_form()
    {
        Assert.Equal(DeskDesign.UnderPlate(0.79, "0a0a0a"), DeskDesign.UnderPlate(0.79, "0a0a0a", DeskPlate.Opacity));
        Assert.True(DeskDesign.UnderPlate(0.79, "0a0a0a", 0.6) < DeskDesign.UnderPlate(0.79, "0a0a0a", 0.3));
    }
}
