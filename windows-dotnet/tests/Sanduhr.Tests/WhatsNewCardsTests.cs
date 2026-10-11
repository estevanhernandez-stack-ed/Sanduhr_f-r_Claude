using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Pins the Windows card table's house rules: unique ids, short plain bodies, real destinations.</summary>
public class WhatsNewCardsTests
{
    private static IReadOnlyList<WhatsNewCard> Table => WhatsNewCards.Table;

    [Fact]
    public void Table_HasTheSixCardsInOrder()
    {
        Assert.Equal(
            ["shortcut-keys", "tray-shows", "limits-come-and-go", "every-folder", "steady-sparklines", "whats-new"],
            Table.Select(c => c.Id));
    }

    [Fact]
    public void Ids_AreUnique() => Assert.Equal(Table.Count, Table.Select(c => c.Id).Distinct().Count());

    [Fact]
    public void Titles_AreNotEmpty() => Assert.All(Table, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));

    [Fact]
    public void Bodies_AreShortPlainSentences()
    {
        foreach (var c in Table)
        {
            Assert.True(c.Body.Length <= 200, $"{c.Id} is {c.Body.Length} chars");
            Assert.EndsWith(".", c.Body);
            Assert.DoesNotContain('—', c.Body);
            var sentences = System.Text.RegularExpressions.Regex.Matches(c.Body, @"\. ").Count + 1;
            Assert.True(sentences <= 2, $"{c.Id} has {sentences} sentences");
        }
    }

    [Fact]
    public void Versions_AreThreePartNumeric()
    {
        foreach (var c in Table)
        {
            Assert.NotEmpty(c.Versions);
            foreach (var v in c.Versions)
            {
                var parts = v.Split('.');
                Assert.Equal(3, parts.Length);
                Assert.All(parts, p => Assert.True(int.TryParse(p, out _), $"{c.Id}: {v}"));
                Assert.Equal(0, WhatsNew.Compare(v, v));
            }
        }
    }

    [Fact]
    public void Destinations_AreDefinedPages() =>
        Assert.All(Table, c => Assert.True(Enum.IsDefined(c.Destination), c.Id));

    [Fact]
    public void Glyphs_AreNotEmpty() => Assert.All(Table, c => Assert.False(string.IsNullOrEmpty(c.Glyph)));
}
