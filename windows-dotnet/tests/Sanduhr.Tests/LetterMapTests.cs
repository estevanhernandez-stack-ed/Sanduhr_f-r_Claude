using System.Globalization;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's MessageStyleTests.swift: the <c>{font:...}</c> tags and the Unicode letter
/// styles. Skipped: <c>MessageTypographyTests</c> (the Mac's text-layout planner, not ported) and
/// <c>notchShowsTheLineWithoutTags</c> (the notch). The sweep timing cases are in MessageEffectsTests.
/// </summary>
public class LetterMapTests
{
    [Fact]
    public void FontTags_TakeTheStatuslineNames()
    {
        foreach (var style in LetterStyles.All)
        {
            var r = MessageMarkup.ParseStrict($"{{font:{LetterStyles.Word(style)}}} hi");
            Assert.True(r.IsSuccess);
            Assert.Equal(style, r.Value!.Effects.Font);
            Assert.Equal("hi", r.Value.Text);
        }
        // Case, spaces, hyphens and underscores don't matter.
        Assert.Equal(LetterStyle.SmallCaps, LetterStyles.Parse("smallcaps"));
        Assert.Equal(LetterStyle.SmallCaps, LetterStyles.Parse("Small Caps"));
        Assert.Equal(LetterStyle.BoldItalic, LetterStyles.Parse("bold_italic"));
        Assert.Equal(LetterStyle.DoubleStruck, LetterStyles.Parse("DOUBLESTRUCK"));
        Assert.Null(LetterStyles.Parse("outline"));
        Assert.Equal("bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps", LetterStyles.TagNames);
    }

    [Fact]
    public void Sweep_OnceOrOnAPeriod()
    {
        var once = MessageMarkup.ParseStrict("{sweep} {font:smallcaps} {ink:#ff2a6d} hi.").Value!;
        Assert.True(once.Effects.Sweep);
        Assert.Null(once.Effects.SweepPeriod);
        Assert.Equal(LetterStyle.SmallCaps, once.Effects.Font);
        Assert.Equal("hi.", once.Text);
        var every = MessageMarkup.ParseStrict("{SWEEP:20} hi").Value!;
        Assert.True(every.Effects.Sweep);
        Assert.Equal(20, every.Effects.SweepPeriod);
        Assert.Equal(2, MessageMarkup.ParseStrict("{sweep:2} a").Value!.Effects.SweepPeriod);
        Assert.Equal(3600, MessageMarkup.ParseStrict("{sweep:3600} a").Value!.Effects.SweepPeriod);
    }

    [Fact]
    public void BadFontAndSweepTags_AreNamed()
    {
        (string Line, MessageFailure Want)[] cases =
        [
            ("{font} hi", MessageFailure.Font),
            ("{font:} hi", MessageFailure.Font),
            ("{font:outline} hi", MessageFailure.Font),
            ("{font:comic} hi", MessageFailure.Font),
            ("{sweep:} hi", MessageFailure.Sweep),
            ("{sweep:1} hi", MessageFailure.Sweep),
            ("{sweep:3601} hi", MessageFailure.Sweep),
            ("{sweep:fast} hi", MessageFailure.Sweep),
            ("{sweep:-5} hi", MessageFailure.Sweep),
        ];
        foreach (var (line, want) in cases)
        {
            var r = MessageMarkup.ParseStrict(line);
            Assert.Equal(want, r.Failure);
        }
        Assert.Contains("small-caps", MessageFailure.Font.Reason);
        Assert.Contains("2 to 3600", MessageFailure.Sweep.Reason);
        Assert.EndsWith("shimmer, sweep, font", MessageFailure.Unknown("x").Reason);
    }

    /// <summary>Unknown or malformed tags draw as written, as before.</summary>
    [Fact]
    public void Lenient_DrawsBadStyleTagsAsText()
    {
        var p = MessageMarkup.Parse("{sweep} {font:comic} hi");
        Assert.True(p.Effects.Sweep);
        Assert.Null(p.Effects.Font);
        Assert.Equal("{font:comic} hi", p.Text);
        Assert.Equal("{sweep:0.5} hi", MessageMarkup.Parse("{sweep:0.5} hi").Text);
        Assert.Equal("{font:bold}", MessageMarkup.Parse("{font:bold}").Text);
    }

    [Fact]
    public void LettersAndDigits()
    {
        Assert.Equal("𝐀𝐛 𝟎𝟗", LetterMap.Map("Ab 09", LetterStyle.Bold));
        Assert.Equal("𝖠𝖻 𝟢𝟫", LetterMap.Map("Ab 09", LetterStyle.Sans));
        Assert.Equal("𝙰𝚋 𝟶𝟿", LetterMap.Map("Ab 09", LetterStyle.Mono));
        Assert.Equal("𝔸𝕓 𝟘𝟡", LetterMap.Map("Ab 09", LetterStyle.DoubleStruck));
        // No digits in the block for these: they stay.
        Assert.Equal("𝐴𝑧 09", LetterMap.Map("Az 09", LetterStyle.Italic));
        Assert.Equal("𝑨𝒛 09", LetterMap.Map("Az 09", LetterStyle.BoldItalic));
        Assert.Equal("𝒜𝓏 09", LetterMap.Map("Az 09", LetterStyle.Script));
        Assert.Equal("𝔄𝔷 09", LetterMap.Map("Az 09", LetterStyle.Fraktur));
    }

    /// <summary>The letters Mathematical Alphanumerics leaves out come from Letterlike Symbols.</summary>
    [Fact]
    public void LetterlikeHoles_AreFilled()
    {
        Assert.Equal("ℎ", LetterMap.Map("h", LetterStyle.Italic));
        Assert.Equal("ℬℰℱℋℐℒℳℛ ℯℊℴ", LetterMap.Map("BEFHILMR ego", LetterStyle.Script));
        Assert.Equal("ℭℌℑℜℨ", LetterMap.Map("CHIRZ", LetterStyle.Fraktur));
        Assert.Equal("ℂℍℕℙℚℝℤ", LetterMap.Map("CHNPQRZ", LetterStyle.DoubleStruck));
        // Every mapped character is one assigned scalar: no reserved code point in a hole.
        foreach (var style in LetterStyles.All)
        {
            foreach (char ch in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
            {
                var outRune = LetterMap.Letter(new Rune(ch), style);
                Assert.NotEqual(UnicodeCategory.OtherNotAssigned, Rune.GetUnicodeCategory(outRune));
            }
        }
    }

    [Fact]
    public void SmallCaps_AndWhatStays()
    {
        Assert.Equal("Hᴇʟʟᴏ, x 42", LetterMap.Map("Hello, x 42", LetterStyle.SmallCaps));
        Assert.Equal("ü𝐧ï𝐜𝐨𝐝𝐞 ✨ – é", LetterMap.Map("ünïcode ✨ – é", LetterStyle.Bold));
        Assert.Equal("as written", LetterMap.Map("as written", null));
    }

    /// <summary>Windows-specific: the table holds 26 BMP letters, and results outside the BMP are whole scalars in the string.</summary>
    [Fact]
    public void SmallCapsTable_Is26SingleUnits_AndAstralResultsAreSurrogatePairs()
    {
        Assert.Equal(26, LetterMap.SmallCaps.Length);
        Assert.All(LetterMap.SmallCaps, c => Assert.False(char.IsSurrogate(c)));
        string bold = LetterMap.Map("a", LetterStyle.Bold);
        Assert.Equal(2, bold.Length);
        Assert.Equal(0x1D41A, char.ConvertToUtf32(bold, 0));
        // A lone surrogate in the input passes through untouched.
        string lone = "a\uD800b";
        Assert.Equal(LetterMap.Map("a", LetterStyle.Bold) + "\uD800" + LetterMap.Map("b", LetterStyle.Bold), LetterMap.Map(lone, LetterStyle.Bold));
    }
}
