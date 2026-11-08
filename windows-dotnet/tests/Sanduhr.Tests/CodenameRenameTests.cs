using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Rename… on Show the real name (dashboard task "Streamer mode: let people name their own codenames", 2026-10-27): a picked codename is
/// unique across every category, never a real name, kept through Shuffle and saved under <c>chosen</c> in codenames.json.
/// </summary>
public class CodenameRenameTests
{
    private static byte[] Salt(byte fill) => Enumerable.Repeat(fill, Codenames.SaltBytes).ToArray();

    private static CodenameBook Book() => new(Salt(21), withDefaults: false);

    [Fact]
    public void A_picked_codename_replaces_the_drawn_one_and_is_marked_chosen()
    {
        var book = Book();
        var drawn = book.Assign(CodenameCategory.Account, "Work");

        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "  Night Shift  "));

        Assert.Equal("Night Shift", book.Assign(CodenameCategory.Account, "Work"));
        Assert.True(book.IsChosen(CodenameCategory.Account, "Work"));
        Assert.Equal(1, book.Count);   // the drawn name went back to the pool
        Assert.Contains(new CodenameEntry(CodenameCategory.Account, "Work", "Night Shift", false, true), book.Entries());
        // The drawn name is free again: another name can draw it.
        Assert.DoesNotContain(drawn, book.Entries().Select(e => e.Codename));
    }

    [Fact]
    public void A_name_that_never_had_a_codename_can_be_given_one()
    {
        var book = Book();
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Repo, "secret-repo", "Lantern"));
        Assert.True(book.TryGet(CodenameCategory.Repo, "secret-repo", out var code) && code == "Lantern");
    }

    [Fact]
    public void A_picked_codename_is_unique_across_every_category_ignoring_case()
    {
        var book = Book();
        var drawn = book.Assign(CodenameCategory.Project, "alpha");
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "Lantern"));

        Assert.Equal(CodenameRename.Taken, book.Rename(CodenameCategory.Folder, @"c:\x\.claude", "lantern"));
        Assert.Equal(CodenameRename.Taken, book.Rename(CodenameCategory.Account, "Personal", drawn.ToUpperInvariant()));
        // Its own codename, or a new spelling of it, is not a clash.
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "LANTERN"));
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Project, "alpha", drawn));
        Assert.False(book.IsChosen(CodenameCategory.Folder, @"c:\x\.claude"));
    }

    [Fact]
    public void The_pool_never_draws_a_picked_codename()
    {
        var book = Book();
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "Dune Fox"));
        var all = Enumerable.Range(0, 200).Select(i => book.Assign(CodenameCategory.Project, "p" + i)).ToList();
        Assert.DoesNotContain("Dune Fox", all);
    }

    [Theory]
    [InlineData(CodenameCategory.Account, "Work", "work")]                                // its own real name
    [InlineData(CodenameCategory.Account, "Work", "Personal")]                            // another account's
    [InlineData(CodenameCategory.Account, "Work", "qxrepo")]                              // a name in another category
    [InlineData(CodenameCategory.Account, "Work", ".claude-qxwork")]                      // a folder's leaf
    [InlineData(CodenameCategory.Account, "Work", "claude-qxwork")]                       // the leaf without its dot, as the statusline shows it
    [InlineData(CodenameCategory.Account, "Work", "qx.person")]                           // an email's name before the @
    [InlineData(CodenameCategory.Account, "Work", "Shown Only")]                          // a name shown as it is, never masked
    public void A_real_name_is_never_accepted(CodenameCategory category, string real, string name)
    {
        var book = Book();
        book.Assign(CodenameCategory.Account, "Work");
        book.Assign(CodenameCategory.Account, "Personal");
        book.Assign(CodenameCategory.Repo, "QxRepo");
        book.Assign(CodenameCategory.Folder, @"c:\users\qx\.claude-qxwork");
        book.Assign(CodenameCategory.Email, "qx.person@example.test");
        book.SetShown(CodenameCategory.Account, "Shown Only", true);
        var before = book.Assign(category, real);

        Assert.Equal(CodenameRename.RealName, book.Rename(category, real, name));
        Assert.True(book.TryGet(category, real, out var still) && still == before);
        Assert.False(book.IsChosen(category, real));
    }

    [Fact]
    public void The_rows_own_real_name_is_refused_before_it_has_a_codename()
    {
        var book = Book();
        Assert.Equal(CodenameRename.RealName, book.Rename(CodenameCategory.Host, "QXPC", "qxpc"));
        Assert.False(book.TryGet(CodenameCategory.Host, "QXPC", out _));
    }

    [Theory]
    [InlineData("a\tb", CodenameRename.BadCharacter)]
    [InlineData("a\nb", CodenameRename.BadCharacter)]
    [InlineData("ab\u200Dcd", CodenameRename.BadCharacter)]        // zero-width joiner hides text
    [InlineData("\u202Exob", CodenameRename.BadCharacter)]          // a bidi override reorders the line
    [InlineData("a\u00A0b", CodenameRename.BadCharacter)]           // a space that isn't one
    [InlineData("a\uE000", CodenameRename.BadCharacter)]            // private use (a lone surrogate has its own test: theory data can't carry one)
    [InlineData("0123456789012345678901234567890123", CodenameRename.TooLong)]   // 34 characters, trimmed or not
    [InlineData("01234567890123456789012345678901", CodenameRename.Ok)]        // exactly 32
    [InlineData("Café Nocturne", CodenameRename.Ok)]
    [InlineData("Ночная сова", CodenameRename.Ok)]
    [InlineData("Night Owl \U0001F989", CodenameRename.Ok)]               // an emoji is a surrogate pair, fine
    [InlineData("R2-D2's spare", CodenameRename.Ok)]
    public void The_shape_rules(string name, CodenameRename expected)
    {
        Assert.Equal(expected, Book().Rename(CodenameCategory.Project, "alpha", name));
    }

    [Fact]
    public void A_lone_surrogate_is_refused()
    {
        Assert.Equal(CodenameRename.BadCharacter, Book().Rename(CodenameCategory.Project, "alpha", "a" + (char)0xD800 + "b"));
    }

    [Fact]
    public void A_refusal_changes_nothing()
    {
        var book = Book();
        var drawn = book.Assign(CodenameCategory.Project, "alpha");
        Assert.Equal(CodenameRename.TooLong, book.Rename(CodenameCategory.Project, "alpha", new string('x', 40)));
        Assert.True(book.TryGet(CodenameCategory.Project, "alpha", out var code) && code == drawn);
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void Blank_gives_the_name_back_to_the_pool()
    {
        var book = Book();
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "Lantern"));
        Assert.Equal(CodenameRename.Cleared, book.Rename(CodenameCategory.Account, "Work", "   "));

        Assert.False(book.TryGet(CodenameCategory.Account, "Work", out _));
        Assert.False(book.IsChosen(CodenameCategory.Account, "Work"));
        Assert.Equal(0, book.Count);
        var drawn = book.Assign(CodenameCategory.Account, "Work");
        Assert.True(Codenames.IsCodename(drawn));
        // Lantern is free for someone else now.
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Repo, "r", "Lantern"));
    }

    [Fact]
    public void Shuffle_keeps_picked_codenames_and_redraws_the_rest()
    {
        var book = Book();
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "Lantern"));
        book.Assign(CodenameCategory.Folder, "f0");

        book.Shuffle(Salt(22));

        Assert.True(book.TryGet(CodenameCategory.Account, "Work", out var kept) && kept == "Lantern");
        Assert.True(book.IsChosen(CodenameCategory.Account, "Work"));
        Assert.False(book.TryGet(CodenameCategory.Folder, "f0", out _));
        Assert.Equal(1, book.Count);
        Assert.Equal(CodenameRename.Taken, book.Rename(CodenameCategory.Repo, "r", "Lantern"));
    }

    [Fact]
    public void A_picked_codename_is_saved_under_chosen_and_round_trips()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "Sanduhr", Codenames.FileName);
        var book = Book();
        var drawn = book.Assign(CodenameCategory.Project, "alpha");
        Assert.Equal(CodenameRename.Ok, book.Rename(CodenameCategory.Account, "Work", "Lantern"));
        Assert.True(Codenames.Save(book, path));

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("Lantern", (string?)json["chosen"]!["account"]!["Work"]);
        Assert.Null(json["maps"]!["account"]);   // a build before Rename… never sees it in maps, where it would be dropped
        Assert.Equal(drawn, (string?)json["maps"]!["project"]!["alpha"]);

        var back = Codenames.Load(path)!;
        Assert.True(back.TryGet(CodenameCategory.Account, "Work", out var w) && w == "Lantern");
        Assert.True(back.IsChosen(CodenameCategory.Account, "Work"));
        Assert.True(back.TryGet(CodenameCategory.Project, "alpha", out var a) && a == drawn);
        Assert.False(back.IsChosen(CodenameCategory.Project, "alpha"));
    }

    private static JsonObject File1(JsonObject? maps = null, JsonObject? chosen = null, JsonObject? shown = null) => new()
    {
        ["schema_version"] = 1,
        ["salt"] = Convert.ToBase64String(Salt(23)),
        ["maps"] = maps ?? new JsonObject(),
        ["chosen"] = chosen,
        ["shown"] = shown ?? new JsonObject(),
    };

    [Fact]
    public void A_file_without_chosen_loads_as_before()
    {
        var root = File1(new JsonObject { ["account"] = new JsonObject { ["Work"] = "Dune Fox" } });
        root.Remove("chosen");
        var book = CodenameBook.FromJson(root)!;
        Assert.True(book.TryGet(CodenameCategory.Account, "Work", out var c) && c == "Dune Fox");
        Assert.False(book.IsChosen(CodenameCategory.Account, "Work"));
    }

    [Fact]
    public void A_hand_edited_file_cant_smuggle_a_real_name_or_a_bad_character_in_as_a_codename()
    {
        var book = CodenameBook.FromJson(File1(
            maps: new JsonObject { ["repo"] = new JsonObject { ["QxRepo"] = "Dune Fox" } },
            chosen: new JsonObject
            {
                ["account"] = new JsonObject { ["Work"] = "qxrepo", ["Personal"] = "Lan\u202Etern", ["Spare"] = "Fine Name" },
                ["nonsense"] = new JsonObject { ["x"] = "Whatever" },
            }))!;

        Assert.False(book.TryGet(CodenameCategory.Account, "Work", out _));
        Assert.False(book.TryGet(CodenameCategory.Account, "Personal", out _));
        Assert.True(book.TryGet(CodenameCategory.Account, "Spare", out var spare) && spare == "Fine Name");
        Assert.True(book.TryGet(CodenameCategory.Repo, "QxRepo", out var repo) && repo == "Dune Fox");
    }

    [Fact]
    public void A_picked_codename_wins_a_clash_with_a_drawn_one_in_the_file()
    {
        var book = CodenameBook.FromJson(File1(
            maps: new JsonObject { ["project"] = new JsonObject { ["alpha"] = "Dune Fox" } },
            chosen: new JsonObject { ["account"] = new JsonObject { ["Work"] = "Dune Fox" } }))!;

        Assert.True(book.TryGet(CodenameCategory.Account, "Work", out var w) && w == "Dune Fox");
        Assert.False(book.TryGet(CodenameCategory.Project, "alpha", out _));   // it draws another on next use
        Assert.NotEqual("Dune Fox", book.Assign(CodenameCategory.Project, "alpha"));
    }

    [Fact]
    public void Every_refusal_has_its_own_sentence_and_ok_has_none()
    {
        var refusals = new[] { CodenameRename.TooLong, CodenameRename.BadCharacter, CodenameRename.Taken, CodenameRename.RealName };
        var lines = refusals.Select(SettingsNames.RenameRefused).ToList();
        Assert.All(lines, l => Assert.Matches(@"\.$", l));
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.Equal("", SettingsNames.RenameRefused(CodenameRename.Ok));
        Assert.Equal("", SettingsNames.RenameRefused(CodenameRename.Cleared));
        Assert.Equal("Rename the codename, Dune Fox", SettingsNames.RenameCodenameName("Dune Fox"));
        foreach (var s in lines.Append(SettingsNames.RenameCaption).Append(SettingsNames.ShuffleCaption).Append(SettingsNames.ShuffleDetail))
        {
            Assert.Empty(SettingsNames.PathProblems(s));
            Assert.Empty(SettingsNames.RetiredNames(s));
            Assert.DoesNotContain("—", s);
            Assert.DoesNotContain("  ", s);
        }
    }
}
