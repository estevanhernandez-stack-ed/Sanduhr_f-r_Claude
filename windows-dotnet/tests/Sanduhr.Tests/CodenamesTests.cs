using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: the codename pool and how names are assigned, kept, shuffled and allowlisted (spec, Streamer mode, Codenames).</summary>
public class CodenamesTests
{
    private static byte[] Salt(byte fill) => Enumerable.Repeat(fill, Codenames.SaltBytes).ToArray();

    [Fact]
    public void The_pool_is_fourteen_by_fourteen_and_none_is_a_known_product()
    {
        Assert.Equal(14, Codenames.FirstWords.Count);
        Assert.Equal(14, Codenames.SecondWords.Count);
        Assert.Equal(196, Codenames.Pool.Count);
        Assert.Equal(196, Codenames.Pool.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Dune Fox", Codenames.Pool);
        Assert.Contains("Quartz Heron", Codenames.Pool);
        Assert.Contains("Saffron Moth", Codenames.Pool);
        Assert.NotEmpty(Codenames.DenyPairs);
        static string Squash(string s) => new string(s.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        var deny = Codenames.DenyPairs.Select(Squash).ToHashSet();
        Assert.Empty(Codenames.Pool.Where(p => deny.Contains(Squash(p))));
        // "Desert Eagle" can't occur: neither word is in the pool (spec).
        Assert.DoesNotContain("Desert", Codenames.FirstWords);
        Assert.DoesNotContain("Eagle", Codenames.SecondWords);
    }

    [Fact]
    public void A_name_keeps_its_codename_and_a_second_book_with_the_same_salt_agrees()
    {
        var a = new CodenameBook(Salt(7));
        var first = a.Assign(CodenameCategory.Account, "Work");
        Assert.Equal(first, a.Assign(CodenameCategory.Account, "Work"));
        Assert.True(Codenames.IsCodename(first));
        Assert.Equal(first, new CodenameBook(Salt(7)).Assign(CodenameCategory.Account, "Work"));
    }

    [Fact]
    public void Another_salt_gives_another_codename_for_most_names()
    {
        var names = Enumerable.Range(0, 20).Select(i => "project-" + i).ToList();
        var a = new CodenameBook(Salt(1));
        var b = new CodenameBook(Salt(2));
        int same = names.Count(n => a.Assign(CodenameCategory.Project, n) == b.Assign(CodenameCategory.Project, n));
        Assert.True(same < 5, $"{same} of 20 names kept their codename across salts");
    }

    [Fact]
    public void No_codename_is_shared_across_categories()
    {
        var book = new CodenameBook(Salt(3));
        var all = new List<string>();
        foreach (var c in Enum.GetValues<CodenameCategory>())
            for (int i = 0; i < 10; i++)
                all.Add(book.Assign(c, "same-name-" + i));
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_real_name_that_looks_like_a_codename_still_gets_another()
    {
        // Preflight P9: every pool name (and its lower-case spelling) as a real name, one salt.
        var book = new CodenameBook(Salt(4));
        foreach (var name in Codenames.Pool)
        {
            Assert.NotEqual(name, book.Assign(CodenameCategory.Account, name), StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(name, book.Assign(CodenameCategory.Repo, name.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Past_196_names_gain_a_numeral()
    {
        var book = new CodenameBook(Salt(5));
        var names = Enumerable.Range(0, 200).Select(i => book.Assign(CodenameCategory.Project, "p" + i)).ToList();
        Assert.Equal(200, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(196, names.Take(196).Count(n => Codenames.Pool.Contains(n)));
        Assert.All(names.Skip(196), n => Assert.Matches(@"^[A-Z][a-z]+ [A-Z][a-z]+ 2$", n));
        Assert.All(names, n => Assert.True(Codenames.IsCodename(n), n));
    }

    [Fact]
    public void Shuffle_changes_every_codename_and_keeps_the_allowlist()
    {
        var book = new CodenameBook(Salt(6));
        var before = Enumerable.Range(0, 12).Select(i => book.Assign(CodenameCategory.Folder, "f" + i)).ToList();
        book.SetShown(CodenameCategory.Account, "Personal", true);
        book.Shuffle(Salt(9));
        Assert.Equal(0, book.Count);
        Assert.False(book.TryGet(CodenameCategory.Folder, "f0", out _));
        var after = Enumerable.Range(0, 12).Select(i => book.Assign(CodenameCategory.Folder, "f" + i)).ToList();
        Assert.True(before.Zip(after).Count(p => p.First == p.Second) < 4);
        Assert.True(book.IsShown(CodenameCategory.Account, "Personal"));
        Assert.Equal(Salt(9), book.Salt);
    }

    [Fact]
    public void Sanduhrs_own_add_ons_are_shown_by_default_and_nothing_else_is()
    {
        var book = new CodenameBook(Salt(8));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "sanduhr"));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "inline"));
        Assert.False(book.IsShown(CodenameCategory.AddOn, "someone-elses-plugin"));
        Assert.False(book.IsShown(CodenameCategory.Account, "Personal"));
        Assert.False(new CodenameBook(Salt(8), withDefaults: false).IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
    }

    [Fact]
    public void Entries_list_mapped_and_shown_names_by_category()
    {
        var book = new CodenameBook(Salt(10), withDefaults: false);
        var code = book.Assign(CodenameCategory.Project, "alpha");
        book.SetShown(CodenameCategory.Account, "Personal", true);
        Assert.Equal(
            [new CodenameEntry(CodenameCategory.Account, "Personal", null, true), new CodenameEntry(CodenameCategory.Project, "alpha", code, false)],
            book.Entries());
    }

    [Fact]
    public void A_book_round_trips_through_its_file()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "Sanduhr", Codenames.FileName);
        var book = new CodenameBook(Salt(11));
        var work = book.Assign(CodenameCategory.Account, "Work");
        var folder = book.Assign(CodenameCategory.Folder, @"c:\users\x\.claude-work");
        book.SetShown(CodenameCategory.AddOn, "sanduhr", false);
        Assert.True(Codenames.Save(book, path));
        Assert.False(File.Exists(path + ".tmp"));

        var back = Codenames.Load(path)!;
        Assert.Equal(book.Salt, back.Salt);
        Assert.True(back.TryGet(CodenameCategory.Account, "Work", out var w) && w == work);
        Assert.True(back.TryGet(CodenameCategory.Folder, @"c:\users\x\.claude-work", out var f) && f == folder);
        Assert.False(back.IsShown(CodenameCategory.AddOn, "sanduhr"));     // the file's allowlist wins over the defaults
        Assert.True(back.IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
        // A new name never collides with a loaded one.
        Assert.DoesNotContain(back.Assign(CodenameCategory.Project, "new"), new[] { work, folder });
    }

    [Fact]
    public void A_missing_file_loads_as_nothing()
    {
        using var tmp = new TempDir();
        Assert.Null(Codenames.Load(Path.Combine(tmp.Path, Codenames.FileName)));
    }

    /// <summary>Review Focus 4: a hand-edited or broken file is cleaned or replaced, never trusted.</summary>
    [Fact]
    public void A_tampered_book_is_cleaned_or_replaced_never_trusted()
    {
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("""{"schema_version":1,"salt":"c2hvcnQ=","maps":{}}""")));    // salt too short
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("""{"schema_version":2,"salt":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","maps":{}}""")));
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("[]")));
        var salt = Convert.ToBase64String(Salt(12));
        var book = CodenameBook.FromJson(JsonNode.Parse($$$"""
            {"schema_version":1,"salt":"{{{salt}}}",
             "maps":{"account":{"Work":"Work","Home":"Dune Fox","Side":"Dune Fox","Odd":"Mesa Owl 3"},"nonsense":{"x":"Amber Wren"}},
             "shown":{"project":["kept"],"nonsense":["y"]}}
            """))!;
        Assert.False(book.TryGet(CodenameCategory.Account, "Work", out _));     // a real name as its own codename is dropped
        Assert.True(book.TryGet(CodenameCategory.Account, "Home", out var home) && home == "Dune Fox");
        Assert.False(book.TryGet(CodenameCategory.Account, "Side", out _));     // a second holder of one codename is dropped
        Assert.True(book.TryGet(CodenameCategory.Account, "Odd", out var odd) && odd == "Mesa Owl 3");
        Assert.True(book.IsShown(CodenameCategory.Project, "kept"));
        Assert.Equal(2, book.Count);
    }

    [Fact]
    public void A_file_too_large_or_not_json_loads_as_nothing()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, Codenames.FileName);
        File.WriteAllText(path, "{ not json");
        Assert.Null(Codenames.Load(path));
        File.WriteAllText(path, new string(' ', Codenames.MaxFileBytes + 1));
        Assert.Null(Codenames.Load(path));
    }

    [Theory]
    [InlineData("Dune Fox", true)]
    [InlineData("Zenith Plover 12", true)]
    [InlineData("Dune Fox 1", false)]
    [InlineData("dune fox", false)]
    [InlineData("Desert Eagle", false)]
    [InlineData("", false)]
    public void Only_pool_names_and_their_numbered_forms_are_codenames(string text, bool expected) =>
        Assert.Equal(expected, Codenames.IsCodename(text));

    [Fact]
    public void Category_ids_round_trip()
    {
        foreach (var c in Enum.GetValues<CodenameCategory>())
            Assert.True(Codenames.TryParseId(Codenames.Id(c), out var back) && back == c);
        Assert.Equal("addon", Codenames.Id(CodenameCategory.AddOn));
        Assert.False(Codenames.TryParseId("nonsense", out _));
    }
}
