using System.Windows.Media;
using Microsoft.Win32;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The six looks that ship inside the app (5.0 spec 5, sections 3 and 10): every file loads, keeps the rules, and fits every
/// kind of wallpaper into something the validator passes. The positions and sizes are not pinned: they are tuned by eye.</summary>
public sealed class DeskLookFilesTests
{
    private static DeskLookCatalog Catalog => DeskLookFiles.Read();

    /// <summary>A one-monitor wallpaper whose every anchor has this brightness, but for the ones overridden.</summary>
    private static WallpaperOverview Flat(double brightness, Dictionary<DeskAnchor, double>? overrides = null)
    {
        var readings = DeskAnchors.All
            .Select(a => new AnchorReading(a, overrides is not null && overrides.TryGetValue(a, out var b) ? b : brightness, 0.2))
            .ToList();
        var summary = new WallpaperSummary([new DominantColor("#808080", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private static IEnumerable<(string Name, WallpaperOverview Wall)> Wallpapers()
    {
        yield return ("dark", Flat(0.03));
        yield return ("light", Flat(0.95));
        yield return ("mid-grey", Flat(0.22));
        yield return ("busy", Flat(0.3, new()
        {
            [DeskAnchor.TL] = 0.9, [DeskAnchor.TC] = 0.05, [DeskAnchor.TR] = 0.6, [DeskAnchor.ML] = 0.15,
            [DeskAnchor.MR] = 0.8, [DeskAnchor.BL] = 0.5, [DeskAnchor.BC] = 0.95, [DeskAnchor.BR] = 0.1,
        }));
        var dark = Flat(0.03).Monitors[0];
        var bright = Flat(0.95).Monitors[0] with { Index = 1, Primary = false };
        yield return ("two monitors, opposite", new WallpaperOverview([], 0.5, [dark, bright]));
    }

    private static DeskDesign UsersDesk() => DeskState.Current(DeskSettings.Default, DeskInkSettings.Default,
        new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]), null);

    [Fact]
    public void All_six_load_in_the_gallerys_order_and_none_is_skipped()
    {
        Assert.Empty(Catalog.Skipped);
        Assert.Equal(DeskLookCatalog.Order, Catalog.Looks.Select(l => l.Id));
    }

    [Fact]
    public void Each_passes_the_Desks_own_rules_as_written()
    {
        foreach (var look in Catalog.Looks)
        {
            var verdict = DeskDesign.Validate(look.Design, DeskDesignContext.Unknown);
            Assert.True(verdict.Ok, $"{look.Id}: {string.Join("; ", verdict.Reasons)}");
            Assert.Empty(DeskLook.Rules(look.Design));
        }
    }

    [Fact]
    public void Each_fits_every_wallpaper_into_a_design_the_validator_passes()
    {
        foreach (var look in Catalog.Looks)
            foreach (var (name, wall) in Wallpapers())
            {
                var ctx = new DeskDesignContext(DeskLook.WindowsFonts.ToList(), wall, null, UsersDesk());
                var fit = DeskLookFit.Fit(look, ctx);
                var verdict = DeskDesign.Validate(fit.Design, ctx);
                Assert.True(verdict.Ok, $"{look.Id} on {name}: {string.Join("; ", verdict.Reasons)}");
                Assert.True(fit.Notes.Count <= 2, $"{look.Id} on {name}: {fit.Notes.Count} notes");
            }
    }

    [Fact]
    public void Each_still_applies_on_a_PC_with_no_fonts_of_its_own()
    {
        foreach (var look in Catalog.Looks)
        {
            var ctx = new DeskDesignContext(new[] { "Wingdings" }, Flat(0.03), null, UsersDesk());
            Assert.Equal(DeskDesignStatus.Ok, DeskDesign.Validate(DeskLookFit.Fit(look, ctx).Design, ctx).Status);
        }
    }

    [Fact]
    public void The_glow_belongs_to_Marquee_and_the_shadow_to_the_other_five()
    {
        foreach (var look in Catalog.Looks)
        {
            bool glows = look.Design.Glow is { On: true };
            Assert.Equal(look.Id == "marquee", glows);
            Assert.Equal(!glows, look.Design.Shadow);
        }
        Assert.Equal("#ffd08a", Catalog.Find("marquee")!.Design.Ink!.Colors.Single());
    }

    [Fact]
    public void Names_and_blurbs_are_the_gallerys_copy()
    {
        Assert.Equal(["Quiet corner", "Ice", "Marquee", "Terminal", "Editorial", "Dashboard"], Catalog.Looks.Select(l => l.Name));
        Assert.Equal("Clock up top, neon glow.", Catalog.Find("marquee")!.Blurb);
        Assert.All(Catalog.Looks, l => Assert.EndsWith(".", l.Blurb));
    }

    [Fact]
    public void Each_shows_a_clock_and_no_two_are_the_same_arrangement()
    {
        Assert.All(Catalog.Looks, l => Assert.True(l.Shows("clock"), l.Id));
        var arrangements = Catalog.Looks.Select(l => DeskDesignApply.Layout(l.Design.Layout!)).ToList();
        Assert.Equal(arrangements.Count, arrangements.Distinct().Count());
    }

    [Fact]
    public void The_cached_catalog_is_the_one_read()
    {
        Assert.Same(DeskLookFiles.Catalog, DeskLookFiles.Catalog);
        Assert.Equal(6, DeskLookFiles.Catalog.Looks.Count);
    }

    [Fact]
    public void Each_look_applied_and_read_back_is_recognised_as_itself()
    {
        var catalog = Catalog; // read once: Assert.Same compares instances, and each read builds new ones
        var fonts = DeskLook.WindowsFonts.ToList();
        var launcher = new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]);
        foreach (var look in catalog.Looks)
            foreach (var (name, wall) in Wallpapers())
            {
                var desk = DeskSettings.Default;
                var ink = DeskInkSettings.Default;
                var before = DeskState.Current(desk, ink, launcher, null);
                var fit = DeskLookFit.Fit(look, new DeskDesignContext(fonts, wall, null, before));

                // The way the App writes a look: the fit merged, the layout in the file's order, through the settings a design writes.
                var merged = DeskDesign.Merge(before, fit.Design) with { Layout = [.. DeskLookFit.LayoutOver(look, before)] };
                var next = DeskDesignApply.Settings(desk, ink, launcher, merged);
                var now = DeskState.Current(next.Desk, next.Ink, next.Launcher, null, followsTheme: next.FollowsTheme ?? false);

                Assert.True(DeskLookFit.IsExactly(look, fit, now), $"{look.Id} on {name}: not exactly its own fit after applying");
                Assert.Same(look, DeskLookFit.Untouched(catalog.Looks, now, fonts));
            }
    }

    [SkippableFact]
    public void Every_font_a_look_names_is_a_family_this_Windows_lists()
    {
        // The spec's check: each font name against the installed-font list the validator reads (WPF's families). Client Windows
        // only: a server image (the CI runner) is not a Windows the package supports and lacks some of these.
        string? kind = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", null) as string;
        Skip.IfNot(kind == "Client", "not a client Windows");
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var look in Catalog.Looks)
        {
            string font = look.Design.Font!.Family;
            Assert.True(BundledFonts.Bundled(font) is not null || installed.Contains(font), $"{look.Id}: {font} is not a family here");
            Assert.True(BundledFonts.Bundled(look.FontFallback) is not null || installed.Contains(look.FontFallback), $"{look.Id}: fallback {look.FontFallback} is not a family here");
        }
    }
}
